using System.Text;
using FluentAssertions;
using RocketWelder.SDK.Operations;

namespace RocketWelder.SDK.Operations.Welding.Tests;

/// <summary>
/// Canonical-serialization tests for the <c>rw.weldprogram/2</c> model (data-model.md §2 rules 1–5).
/// </summary>
public class WeldProgramSerializerTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    // ---- AT-A4: deterministic / byte-identical -------------------------

    [Fact]
    public void Serialize_UnchangedProgram_IsByteIdenticalOnReserialize()
    {
        var program = SampleData.Program();

        var first = WeldProgramSerializer.SerializeToUtf8Bytes(program);
        var second = WeldProgramSerializer.SerializeToUtf8Bytes(program);

        second.Should().Equal(first);
    }

    [Fact]
    public void Serialize_ThenDeserialize_ThenSerialize_IsByteIdentical()
    {
        var program = SampleData.Program();

        var bytes = WeldProgramSerializer.SerializeToUtf8Bytes(program);
        var roundTripped = WeldProgramDeserializer.Deserialize(bytes);
        var bytes2 = WeldProgramSerializer.SerializeToUtf8Bytes(roundTripped);

        bytes2.Should().Equal(bytes);
    }

    [Fact]
    public void Deserialize_ThenReserialize_OfCanonicalV2Fixture_IsByteIdentical()
    {
        // The tester reads ONLY data-model.md + dev-log.md; this proves a canonical /2 file on disk
        // round-trips byte-for-byte through the SDK (AT-A4), independent of the in-memory builder.
        var original = File.ReadAllBytes(Path.Combine(FixturesDir, "sample-v2.json"));

        var program = WeldProgramDeserializer.Deserialize(original);
        var reserialized = WeldProgramSerializer.SerializeToUtf8Bytes(program);

        reserialized.Should().Equal(original);
    }

    [Fact]
    public void Serialize_ProducesLfEndings_NoBom_TrailingNewline_TwoSpaceIndent()
    {
        var bytes = WeldProgramSerializer.SerializeToUtf8Bytes(SampleData.Program());

        // no UTF-8 BOM
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).Should().BeFalse();
        // no CR anywhere (pure LF)
        bytes.Should().NotContain((byte)'\r');
        // trailing newline
        bytes[^1].Should().Be((byte)'\n');

        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');
        var idLine = Array.Find(lines, l => l.TrimStart().StartsWith("\"id\""))!;
        idLine.Should().NotBeNull();
        idLine.Should().StartWith("  \"");
        idLine.StartsWith("    \"").Should().BeFalse(); // not 4 spaces at depth 1
    }

    [Fact]
    public void Serialize_FirstKeyIsSchemaV2_KeysInFixedOrder()
    {
        var text = Encoding.UTF8.GetString(WeldProgramSerializer.SerializeToUtf8Bytes(SampleData.Program()));

        var iSchema = text.IndexOf("\"schema\"", StringComparison.Ordinal);
        var iId = text.IndexOf("\"id\"", StringComparison.Ordinal);
        var iName = text.IndexOf("\"name\"", StringComparison.Ordinal);
        var iStep = text.IndexOf("\"step\"", StringComparison.Ordinal);
        var iSegments = text.IndexOf("\"segments\"", StringComparison.Ordinal);
        var iVersion = text.IndexOf("\"version\"", StringComparison.Ordinal);

        iSchema.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(iId);
        iId.Should().BeLessThan(iName);
        iName.Should().BeLessThan(iStep);
        iStep.Should().BeLessThan(iSegments);
        iSegments.Should().BeLessThan(iVersion);
        text.Should().StartWith("{\n  \"schema\": \"rw.weldprogram/2\"");
    }

    [Fact]
    public void Serialize_SegmentKeysInFixedSchemaOrder()
    {
        var text = Encoding.UTF8.GetString(WeldProgramSerializer.SerializeToUtf8Bytes(SampleData.Program()));

        // Per §2: id, binding, subRange, seamType, position, weldSize, gas, polarity, passes,
        // resolver, externalAxis.
        var order = new[]
        {
            "\"binding\"", "\"subRange\"", "\"seamType\"", "\"position\"", "\"weldSize\"",
            "\"gas\"", "\"polarity\"", "\"passes\"", "\"resolver\"", "\"externalAxis\""
        };

        var prev = -1;
        foreach (var key in order)
        {
            var idx = text.IndexOf(key, StringComparison.Ordinal);
            idx.Should().BeGreaterThan(prev, $"'{key}' must follow the previous key in §2 order");
            prev = idx;
        }
    }

    [Fact]
    public void Serialize_ExternalAxis_WritesDeviceAxisAngleInThatOrder()
    {
        // epic-065 FR-9: the setpoint identifies its axis by NAME on both halves — the cell role
        // ("positioner") and the plugin-frozen axis name ("tilt") — never by a numeric joint id.
        var text = Encoding.UTF8.GetString(WeldProgramSerializer.SerializeToUtf8Bytes(SampleData.Program()));

        text.Should().NotContain("jointId");

        var start = text.IndexOf("\"externalAxis\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        var block = text[start..(start + 200)];

        var iDevice = block.IndexOf("\"device\": \"positioner\"", StringComparison.Ordinal);
        var iAxis = block.IndexOf("\"axis\": \"tilt\"", StringComparison.Ordinal);
        var iAngle = block.IndexOf("\"angleDeg\"", StringComparison.Ordinal);

        iDevice.Should().BeGreaterThan(-1);
        iDevice.Should().BeLessThan(iAxis);
        iAxis.Should().BeLessThan(iAngle);
    }

    [Fact]
    public void Serialize_PassKeysInFixedSchemaOrder()
    {
        var text = Encoding.UTF8.GetString(WeldProgramSerializer.SerializeToUtf8Bytes(SampleData.Program()));

        // Within the first pass: id, role, jobRef, toolFrame, motion.
        var iRole = text.IndexOf("\"role\"", StringComparison.Ordinal);
        var iJobRef = text.IndexOf("\"jobRef\"", StringComparison.Ordinal);
        var iToolFrame = text.IndexOf("\"toolFrame\"", StringComparison.Ordinal);
        var iMotion = text.IndexOf("\"motion\"", StringComparison.Ordinal);

        iRole.Should().BeLessThan(iJobRef);
        iJobRef.Should().BeLessThan(iToolFrame);
        iToolFrame.Should().BeLessThan(iMotion);
    }

    // ---- AT-E3: change one field -> diff is exactly one line ------------

    [Fact]
    public void Serialize_ChangeOneField_DiffIsExactlyOneLine()
    {
        var program = SampleData.Program();
        var baseline = Lines(WeldProgramSerializer.Serialize(program));

        // change a single pass's travel speed
        var s0 = program.Segments[0];
        var p0 = s0.Passes[0];
        var changedS0 = s0 with
        {
            Passes = new[]
            {
                p0 with { Motion = p0.Motion with { TravelSpeedMmPerS = 7.0 } },
                s0.Passes[1]
            }
        };
        var changed = program with
        {
            Segments = new[] { changedS0, program.Segments[1] }
        };

        var modified = Lines(WeldProgramSerializer.Serialize(changed));

        var diff = LineDiff(baseline, modified);
        diff.removed.Should().Be(1);
        diff.added.Should().Be(1);
    }

    [Fact]
    public void Serialize_ChangeOneEnumField_DiffIsExactlyOneLine()
    {
        var program = SampleData.Program();
        var baseline = Lines(WeldProgramSerializer.Serialize(program));

        var changed = program with
        {
            Segments = new[]
            {
                program.Segments[0] with { Position = WeldPosition.PC },
                program.Segments[1]
            }
        };
        var modified = Lines(WeldProgramSerializer.Serialize(changed));

        var diff = LineDiff(baseline, modified);
        diff.removed.Should().Be(1);
        diff.added.Should().Be(1);
    }

    // ---- positional: reorder segments -> ids stable, move not delete+add

    [Fact]
    public void Serialize_ReorderTwoSegments_IdsStable_NotDeleteAdd()
    {
        var program = SampleData.Program();
        var baselineText = WeldProgramSerializer.Serialize(program);
        var baseline = Lines(baselineText);

        var reordered = program with
        {
            Segments = new[] { program.Segments[1], program.Segments[0] }
        };
        var reorderedText = WeldProgramSerializer.Serialize(reordered);

        CountOccurrences(reorderedText, "\"id\": \"s0\"").Should().Be(1);
        CountOccurrences(reorderedText, "\"id\": \"s1\"").Should().Be(1);

        var iS0 = reorderedText.IndexOf("\"id\": \"s0\"", StringComparison.Ordinal);
        var iS1 = reorderedText.IndexOf("\"id\": \"s1\"", StringComparison.Ordinal);
        iS1.Should().BeLessThan(iS0); // the move is a real, visible diff

        reorderedText.Should().NotBe(baselineText);

        // the set of lines is identical (a pure move) -> no field deleted or invented
        var moved = Lines(reorderedText);
        moved.OrderBy(x => x, StringComparer.Ordinal)
            .Should().Equal(baseline.OrderBy(x => x, StringComparer.Ordinal));
    }

    // ---- positional: reorder passes -> ids stable, move not delete+add -

    [Fact]
    public void Serialize_ReorderTwoPasses_IdsStable_NotDeleteAdd()
    {
        var program = SampleData.Program();
        var s0 = program.Segments[0];
        s0.Passes.Count.Should().Be(2);

        var baselineText = WeldProgramSerializer.Serialize(program);

        var reorderedS0 = s0 with { Passes = new[] { s0.Passes[1], s0.Passes[0] } };
        var reordered = program with { Segments = new[] { reorderedS0, program.Segments[1] } };
        var reorderedText = WeldProgramSerializer.Serialize(reordered);

        reorderedText.Should().NotBe(baselineText);

        // both pass ids still present; within s0 the order flipped (p1 before p0)
        var iP0 = reorderedText.IndexOf("\"id\": \"p0\"", StringComparison.Ordinal);
        var iP1 = reorderedText.IndexOf("\"id\": \"p1\"", StringComparison.Ordinal);
        iP1.Should().BeLessThan(iP0);

        // pure move: identical multiset of lines
        Lines(reorderedText).OrderBy(x => x, StringComparer.Ordinal)
            .Should().Equal(Lines(baselineText).OrderBy(x => x, StringComparer.Ordinal));
    }

    // ---- float formatting: 6 significant digits, invariant culture -----

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(1.0, "1")]
    [InlineData(6.5, "6.5")]
    [InlineData(84.32, "84.32")]
    [InlineData(0.123456789, "0.123457")]
    [InlineData(123456.789, "123457")]
    [InlineData(1234567.0, "1234570")]
    public void FormatFloat_SixSignificantDigits_Invariant(double value, string expected)
    {
        WeldProgramSerializer.FormatFloat(value).Should().Be(expected);
    }

    [Fact]
    public void FormatFloat_NormalizesNegativeZero()
    {
        var negativeZero = -0.0;
        double.IsNegative(negativeZero).Should().BeTrue();
        WeldProgramSerializer.FormatFloat(negativeZero).Should().Be("0");
    }

    // ---- helpers -------------------------------------------------------

    private static string[] Lines(string text) => text.Split('\n');

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    private static (int removed, int added) LineDiff(string[] a, string[] b)
    {
        var bag = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var l in a) bag[l] = bag.GetValueOrDefault(l) + 1;

        var added = 0;
        foreach (var l in b)
        {
            if (bag.TryGetValue(l, out var n) && n > 0) bag[l] = n - 1;
            else added++;
        }
        var removed = 0;
        foreach (var kv in bag) removed += kv.Value;
        return (removed, added);
    }
}
