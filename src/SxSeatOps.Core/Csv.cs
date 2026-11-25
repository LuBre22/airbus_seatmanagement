using System.Globalization;
using System.Text;

namespace SxSeatOps.Core;

/// <summary>CSV culture and line-ending configuration.</summary>
public sealed record CsvDialect(char Delimiter,CultureInfo NumberCulture,string LineEnding)
{
    /// <summary>German manifest dialect.</summary>
    public static CsvDialect Manifest {get;}=new(';',CultureInfo.GetCultureInfo("de-DE"),"\r\n");
    /// <summary>Machine-stable invariant dialect.</summary>
    public static CsvDialect Machine {get;}=new(';',CultureInfo.InvariantCulture,"\r\n");
}
/// <summary>CSV field escaping and reversible formula-prefix neutralization.</summary>
public static class CsvWriter
{
    /// <summary>Encodes a text field, neutralizing formula prefixes and applying RFC-style quoting.</summary>
    public static string EncodeText(string? field,char delimiter=';')
    {
        string value=field??string.Empty;
        if(value.Length>0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value="'"+value;
        bool quote=value.Contains(delimiter)||value.Contains('"')||value.Contains('\r')||value.Contains('\n')||value.Length>0&&(char.IsWhiteSpace(value[0])||char.IsWhiteSpace(value[^1]));
        if(!quote)return value;return "\""+value.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";
    }
    /// <summary>Decodes a field after CSV tokenization and reverses a leading apostrophe escape.</summary>
    public static string DecodeText(string field)
    {if(field.Length>1&&field[0]=='\''&&field[1] is '=' or '+' or '-' or '@' or '\t' or '\r')return field[1..];return field;}
    /// <summary>Writes fields separated by the selected dialect delimiter.</summary>
    public static string WriteRow(IReadOnlyList<string?> fields,CsvDialect? dialect=null)
    {ArgumentNullException.ThrowIfNull(fields);CsvDialect d=dialect??CsvDialect.Machine;var b=new StringBuilder();for(int i=0;i<fields.Count;i++){if(i>0)b.Append(d.Delimiter);b.Append(EncodeText(fields[i],d.Delimiter));}b.Append(d.LineEnding);return b.ToString();}
}
/// <summary>A tokenized CSV row and its physical starting line.</summary>
public sealed record CsvRecord(int StartLine,IReadOnlyList<string> Fields);
/// <summary>State-machine CSV reader supporting quoted delimiters, doubled quotes, and embedded newlines.</summary>
public static class CsvTokenizer
{
    /// <summary>Reads all CSV records from a text reader without using string.Split on the data.</summary>
    public static IReadOnlyList<CsvRecord> ReadAll(TextReader reader,char delimiter=';')
    {
        ArgumentNullException.ThrowIfNull(reader);var records=new List<CsvRecord>();var fields=new List<string>();var field=new StringBuilder();bool quoted=false,afterQuote=false;int line=1,start=1;int raw;
        while((raw=reader.Read())!=-1)
        {
            char c=(char)raw;
            if(quoted){if(c=='"'){if(reader.Peek()=='"'){reader.Read();field.Append('"');}else{quoted=false;afterQuote=true;}}else{field.Append(c);if(c=='\n')line++;}continue;}
            if(afterQuote){if(c==delimiter){fields.Add(field.ToString());field.Clear();afterQuote=false;continue;}if(c=='\r'||c=='\n'){fields.Add(field.ToString());field.Clear();AddRecord();afterQuote=false;if(c=='\r'&&reader.Peek()=='\n')reader.Read();line++;start=line;continue;}if(char.IsWhiteSpace(c))continue;throw new FormatException($"Unexpected character after closing quote at line {line}.");}
            if(c=='"'&&field.Length==0){quoted=true;continue;}
            if(c==delimiter){fields.Add(field.ToString());field.Clear();continue;}
            if(c=='\r'||c=='\n'){fields.Add(field.ToString());field.Clear();AddRecord();if(c=='\r'&&reader.Peek()=='\n')reader.Read();line++;start=line;continue;}
            field.Append(c);
        }
        if(quoted)throw new FormatException($"Unclosed quoted field starting at line {start}.");
        if(field.Length>0||fields.Count>0||afterQuote){fields.Add(field.ToString());AddRecord();}
        return records.AsReadOnly();
        void AddRecord(){if(fields.Count==1&&fields[0].Length==0){fields.Clear();return;}records.Add(new CsvRecord(start,fields.ToArray()));fields.Clear();}
    }
}
/// <summary>Severity of a manifest-import finding.</summary>
public enum IssueSeverity:byte{Warning=0,Error=1}
/// <summary>One import validation issue.</summary>
public sealed record ImportIssue(int LineNumber,string Field,IssueSeverity Severity,string Message);
/// <summary>Manifest import output.</summary>
public sealed class ImportResult
{
    /// <summary>Imported passengers indexed by PNR.</summary>
    public Dictionary<string,Passenger> Passengers {get;}=new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Initial seat assignments indexed by seat.</summary>
    public Dictionary<SeatId,string> PreAssignments {get;}=new();
    /// <summary>Validation issues in encounter order.</summary>
    public List<ImportIssue> Issues {get;}=new();
    /// <summary>Fatal file/header error, if any.</summary>
    public string? FatalError {get; internal set;}
}
/// <summary>File-based, bounded-size manifest importer.</summary>
public sealed class ManifestCsvImporter
{
    /// <summary>Imports the required manifest fields. Invalid data rows are reported and skipped.</summary>
    public ImportResult Import(string path,FlightSchedule schedule,CabinLayout layout)
    {
        ArgumentNullException.ThrowIfNull(path);ArgumentNullException.ThrowIfNull(schedule);ArgumentNullException.ThrowIfNull(layout);
        var result=new ImportResult();var info=new FileInfo(path);if(info.Length>BusinessRules.MaximumManifestBytes){result.FatalError="Manifest exceeds the 5 MiB maximum.";return result;}
        using var reader=new StreamReader(path,new UTF8Encoding(false,true),true);IReadOnlyList<CsvRecord> records;
        try{records=CsvTokenizer.ReadAll(reader);}catch(FormatException ex){result.FatalError=ex.Message;return result;}
        int headerIndex=-1;CsvRecord? header=null;
        for(int i=0;i<records.Count;i++){string first=records[i].Fields.Count>0?records[i].Fields[0]:string.Empty;if(first.StartsWith('#'))continue;header=records[i];headerIndex=i;break;}
        if(header is null){result.FatalError="Manifest has no header.";return result;}
        var columns=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);for(int i=0;i<header.Fields.Count;i++)columns.TryAdd(header.Fields[i].Trim(),i);
        string[] required=["PNR","Nachname","Vorname","Geburtsdatum","Kabine","Vielflieger","Meilen","Gepaeck_kg","CheckIn","Ticket","PRM","Sitz"];
        foreach(string col in required)if(!columns.ContainsKey(col)){result.FatalError=$"Required column '{col}' is missing.";return result;}
        foreach(string column in columns.Keys){bool known=false;foreach(string expected in required)if(string.Equals(column,expected,StringComparison.OrdinalIgnoreCase)){known=true;break;}if(!known)result.Issues.Add(new ImportIssue(header.StartLine,column,IssueSeverity.Warning,"Unknown column was ignored."));}
        for(int ri=headerIndex+1;ri<records.Count;ri++)
        {
            CsvRecord row=records[ri];if(row.Fields.Count==1&&row.Fields[0].StartsWith('#'))continue;
            string Get(string name)=>columns.TryGetValue(name,out int ix)&&ix<row.Fields.Count?row.Fields[ix].Trim():string.Empty;
            string pnr=Get("PNR"),surname=Get("Nachname"),given=Get("Vorname");
            if(!Validators.IsValidPnr(pnr)){Add("PNR","Invalid PNR.");continue;}
            if(result.Passengers.ContainsKey(pnr)){Add("PNR","Duplicate PNR; first row wins.");continue;}
            if(!NameField.TryFormat(surname,given,null,out string? name,out bool replaced)){Add("Name","Invalid or empty name.");continue;}
            if(!DateOnly.TryParseExact(Get("Geburtsdatum"),["dd.MM.yyyy","yyyy-MM-dd"],CultureInfo.InvariantCulture,DateTimeStyles.None,out DateOnly birth)){Add("Geburtsdatum","Invalid date.");continue;}
            if(!EnumExtensions.TryParseCabinCode(Get("Kabine"),out CabinClass cabin)){Add("Kabine","Unknown cabin code.");continue;}
            if(!EnumExtensions.TryParseEnum(Get("Vielflieger"),out LoyaltyTier tier)){Add("Vielflieger","Unknown tier.");continue;}
            if(!NumberParser.TryParseMiles(Get("Meilen"),out long miles)){Add("Meilen","Invalid miles.");continue;}
            if(!NumberParser.TryParseBaggageGerman(Get("Gepaeck_kg"),out decimal baggage)||baggage>100){Add("Gepaeck_kg","Invalid baggage mass.");continue;}
            if(!Validators.IsValidTicketNumber(Get("Ticket"))){Add("Ticket","Invalid ticket number.");continue;}
            bool prm=Get("PRM") switch{"0"=>false,"1"=>true,_=>true};if(Get("PRM") is not ("0" or "1")){Add("PRM","Expected 0 or 1.");continue;}
            DateTimeOffset checkedIn;if(!TryParseCheckIn(Get("CheckIn"),schedule,out checkedIn)||!schedule.IsCheckInOpen(checkedIn)){Add("CheckIn","Invalid or outside check-in window.");continue;}
            var passenger=new Passenger(pnr,name!,birth,cabin,tier,miles,baggage,checkedIn,Get("Ticket"),prm);
            string seatText=Get("Sitz");if(seatText.Length>0){if(!SeatId.TryParse(seatText,out SeatId seat)||!layout.Contains(seat)||AgeRules.Classify(birth,schedule.LocalDepartureDate)==PassengerType.Infant){Add("Sitz","Invalid seat or infant seat assignment.");continue;}if(result.PreAssignments.ContainsKey(seat)){Add("Sitz","Duplicate seat; first row wins.");continue;}if(!layout.TryGetCabin(seat,out CabinClass seatCabin)||seatCabin!=cabin){Add("Sitz","Seat cabin differs from booked cabin.");continue;}if(!CabinLayout.TryGetLayout(seat.Row,out _,out string? rowPattern)||rowPattern is null){Add("Sitz","Seat row is not defined.");continue;}int position=rowPattern.IndexOf(seat.Column);Seat derived=SeatFactory.Create(seatCabin,seat,CabinLayout.Derive(seat.Row,rowPattern,position,seat.Column));if(!derived.IsEligibleFor(passenger,schedule.LocalDepartureDate,out string? eligibilityReason)){Add("Sitz",eligibilityReason??"Passenger is not eligible for this seat.");continue;}result.PreAssignments.Add(seat,pnr);}
            result.Passengers.Add(pnr,passenger);if(replaced)result.Issues.Add(new ImportIssue(row.StartLine,"Name",IssueSeverity.Warning,"Unsupported name characters were replaced."));
            void Add(string field,string message)=>result.Issues.Add(new ImportIssue(row.StartLine,field,IssueSeverity.Error,message));
        }
        return result;
    }
    private static bool TryParseCheckIn(string value,FlightSchedule schedule,out DateTimeOffset checkIn)
    {return TimestampParser.TryParse(value,schedule,out checkIn,out _);}
}

