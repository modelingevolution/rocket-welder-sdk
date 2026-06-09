using ModelingEvolution.Drawing;
using RocketWelder.SDK.Operations;

namespace RocketWelder.SDK.Operations.Welding.Tests;

/// <summary>
/// Builders for a representative <c>rw.weldprogram/2</c> <see cref="WeldProgram"/> and a matching
/// <see cref="Topology"/>, shared across the tests. Geometry is a simple rectangular plate so the edges
/// are distinguishable yet some are deliberately near-equivalent (for the ambiguity test).
/// </summary>
internal static class SampleData
{
    public static readonly Guid ProgramId = Guid.Parse("f3b1c0de-1111-2222-3333-444455556666");

    /// <summary>
    /// A two-segment /2 weld program bound to edges E0 and E2 of <see cref="PlateTopology"/>.
    /// s0 is a multi-pass fillet (root + cap, the cap weaves); s1 is a single-pass butt (v1 happy path).
    /// </summary>
    public static WeldProgram Program()
    {
        var resolver = new EdgeBindingResolver();
        var topo = PlateTopology();

        var bindingE0 = resolver.Fingerprint(topo.Get("E0")!);
        var bindingE2 = resolver.Fingerprint(topo.Get("E2")!);

        var s0 = new Segment(
            Id: "s0",
            Binding: bindingE0,
            SubRange: new SubRange(0.0, 1.0),
            SeamType: SeamType.Fillet,
            Position: WeldPosition.PB,
            WeldSize: new WeldSize(new FilletSize(LegMm: 8.0, ThroatMm: null), Butt: null),
            Gas: new Gas("80/20 Ar/CO2", 14.0),
            Polarity: Polarity.DCEP,
            Passes: new[]
            {
                new Pass("p0", PassRole.Root, new JobRef(17),
                    new ToolFrame(12.0, 45.0, 10.0, Technique.Drag),
                    new MotionProfile(6.5, null)),
                new Pass("p1", PassRole.Cap, new JobRef(18),
                    new ToolFrame(11.0, 45.0, 8.0, Technique.Drag),
                    new MotionProfile(5.5, new Weave(2.0, 1.5, 120.0, "triangle"))),
            },
            Resolver: new SegmentResolver(ResolverMode.Metrology, "E7",
                new Tracking(TrackingMode.Tast, GapFill: true)),
            ExternalAxis: new ExternalAxis(JointId: 1, AngleDeg: 30.0));

        var s1 = new Segment(
            Id: "s1",
            Binding: bindingE2,
            SubRange: new SubRange(0.0, 0.75),
            SeamType: SeamType.Butt,
            Position: WeldPosition.PA,
            WeldSize: new WeldSize(Fillet: null, new ButtSize(60.0, 2.0, 1.5, "single-V")),
            Gas: new Gas("100 Ar", 12.0),
            Polarity: Polarity.DCEP,
            Passes: new[]
            {
                new Pass("p0", PassRole.Cap, new JobRef(4),
                    new ToolFrame(10.0, 90.0, 0.0, Technique.Perpendicular),
                    new MotionProfile(8.25, null)),
            },
            Resolver: null,
            ExternalAxis: null);

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

    /// <summary>A rectangular plate 100×50 in the z=0 plane with four line edges E0..E3.</summary>
    public static Topology PlateTopology()
    {
        var nUp = new Vector3<double>(0, 0, 1);
        var nLeft = new Vector3<double>(-1, 0, 0);
        var nRight = new Vector3<double>(1, 0, 0);
        var nFront = new Vector3<double>(0, -1, 0);
        var nBack = new Vector3<double>(0, 1, 0);

        var edges = new List<EdgeTopology>
        {
            Line("E0", (0, 0, 0), (100, 0, 0), nUp, nFront),
            Line("E1", (100, 0, 0), (100, 50, 0), nUp, nRight),
            Line("E2", (100, 50, 0), (0, 50, 0), nUp, nBack),
            Line("E3", (0, 50, 0), (0, 0, 0), nUp, nLeft),
        };

        return new Topology(edges);
    }

    /// <summary>The same plate after a STEP revision: ids re-labelled and geometry perturbed within tolerance.</summary>
    public static Topology RevisedPlateTopology()
    {
        var nUp = new Vector3<double>(0, 0, 1);
        var nLeft = new Vector3<double>(-1, 0, 0);
        var nRight = new Vector3<double>(1, 0, 0);
        var nFront = new Vector3<double>(0, -1, 0);
        var nBack = new Vector3<double>(0, 1, 0);

        const double e = 0.01;
        var edges = new List<EdgeTopology>
        {
            Line("EDGE_42", (100, 50 + e, 0), (0 + e, 50, 0), nUp, nBack),   // was E2
            Line("EDGE_7",  (0, 0, 0), (100 + e, 0, 0), nUp, nFront),         // was E0
            Line("EDGE_9",  (0, 50, 0), (0, 0 - e, 0), nUp, nLeft),           // was E3
            Line("EDGE_3",  (100, 0, 0), (100, 50 - e, 0), nUp, nRight),      // was E1
        };
        return new Topology(edges);
    }

    /// <summary>
    /// An ambiguous topology with two edges geometrically equivalent to the binding for E0, so the
    /// resolver must return UNRESOLVED, never a wrong bind.
    /// </summary>
    public static Topology AmbiguousTopology()
    {
        var nUp = new Vector3<double>(0, 0, 1);
        var nFront = new Vector3<double>(0, -1, 0);
        var nBack = new Vector3<double>(0, 1, 0);

        var edges = new List<EdgeTopology>
        {
            Line("A", (0, 0, 0), (100, 0, 0), nUp, nFront),
            Line("B", (0, 0, 0), (100, 0, 0), nUp, nFront),
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
