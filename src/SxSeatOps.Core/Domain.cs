using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SxSeatOps.Core;

/// <summary>Aircraft cabin classification, ordered from highest to lowest cabin.</summary>
public enum CabinClass : byte { Business = 0, PremiumEconomy = 1, Economy = 2 }
/// <summary>Current state of an individual seat.</summary>
public enum SeatStatus : byte { Available = 0, Blocked = 1, Occupied = 2 }
/// <summary>Frequent-flyer status; higher values represent higher priority.</summary>
public enum LoyaltyTier : byte { None = 0, Silver = 1, Gold = 2, Platinum = 3 }
/// <summary>Passenger age category on the local departure date.</summary>
public enum PassengerType : byte { Infant = 0, Child = 1, Adult = 2 }
/// <summary>Business reason for an operational seat block.</summary>
public enum BlockReason : byte { CrewRest = 0, Technical = 1, WeightBalance = 2, Security = 3, Medical = 4 }
/// <summary>Flags describing a seat's location and facilities.</summary>
[Flags] public enum SeatAttributes : ushort { None=0, Window=1, Aisle=2, ExitRow=4, ExtraLegroom=8, Bassinet=16, NearGalley=32, NearLavatory=64, ExitExtraLegroom=ExitRow|ExtraLegroom }
/// <summary>Operational permissions assigned to a user.</summary>
[Flags] public enum OperatorRole : ushort { None=0, Occ=1, Station=2, Revenue=4, Purser=8, Compliance=16, Supervisor=32, Operations=Occ|Station, All=Occ|Station|Revenue|Purser|Compliance|Supervisor }

/// <summary>Stable, immutable identifier for a seat in a cabin.</summary>
public readonly struct SeatId : IEquatable<SeatId>, IComparable<SeatId>
{
    private const string Columns = "ABCDEGHJK";
    /// <summary>Gets the row number.</summary>
    public byte Row { get; }
    /// <summary>Gets the seat column.</summary>
    public char Column { get; }
    /// <summary>Creates a seat identifier from a row and column.</summary>
    public SeatId(byte row, char column)
    {
        if (row is < 1 or > BusinessRules.MaximumSeatRow) throw new ArgumentOutOfRangeException(nameof(row));
        if (Columns.IndexOf(column) < 0) throw new ArgumentOutOfRangeException(nameof(column));
        Row = row; Column = column;
    }
    /// <summary>Parses an exact seat designator such as 12A.</summary>
    public static bool TryParse(string? text, out SeatId id)
    {
        id = default;
        if (string.IsNullOrEmpty(text)) return false;
        int i = 0; int row = 0;
        while (i < text.Length && text[i] is >= '0' and <= '9') { if(row>99)return false; row = row * 10 + text[i] - '0'; if(row>99)return false; i++; }
        if (i < 1 || i >= text.Length || i != text.Length - 1 || text[0] == '0' || row is < 1 or > BusinessRules.MaximumSeatRow) return false;
        char col = text[i]; if (Columns.IndexOf(col) < 0) return false;
        id = new SeatId((byte)row, col); return true;
    }
    /// <summary>Creates an identifier or throws for invalid input.</summary>
    public static SeatId Create(int row, char column) { if(row is <1 or >BusinessRules.MaximumSeatRow)throw new ArgumentOutOfRangeException(nameof(row)); return new SeatId((byte)row,column); }
    /// <summary>Creates an identifier from exact text or throws an argument exception.</summary>
    public static SeatId Create(string text) => TryParse(text, out SeatId id) ? id : throw new ArgumentException("Invalid seat designator.", nameof(text));
    /// <summary>Compares by row, then column in layout order.</summary>
    public int CompareTo(SeatId other) { int r = Row.CompareTo(other.Row); return r != 0 ? r : Columns.IndexOf(Column).CompareTo(Columns.IndexOf(other.Column)); }
    /// <inheritdoc/>
    public bool Equals(SeatId other) => Row == other.Row && Column == other.Column;
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SeatId other && Equals(other);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Row, Column);
    /// <inheritdoc/>
    public override string ToString() => Row.ToString(CultureInfo.InvariantCulture) + Column;
    /// <summary>Tests identifier equality.</summary>
    public static bool operator ==(SeatId left, SeatId right) => left.Equals(right);
    /// <summary>Tests identifier inequality.</summary>
    public static bool operator !=(SeatId left, SeatId right) => !left.Equals(right);
    /// <summary>Tests ordering.</summary>
    public static bool operator <(SeatId left, SeatId right) => left.CompareTo(right) < 0;
    /// <summary>Tests ordering.</summary>
    public static bool operator >(SeatId left, SeatId right) => left.CompareTo(right) > 0;
    /// <summary>Tests ordering.</summary>
    public static bool operator <=(SeatId left, SeatId right) => left.CompareTo(right) <= 0;
    /// <summary>Tests ordering.</summary>
    public static bool operator >=(SeatId left, SeatId right) => left.CompareTo(right) >= 0;
}

