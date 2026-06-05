using System.Text.Json;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Operations;

// Headless CLI for IT-1 tester verification (bash + diff/git diff). No source access required.
// Run `weldprogram --help` (or any command with --help) for usage and the topology schema.

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    PrintTopLevelHelp();
    return args.Length == 0 ? 2 : 0;
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
        case "sample-topology":
            return SampleTopologyCmd(args);
        default:
            Console.Error.WriteLine($"unknown command '{args[0]}'");
            PrintTopLevelHelp();
            return 2;
    }
}
catch (TopologyFormatException ex)
{
    // Friendly, field-named error for malformed topology input (no raw KeyNotFound).
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static bool WantsHelp(string[] args) =>
    Array.Exists(args, a => a is "--help" or "-h");

static int Canonicalize(string[] args)
{
    if (WantsHelp(args) || args.Length < 2)
    {
        Console.Error.WriteLine(
            "usage: weldprogram canonicalize <program.json> [out.json]\n" +
            "  Read a program.json and re-serialize it canonically (byte-stable). With no\n" +
            "  out file, writes to stdout. Proves byte-identical round-trip (AT-A4) and\n" +
            "  one-line field diffs (AT-E3).");
        return WantsHelp(args) ? 0 : 2;
    }

    var bytes = File.ReadAllBytes(args[1]);
    var program = WeldProgramDeserializer.Deserialize(bytes);
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
    if (WantsHelp(args) || args.Length < 3)
    {
        Console.Error.WriteLine(
            "usage: weldprogram resolve <program.json> <topology.json>\n" +
            "  Re-resolve every segment's EdgeBinding against the live topology and print\n" +
            "  \"<segmentId> <edgeId|UNRESOLVED>\" per line (AT-E1/E2). Exit 0 always —\n" +
            "  UNRESOLVED is data, not an error.\n\n" +
            TopologyJson.SchemaHelp);
        return WantsHelp(args) ? 0 : 2;
    }

    var program = WeldProgramDeserializer.Deserialize(File.ReadAllBytes(args[1]));
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
    if (WantsHelp(args))
    {
        Console.Error.WriteLine(
            "usage: weldprogram sample [out.json]\n" +
            "  Emit a built-in canonical sample program.json (a two-segment plate program).\n" +
            "  Pairs with `sample-topology`: `resolve sample.json topology.json` binds s0->E0, s1->E2.");
        return 0;
    }

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

static int SampleTopologyCmd(string[] args)
{
    if (WantsHelp(args))
    {
        Console.Error.WriteLine(
            "usage: weldprogram sample-topology [out.json]\n" +
            "  Emit a valid example topology.json (the plate that `sample` was authored against),\n" +
            "  in the documented schema below. `resolve <sample program> <this topology>` binds\n" +
            "  every segment.\n\n" +
            TopologyJson.SchemaHelp);
        return 0;
    }

    var json = TopologyJson.SampleTopologyJson();
    if (args.Length >= 2)
    {
        File.WriteAllText(args[1], json);
    }
    else
    {
        Console.Out.Write(json);
    }
    return 0;
}

static void PrintTopLevelHelp()
{
    Console.Error.WriteLine(
        "weldprogram — headless IT-1 weld-program model CLI\n\n" +
        "commands:\n" +
        "  canonicalize <program.json> [out.json]   re-serialize a program canonically (AT-A4/E3)\n" +
        "  resolve <program.json> <topology.json>   re-bind every segment's edge (AT-E1/E2)\n" +
        "  sample [out.json]                        emit a built-in sample program.json\n" +
        "  sample-topology [out.json]               emit a matching example topology.json\n\n" +
        "Run `weldprogram <command> --help` for details and the topology schema.");
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

/// <summary>Raised for a malformed topology file; carries a human-readable, field-named message.</summary>
internal sealed class TopologyFormatException(string message) : Exception(message);

/// <summary>
/// Robust reader for a topology JSON file. Aligns to the integration-contract §3 Edge schema
/// (<c>edgeId, kind, polyline[], length, closed, adjacentFaces:[faceId…]</c>). Because the
/// <see cref="EdgeBindingResolver"/> fingerprint needs adjacent-face NORMALS (a normal is only
/// defined at a point + chosen face — integration-contract §3), the topology file supplies the
/// normals one of two documented ways per edge:
/// <list type="bullet">
/// <item><c>adjacentFaceNormals: [[x,y,z]…]</c> — the normals directly (simplest for tests); OR</item>
/// <item><c>adjacentFaces: [faceId…]</c> — face ids resolved against a top-level
///       <c>faces: [{ faceId, normal:[x,y,z] }…]</c> map.</item>
/// </list>
/// Every field access is guarded and reports the offending edge/field by name.
/// </summary>
internal static class TopologyJson
{
    public const string SchemaHelp =
        "topology.json schema (model-frame mm; aligns to integration-contract §3 Edge):\n" +
        "  {\n" +
        "    \"bboxDiagonalMm\": 111.8,            // optional; auto-computed from polylines if omitted\n" +
        "    \"faces\": [                            // optional; only needed if edges use \"adjacentFaces\"\n" +
        "      { \"faceId\": \"F1\", \"normal\": [0,0,1] }\n" +
        "    ],\n" +
        "    \"edges\": [\n" +
        "      {\n" +
        "        \"edgeId\": \"E0\",\n" +
        "        \"kind\": \"line\",                 // line|arc|circle|spline\n" +
        "        \"polyline\": [[0,0,0],[100,0,0]],  // >= 2 points, vertex-to-vertex\n" +
        "        \"length\": 100,\n" +
        "        \"closed\": false,                  // accepted, ignored by the fingerprint\n" +
        "        // ONE of the following two supplies the adjacent-face normals:\n" +
        "        \"adjacentFaceNormals\": [[0,0,1],[0,-1,0]]   // (a) normals directly, OR\n" +
        "        // \"adjacentFaces\": [\"F1\",\"F2\"]            // (b) face ids -> resolved via \"faces\"\n" +
        "      }\n" +
        "    ]\n" +
        "  }\n" +
        "Get a ready-to-run example with: weldprogram sample-topology";

    public static Topology Read(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new TopologyFormatException($"topology is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new TopologyFormatException("topology root must be a JSON object with an \"edges\" array.");

            if (!root.TryGetProperty("edges", out var edgesEl) || edgesEl.ValueKind != JsonValueKind.Array)
                throw new TopologyFormatException("topology must have an \"edges\" array. " + SchemaHelp);

            var faceNormals = ReadFaceMap(root);

            var edges = new List<EdgeTopology>();
            var index = 0;
            foreach (var e in edgesEl.EnumerateArray())
            {
                edges.Add(ReadEdge(e, index, faceNormals));
                index++;
            }

            double? diag = root.TryGetProperty("bboxDiagonalMm", out var d) && d.ValueKind == JsonValueKind.Number
                ? d.GetDouble()
                : null;

            return new Topology(edges, diag);
        }
    }

    private static Dictionary<string, Vector3<double>> ReadFaceMap(JsonElement root)
    {
        var map = new Dictionary<string, Vector3<double>>();
        if (!root.TryGetProperty("faces", out var facesEl) || facesEl.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var f in facesEl.EnumerateArray())
        {
            var faceId = GetString(f, "faceId", "faces[]")
                         ?? throw new TopologyFormatException("a faces[] entry is missing \"faceId\".");
            if (!f.TryGetProperty("normal", out var n))
                throw new TopologyFormatException($"face '{faceId}' is missing \"normal\": [x,y,z].");
            map[faceId] = ReadVec(n, $"faces['{faceId}'].normal");
        }
        return map;
    }

    private static EdgeTopology ReadEdge(JsonElement e, int index, Dictionary<string, Vector3<double>> faceNormals)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new TopologyFormatException($"edges[{index}] must be a JSON object.");

        var edgeId = GetString(e, "edgeId", $"edges[{index}]")
                     ?? throw new TopologyFormatException($"edges[{index}] is missing \"edgeId\".");
        var where = $"edge '{edgeId}'";

        var kindStr = GetString(e, "kind", where)
                      ?? throw new TopologyFormatException($"{where} is missing \"kind\" (line|arc|circle|spline).");
        var kind = ParseKind(kindStr, where);

        if (!e.TryGetProperty("polyline", out var polyEl) || polyEl.ValueKind != JsonValueKind.Array)
            throw new TopologyFormatException($"{where} is missing \"polyline\": [[x,y,z]…].");
        var polyline = ReadVecList(polyEl, $"{where}.polyline");
        if (polyline.Count < 2)
            throw new TopologyFormatException($"{where} \"polyline\" needs >= 2 points (has {polyline.Count}).");

        if (!e.TryGetProperty("length", out var lenEl) || lenEl.ValueKind != JsonValueKind.Number)
            throw new TopologyFormatException($"{where} is missing numeric \"length\".");
        var length = lenEl.GetDouble();

        var normals = ReadAdjacentNormals(e, where, faceNormals);

        return new EdgeTopology(edgeId, kind, polyline, length, normals);
    }

    private static IReadOnlyList<Vector3<double>> ReadAdjacentNormals(
        JsonElement e, string where, Dictionary<string, Vector3<double>> faceNormals)
    {
        // Form (a): normals given directly.
        if (e.TryGetProperty("adjacentFaceNormals", out var nEl) && nEl.ValueKind == JsonValueKind.Array)
            return ReadVecList(nEl, $"{where}.adjacentFaceNormals");

        // Form (b): face ids resolved against the top-level faces[] map.
        if (e.TryGetProperty("adjacentFaces", out var fEl) && fEl.ValueKind == JsonValueKind.Array)
        {
            var list = new List<Vector3<double>>();
            foreach (var fid in fEl.EnumerateArray())
            {
                var faceId = fid.GetString()
                             ?? throw new TopologyFormatException($"{where} \"adjacentFaces\" contains a non-string face id.");
                if (!faceNormals.TryGetValue(faceId, out var normal))
                    throw new TopologyFormatException(
                        $"{where} references face '{faceId}' in \"adjacentFaces\", but no \"faces\" entry " +
                        $"with that id supplies a normal. Add it to the top-level \"faces\": " +
                        $"[{{ \"faceId\": \"{faceId}\", \"normal\": [x,y,z] }}], or use \"adjacentFaceNormals\" directly.");
                list.Add(normal);
            }
            return list;
        }

        throw new TopologyFormatException(
            $"{where} must supply adjacent-face normals via either \"adjacentFaceNormals\": [[x,y,z]…] " +
            $"or \"adjacentFaces\": [faceId…] (resolved against a top-level \"faces\" map). " +
            $"A surface normal is only defined at a point + chosen face (integration-contract §3), so the " +
            $"fingerprint needs the normals here.");
    }

    private static List<Vector3<double>> ReadVecList(JsonElement el, string where)
    {
        var list = new List<Vector3<double>>();
        var i = 0;
        foreach (var v in el.EnumerateArray())
        {
            list.Add(ReadVec(v, $"{where}[{i}]"));
            i++;
        }
        return list;
    }

    private static Vector3<double> ReadVec(JsonElement el, string where)
    {
        if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() != 3)
            throw new TopologyFormatException($"{where} must be a [x,y,z] array of 3 numbers.");
        try
        {
            return new Vector3<double>(el[0].GetDouble(), el[1].GetDouble(), el[2].GetDouble());
        }
        catch (Exception)
        {
            throw new TopologyFormatException($"{where} must contain 3 numbers.");
        }
    }

    private static string? GetString(JsonElement el, string name, string where)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        if (v.ValueKind != JsonValueKind.String)
            throw new TopologyFormatException($"{where} \"{name}\" must be a string.");
        return v.GetString();
    }

    private static EdgeKind ParseKind(string s, string where) => s switch
    {
        "line" => EdgeKind.Line,
        "arc" => EdgeKind.Arc,
        "circle" => EdgeKind.Circle,
        "spline" => EdgeKind.Spline,
        _ => throw new TopologyFormatException(
            $"{where} has unknown \"kind\": '{s}'. Expected line|arc|circle|spline.")
    };

    /// <summary>
    /// Emits the topology that <see cref="SampleProgram"/> was authored against, in the documented
    /// integration-contract Edge schema (using <c>adjacentFaces</c> face ids + a <c>faces</c> normal
    /// map, to exercise the contract shape end-to-end). `resolve sample.json this.json` binds every
    /// segment (s0->E0, s1->E2).
    /// </summary>
    public static string SampleTopologyJson() =>
        """
        {
          "bboxDiagonalMm": 111.803,
          "faces": [
            { "faceId": "TOP",   "normal": [0, 0, 1] },
            { "faceId": "FRONT", "normal": [0, -1, 0] },
            { "faceId": "RIGHT", "normal": [1, 0, 0] },
            { "faceId": "BACK",  "normal": [0, 1, 0] },
            { "faceId": "LEFT",  "normal": [-1, 0, 0] }
          ],
          "edges": [
            { "edgeId": "E0", "kind": "line", "length": 100, "closed": false,
              "polyline": [[0, 0, 0], [100, 0, 0]],   "adjacentFaces": ["TOP", "FRONT"] },
            { "edgeId": "E1", "kind": "line", "length": 50,  "closed": false,
              "polyline": [[100, 0, 0], [100, 50, 0]], "adjacentFaces": ["TOP", "RIGHT"] },
            { "edgeId": "E2", "kind": "line", "length": 100, "closed": false,
              "polyline": [[100, 50, 0], [0, 50, 0]],  "adjacentFaces": ["TOP", "BACK"] },
            { "edgeId": "E3", "kind": "line", "length": 50,  "closed": false,
              "polyline": [[0, 50, 0], [0, 0, 0]],     "adjacentFaces": ["TOP", "LEFT"] }
          ]
        }

        """;
}
