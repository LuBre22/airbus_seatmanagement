using System.Diagnostics.CodeAnalysis;

namespace SxSeatOps.Core;

/// <summary>Ref-mutable totals used for passenger load accumulation.</summary>
public struct LoadTotals
{
    /// <summary>Gets or sets passenger count.</summary>
    public int PassengerCount {get;set;}
    /// <summary>Gets or sets passenger mass in kilograms.</summary>
    public decimal PassengerMassKg {get;set;}
    /// <summary>Gets or sets baggage mass in kilograms.</summary>
    public decimal BaggageMassKg {get;set;}
}
/// <summary>Mass value for geometry calculations.</summary>
public readonly struct Mass
{
    /// <summary>Mass in kilograms.</summary>
    public decimal Kilograms {get;}
    /// <summary>Creates a nonnegative mass.</summary>
    public Mass(decimal kilograms){if(kilograms<0)throw new ArgumentOutOfRangeException(nameof(kilograms));Kilograms=kilograms;}
}
/// <summary>Longitudinal aircraft seat position and reference dimensions.</summary>
public readonly struct SeatGeometry
{
    /// <summary>Longitudinal arm in metres.</summary>
    public decimal ArmMetres {get;}
    /// <summary>Seat track offset in metres.</summary>
    public decimal TrackOffsetMetres {get;}
    /// <summary>Creates immutable seat geometry.</summary>
    public SeatGeometry(decimal armMetres,decimal trackOffsetMetres){ArmMetres=armMetres;TrackOffsetMetres=trackOffsetMetres;}
}
/// <summary>Explicit ref and in parameter demonstrations for domain values.</summary>
public static class ParameterSemantics
{
    private static readonly DateOnly DefaultFlightDate=new(2026,12,18);
    /// <summary>Adds a passenger and baggage load to a caller-owned total.</summary>
    public static void AddToLoad(ref LoadTotals totals,Passenger passenger)
    {ArgumentNullException.ThrowIfNull(passenger);totals.PassengerCount++;PassengerType type=AgeRules.Classify(passenger.BirthDate,DefaultFlightDate);totals.PassengerMassKg+=type switch{PassengerType.Infant=>BusinessRules.InfantMassKilograms,PassengerType.Child=>BusinessRules.ChildMassKilograms,_=>BusinessRules.AdultMassKilograms};totals.BaggageMassKg+=passenger.BaggageKg;}
    /// <summary>Computes the moment as mass times the combined arm.</summary>
    public static decimal ComputeMoment(in SeatGeometry geometry,in Mass mass)=>mass.Kilograms*(geometry.ArmMetres+geometry.TrackOffsetMetres);
}
/// <summary>BR-6 to BR-8 assignment eligibility helper with nullable-flow annotations.</summary>
public static class AssignmentValidator
{
    /// <summary>Returns whether the passenger is eligible and supplies an error only on failure.</summary>
    public static bool TryValidateAssignment(Passenger passenger,Seat seat,DateOnly flightDate,[NotNullWhen(false)] out string? error)
    {ArgumentNullException.ThrowIfNull(passenger);ArgumentNullException.ThrowIfNull(seat);return seat.IsEligibleFor(passenger,flightDate,out error);}
}
/// <summary>Ref-return access to a seat slot in a jagged seat array.</summary>
public static class SeatSlotAccessor
{
    /// <summary>Returns a reference to a seat slot found by row and layout column order.</summary>
    public static ref Seat? GetSlot(Seat?[][] grid,byte[] rowNumbers,IReadOnlyList<string> rowLayouts,in SeatId id)
    {
        ArgumentNullException.ThrowIfNull(grid);ArgumentNullException.ThrowIfNull(rowNumbers);ArgumentNullException.ThrowIfNull(rowLayouts);
        int rowIndex=Array.BinarySearch(rowNumbers,id.Row);if(rowIndex<0||rowIndex>=grid.Length||rowIndex>=rowLayouts.Count)throw new ArgumentOutOfRangeException(nameof(id));string layout=rowLayouts[rowIndex];int seatIndex=0;
        for(int i=0;i<layout.Length;i++)if(layout[i]!='-'){if(layout[i]==id.Column){if(seatIndex>=grid[rowIndex].Length)throw new ArgumentOutOfRangeException(nameof(id));return ref grid[rowIndex][seatIndex];}seatIndex++;}
        throw new ArgumentOutOfRangeException(nameof(id));
    }
}