/// <summary>Named conversions and flag helpers for the domain enums.</summary>
public static class EnumExtensions
{
    /// <summary>Gets the IATA cabin letter.</summary>
    public static char ToIataCode(this CabinClass cabin) => cabin switch { CabinClass.Business=>'J', CabinClass.PremiumEconomy=>'W', CabinClass.Economy=>'Y', _=>throw new ArgumentOutOfRangeException(nameof(cabin)) };
    /// <summary>Parses an IATA cabin letter without culture-sensitive behavior.</summary>
    public static bool TryParseCabinCode(string? code, out CabinClass cabin)
    { cabin=default; if(code is null || code.Length!=1) return false; cabin=char.ToUpperInvariant(code[0]) switch {'J'=>CabinClass.Business,'W'=>CabinClass.PremiumEconomy,'Y'=>CabinClass.Economy,_=>(CabinClass)255}; return Enum.IsDefined(cabin); }
    /// <summary>Gets the operational block code.</summary>
    public static string ToCode(this BlockReason reason) => reason switch { BlockReason.CrewRest=>"CREWREST", BlockReason.Technical=>"TECH", BlockReason.WeightBalance=>"WB", BlockReason.Security=>"SEC", BlockReason.Medical=>"MED", _=>throw new ArgumentOutOfRangeException(nameof(reason)) };
    /// <summary>Lists all block reasons and their stable operational codes.</summary>
    public static IReadOnlyList<string> GetBlockReasonCodes()
    {var result=new List<string>();foreach(BlockReason reason in Enum.GetValues<BlockReason>())result.Add(reason.ToString()+"="+reason.ToCode());return result.AsReadOnly();}
    /// <summary>Parses an operational block code.</summary>
    public static bool TryParseCode(string? code, out BlockReason reason)
    { reason=default; if(code is null) return false; foreach(BlockReason candidate in Enum.GetValues<BlockReason>()) if(string.Equals(candidate.ToCode(),code,StringComparison.OrdinalIgnoreCase)){reason=candidate;return true;} return false; }
    /// <summary>Returns whether all specified seat flags are set.</summary>
    public static bool HasAll(this SeatAttributes value, SeatAttributes flags) => (value & flags) == flags;
    /// <summary>Returns whether at least one specified seat flag is set.</summary>
    public static bool HasAny(this SeatAttributes value, SeatAttributes flags) => (value & flags) != SeatAttributes.None;
    /// <summary>Sets the specified seat flags.</summary>
    public static SeatAttributes Set(this SeatAttributes value, SeatAttributes flags) => value | flags;
    /// <summary>Clears the specified seat flags.</summary>
    public static SeatAttributes Clear(this SeatAttributes value, SeatAttributes flags) => value & ~flags;
    /// <summary>Toggles the specified seat flags.</summary>
    public static SeatAttributes Toggle(this SeatAttributes value, SeatAttributes flags) => value ^ flags;
    /// <summary>Culture-independent parser for non-flags enums.</summary>
    public static bool TryParseEnum<T>(string? text, out T value) where T : struct, Enum
    {
        value=default;
        if(string.IsNullOrWhiteSpace(text)) return false;
        string trimmed=text.Trim();
        if(trimmed.Length==0 || trimmed[0] is >= '0' and <= '9' || trimmed[0]=='-' || trimmed[0]=='+') return false;
        if(!Enum.TryParse(trimmed,true,out T parsed) || !Enum.IsDefined(parsed)) return false;
        value=parsed; return true;
    }
    private static bool IsNumericEnumToken(string token)=>token[0] is >= '0' and <= '9' or '+' or '-';
    /// <summary>Parses comma-separated operational role names and defined combinations.</summary>
    public static bool TryParseRoles(string? text,out OperatorRole roles)
    {
        roles=OperatorRole.None;if(string.IsNullOrWhiteSpace(text))return false;string[] parts=text.Split(',');OperatorRole parsed=OperatorRole.None;
        foreach(string part in parts){string token=part.Trim();if(token.Length==0||IsNumericEnumToken(token)||token.Contains('|')||!Enum.TryParse(token,true,out OperatorRole role)||!Enum.IsDefined(role)||role==OperatorRole.None)return false;parsed|=role;}
        roles=parsed;return true;
    }
    /// <summary>Parses comma-separated, defined flags names; numeric forms and other separators are rejected.</summary>
    public static bool TryParseSeatAttributes(string? text, out SeatAttributes value)
    {
        value=SeatAttributes.None; if(string.IsNullOrWhiteSpace(text)) return false;
        string[] parts=text.Split(','); SeatAttributes parsed=SeatAttributes.None;
        foreach(string part in parts) { string token=part.Trim(); if(token.Length==0 || IsNumericEnumToken(token) || token.Contains('|') || !Enum.TryParse(token,true,out SeatAttributes flag) || !Enum.IsDefined(flag) || flag==SeatAttributes.None&&parts.Length>1) return false; parsed |= flag; }
        value=parsed; return true;
    }
}

