using FakeItEasy;
using FluentAssertions;
using SxSeatOps.Core;
using Xunit;

namespace SxSeatOps.Tests.Unit;

public sealed class SeatOperationTests
{
    private static Passenger Passenger(string pnr = "K7M4QZ", bool prm = false,
        CabinClass cabin = CabinClass.Economy, DateOnly? birth = null) =>
        new(pnr, "MULLER/HANS", birth ?? new DateOnly(1990, 1, 1), cabin, LoyaltyTier.Gold, 1000, 20m,
            new DateTimeOffset(2026, 12, 18, 10, 0, 0, TimeSpan.FromHours(1)), "350-1234567890", prm);

    private static CabinState State(DateTimeOffset? now = null) =>
        new(clock: new ManualTimeProvider(now ?? new DateTimeOffset(2026, 12, 18, 19, 0, 0, TimeSpan.Zero)));

    private static readonly OperatorContext Station = new("op", OperatorRole.Station);

    [Fact]
    public void Standard_map_contains_289_seats_with_expected_cabins()
    {
        var map = new SeatMap();
        map.Seats.Should().HaveCount(289);
        map.GetStatistics(CabinClass.Business).Total.Should().Be(40);
        map.GetStatistics(CabinClass.PremiumEconomy).Total.Should().Be(24);
        map.GetStatistics(CabinClass.Economy).Total.Should().Be(225);
    }

    [Theory]
    [InlineData("1A", SeatAttributes.Window | SeatAttributes.Aisle | SeatAttributes.NearGalley)]
    [InlineData("1D", SeatAttributes.Aisle | SeatAttributes.NearGalley)]
    [InlineData("21E", SeatAttributes.NearGalley)]
    [InlineData("31E", SeatAttributes.ExtraLegroom | SeatAttributes.Bassinet | SeatAttributes.NearGalley)]
    [InlineData("41K", SeatAttributes.Window | SeatAttributes.ExitRow | SeatAttributes.ExtraLegroom)]
    [InlineData("55J", SeatAttributes.NearLavatory)]
    public void Seat_features_are_derived_from_layout(string text, SeatAttributes expected)
    {
        var map = new SeatMap();
        map.TryGetSeat(text, out Seat? seat).Should().BeTrue();
        seat!.Attributes.Should().Be(expected);
    }

    [Theory]
    [InlineData("13A")]
    [InlineData("31F")]
    [InlineData("99A")]
    public void Nonexistent_designators_are_absent(string text) =>
        new SeatMap().TryGetSeat(text, out _).Should().BeFalse();

