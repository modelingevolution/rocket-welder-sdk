using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Operations;

/// <summary>
/// The single canonical (de)serializer for <c>program.json</c> (per <c>data-model.md</c> §2).
/// Nothing else writes <c>program.json</c>. Guarantees byte-identical re-serialization of an
/// unchanged in-memory program (AT-A4) by enforcing:
/// <list type="number">
/// <item>fixed schema key order (never alphabetical-by-runtime);</item>
/// <item>segments serialized positionally in weld order, datum.points in touch order (never re-sorted);</item>
/// <item>floats to 6 significant digits, '.' decimal, invariant culture, no trailing-zero variance;</item>
/// <item>UTF-8, LF line endings, 2-space indent, trailing newline, no BOM;</item>
/// <item>re-serializing an unchanged program is byte-identical.</item>
/// </list>
/// </summary>
public static class WeldProgramSerializer
{
    /// <summary>Schema id + major version emitted as the first key (per §2).</summary>
    public const string Schema = "rw.weldprogram/1";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
        // We pre-format every floating value via WriteRawValue, and write strings ourselves,
        // so the relaxed encoder only affects any incidental escaping; safe and deterministic.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Serializes a <see cref="WeldProgram"/> to the canonical UTF-8 byte sequence
    /// (LF, 2-space indent, trailing newline, no BOM).
    /// </summary>
    public static byte[] SerializeToUtf8Bytes(WeldProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteProgram(writer, program);
        }

