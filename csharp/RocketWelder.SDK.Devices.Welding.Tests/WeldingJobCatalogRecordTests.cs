using FluentAssertions;

namespace RocketWelder.SDK.Devices.Welding.Tests;

/// <summary>
/// design.md §1: the catalog records are plain immutable values. The host read model builds them from events and the
/// panel compares them to decide what to re-render, so value equality and the documented member meanings are pinned.
/// </summary>
public sealed class WeldingJobCatalogRecordTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WeldingJobSnapshot_Default_HasEveryRegisterUnread()
    {
        var snapshot = default(WeldingJobSnapshot);

        snapshot.CurrentA.Should().BeNull();
        snapshot.VoltageV.Should().BeNull();
        snapshot.WireFeedMMin.Should().BeNull();
        snapshot.SheetThicknessMm.Should().BeNull();
        snapshot.Program.Should().BeNull();
    }

    [Fact]
    public void WeldingJobSnapshot_SameValues_AreEqual()
    {
        var a = new WeldingJobSnapshot(180.5f, 21.3f, 8.25f, 3.0f, 1234);
        var b = new WeldingJobSnapshot(180.5f, 21.3f, 8.25f, 3.0f, 1234);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void WeldingJobSnapshot_NullRegisterDiffersFromZero()
    {
        // null = that register could not be read; 0 is a real reading.
        new WeldingJobSnapshot(null, 21.3f, 8.25f, 3.0f, 1234)
            .Should().NotBe(new WeldingJobSnapshot(0f, 21.3f, 8.25f, 3.0f, 1234));
    }

    [Fact]
    public void WeldingJobProbeRecord_SameValues_AreEqual()
    {
        var snapshot = new WeldingJobSnapshot(180.5f, 21.3f, null, null, 7);

        new WeldingJobProbeRecord(42, WeldingJobProbeOutcome.Populated, snapshot, At)
            .Should().Be(new WeldingJobProbeRecord(42, WeldingJobProbeOutcome.Populated, snapshot, At));
    }

    [Fact]
    public void WeldingJobProbeRecord_DifferentOutcome_AreNotEqual()
    {
        new WeldingJobProbeRecord(42, WeldingJobProbeOutcome.Populated, default, At)
            .Should().NotBe(new WeldingJobProbeRecord(42, WeldingJobProbeOutcome.Empty, default, At));
    }

    [Fact]
    public void WeldingRecipe_WithReplacesNameKeepingIdentity()
    {
        // SaveRecipeAsync: same Id = replace (rename).
        var setpoints = new Dictionary<string, float> { ["WireFeed"] = 8.0f };
        var recipe = new WeldingRecipe(Guid.NewGuid(), "Fillet 3mm", WeldingMode.MigMagSynergic, setpoints, null);

        var renamed = recipe with { Name = "Fillet 3 mm" };

        renamed.Id.Should().Be(recipe.Id);
        renamed.Mode.Should().Be(WeldingMode.MigMagSynergic);
        renamed.Setpoints.Should().BeSameAs(setpoints);
        renamed.LinkedJobNumber.Should().BeNull();
        renamed.Should().NotBe(recipe);
    }

    [Fact]
    public void WeldingRecipe_SameSetpointsInstance_AreEqual()
    {
        // Record equality compares Setpoints by reference (IReadOnlyDictionary has no value equality).
        var id = Guid.NewGuid();
        var setpoints = new Dictionary<string, float> { ["Current"] = 150f };

        new WeldingRecipe(id, "R", WeldingMode.MigMagStandard, setpoints, 12)
            .Should().Be(new WeldingRecipe(id, "R", WeldingMode.MigMagStandard, setpoints, 12));
    }

    [Fact]
    public void WeldingJobEntry_Unprobed_Unnamed_Unlinked()
    {
        var entry = new WeldingJobEntry(57, null, false, null, null);

        entry.JobNumber.Should().Be(57);
        entry.Name.Should().BeNull();
        entry.Hidden.Should().BeFalse();
        entry.LastProbe.Should().BeNull();
        entry.LinkedRecipe.Should().BeNull();
    }

    [Fact]
    public void WeldingJobEntry_SameValues_AreEqual()
    {
        var probe = new WeldingJobProbeRecord(57, WeldingJobProbeOutcome.Unreadable, default, At);

        new WeldingJobEntry(57, "Root pass", true, probe, null)
            .Should().Be(new WeldingJobEntry(57, "Root pass", true, probe, null));
    }

    [Fact]
    public void WeldingJobGroup_SameValues_AreEqual()
    {
        new WeldingJobGroup(5, "Frames", 3, 10).Should().Be(new WeldingJobGroup(5, "Frames", 3, 10));
        new WeldingJobGroup(5, "Frames", 3, 10).Should().NotBe(new WeldingJobGroup(5, "Frames", 4, 10));
    }
}
