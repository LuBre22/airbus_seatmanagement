using System.Globalization;

namespace SxSeatOps.Core;

/// <summary>Immutable flight time domain with explicit airport time zones.</summary>
public sealed class FlightSchedule
{
    /// <summary>Flight identifier.</summary>
    public string FlightNumber { get; }
    /// <summary>Origin IATA code.</summary>
    public string Origin { get; }
    /// <summary>Destination IATA code.</summary>
    public string Destination { get; }
    /// <summary>Local scheduled departure instant.</summary>
    public DateTimeOffset Std { get; }
    /// <summary>Local scheduled arrival instant.</summary>
    public DateTimeOffset Sta { get; }
    /// <summary>Origin timezone.</summary>
    public TimeZoneInfo OriginZone { get; }
    /// <summary>Destination timezone.</summary>
    public TimeZoneInfo DestinationZone { get; }
    /// <summary>Optional ambiguity warning from local-time conversion.</summary>
    public string? Warning { get; }
    /// <summary>Gets departure as UTC.</summary>
    public DateTimeOffset StdUtc=>Std.ToUniversalTime();
    /// <summary>Gets arrival as UTC.</summary>
    public DateTimeOffset StaUtc=>Sta.ToUniversalTime();
    /// <summary>Gets elapsed block time.</summary>
    public TimeSpan BlockTime=>StaUtc-StdUtc;
    /// <summary>Gets the opening instant of check-in.</summary>
    public DateTimeOffset CheckInOpens=>Std.AddHours(-BusinessRules.CheckInOpeningHoursBeforeDeparture);
    /// <summary>Gets the exclusive closing instant of check-in.</summary>
    public DateTimeOffset CheckInCloses=>Std.AddMinutes(-BusinessRules.CheckInClosingMinutesBeforeDeparture);
    /// <summary>Gets the inclusive seat-map freeze instant.</summary>
    public DateTimeOffset SeatMapFreeze=>Std.AddMinutes(-BusinessRules.SeatMapFreezeMinutesBeforeDeparture);
    /// <summary>Gets local departure date.</summary>
    public DateOnly LocalDepartureDate=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Std,OriginZone).DateTime);
    /// <summary>Gets departure weekday.</summary>
    public DayOfWeek DepartureWeekday=>TimeZoneInfo.ConvertTime(Std,OriginZone).DayOfWeek;
    /// <summary>Gets arrival weekday.</summary>
    public DayOfWeek ArrivalWeekday=>TimeZoneInfo.ConvertTime(Sta,DestinationZone).DayOfWeek;
    private FlightSchedule(string number,string origin,string destination,DateTimeOffset std,DateTimeOffset sta,TimeZoneInfo originZone,TimeZoneInfo destinationZone,string? warning)
    { FlightNumber=number; Origin=origin; Destination=destination; Std=std; Sta=sta; OriginZone=originZone; DestinationZone=destinationZone; Warning=warning; }
    /// <summary>Creates a schedule from local wall-clock times, rejecting invalid local times.</summary>
    public static bool TryCreate(string number,string origin,string destination,DateTime departureLocal,string originZoneId,DateTime arrivalLocal,string destinationZoneId,out FlightSchedule? schedule,out string? error)
    {
        schedule=null; error=null;
        try
        {
            TimeZoneInfo oz=TimeZoneInfo.FindSystemTimeZoneById(originZoneId), dz=TimeZoneInfo.FindSystemTimeZoneById(destinationZoneId);
            if(departureLocal.Kind!=DateTimeKind.Unspecified) departureLocal=DateTime.SpecifyKind(departureLocal,DateTimeKind.Unspecified);
            if(arrivalLocal.Kind!=DateTimeKind.Unspecified) arrivalLocal=DateTime.SpecifyKind(arrivalLocal,DateTimeKind.Unspecified);
            if(oz.IsInvalidTime(departureLocal)||dz.IsInvalidTime(arrivalLocal)) { error="A local time does not exist because of a daylight-saving transition."; return false; }
            string? warning=null;
            DateTimeOffset std=Resolve(departureLocal,oz,ref warning); DateTimeOffset sta=Resolve(arrivalLocal,dz,ref warning);
            if(sta<=std) { error="Arrival must be after departure."; return false; }
            schedule=new FlightSchedule(number,origin,destination,std,sta,oz,dz,warning); return true;
        }
        catch(TimeZoneNotFoundException) { error="A timezone identifier was not found on this platform."; return false; }
        catch(InvalidTimeZoneException) { error="A timezone definition is invalid."; return false; }
    }
    /// <summary>Creates the specified training flight.</summary>
    public static FlightSchedule CreateDefault()
    {
        bool ok=TryCreate("SX0350","MUC","SIN",new DateTime(2026,12,18,22,5,0),"Europe/Berlin",new DateTime(2026,12,19,17,35,0),"Asia/Singapore",out var value,out var error);
        return ok ? value! : throw new InvalidOperationException(error);
    }
    /// <summary>Tests whether an instant lies in the half-open check-in window.</summary>
    public bool IsCheckInOpen(DateTimeOffset moment)=>moment>=CheckInOpens && moment<CheckInCloses;
    /// <summary>Tests whether the seat map has frozen.</summary>
    public bool IsSeatMapFrozen(DateTimeOffset moment)=>moment>=SeatMapFreeze;
    private static DateTimeOffset Resolve(DateTime local,TimeZoneInfo zone,ref string? warning)
    {
        if(!zone.IsAmbiguousTime(local)) return new DateTimeOffset(local,zone.GetUtcOffset(local));
        TimeSpan[] offsets=zone.GetAmbiguousTimeOffsets(local); TimeSpan chosen=offsets[0];
        for(int i=1;i<offsets.Length;i++) if(offsets[i]>chosen) chosen=offsets[i];
        warning=$"Ambiguous local time {local:O} in {zone.Id}; larger UTC offset {chosen} selected (earlier instant).";
        return new DateTimeOffset(local,chosen);
    }
}

