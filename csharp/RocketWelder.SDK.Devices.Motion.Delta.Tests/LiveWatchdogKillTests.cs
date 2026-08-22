using Microsoft.Extensions.Logging.Abstractions;
using ModelingEvolution.Drawing.Units;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// FR-11's whole point, against a live drive: <b>motion must cease when the commander dies</b>, not
/// only when it politely calls <c>StopAsync</c>. The RUN coil latches in the ladder, so a killed
/// process otherwise leaves the axis running — indefinitely, in continuous rotation.
///
/// <para>
/// A killed process is emulated the way it really fails: its socket goes away mid-operation, without
/// a stop, without a lease release and without a chance to run any cleanup path. The drive is then
/// observed from a <i>separate</i> connection, because the point is what the drive does on its own.
/// </para>
///
/// <para>
/// What is asserted is the trip's <b>consequences</b> — run state dropped, frequency zeroed, limits
/// re-asserted, fault latched, home latch untouched — and never how far the axis coasted afterwards.
/// The coast is a bench measurement (~59° worst case on the turntable, of which the simulator models
/// only the ramp).
/// </para>
/// </summary>
[Trait("Category", "Integration")]
[Collection(LiveSimulatorCollection.Name)]
public class LiveWatchdogKillTests
{
    private static readonly TimeSpan StallWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TripBudget = TimeSpan.FromSeconds(10);

    [SimFact]
    public async Task WhenTheHeartbeatStops_TheDriveDropsTheRunStateOnItsOwn()
    {
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var observer = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await observer.ConnectAsync(CancellationToken.None);

        var tripsBefore = await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit,
            DeltaRegisters.D133_WatchdogTripCount);

        // A commander attaches, arms the watchdog with a non-zero beat, and starts the axis turning.
        var commander = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await commander.ConnectAsync(CancellationToken.None);
        var heartbeat = new DeltaHeartbeat("turntable", commander, 201, TimeSpan.FromMilliseconds(200),
            StallWindow, NullLogger.Instance);
        await heartbeat.AcquireAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        heartbeat.Start();

