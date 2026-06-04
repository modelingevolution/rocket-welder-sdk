using System.Text;
using RocketWelder.SDK.Automation.WeldProgramModel;

namespace RocketWelder.SDK.Automation.Tests.WeldProgramModel;

public class WeldProgramSerializerTests
{
    // ---- AT-A4: deterministic / byte-identical -------------------------

    [Fact]
    public void Serialize_UnchangedProgram_IsByteIdenticalOnReserialize()
    {
        var program = SampleData.Program();

        var first = WeldProgramSerializer.SerializeToUtf8Bytes(program);
        var second = WeldProgramSerializer.SerializeToUtf8Bytes(program);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Serialize_ThenDeserialize_ThenSerialize_IsByteIdentical()
    {
        var program = SampleData.Program();

        var bytes = WeldProgramSerializer.SerializeToUtf8Bytes(program);
        var roundTripped = WeldProgramDeserializer.Deserialize(bytes);
        var bytes2 = WeldProgramSerializer.SerializeToUtf8Bytes(roundTripped);

        Assert.Equal(bytes, bytes2);
    }

    [Fact]
    public void Serialize_ProducesLfEndings_NoBom_TrailingNewline_TwoSpaceIndent()
    {
        var bytes = WeldProgramSerializer.SerializeToUtf8Bytes(SampleData.Program());

        // no UTF-8 BOM
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        // no CR anywhere (pure LF)
        Assert.DoesNotContain((byte)'\r', bytes);
        // trailing newline
        Assert.Equal((byte)'\n', bytes[^1]);

        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');
        // second line ("id") is one level deep -> exactly 2 leading spaces
        var idLine = Array.Find(lines, l => l.TrimStart().StartsWith("\"id\""));
        Assert.NotNull(idLine);
        Assert.StartsWith("  \"", idLine);
        Assert.False(idLine!.StartsWith("    \"")); // not 4 spaces at depth 1
    }

    [Fact]
    public void Serialize_FirstKeyIsSchema_KeysInFixedOrder()
    {
        var text = Encoding.UTF8.GetString(WeldProgramSerializer.SerializeToUtf8Bytes(SampleData.Program()));

        var iSchema = text.IndexOf("\"schema\"", StringComparison.Ordinal);
        var iId = text.IndexOf("\"id\"", StringComparison.Ordinal);
        var iName = text.IndexOf("\"name\"", StringComparison.Ordinal);
        var iStep = text.IndexOf("\"step\"", StringComparison.Ordinal);
        var iSegments = text.IndexOf("\"segments\"", StringComparison.Ordinal);
        var iVersion = text.IndexOf("\"version\"", StringComparison.Ordinal);

        Assert.True(iSchema >= 0 && iSchema < iId);
        Assert.True(iId < iName);
        Assert.True(iName < iStep);
        Assert.True(iStep < iSegments);
        Assert.True(iSegments < iVersion);
        Assert.StartsWith("{\n  \"schema\": \"rw.weldprogram/1\"", text);
    }

    // ---- AT-E3: change one field -> diff is exactly one line ------------

    [Fact]
    public void Serialize_ChangeOneField_DiffIsExactlyOneLine()
    {
        var program = SampleData.Program();
        var baseline = Lines(WeldProgramSerializer.Serialize(program));

        var s1 = program.Segments[0];
        var changed = program with
        {
            Segments = new[]
            {
                s1 with { Process = s1.Process with { TravelSpeedMmPerS = 7.0 } },
                program.Segments[1]
            }
        };

        var modified = Lines(WeldProgramSerializer.Serialize(changed));

        var diff = LineDiff(baseline, modified);
        // one line removed + one line added = the single edited line
        Assert.Equal(1, diff.removed);
        Assert.Equal(1, diff.added);
    }

    // ---- Reorder two segments -> ids stable, move not delete+add --------

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

        // both segment ids still present exactly once (stable, not a delete+add of one)
        Assert.Equal(1, CountOccurrences(reorderedText, "\"id\": \"s0\""));
        Assert.Equal(1, CountOccurrences(reorderedText, "\"id\": \"s1\""));

        // s1 now appears before s0 in the file -> the move is a real, visible diff
        var iS0 = reorderedText.IndexOf("\"id\": \"s0\"", StringComparison.Ordinal);
        var iS1 = reorderedText.IndexOf("\"id\": \"s1\"", StringComparison.Ordinal);
        Assert.True(iS1 < iS0);

        // and it actually differs from the baseline (a genuine diff, not a no-op)
        Assert.NotEqual(baselineText, reorderedText);

        // the set of lines is identical (a pure move), proving no field was deleted or invented
        var moved = Lines(reorderedText);
        Assert.Equal(baseline.OrderBy(x => x, StringComparer.Ordinal),
                     moved.OrderBy(x => x, StringComparer.Ordinal));
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
        Assert.Equal(expected, WeldProgramSerializer.FormatFloat(value));
    }

    [Fact]
    public void FormatFloat_NormalizesNegativeZero()
    {
        var negativeZero = -0.0;
        Assert.True(double.IsNegative(negativeZero)); // genuinely -0.0 at runtime
        Assert.Equal("0", WeldProgramSerializer.FormatFloat(negativeZero));
    }

    // ---- helpers -------------------------------------------------------

    private static string[] Lines(string text) =>
        text.Split('\n');

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

    /// <summary>
    /// A minimal line diff: counts lines present in one side but not the other (multiset).
    /// For a single in-place edit this yields removed=1, added=1.
    /// </summary>
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
