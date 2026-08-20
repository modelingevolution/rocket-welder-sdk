using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Operations;

/// <summary>
/// The single canonical serializer for <c>program.json</c> (schema <c>rw.weldprogram/2</c>, per
/// <c>data-model.md</c> §2). Nothing else writes <c>program.json</c> (ADR-012). Guarantees
/// byte-identical re-serialization of an unchanged in-memory program (AT-A4) by enforcing the §2
/// canonical-serialization rules:
/// <list type="number">
/// <item>fixed schema key order (never alphabetical-by-runtime);</item>
/// <item>segments serialized positionally in weld order, passes positionally in deposition order,
///   datum.points in touch order (never re-sorted);</item>
/// <item>floats to 6 significant digits, '.' decimal, invariant culture, no trailing-zero variance;</item>
/// <item>UTF-8, LF line endings, 2-space indent, trailing newline, no BOM;</item>
/// <item>re-serializing an unchanged program is byte-identical.</item>
/// </list>
/// </summary>
public static class WeldProgramSerializer
{
    /// <summary>Schema id + major version emitted as the first key (per §2).</summary>
    public const string Schema = "rw.weldprogram/2";

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
        foreach (var segment in p.Segments) // positional — weld order, never re-sorted (§2 rule 2)
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

        // ── segment-level welding facts (overlay) ──
        w.WriteString("seamType", SeamTypeToString(s.SeamType));
        if (s.Position is { } position)
            w.WriteString("position", PositionToString(position));
        else
            w.WriteNull("position");
        WriteWeldSize(w, s.WeldSize);
        WriteGas(w, s.Gas);
        if (s.Polarity is { } polarity)
            w.WriteString("polarity", PolarityToString(polarity));
        else
            w.WriteNull("polarity");

        // ── ordered pass list (positional; §2 rule 2) ──
        w.WritePropertyName("passes");
        w.WriteStartArray();
        foreach (var pass in s.Passes) // deposition order, never re-sorted
            WritePass(w, pass);
        w.WriteEndArray();

        WriteResolver(w, s.Resolver);