/// <summary>Normalized passenger name in airline format.</summary>
public static class NameField
{
    /// <summary>Normalizes names to SURNAME/GIVEN TITLE; reports replacement of unsupported characters.</summary>
    public static bool TryFormat(string? surname, string? givenNames, string? title, out string? result, out bool replaced)
    {
        result=null; replaced=false;
        if(surname is null || givenNames is null) return false;
        string family=Normalize(surname, ref replaced); string given=Normalize(givenNames, ref replaced); string honorific=Normalize(title ?? string.Empty, ref replaced);
        if(family.Length==0 || given.Length==0) return false;
        string suffix=honorific.Length==0 ? "" : " "+honorific;
        if(1+1+1+suffix.Length>BusinessRules.NameFieldMaximumLength)suffix="";
        int maximumFamily=BusinessRules.NameFieldMaximumLength-1-1-suffix.Length;
        if(family.Length>maximumFamily)family=family[..maximumFamily];
        int givenRoom=BusinessRules.NameFieldMaximumLength-family.Length-1-suffix.Length;
        if(given.Length>givenRoom)given=given[..givenRoom].TrimEnd();
        if(given.Length==0)given=givenNames.Trim().Length>0?"X":"";
        if(given.Length==0)return false;
        result=family+"/"+given+suffix; return true;
    }
    /// <summary>Overload that omits the replacement indicator.</summary>
    public static bool TryFormat(string? surname,string? givenNames,string? title,out string? result) => TryFormat(surname,givenNames,title,out result,out _);
    /// <summary>Splits a normalized name field into components.</summary>
    public static bool TryParse(string? text,out string? surname,out string? givenNames,out string? title)
    {
        surname=null; givenNames=null; title=null; if(string.IsNullOrWhiteSpace(text)) return false;
        string v=text.Trim(); int slash=v.IndexOf('/'); if(slash<=0 || slash!=v.LastIndexOf('/')) return false;
        int space=v.IndexOf(' ',slash+1); surname=v[..slash]; givenNames=space<0?v[(slash+1)..]:v[(slash+1)..space]; title=space<0?null:v[(space+1)..];
        return givenNames.Length>0;
    }
    private static string Normalize(string input,ref bool replaced)
    {
        string s=input.Trim().Replace("Ä","AE",StringComparison.OrdinalIgnoreCase).Replace("Ö","OE",StringComparison.OrdinalIgnoreCase).Replace("Ü","UE",StringComparison.OrdinalIgnoreCase).Replace("ß","SS",StringComparison.OrdinalIgnoreCase).Normalize(NormalizationForm.FormD);
        var b=new StringBuilder(s.Length); bool previousSpace=false;
        foreach(char c in s) { UnicodeCategory cat=char.GetUnicodeCategory(c); if(cat==UnicodeCategory.NonSpacingMark) continue; char u=char.ToUpperInvariant(c); if(u is '-' or '\'' or '’') continue; if(u==' ') { if(!previousSpace) b.Append(' '); previousSpace=true; continue; } previousSpace=false; if(u is >= 'A' and <= 'Z') b.Append(u); else { b.Append('X'); replaced=true; } }
        return b.ToString().Trim();
    }
}

