using System.Globalization;
using System.Text.Json;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Operations;

/// <summary>
/// The canonical reader for <c>program.json</c> (the inverse of <see cref="WeldProgramSerializer"/>) for
/// schema <c>rw.weldprogram/2</c>. Round-trips losslessly: <c>Serialize(Deserialize(bytes))</c> is
/// byte-identical to <c>bytes</c> for any canonically-written <c>/2</c> file (AT-A4).
/// <para>
/// A <c>/1</c> file is migrated to the <c>/2</c> model on read via <see cref="WeldProgramMigrator"/>
/// (per <c>data-model.md</c> §4) — no value is fabricated; absent fields stay <c>null</c>.
/// </para>
/// </summary>
public static class WeldProgramDeserializer
{
    /// <summary>Reads a <see cref="WeldProgram"/> from canonical UTF-8 <c>program.json</c> bytes.</summary>
    public static WeldProgram Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        using var doc = JsonDocument.Parse(utf8Json.ToArray());
        return ReadProgram(doc.RootElement);
    }

    /// <summary>Reads a <see cref="WeldProgram"/> from a canonical <c>program.json</c> string.</summary>
    public static WeldProgram Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ReadProgram(doc.RootElement);
    }

    private static WeldProgram ReadProgram(JsonElement root)
    {
        var schema = root.GetProperty("schema").GetString()
                     ?? throw new JsonException("Missing 'schema'.");

        // /1 → /2 migration (§4): a /1 file is lifted to the /2 model before reading.
        if (schema == WeldProgramMigrator.SchemaV1)
            return WeldProgramMigrator.MigrateV1ToV2(root);

        if (schema != WeldProgramSerializer.Schema)
            throw new JsonException(
                $"Unsupported schema '{schema}'. Expected '{WeldProgramSerializer.Schema}' " +
                $"(or '{WeldProgramMigrator.SchemaV1}' for migration).");

        var step = root.GetProperty("step");
        var preview = root.GetProperty("preview");

        return new WeldProgram(
            Id: Guid.Parse(root.GetProperty("id").GetString()!),
            Name: root.GetProperty("name").GetString()!,
            Step: new StepRef(step.GetProperty("path").GetString()!, step.GetProperty("sha256").GetString()!),
            Preview: new PreviewRef(preview.GetProperty("path").GetString()!),
            Datum: ReadDatum(root.GetProperty("datum")),
            Segments: ReadSegments(root.GetProperty("segments")),
            WeldOrderStrategy: root.GetProperty("weldOrderStrategy").GetString()!,
            Version: ReadVersion(root.GetProperty("version")));
    }

    internal static Datum ReadDatum(JsonElement el)
    {
        var points = new List<DatumPoint>();
        foreach (var p in el.GetProperty("points").EnumerateArray())
        {
            points.Add(new DatumPoint(
                Id: p.GetProperty("id").GetString()!,
                P: ReadVec(p.GetProperty("p")),
                OnFace: ReadNullableString(p, "onFace"),
                OnEdge: ReadNullableString(p, "onEdge")));
        }
        return new Datum(el.GetProperty("scheme").GetString()!, points);
    }

    private static IReadOnlyList<Segment> ReadSegments(JsonElement el)
    {
        var segments = new List<Segment>();
        foreach (var s in el.EnumerateArray())
            segments.Add(ReadSegment(s));
        return segments;
    }

    private static Segment ReadSegment(JsonElement s)
    {
        var subRange = s.GetProperty("subRange");

        return new Segment(
            Id: s.GetProperty("id").GetString()!,
            Binding: ReadBinding(s.GetProperty("binding")),
            SubRange: new SubRange(subRange[0].GetDouble(), subRange[1].GetDouble()),
            SeamType: ParseSeamType(s.GetProperty("seamType").GetString()!),
            Position: ReadNullableEnum(s, "position", ParsePosition),
            WeldSize: ReadWeldSize(s.GetProperty("weldSize")),
            Gas: ReadGas(s.GetProperty("gas")),
            Polarity: ReadNullableEnum(s, "polarity", ParsePolarity),
            Passes: ReadPasses(s.GetProperty("passes")),
            Resolver: ReadResolver(s.GetProperty("resolver")),
            ExternalAxis: ReadExternalAxis(s.GetProperty("externalAxis")));
    }

    private static WeldSize? ReadWeldSize(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Null)
            return null;

        FilletSize? fillet = null;
        var filletEl = el.GetProperty("fillet");
        if (filletEl.ValueKind != JsonValueKind.Null)
        {
            fillet = new FilletSize(
                LegMm: ReadNullableDouble(filletEl, "legMm"),
                ThroatMm: ReadNullableDouble(filletEl, "throatMm"));
        }

        ButtSize? butt = null;
        var buttEl = el.GetProperty("butt");
        if (buttEl.ValueKind != JsonValueKind.Null)
        {
            butt = new ButtSize(
                GrooveAngleDeg: buttEl.GetProperty("grooveAngleDeg").GetDouble(),
                RootGapMm: buttEl.GetProperty("rootGapMm").GetDouble(),
                RootFaceMm: buttEl.GetProperty("rootFaceMm").GetDouble(),
                Prep: buttEl.GetProperty("prep").GetString()!);
        }

        return new WeldSize(fillet, butt);
    }

    private static Gas? ReadGas(JsonElement el) =>
        el.ValueKind == JsonValueKind.Null
            ? null
            : new Gas(el.GetProperty("mix").GetString()!, el.GetProperty("flowLpm").GetDouble());

    private static IReadOnlyList<Pass> ReadPasses(JsonElement el)
    {
        var passes = new List<Pass>();
        foreach (var p in el.EnumerateArray())
            passes.Add(ReadPass(p));
        return passes;
    }

    private static Pass ReadPass(JsonElement p)
    {
        var jobRef = p.GetProperty("jobRef");
        var tool = p.GetProperty("toolFrame");
        var motion = p.GetProperty("motion");

        Weave? weave = null;
        var weaveEl = motion.GetProperty("weave");
        if (weaveEl.ValueKind != JsonValueKind.Null)
        {
            weave = new Weave(
                AmplitudeMm: weaveEl.GetProperty("amplitudeMm").GetDouble(),
                FrequencyHz: weaveEl.GetProperty("frequencyHz").GetDouble(),
                EdgeDwellMs: weaveEl.GetProperty("edgeDwellMs").GetDouble(),
                Pattern: weaveEl.GetProperty("pattern").GetString()!);
        }

        return new Pass(
            Id: p.GetProperty("id").GetString()!,
            Role: ParseRole(p.GetProperty("role").GetString()!),
            JobRef: new JobRef(jobRef.GetProperty("id").GetInt32()),
            ToolFrame: new ToolFrame(
                StandoffMm: tool.GetProperty("standoffMm").GetDouble(),
                WorkAngleDeg: tool.GetProperty("workAngleDeg").GetDouble(),
                TravelAngleDeg: tool.GetProperty("travelAngleDeg").GetDouble(),
                Technique: ParseTechnique(tool.GetProperty("technique").GetString()!)),
            Motion: new MotionProfile(
                TravelSpeedMmPerS: motion.GetProperty("travelSpeedMmPerS").GetDouble(),
                Weave: weave));
    }

    private static SegmentResolver? ReadResolver(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Null)
            return null;

        Tracking? tracking = null;
        var trackingEl = el.GetProperty("tracking");
        if (trackingEl.ValueKind != JsonValueKind.Null)
        {
            tracking = new Tracking(
                Mode: ParseTrackingMode(trackingEl.GetProperty("mode").GetString()!),
                GapFill: trackingEl.GetProperty("gapFill").GetBoolean());
        }

        return new SegmentResolver(
            Mode: ParseResolverMode(el.GetProperty("mode").GetString()!),
            FeatureRef: ReadNullableString(el, "featureRef"),
            Tracking: tracking);
    }

    private static ExternalAxis? ReadExternalAxis(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Null)
            return null;
        return new ExternalAxis(
            Device: el.GetProperty("device").GetString()!,
            Axis: el.GetProperty("axis").GetString()!,
            AngleDeg: el.GetProperty("angleDeg").GetDouble());
    }

    internal static EdgeBinding ReadBinding(JsonElement b)
    {
        return new EdgeBinding(
            EdgeIdHint: b.GetProperty("edgeIdHint").GetString()!,
            Kind: ParseKind(b.GetProperty("kind").GetString()!),
            LengthMm: b.GetProperty("lengthMm").GetDouble(),
            Midpoint: ReadVec(b.GetProperty("midpoint")),
            TangentAtMid: ReadVec(b.GetProperty("tangentAtMid")),
            Endpoints: ReadVecList(b.GetProperty("endpoints")),
            AdjFaceNormals: ReadVecList(b.GetProperty("adjFaceNormals")));
    }

    internal static VersionInfo ReadVersion(JsonElement v)
    {
        return new VersionInfo(
            AuthoredBy: v.GetProperty("authoredBy").GetString()!,
            AuthoredAtUtc: DateTimeOffset.Parse(
                v.GetProperty("authoredAtUtc").GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            ParentCommit: ReadNullableString(v, "parentCommit"),
            AppVersion: v.GetProperty("appVersion").GetString()!);
    }

    internal static IReadOnlyList<Vector3<double>> ReadVecList(JsonElement el)
    {
        var list = new List<Vector3<double>>();
        foreach (var v in el.EnumerateArray())
            list.Add(ReadVec(v));
        return list;
    }

    internal static Vector3<double> ReadVec(JsonElement el) =>
        new(el[0].GetDouble(), el[1].GetDouble(), el[2].GetDouble());

    internal static string? ReadNullableString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        return el.GetString();
    }

    private static double? ReadNullableDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        return el.GetDouble();
    }

    private static T? ReadNullableEnum<T>(JsonElement parent, string name, Func<string, T> parse)
        where T : struct
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        return parse(el.GetString()!);
    }

    // --- canonical string -> enum maps (§2 vocabularies) ----------------

    private static EdgeKind ParseKind(string s) => s switch
    {
        "line" => EdgeKind.Line,
        "arc" => EdgeKind.Arc,
        "circle" => EdgeKind.Circle,
        "spline" => EdgeKind.Spline,
        _ => throw new JsonException($"Unknown edge kind '{s}'.")
    };

    private static SeamType ParseSeamType(string s) => s switch
    {
        "fillet" => SeamType.Fillet,
        "butt" => SeamType.Butt,
        "edge" => SeamType.Edge,
        "lap" => SeamType.Lap,
        "corner" => SeamType.Corner,
        "circumferential" => SeamType.Circumferential,
        _ => throw new JsonException($"Unknown seamType '{s}'.")
    };

    private static WeldPosition ParsePosition(string s) => s switch
    {
        "PA" => WeldPosition.PA,
        "PB" => WeldPosition.PB,
        "PC" => WeldPosition.PC,
        "PD" => WeldPosition.PD,
        "PE" => WeldPosition.PE,
        "PF" => WeldPosition.PF,
        "PG" => WeldPosition.PG,
        _ => throw new JsonException($"Unknown position '{s}'.")
    };

    private static PassRole ParseRole(string s) => s switch
    {
        "root" => PassRole.Root,
        "hot" => PassRole.Hot,
        "fill" => PassRole.Fill,
        "cap" => PassRole.Cap,
        _ => throw new JsonException($"Unknown pass role '{s}'.")
    };

    private static Technique ParseTechnique(string s) => s switch
    {
        "push" => Technique.Push,
        "drag" => Technique.Drag,
        "perpendicular" => Technique.Perpendicular,
        _ => throw new JsonException($"Unknown technique '{s}'.")
    };

    private static Polarity ParsePolarity(string s) => s switch
    {
        "DCEP" => Polarity.DCEP,
        "DCEN" => Polarity.DCEN,
        "AC" => Polarity.AC,
        _ => throw new JsonException($"Unknown polarity '{s}'.")
    };

    private static ResolverMode ParseResolverMode(string s) => s switch
    {
        "metrology" => ResolverMode.Metrology,
        "adjacent-feature" => ResolverMode.AdjacentFeature,
        "laser-profile" => ResolverMode.LaserProfile,
        "touch-only" => ResolverMode.TouchOnly,
        "seam-tracking" => ResolverMode.SeamTracking,
        _ => throw new JsonException($"Unknown resolver mode '{s}'.")
    };

    private static TrackingMode ParseTrackingMode(string s) => s switch
    {
        "tast" => TrackingMode.Tast,
        "laser" => TrackingMode.Laser,
        "touch" => TrackingMode.Touch,
        _ => throw new JsonException($"Unknown tracking mode '{s}'.")
    };
}
