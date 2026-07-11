using System.Text.Json;

namespace RocketWelder.SDK.Operations;

/// <summary>
/// Migrates a <c>rw.weldprogram/1</c> program to the <c>rw.weldprogram/2</c> model (per
/// <c>data-model.md</c> §4 — "the Pass[] wrap"). <c>/2</c> is a structural superset: every <c>/1</c>
/// program migrates mechanically, single-pass, with no loss and <strong>no fabricated values</strong> —
/// anything <c>/1</c> did not carry stays <c>null</c>/absent, never a fake <c>0</c>.
/// <para>
/// The migration is one-way and idempotent; the canonical <c>/2</c> serializer re-emits a migrated
/// program byte-stably (AT-A4 still holds).
/// </para>
/// </summary>
public static class WeldProgramMigrator
{
    /// <summary>The legacy schema id this migrator reads from.</summary>
    public const string SchemaV1 = "rw.weldprogram/1";

    /// <summary>
    /// Lifts a parsed <c>/1</c> <c>program.json</c> root element into the <c>/2</c>
    /// <see cref="WeldProgram"/> model (§4).
    /// </summary>
    public static WeldProgram MigrateV1ToV2(JsonElement root)
    {
        var schema = root.GetProperty("schema").GetString();
        if (schema != SchemaV1)
            throw new JsonException($"WeldProgramMigrator expects '{SchemaV1}', got '{schema}'.");

        var step = root.GetProperty("step");
        var preview = root.GetProperty("preview");

        var segments = new List<Segment>();
        foreach (var s in root.GetProperty("segments").EnumerateArray()) // positional — weld order preserved
            segments.Add(MigrateSegment(s));

        // §4.4: carry binding/subRange/resolver/datum/version unchanged; set schema to /2.
        return new WeldProgram(
            Id: Guid.Parse(root.GetProperty("id").GetString()!),
            Name: root.GetProperty("name").GetString()!,
            Step: new StepRef(step.GetProperty("path").GetString()!, step.GetProperty("sha256").GetString()!),
            Preview: new PreviewRef(preview.GetProperty("path").GetString()!),
            Datum: WeldProgramDeserializer.ReadDatum(root.GetProperty("datum")),
            Segments: segments,
            WeldOrderStrategy: root.GetProperty("weldOrderStrategy").GetString()!,
            Version: WeldProgramDeserializer.ReadVersion(root.GetProperty("version")));
    }

    private static Segment MigrateSegment(JsonElement s)
    {
        var subRange = s.GetProperty("subRange");
        var process = s.GetProperty("process");
        var weldJob = process.GetProperty("weldJob");
        var torch = s.GetProperty("torchFrame");

        // §4.3: wrap the single /1 run as passes[0].
        //  - role defaults "cap" (a lone single-pass run is its own cap) — §4.3.
        //  - process.weldJob.id -> jobRef.id; the old weldJob.params blob is DROPPED (D-D: those
        //    electrical setpoints belong to the device's job, not the file).
        //  - torchFrame -> toolFrame verbatim (field-compatible).
        //  - process.travelSpeedMmPerS -> motion.travelSpeedMmPerS; motion.weave = null (stringer).
        var pass = new Pass(
            Id: "p0",
            Role: PassRole.Cap,
            JobRef: new JobRef(weldJob.GetProperty("id").GetInt32()),
            ToolFrame: new ToolFrame(
                StandoffMm: torch.GetProperty("standoffMm").GetDouble(),
                WorkAngleDeg: torch.GetProperty("workAngleDeg").GetDouble(),
                TravelAngleDeg: torch.GetProperty("travelAngleDeg").GetDouble(),
                Technique: ParseTechnique(torch.GetProperty("technique").GetString()!)),
            Motion: new MotionProfile(
                TravelSpeedMmPerS: process.GetProperty("travelSpeedMmPerS").GetDouble(),
                Weave: null));

        // §4.3: resolver carried unchanged. /1's resolver had {mode, featureRef}; /2 adds tracking (null here).
        SegmentResolver? resolver = null;
        var resolverEl = s.GetProperty("resolver");
        if (resolverEl.ValueKind != JsonValueKind.Null)
        {
            resolver = new SegmentResolver(
                Mode: ParseResolverMode(resolverEl.GetProperty("mode").GetString()!),
                FeatureRef: WeldProgramDeserializer.ReadNullableString(resolverEl, "featureRef"),
                Tracking: null);
        }

        // §4.2: lift seamType; position/weldSize/gas/polarity default null (NOT invented).
        return new Segment(
            Id: s.GetProperty("id").GetString()!,
            Binding: WeldProgramDeserializer.ReadBinding(s.GetProperty("binding")),
            SubRange: new SubRange(subRange[0].GetDouble(), subRange[1].GetDouble()),
            SeamType: ParseSeamType(process.GetProperty("seamType").GetString()!),
            Position: null,
            WeldSize: null,
            Gas: null,
            Polarity: null,
            Passes: new[] { pass },
            Resolver: resolver,
            ExternalAxis: null);
    }

    // /1 used free strings for these; map to the /2 vocabulary. An out-of-vocabulary /1 value is a
    // real data error (loud failure), never silently defaulted.

    private static SeamType ParseSeamType(string s) => s switch
    {
        "fillet" => SeamType.Fillet,
        "butt" => SeamType.Butt,
        "edge" => SeamType.Edge,
        "lap" => SeamType.Lap,
        "corner" => SeamType.Corner,
        "circumferential" => SeamType.Circumferential,
        _ => throw new JsonException($"/1 segment has seamType '{s}' outside the /2 vocabulary.")
    };

    private static Technique ParseTechnique(string s) => s switch
    {
        "push" => Technique.Push,
        "drag" => Technique.Drag,
        "perpendicular" => Technique.Perpendicular,
        _ => throw new JsonException($"/1 torchFrame has technique '{s}' outside the /2 vocabulary.")
    };

    private static ResolverMode ParseResolverMode(string s) => s switch
    {
        "metrology" => ResolverMode.Metrology,
        "adjacent-feature" => ResolverMode.AdjacentFeature,
        "laser-profile" => ResolverMode.LaserProfile,
        "touch-only" => ResolverMode.TouchOnly,
        "seam-tracking" => ResolverMode.SeamTracking,
        _ => throw new JsonException($"/1 resolver has mode '{s}' outside the /2 vocabulary.")
    };
}