/// <summary>Immutable passenger manifest row.</summary>
public sealed class Passenger
{
    /// <summary>Six-character reservation code.</summary>
    public string Pnr { get; }
    /// <summary>Normalized name field.</summary>
    public string NameField { get; }
    /// <summary>Date of birth.</summary>
    public DateOnly BirthDate { get; }
    /// <summary>Initially booked cabin.</summary>
    public CabinClass BookedCabin { get; }
    /// <summary>Current cabin entitlement.</summary>
    public CabinClass EntitledCabin { get; internal set; }
    /// <summary>Loyalty tier.</summary>
    public LoyaltyTier Tier { get; }
    /// <summary>Frequent-flyer miles.</summary>
    public long Miles { get; }
    /// <summary>Checked baggage mass in kilograms.</summary>
    public decimal BaggageKg { get; }
    /// <summary>Check-in instant.</summary>
    public DateTimeOffset CheckIn { get; }
    /// <summary>Ticket number.</summary>
    public string Ticket { get; }
    /// <summary>Whether the passenger has reduced mobility.</summary>
    public bool HasReducedMobility { get; }
    /// <summary>Creates a passenger after validating basic data invariants.</summary>
    public Passenger(string pnr,string nameField,DateOnly birthDate,CabinClass bookedCabin,LoyaltyTier tier,long miles,decimal baggageKg,DateTimeOffset checkIn,string ticket,bool hasReducedMobility=false)
    {
        ArgumentNullException.ThrowIfNull(pnr); ArgumentNullException.ThrowIfNull(nameField); ArgumentNullException.ThrowIfNull(ticket);
        if(!Validators.IsValidPnr(pnr)) throw new ArgumentException("PNR must have six permitted characters.",nameof(pnr));
        if(miles<0 || baggageKg<0 || baggageKg>BusinessRules.MaximumBaggageKilograms) throw new ArgumentOutOfRangeException(nameof(miles));
        Pnr=pnr; NameField=nameField; BirthDate=birthDate; BookedCabin=bookedCabin; EntitledCabin=bookedCabin; Tier=tier; Miles=miles; BaggageKg=baggageKg; CheckIn=checkIn; Ticket=ticket; HasReducedMobility=hasReducedMobility;
    }
}

/// <summary>Age policy applied on the local departure date.</summary>
public static class AgeRules
{
    /// <summary>Calculates completed years, treating 29 February birthdays as 1 March in non-leap years.</summary>
    public static int AgeInYears(DateOnly birthDate,DateOnly reference)
    {
        int year=reference.Year; int month=birthDate.Month; int day=birthDate.Day;
        if(month==2 && day==29 && !DateTime.IsLeapYear(year)) { month=3; day=1; }
        int age=reference.Year-birthDate.Year; if(reference.Month<month || reference.Month==month && reference.Day<day) age--; return age;
    }
    /// <summary>Classifies passenger age using the published thresholds.</summary>
    public static PassengerType Classify(DateOnly birthDate,DateOnly reference) { int age=AgeInYears(birthDate,reference); return age<BusinessRules.InfantAgeExclusive?PassengerType.Infant:age<BusinessRules.ChildAgeExclusive?PassengerType.Child:PassengerType.Adult; }
}

/// <summary>Domain validators for reservation and ticket identifiers.</summary>
public static partial class Validators
{
    /// <summary>Checks six permitted ASCII characters for a PNR.</summary>
    public static bool IsValidPnr(string? value) => value is not null && PnrRegex().IsMatch(value);
    /// <summary>Checks the three-digit airline code, hyphen, and ten ASCII ticket digits.</summary>
    public static bool IsValidTicketNumber(string? value) => value is not null && TicketRegex().IsMatch(value);
    /// <summary>Checks seat designator syntax (existence is checked by the layout).</summary>
    public static bool IsValidSeatDesignator(string? value) => SeatId.TryParse(value,out _);
    [GeneratedRegex("\\A[A-HJ-NP-Z2-9]{6}\\z",RegexOptions.CultureInvariant)] private static partial Regex PnrRegex();
    [GeneratedRegex("\\A[0-9]{3}-[0-9]{10}\\z",RegexOptions.CultureInvariant)] private static partial Regex TicketRegex();
}
