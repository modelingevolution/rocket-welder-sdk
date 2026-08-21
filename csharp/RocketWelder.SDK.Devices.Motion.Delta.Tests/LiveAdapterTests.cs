using Microsoft.Extensions.Logging.Abstractions;
using ModelingEvolution.Drawing.Units;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// The adapter against the live <c>delta-positioner-sim</c>, over real Modbus TCP.
///
/// <para>
/// These assert <b>logic</b> — the state machine, register writes and their order, convergence,
/// error mapping. They never assert a duration, a coast distance or a braking margin: the
/// simulator's coast is the programmed ramp alone and runs ~7 % fast on long moves, so anything
/// timing-shaped validated here would be validated against a model of the machine rather than the
/// machine.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
[Collection(LiveSimulatorCollection.Name)]
public class LiveAdapterTests
{
    private static readonly TimeSpan MoveBudget = TimeSpan.FromMinutes(2);

    [SimFact]
    public async Task ConnectingEnforcesTheDriveParametersTheAdapterDependsOn()
    {
        // The simulator seeds these DIFFERENT from what the adapter wants precisely so the startup
        // write path is exercised rather than short-circuited by an equality check that was already
        // true. Nothing about behaviour may depend on what somebody last typed into the keypad.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var positioner = BuildTurntable(ownerId: 101);
        await positioner.ConnectAsync();

        using var observer = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await observer.ConnectAsync(CancellationToken.None);

        foreach (var (address, expected, why) in DeltaRegisters.RequiredSetup)
        {
            var actual = await LiveSimulator.ReadAsync(observer, DeltaRegisters.DriveUnit, address);
            actual.Should().Be(expected, why);
        }

        await positioner.DisconnectAsync();
    }

    [SimFact]
    public async Task AMotionCommandDuringAMove_IsRejectedBusy_AndTheMoveKeepsRunning()
    {
        // AC-2 against a drive that is genuinely turning, not a fake that merely says it is.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var positioner = BuildTurntable(ownerId: 102);
        await positioner.ConnectAsync();
        var axis = (IRotaryAxis)positioner[DeltaPositionerDefaults.TurntableAxisName];

        await axis.PowerAsync(true);
        await axis.MoveVelocityAsync(new AngularSpeed<double, DegreePerSecond<double>>(5));

        var act = () => axis.MoveRelativeAsync(Deg.Of(10));
        (await act.Should().ThrowAsync<MotionException>()).Which.Error.Should().Be(MotionError.Busy);
        axis.State.Should().Be(AxisState.ContinuousMotion);

        await axis.StopAsync();
        axis.State.Should().Be(AxisState.Standstill);
        await positioner.DisconnectAsync();
    }

    [SimFact]
    public async Task HomingLatchesTheZero_WithTheLaddersDsubSeeingTheSentinel()
    {
        // The DSUB-before-DMOV order, verified against a REAL ladder rather than a test's emulation
        // of one: because DSUB subtracts the OLD D120, the sentinel this adapter wrote is what
        // appears in D122. If the adapter armed M6 before writing the sentinel, or the ladder ran
        // the other way round, this number could not be what it is.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var positioner = BuildTurntable(ownerId: 103);
        await positioner.ConnectAsync();
        var axis = (DeltaAxis)positioner[DeltaPositionerDefaults.TurntableAxisName];

        await axis.PowerAsync(true);
        await axis.HomeAsync(new CancellationTokenSource(MoveBudget).Token);

        axis.IsHomed.Should().BeTrue();
        axis.State.Should().Be(AxisState.Standstill);

        using var observer = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await observer.ConnectAsync(CancellationToken.None);
        var latch = await LiveSimulator.ReadDWordAsync(observer, DeltaRegisters.D120_HomeLatch);
        var delta = await LiveSimulator.ReadDWordAsync(observer, DeltaRegisters.D122_LatchDelta);

        delta.Should().Be(latch - 1_000_000_000,
            "DSUB ran against the sentinel still sitting in D120, before DMOV replaced it");

        // Homing promises that the LATCHED position defines zero — not that the axis ends at zero,
        // which it does not: it creeps past the cam edge and coasts.
        var status = await axis.ReadStatusAsync();
        status.Position.Should().NotBeNull();

        await positioner.DisconnectAsync();
    }

    [SimFact]
    public async Task AnAbsoluteMoveConvergesInsideTheAxisTolerance()
    {
        // Convergence only. How LONG it takes and how far it coasts are bench numbers; that this
        // adapter's loop closes on the target at all is logic, and is what breaks if the wrap, the
        // sense or the direction convention is wrong.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var positioner = BuildTurntable(ownerId: 104);
        await positioner.ConnectAsync();
        var axis = (DeltaAxis)positioner[DeltaPositionerDefaults.TurntableAxisName];

        await axis.PowerAsync(true);
        await axis.HomeAsync(new CancellationTokenSource(MoveBudget).Token);

        var start = (await axis.ReadStatusAsync()).Position!.Value;
        var target = Normalise(start + 30.0);
        await axis.MoveAbsoluteAsync(Deg.Of(target), ct: new CancellationTokenSource(MoveBudget).Token);

        var landed = (await axis.ReadStatusAsync()).Position!.Value;
        ShortestGap(landed, target).Should().BeLessThanOrEqualTo((double)axis.Tolerance);
        axis.State.Should().Be(AxisState.Standstill);

        await positioner.DisconnectAsync();
    }

