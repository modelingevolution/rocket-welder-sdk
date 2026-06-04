using ModelingEvolution.Drawing;
using RocketWelder.SDK.Automation.WeldProgramModel;

namespace RocketWelder.SDK.Automation.Tests.WeldProgramModel;

public class EdgeBindingResolverTests
{
    private readonly EdgeBindingResolver _resolver = new();

    // ---- AT-E1: same-topology reload re-binds every edge ---------------

    [Fact]
    public void Resolve_SameTopology_RebindsEveryEdge()
    {
        var topology = SampleData.PlateTopology();
        var program = SampleData.Program();

        // The program was authored against this exact topology; each segment must re-bind to the
        // very edge it was fingerprinted from.
        Assert.Equal("E0", _resolver.Resolve(program.Segments[0].Binding, topology));
        Assert.Equal("E2", _resolver.Resolve(program.Segments[1].Binding, topology));
    }

    [Fact]
    public void Resolve_SameTopology_EveryEdgeBindsToItself()
    {
        var topology = SampleData.PlateTopology();
        foreach (var edge in topology.Edges)
        {
            var binding = _resolver.Fingerprint(edge);
            Assert.Equal(edge.EdgeId, _resolver.Resolve(binding, topology));
        }
    }

    [Fact]
    public void Resolve_FastPath_UsesHintWhenFingerprintMatches()
    {
        var topology = SampleData.PlateTopology();
        var binding = _resolver.Fingerprint(topology.Get("E1")!);
        Assert.Equal("E1", _resolver.Resolve(binding, topology));
    }

    // ---- AT-E2: revised topology re-binds by fingerprint ---------------

    [Fact]
    public void Resolve_RevisedTopology_ShiftedIdsAndPerturbedGeometry_RebindsByFingerprint()
    {
        var authored = SampleData.PlateTopology();
        var revised = SampleData.RevisedPlateTopology();

        // E0 -> EDGE_7, E2 -> EDGE_42 in the revised STEP. The ids shifted and the geometry was
        // perturbed within tolerance; the binding must still resolve by fingerprint, not by id.
        var bindingE0 = _resolver.Fingerprint(authored.Get("E0")!);
        var bindingE2 = _resolver.Fingerprint(authored.Get("E2")!);

        Assert.Equal("EDGE_7", _resolver.Resolve(bindingE0, revised));
        Assert.Equal("EDGE_42", _resolver.Resolve(bindingE2, revised));
    }

    [Fact]
    public void Resolve_RevisedTopology_HintIdGoneEntirely_StillRebinds()
    {
        var authored = SampleData.PlateTopology();
        var revised = SampleData.RevisedPlateTopology();

        // none of the original ids (E0..E3) exist in the revised topology, so the fast path can't fire.
        Assert.False(revised.Has("E1"));
        var bindingE1 = _resolver.Fingerprint(authored.Get("E1")!);
        Assert.Equal("EDGE_3", _resolver.Resolve(bindingE1, revised));
    }

    // ---- ambiguity: UNRESOLVED, never a wrong bind ---------------------

    [Fact]
    public void Resolve_AmbiguousTopology_ReturnsUnresolved_NotAWrongBind()
    {
        var authored = SampleData.PlateTopology();
        var ambiguous = SampleData.AmbiguousTopology();

        var bindingE0 = _resolver.Fingerprint(authored.Get("E0")!);
        var result = _resolver.Resolve(bindingE0, ambiguous);

        Assert.Equal(EdgeBindingResolver.Unresolved, result);
        // critically it did NOT silently pick "A" or "B"
        Assert.NotEqual("A", result);
        Assert.NotEqual("B", result);
    }

    [Fact]
    public void Resolve_NoCandidateOfSameKind_ReturnsUnresolved()
    {
        var authored = SampleData.PlateTopology();
        var bindingLine = _resolver.Fingerprint(authored.Get("E0")!);

        // a topology with only an arc -> no same-kind candidate
        var arcOnly = new Topology(new[]
        {
            new EdgeTopology("ARC0", EdgeKind.Arc,
                new[]
                {
                    new Vector3<double>(0, 0, 0),
                    new Vector3<double>(50, 10, 0),
                    new Vector3<double>(100, 0, 0)
                },
                120.0,
                new[] { new Vector3<double>(0, 0, 1), new Vector3<double>(0, -1, 0) })
        });

        Assert.Equal(EdgeBindingResolver.Unresolved, _resolver.Resolve(bindingLine, arcOnly));
    }

    // ---- fingerprint canonicalization ----------------------------------

    [Fact]
    public void Fingerprint_IsOrientationIndependent_SamePrintForReversedPolyline()
    {
        var forward = new EdgeTopology("F", EdgeKind.Line,
            new[] { new Vector3<double>(0, 0, 0), new Vector3<double>(100, 0, 0) },
            100.0,
            new[] { new Vector3<double>(0, 0, 1), new Vector3<double>(0, -1, 0) });

        var reversed = new EdgeTopology("R", EdgeKind.Line,
            new[] { new Vector3<double>(100, 0, 0), new Vector3<double>(0, 0, 0) },
            100.0,
            new[] { new Vector3<double>(0, -1, 0), new Vector3<double>(0, 0, 1) }); // normals also swapped

        var fpForward = _resolver.Fingerprint(forward);
        var fpReversed = _resolver.Fingerprint(reversed);

        // endpoints canonically sorted -> identical
        Assert.Equal(fpForward.Endpoints[0], fpReversed.Endpoints[0]);
        Assert.Equal(fpForward.Endpoints[1], fpReversed.Endpoints[1]);
        // tangent oriented endpoint[0]->endpoint[1] -> identical direction
        Assert.True(Vector3<double>.Dot(fpForward.TangentAtMid, fpReversed.TangentAtMid) > 0.999);
        // normals canonically sorted -> identical order
        Assert.Equal(fpForward.AdjFaceNormals[0], fpReversed.AdjFaceNormals[0]);
        Assert.Equal(fpForward.AdjFaceNormals[1], fpReversed.AdjFaceNormals[1]);

        // and the distance between the two fingerprints is ~0
        Assert.True(_resolver.Dist(fpForward, fpReversed, 200.0) < 1e-9);
    }

    [Fact]
    public void Dist_IdenticalEdge_IsZero()
    {
        var topo = SampleData.PlateTopology();
        var edge = topo.Get("E0")!;
        var binding = _resolver.Fingerprint(edge);
        Assert.True(_resolver.Dist(edge, binding, topo.BBoxDiagonalMm) < 1e-12);
    }
}
