using FluentAssertions;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Operations;

namespace RocketWelder.SDK.Operations.Welding.Tests;

/// <summary>
/// EdgeBinding fingerprint + resolve tests (data-model.md §3). The §2 model evolved to /2 but the §3
/// fingerprint is unchanged; these guard that the binding still resolves correctly.
/// </summary>
public class EdgeBindingResolverTests
{
    private readonly EdgeBindingResolver _resolver = new();

    [Fact]
    public void Resolve_SameTopology_RebindsEveryEdge()
    {
        var topology = SampleData.PlateTopology();
        var program = SampleData.Program();

        _resolver.Resolve(program.Segments[0].Binding, topology).Should().Be("E0");
        _resolver.Resolve(program.Segments[1].Binding, topology).Should().Be("E2");
    }

    [Fact]
    public void Resolve_SameTopology_EveryEdgeBindsToItself()
    {
        var topology = SampleData.PlateTopology();
        foreach (var edge in topology.Edges)
        {
            var binding = _resolver.Fingerprint(edge);
            _resolver.Resolve(binding, topology).Should().Be(edge.EdgeId);
        }
    }

    [Fact]
    public void Resolve_RevisedTopology_ShiftedIdsAndPerturbedGeometry_RebindsByFingerprint()
    {
        var authored = SampleData.PlateTopology();
        var revised = SampleData.RevisedPlateTopology();

        var bindingE0 = _resolver.Fingerprint(authored.Get("E0")!);
        var bindingE2 = _resolver.Fingerprint(authored.Get("E2")!);

        _resolver.Resolve(bindingE0, revised).Should().Be("EDGE_7");
        _resolver.Resolve(bindingE2, revised).Should().Be("EDGE_42");
    }

    [Fact]
    public void Resolve_AmbiguousTopology_ReturnsUnresolved_NotAWrongBind()
    {
        var authored = SampleData.PlateTopology();
        var ambiguous = SampleData.AmbiguousTopology();

        var bindingE0 = _resolver.Fingerprint(authored.Get("E0")!);
        var result = _resolver.Resolve(bindingE0, ambiguous);

        result.Should().Be(EdgeBindingResolver.Unresolved);
        result.Should().NotBe("A");
        result.Should().NotBe("B");
    }

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
            new[] { new Vector3<double>(0, -1, 0), new Vector3<double>(0, 0, 1) });

        var fpForward = _resolver.Fingerprint(forward);
        var fpReversed = _resolver.Fingerprint(reversed);

        fpForward.Endpoints[0].Should().Be(fpReversed.Endpoints[0]);
        fpForward.Endpoints[1].Should().Be(fpReversed.Endpoints[1]);
        Vector3<double>.Dot(fpForward.TangentAtMid, fpReversed.TangentAtMid).Should().BeGreaterThan(0.999);
        _resolver.Dist(fpForward, fpReversed, 200.0).Should().BeLessThan(1e-9);
    }
}