        // Rule 4: trailing newline (Utf8JsonWriter does not emit one).
        var body = buffer.WrittenSpan;
        var result = new byte[body.Length + 1];
        body.CopyTo(result);
        result[^1] = (byte)'\n';
        return result;
    }

    /// <summary>
    /// Serializes a <see cref="WeldProgram"/> to a canonical string (the UTF-8 bytes decoded).
    /// </summary>
    public static string Serialize(WeldProgram program) =>
        Encoding.UTF8.GetString(SerializeToUtf8Bytes(program));

    private static void WriteProgram(Utf8JsonWriter w, WeldProgram p)
    {
        w.WriteStartObject();

        w.WriteString("schema", Schema);
        w.WriteString("id", p.Id.ToString("D", CultureInfo.InvariantCulture));
        w.WriteString("name", p.Name);

        w.WritePropertyName("step");
        w.WriteStartObject();
        w.WriteString("path", p.Step.Path);
        w.WriteString("sha256", p.Step.Sha256);
        w.WriteEndObject();

        w.WritePropertyName("preview");
        w.WriteStartObject();
        w.WriteString("path", p.Preview.Path);
        w.WriteEndObject();

        WriteDatum(w, p.Datum);

        w.WritePropertyName("segments");
        w.WriteStartArray();
        foreach (var segment in p.Segments) // positional — weld order, never re-sorted
            WriteSegment(w, segment);
        w.WriteEndArray();

        w.WriteString("weldOrderStrategy", p.WeldOrderStrategy);

        WriteVersion(w, p.Version);

        w.WriteEndObject();
    }

    private static void WriteDatum(Utf8JsonWriter w, Datum datum)
    {
        w.WritePropertyName("datum");
        w.WriteStartObject();
        w.WriteString("scheme", datum.Scheme);
        w.WritePropertyName("points");
        w.WriteStartArray();
        foreach (var pt in datum.Points) // touch order, never re-sorted
        {
            w.WriteStartObject();
            w.WriteString("id", pt.Id);
            WriteVecProperty(w, "p", pt.P);
            WriteNullableString(w, "onFace", pt.OnFace);
            WriteNullableString(w, "onEdge", pt.OnEdge);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteSegment(Utf8JsonWriter w, Segment s)
    {
        w.WriteStartObject();
        w.WriteString("id", s.Id);

        WriteBinding(w, s.Binding);

        w.WritePropertyName("subRange");
        w.WriteRawValue(
            string.Concat("[", FormatFloat(s.SubRange.T0), ", ", FormatFloat(s.SubRange.T1), "]"),
            skipInputValidation: true);

        w.WritePropertyName("process");
        w.WriteStartObject();
        w.WriteString("seamType", s.Process.SeamType);
        w.WritePropertyName("weldJob");
        w.WriteStartObject();
        w.WriteNumber("id", s.Process.WeldJob.Id);
        w.WritePropertyName("params");
        WriteParams(w, s.Process.WeldJob.Params);
        w.WriteEndObject();
        WriteFloatProperty(w, "travelSpeedMmPerS", s.Process.TravelSpeedMmPerS);
        w.WriteEndObject();

        w.WritePropertyName("torchFrame");
        w.WriteStartObject();
        WriteFloatProperty(w, "standoffMm", s.TorchFrame.StandoffMm);
        WriteFloatProperty(w, "workAngleDeg", s.TorchFrame.WorkAngleDeg);
        WriteFloatProperty(w, "travelAngleDeg", s.TorchFrame.TravelAngleDeg);
        w.WriteString("technique", s.TorchFrame.Technique);
        w.WriteEndObject();

        w.WritePropertyName("resolver");
        if (s.Resolver is null)
        {
            w.WriteNullValue();
        }
        else
        {
            w.WriteStartObject();
            w.WriteString("mode", s.Resolver.Mode);
            WriteNullableString(w, "featureRef", s.Resolver.FeatureRef);
            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    private static void WriteBinding(Utf8JsonWriter w, EdgeBinding b)
    {
        w.WritePropertyName("binding");
        w.WriteStartObject();
        w.WriteString("edgeIdHint", b.EdgeIdHint);
        w.WriteString("kind", KindToString(b.Kind));
        WriteFloatProperty(w, "lengthMm", b.LengthMm);
        WriteVecProperty(w, "midpoint", b.Midpoint);
        WriteVecProperty(w, "tangentAtMid", b.TangentAtMid);

        w.WritePropertyName("endpoints");
        w.WriteStartArray();
        foreach (var ep in b.Endpoints)
            WriteVec(w, ep);
        w.WriteEndArray();

        w.WritePropertyName("adjFaceNormals");
        w.WriteStartArray();
        foreach (var n in b.AdjFaceNormals)
            WriteVec(w, n);
        w.WriteEndArray();

        w.WriteEndObject();
    }

    private static void WriteVersion(Utf8JsonWriter w, VersionInfo v)
    {
        w.WritePropertyName("version");
        w.WriteStartObject();
        w.WriteString("authoredBy", v.AuthoredBy);
        w.WriteString("authoredAtUtc", v.AuthoredAtUtc.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        WriteNullableString(w, "parentCommit", v.ParentCommit);
        w.WriteString("appVersion", v.AppVersion);
        w.WriteEndObject();
    }

    private static void WriteParams(Utf8JsonWriter w, IReadOnlyDictionary<string, JsonElement> p)
    {
        w.WriteStartObject();
        foreach (var kv in p) // preserve authored order
        {
            w.WritePropertyName(kv.Key);
            kv.Value.WriteTo(w);
        }
        w.WriteEndObject();
    }

    // --- value writers ---------------------------------------------------

    private static void WriteVecProperty(Utf8JsonWriter w, string name, Vector3<double> v)
    {
        w.WritePropertyName(name);
        WriteVec(w, v);
    }

    private static void WriteVec(Utf8JsonWriter w, Vector3<double> v)
    {
        // Emit as a single inline token "[x, y, z]" so a point/vector occupies exactly one line
        // (keeps the diff for a coordinate change to one line, and reads cleanly).
        var text = string.Concat("[", FormatFloat(v.X), ", ", FormatFloat(v.Y), ", ", FormatFloat(v.Z), "]");
        w.WriteRawValue(text, skipInputValidation: true);
    }

    private static void WriteFloatProperty(Utf8JsonWriter w, string name, double value)
    {
        w.WritePropertyName(name);
        WriteFloat(w, value);
    }

    private static void WriteFloat(Utf8JsonWriter w, double value) =>
        w.WriteRawValue(FormatFloat(value), skipInputValidation: true);

    private static void WriteNullableString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
            w.WriteNull(name);
        else
            w.WriteString(name, value);
    }

    private static string KindToString(EdgeKind kind) => kind switch
    {
        EdgeKind.Line => "line",
        EdgeKind.Arc => "arc",
        EdgeKind.Circle => "circle",
        EdgeKind.Spline => "spline",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown EdgeKind")
    };

    /// <summary>
    /// Formats a double to canonical JSON-number text: 6 significant digits, '.' decimal,
    /// invariant culture, no trailing-zero variance, no exponent for normal magnitudes.
    /// (Rule 3.) Integers render without a decimal point (e.g. <c>0</c>, <c>1</c>).
    /// </summary>
    public static string FormatFloat(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentException($"Non-finite values are not serializable: {value}", nameof(value));

        // "G6" gives 6 significant digits, round-trip-stable, invariant when given the invariant culture.
        // Normalize -0 to 0.
        if (value == 0d) value = 0d;
        var text = value.ToString("G6", CultureInfo.InvariantCulture);

        // "G6" may emit an exponent ("1.23457E+08") for large/small magnitudes. Expand to plain decimal
        // so the on-disk form is stable, diff-friendly, and locale-free.
        if (text.IndexOf('E') >= 0 || text.IndexOf('e') >= 0)
            text = ExpandExponent(value);

        return text;
    }

    private static string ExpandExponent(double value)
    {
        // Re-render the 6-significant-digit value without an exponent.
        var magnitude = Math.Abs(value);
        var exponent = (int)Math.Floor(Math.Log10(magnitude));
        var decimals = 6 - 1 - exponent;

        if (decimals >= 0)
        {
            // Fractional/small-magnitude: keep exactly the decimals needed for 6 sig digits, then trim.
            var t = value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (t.Contains('.'))
            {
                t = t.TrimEnd('0').TrimEnd('.');
                if (t.Length == 0 || t == "-") t = "0";
            }
            return t;
        }

        // Large magnitude (>= 1e6): 6 sig digits means an integer ending in zeros. Round to 6 sig
        // figures (the trailing digits become structural zeros), then render with no decimal point.
        var scale = Math.Pow(10, -decimals); // = 10^(exponent-5)
        var rounded = Math.Round(value / scale, MidpointRounding.ToEven) * scale;
        return rounded.ToString("F0", CultureInfo.InvariantCulture);
    }
}
