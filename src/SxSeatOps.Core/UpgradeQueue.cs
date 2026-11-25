namespace SxSeatOps.Core;

/// <summary>One request for a cabin upgrade.</summary>
public sealed record UpgradeRequest(string Pnr,CabinClass TargetCabin,DateTimeOffset ReceivedAt);
/// <summary>Upgrade decision kind.</summary>
public enum UpgradeDecisionKind:byte{Granted=0,Rejected=1,Deferred=2}
/// <summary>Outcome of processing one upgrade request.</summary>
public sealed record UpgradeDecision(UpgradeRequest Request,UpgradeDecisionKind Kind,string? Reason,decimal? Fee,SeatId? AssignedSeat);
/// <summary>Priority queue partitioned by loyalty tier and FIFO within each tier.</summary>
public sealed class UpgradeQueue
{
    private readonly Dictionary<LoyaltyTier,Queue<UpgradeRequest>> _queues=new();
    private static readonly LoyaltyTier[] Priority=[LoyaltyTier.Platinum,LoyaltyTier.Gold,LoyaltyTier.Silver,LoyaltyTier.None];
    /// <summary>Gets the total number of pending requests.</summary>
    public int Count {get{int total=0;foreach(Queue<UpgradeRequest> q in _queues.Values)total+=q.Count;return total;}}
    /// <summary>Adds a request to its loyalty queue.</summary>
    public void Enqueue(LoyaltyTier tier,UpgradeRequest request){ArgumentNullException.ThrowIfNull(request);if(!_queues.TryGetValue(tier,out Queue<UpgradeRequest>? q)){q=new Queue<UpgradeRequest>();_queues.Add(tier,q);}q.Enqueue(request);}
    /// <summary>Processes each request present at call start at most once, preserving deferred order.</summary>
    public IReadOnlyList<UpgradeDecision> ProcessUpgrades(CabinState state,OperatorContext context,IAuditSink audit)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(context);ArgumentNullException.ThrowIfNull(audit);var decisions=new List<UpgradeDecision>();var deferred=new List<(LoyaltyTier Tier,UpgradeRequest Request)>();
        foreach(LoyaltyTier tier in Priority)
        {
            if(!_queues.TryGetValue(tier,out Queue<UpgradeRequest>? queue)||queue.Count==0)continue;int atStart=queue.Count;
            for(int i=0;i<atStart;i++)
            {
                if(!queue.TryDequeue(out UpgradeRequest? request))break;
                if(!state.TryGetPassenger(request.Pnr,out Passenger? passenger)){decisions.Add(new(request,UpgradeDecisionKind.Rejected,"Unknown PNR.",null,null));audit.Record(context.Actor,$"UPG {request.Pnr} REJECTED Unknown PNR");continue;}
                if((int)request.TargetCabin >= (int)passenger!.EntitledCabin){decisions.Add(new(request,UpgradeDecisionKind.Rejected,"Upgrade must be upward.",null,null));audit.Record(context.Actor,$"UPG {request.Pnr} REJECTED Not upward");continue;}
                Seat? eligible=null;bool anyAvailable=false;foreach(Seat seat in state.SeatMap.GetSeats(request.TargetCabin))if(seat.Status==SeatStatus.Available){anyAvailable=true;CabinClass old=passenger.EntitledCabin;passenger.EntitledCabin=request.TargetCabin;bool ok=seat.IsEligibleFor(passenger,state.Schedule.LocalDepartureDate,out _);passenger.EntitledCabin=old;if(ok){eligible=seat;break;}}
                if(eligible is null)
                {deferred.Add((tier,request));decisions.Add(new(request,UpgradeDecisionKind.Deferred,anyAvailable?"No suitable available seat.":"No seat is currently available.",null,null));continue;}
                decimal fee=ComputeFee(passenger.EntitledCabin,request.TargetCabin,tier);var operation=new UpgradeOperation(request.Pnr,request.TargetCabin,eligible.Id);OperationResult result=state.History.Execute(operation,state,context,audit);
                decisions.Add(result.Succeeded?new(request,UpgradeDecisionKind.Granted,null,fee,eligible.Id):new(request,UpgradeDecisionKind.Rejected,result.Reason,null,null));
            }
        }
        foreach(var item in deferred)Enqueue(item.Tier,item.Request);
        return decisions.AsReadOnly();
    }
    /// <summary>Computes a loyalty-discounted two-decimal upgrade fee using away-from-zero rounding.</summary>
    public static decimal ComputeFee(CabinClass from,CabinClass to,LoyaltyTier tier)
    {
        decimal baseFee=(from,to) switch{(CabinClass.Economy,CabinClass.PremiumEconomy)=>BusinessRules.EconomyToPremiumFee,(CabinClass.Economy,CabinClass.Business)=>BusinessRules.EconomyToBusinessFee,(CabinClass.PremiumEconomy,CabinClass.Business)=>BusinessRules.PremiumToBusinessFee,_=>throw new ArgumentException("Only upward upgrades have a fee.")};
        decimal discount=tier switch{LoyaltyTier.None=>0m,LoyaltyTier.Silver=>BusinessRules.SilverDiscount,LoyaltyTier.Gold=>BusinessRules.GoldDiscount,LoyaltyTier.Platinum=>1m,_=>throw new ArgumentOutOfRangeException(nameof(tier))};return decimal.Round(baseFee*(1m-discount),2,MidpointRounding.AwayFromZero);
    }
}
/// <summary>Culture-aware human-readable numeric output with domain rounding.</summary>
public static class NumberFormatter
{
    /// <summary>Formats mass with one decimal place and kg suffix.</summary>
    public static string FormatMass(decimal value,IFormatProvider culture)=>value.ToString("N1",culture)+" kg";
    /// <summary>Formats an occupancy ratio as a percent with one decimal place.</summary>
    public static string FormatLoad(decimal ratio,IFormatProvider culture)=>ratio.ToString("P1",culture);
    /// <summary>Formats a monetary fee in euros, rounding away from zero to cents.</summary>
    public static string FormatFee(decimal value,IFormatProvider culture)
    {ArgumentNullException.ThrowIfNull(culture);var format=(System.Globalization.NumberFormatInfo)System.Globalization.NumberFormatInfo.GetInstance(culture).Clone();format.CurrencySymbol="€";return decimal.Round(value,2,MidpointRounding.AwayFromZero).ToString("C2",format);}
    /// <summary>Formats a row number with three digits.</summary>
    public static string FormatRow(byte row)=>row.ToString("D3",System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>Formats a cell right-aligned to a fixed composite-format width.</summary>
    public static string FormatColumn<T>(T value,int width,string format,IFormatProvider culture)
    {if(width<0)throw new ArgumentOutOfRangeException(nameof(width));ArgumentNullException.ThrowIfNull(format);ArgumentNullException.ThrowIfNull(culture);return string.Format(culture,"{0,"+width.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+format+"}",value);}
    /// <summary>Formats a hash byte as lowercase hexadecimal.</summary>
    public static string FormatHashByte(byte value)=>value.ToString("x2",System.Globalization.CultureInfo.InvariantCulture);
}
