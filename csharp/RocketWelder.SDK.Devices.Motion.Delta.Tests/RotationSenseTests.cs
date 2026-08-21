using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// FR-2: <see cref="RotationSense"/> is meaningful only where two paths to a target exist. On a
/// wrapping axis it selects the path; on a limited one, anything but
/// <see cref="RotationSense.Shortest"/> is <b>rejected</b> with
/// <see cref="MotionError.UnsupportedSense"/> — never silently ignored.
/// </summary>
public class RotationSenseTests
{
    [Theory]
    // current, target, sense                      expected signed travel
    [InlineData(10.0, 350.0, RotationSense.Shortest, -20.0)]
    [InlineData(10.0, 350.0, RotationSense.Positive, 340.0)]
    [InlineData(10.0, 350.0, RotationSense.Negative, -20.0)]
    [InlineData(350.0, 10.0, RotationSense.Shortest, 20.0)]
    [InlineData(350.0, 10.0, RotationSense.Positive, 20.0)]
    [InlineData(350.0, 10.0, RotationSense.Negative, -340.0)]
    [InlineData(0.0, 180.0, RotationSense.Shortest, 180.0)]
    [InlineData(0.0, 181.0, RotationSense.Shortest, -179.0)]
    public void TheSenseSelectsThePath(double current, double target, RotationSense sense, double expected) =>
        DeltaAxis.WrappedTravel(current, target, sense).Should().BeApproximately(expected, 1e-9);

    [Fact]
    public void AnAbsoluteTargetIsNormalisedIntoTheWrapDomain()
    {
        // 730° is 10°: the contract says MoveAbsoluteAsync normalises the target into [Min, Max) on
        // a wrapping axis, which is what makes 730 and 10 the same command.
        DeltaAxis.WrappedTravel(0.0, 730.0, RotationSense.Shortest)
            .Should().BeApproximately(DeltaAxis.WrappedTravel(0.0, 10.0, RotationSense.Shortest), 1e-9);
    }

    [Fact]
    public void AnUnwrappedCurrentAngleDoesNotConfuseTheSense()
    {
        // The move loop works in unwrapped angle, so the current position may be 1000° after several
        // revolutions. The travel is still computed against the wrapped position.
        DeltaAxis.WrappedTravel(1090.0, 350.0, RotationSense.Shortest)
            .Should().BeApproximately(DeltaAxis.WrappedTravel(10.0, 350.0, RotationSense.Shortest), 1e-9);
    }

    [Fact]
    public void AskingForAPathToWhereWeAlreadyAre_IsZeroTravelInEitherSense()
    {
        DeltaAxis.WrappedTravel(45.0, 45.0, RotationSense.Positive).Should().Be(0.0);
        DeltaAxis.WrappedTravel(45.0, 45.0, RotationSense.Negative).Should().Be(0.0,
            "a full extra revolution to stand still would be an obedient absurdity");
    }

    [Fact]
    public async Task ALimitedAxisRejectsAnySenseButShortest()
    {
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt);

        bed.Axis.Capabilities.Should().NotHaveFlag(AxisCapabilities.ContinuousRotation);

        foreach (var sense in new[] { RotationSense.Positive, RotationSense.Negative })
        {
            var act = () => bed.Axis.MoveAbsoluteAsync(Degree<double>.Create(45), sense: sense);
            (await act.Should().ThrowAsync<MotionException>()).Which.Error
                .Should().Be(MotionError.UnsupportedSense, $"sense {sense} has no meaning on a limited axis");
        }
    }

    [Fact]
    public async Task ALimitedAxisAcceptsShortest_BecauseItIsTheOnlyPathItHas()
    {
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt);

        // Rejected for being out of range, NOT for the sense: this is the control that shows the
        // sense check is not simply refusing everything on a limited axis.
        var act = () => bed.Axis.MoveAbsoluteAsync(Degree<double>.Create(500), sense: RotationSense.Shortest);

        (await act.Should().ThrowAsync<MotionException>()).Which.Error.Should().Be(MotionError.OutOfRange);
    }

    [Fact]
    public async Task ATargetOutsideALimitedAxisTravel_IsRejected()
    {
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt);

        foreach (var target in new[] { -46.0, 91.0 })
        {
            var act = () => bed.Axis.MoveAbsoluteAsync(Degree<double>.Create(target));
            (await act.Should().ThrowAsync<MotionException>()).Which.Error.Should().Be(MotionError.OutOfRange);
        }

        bed.Axis.State.Should().Be(AxisState.Standstill, "a rejected target leaves the state intact");
    }

    [Fact]
    public async Task AnAbsoluteMoveOnAnUnhomedAxisThatNeedsHoming_IsRejectedNotHomed()
    {
        using var bed = await AxisTestBed.Tilt().PoweredAsync();

        var act = () => bed.Axis.MoveAbsoluteAsync(Degree<double>.Create(45));

        (await act.Should().ThrowAsync<MotionException>()).Which.Error.Should().Be(MotionError.NotHomed);
    }

    [Fact]
    public async Task AnUnhomedAxisReportsNoPosition_RatherThanAConfidentWrongOne()
    {
        using var bed = await AxisTestBed.Tilt().PoweredAsync();
        bed.Drive.PositionCounts = 41_730;   // arbitrary, as on a real encoder after power-up

        var status = await bed.Axis.ReadStatusAsync();

        status.Position.Should().BeNull();
        bed.Axis.Angle.Should().BeNull();
    }

    [Fact]
    public void TheWrappingAxisDeclaresContinuousRotation_AndNeitherAxisClaimsSynchronised()
    {
        using var turntable = AxisTestBed.Turntable();
        using var tilt = AxisTestBed.Tilt();

        turntable.Axis.Capabilities.Should().HaveFlag(AxisCapabilities.ContinuousRotation);
        tilt.Axis.Capabilities.Should().NotHaveFlag(AxisCapabilities.ContinuousRotation);

        // AC-18: every implementation this epic delivers reports Synchronised false, and asking
        // causes no motion.
        turntable.Axis.Capabilities.Should().NotHaveFlag(AxisCapabilities.Synchronised);
        tilt.Axis.Capabilities.Should().NotHaveFlag(AxisCapabilities.Synchronised);
        turntable.Drive.Writes.Should().BeEmpty();
    }

    [Fact]
    public void TheAxisKindIsDerivedFromTheLeaf_NotDeclared()
    {
        using var bed = AxisTestBed.Turntable();
        IMotionAxis axis = bed.Axis;

        axis.Kind.Should().Be(AxisKind.Rotary);
        axis.Should().BeAssignableTo<IRotaryAxis>();
    }
}
