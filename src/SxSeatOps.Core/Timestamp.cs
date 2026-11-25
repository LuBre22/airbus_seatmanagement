using System.Globalization;

namespace SxSeatOps.Core;

/// <summary>Exact timestamp parsing and culture-independent report formatting.</summary>
public static class TimestampFormatter
{
    /// <summary>Formats UTC audit time as fixed millisecond ISO text.</summary>
    public static string FormatAudit(DateTimeOffset value)=>value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss.fff'Z'",CultureInfo.InvariantCulture);
    /// <summary>Formats an IATA local timestamp.</summary>
    public static string FormatIata(DateTimeOffset value)=>value.ToString("ddMMMyy HHmm",CultureInfo.InvariantCulture).ToUpperInvariant();
    /// <summary>Formats a German station timestamp including weekday and numeric offset.</summary>
    public static string FormatGermanStation(DateTimeOffset value,TimeZoneInfo zone)=>TimeZoneInfo.ConvertTime(value,zone).ToString("ddd, dd.MM.yyyy HH:mm '(UTC'zzz')'",CultureInfo.GetCultureInfo("de-DE"));
    /// <summary>Formats a Singapore station timestamp using English 12-hour notation.</summary>
    public static string FormatSingapore(DateTimeOffset value,TimeZoneInfo zone)=>TimeZoneInfo.ConvertTime(value,zone).ToString("ddd, d MMM yyyy h:mm tt",CultureInfo.GetCultureInfo("en-SG"));
    /// <summary>Formats the deterministic manifest file name for local departure date.</summary>
    public static string FormatManifestFileName(string flightNumber,DateOnly localDepartureDate)=>flightNumber+"_"+localDepartureDate.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+"_manifest.csv";
    /// <summary>Formats a duration as hours and minutes, or days and hours/minutes for at least one day.</summary>
    public static string FormatDuration(TimeSpan duration)
    {if(duration<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(duration));long totalMinutes=(long)duration.TotalMinutes;long hours=totalMinutes/60;long minutes=totalMinutes%60;if(hours<24)return hours.ToString("D2",CultureInfo.InvariantCulture)+" h "+minutes.ToString("D2",CultureInfo.InvariantCulture)+" min";long days=hours/24;long remaining=hours%24;return days.ToString(CultureInfo.InvariantCulture)+" d "+remaining.ToString("D2",CultureInfo.InvariantCulture)+" h "+minutes.ToString("D2",CultureInfo.InvariantCulture)+" min";}
    /// <summary>Formats a canonical lossless ISO instant with offset.</summary>
    public static string FormatIso(DateTimeOffset value)=>value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz",CultureInfo.InvariantCulture);
    /// <summary>Parses ISO 8601 timestamps carrying an explicit offset.</summary>
    public static bool TryParseIso(string? text,out DateTimeOffset value)
    {value=default;if(string.IsNullOrWhiteSpace(text))return false;return DateTimeOffset.TryParseExact(text.Trim(),["yyyy-MM-dd'T'HH:mm:sszzz","yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],CultureInfo.InvariantCulture,DateTimeStyles.None,out value);}
    /// <summary>Parses a UTC audit timestamp.</summary>
    public static bool TryParseAudit(string? text,out DateTimeOffset value)
    {value=default;if(string.IsNullOrWhiteSpace(text))return false;if(!DateTimeOffset.TryParseExact(text.Trim(),"yyyyMMdd'T'HHmmss.fff'Z'",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out value))return false;return true;}
    /// <summary>Parses IATA time using an explicit English month calendar.</summary>
    public static bool TryParseIata(string? text,TimeZoneInfo zone,out DateTimeOffset value,out string? warning)
    {
        value=default;warning=null;if(string.IsNullOrWhiteSpace(text))return false;
        string input=text.Trim();if(input.Length!=12||input[5] is < '0' or > '9'||input[6] is < '0' or > '9'||input[7]!=' ')return false;
        if(!DateTime.TryParseExact(input[..5].ToUpperInvariant()+"2000","ddMMMyyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out DateTime dayMonth))return false;
        if(!TimeOnly.TryParseExact(input[8..],"HHmm",CultureInfo.InvariantCulture,DateTimeStyles.None,out TimeOnly clock))return false;
        int year=2000+(input[5]-'0')*10+(input[6]-'0');DateTime local;
        try{local=new DateTime(year,dayMonth.Month,dayMonth.Day,clock.Hour,clock.Minute,0,DateTimeKind.Unspecified);}catch(ArgumentOutOfRangeException){return false;}
        if(zone.IsInvalidTime(local))return false;
        TimeSpan offset=zone.GetUtcOffset(local);if(zone.IsAmbiguousTime(local)){TimeSpan[] offsets=zone.GetAmbiguousTimeOffsets(local);offset=offsets[0];for(int i=1;i<offsets.Length;i++)if(offsets[i]>offset)offset=offsets[i];warning="Ambiguous local time resolved with the larger UTC offset.";}
        value=new DateTimeOffset(local,offset);return true;
    }
}

/// <summary>Strict parsing helpers for supported timestamps, dates, and times.</summary>
public static class TimestampParser
{
    /// <summary>Parses one of the specified flight timestamp forms.</summary>
    public static bool TryParse(string? text,FlightSchedule schedule,out DateTimeOffset value,out string? warning)
    {
        ArgumentNullException.ThrowIfNull(schedule);value=default;warning=null;if(string.IsNullOrWhiteSpace(text))return false;string input=text.Trim();
        if(TimestampFormatter.TryParseIso(input,out value))return true;
        if(TimestampFormatter.TryParseAudit(input,out value))return true;
        if(TimestampFormatter.TryParseIata(input,schedule.OriginZone,out value,out warning))return true;
        if(!DateTime.TryParseExact(input,"dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out DateTime local))return false;
        local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);if(schedule.OriginZone.IsInvalidTime(local))return false;
        TimeSpan offset=schedule.OriginZone.GetUtcOffset(local);if(schedule.OriginZone.IsAmbiguousTime(local)){TimeSpan[] offsets=schedule.OriginZone.GetAmbiguousTimeOffsets(local);offset=offsets[0];for(int i=1;i<offsets.Length;i++)if(offsets[i]>offset)offset=offsets[i];warning="Ambiguous local time resolved with the larger UTC offset.";}
        value=new DateTimeOffset(local,offset);return true;
    }
    /// <summary>Parses an ISO or German date without applying a culture-specific calendar.</summary>
    public static bool TryParseDateOnly(string? text,out DateOnly date)
    {date=default;if(string.IsNullOrWhiteSpace(text))return false;return DateOnly.TryParseExact(text.Trim(),["yyyy-MM-dd","dd.MM.yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.None,out date);}
    /// <summary>Parses a 24-hour time as HHmm.</summary>
    public static bool TryParseTimeOnly(string? text,out TimeOnly time)
    {time=default;if(string.IsNullOrWhiteSpace(text))return false;string value=text.Trim();return value.Length==4&&TimeOnly.TryParseExact(value,"HHmm",CultureInfo.InvariantCulture,DateTimeStyles.None,out time);}
}