        await commander.WriteCoilAsync(DeltaRegisters.PlcUnit, DeltaRegisters.M0_Run, true,
            "run", ChannelPriority.Move, CancellationToken.None);
        await commander.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency, 2000,
            "20 Hz", ChannelPriority.Move, CancellationToken.None);
        await commander.WriteCoilAsync(DeltaRegisters.PlcUnit, DeltaRegisters.M4_Move, true,
            "move", ChannelPriority.Move, CancellationToken.None);

        (await LiveSimulator.WaitUntilAsync(async () =>
                await LiveSimulator.ReadAsync(observer, DeltaRegisters.DriveUnit,
                    DeltaRegisters.OutputFrequency) > 0,
            TimeSpan.FromSeconds(10)))
            .Should().BeTrue("the arrangement only means anything if the axis is really turning");

        (await LiveSimulator.WaitUntilArmedAsync(observer, TimeSpan.FromSeconds(10)))
            .Should().BeTrue("the watchdog arms on the first CHANGE of D130 — killing a disarmed "
                             + "network proves nothing");

        // kill -9: the socket dies mid-motion. No stop, no release, no cleanup.
        commander.Dispose();

        var tripped = await LiveSimulator.WaitUntilAsync(async () =>
            await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit,
                DeltaRegisters.D132_WatchdogFault) == DeltaRegisters.WatchdogHeartbeatStall, TripBudget);

        tripped.Should().BeTrue("one second without a heartbeat change must drop the axis");

        var coils = await LiveSimulator.ReadCoilsAsync(observer, DeltaRegisters.M0_Run, 6);
        coils[0].Should().BeFalse("M0 carries M1025/M1040 — the latched run state is what must go");
        coils[4].Should().BeFalse("M4 motion is dropped with it");
        (await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency))
            .Should().Be(0, "the commanded frequency is zeroed, not merely ignored");

        (await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit,
                DeltaRegisters.D133_WatchdogTripCount))
            .Should().Be((ushort)(tripsBefore + 1));

        await heartbeat.DisposeAsync();
    }

    [SimFact]
    public async Task ATripDoesNotTouchTheHomeLatch_SoRecoveryIsResetAndReCommand()
    {
        // "Protection restored, position kept". AC-11's second half: after a kill and a restart, a
        // reset and a commanded move succeed WITHOUT re-homing.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var observer = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await observer.ConnectAsync(CancellationToken.None);

        var commander = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await commander.ConnectAsync(CancellationToken.None);
        var heartbeat = new DeltaHeartbeat("turntable", commander, 202, TimeSpan.FromMilliseconds(200),
            StallWindow, NullLogger.Instance);
        await heartbeat.AcquireAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        heartbeat.Start();

        var latchBefore = await LiveSimulator.ReadDWordAsync(observer, DeltaRegisters.D120_HomeLatch);

        await commander.WriteCoilAsync(DeltaRegisters.PlcUnit, DeltaRegisters.M0_Run, true, "run",
            ChannelPriority.Move, CancellationToken.None);

        (await LiveSimulator.WaitUntilArmedAsync(observer, TimeSpan.FromSeconds(10)))
            .Should().BeTrue("the watchdog arms on the first CHANGE of D130 — killing a disarmed "
                             + "network proves nothing");

        commander.Dispose();

        (await LiveSimulator.WaitUntilAsync(async () =>
            await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit,
                DeltaRegisters.D132_WatchdogFault) == DeltaRegisters.WatchdogHeartbeatStall, TripBudget))
            .Should().BeTrue();

        (await LiveSimulator.ReadDWordAsync(observer, DeltaRegisters.D120_HomeLatch))
            .Should().Be(latchBefore, "the zero survives a watchdog trip — no re-home is needed");

        await heartbeat.DisposeAsync();
    }

    [SimFact]
    public async Task ATripOnALimitedAxis_ReAssertsTheLimitFunctions()
    {
        // Risk R-2: retreating from a tripped limit lifts the drive's own limit function, and a
        // process killed inside that window used to leave the drive with no hardware limit
        // protection until somebody noticed. The watchdog bounds it at the stall window instead.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TiltPort);
        using var observer = LiveSimulator.Observe(LiveSimulator.TiltPort);
        await observer.ConnectAsync(CancellationToken.None);

        var commander = LiveSimulator.Observe(LiveSimulator.TiltPort);
        await commander.ConnectAsync(CancellationToken.None);
        var heartbeat = new DeltaHeartbeat("tilt", commander, 203, TimeSpan.FromMilliseconds(200),
            StallWindow, NullLogger.Instance);
        await heartbeat.AcquireAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        heartbeat.Start();

        (await LiveSimulator.WaitUntilArmedAsync(observer, TimeSpan.FromSeconds(10)))
            .Should().BeTrue("the watchdog arms on the first CHANGE of D130 — killing a disarmed "
                             + "network proves nothing");

        // Mid-retreat: the limit functions are lifted and the process is about to die.
        await commander.WriteRegisterAsync(DeltaRegisters.DriveUnit, DeltaRegisters.Pr0204_Mi4, 0,
            "lift lower limit", ChannelPriority.Move, CancellationToken.None);
        await commander.WriteRegisterAsync(DeltaRegisters.DriveUnit, DeltaRegisters.Pr0205_Mi5, 0,
            "lift upper limit", ChannelPriority.Move, CancellationToken.None);
        commander.Dispose();

        (await LiveSimulator.WaitUntilAsync(async () =>
            await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit,
                DeltaRegisters.D132_WatchdogFault) == DeltaRegisters.WatchdogHeartbeatStall, TripBudget))
            .Should().BeTrue();

        (await LiveSimulator.ReadAsync(observer, DeltaRegisters.DriveUnit, DeltaRegisters.Pr0204_Mi4))
            .Should().Be(DeltaRegisters.Mi4LimitFunction);
        (await LiveSimulator.ReadAsync(observer, DeltaRegisters.DriveUnit, DeltaRegisters.Pr0205_Mi5))
            .Should().Be(DeltaRegisters.Mi5LimitFunction);

        await heartbeat.DisposeAsync();
    }

    [SimFact]
    public async Task ALiveCommanderIsNeverTripped_EvenWhileTheChannelCarriesMoveTraffic()
    {
        // AC-24 in miniature: the beat shares one channel with the move loop, and if its deferral
        // were unbounded a long move would starve it into tripping its own watchdog. The full soak
        // is thirty minutes on the bench; this is the shape of it.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var observer = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await observer.ConnectAsync(CancellationToken.None);

        var commander = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await commander.ConnectAsync(CancellationToken.None);
        await using var heartbeat = new DeltaHeartbeat("turntable", commander, 204,
            TimeSpan.FromMilliseconds(200), StallWindow, NullLogger.Instance);
        await heartbeat.AcquireAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        heartbeat.Start();

        // Saturate the channel with move-lane traffic for well over the stall window.
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            while (!stop.IsCancellationRequested)
                await commander.ReadHoldingAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D1051_Position,
                    2, "flood", ChannelPriority.Move, stop.Token);
        }
        catch (OperationCanceledException)
        {
            // The flood ran its course.
        }

        (await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault))
            .Should().Be(DeltaRegisters.WatchdogHealthy,
                "the heartbeat's bounded deferral is what keeps a busy channel from self-tripping");

        commander.Dispose();
    }

    [SimFact]
    public async Task KillingTheCommanderMidMove_StopsTheAxisAndTheAdapterReportsWatchdogTripped()
    {
        // The full adapter path, not just the registers: a killed commander's drive is found tripped
        // by the NEXT instance, which surfaces it as MotionError.WatchdogTripped rather than as a
        // mystery.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);

        var victim = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await victim.ConnectAsync(CancellationToken.None);
        var victimBeat = new DeltaHeartbeat("turntable", victim, 205, TimeSpan.FromMilliseconds(200),
            StallWindow, NullLogger.Instance);
        await victimBeat.AcquireAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        victimBeat.Start();

        var victimAxis = new DeltaAxis(LiveSimulator.Turntable, victim, new InMemoryAxisStateStore(),
            NullLogger.Instance);
        await victimAxis.PowerAsync(true);
        await victimAxis.MoveVelocityAsync(new AngularSpeed<double, DegreePerSecond<double>>(10));
        victimAxis.State.Should().Be(AxisState.ContinuousMotion);

        using var observer = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await observer.ConnectAsync(CancellationToken.None);
        (await LiveSimulator.WaitUntilArmedAsync(observer, TimeSpan.FromSeconds(10)))
            .Should().BeTrue("the watchdog arms on the first CHANGE of D130 — killing a disarmed "
                             + "network proves nothing");

        victim.Dispose();   // kill -9 mid-move

        (await LiveSimulator.WaitUntilAsync(async () =>
            await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit,
                DeltaRegisters.D132_WatchdogFault) == DeltaRegisters.WatchdogHeartbeatStall, TripBudget))
            .Should().BeTrue("the axis was left turning by a process that will never call StopAsync");

        // The successor attaches — the lease expired with the beat — and its first heartbeat read
        // finds the latched fault.
        var successor = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await successor.ConnectAsync(CancellationToken.None);
        var successorAxis = new DeltaAxis(LiveSimulator.Turntable, successor, new InMemoryAxisStateStore(),
            NullLogger.Instance);
        await using var successorBeat = new DeltaHeartbeat("turntable", successor, 206,
            TimeSpan.FromMilliseconds(200), StallWindow, NullLogger.Instance);
        successorBeat.WatchdogTripped += (_, _) => successorAxis.OnWatchdogTripped();

        await successorBeat.AcquireAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await successorBeat.BeatOnceAsync(CancellationToken.None);

        successorAxis.State.Should().Be(AxisState.ErrorStop);
        successorAxis.Status.Error.Should().Be(MotionError.WatchdogTripped);

        // Recovery is reset + re-command. ResetAsync clears the latch, and nothing re-homes.
        await successorAxis.ResetAsync();
        successorAxis.State.Should().Be(AxisState.Standstill);
        (await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault))
            .Should().Be(DeltaRegisters.WatchdogHealthy);

        successorAxis.Dispose();
        await victimBeat.DisposeAsync();
        victimAxis.Dispose();
    }
}
