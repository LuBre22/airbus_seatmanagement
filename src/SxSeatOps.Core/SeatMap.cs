using System.Collections.ObjectModel;
namespace SxSeatOps.Core;

/// <summary>Immutable description of the aircraft's row layouts and designated features.</summary>
public sealed class CabinLayout
{
    /// <summary>Gets all rows in ascending order.</summary>
    public IReadOnlyList<byte> RowNumbers { get; }
    private static readonly (CabinClass Cabin,byte First,byte Last,string Pattern)[] Layouts =
    [ (CabinClass.Business,1,10,"A-DG-K"), (CabinClass.PremiumEconomy,21,23,"AB-DEGH-JK"), (CabinClass.Economy,31,55,"ABC-DEG-HJK") ];
    /// <summary>Creates the fixed SX 0350 cabin layout.</summary>
    public CabinLayout() { var rows=new List<byte>(40); foreach(var l in Layouts) for(int r=l.First;r<=l.Last;r++) rows.Add((byte)r); RowNumbers=Array.AsReadOnly(rows.ToArray()); }
    /// <summary>Returns all seat identifiers in the selected cabin and row order.</summary>
    public IReadOnlyList<SeatId> GetSeatIds(CabinClass cabin)
    { var result=new List<SeatId>(); foreach(var l in Layouts) if(l.Cabin==cabin) for(int r=l.First;r<=l.Last;r++) foreach(char c in l.Pattern) if(c!='-') result.Add(new SeatId((byte)r,c)); return result.AsReadOnly(); }
    /// <summary>Tests whether an identifier exists in the layout.</summary>
    public bool Contains(SeatId id) { foreach(var l in Layouts) if(id.Row>=l.First && id.Row<=l.Last && l.Pattern.IndexOf(id.Column)>=0) return true; return false; }
    /// <summary>Gets the cabin assigned to a seat identifier.</summary>
    public bool TryGetCabin(SeatId id,out CabinClass cabin) { foreach(var l in Layouts) if(id.Row>=l.First&&id.Row<=l.Last&&l.Pattern.IndexOf(id.Column)>=0){cabin=l.Cabin;return true;} cabin=default;return false; }
    internal static bool TryGetLayout(byte row,out CabinClass cabin,out string? pattern)
    { foreach(var l in Layouts) if(row>=l.First&&row<=l.Last){cabin=l.Cabin;pattern=l.Pattern;return true;} cabin=default;pattern=null;return false; }
    internal static SeatAttributes Derive(byte row,string pattern,int position,char column)
    {
        SeatAttributes a=SeatAttributes.None; if(position==0||position==pattern.Length-1)a|=SeatAttributes.Window;
        if(position>0&&pattern[position-1]=='-' || position+1<pattern.Length&&pattern[position+1]=='-')a|=SeatAttributes.Aisle;
        if(row is 41 or 42)a|=SeatAttributes.ExitRow|SeatAttributes.ExtraLegroom;
        if(row==31)a|=SeatAttributes.ExtraLegroom;
        if(row is 1 or 21 or 31)a|=SeatAttributes.NearGalley;
        if(row is 10 or 23 or 55)a|=SeatAttributes.NearLavatory;
        if(row==31&&column is 'D' or 'E' or 'G')a|=SeatAttributes.Bassinet;
        return a;
    }
}

/// <summary>Seat storage built from a jagged layout array; aisle characters never occupy slots.</summary>
public sealed class SeatMap
{
    private readonly byte[] _rowNumbers;
    private readonly Seat?[][] _grid;
    private readonly ReadOnlyCollection<Seat> _allSeats;
    /// <summary>Gets the layout used to create this map.</summary>
    public CabinLayout Layout { get; }
    /// <summary>Gets all seats as a read-only collection.</summary>
    public IReadOnlyCollection<Seat> Seats=>_allSeats;
    /// <summary>Creates the standard 289-seat aircraft configuration.</summary>
    public SeatMap(CabinLayout? layout=null)
    {
        Layout=layout??new CabinLayout();_rowNumbers=new byte[Layout.RowNumbers.Count];
        for(int i=0;i<_rowNumbers.Length;i++)_rowNumbers[i]=Layout.RowNumbers[i];
        _grid=new Seat?[_rowNumbers.Length][];var all=new List<Seat>();
        for(int rowIndex=0;rowIndex<_rowNumbers.Length;rowIndex++)
        {
            byte row=_rowNumbers[rowIndex];if(!CabinLayout.TryGetLayout(row,out CabinClass cabin,out string? pattern)||pattern is null)throw new InvalidOperationException("Layout row has no cabin pattern.");
            int count=0;foreach(char c in pattern)if(c!='-')count++;_grid[rowIndex]=new Seat?[count];int slot=0;
            for(int position=0;position<pattern.Length;position++)if(pattern[position]!='-')
            {var id=new SeatId(row,pattern[position]);Seat seat=SeatFactory.Create(cabin,id,CabinLayout.Derive(row,pattern,position,pattern[position]));_grid[rowIndex][slot++]=seat;all.Add(seat);}
        }
        all.Sort((left,right)=>left.Id.CompareTo(right.Id));_allSeats=all.AsReadOnly();
    }
    /// <summary>Finds a seat by row and cabin layout column order.</summary>
    public bool TryGetSeat(SeatId id,out Seat? seat)
    {
        seat=null;int rowIndex=Array.BinarySearch(_rowNumbers,id.Row);if(rowIndex<0||!CabinLayout.TryGetLayout(id.Row,out _,out string? pattern)||pattern is null)return false;
        int slot=0;foreach(char column in pattern)if(column!='-'){if(column==id.Column){seat=_grid[rowIndex][slot];return seat is not null;}slot++;}return false;
    }
    /// <summary>Finds a seat using validated text.</summary>
    public bool TryGetSeat(string? text,out Seat? seat){seat=null;return SeatId.TryParse(text,out SeatId id)&&TryGetSeat(id,out seat);}
    /// <summary>Returns seats in deterministic row and layout-column order.</summary>
    public IReadOnlyList<Seat> GetSeats(CabinClass cabin)
    {var result=new List<Seat>();foreach(Seat seat in _allSeats)if(seat.Cabin==cabin)result.Add(seat);return result.AsReadOnly();}
    /// <summary>Returns the first available seat satisfying all required and no forbidden attributes.</summary>
    public Seat? FindFirstFree(CabinClass cabin,SeatAttributes required=SeatAttributes.None,SeatAttributes forbidden=SeatAttributes.None)
    {foreach(Seat seat in _allSeats)if(seat.Cabin==cabin&&seat.Status==SeatStatus.Available&&seat.Attributes.HasAll(required)&&!seat.Attributes.HasAny(forbidden))return seat;return null;}
    /// <summary>Returns per-cabin total, blocked, occupied, and available counts.</summary>
    public CabinStatistics GetStatistics(CabinClass cabin)
    {int total=0,blocked=0,occupied=0;foreach(Seat seat in _allSeats)if(seat.Cabin==cabin){total++;if(seat.Status==SeatStatus.Blocked)blocked++;if(seat.Status==SeatStatus.Occupied)occupied++;}return new CabinStatistics(total,blocked,occupied,total-blocked-occupied);}
}

/// <summary>Seat counts for one cabin.</summary>
public readonly record struct CabinStatistics(int Total,int Blocked,int Occupied,int Available);