    [SimFact]
    public async Task APositiveSenseMoveReallyTravelsTheLongWayRound()
    {
        // The sense is only meaningful if it changes what the MACHINE does, so this measures the
        // distance actually travelled rather than re-checking the arithmetic a unit test already
        // pins. The raw encoder count is unwrapped and monotonic, so the total travel is simply the
        // difference — no timing, no coast, nothing model-shaped.
        //
        // Target 20° BEHIND: Shortest would travel 20°, Positive must travel 340°.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var positioner = BuildTurntable(ownerId: 105);
        await positioner.ConnectAsync();
        var axis = (DeltaAxis)positioner[DeltaPositionerDefaults.TurntableAxisName];

        await axis.PowerAsync(true);
        await axis.HomeAsync(new CancellationTokenSource(MoveBudget).Token);

        using var observer = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await observer.ConnectAsync(CancellationToken.None);

        var start = (await axis.ReadStatusAsync()).Position!.Value;
        var target = Normalise(start - 20.0);
        var countsBefore = await LiveSimulator.ReadDWordAsync(observer, DeltaRegisters.D1051_Position);

        await axis.MoveAbsoluteAsync(Deg.Of(target), sense: RotationSense.Positive,
            ct: new CancellationTokenSource(MoveBudget).Token);

        var countsAfter = await LiveSimulator.ReadDWordAsync(observer, DeltaRegisters.D1051_Position);
        var travelled = Math.Abs(countsAfter - countsBefore) / axis.Config.CountsPerDegree;

        travelled.Should().BeGreaterThan(180.0,
            "a positive-sense move to a target 20° behind has to go the long way round, and 340° of "
            + "travel is not something a shortest-path move could produce by any margin of error");
        ShortestGap((await axis.ReadStatusAsync()).Position!.Value, target)
            .Should().BeLessThanOrEqualTo((double)axis.Tolerance);

        await positioner.DisconnectAsync();
    }

    [SimFact]
    public async Task AShortestSenseMoveToTheSameTargetTakesTheShortWay()
    {
        // The control for the test above: same target, same axis, the other sense — and now the
        // travel must be the small one. Without this pair, "it moved 340°" could just be what this
        // adapter always does.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var positioner = BuildTurntable(ownerId: 106);
        await positioner.ConnectAsync();
        var axis = (DeltaAxis)positioner[DeltaPositionerDefaults.TurntableAxisName];

        await axis.PowerAsync(true);
        await axis.HomeAsync(new CancellationTokenSource(MoveBudget).Token);

        using var observer = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await observer.ConnectAsync(CancellationToken.None);

        var start = (await axis.ReadStatusAsync()).Position!.Value;
        var target = Normalise(start - 20.0);
        var countsBefore = await LiveSimulator.ReadDWordAsync(observer, DeltaRegisters.D1051_Position);

        await axis.MoveAbsoluteAsync(Deg.Of(target), sense: RotationSense.Shortest,
            ct: new CancellationTokenSource(MoveBudget).Token);

        var countsAfter = await LiveSimulator.ReadDWordAsync(observer, DeltaRegisters.D1051_Position);
        var travelled = Math.Abs(countsAfter - countsBefore) / axis.Config.CountsPerDegree;

        travelled.Should().BeLessThan(90.0, "the short way to a target 20° behind is 20°");

        await positioner.DisconnectAsync();
    }

    [SimFact]
    public async Task ARequestOnAnUnknownUnitId_SurfacesAsCommunicationLost()
    {
        // The drive answers units 1 and 2 and is SILENT on everything else — it does not return an
        // exception frame. A commander that assumed an error response would hang; the adapter must
        // surface the silence as a transport failure a caller can branch on.
        using var channel = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await channel.ConnectAsync(CancellationToken.None);

        var act = () => channel.ReadHoldingAsync(unit: 3, DeltaRegisters.D110_Frequency, 1,
            "unknown unit", ChannelPriority.Move, CancellationToken.None);

        (await act.Should().ThrowAsync<MotionException>()).Which.Error
            .Should().Be(MotionError.CommunicationLost);
    }

    [SimFact]
    public async Task ADisposedChannelRefusesToQuietlyReopenTheSocket()
    {
        // Found while designing the kill-test: without this, a channel the owner had torn down
        // reconnected on the next transaction — which would make a killed commander look alive again
        // for one write.
        var channel = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await channel.ConnectAsync(CancellationToken.None);
        channel.Dispose();

        var act = () => LiveSimulator.ReadAsync(channel, DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency);

        (await act.Should().ThrowAsync<MotionException>()).Which.Error
            .Should().Be(MotionError.CommunicationLost);
    }

    private static DeltaPositioner BuildTurntable(ushort ownerId) =>
        new(new DeviceId("DeltaPositioner_VFDC2000", Guid.NewGuid()), [LiveSimulator.Turntable],
            ownerId, new InMemoryAxisStateStore(), TimeSpan.FromSeconds(10), NullLogger<DeltaPositioner>.Instance);

    private static double Normalise(double angle) => ((angle % 360.0) + 360.0) % 360.0;

    /// <summary>The shorter of the two ways round between two wrapped angles.</summary>
    private static double ShortestGap(double a, double b)
    {
        var gap = Math.Abs(Normalise(a) - Normalise(b));
        return Math.Min(gap, 360.0 - gap);
    }
}