    [Fact]
    public void Infant_cannot_be_assigned_and_rejection_is_audited()
    {
        var state = State();
        state.TryAddPassenger(Passenger("A2BC3D", birth: new DateOnly(2025, 12, 19))).Should().BeTrue();
        IAuditSink sink = A.Fake<IAuditSink>();
        var operation = new AssignSeatOperation("A2BC3D", SeatId.Create("31A"));

        OperationResult result = operation.Run(state, Station, sink);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Contain("Infants");
        A.CallTo(() => sink.Record("op", A<string>.That.Matches(payload => payload.Contains("REJECTED", StringComparison.Ordinal))))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Reduced_mobility_requires_aisle_and_not_exit_row()
    {
        var state = State();
        state.TryAddPassenger(Passenger(prm: true)).Should().BeTrue();
        IAuditSink sink = A.Fake<IAuditSink>();
        var middle = new AssignSeatOperation("K7M4QZ", SeatId.Create("31E"));
        var exit = new AssignSeatOperation("K7M4QZ", SeatId.Create("41D"));

        middle.Run(state, Station, sink).Succeeded.Should().BeFalse();
        exit.Run(state, Station, sink).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Successful_assignment_makes_indexes_consistent()
    {
        var state = State();
        state.TryAddPassenger(Passenger()).Should().BeTrue();
        IAuditSink sink = A.Fake<IAuditSink>();

        OperationResult result = new AssignSeatOperation("K7M4QZ", SeatId.Create("31A")).Run(state, Station, sink);

        result.Succeeded.Should().BeTrue();
        state.CheckConsistency(out _).Should().BeTrue();
        state.SeatAssignments[SeatId.Create("31A")].Should().Be("K7M4QZ");
    }

    [Fact]
    public void Block_and_release_restore_available_state()
    {
        var state = State();
        IAuditSink sink = A.Fake<IAuditSink>();
        var context = new OperatorContext("occ", OperatorRole.Occ);
        var seat = SeatId.Create("1A");

        new BlockSeatOperation(seat, BlockReason.CrewRest).Run(state, context, sink).Succeeded.Should().BeTrue();
        state.SeatMap.Seats.Single(item => item.Id == seat).Status.Should().Be(SeatStatus.Blocked);
        new ReleaseSeatOperation(seat).Run(state, context, sink).Succeeded.Should().BeTrue();
        state.CheckConsistency(out _).Should().BeTrue();
        A.CallTo(() => sink.Record("occ", A<string>._)).MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public void Missing_role_is_rejected_and_audited_once()
    {
        var state = State();
        IAuditSink sink = A.Fake<IAuditSink>();

        OperationResult result = new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Technical)
            .Run(state, Station, sink);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().ContainEquivalentOf("role");
        A.CallTo(() => sink.Record("op", A<string>.That.Matches(payload => payload.Contains("REJECTED", StringComparison.Ordinal))))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Freeze_requires_additional_supervisor_role()
    {
        var frozen = new DateTimeOffset(2026, 12, 18, 20, 20, 0, TimeSpan.Zero);
        var state = State(frozen);
        IAuditSink sink = A.Fake<IAuditSink>();

        new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Technical)
            .Run(state, new OperatorContext("occ", OperatorRole.Occ), sink).Succeeded.Should().BeFalse();
        new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Technical)
            .Run(state, new OperatorContext("sup", OperatorRole.Occ | OperatorRole.Supervisor), sink).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Swap_keeps_both_assignment_indexes_mirrored()
    {
        var state = State();
        state.TryAddPassenger(Passenger("K7M4QZ")).Should().BeTrue();
        state.TryAddPassenger(Passenger("R2D9TX")).Should().BeTrue();
        IAuditSink sink = A.Fake<IAuditSink>();
        new AssignSeatOperation("K7M4QZ", SeatId.Create("31A")).Run(state, Station, sink).Succeeded.Should().BeTrue();
        new AssignSeatOperation("R2D9TX", SeatId.Create("31B")).Run(state, Station, sink).Succeeded.Should().BeTrue();

        new SwapSeatsOperation(SeatId.Create("31A"), SeatId.Create("31B"))
            .Run(state, Station, sink).Succeeded.Should().BeTrue();

        state.CheckConsistency(out _).Should().BeTrue();
        state.PassengerSeats["K7M4QZ"].Should().Be(SeatId.Create("31B"));
    }

    [Fact]
    public void Upgrade_changes_entitlement_and_assigns_business_seat()
    {
        var state = State();
        state.TryAddPassenger(Passenger()).Should().BeTrue();
        var operation = new UpgradeOperation("K7M4QZ", CabinClass.Business, SeatId.Create("1A"));

        operation.Run(state, new OperatorContext("rev", OperatorRole.Revenue), A.Fake<IAuditSink>())
            .Succeeded.Should().BeTrue();

        state.Passengers["K7M4QZ"].EntitledCabin.Should().Be(CabinClass.Business);
        state.CheckConsistency(out _).Should().BeTrue();
    }

    [Fact]
    public void Undo_restores_exact_snapshot_for_block_operation()
    {
        var state = State();
        CabinStateSnapshot before = state.Snapshot();
        var operation = new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Technical);

        operation.Run(state, new OperatorContext("occ", OperatorRole.Occ), A.Fake<IAuditSink>())
            .Succeeded.Should().BeTrue();
        operation.Undo(state).Should().BeTrue();
        state.Snapshot().Should().Be(before);
    }

    [Fact]
    public void New_successful_operation_clears_redo_stack()
    {
        var state = State();
        var history = new OperationHistory();
        IAuditSink sink = A.Fake<IAuditSink>();
        var context = new OperatorContext("occ", OperatorRole.Occ);

        history.Execute(new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Technical), state, context, sink)
            .Succeeded.Should().BeTrue();
        history.TryUndo(state, out _).Should().BeTrue();
        history.Execute(new BlockSeatOperation(SeatId.Create("1D"), BlockReason.Security), state, context, sink)
            .Succeeded.Should().BeTrue();
        history.RedoCount.Should().Be(0);
    }

    [Fact]
    public void Failed_batch_rolls_back_earlier_changes()
    {
        var state = State();
        CabinStateSnapshot before = state.Snapshot();
        var history = new OperationHistory();
        SeatOperation[] operations =
        [
            new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Security),
            new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Technical)
        ];

        history.ExecuteBatch(operations, state, new OperatorContext("occ", OperatorRole.Occ), A.Fake<IAuditSink>(), out _)
            .Should().BeFalse();
        state.Snapshot().Should().Be(before);
    }
}
