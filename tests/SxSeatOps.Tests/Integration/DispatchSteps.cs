using System.Globalization;
using FluentAssertions;
using Reqnroll;
using SxSeatOps.Core;

namespace SxSeatOps.Tests.Integration;

[Binding]
public sealed class DispatchSteps
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "data", "manifest_SX0350.csv");

    private ImportResult? _import;
    private CabinState? _state;
    private AuditChain? _audit;
    private string? _export;
    private List<string>? _hashes;
    private string? _temporaryFile;

    [AfterScenario]
    public void CleanupTemporaryFile()
    {
        if (File.Exists(_temporaryFile))
        {
            File.Delete(_temporaryFile);
        }
    }

    [Given("the sample manifest has been imported")]
    public void GivenTheSampleManifestHasBeenImported()
    {
        _import = new ManifestCsvImporter().Import(Fixture, FlightSchedule.CreateDefault(), new CabinLayout());
        _import.FatalError.Should().BeNull();
    }

    [Then("the first record for {string} is accepted")]
    public void ThenTheFirstRecordIsAccepted(string pnr)
    {
        Passenger passenger = _import!.Passengers.Should().ContainKey(pnr).WhoseValue;
        passenger.NameField.Should().Be("MUELLER/HANS");
        _import.Issues.Should().Contain(issue => issue.Field == "PNR" && issue.Message.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Then("invalid date and miles fields are reported")]
    public void ThenInvalidDateAndMilesFieldsAreReported()
    {
        _import!.Issues.Should().Contain(issue => issue.Field == "Geburtsdatum");
        _import.Issues.Should().Contain(issue => issue.Field == "Meilen");
        _import.Passengers.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [When("I load imported passengers and apply their preassignments")]
    public void WhenILoadPassengersAndApplyPreassignments()
    {
        var state = new CabinState(clock: new ManualTimeProvider(
            new DateTimeOffset(2026, 12, 18, 19, 0, 0, TimeSpan.Zero)));
        _state = state;
        foreach (Passenger passenger in _import!.Passengers.Values)
        {
            state.TryAddPassenger(passenger).Should().BeTrue();
        }

        var audit = new AuditChain(state.Clock);
        _audit = audit;
        foreach ((SeatId seat, string pnr) in _import.PreAssignments)
        {
            OperationResult result = new AssignSeatOperation(pnr, seat)
                .Run(state, new OperatorContext("station", OperatorRole.Station), audit);
            result.Succeeded.Should().BeTrue(result.Reason ?? "The imported preassignment failed.");
        }
    }

    [Then("the flight state is consistent")]
    public void ThenTheFlightStateIsConsistent() =>
        _state!.CheckConsistency(out string? error).Should().BeTrue(error);

    [When("I create audit hashes under German, English, and Thai cultures")]
    public void WhenICreateAuditHashesUnderCultures()
    {
        CultureInfo savedCulture = CultureInfo.CurrentCulture;
        _hashes = new List<string>();
        try
        {
            foreach (string culture in new[] { "de-DE", "en-US", "th-TH" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var clock = new ManualTimeProvider(new DateTimeOffset(2026, 12, 18, 8, 0, 0, TimeSpan.Zero));
                var audit = new AuditChain(clock);
                audit.Append("actor", "PAYLOAD");
                _hashes.Add(audit.Head.Hash);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }
    }

    [Then("all generated audit hashes are identical")]
    public void ThenAllGeneratedAuditHashesAreIdentical()
    {
        List<string> hashes = _hashes!;
        hashes.Should().HaveCount(3);
        hashes.Should().OnlyContain(hash => hash == hashes[0]);
    }

    [When("I block a seat and assign a passenger")]
    public void WhenIBlockASeatAndAssignAPassenger()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 12, 18, 19, 0, 0, TimeSpan.Zero));
        var state = new CabinState(clock: clock);
        var audit = new AuditChain(clock);
        _state = state;
        _audit = audit;
        var context = new OperatorContext("ops", OperatorRole.Occ | OperatorRole.Station);

        new BlockSeatOperation(SeatId.Create("1A"), BlockReason.CrewRest)
            .Run(state, context, audit).Succeeded.Should().BeTrue();
        state.TryAddPassenger(new Passenger("K7M4QZ", "MULLER/HANS", new DateOnly(1990, 1, 1),
            CabinClass.Economy, LoyaltyTier.Gold, 1, 10m, clock.GetUtcNow(), "350-1234567890")).Should().BeTrue();
        new AssignSeatOperation("K7M4QZ", SeatId.Create("31A"))
            .Run(state, context, audit).Succeeded.Should().BeTrue();
    }

    [Then("the flight state and audit chain are valid")]
    public void ThenTheFlightStateAndAuditChainAreValid()
    {
        _state!.CheckConsistency(out string? error).Should().BeTrue(error);
        _audit!.Verify(out _, out string? reason).Should().BeTrue(reason);
        _audit.Blocks.Should().HaveCount(3);
    }

    [When("I execute a batch that blocks the same seat twice")]
    public void WhenIExecuteAFailingBatch()
    {
        var state = new CabinState(clock: new ManualTimeProvider(
            new DateTimeOffset(2026, 12, 18, 19, 0, 0, TimeSpan.Zero)));
        _state = state;
        CabinStateSnapshot before = state.Snapshot();
        var audit = new AuditChain(state.Clock);
        _audit = audit;
        SeatOperation[] operations =
        [
            new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Security),
            new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Medical)
        ];

        new OperationHistory().ExecuteBatch(operations, state, new OperatorContext("occ", OperatorRole.Occ),
            audit, out _).Should().BeFalse();
        state.Snapshot().Should().Be(before);
    }

    [Then("the state is restored and the failed batch is audited")]
    public void ThenTheStateIsRestoredAndTheBatchIsAudited()
    {
        _state!.CheckConsistency(out string? error).Should().BeTrue(error);
        _audit!.Blocks.Should().HaveCount(3);
    }

    [When("I attempt to block a seat before and after supervisor authorization")]
    public void WhenIAttemptToBlockBeforeAndAfterSupervisorAuthorization()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 12, 18, 20, 20, 0, TimeSpan.Zero));
        var state = new CabinState(clock: clock);
        var audit = new AuditChain(clock);
        _state = state;
        _audit = audit;

        OperationResult rejected = new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Security)
            .Run(state, new OperatorContext("op", OperatorRole.Occ), audit);
        OperationResult accepted = new BlockSeatOperation(SeatId.Create("1A"), BlockReason.Security)
            .Run(state, new OperatorContext("sup", OperatorRole.Occ | OperatorRole.Supervisor), audit);
        rejected.Succeeded.Should().BeFalse();
        accepted.Succeeded.Should().BeTrue();
    }

    [Then("only the supervisor operation succeeds and the audit chain is valid")]
    public void ThenOnlySupervisorOperationSucceedsAndAuditChainIsValid()
    {
        _state!.CheckConsistency(out string? error).Should().BeTrue(error);
        _audit!.Verify(out _, out string? reason).Should().BeTrue(reason);
    }

    [Then("the standard cabin layout contains the characterized seats")]
    public void ThenTheStandardCabinLayoutContainsCharacterizedSeats()
    {
        var state = new CabinState();
        state.SeatMap.Layout.Contains(SeatId.Create("31E")).Should().BeTrue();
        state.SeatMap.Layout.Contains(SeatId.Create("13A")).Should().BeFalse();
        state.SeatMap.Seats.Should().HaveCount(289);
    }

    [When("I export a passenger manifest")]
    public void WhenIExportAPassengerManifest()
    {
        var passenger = new Passenger("K7M4QZ", "MULLER/HANS", new DateOnly(1990, 1, 1),
            CabinClass.Economy, LoyaltyTier.Gold, 10, 12.5m,
            new DateTimeOffset(2026, 12, 18, 10, 0, 0, TimeSpan.Zero), "350-1234567890");
        var passengers = new Dictionary<string, Passenger>(StringComparer.OrdinalIgnoreCase)
        {
            [passenger.Pnr] = passenger
        };
        var passengerSeats = new Dictionary<string, SeatId>();
        _export = ManifestCsvExporter.Export(passengers, passengerSeats);
    }

    [Then("the export is deterministic and contains no byte order mark")]
    public void ThenTheExportIsDeterministicAndContainsNoByteOrderMark()
    {
        string export = _export!;
        export.Should().StartWith("PNR;");
        export.Should().NotContain("\uFEFF");
        var passenger = new Passenger("K7M4QZ", "MULLER/HANS", new DateOnly(1990, 1, 1),
            CabinClass.Economy, LoyaltyTier.Gold, 10, 12.5m,
            new DateTimeOffset(2026, 12, 18, 10, 0, 0, TimeSpan.Zero), "350-1234567890");
        var passengers = new Dictionary<string, Passenger>(StringComparer.OrdinalIgnoreCase)
        {
            [passenger.Pnr] = passenger
        };
        ManifestCsvExporter.Export(passengers, new Dictionary<string, SeatId>()).Should().Be(export);
    }

    [When("I atomically write a CSV file")]
    public void WhenIAtomicallyWriteACsvFile()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
        _temporaryFile = path;
        AtomicCsvFile.Write(path, "a;b\r\n1;2\r\n");
    }

    [Then("the destination contains the complete replacement content")]
    public void ThenTheDestinationContainsCompleteReplacementContent()
    {
        File.ReadAllText(_temporaryFile!).Should().Be("a;b\r\n1;2\r\n");
    }
}
