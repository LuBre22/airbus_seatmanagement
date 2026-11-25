using System.Globalization;
using SxSeatOps.Core;

namespace SxSeatOps.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        TimeProvider clock=TimeProvider.System;bool manual=false;string? script=null;
        try
        {
            for(int i=0;i<args.Length;i++)
            {
                if(args[i]=="--manual-clock"&&i+1<args.Length&&TimestampFormatter.TryParseIso(args[++i],out DateTimeOffset initial)){clock=new ManualTimeProvider(initial);manual=true;}
                else if(args[i]=="--script"&&i+1<args.Length)script=args[++i];
                else {Console.Error.WriteLine("Usage: SeatOps [--manual-clock ISO] [--script path]");return 1;}
            }
            using TextReader input=script is null?Console.In:new StreamReader(script);
            var state=new CabinState(clock:clock);var audit=new AuditChain(clock);OperationHistory history=state.History;OperatorContext? actor=null;bool syntaxError=false;
            if(script is null)Console.WriteLine("SeatOps internal seat management (type STAT or EXIT)");
            string? line;while((line=ReadLine(input,script is null)) is not null)
            {
                if(line.Equals("EXIT",StringComparison.OrdinalIgnoreCase))break;
                CommandParseResult parsed=CommandParser.Parse(line);if(!parsed.Succeeded){Console.WriteLine($"ERROR: {parsed.Error}");if(script is not null)syntaxError=true;continue;}
                ParsedCommand command=parsed.Command!;
                try{Execute(command,state,audit,history,ref actor,clock,manual);}catch(IOException ex){Console.Error.WriteLine($"I/O error: {ex.Message}");return 2;}catch(ArgumentException ex){Console.WriteLine($"ERROR: {ex.Message}");if(script is not null)syntaxError=true;}catch(FormatException ex){Console.WriteLine($"ERROR: {ex.Message}");if(script is not null)syntaxError=true;}
            }
            return syntaxError?1:0;
        }
        catch(IOException ex){Console.Error.WriteLine($"I/O error: {ex.Message}");return 2;}
    }
    private static string? ReadLine(TextReader input,bool interactive)
    {if(interactive){Console.Write("SeatOps> ");}return input.ReadLine();}
    private static void Execute(ParsedCommand c,CabinState state,AuditChain audit,OperationHistory history,ref OperatorContext? actor,TimeProvider clock,bool manual)
    {
        string[] a=c.Arguments.ToArray();
        switch(c.Name)
        {
            case "LOGIN":
                if(a.Length!=2){Console.WriteLine("ERROR: LOGIN expects actor and comma-separated roles.");return;}
                if(a[0].Length is <1 or >32){Console.WriteLine("ERROR: Actor must be 1-32 characters.");return;}
                if(!TryRoles(a[1],out OperatorRole roles)){Console.WriteLine("ERROR: Unknown or empty role.");return;}actor=new OperatorContext(a[0],roles);Console.WriteLine($"Logged in: {actor.Actor} ({roles})");break;
            case "STAT": PrintStats(state,audit);break;
            case "CLOCK":
                if(!manual||clock is not ManualTimeProvider manualClock){Console.WriteLine("REJECTED: CLOCK is available only with --manual-clock.");return;}
                if(!TimestampFormatter.TryParseIso(a[0],out DateTimeOffset instant)){Console.WriteLine("ERROR: CLOCK expects ISO 8601 with offset.");return;}manualClock.SetUtcNow(instant);Console.WriteLine("Clock set to "+instant.ToString("O",CultureInfo.InvariantCulture));break;
            case "BLK":
                if(actor is null){Console.WriteLine("REJECTED: LOGIN required.");return;}
                if(a.Length<2||!EnumExtensions.TryParseCode(a[^1],out BlockReason reason)){Console.WriteLine("ERROR: BLK requires a seat and reason code.");return;}
                string seatToken=a[0];if(!SeatId.TryParse(seatToken,out SeatId blockSeat)){Console.WriteLine("ERROR: This compact CLI supports one seat per BLK command.");return;}
                Print(history.Execute(new BlockSeatOperation(blockSeat,reason),state,actor,audit));break;
            case "REL":
                if(actor is null||!SeatId.TryParse(a[0],out SeatId releaseSeat)){Console.WriteLine("REJECTED: LOGIN required or invalid seat.");return;}Print(history.Execute(new ReleaseSeatOperation(releaseSeat),state,actor,audit));break;
            case "ASG":
                if(actor is null||!SeatId.TryParse(a[1],out SeatId assignSeat)){Console.WriteLine("REJECTED: LOGIN required or invalid seat.");return;}Print(history.Execute(new AssignSeatOperation(a[0],assignSeat),state,actor,audit));break;
            case "SWP":
                if(actor is null||!SeatId.TryParse(a[0],out SeatId first)||!SeatId.TryParse(a[1],out SeatId second)){Console.WriteLine("REJECTED: LOGIN required or invalid seats.");return;}Print(history.Execute(new SwapSeatsOperation(first,second),state,actor,audit));break;
            case "UPG":
                if(actor is null||!actor.Roles.HasFlag(OperatorRole.Revenue)||!EnumExtensions.TryParseCabinCode(a[1],out CabinClass target)||!state.TryGetPassenger(a[0],out Passenger? upgradePassenger)){Console.WriteLine("REJECTED: REVENUE login, known PNR, or valid target cabin required.");return;}
                state.UpgradeQueue.Enqueue(upgradePassenger!.Tier,new UpgradeRequest(a[0],target,clock.GetUtcNow()));audit.Record(actor.Actor,$"UPG {a[0]} REQUESTED {target}");Console.WriteLine("Upgrade request queued.");break;
            case "PROCESS":
                if(actor is null||!actor.Roles.HasFlag(OperatorRole.Revenue)){Console.WriteLine("REJECTED: REVENUE login required.");return;}
                foreach(UpgradeDecision decision in state.UpgradeQueue.ProcessUpgrades(state,actor,audit))Console.WriteLine($"{decision.Kind} {decision.Request.Pnr}: {decision.Reason ?? decision.Fee?.ToString("C2",CultureInfo.GetCultureInfo("de-DE"))}");break;
            case "UNDO":
                OperatorContext undoActor=actor??new OperatorContext("ANONYMOUS",OperatorRole.None);if(history.TryUndo(state,undoActor,audit,out SeatOperation? undone)){Console.WriteLine("UNDO accepted.");}else Console.WriteLine("REJECTED: UNDO was not permitted or history is empty.");break;
            case "REDO":
                OperatorContext redoActor=actor??new OperatorContext("ANONYMOUS",OperatorRole.None);if(history.TryRedo(state,redoActor,audit,out SeatOperation? redone)){Console.WriteLine("REDO accepted.");}else Console.WriteLine("REJECTED: REDO was not permitted or history is empty.");break;
            case "VERIFY":
                if(audit.Verify(out long invalid,out string? why))Console.WriteLine($"Audit valid: {audit.Blocks.Count} blocks.");else Console.WriteLine($"Audit invalid at {invalid}: {why}");break;
            case "IMPORT":
                if(actor is null||!actor.Roles.HasFlag(OperatorRole.Station)){Console.WriteLine("REJECTED: STATION login required.");return;}
                if(a.Length!=2||!a[0].Equals("MANIFEST",StringComparison.OrdinalIgnoreCase)){Console.WriteLine("ERROR: Expected IMPORT MANIFEST path.");return;}
                var imported=new ManifestCsvImporter().Import(a[1],state.Schedule,state.SeatMap.Layout);if(imported.FatalError is not null){Console.WriteLine("IMPORT ERROR: "+imported.FatalError);return;}
                int accepted=0;foreach(Passenger passenger in imported.Passengers.Values)if(state.TryAddPassenger(passenger))accepted++;
                audit.Record(actor.Actor,$"IMPORT MANIFEST accepted={accepted} issues={imported.Issues.Count}");
                foreach(ImportIssue issue in imported.Issues)Console.WriteLine($"{issue.Severity} line {issue.LineNumber} {issue.Field}: {issue.Message}");
                foreach(var preassignment in imported.PreAssignments){OperationResult assignment=history.Execute(new AssignSeatOperation(preassignment.Value,preassignment.Key),state,actor,audit);if(!assignment.Succeeded)Console.WriteLine($"Preassignment rejected: {assignment.Reason}");}
                Console.WriteLine($"Imported {accepted} passenger(s).");break;
            default:Console.WriteLine("ERROR: Command is not implemented in this CLI profile.");break;
        }
        if(!state.CheckConsistency(out string? violation))Console.WriteLine("INVARIANT ERROR: "+violation);
    }
    private static bool TryRoles(string text,out OperatorRole roles)
    {roles=OperatorRole.None;string[] parts=text.Split(',');foreach(string part in parts){string role=part.Trim().ToUpperInvariant();OperatorRole bit=role switch{"OCC"=>OperatorRole.Occ,"STATION"=>OperatorRole.Station,"REVENUE"=>OperatorRole.Revenue,"PURSER"=>OperatorRole.Purser,"COMPLIANCE"=>OperatorRole.Compliance,"SUPERVISOR"=>OperatorRole.Supervisor,_=>OperatorRole.None};if(bit==OperatorRole.None)return false;roles|=bit;}return roles!=OperatorRole.None;}
    private static void Print(OperationResult result)=>Console.WriteLine(result.Succeeded?$"ACCEPTED {result.OperationId}":$"REJECTED: {result.Reason}");
    private static void PrintStats(CabinState state,AuditChain audit)
    {
        FlightSchedule f=state.Schedule;string departureLocal=TimeZoneInfo.ConvertTime(f.Std,f.OriginZone).ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture);string arrivalLocal=TimeZoneInfo.ConvertTime(f.Sta,f.DestinationZone).ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture);string departureUtc=f.StdUtc.ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture);string arrivalUtc=f.StaUtc.ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture);Console.WriteLine($"{f.FlightNumber} {f.Origin} -> {f.Destination} | STD {departureLocal} ({f.OriginZone.Id}) / {departureUtc}Z | STA {arrivalLocal} ({f.DestinationZone.Id}) / {arrivalUtc}Z");
        Console.WriteLine("Cabin             Total Blocked Occupied Available Load");foreach(CabinClass c in Enum.GetValues<CabinClass>()){CabinStatistics x=state.SeatMap.GetStatistics(c);decimal load=x.Total==0?0m:(decimal)x.Occupied/x.Total;string loadText=NumberFormatter.FormatLoad(load,CultureInfo.GetCultureInfo("de-DE"));Console.WriteLine($"{c,-17} {x.Total,5} {x.Blocked,7} {x.Occupied,8} {x.Available,9} {loadText,8}");}
        int infants=0,children=0,adults=0;decimal passengerMass=0m,baggageMass=0m;foreach(Passenger passenger in state.Passengers.Values){PassengerType type=AgeRules.Classify(passenger.BirthDate,f.LocalDepartureDate);switch(type){case PassengerType.Infant:infants++;passengerMass+=StandardPassengerMass.InfantKilograms;break;case PassengerType.Child:children++;passengerMass+=StandardPassengerMass.ChildKilograms;break;default:adults++;passengerMass+=StandardPassengerMass.AdultKilograms;break;}baggageMass+=passenger.BaggageKg;}
        Console.WriteLine($"Passengers: {state.Passengers.Count} (Infant {infants}, Child {children}, Adult {adults}); Upgrade requests: {state.UpgradeQueue.Count}");Console.WriteLine($"Mass: passengers {NumberFormatter.FormatMass(passengerMass,CultureInfo.GetCultureInfo("de-DE"))}; baggage {NumberFormatter.FormatMass(baggageMass,CultureInfo.GetCultureInfo("de-DE"))}; total {NumberFormatter.FormatMass(passengerMass+baggageMass,CultureInfo.GetCultureInfo("de-DE"))}");Console.WriteLine($"Audit blocks: {audit.Blocks.Count}; Head: {audit.Head.Hash[..12]}");
    }
}
