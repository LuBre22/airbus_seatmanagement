using System.Globalization;
using FluentAssertions;
using SxSeatOps.Core;
using Xunit;

namespace SxSeatOps.Tests.Unit;

public sealed class UpgradeAndParameterTests
{
    private static Passenger Create(string pnr, LoyaltyTier tier = LoyaltyTier.None, bool prm = false,
        CabinClass cabin = CabinClass.Economy) =>
        new(pnr, "MULLER/HANS", new DateOnly(1990, 1, 1), cabin, tier, 1000, 20m,
            new DateTimeOffset(2026, 12, 18, 10, 0, 0, TimeSpan.FromHours(1)), "350-1234567890", prm);

    [Fact]
    public void Upgrade_fees_use_discount_and_away_from_zero()
    {
        UpgradeQueue.ComputeFee(CabinClass.Economy, CabinClass.PremiumEconomy, LoyaltyTier.Silver).Should().Be(276.58m);
        UpgradeQueue.ComputeFee(CabinClass.Economy, CabinClass.PremiumEconomy, LoyaltyTier.Gold).Should().Be(261.63m);
        UpgradeQueue.ComputeFee(CabinClass.Economy, CabinClass.Business, LoyaltyTier.Platinum).Should().Be(0m);
    }

    [Fact]
    public void Priority_order_promotes_silver_after_rejected_platinum_request()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 12, 18, 19, 0, 0, TimeSpan.Zero));
        var state = new CabinState(clock: clock);
        state.TryAddPassenger(Create("A2BC3D", LoyaltyTier.Silver)).Should().BeTrue();
        var audit = new AuditChain(clock);
        foreach (Seat seat in state.SeatMap.GetSeats(CabinClass.Business))
        {
            if (seat.Id != SeatId.Create("1A"))
            {
                new BlockSeatOperation(seat.Id, BlockReason.Technical)
                    .Run(state, new OperatorContext("occ", OperatorRole.Occ), audit).Succeeded.Should().BeTrue();
            }
        }

        state.UpgradeQueue.Enqueue(LoyaltyTier.Platinum,
            new UpgradeRequest("Z9Y8XW", CabinClass.Business, clock.GetUtcNow()));
        state.UpgradeQueue.Enqueue(LoyaltyTier.Silver,
            new UpgradeRequest("A2BC3D", CabinClass.Business, clock.GetUtcNow()));
        IReadOnlyList<UpgradeDecision> decisions =
            state.UpgradeQueue.ProcessUpgrades(state, new OperatorContext("rev", OperatorRole.Revenue), audit);

        decisions.Select(decision => decision.Kind).Should().Equal(UpgradeDecisionKind.Rejected, UpgradeDecisionKind.Granted);
        state.UpgradeQueue.Count.Should().Be(0);
        state.CheckConsistency(out _).Should().BeTrue();
    }

    [Fact]
    public void Prm_request_is_deferred_if_free_premium_seat_is_not_aisle()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 12, 18, 19, 0, 0, TimeSpan.Zero));
        var state = new CabinState(clock: clock);
        state.TryAddPassenger(Create("A2BC3D", LoyaltyTier.Gold, true)).Should().BeTrue();
        var audit = new AuditChain(clock);
        foreach (Seat seat in state.SeatMap.GetSeats(CabinClass.PremiumEconomy))
        {
            if (seat.Id != SeatId.Create("21E"))
            {
                new BlockSeatOperation(seat.Id, BlockReason.Technical)
                    .Run(state, new OperatorContext("occ", OperatorRole.Occ), audit).Succeeded.Should().BeTrue();
            }
        }

        state.UpgradeQueue.Enqueue(LoyaltyTier.Gold,
            new UpgradeRequest("A2BC3D", CabinClass.PremiumEconomy, clock.GetUtcNow()));
        IReadOnlyList<UpgradeDecision> decisions =
            state.UpgradeQueue.ProcessUpgrades(state, new OperatorContext("rev", OperatorRole.Revenue), audit);

        decisions.Should().ContainSingle()
            .Which.Kind.Should().Be(UpgradeDecisionKind.Deferred);
        state.UpgradeQueue.Count.Should().Be(1);
        audit.Blocks[^1].Payload.Should().NotContain("UPG");
    }

    [Fact]
    public void Ref_parameter_accumulates_mass_and_count()
    {
        var totals = new LoadTotals();
        ParameterSemantics.AddToLoad(ref totals, Create("A2BC3D"));
        totals.PassengerCount.Should().Be(1);
        totals.PassengerMassKg.Should().Be(84m);
        totals.BaggageMassKg.Should().Be(20m);
    }

    [Fact]
    public void In_parameters_compute_moment_from_readonly_values()
    {
        var geometry = new SeatGeometry(2.5m, 0.25m);
        var mass = new Mass(100m);
        ParameterSemantics.ComputeMoment(in geometry, in mass).Should().Be(275m);
    }

    [Fact]
    public void Ref_return_replaces_array_slot()
    {
        Seat?[][] grid = [new Seat?[3]];
        byte[] rows = [1];
        string[] layouts = ["A-DG-K"];
        SeatId id = SeatId.Create("1D");
        ref Seat? slot = ref SeatSlotAccessor.GetSlot(grid, rows, layouts, in id);
        slot = new BusinessSeat(id, SeatAttributes.Aisle);
        grid[0][1].Should().NotBeNull();
        grid[0][1]!.Id.Should().Be(id);
    }

    [Fact]
    public void Timestamp_parser_uses_explicit_two_digit_year_and_timezone()
    {
        FlightSchedule schedule = FlightSchedule.CreateDefault();
        TimestampParser.TryParse("18DEC26 0915", schedule, out DateTimeOffset value, out _).Should().BeTrue();
        value.Year.Should().Be(2026);
        TimestampParser.TryParse("29FEB24 0915", schedule, out DateTimeOffset leap, out _).Should().BeTrue();
        leap.Year.Should().Be(2024);
        TimestampParser.TryParse("29FEB26 0915", schedule, out _, out _).Should().BeFalse();
        TimestampParser.TryParseDateOnly("30.07.1988", out DateOnly date).Should().BeTrue();
        date.Should().Be(new DateOnly(1988, 7, 30));
        TimestampParser.TryParseTimeOnly("2400", out _).Should().BeFalse();
    }

    [Fact]
    public void Numeric_report_formatters_apply_documented_precision()
    {
        NumberFormatter.FormatRow(7).Should().Be("007");
        NumberFormatter.FormatHashByte(15).Should().Be("0f");
        NumberFormatter.FormatMass(23.5m, CultureInfo.InvariantCulture).Should().Be("23.5 kg");
        NumberFormatter.FormatFee(2.665m, CultureInfo.InvariantCulture).Should().Contain("€");
    }
}