/// <summary>Writes UTF-8-without-BOM CSV atomically using a temporary sibling file.</summary>
public static class AtomicCsvFile
{
    /// <summary>Writes complete CSV content and atomically replaces the destination.</summary>
    public static void Write(string path,string content)
    {
        ArgumentNullException.ThrowIfNull(path);ArgumentNullException.ThrowIfNull(content);string full=Path.GetFullPath(path);string directory=Path.GetDirectoryName(full)!;Directory.CreateDirectory(directory);string temp=Path.Combine(directory,"."+Path.GetFileName(full)+"."+Guid.NewGuid().ToString("N")+".tmp");
        try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] bytes=new UTF8Encoding(false).GetBytes(content);stream.Write(bytes);}File.Move(temp,full,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
/// <summary>Exports a manifest in deterministic PNR order.</summary>
public static class ManifestCsvExporter
{
    /// <summary>Creates manifest CSV, filling seat designators from the passenger-seat index.</summary>
    public static string Export(IReadOnlyDictionary<string,Passenger> passengers,IReadOnlyDictionary<string,SeatId> passengerSeats,CsvDialect? dialect=null)
    {
        ArgumentNullException.ThrowIfNull(passengers);ArgumentNullException.ThrowIfNull(passengerSeats);CsvDialect d=dialect??CsvDialect.Manifest;var keys=new List<string>(passengers.Keys);keys.Sort(StringComparer.Ordinal);var b=new StringBuilder();b.Append(CsvWriter.WriteRow(["PNR","Nachname","Vorname","Geburtsdatum","Kabine","Vielflieger","Meilen","Gepaeck_kg","CheckIn","Ticket","PRM","Sitz"],d));
        foreach(string pnr in keys){Passenger p=passengers[pnr];string family="",given="",title="";NameField.TryParse(p.NameField,out string? f,out string? g,out string? t);family=f??"";given=g??"";title=t??"";string fullFamily=family;var fields=new string?[]{pnr,fullFamily,given+(title.Length==0?"":" "+title),p.BirthDate.ToString("dd.MM.yyyy",CultureInfo.InvariantCulture),p.EntitledCabin.ToIataCode().ToString(),p.Tier.ToString(),p.Miles.ToString("N0",d.NumberCulture),p.BaggageKg.ToString("F1",d.NumberCulture),p.CheckIn.ToString("O",CultureInfo.InvariantCulture),p.Ticket,p.HasReducedMobility?"1":"0",passengerSeats.TryGetValue(pnr,out SeatId seat)?seat.ToString():""};b.Append(CsvWriter.WriteRow(fields,d));}
        return b.ToString();
    }
    /// <summary>Writes a manifest export to a path atomically.</summary>
    public static void Write(string path,IReadOnlyDictionary<string,Passenger> passengers,IReadOnlyDictionary<string,SeatId> passengerSeats,CsvDialect? dialect=null)=>AtomicCsvFile.Write(path,Export(passengers,passengerSeats,dialect));
}
/// <summary>Exports a deterministic seat-to-passenger table.</summary>
public static class SeatAssignmentsCsvExporter
{
    /// <summary>Creates ordered seat-assignment CSV.</summary>
    public static string Export(IReadOnlyDictionary<SeatId,string> assignments,CsvDialect? dialect=null)
    {ArgumentNullException.ThrowIfNull(assignments);CsvDialect d=dialect??CsvDialect.Machine;var keys=new List<SeatId>(assignments.Keys);keys.Sort();var b=new StringBuilder();b.Append(CsvWriter.WriteRow(["Sitz","PNR"],d));foreach(SeatId id in keys)b.Append(CsvWriter.WriteRow([id.ToString(),assignments[id]],d));return b.ToString();}
    /// <summary>Writes seat assignment CSV atomically.</summary>
    public static void Write(string path,IReadOnlyDictionary<SeatId,string> assignments,CsvDialect? dialect=null)=>AtomicCsvFile.Write(path,Export(assignments,dialect));
}
/// <summary>Exports per-cabin seat counts.</summary>
public static class StatsCsvExporter
{
    /// <summary>Creates cabin statistics CSV.</summary>
    public static string Export(IReadOnlyDictionary<CabinClass,CabinStatistics> statistics,CsvDialect? dialect=null)
    {ArgumentNullException.ThrowIfNull(statistics);CsvDialect d=dialect??CsvDialect.Machine;var b=new StringBuilder();b.Append(CsvWriter.WriteRow(["Cabin","Total","Blocked","Occupied","Available"],d));foreach(CabinClass cabin in Enum.GetValues<CabinClass>())if(statistics.TryGetValue(cabin,out CabinStatistics s))b.Append(CsvWriter.WriteRow([cabin.ToString(),s.Total.ToString(CultureInfo.InvariantCulture),s.Blocked.ToString(CultureInfo.InvariantCulture),s.Occupied.ToString(CultureInfo.InvariantCulture),s.Available.ToString(CultureInfo.InvariantCulture)],d));return b.ToString();}
}
