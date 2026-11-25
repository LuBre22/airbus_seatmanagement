namespace SxSeatOps.Core;

/// <summary>Abstract seat with immutable identity and controlled operational status.</summary>
public abstract class Seat : IEquatable<Seat>
{
    /// <summary>Gets the unique seat identifier.</summary>
    public SeatId Id { get; }
    /// <summary>Gets derived seat features.</summary>
    public SeatAttributes Attributes { get; }
    /// <summary>Gets the current operational status.</summary>
    public SeatStatus Status { get; internal set; }
    /// <summary>Gets the cabin category.</summary>
    public abstract CabinClass Cabin { get; }
    /// <summary>Creates a seat.</summary>
    protected Seat(SeatId id,SeatAttributes attributes) { Id=id; Attributes=attributes; Status=SeatStatus.Available; }
    /// <summary>Determines whether a passenger may occupy this seat.</summary>
    public virtual bool IsEligibleFor(Passenger passenger,DateOnly flightDate,out string? reason)
    {
        ArgumentNullException.ThrowIfNull(passenger); reason=null;
        if(AgeRules.Classify(passenger.BirthDate,flightDate)==PassengerType.Infant) { reason="Infants cannot occupy a seat."; return false; }
        if(passenger.EntitledCabin!=Cabin) { reason="Passenger is not entitled to this cabin."; return false; }
        if(Attributes.HasAll(SeatAttributes.ExitRow) && (AgeRules.AgeInYears(passenger.BirthDate,flightDate)<BusinessRules.ExitRowMinimumAge || passenger.HasReducedMobility)) { reason="Exit-row seats require an adult aged at least 15 without reduced mobility."; return false; }
        if(passenger.HasReducedMobility && (!Attributes.HasAll(SeatAttributes.Aisle) || Attributes.HasAll(SeatAttributes.ExitRow))) { reason="Reduced-mobility passengers require a non-exit aisle seat."; return false; }
        return true;
    }
    /// <summary>Describes the seat and its current state.</summary>
    public virtual string Describe() => $"{Id} [{Cabin} | {Attributes}] {Status}";
    /// <inheritdoc/>
    public override string ToString()=>Describe();
    /// <inheritdoc/>
    public bool Equals(Seat? other)=>other is not null && Id.Equals(other.Id);
    /// <inheritdoc/>
    public override bool Equals(object? obj)=>obj is Seat other && Equals(other);
    /// <inheritdoc/>
    public override int GetHashCode()=>Id.GetHashCode();
}
/// <summary>Business cabin seat.</summary>
public sealed class BusinessSeat(SeatId id,SeatAttributes attributes) : Seat(id,attributes)
{
    /// <inheritdoc/>
    public override CabinClass Cabin=>CabinClass.Business;
}
/// <summary>Premium economy cabin seat.</summary>
public sealed class PremiumEconomySeat(SeatId id,SeatAttributes attributes) : Seat(id,attributes)
{
    /// <inheritdoc/>
    public override CabinClass Cabin=>CabinClass.PremiumEconomy;
}
/// <summary>Economy cabin seat.</summary>
public class EconomySeat(SeatId id,SeatAttributes attributes) : Seat(id,attributes)
{
    /// <inheritdoc/>
    public override CabinClass Cabin=>CabinClass.Economy;
}
/// <summary>Economy exit-row seat with the additional exit restriction.</summary>
public sealed class ExitRowSeat(SeatId id,SeatAttributes attributes) : EconomySeat(id,attributes)
{
    /// <inheritdoc/>
    public sealed override bool IsEligibleFor(Passenger passenger,DateOnly flightDate,out string? reason)
    {
        if(AgeRules.AgeInYears(passenger.BirthDate,flightDate)<BusinessRules.ExitRowMinimumAge || passenger.HasReducedMobility) { reason="Exit-row seats require an adult aged at least 15 without reduced mobility."; return false; }
        return base.IsEligibleFor(passenger,flightDate,out reason);
    }
}
/// <summary>Factory selecting the concrete seat type from cabin and attributes.</summary>
public static class SeatFactory
{
    /// <summary>Creates the correct seat implementation.</summary>
    public static Seat Create(CabinClass cabin,SeatId id,SeatAttributes attributes) => cabin switch
    { CabinClass.Business=>new BusinessSeat(id,attributes), CabinClass.PremiumEconomy=>new PremiumEconomySeat(id,attributes), CabinClass.Economy when attributes.HasAll(SeatAttributes.ExitRow)=>new ExitRowSeat(id,attributes), CabinClass.Economy=>new EconomySeat(id,attributes), _=>throw new ArgumentOutOfRangeException(nameof(cabin)) };
}
