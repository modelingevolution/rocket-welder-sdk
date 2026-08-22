namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// Homing's load-bearing sequence: the sentinel goes into <c>D120</c> <b>before</b> <c>M6</c> arms
/// the latch, and the ladder's <c>DSUB</c> runs <b>before</b> its <c>DMOV</c>.
///
/// <para>
/// Why both halves matter: because DSUB subtracts the OLD D120, a correctly working latch writes the
/// <i>same number back</i> when the axis returns to the same cam edge — and the better the machine's
/// repeatability, the more often it does. Comparing against the previous value therefore cannot tell
/// "the latch ran" from "the ladder is missing"; only a sentinel can, and only if it is written
/// first. Getting this wrong produced a false homing failure that the superseded Python controller
/// reported as <c>homed: true, ready: false, error: "latch did not fire"</c>.
/// </para>
///
/// <para>
/// The ladder's part is played by the test, not by the fake, so what is being emulated is visible
/// here rather than hidden in a helper.
/// </para>
/// </summary>
public class HomingLatchTests
{
    private const int PowerUpCounts = -13_280;

    [Fact]
    public async Task TheSentinelIsWrittenBeforeTheLatchIsArmed()
    {
        using var bed = await HomeOnceAsync();

        var writes = bed.Drive.Writes.ToArray();
        var sentinel = Array.FindIndex(writes,
            o => o.Kind == "write-holding" && o.Address == DeltaRegisters.D120_HomeLatch);
        var arm = Array.FindIndex(writes,
            o => o.Kind == "write-coil" && o.Address == DeltaRegisters.M6_ArmLatch && o.Flag);

        sentinel.Should().BeGreaterThan(-1, "the sentinel must reach the wire at all");
        arm.Should().BeGreaterThan(sentinel,
            "arming first lets the latch fire and then be overwritten by the sentinel");
    }

    [Fact]
    public async Task TheLadderSubtractsTheOldLatchValue_WhichIsWhatMakesTheSentinelReadable()
    {
        using var bed = await HomeOnceAsync();

        // DSUB saw the sentinel, because DMOV had not yet replaced it. If the ladder ran the other
        // way round, D122 would be zero and the sentinel would be invisible.
        bed.Drive.LatchDelta.Should().Be(PowerUpCounts - 1_000_000_000);
    }

    [Fact]
    public async Task TheLatchDefinesZero_NotTheAxisBeingAtZero()
    {
        using var bed = await HomeOnceAsync();

        bed.Axis.IsHomed.Should().BeTrue();
        bed.Drive.HomeLatch.Should().Be(PowerUpCounts, "DMOV stored the position the cam edge was at");

        var status = await bed.Axis.ReadStatusAsync();
        status.Position.Should().BeApproximately(0.0, 1e-9,
            "homing promises that the LATCHED position reads as zero, not that the axis moved to zero");
    }

    [Fact]
    public async Task TheArmingCoilIsDroppedOnTheWayOut()
    {
        using var bed = await HomeOnceAsync();

        bed.Drive.ReadCoil(DeltaRegisters.PlcUnit, DeltaRegisters.M6_ArmLatch).Should().BeFalse();
    }

    [Fact]
    public async Task ALadderThatNeverLatches_FailsHomingRatherThanReportingASilentZero()
    {
        // The control: the sentinel's whole purpose. With no ladder to overwrite it, D120 still
        // holds the sentinel when homing reads it back, and homing must say so.
        using var bed = AxisTestBed.Build(DeltaPositionerDefaults.Turntable, drive =>
        {
            drive.PositionCounts = PowerUpCounts;
            AxisTestBed.ScriptTheCam(drive, latchFires: false);
        });

        await bed.Axis.PowerAsync(true);
        var act = () => bed.Axis.HomeAsync();

        var ex = (await act.Should().ThrowAsync<MotionException>()).Which;
        ex.Message.Should().Contain("never latched");
        bed.Axis.IsHomed.Should().BeFalse();
        bed.Axis.State.Should().Be(AxisState.ErrorStop);
    }

    private static async Task<AxisTestBed> HomeOnceAsync()
    {
        var bed = AxisTestBed.Build(DeltaPositionerDefaults.Turntable, drive =>
        {
            drive.PositionCounts = PowerUpCounts;
            AxisTestBed.ScriptTheCam(drive, latchFires: true);
        });

        await bed.Axis.PowerAsync(true);
        await bed.Axis.HomeAsync();
        return bed;
    }

}
