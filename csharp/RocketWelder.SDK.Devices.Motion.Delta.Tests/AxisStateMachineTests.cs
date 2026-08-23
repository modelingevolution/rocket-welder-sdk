using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// FR-1: one explicit state, derived booleans, and a Busy-reject that leaves the state <b>intact</b>
/// — the epic's deliberate departure from PLCopen, which would drive the axis to
/// <see cref="AxisState.ErrorStop"/> for a caller's mistiming (AC-1, AC-2, AC-10, AC-19).
/// </summary>
public class AxisStateMachineTests
{
    [Fact]
    public void AnUnpoweredAxis_IsDisabled_AndNoConvenienceBooleanSaysOtherwise()
    {
        using var bed = AxisTestBed.Turntable();

        bed.Axis.State.Should().Be(AxisState.Disabled);
        bed.Axis.IsReady.Should().BeFalse();
        bed.Axis.IsMoving.Should().BeFalse();
    }

    [Fact]
    public async Task PoweringOn_ReachesStandstill_AndSetsTheRunCoil()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();

        bed.Axis.State.Should().Be(AxisState.Standstill);
        bed.Axis.IsReady.Should().BeTrue();
        bed.Drive.ReadCoil(DeltaRegisters.PlcUnit, DeltaRegisters.M0_Run).Should().BeTrue();
    }

    // Found live 2026-08-24: a freshly connected positioner sits Disabled, PowerAsync is the only exit, and
    // no seam in any host calls it — so every verb, including Home, was refused forever. Homing's first
    // write has always been M0_Run=true on the bench, so Home IS the deliberate energise act and must be
    // callable from Disabled. Move verbs stay gated — the test below pins that side.
    [Fact]
    public async Task HomingFromDisabled_PowersOn_References_AndEndsAtStandstill()
    {
        using var bed = AxisTestBed.Build(DeltaPositionerDefaults.Turntable, drive =>
        {
            drive.PositionCounts = -13_280;
            AxisTestBed.ScriptTheCam(drive, latchFires: true);
        });
        bed.Axis.State.Should().Be(AxisState.Disabled);

        await bed.Axis.HomeAsync();

        bed.Axis.State.Should().Be(AxisState.Standstill);
        bed.Axis.IsHomed.Should().BeTrue();
        bed.Drive.ReadCoil(DeltaRegisters.PlcUnit, DeltaRegisters.M0_Run).Should().BeTrue();
    }

    [Fact]
    public async Task AMotionCommandFromDisabled_IsRejectedBusy_AndNothingIsCommanded()
    {
        using var bed = AxisTestBed.Turntable();

        var act = () => bed.Axis.MoveAbsoluteAsync(Degree<double>.Create(90));

        (await act.Should().ThrowAsync<MotionException>()).Which.Error.Should().Be(MotionError.Busy);
        bed.Axis.State.Should().Be(AxisState.Disabled, "a rejected command leaves the state intact");
        bed.Drive.Writes.Should().NotContain(o => o.Address == DeltaRegisters.D110_Frequency,
            "AC-2: the axis does not move");
    }

    [Fact]
    public async Task AMotionCommandDuringContinuousMotion_IsRejectedBusy_AndTheAxisKeepsTurning()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();
        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));
        bed.Axis.State.Should().Be(AxisState.ContinuousMotion);

        var act = () => bed.Axis.MoveAbsoluteAsync(Degree<double>.Create(90));

        (await act.Should().ThrowAsync<MotionException>()).Which.Error.Should().Be(MotionError.Busy);
        bed.Axis.State.Should().Be(AxisState.ContinuousMotion,
            "FR-1: Busy leaves the state intact — ErrorStop is for faults, not caller mistakes");

        await bed.Axis.StopAsync();
    }

    [Fact]
    public async Task MoveVelocity_EntersContinuousMotion_AndStopReturnsToStandstill()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();

        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));
        bed.Axis.State.Should().Be(AxisState.ContinuousMotion);
        bed.Axis.IsMoving.Should().BeTrue();

        await bed.Axis.StopAsync();

        bed.Axis.State.Should().Be(AxisState.Standstill,
            "AC-10: after a stop the axis is in a state a subsequent command accepts");
        bed.Axis.IsMoving.Should().BeFalse();
    }

    [Fact]
    public async Task StopDropsTheMotionCoilOnlyAfterTheDriveHasStopped()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();
        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));

        await bed.Axis.StopAsync();

        // The coil must only fall on a stationary axis — the PLC switches mode on that edge.
        var writes = bed.Drive.Writes.ToArray();
        var rampDown = Array.FindLastIndex(writes,
            o => o.Address == DeltaRegisters.D110_Frequency && o.Value == 0);
        var coilOff = Array.FindLastIndex(writes,
            o => o.Kind == "write-coil" && o.Address == DeltaRegisters.M4_Move && !o.Flag);

        rampDown.Should().BeGreaterThan(-1);
        coilOff.Should().BeGreaterThan(rampDown, "the frequency goes to zero before M4 is dropped");
    }

    [Fact]
    public async Task TheFirstStopWriteTakesThePriorityLane()
    {
        // NFR-5 / AC-23: the write that stops the drive must not queue behind move traffic. Timing
        // is measured against the live simulator; what is pinned HERE is that the adapter asks for
        // the lane at all, because a stop on the ordinary lane cannot meet 200 ms by luck.
        using var bed = await AxisTestBed.Turntable().PoweredAsync();
        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));
        var before = bed.Drive.Ops.Count;

        await bed.Axis.StopAsync();

        var firstStopWrite = bed.Drive.Ops.Skip(before)
            .First(o => o.IsWrite && o.Address == DeltaRegisters.D110_Frequency);
        firstStopWrite.Priority.Should().Be(ChannelPriority.Stop);
        firstStopWrite.Value.Should().Be(0);
    }

    [Fact]
    public async Task AFaultLatchesErrorStop_WhichOnlyResetLeaves()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();

        // The drive reports a fault of its own; the next status read notices it.
        bed.Drive.WriteHolding(DeltaRegisters.DriveUnit, DeltaRegisters.FaultCode, 40);
        await bed.Axis.ReadStatusAsync();

        bed.Axis.State.Should().Be(AxisState.ErrorStop);
        bed.Axis.Status.Error.Should().Be(MotionError.DriveFault);

        var act = () => bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));
        (await act.Should().ThrowAsync<MotionException>()).Which.Error.Should().Be(MotionError.Busy);
        bed.Axis.State.Should().Be(AxisState.ErrorStop, "only ResetAsync leaves ErrorStop");

        bed.Drive.WriteHolding(DeltaRegisters.DriveUnit, DeltaRegisters.FaultCode, 0);
        await bed.Axis.ResetAsync();

        bed.Axis.State.Should().Be(AxisState.Standstill);
        bed.Axis.Status.Error.Should().BeNull();
    }

    [Fact]
    public async Task AStatusSnapshotCanNeverDisagreeWithTheLiveState()
    {
        // AC-1, at the place it actually breaks: a CACHED AxisStatus is the stale-boolean shape the
        // criterion forbids. This test found a real one — a reset axis still reporting the fault it
        // had just been reset out of — so the status is now composed from the live state rather than
        // stored beside it.
        using var bed = await AxisTestBed.Turntable().PoweredAsync();
        bed.Drive.WriteHolding(DeltaRegisters.DriveUnit, DeltaRegisters.FaultCode, 40);
        await bed.Axis.ReadStatusAsync();

        bed.Axis.Status.State.Should().Be(bed.Axis.State);

        bed.Drive.WriteHolding(DeltaRegisters.DriveUnit, DeltaRegisters.FaultCode, 0);
        await bed.Axis.ResetAsync();

        // No further status read: the snapshot must already agree, without being refreshed.
        bed.Axis.Status.State.Should().Be(AxisState.Standstill);
        bed.Axis.Status.Error.Should().BeNull();
    }

    [Fact]
    public async Task PoweringOnFromErrorStop_IsRefused_AndPointsAtReset()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();
        bed.Drive.WriteHolding(DeltaRegisters.DriveUnit, DeltaRegisters.FaultCode, 40);
        await bed.Axis.ReadStatusAsync();

        var act = () => bed.Axis.PowerAsync(true);

        var ex = (await act.Should().ThrowAsync<MotionException>()).Which;
        ex.Error.Should().Be(MotionError.Busy);
        ex.Message.Should().Contain("ResetAsync");
    }

    [Fact]
    public async Task ResetClearsTheWatchdogLatch_BecauseNothingElseClearsIt()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();
        bed.Drive.WriteHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault,
            DeltaRegisters.WatchdogHeartbeatStall);

        await bed.Axis.ResetAsync();

        bed.Drive.ReadHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault)
            .Should().Be(DeltaRegisters.WatchdogHealthy);
    }

    [Fact]
    public async Task ARejectedCommandCarriesTheAxisName_SoACallerNeedNotParseTheMessage()
    {
        // AC-19: a caller branches on the enum and the axis name, never on message text. The example
        // verb is a Move: Home is no longer rejected from Disabled (it IS the energise act), so the
        // rejected-command specimen must be one that stays gated.
        using var bed = AxisTestBed.Tilt();

        var act = () => bed.Axis.MoveAbsoluteAsync(Degree<double>.Create(10));

        var ex = (await act.Should().ThrowAsync<MotionException>()).Which;
        ex.Error.Should().Be(MotionError.Busy);
        ex.AxisName.Should().Be(DeltaPositionerDefaults.TiltAxisName);
    }

    [Fact]
    public async Task CancellingAMove_StopsTheAxisAndLeavesItCommandable()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();
        using var cts = new CancellationTokenSource();

        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5), cts.Token);
        bed.Axis.State.Should().Be(AxisState.ContinuousMotion);

        // The token stays observed AFTER MoveVelocityAsync completes: cancelling it stops the axis.
        await cts.CancelAsync();
        await bed.Axis.StopAsync();

        bed.Axis.State.Should().Be(AxisState.Standstill);
        bed.Drive.ReadCoil(DeltaRegisters.PlcUnit, DeltaRegisters.M4_Move).Should().BeFalse();
    }

    [Fact]
    public async Task Control_TheHarnessCanObserveAStateChangeAtAll()
    {
        // Without this, every "the state is X" assertion above could be passing because the state
        // never moves off its initial value.
        using var bed = AxisTestBed.Turntable();
        var initial = bed.Axis.State;

        await bed.Axis.PowerAsync(true);

        bed.Axis.State.Should().NotBe(initial);
    }
}
