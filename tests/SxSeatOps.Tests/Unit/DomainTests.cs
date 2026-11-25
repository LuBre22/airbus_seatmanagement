using System.Globalization;
using FluentAssertions;
using SxSeatOps.Core;
using Xunit;

namespace SxSeatOps.Tests.Unit;

public sealed class DomainTests
{
    [Theory]
    [InlineData("1A", 1, 'A')]
    [InlineData("12K", 12, 'K')]
    [InlineData("99J", 99, 'J')]
    public void SeatId_parses_permitted_designators(string text, byte row, char column)
    {
        SeatId.TryParse(text, out SeatId id).Should().BeTrue();
        id.Row.Should().Be(row);
        id.Column.Should().Be(column);
        id.ToString().Should().Be(text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01A")]
    [InlineData("0A")]
    [InlineData("100A")]
    [InlineData("1F")]
    [InlineData("1a")]
    [InlineData("1AA")]
    [InlineData(" A")]
    public void SeatId_rejects_malformed_designators(string? text) =>
        SeatId.TryParse(text, out _).Should().BeFalse();

    [Fact]
    public void SeatId_has_value_equality_and_layout_order()
    {
        SeatId.Create("1A").Should().Be(SeatId.Create("1A"));
        SeatId.Create("1A").Should().NotBe(SeatId.Create("2A"));
        (SeatId.Create("1A") < SeatId.Create("1D")).Should().BeTrue();
    }

    [Theory]
    [InlineData("J", CabinClass.Business)]
    [InlineData("w", CabinClass.PremiumEconomy)]
    [InlineData("Y", CabinClass.Economy)]
    public void Cabin_codes_are_case_insensitive(string code, CabinClass cabin)
    {
        EnumExtensions.TryParseCabinCode(code, out CabinClass actual).Should().BeTrue();
        actual.Should().Be(cabin);
        cabin.ToIataCode().Should().Be(code.ToUpperInvariant()[0]);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("999")]
    [InlineData("")]
    [InlineData("Economy!")]
    public void Enum_parser_rejects_numeric_and_unknown_values(string text) =>
        EnumExtensions.TryParseEnum<CabinClass>(text, out _).Should().BeFalse();

    [Fact]
    public void Flag_parser_accepts_comma_list_and_round_trips()
    {
        SeatAttributes combination = SeatAttributes.Window | SeatAttributes.Aisle;
        EnumExtensions.TryParseSeatAttributes("window, aisle", out SeatAttributes value).Should().BeTrue();
        value.Should().Be(combination);
        EnumExtensions.TryParseSeatAttributes(combination.ToString(), out SeatAttributes reparsed).Should().BeTrue();
        reparsed.Should().Be(combination);
        EnumExtensions.TryParseSeatAttributes("Window | Aisle", out _).Should().BeFalse();
        EnumExtensions.TryParseSeatAttributes("9999", out _).Should().BeFalse();
        EnumExtensions.TryParseSeatAttributes("1", out _).Should().BeFalse();
    }

    [Fact]
    public void Flag_operations_do_not_use_hasflag_semantics_for_none()
    {
        SeatAttributes.None.HasAny(SeatAttributes.Window).Should().BeFalse();
        (SeatAttributes.Window | SeatAttributes.Aisle).HasAll(SeatAttributes.Window | SeatAttributes.Aisle).Should().BeTrue();
        (SeatAttributes.Window | SeatAttributes.Aisle).Clear(SeatAttributes.Window).Should().Be(SeatAttributes.Aisle);
    }

    [Fact]
    public void Loyalty_order_is_meaningful() =>
        ((int)LoyaltyTier.Gold).Should().BeGreaterThan((int)LoyaltyTier.Silver);

    [Theory]
    [InlineData("Müller", "Hans", "MUELLER/HANS")]
    [InlineData("Ångström", "Ola", "ANGSTROEM/OLA")]
    [InlineData("Çelik", "Ece", "CELIK/ECE")]
    [InlineData("O'Brien", "Mary", "OBRIEN/MARY")]
    [InlineData("Hans-Peter", "Anna", "HANSPETER/ANNA")]
    [InlineData("Straße", "Eva", "STRASSE/EVA")]
    public void Names_are_transliterated(string family, string given, string expected)
    {
        NameField.TryFormat(family, given, null, out string? formatted).Should().BeTrue();
        formatted.Should().Be(expected);
    }

    [Fact]
    public void Unsupported_name_characters_are_reported()
    {
        NameField.TryFormat("Łukasz", "Wei", null, out string? result, out bool changed).Should().BeTrue();
        changed.Should().BeTrue();
        result.Should().Contain("X");
    }

    [Fact]
    public void Name_parser_rejects_multiple_slashes_and_empty_components()
    {
        NameField.TryParse("A/B/C", out _, out _, out _).Should().BeFalse();
        NameField.TryFormat("  ", "A", null, out _).Should().BeFalse();
    }

    [Fact]
    public void Name_format_is_culture_invariant_in_turkish()
    {
        CultureInfo old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            NameField.TryFormat("inci", "ilker", null, out string? name).Should().BeTrue();
            name.Should().Be("INCI/ILKER");
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    [Theory]
    [InlineData("1.234.567", true)]
    [InlineData("9223372036854775808", false)]
    [InlineData("1,234", false)]
    [InlineData("١٢٣", false)]
    [InlineData("1e3", false)]
    [InlineData("-3", false)]
    [InlineData("", false)]
    public void Miles_parser_is_safe(string value, bool expected) =>
        NumberParser.TryParseMiles(value, out _).Should().Be(expected);

    [Fact]
    public void Decimal_parser_is_explicit_about_culture()
    {
        NumberParser.TryParseBaggageGerman("23,5", out decimal german).Should().BeTrue();
        german.Should().Be(23.5m);
        NumberParser.TryParseBaggageInvariant("23.5", out decimal invariant).Should().BeTrue();
        invariant.Should().Be(23.5m);
        NumberParser.TryParseBaggageGerman("1.234", out decimal germanThousands).Should().BeTrue();
        germanThousands.Should().Be(1234m);
        NumberParser.TryParseBaggageInvariant("1.234", out decimal invariantDecimal).Should().BeTrue();
        invariantDecimal.Should().Be(1.234m);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("255", true)]
    [InlineData("256", false)]
    [InlineData("٣", false)]
    [InlineData("+1", false)]
    [InlineData("1 2", false)]
    public void Byte_parser_accepts_only_ascii_range(string value, bool expected) =>
        NumberParser.TryParseRow(value, out _).Should().Be(expected);

    [Fact]
    public void Age_rules_use_completed_years_and_leap_day_march_rule()
    {
        var reference = new DateOnly(2026, 3, 1);
        AgeRules.AgeInYears(new DateOnly(2008, 2, 29), reference).Should().Be(18);
        AgeRules.Classify(new DateOnly(2024, 12, 19), new DateOnly(2026, 12, 18)).Should().Be(PassengerType.Infant);
    }

    [Fact]
    public void Flight_schedule_has_12_hour_30_minute_block_time()
    {
        FlightSchedule schedule = FlightSchedule.CreateDefault();
        schedule.BlockTime.Should().Be(TimeSpan.FromHours(12.5));
        schedule.LocalDepartureDate.Should().Be(new DateOnly(2026, 12, 18));
    }

    [Fact]
    public void Checkin_window_is_half_open_and_freeze_is_inclusive()
    {
        FlightSchedule schedule = FlightSchedule.CreateDefault();
        schedule.IsCheckInOpen(schedule.CheckInOpens).Should().BeTrue();
        schedule.IsCheckInOpen(schedule.CheckInCloses).Should().BeFalse();
        schedule.IsSeatMapFrozen(schedule.SeatMapFreeze.AddSeconds(-1)).Should().BeFalse();
        schedule.IsSeatMapFrozen(schedule.SeatMapFreeze).Should().BeTrue();
    }

    [Fact]
    public void Daylight_saving_invalid_local_time_is_rejected()
    {
        bool ok = FlightSchedule.TryCreate("T", "A", "B", new DateTime(2027, 3, 28, 2, 30, 0), "Europe/Berlin",
            new DateTime(2027, 3, 28, 5, 0, 0), "Europe/Berlin", out _, out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Daylight_saving_ambiguous_local_time_has_a_warning()
    {
        bool ok = FlightSchedule.TryCreate("T", "A", "B", new DateTime(2027, 10, 31, 2, 30, 0), "Europe/Berlin",
            new DateTime(2027, 10, 31, 5, 0, 0), "Europe/Berlin", out FlightSchedule? schedule, out _);
        ok.Should().BeTrue();
        schedule!.Warning.Should().Contain("Ambiguous");
    }

    [Fact]
    public void Manual_clock_can_be_advanced()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-12-18T08:00:00Z", CultureInfo.InvariantCulture));
        clock.Advance(TimeSpan.FromMinutes(5));
        clock.GetUtcNow().Should().Be(DateTimeOffset.Parse("2026-12-18T08:05:00Z", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Timestamp_audit_round_trip_is_culture_invariant()
    {
        DateTimeOffset source = new(2026, 12, 18, 21, 5, 0, 123, TimeSpan.Zero);
        string text = TimestampFormatter.FormatAudit(source);
        TimestampFormatter.TryParseAudit(text, out DateTimeOffset parsed).Should().BeTrue();
        TimestampFormatter.FormatAudit(parsed).Should().Be(text);
    }
}
