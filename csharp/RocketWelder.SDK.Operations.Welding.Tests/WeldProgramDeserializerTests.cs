using FluentAssertions;
using RocketWelder.SDK.Operations;

namespace RocketWelder.SDK.Operations.Welding.Tests;

/// <summary>
/// Semantic round-trip of the /2 model: serialize then deserialize must reproduce every value
/// (enums, nested optionals, fillet leg-vs-throat, weave, tracking, external axis).
/// </summary>
public class WeldProgramDeserializerTests
{
    private static WeldProgram RoundTrip(WeldProgram p)
    {
        var bytes = WeldProgramSerializer.SerializeToUtf8Bytes(p);
        return WeldProgramDeserializer.Deserialize(bytes);
    }

    [Fact]
    public void RoundTrip_PreservesAllSegmentLevelWeldingFacts()
    {
        var p = RoundTrip(SampleData.Program());
        var s0 = p.Segments[0];

        s0.SeamType.Should().Be(SeamType.Fillet);
        s0.Position.Should().Be(WeldPosition.PB);
        s0.Polarity.Should().Be(Polarity.DCEP);
        s0.Gas!.Mix.Should().Be("80/20 Ar/CO2");
        s0.Gas.FlowLpm.Should().Be(14.0);
        s0.WeldSize!.Fillet!.LegMm.Should().Be(8.0);
        s0.WeldSize.Fillet.ThroatMm.Should().BeNull();
        s0.WeldSize.Butt.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_PreservesButtWeldSize()
    {
        var p = RoundTrip(SampleData.Program());
        var butt = p.Segments[1].WeldSize!.Butt!;

        butt.GrooveAngleDeg.Should().Be(60.0);
        butt.RootGapMm.Should().Be(2.0);
        butt.RootFaceMm.Should().Be(1.5);
        butt.Prep.Should().Be("single-V");
        p.Segments[1].WeldSize!.Fillet.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_PreservesPassesPositionallyWithRolesAndJobRefs()
    {
        var passes = RoundTrip(SampleData.Program()).Segments[0].Passes;

        passes.Should().HaveCount(2);
        passes[0].Id.Should().Be("p0");
        passes[0].Role.Should().Be(PassRole.Root);
        passes[0].JobRef.Id.Should().Be(17);
        passes[1].Id.Should().Be("p1");
        passes[1].Role.Should().Be(PassRole.Cap);
        passes[1].JobRef.Id.Should().Be(18);
    }

    [Fact]
    public void RoundTrip_PreservesWeave_AndStringerNull()
    {
        var p = RoundTrip(SampleData.Program());

        p.Segments[0].Passes[0].Motion.Weave.Should().BeNull(); // stringer
        var weave = p.Segments[0].Passes[1].Motion.Weave!;
        weave.AmplitudeMm.Should().Be(2.0);
        weave.FrequencyHz.Should().Be(1.5);
        weave.EdgeDwellMs.Should().Be(120.0);
        weave.Pattern.Should().Be("triangle");
    }

    [Fact]
    public void RoundTrip_PreservesResolverTracking_AndExternalAxis()
    {
        var s0 = RoundTrip(SampleData.Program()).Segments[0];

        s0.Resolver!.Mode.Should().Be(ResolverMode.Metrology);
        s0.Resolver.FeatureRef.Should().Be("E7");
        s0.Resolver.Tracking!.Mode.Should().Be(TrackingMode.Tast);
        s0.Resolver.Tracking.GapFill.Should().BeTrue();

        s0.ExternalAxis!.Device.Should().Be("positioner");
        s0.ExternalAxis.Axis.Should().Be("tilt");
        s0.ExternalAxis.AngleDeg.Should().Be(30.0);
    }

    [Fact]
    public void Deserialize_UnknownSchema_Throws()
    {
        const string json = """{ "schema": "rw.weldprogram/3" }""";
        var act = () => WeldProgramDeserializer.Deserialize(json);
        act.Should().Throw<System.Text.Json.JsonException>();
    }
}
