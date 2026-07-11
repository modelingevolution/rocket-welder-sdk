using FluentAssertions;
using RocketWelder.SDK.Operations;

namespace RocketWelder.SDK.Operations.Welding.Tests;

/// <summary>
/// <c>rw.weldprogram/1 → /2</c> migration tests (the Pass[] wrap, data-model.md §4). No value is
/// fabricated: anything <c>/1</c> did not carry stays <c>null</c>, never a fake <c>0</c>.
/// </summary>
public class WeldProgramMigrationTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static WeldProgram MigrateFixtureV1()
    {
        var v1 = File.ReadAllBytes(Path.Combine(FixturesDir, "sample-v1.json"));
        // Deserialize auto-detects /1 and migrates (§4); equivalent to WeldProgramMigrator.MigrateV1ToV2.
        return WeldProgramDeserializer.Deserialize(v1);
    }

    [Fact]
    public void Migrate_V1Fixture_ProducesV2Schema()
    {
        var program = MigrateFixtureV1();
        var json = WeldProgramSerializer.Serialize(program);
        json.Should().StartWith("{\n  \"schema\": \"rw.weldprogram/2\"");
    }

    [Fact]
    public void Migrate_CarriesIdNameStepPreviewDatumVersionUnchanged()
    {
        var program = MigrateFixtureV1();

        program.Id.Should().Be(Guid.Parse("f3b1c0de-1111-2222-3333-444455556666"));
        program.Name.Should().Be("T-bracket 6mm fillet");
        program.Step.Path.Should().Be("tbracket.step");
        program.Step.Sha256.Should().Be("abc123sha256def");
        program.Preview.Path.Should().Be("tbracket.preview.jpg");
        program.Datum.Scheme.Should().Be("three-point");
        program.Datum.Points.Should().HaveCount(3);
        program.Version.AuthoredBy.Should().Be("operator-id");
        program.Version.ParentCommit.Should().Be("abc123");
        program.WeldOrderStrategy.Should().Be("distortion-balanced");
    }

    [Fact]
    public void Migrate_WrapsSingleRunAsOnePass_RoleCap_PerSection4()
    {
        var program = MigrateFixtureV1();
        var s0 = program.Segments[0];

        s0.Passes.Should().HaveCount(1);
        var p0 = s0.Passes[0];
        p0.Id.Should().Be("p0");
        p0.Role.Should().Be(PassRole.Cap); // §4.3: a lone single-pass run is its own cap
    }

    [Fact]
    public void Migrate_MapsWeldJobIdToJobRefId_DropsParamsBlob()
    {
        var program = MigrateFixtureV1();

        // /1 segment s0 had weldJob.id = 17 with a params blob {wire,gas}; /2 keeps only the jobRef.id.
        program.Segments[0].Passes[0].JobRef.Id.Should().Be(17);
        program.Segments[1].Passes[0].JobRef.Id.Should().Be(4);
    }

    [Fact]
    public void Migrate_CarriesTorchFrameVerbatimAsToolFrame_AndTravelSpeedAsMotion()
    {
        var program = MigrateFixtureV1();
        var tool = program.Segments[0].Passes[0].ToolFrame;

        tool.StandoffMm.Should().Be(12.0);
        tool.WorkAngleDeg.Should().Be(45.0);
        tool.TravelAngleDeg.Should().Be(10.0);
        tool.Technique.Should().Be(Technique.Drag);

        program.Segments[0].Passes[0].Motion.TravelSpeedMmPerS.Should().Be(6.5);
        program.Segments[0].Passes[0].Motion.Weave.Should().BeNull(); // stringer
    }

    [Fact]
    public void Migrate_DoesNotFabricate_PositionWeldSizeGasPolarityAreNull()
    {
        var program = MigrateFixtureV1();

        foreach (var seg in program.Segments)
        {
            seg.Position.Should().BeNull();
            seg.WeldSize.Should().BeNull();
            seg.Gas.Should().BeNull();
            seg.Polarity.Should().BeNull();
            seg.ExternalAxis.Should().BeNull();
        }
    }

    [Fact]
    public void Migrate_LiftsSeamType_AndCarriesBindingSubRangeResolver()
    {
        var program = MigrateFixtureV1();

        program.Segments[0].SeamType.Should().Be(SeamType.Fillet);
        program.Segments[1].SeamType.Should().Be(SeamType.Butt);

        program.Segments[0].Binding.EdgeIdHint.Should().Be("E0");
        program.Segments[0].SubRange.Should().Be(new SubRange(0.0, 1.0));
        program.Segments[1].SubRange.Should().Be(new SubRange(0.0, 0.75));

        // /1 resolver carried; tracking is null in /2 (absent in /1, not invented)
        program.Segments[0].Resolver.Should().NotBeNull();
        program.Segments[0].Resolver!.Mode.Should().Be(ResolverMode.Metrology);
        program.Segments[0].Resolver!.FeatureRef.Should().Be("E7");
        program.Segments[0].Resolver!.Tracking.Should().BeNull();
        program.Segments[1].Resolver.Should().BeNull();
    }

    [Fact]
    public void Migrate_PreservesSegmentWeldOrder_Positional()
    {
        var program = MigrateFixtureV1();
        program.Segments.Select(s => s.Id).Should().ContainInOrder("s0", "s1");
    }

    [Fact]
    public void Migrate_IsIdempotent_MigratedThenReserializedReadsBackEqual()
    {
        var migrated = MigrateFixtureV1();
        var bytes = WeldProgramSerializer.SerializeToUtf8Bytes(migrated);

        // re-reading the /2 output (now schema /2) re-serializes byte-identically (AT-A4 holds post-migration)
        var reread = WeldProgramDeserializer.Deserialize(bytes);
        var bytes2 = WeldProgramSerializer.SerializeToUtf8Bytes(reread);

        bytes2.Should().Equal(bytes);
    }

    [Fact]
    public void Migrate_MatchesGoldenMigratedFixture()
    {
        var migratedBytes = WeldProgramSerializer.SerializeToUtf8Bytes(MigrateFixtureV1());
        var golden = File.ReadAllBytes(Path.Combine(FixturesDir, "sample-v1-migrated-to-v2.json"));
        migratedBytes.Should().Equal(golden);
    }
}
