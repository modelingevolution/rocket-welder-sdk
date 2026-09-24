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
    public void WeldingJobEntry_Unprobed_Unnamed()
    {
        var entry = new WeldingJobEntry(57, null, false, null);

        entry.JobNumber.Should().Be(57);
        entry.Name.Should().BeNull();
        entry.Hidden.Should().BeFalse();
        entry.LastProbe.Should().BeNull();
    }

    [Fact]
    public void WeldingJobEntry_SameValues_AreEqual()
    {
        var probe = new WeldingJobProbeRecord(57, WeldingJobProbeOutcome.Unreadable, default, At);

        new WeldingJobEntry(57, "Root pass", true, probe)
            .Should().Be(new WeldingJobEntry(57, "Root pass", true, probe));
    }

    [Fact]
    public void WeldingJobGroup_SameValues_AreEqual()
    {
        new WeldingJobGroup(5, "Frames", 3, 10).Should().Be(new WeldingJobGroup(5, "Frames", 3, 10));
        new WeldingJobGroup(5, "Frames", 3, 10).Should().NotBe(new WeldingJobGroup(5, "Frames", 4, 10));
    }
}