/// <summary>Deterministic clock for repeatable scenarios and tests.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    /// <summary>Creates a manual clock at the supplied instant.</summary>
    public ManualTimeProvider(DateTimeOffset initialTime) { _utcNow=initialTime.ToUniversalTime(); }
    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow()=>_utcNow;
    /// <summary>Sets the clock to an instant.</summary>
    public void SetUtcNow(DateTimeOffset instant)=>_utcNow=instant.ToUniversalTime();
    /// <summary>Advances the clock by a duration.</summary>
    public void Advance(TimeSpan amount)=>_utcNow=_utcNow.Add(amount);
}

/// <summary>Strict culture-invariant and explicit-culture number parsers.</summary>
public static class NumberParser
{
    /// <summary>Parses nonnegative de-DE miles with optional thousands separators.</summary>
    public static bool TryParseMiles(string? text,out long value)=>TryLong(text,"de-DE",NumberStyles.AllowThousands,out value);
    /// <summary>Parses baggage kilograms using German decimal notation.</summary>
    public static bool TryParseBaggageGerman(string? text,out decimal value)=>TryDecimal(text,"de-DE",out value);
    /// <summary>Parses baggage kilograms using invariant decimal notation.</summary>
    public static bool TryParseBaggageInvariant(string? text,out decimal value)=>TryDecimal(text,"",out value);
    /// <summary>Parses a nonnegative ASCII row number.</summary>
    public static bool TryParseRow(string? text,out byte value) { value=0; return text is not null && byte.TryParse(text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out value) && value>0 && IsAsciiDigits(text.Trim()); }
    /// <summary>Parses a nonnegative ASCII count.</summary>
    public static bool TryParseCount(string? text,out int value) { value=0; return text is not null && int.TryParse(text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out value) && IsAsciiDigits(text.Trim()); }
    private static bool TryLong(string? text,string culture,NumberStyles style,out long value) { value=0; if(!ValidateText(text)) return false; return long.TryParse(text!.Trim(),style,CultureInfo.GetCultureInfo(culture),out value) && value>=0; }
    private static bool TryDecimal(string? text,string culture,out decimal value) { value=0; if(!ValidateText(text)) return false; NumberFormatInfo n=culture.Length==0?CultureInfo.InvariantCulture.NumberFormat:CultureInfo.GetCultureInfo(culture).NumberFormat; NumberStyles style=culture.Length==0?NumberStyles.AllowDecimalPoint:NumberStyles.Number; return decimal.TryParse(text!.Trim(),style,n,out value) && value>=0; }
    private static bool ValidateText(string? text) { if(string.IsNullOrWhiteSpace(text)) return false; string t=text.Trim(); if(t.Contains('e')||t.Contains('E')||t.Contains('€')) return false; foreach(char c in t){if(char.IsWhiteSpace(c)||c is '+' or '-')return false;if(char.IsDigit(c)&&(c<'0'||c>'9'))return false;} return true; }
    private static bool IsAsciiDigits(string value) { if(value.Length==0)return false; foreach(char c in value) if(c is < '0' or > '9')return false; return true; }
}
