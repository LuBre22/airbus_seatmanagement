namespace SxSeatOps.Core;

/// <summary>Named domain constants shared by the flight, passenger, and cabin rules.</summary>
internal static class BusinessRules
{
    internal const byte MaximumSeatRow=99;
    internal const int NameFieldMaximumLength=30;
    internal const int InfantAgeExclusive=2;
    internal const int ChildAgeExclusive=12;
    internal const int ExitRowMinimumAge=15;
    internal const decimal MaximumBaggageKilograms=100m;
    internal const int CheckInOpeningHoursBeforeDeparture=30;
    internal const int CheckInClosingMinutesBeforeDeparture=60;
    internal const int SeatMapFreezeMinutesBeforeDeparture=45;
    internal const long MaximumManifestBytes=5L*1024L*1024L;
    internal const int MaximumCommandCharacters=4096;
    internal const decimal AdultMassKilograms=StandardPassengerMass.AdultKilograms;
    internal const decimal ChildMassKilograms=StandardPassengerMass.ChildKilograms;
    internal const decimal InfantMassKilograms=StandardPassengerMass.InfantKilograms;
    internal const decimal EconomyToPremiumFee=299m;
    internal const decimal EconomyToBusinessFee=899m;
    internal const decimal PremiumToBusinessFee=649m;
    internal const decimal SilverDiscount=0.075m;
    internal const decimal GoldDiscount=0.125m;
}

/// <summary>Published fictive standard passenger masses for SX 0350 load reports.</summary>
public static class StandardPassengerMass
{
    /// <summary>Adult mass in kilograms.</summary>
    public const decimal AdultKilograms=84.0m;
    /// <summary>Child mass in kilograms.</summary>
    public const decimal ChildKilograms=35.0m;
    /// <summary>Infant mass in kilograms.</summary>
    public const decimal InfantKilograms=10.0m;
}
