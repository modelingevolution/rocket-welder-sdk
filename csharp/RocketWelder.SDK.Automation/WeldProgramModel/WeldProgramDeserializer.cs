using System.Globalization;
using System.Text.Json;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation.WeldProgramModel;

/// <summary>
/// The canonical reader for <c>program.json</c> (the inverse of <see cref="WeldProgramSerializer"/>).
/// Round-trips losslessly: <c>Serialize(Deserialize(bytes))</c> is byte-identical to <c>bytes</c>
/// for any canonically-written file (AT-A4).
/// </summary>
public static class WeldProgramSerializerReader
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
        if (schema != WeldProgramSerializer.Schema)
            throw new JsonException($"Unsupported schema '{schema}'. Expected '{WeldProgramSerializer.Schema}'.");

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

    private static Datum ReadDatum(JsonElement el)
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
        var process = s.GetProperty("process");
        var weldJob = process.GetProperty("weldJob");
        var torch = s.GetProperty("torchFrame");

        SegmentResolver? resolver = null;
        var resolverEl = s.GetProperty("resolver");
        if (resolverEl.ValueKind != JsonValueKind.Null)
        {
            resolver = new SegmentResolver(
                Mode: resolverEl.GetProperty("mode").GetString()!,
                FeatureRef: ReadNullableString(resolverEl, "featureRef"));
        }

        return new Segment(
            Id: s.GetProperty("id").GetString()!,
            Binding: ReadBinding(s.GetProperty("binding")),
            SubRange: new SubRange(
                subRange[0].GetDouble(),
                subRange[1].GetDouble()),
            Process: new WeldProcess(
                SeamType: process.GetProperty("seamType").GetString()!,
                WeldJob: new WeldJob(
                    Id: weldJob.GetProperty("id").GetInt32(),
                    Params: ReadParams(weldJob.GetProperty("params"))),
                TravelSpeedMmPerS: process.GetProperty("travelSpeedMmPerS").GetDouble()),
            TorchFrame: new TorchFrame(
                StandoffMm: torch.GetProperty("standoffMm").GetDouble(),
                WorkAngleDeg: torch.GetProperty("workAngleDeg").GetDouble(),
                TravelAngleDeg: torch.GetProperty("travelAngleDeg").GetDouble(),
                Technique: torch.GetProperty("technique").GetString()!),
            Resolver: resolver);
    }

    private static EdgeBinding ReadBinding(JsonElement b)
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

    private static VersionInfo ReadVersion(JsonElement v)
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

    private static IReadOnlyDictionary<string, JsonElement> ReadParams(JsonElement el)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var prop in el.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    private static IReadOnlyList<Vector3<double>> ReadVecList(JsonElement el)
    {
        var list = new List<Vector3<double>>();
        foreach (var v in el.EnumerateArray())
            list.Add(ReadVec(v));
        return list;
    }

    private static Vector3<double> ReadVec(JsonElement el) =>
        new(el[0].GetDouble(), el[1].GetDouble(), el[2].GetDouble());

    private static string? ReadNullableString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        return el.GetString();
    }

    private static EdgeKind ParseKind(string s) => s switch
    {
        "line" => EdgeKind.Line,
        "arc" => EdgeKind.Arc,
        "circle" => EdgeKind.Circle,
        "spline" => EdgeKind.Spline,
        _ => throw new JsonException($"Unknown edge kind '{s}'.")
    };
}
