using System.Globalization;
using FluentAssertions;
using SxSeatOps.Core;
using Xunit;

namespace SxSeatOps.Tests.Unit;

public sealed class InfrastructureTests
{
    [Fact]
    public void Audit_chain_starts_with_valid_genesis_and_appends()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 12, 18, 8, 0, 0, TimeSpan.Zero));
        var chain = new AuditChain(clock);
        chain.Append("op", "BLK 1A TECH");
        chain.Blocks.Should().HaveCount(2);
        chain.Verify(out long invalid, out string? reason).Should().BeTrue(reason);
        invalid.Should().Be(-1);
    }

    [Fact]
    public void Audit_hash_is_sha384_lowercase_and_has_96_hex_characters()
    {
        var chain = new AuditChain(new ManualTimeProvider(DateTimeOffset.UnixEpoch));
        AuditBlock block = chain.Append("a", "payload");
        block.Hash.Should().HaveLength(96);
        block.Hash.Should().Be(block.Hash.ToLowerInvariant());
    }

    [Fact]
    public void Canonical_serialization_uses_length_prefixes()
    {
        var first = new AuditBlock(1, DateTimeOffset.UnixEpoch, "a|b", "c", new string('0', 96), 1, "x");
        var second = new AuditBlock(1, DateTimeOffset.UnixEpoch, "a", "b|c", new string('0', 96), 1, "x");
        AuditChain.Canonicalize(first).Should().NotBe(AuditChain.Canonicalize(second));
    }

    [Fact]
    public void Audit_timestamps_do_not_move_backwards()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-12-18T08:00:00Z", CultureInfo.InvariantCulture));
        var chain = new AuditChain(clock);
        chain.Append("a", "first");
        clock.SetUtcNow(DateTimeOffset.Parse("2026-12-18T07:00:00Z", CultureInfo.InvariantCulture));
        AuditBlock next = chain.Append("a", "second");
        next.TimestampUtc.Should().Be(chain.Blocks[^2].TimestampUtc);
        chain.Verify(out _, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-12-18T09:42:00+01:00", true)]
    [InlineData("2026-02-30T09:42:00+01:00", false)]
    [InlineData("2026-12-18 09:42", false)]
    [InlineData("03/04/2026", false)]
    public void Strict_iso_parser_rejects_ambiguous_or_invalid_values(string value, bool expected) =>
        TimestampFormatter.TryParseIso(value, out _).Should().Be(expected);

    [Fact]
    public void Csv_writer_quotes_delimiters_and_escapes_quotes() =>
        CsvWriter.EncodeText("a;\"b\"").Should().Be("\"a;\"\"b\"\"\"");

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+1+1")]
    [InlineData("-cmd")]
    [InlineData("@formula")]
    public void Csv_formula_neutralization_round_trips(string value)
    {
        string encoded = CsvWriter.EncodeText(value);
        encoded.Should().StartWith("'");
        CsvWriter.DecodeText(encoded).Should().Be(value);
    }

    [Fact]
    public void Csv_tokenizer_preserves_quoted_delimiters_quotes_and_multiline_fields()
    {
        const string csv = "PNR;Name\r\nAB2345;\"Smith; \"\"Dr\"\"\r\nLee\"\r\n";
        IReadOnlyList<CsvRecord> rows = CsvTokenizer.ReadAll(new StringReader(csv));
        rows.Should().HaveCount(2);
        rows[1].Fields[1].Should().Be("Smith; \"Dr\"\r\nLee");
        rows[1].StartLine.Should().Be(2);
    }

    [Fact]
    public void Csv_tokenizer_rejects_unclosed_quote()
    {
        Action parse = () => CsvTokenizer.ReadAll(new StringReader("a;\"unfinished"));
        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void Binary_search_matches_expected_index_and_insertion_point()
    {
        int[] data = [1, 3, 3, 8];
        int found = Algorithms.BinarySearch(data, 3, Comparer<int>.Default);
        found.Should().BeGreaterThanOrEqualTo(0);
        data[found].Should().Be(3);
        Algorithms.BinarySearch(data, 4, Comparer<int>.Default).Should().Be(~3);
        Algorithms.LowerBound(data, 3, Comparer<int>.Default).Should().Be(1);
        Algorithms.UpperBound(data, 3, Comparer<int>.Default).Should().Be(3);
    }

    [Fact]
    public void Stable_sort_preserves_equal_key_order()
    {
        var data = new List<(int Key, string Name)> { (2, "a"), (1, "b"), (2, "c"), (1, "d") };
        IComparer<(int Key, string Name)> comparer =
            Comparer<(int Key, string Name)>.Create((left, right) => left.Key.CompareTo(right.Key));
        Algorithms.StableSort(data, comparer, 2);
        data.Select(item => item.Name).Should().Equal("b", "d", "a", "c");
        Algorithms.IsSorted(data, comparer).Should().BeTrue();
    }

    [Fact]
    public void Flight_number_is_normalized_and_range_checked()
    {
        CommandParser.TryNormalizeFlightNumber("sx 350", out string? flight).Should().BeTrue();
        flight.Should().Be("SX0350");
        CommandParser.TryNormalizeFlightNumber("SX0", out _).Should().BeFalse();
        CommandParser.TryNormalizeFlightNumber("SX10000", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("STAT", true)]
    [InlineData("LOGIN a OCC,STATION", true)]
    [InlineData("ASG K7M4QZ 31A", true)]
    [InlineData("UNKNOWN", false)]
    [InlineData("REL", false)]
    public void Command_parser_checks_command_and_arity(string input, bool expected) =>
        CommandParser.Parse(input).Succeeded.Should().Be(expected);

    [Fact]
    public void File_name_and_duration_are_fixed_and_round_trip_safe()
    {
        TimestampFormatter.FormatManifestFileName("SX0350", new DateOnly(2026, 12, 18))
            .Should().Be("SX0350_20261218_manifest.csv");
        TimestampFormatter.FormatDuration(TimeSpan.FromHours(12.5)).Should().Be("12 h 30 min");
        TimestampFormatter.FormatDuration(TimeSpan.FromHours(26) + TimeSpan.FromMinutes(5))
            .Should().Be("1 d 02 h 05 min");
    }

    [Fact]
    public void Culture_specific_formatting_is_not_used_for_audit_hashes()
    {
        string before = TimestampFormatter.FormatAudit(new DateTimeOffset(2026, 12, 18, 21, 5, 0, TimeSpan.Zero));
        CultureInfo old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            TimestampFormatter.FormatAudit(new DateTimeOffset(2026, 12, 18, 21, 5, 0, TimeSpan.Zero)).Should().Be(before);
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }
}
