namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// FR-7 / AC-7 — the commissioning direction check, exposed through the optional
/// <see cref="ISelfCheckingAxis"/> rather than on <c>IMotionAxis</c>, because it is a diagnostic and
/// not a motion verb (which is what keeps FR-12's block palette closed).
///
/// <para>
/// It exists because an inverted axis drives the correct distance the <b>wrong way</b> and reads as
/// a broken control loop rather than a wiring fault — a failure mode that cost a full diagnostic
/// session on the bench and that this check finds in about three seconds. So the branch that matters
/// most is the one that says "your motor is wired backwards", and it is tested here.
/// </para>
///
/// <para>
/// The drive's reaction is scripted by each test rather than built into the fake: <c>M4</c> going
/// high is the moment the axis would start turning, so that is where the position moves.
/// </para>
/// </summary>
public class SelfCheckTests
{
    private const int PowerUpCounts = 41_730;

    [Fact]
    public void TheCapabilityIsOptional_AndAskedForByType()
    {
        using var bed = AxisTestBed.Tilt();

        // The pattern a diagnostics UI uses: ask, do not assume.
        (bed.Axis as ISelfCheckingAxis).Should().NotBeNull();
        ((IMotionAxis)bed.Axis).Should().BeAssignableTo<ISelfCheckingAxis>();
    }

    [Fact]
    public async Task ACorrectlyWiredAxisPasses()
    {
        using var bed = await ArrangeAsync(countsMovedWhenJogged: +5_000);

        await bed.Axis.VerifyDirectionAsync();

        bed.Axis.State.Should().Be(AxisState.Standstill, "a passing check leaves the axis commandable");
    }

    [Fact]
    public async Task AnInvertedlyWiredAxisIsReported_NamingTheConfigurationThatFixesIt()
    {
        // The forward command moved the count DOWN. This is the wiring fault the whole check exists
        // for, and the message has to point at the fix rather than just at the symptom.
        using var bed = await ArrangeAsync(countsMovedWhenJogged: -5_000);

        var act = () => bed.Axis.VerifyDirectionAsync();

        var ex = (await act.Should().ThrowAsync<MotionException>()).Which;
        ex.AxisName.Should().Be(DeltaPositionerDefaults.TiltAxisName);
        ex.Message.Should().Contain(nameof(DeltaAxisConfig.InvertDirection));
        bed.Axis.State.Should().Be(AxisState.ErrorStop);
    }

    [Fact]
    public async Task AnAxisThatDoesNotMoveAtAllIsReportedAsSuch_NotAsAWiringFault()
    {
        // The control that keeps the sign test honest: zero movement must NOT be read as "negative".
        using var bed = await ArrangeAsync(countsMovedWhenJogged: 0);

        var act = () => bed.Axis.VerifyDirectionAsync();

        var ex = (await act.Should().ThrowAsync<MotionException>()).Which;
        ex.Message.Should().Contain("did not move");
        ex.Message.Should().NotContain(nameof(DeltaAxisConfig.InvertDirection));
    }

    [Fact]
    public async Task MovementBelowTheDetectionThreshold_CountsAsNotMoving()
    {
        // The threshold is 20 raw counts — two encoder quanta. Just under it must read as "did not
        // move" rather than as a direction result drawn from noise.
        using var bed = await ArrangeAsync(countsMovedWhenJogged: -19);

        var act = () => bed.Axis.VerifyDirectionAsync();

        (await act.Should().ThrowAsync<MotionException>()).Which.Message.Should().Contain("did not move");
    }

    [Fact]
    public async Task MovementAtTheThresholdIsATrustworthyDirectionReading()
    {
        // ...and just over it is a real reading, so the boundary is pinned from both sides.
        using var bed = await ArrangeAsync(countsMovedWhenJogged: -20);

        var act = () => bed.Axis.VerifyDirectionAsync();

        (await act.Should().ThrowAsync<MotionException>()).Which.Message
            .Should().Contain(nameof(DeltaAxisConfig.InvertDirection));
    }

    [Fact]
    public async Task ItRefusesToStartFromATrippedLimit()
    {
        // The jog runs open-loop for a fixed time and does NOT watch the limits, so starting from a
        // tripped one would drive further into it and leave the drive faulted. Inputs are normally
        // CLOSED: 0 is tripped.
        using var bed = await ArrangeAsync(countsMovedWhenJogged: +5_000);
        bed.Drive.Inputs[5] = false;   // the lower travel limit

        var act = () => bed.Axis.VerifyDirectionAsync();

        var ex = (await act.Should().ThrowAsync<MotionException>()).Which;
        ex.Error.Should().Be(MotionError.LimitTripped);
        ex.Message.Should().Contain("move the axis clear first");

        // And it really did refuse to START — no motion coil was ever raised.
        bed.Drive.Writes.Should().NotContain(o => o.Address == DeltaRegisters.M4_Move && o.Flag);
    }

    [Fact]
    public async Task ItRefusesFromTheUpperLimitToo()
    {
        using var bed = await ArrangeAsync(countsMovedWhenJogged: +5_000);
        bed.Drive.Inputs[6] = false;   // the upper travel limit

        var act = () => bed.Axis.VerifyDirectionAsync();

        (await act.Should().ThrowAsync<MotionException>()).Which.Error
            .Should().Be(MotionError.LimitTripped);
    }

    [Fact]
    public async Task OnAnAxisWithoutLimitSwitches_ThereIsNothingToRefuseFrom()
    {
        // The turntable has no limit inputs, so X5/X6 mean nothing on it and the check runs.
        var bed = AxisTestBed.Build(DeltaPositionerDefaults.Turntable, drive =>
        {
            drive.PositionCounts = PowerUpCounts;
            drive.Inputs[5] = false;
            drive.Inputs[6] = false;
            MoveOnJog(drive, +5_000);
        });

        using var _ = bed;
        await bed.Axis.PowerAsync(true);

        await bed.Axis.VerifyDirectionAsync();

        bed.Axis.State.Should().Be(AxisState.Standstill);
    }

    [Fact]
    public async Task TheCheckIsExclusiveWithMotion()
    {
        using var bed = await ArrangeAsync(countsMovedWhenJogged: +5_000);
        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));

        var act = () => bed.Axis.VerifyDirectionAsync();

        (await act.Should().ThrowAsync<MotionException>()).Which.Error.Should().Be(MotionError.Busy);
        await bed.Axis.StopAsync();
    }

    private static async Task<AxisTestBed> ArrangeAsync(int countsMovedWhenJogged)
    {
        var bed = AxisTestBed.Build(DeltaPositionerDefaults.Tilt, drive =>
        {
            drive.PositionCounts = PowerUpCounts;
            MoveOnJog(drive, countsMovedWhenJogged);
        });

        await bed.Axis.PowerAsync(true);
        return bed;
    }

    /// <summary>
    /// The machine's part, played by the test: raising <c>M4</c> is the instant the axis starts
    /// turning, so that is when the encoder count changes. Once, so a stop-start does not double it.
    /// </summary>
    private static void MoveOnJog(FakeDrive drive, int counts)
    {
        var moved = false;
        drive.React = (d, op) =>
        {
            if (moved || op.Address != DeltaRegisters.M4_Move || !op.Flag) return;
            moved = true;
            d.PositionCounts += counts;
        };
    }
}
