namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// FR-11's advisory lease, from the side that <b>owns</b> the rule.
///
/// <para>
/// <c>AdvisoryLease.Evaluate</c> in this package is the epic's reference implementation; the
/// simulator's method of the same name is the mirrored oracle. The two are kept in agreement by the
/// vector table in <c>delta-positioner-sim/docs/register-map.md</c> — <b>shared test data, not a
/// shared package</b>, because a production adapter must not depend on a simulator library. The
/// table below is that table, row for row and value for value; changing behaviour means changing
/// the table first and then both implementations.
/// </para>
/// </summary>
public class AdvisoryLeaseTests
{
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The shared lease vectors, rows 1–7. Row 8 carries two owner ids and two outcomes, which this
    /// signature cannot hold, so it is pinned by
    /// <see cref="TheLeaseIsAdvisory_AndTwoInstancesStartingTogetherBothPass"/> instead — exactly as
    /// the simulator's mirror of this table does.
    /// </summary>
    [Theory]
    [InlineData(1, (ushort)0, 0.00, (ushort)7, true, "unowned")]
    [InlineData(2, (ushort)3, 0.12, (ushort)7, false, "a live foreign commander holds the drive")]
    [InlineData(3, (ushort)3, 0.99, (ushort)7, false, "still inside the window")]
    [InlineData(4, (ushort)3, 1.00, (ushort)7, true, "boundary — age >= expiry is expired")]
    [InlineData(5, (ushort)3, 5.00, (ushort)7, true, "long expired")]
    [InlineData(6, (ushort)7, 0.05, (ushort)7, true, "already ours; reattach without waiting")]
    [InlineData(7, (ushort)7, 5.00, (ushort)7, true, "still ours")]
    public void TheSharedLeaseVectors_HoldRowForRow(
        int row, ushort owner, double ageSeconds, ushort myOwnerId, bool expected, string why)
    {
        var decision = AdvisoryLease.Evaluate(owner, TimeSpan.FromSeconds(ageSeconds), Expiry, myOwnerId);

        decision.Granted.Should().Be(expected, $"vector row {row}: {why}");
    }

    [Fact]
    public void TheLeaseIsAdvisory_AndTwoInstancesStartingTogetherBothPass()
    {
        // Vector row 8: owner 0, age 0, ids 11 and 12, both granted. Modbus has no compare-and-swap,
        // so two instances that read an unowned register in the same window both pass. This is not a
        // defect to be fixed — it is the documented limit of an advisory lease on a bus without CAS,
        // and the watchdog is what bounds its consequence. No CAS ladder logic is added, because
        // that would grow the unversioned-ladder artefact (R-1) to close a seconds-wide race.
        var first = AdvisoryLease.Evaluate(AdvisoryLease.Unowned, TimeSpan.Zero, Expiry, myOwnerId: 11);
        var second = AdvisoryLease.Evaluate(AdvisoryLease.Unowned, TimeSpan.Zero, Expiry, myOwnerId: 12);

        first.Granted.Should().BeTrue();
        second.Granted.Should().BeTrue();
    }

    [Fact]
    public void TheExpiryComparisonIsInclusive_WhichIsWhatRowFourPins()
    {
        // Spelled out separately because it is the one line an independent implementation is most
        // likely to get subtly wrong: a heartbeat exactly at the window is ALREADY expired.
        AdvisoryLease.Evaluate(3, Expiry, Expiry, myOwnerId: 7).Granted.Should().BeTrue();
        AdvisoryLease.Evaluate(3, Expiry - TimeSpan.FromMilliseconds(1), Expiry, myOwnerId: 7)
            .Granted.Should().BeFalse();
    }

    [Fact]
    public void ARefusedDecision_NamesTheOwnerItSaw()
    {
        // AC-12 reads the refused owner out of this text, so it is part of the behaviour rather than
        // decoration.
        var decision = AdvisoryLease.Evaluate(ownerRegister: 4242, TimeSpan.FromMilliseconds(120),
            Expiry, myOwnerId: 7);

        decision.Granted.Should().BeFalse();
        decision.Reason.Should().Contain("4242");
    }

    [Fact]
    public void ARefusedInstance_RetriesAtOneHertz() =>
        AdvisoryLease.RetryInterval.Should().Be(TimeSpan.FromSeconds(1));

    [Fact]
    public void ExpiryIsTheWatchdogStallWindow() =>
        AdvisoryLease.Expiry.Should().Be(TimeSpan.FromSeconds(1),
            "FR-11 decision D-f pins the stall window at 1 s and the lease expires with it");

    [Fact]
    public void Control_TheVectorTableCanActuallyFail()
    {
        // Before trusting a table-driven test, break the thing it covers and watch it fail. If
        // Evaluate ignored the owner register entirely and granted everything, row 2 would still
        // pass on a test that only checked "granted" cases — so assert that at least one row is a
        // refusal and that the refusal is reachable through the same entry point.
        AdvisoryLease.Evaluate(3, TimeSpan.FromMilliseconds(120), Expiry, myOwnerId: 7)
            .Granted.Should().BeFalse();
        AdvisoryLease.Evaluate(3, TimeSpan.FromMilliseconds(120), Expiry, myOwnerId: 3)
            .Granted.Should().BeTrue("the same age and register is granted when the owner IS us");
    }
}
