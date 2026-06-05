using System.Text.Json;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Operations;

namespace RocketWelder.SDK.Automation.Tests.WeldProgramModel;

/// <summary>
/// Builders for a representative <see cref="WeldProgram"/> and a matching <see cref="Topology"/>,
/// shared across the IT-1 tests. Geometry is a simple rectangular plate so the edges are
/// distinguishable yet some are deliberately near-equivalent (for the ambiguity test).
/// </summary>
internal static class SampleData
{
    public static readonly Guid ProgramId = Guid.Parse("f3b1c0de-1111-2222-3333-444455556666");

    /// <summary>A two-segment weld program bound to edges E0 and E2 of <see cref="PlateTopology"/>.</summary>
    public static WeldProgram Program()
    {
        var resolver = new EdgeBindingResolver();
        var topo = PlateTopology();

        var bindingE0 = resolver.Fingerprint(topo.Get("E0")!);
        var bindingE2 = resolver.Fingerprint(topo.Get("E2")!);

        var emptyParams = (IReadOnlyDictionary<string, JsonElement>)new Dictionary<string, JsonElement>();
        var jobParams = ParseParams("""{"wire":1.2,"gas":"82/18"}""");

        var s0 = new Segment(
            Id: "s0",
            Binding: bindingE0,
            SubRange: new SubRange(0.0, 1.0),
            Process: new WeldProcess("fillet", new WeldJob(17, jobParams), 6.5),
            TorchFrame: new TorchFrame(12.0, 45.0, 10.0, "drag"),
            Resolver: new SegmentResolver("metrology", "E7"));

        var s1 = new Segment(
            Id: "s1",
            Binding: bindingE2,
            SubRange: new SubRange(0.0, 0.75),
            Process: new WeldProcess("butt", new WeldJob(4, emptyParams), 8.25),
            TorchFrame: new TorchFrame(10.0, 90.0, 0.0, "perpendicular"),
            Resolver: null);

        return new WeldProgram(
            Id: ProgramId,
            Name: "T-bracket 6mm fillet",
            Step: new StepRef("tbracket.step", "abc123sha256def"),
            Preview: new PreviewRef("tbracket.preview.jpg"),
            Datum: new Datum("three-point", new[]
            {
                new DatumPoint("d0", new Vector3<double>(0, 0, 0), "F12", null),
                new DatumPoint("d1", new Vector3<double>(100, 0, 0), null, "E7"),
                new DatumPoint("d2", new Vector3<double>(0, 50, 10), "F30", null)
            }),
            Segments: new[] { s0, s1 },
            WeldOrderStrategy: "distortion-balanced",
            Version: new VersionInfo(
                "operator-id",
                new DateTimeOffset(2026, 6, 4, 18, 0, 0, TimeSpan.Zero),
                "abc123",
                "rw2 1.2.3"));
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseParams(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in doc.RootElement.EnumerateObject())
            dict[p.Name] = p.Value.Clone();
        return dict;
    }

    /// <summary>
    /// A rectangular plate 100×50 in the z=0 plane. Four line edges E0..E3 (the four sides) plus
    /// one extra interior line E4 used to make a near-duplicate of E0 for the ambiguity test —
    /// disambiguated by its different adjacent-face normals.
    /// </summary>
    public static Topology PlateTopology()
    {
        // outward normals chosen to give each side a distinct dihedral context.
        var nUp = new Vector3<double>(0, 0, 1);
        var nDown = new Vector3<double>(0, 0, -1);
        var nLeft = new Vector3<double>(-1, 0, 0);
        var nRight = new Vector3<double>(1, 0, 0);
        var nFront = new Vector3<double>(0, -1, 0);
        var nBack = new Vector3<double>(0, 1, 0);

        var edges = new List<EdgeTopology>
        {
            // E0: bottom edge y=0, from (0,0,0)->(100,0,0); faces: top plate + front wall
            Line("E0", (0, 0, 0), (100, 0, 0), nUp, nFront),
            // E1: right edge x=100, (100,0,0)->(100,50,0); faces: top + right wall
            Line("E1", (100, 0, 0), (100, 50, 0), nUp, nRight),
            // E2: top edge y=50, (100,50,0)->(0,50,0); faces: top + back wall
            Line("E2", (100, 50, 0), (0, 50, 0), nUp, nBack),
            // E3: left edge x=0, (0,50,0)->(0,0,0); faces: top + left wall
            Line("E3", (0, 50, 0), (0, 0, 0), nUp, nLeft),
        };

        return new Topology(edges);
    }

    /// <summary>
    /// The same plate after a STEP revision: ids re-labelled (shifted ordinals) and geometry
    /// perturbed slightly (within tolerance). E0 -> "EDGE_7", etc.
    /// </summary>
    public static Topology RevisedPlateTopology()
    {
        var nUp = new Vector3<double>(0, 0, 1);
        var nDown = new Vector3<double>(0, 0, -1);
        var nLeft = new Vector3<double>(-1, 0, 0);
        var nRight = new Vector3<double>(1, 0, 0);
        var nFront = new Vector3<double>(0, -1, 0);
        var nBack = new Vector3<double>(0, 1, 0);

        const double e = 0.01; // tiny perturbation, well within tolerance after normalization
        var edges = new List<EdgeTopology>
        {
            // ids shifted, order shuffled, geometry perturbed by ~0.01mm
            Line("EDGE_42", (100, 50 + e, 0), (0 + e, 50, 0), nUp, nBack),   // was E2
            Line("EDGE_7",  (0, 0, 0), (100 + e, 0, 0), nUp, nFront),         // was E0
            Line("EDGE_9",  (0, 50, 0), (0, 0 - e, 0), nUp, nLeft),           // was E3
            Line("EDGE_3",  (100, 0, 0), (100, 50 - e, 0), nUp, nRight),      // was E1
        };
        return new Topology(edges);
    }

    /// <summary>
    /// An ambiguous topology: contains TWO edges geometrically equivalent to the binding for E0
    /// (same length, midpoint, tangent AND same adjacent-face normals) so the resolver cannot
    /// safely choose — it must return UNRESOLVED, never a wrong bind.
    /// </summary>
    public static Topology AmbiguousTopology()
    {
        var nUp = new Vector3<double>(0, 0, 1);
        var nFront = new Vector3<double>(0, -1, 0);
        var nBack = new Vector3<double>(0, 1, 0);

        var edges = new List<EdgeTopology>
        {
            // Two near-identical copies of E0 (bottom edge), with identical fingerprints.
            Line("A", (0, 0, 0), (100, 0, 0), nUp, nFront),
            Line("B", (0, 0, 0), (100, 0, 0), nUp, nFront),
            // an unrelated edge of the same kind, far away
            Line("C", (0, 200, 0), (100, 200, 0), nUp, nBack),
        };
        return new Topology(edges);
    }

    private static EdgeTopology Line(string id, (double, double, double) a, (double, double, double) b,
        Vector3<double> n0, Vector3<double> n1)
    {
        var pa = new Vector3<double>(a.Item1, a.Item2, a.Item3);
        var pb = new Vector3<double>(b.Item1, b.Item2, b.Item3);
        var length = (pb - pa).Length;
        return new EdgeTopology(
            EdgeId: id,
            Kind: EdgeKind.Line,
            Polyline: new[] { pa, pb },
            Length: length,
            AdjacentFaceNormals: new[] { n0, n1 });
    }
}