        w.WritePropertyName("externalAxis");
        if (s.ExternalAxis is null)
        {
            w.WriteNullValue();
        }
        else
        {
            w.WriteStartObject();
            w.WriteString("device", s.ExternalAxis.Device);
            w.WriteString("axis", s.ExternalAxis.Axis);
            WriteFloatProperty(w, "angleDeg", s.ExternalAxis.AngleDeg);
            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    private static void WriteWeldSize(Utf8JsonWriter w, WeldSize? size)
    {
        w.WritePropertyName("weldSize");
        if (size is null)
        {
            w.WriteNullValue();
            return;
        }

        w.WriteStartObject();

        w.WritePropertyName("fillet");
        if (size.Fillet is null)
        {
            w.WriteNullValue();
        }
        else
        {
            w.WriteStartObject();
            // exactly one of leg/throat is set per §2; emit only the present one (absent stays absent)
            if (size.Fillet.LegMm is { } leg)
                WriteFloatProperty(w, "legMm", leg);
            if (size.Fillet.ThroatMm is { } throat)
                WriteFloatProperty(w, "throatMm", throat);
            w.WriteEndObject();
        }

        w.WritePropertyName("butt");
        if (size.Butt is null)
        {
            w.WriteNullValue();
        }
        else
        {
            w.WriteStartObject();
            WriteFloatProperty(w, "grooveAngleDeg", size.Butt.GrooveAngleDeg);
            WriteFloatProperty(w, "rootGapMm", size.Butt.RootGapMm);
            WriteFloatProperty(w, "rootFaceMm", size.Butt.RootFaceMm);
            w.WriteString("prep", size.Butt.Prep);
            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    private static void WriteGas(Utf8JsonWriter w, Gas? gas)
    {
        w.WritePropertyName("gas");
        if (gas is null)
        {
            w.WriteNullValue();
            return;
        }
        w.WriteStartObject();
        w.WriteString("mix", gas.Mix);
        WriteFloatProperty(w, "flowLpm", gas.FlowLpm);
        w.WriteEndObject();
    }

    private static void WritePass(Utf8JsonWriter w, Pass pass)
    {
        w.WriteStartObject();
        w.WriteString("id", pass.Id);
        w.WriteString("role", RoleToString(pass.Role));

        w.WritePropertyName("jobRef");
        w.WriteStartObject();
        w.WriteNumber("id", pass.JobRef.Id);
        w.WriteEndObject();

        w.WritePropertyName("toolFrame");
        w.WriteStartObject();
        WriteFloatProperty(w, "standoffMm", pass.ToolFrame.StandoffMm);
        WriteFloatProperty(w, "workAngleDeg", pass.ToolFrame.WorkAngleDeg);
        WriteFloatProperty(w, "travelAngleDeg", pass.ToolFrame.TravelAngleDeg);
        w.WriteString("technique", TechniqueToString(pass.ToolFrame.Technique));
        w.WriteEndObject();

        w.WritePropertyName("motion");
        w.WriteStartObject();
        WriteFloatProperty(w, "travelSpeedMmPerS", pass.Motion.TravelSpeedMmPerS);
        w.WritePropertyName("weave");
        if (pass.Motion.Weave is null)
        {
            w.WriteNullValue();
        }
        else
        {
            w.WriteStartObject();
            WriteFloatProperty(w, "amplitudeMm", pass.Motion.Weave.AmplitudeMm);
            WriteFloatProperty(w, "frequencyHz", pass.Motion.Weave.FrequencyHz);
            WriteFloatProperty(w, "edgeDwellMs", pass.Motion.Weave.EdgeDwellMs);
            w.WriteString("pattern", pass.Motion.Weave.Pattern);
            w.WriteEndObject();
        }
        w.WriteEndObject();

        w.WriteEndObject();
    }

    private static void WriteResolver(Utf8JsonWriter w, SegmentResolver? resolver)
    {
        w.WritePropertyName("resolver");
        if (resolver is null)
        {
            w.WriteNullValue();
            return;
        }

        w.WriteStartObject();
        w.WriteString("mode", ResolverModeToString(resolver.Mode));
        WriteNullableString(w, "featureRef", resolver.FeatureRef);

        w.WritePropertyName("tracking");
        if (resolver.Tracking is null)
        {
            w.WriteNullValue();
        }
        else
        {
            w.WriteStartObject();
            w.WriteString("mode", TrackingModeToString(resolver.Tracking.Mode));
            w.WriteBoolean("gapFill", resolver.Tracking.GapFill);
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

    // --- enum <-> canonical string maps (§2 vocabularies) ---------------

    private static string KindToString(EdgeKind kind) => kind switch
    {
        EdgeKind.Line => "line",
        EdgeKind.Arc => "arc",
        EdgeKind.Circle => "circle",
        EdgeKind.Spline => "spline",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown EdgeKind")
    };

    private static string SeamTypeToString(SeamType seam) => seam switch
    {
        SeamType.Fillet => "fillet",
        SeamType.Butt => "butt",
        SeamType.Edge => "edge",
        SeamType.Lap => "lap",
        SeamType.Corner => "corner",
        SeamType.Circumferential => "circumferential",
        _ => throw new ArgumentOutOfRangeException(nameof(seam), seam, "Unknown SeamType")
    };

    private static string PositionToString(WeldPosition position) => position switch
    {
        WeldPosition.PA => "PA",
        WeldPosition.PB => "PB",
        WeldPosition.PC => "PC",
        WeldPosition.PD => "PD",
        WeldPosition.PE => "PE",
        WeldPosition.PF => "PF",
        WeldPosition.PG => "PG",
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown WeldPosition")
    };

    private static string RoleToString(PassRole role) => role switch
    {
        PassRole.Root => "root",
        PassRole.Hot => "hot",
        PassRole.Fill => "fill",
        PassRole.Cap => "cap",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown PassRole")
    };

    private static string TechniqueToString(Technique technique) => technique switch
    {
        Technique.Push => "push",
        Technique.Drag => "drag",
        Technique.Perpendicular => "perpendicular",
        _ => throw new ArgumentOutOfRangeException(nameof(technique), technique, "Unknown Technique")
    };

    private static string PolarityToString(Polarity polarity) => polarity switch
    {
        Polarity.DCEP => "DCEP",
        Polarity.DCEN => "DCEN",
        Polarity.AC => "AC",
        _ => throw new ArgumentOutOfRangeException(nameof(polarity), polarity, "Unknown Polarity")
    };

    private static string ResolverModeToString(ResolverMode mode) => mode switch
    {
        ResolverMode.Metrology => "metrology",
        ResolverMode.AdjacentFeature => "adjacent-feature",
        ResolverMode.LaserProfile => "laser-profile",
        ResolverMode.TouchOnly => "touch-only",
        ResolverMode.SeamTracking => "seam-tracking",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ResolverMode")
    };

    private static string TrackingModeToString(TrackingMode mode) => mode switch
    {
        TrackingMode.Tast => "tast",
        TrackingMode.Laser => "laser",
        TrackingMode.Touch => "touch",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown TrackingMode")
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
