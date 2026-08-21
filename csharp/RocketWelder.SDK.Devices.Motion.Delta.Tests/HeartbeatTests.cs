using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// FR-11's client half: the beat itself, its paired read, and the lease handshake that decides
/// whether this instance may attach at all (AC-11, AC-12).
/// </summary>
public class HeartbeatTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(1);

    private static DeltaHeartbeat Build(FakeDrive drive, ushort ownerId = 7) =>
        new("turntable", drive, ownerId, Interval, Expiry, NullLogger.Instance);

    [Fact]
    public void TheBeatCounterNeverProduces_ZeroEvenOnWrap()
    {
        // 0 is not a heartbeat: it is the value D130 powers up at, and writing it can leave the
        // drive's network unarmed forever. Arming is the first NON-ZERO write.
        DeltaHeartbeat.NextBeat(0).Should().Be(1);
        DeltaHeartbeat.NextBeat(1).Should().Be(2);
        DeltaHeartbeat.NextBeat(ushort.MaxValue).Should().Be(1, "the wrap must skip 0, not land on it");

        ushort beat = 0;
        for (var i = 0; i < 200_000; i++)
        {
            beat = DeltaHeartbeat.NextBeat(beat);
            beat.Should().NotBe(0);
        }
    }

    [Fact]
    public async Task TheFirstBeatIsNonZero_SoTheDrivesNetworkArms()
    {
        var drive = new FakeDrive();
        await using var heartbeat = Build(drive);

        await heartbeat.BeatOnceAsync(CancellationToken.None);

        drive.ReadHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D130_Heartbeat).Should().Be(1);
        heartbeat.LastBeat.Should().Be(1);
    }

    [Fact]
    public async Task EveryBeatChangesTheRegister_BecauseTheNetworkArmsOnTheCHANGE()
    {
        var drive = new FakeDrive();
        await using var heartbeat = Build(drive);
        var seen = new List<ushort>();

        for (var i = 0; i < 5; i++)
        {
            await heartbeat.BeatOnceAsync(CancellationToken.None);
            seen.Add(drive.ReadHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D130_Heartbeat));
        }

        seen.Should().OnlyHaveUniqueItems("a repeated value is indistinguishable from a stalled commander");
        seen.Should().NotContain((ushort)0);
    }

    [Fact]
    public async Task TheBeatTravelsOnTheHeartbeatLane()
    {
        var drive = new FakeDrive();
        await using var heartbeat = Build(drive);

        await heartbeat.BeatOnceAsync(CancellationToken.None);

        drive.Ops.Where(o => o.Address == DeltaRegisters.D130_Heartbeat)
            .Should().OnlyContain(o => o.Priority == ChannelPriority.Heartbeat);
    }

    [Fact]
    public async Task TheBeatsPairedReadCoversTheWatchdogRegisters()
    {
        var drive = new FakeDrive();
        await using var heartbeat = Build(drive);
        HeartbeatTick? seen = null;
        heartbeat.Ticked += (_, tick) => seen = tick;

        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D133_WatchdogTripCount, 3);
        await heartbeat.BeatOnceAsync(CancellationToken.None);

        seen.Should().NotBeNull();
        seen!.Value.Beat.Should().Be(1);
        seen.Value.WatchdogFault.Should().Be(DeltaRegisters.WatchdogHealthy);
        seen.Value.TripCount.Should().Be(3);

        // One transaction, not two: D132 and D133 are adjacent on purpose.
        drive.Ops.Count(o => o.Kind == "read-holding" && o.Address == DeltaRegisters.D132_WatchdogFault)
            .Should().Be(1);
    }

    [Fact]
    public async Task ALatchedWatchdogFault_RaisesTheTripEventOnceOnItsRisingEdge()
    {
        var drive = new FakeDrive();
        await using var heartbeat = Build(drive);
        var trips = 0;
        heartbeat.WatchdogTripped += (_, _) => trips++;

        await heartbeat.BeatOnceAsync(CancellationToken.None);
        trips.Should().Be(0);

        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault,
            DeltaRegisters.WatchdogHeartbeatStall);
        await heartbeat.BeatOnceAsync(CancellationToken.None);
        await heartbeat.BeatOnceAsync(CancellationToken.None);

        trips.Should().Be(1, "the latch stays set until cleared; the trip is an edge, not a level");
    }

    [Fact]
    public async Task ClearingTheFaultRearmsTheEdge()
    {
        var drive = new FakeDrive();
        await using var heartbeat = Build(drive);
        var trips = 0;
        heartbeat.WatchdogTripped += (_, _) => trips++;

        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault,
            DeltaRegisters.WatchdogHeartbeatStall);
        await heartbeat.BeatOnceAsync(CancellationToken.None);

        await heartbeat.ClearWatchdogFaultAsync(CancellationToken.None);
        drive.ReadHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault)
            .Should().Be(DeltaRegisters.WatchdogHealthy);

        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault,
            DeltaRegisters.WatchdogHeartbeatStall);
        await heartbeat.BeatOnceAsync(CancellationToken.None);

        trips.Should().Be(2);
    }

    [Fact]
    public void AnOwnerIdOfZeroIsRefused_BecauseItIsTheUnownedMarker()
    {
        var drive = new FakeDrive();

        var act = () => new DeltaHeartbeat("turntable", drive, AdvisoryLease.Unowned, Interval, Expiry);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AnUnownedDrive_IsClaimedWithoutTheSamplingWait()
    {
        // Rows 1, 6 and 7 of the lease table do not depend on the heartbeat's age, so the attaching
        // instance must not pay the one-second sampling wait for them.
        var drive = new FakeDrive();
        await using var heartbeat = Build(drive);
        var started = DateTime.UtcNow;

        var decision = await heartbeat.TryAcquireAsync(CancellationToken.None);

        decision.Granted.Should().BeTrue();
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromMilliseconds(500));
        drive.ReadHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId).Should().Be(7);
    }

    [Fact]
    public async Task OurOwnLeaseIsReattachedWithoutWaiting()
    {
        var drive = new FakeDrive();
        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId, 7);
        await using var heartbeat = Build(drive);
        var started = DateTime.UtcNow;

        (await heartbeat.TryAcquireAsync(CancellationToken.None)).Granted.Should().BeTrue();

        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task ALiveForeignHeartbeat_IsRefused_AndTheReasonNamesTheOwner()
    {
        // AC-12. The age is obtained by SAMPLING D130 across the expiry window — the drive publishes
        // the value, never its age — so a foreign owner whose beat keeps changing is refused.
        var drive = new FakeDrive();
        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId, 4242);
        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D130_Heartbeat, 5);

        // The incumbent beats between our two samples.
        var reads = 0;
        drive.React = (_, _) => { };
        await using var heartbeat = new DeltaHeartbeat("turntable", new BeatingDrive(drive, () => reads++),
            7, Interval, TimeSpan.FromMilliseconds(50), NullLogger.Instance);

        var decision = await heartbeat.TryAcquireAsync(CancellationToken.None);

        decision.Granted.Should().BeFalse();
        decision.Reason.Should().Contain("4242");
        reads.Should().BeGreaterThan(1, "the rule must sample D130 twice, not trust one stale read");
    }

    [Fact]
    public async Task AForeignHeartbeatThatHasStopped_IsTakenOver()
    {
        // The rolling-deploy case: the outgoing instance stops beating, so D130 is unchanged across
        // the whole expiry window and the lease is takeable.
        var drive = new FakeDrive();
        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId, 4242);
        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D130_Heartbeat, 5);
        await using var heartbeat = new DeltaHeartbeat("turntable", drive, 7, Interval,
            TimeSpan.FromMilliseconds(50), NullLogger.Instance);

        var decision = await heartbeat.TryAcquireAsync(CancellationToken.None);

        decision.Granted.Should().BeTrue();
        decision.Reason.Should().Contain("expired");
        drive.ReadHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId).Should().Be(7);
    }

    [Fact]
    public async Task AcquireGivesUpWithLeaseHeld_NamingTheOwnerItKeptSeeing()
    {
        var drive = new FakeDrive();
        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId, 4242);
        await using var heartbeat = new DeltaHeartbeat("turntable",
            new BeatingDrive(drive, () => { }), 7, Interval, TimeSpan.FromMilliseconds(20),
            NullLogger.Instance);

        var act = () => heartbeat.AcquireAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<MotionException>()).Which;
        ex.Error.Should().Be(MotionError.LeaseHeld);
        ex.Message.Should().Contain("4242");
    }

    [Fact]
    public async Task StoppingReleasesTheLease_SoASuccessorNeedNotWaitOutTheWindow()
    {
        var drive = new FakeDrive();
        await using (var heartbeat = Build(drive))
        {
            await heartbeat.TryAcquireAsync(CancellationToken.None);
            heartbeat.Start();
            await Task.Delay(50);
            await heartbeat.StopAsync();
        }

        drive.ReadHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId)
            .Should().Be(AdvisoryLease.Unowned);
    }

    [Fact]
    public async Task StoppingLeavesAForeignOwnersLeaseAlone()
    {
        var drive = new FakeDrive();
        await using var heartbeat = Build(drive);
        await heartbeat.TryAcquireAsync(CancellationToken.None);
        heartbeat.Start();

        // Somebody else took the drive while we were running; releasing must not clear their claim.
        drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId, 99);
        await heartbeat.StopAsync();

        drive.ReadHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId).Should().Be(99);
    }

    [Fact]
    public async Task TheRunningTimerBeatsAtLeastFiveTimesASecond()
    {
        // FR-11 decision D-f: >= 5 Hz against a 1 s stall window, which leaves four missed beats of
        // slack. Driven by the injected clock rather than by wall time — a sleep-and-count version
        // asserts the build agent's scheduling as much as the adapter's, and fails for reasons that
        // have nothing to do with the beat rate.
        var clock = new FakeTimeProvider();
        var drive = new FakeDrive();
        await using var heartbeat = new DeltaHeartbeat("turntable", drive, 7, Interval, Expiry,
            NullLogger.Instance, clock);
        await heartbeat.TryAcquireAsync(CancellationToken.None);

        heartbeat.Start();
        for (var i = 0; i < 5; i++)
        {
            clock.Advance(Interval);
            await WaitForBeatsAsync(drive, i + 1);
        }

        Beats(drive).Should().BeGreaterThanOrEqualTo(5,
            "one second of stall window must contain at least five beats");
        await heartbeat.StopAsync();
    }

    [Fact]
    public async Task TheConfiguredIntervalIsFastEnoughForTheStallWindow()
    {
        // The arithmetic behind D-f, pinned so a future "let's beat less often" edit has to argue
        // with it: the default interval must leave at least four missed beats inside the window.
        var cfg = DeltaPositionerDefaults.Turntable;

        (cfg.WatchdogStallWindow / cfg.HeartbeatInterval).Should().BeGreaterThanOrEqualTo(5);
    }

    private static int Beats(FakeDrive drive) =>
        drive.Ops.Count(o => o.IsWrite && o.Address == DeltaRegisters.D130_Heartbeat);

    /// <summary>
    /// The timer callback runs on the threadpool, so advancing the clock schedules a beat rather
    /// than performing one. Waiting for the count is what makes this deterministic instead of
    /// merely usually-right.
    /// </summary>
    private static async Task WaitForBeatsAsync(FakeDrive drive, int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (Beats(drive) < expected && DateTime.UtcNow < deadline) await Task.Delay(5);
        Beats(drive).Should().BeGreaterThanOrEqualTo(expected);
    }

    /// <summary>
    /// A drive whose D130 changes on every read — the incumbent commander is alive. Wrapping the
    /// fake rather than teaching it this behaviour keeps the arrangement visible in the test.
    /// </summary>
    private sealed class BeatingDrive(FakeDrive inner, Action onHeartbeatRead) : IModbusChannel
    {
        private ushort _beat = 5;

        public string Host => inner.Host;

        public bool IsConnected => inner.IsConnected;

        public Task<bool> IsAvailableAsync(TimeSpan timeout, CancellationToken ct) =>
            inner.IsAvailableAsync(timeout, ct);

        public Task ConnectAsync(CancellationToken ct) => inner.ConnectAsync(ct);

        public Task DisconnectAsync(CancellationToken ct = default) => inner.DisconnectAsync(ct);

        public Task<ushort[]> ReadHoldingAsync(byte unit, ushort address, ushort count, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
        {
            if (address == DeltaRegisters.D130_Heartbeat)
            {
                onHeartbeatRead();
                inner.WriteHolding(unit, address, ++_beat);
            }

            return inner.ReadHoldingAsync(unit, address, count, what, priority, ct);
        }

        public Task WriteRegisterAsync(byte unit, ushort address, ushort value, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default) =>
            inner.WriteRegisterAsync(unit, address, value, what, priority, ct);

        public Task WriteRegistersAsync(byte unit, ushort address, ushort[] values, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default) =>
            inner.WriteRegistersAsync(unit, address, values, what, priority, ct);

        public Task<bool[]> ReadCoilsAsync(byte unit, ushort address, ushort count, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default) =>
            inner.ReadCoilsAsync(unit, address, count, what, priority, ct);

        public Task WriteCoilAsync(byte unit, ushort address, bool value, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default) =>
            inner.WriteCoilAsync(unit, address, value, what, priority, ct);

        public Task<bool[]> ReadDiscreteInputsAsync(byte unit, ushort address, ushort count, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default) =>
            inner.ReadDiscreteInputsAsync(unit, address, count, what, priority, ct);

        public void Dispose() => inner.Dispose();
    }
}
