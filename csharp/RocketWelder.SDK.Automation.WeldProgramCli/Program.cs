using System.Text.Json;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Automation.WeldProgramModel;

// Headless CLI for IT-1 tester verification (bash + diff/git diff). No source access required.
//
//   weldprogram canonicalize <program.json> [out.json]
//       Read a program.json, re-serialize canonically. With no out, writes to stdout.
//       Use to prove byte-identical round-trip (AT-A4) and one-line field diffs (AT-E3).
//
//   weldprogram resolve <program.json> <topology.json>
//       Re-resolve every segment's EdgeBinding against the given topology; prints
//       "<segmentId> <edgeId|UNRESOLVED>" per line (AT-E1/E2). Exit 0 always (UNRESOLVED is data).
//
//   weldprogram sample [out.json]
//       Emit a built-in canonical sample program.json (a two-segment plate program), for
//       bash-driven round-trip / diff verification without any other input.

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: weldprogram <canonicalize|resolve> ...");
    return 2;
}

try
{
    switch (args[0])
    {
        case "canonicalize":
            return Canonicalize(args);
        case "resolve":
            return Resolve(args);
        case "sample":
            return Sample(args);
        default:
            Console.Error.WriteLine($"unknown command '{args[0]}'");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int Canonicalize(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: weldprogram canonicalize <program.json> [out.json]");
        return 2;
    }

    var bytes = File.ReadAllBytes(args[1]);
    var program = WeldProgramSerializerReader.Deserialize(bytes);
    var canonical = WeldProgramSerializer.SerializeToUtf8Bytes(program);

    if (args.Length >= 3)
    {
        File.WriteAllBytes(args[2], canonical);
    }
    else
    {
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(canonical, 0, canonical.Length);
    }

    return 0;
}

static int Resolve(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: weldprogram resolve <program.json> <topology.json>");
        return 2;
    }

    var program = WeldProgramSerializerReader.Deserialize(File.ReadAllBytes(args[1]));
    var topology = TopologyJson.Read(File.ReadAllText(args[2]));
    var resolver = new EdgeBindingResolver();

    foreach (var segment in program.Segments)
    {
        var edgeId = resolver.Resolve(segment.Binding, topology);
        Console.WriteLine($"{segment.Id} {edgeId}");
    }

    return 0;
}

static int Sample(string[] args)
{
    var bytes = WeldProgramSerializer.SerializeToUtf8Bytes(SampleProgram.Build());
    if (args.Length >= 2)
    {
        File.WriteAllBytes(args[1], bytes);
    }
    else
    {
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(bytes, 0, bytes.Length);
    }
    return 0;
}

/// <summary>
/// A built-in canonical sample program (a two-segment plate program bound to edges of
/// <see cref="SampleTopology"/>), for bash-driven round-trip / diff verification.
/// </summary>
internal static class SampleProgram
{
    public static WeldProgram Build()
    {
        var resolver = new EdgeBindingResolver();
        var topo = SampleTopology.Plate();
        var b0 = resolver.Fingerprint(topo.Get("E0")!);
        var b2 = resolver.Fingerprint(topo.Get("E2")!);

        var jobParams = (IReadOnlyDictionary<string, JsonElement>)new Dictionary<string, JsonElement>();

        var s0 = new Segment("s0", b0, new SubRange(0.0, 1.0),
            new WeldProcess("fillet", new WeldJob(17, jobParams), 6.5),
            new TorchFrame(12.0, 45.0, 10.0, "drag"),
            new SegmentResolver("metrology", "E7"));

        var s1 = new Segment("s1", b2, new SubRange(0.0, 0.75),
            new WeldProcess("butt", new WeldJob(4, jobParams), 8.25),
            new TorchFrame(10.0, 90.0, 0.0, "perpendicular"),
            null);

        return new WeldProgram(
            Guid.Parse("f3b1c0de-1111-2222-3333-444455556666"),
            "T-bracket 6mm fillet",
            new StepRef("tbracket.step", "abc123sha256def"),
            new PreviewRef("tbracket.preview.jpg"),
            new Datum("three-point", new[]
            {
                new DatumPoint("d0", new Vector3<double>(0, 0, 0), "F12", null),
                new DatumPoint("d1", new Vector3<double>(100, 0, 0), null, "E7"),
                new DatumPoint("d2", new Vector3<double>(0, 50, 10), "F30", null)
            }),
            new[] { s0, s1 },
            "distortion-balanced",
            new VersionInfo("operator-id",
                new DateTimeOffset(2026, 6, 4, 18, 0, 0, TimeSpan.Zero),
                "abc123", "rw2 1.2.3"));
    }
}

/// <summary>A built-in plate topology matching <see cref="SampleProgram"/>.</summary>
internal static class SampleTopology
{
    public static Topology Plate()
    {
        var nUp = new Vector3<double>(0, 0, 1);
        var nLeft = new Vector3<double>(-1, 0, 0);
        var nRight = new Vector3<double>(1, 0, 0);
        var nFront = new Vector3<double>(0, -1, 0);
        var nBack = new Vector3<double>(0, 1, 0);
        return new Topology(new[]
        {
            Line("E0", (0, 0, 0), (100, 0, 0), nUp, nFront),
            Line("E1", (100, 0, 0), (100, 50, 0), nUp, nRight),
            Line("E2", (100, 50, 0), (0, 50, 0), nUp, nBack),
            Line("E3", (0, 50, 0), (0, 0, 0), nUp, nLeft),
        });
    }

    private static EdgeTopology Line(string id, (double, double, double) a, (double, double, double) b,
        Vector3<double> n0, Vector3<double> n1)
    {
        var pa = new Vector3<double>(a.Item1, a.Item2, a.Item3);
        var pb = new Vector3<double>(b.Item1, b.Item2, b.Item3);
        return new EdgeTopology(id, EdgeKind.Line, new[] { pa, pb }, (pb - pa).Length, new[] { n0, n1 });
    }
}

/// <summary>
/// Minimal reader for a topology JSON file (the SDK-side mirror of the geometry service topology):
/// <c>{ "bboxDiagonalMm"?: number, "edges": [ { "edgeId", "kind", "length", "polyline": [[x,y,z]...],
/// "adjacentFaceNormals": [[x,y,z]...] } ] }</c>. Used only by the CLI for bash-driven testing.
/// </summary>
internal static class TopologyJson
{
    public static Topology Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var edges = new List<EdgeTopology>();
        foreach (var e in root.GetProperty("edges").EnumerateArray())
        {
            edges.Add(new EdgeTopology(
                EdgeId: e.GetProperty("edgeId").GetString()!,
                Kind: ParseKind(e.GetProperty("kind").GetString()!),
                Polyline: ReadVecList(e.GetProperty("polyline")),
                Length: e.GetProperty("length").GetDouble(),
                AdjacentFaceNormals: ReadVecList(e.GetProperty("adjacentFaceNormals"))));
        }

        double? diag = root.TryGetProperty("bboxDiagonalMm", out var d) && d.ValueKind == JsonValueKind.Number
            ? d.GetDouble()
            : null;

        return new Topology(edges, diag);
    }

    private static List<Vector3<double>> ReadVecList(JsonElement el)
    {
        var list = new List<Vector3<double>>();
        foreach (var v in el.EnumerateArray())
            list.Add(new Vector3<double>(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble()));
        return list;
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
