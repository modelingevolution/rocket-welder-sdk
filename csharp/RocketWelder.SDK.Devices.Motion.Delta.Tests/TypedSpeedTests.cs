using ModelingEvolution.Drawing;
using ModelingEvolution.Drawing.Units;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// FR-5: speeds are commanded in the axis's own engineering units, converted per machine by a
/// measured fit, and a speed outside the achievable range is <b>rejected, never clamped</b> — a
/// <c>Percentage</c> included, which resolves against <c>MaxSpeed</c> first and is then subject to
/// the same rule (AC-6, AC-19).
/// </summary>
public class TypedSpeedTests
{
    private static readonly SpeedCalibration Measured = DeltaPositionerDefaults.TurntableSpeed;

    [Fact]
    public void TheCalibrationCrossesBetweenTheTwoUnits_AndBackAgain()
    {
        var hz = Frequency<double>.FromHertz(20.0);

        var speed = Measured.ToAngularSpeed(hz);
        var back = Measured.ToFrequency(speed);

        speed.Value.Should().BeApproximately(0.5435 * 20.0 - 0.199, 1e-9);
        back.Hertz.Should().BeApproximately(20.0, 1e-9);
    }

    [Fact]
    public void ASignedVelocityConvertsByItsMagnitude_BecauseTheSignIsCarriedByTheDirectionCoil()
    {
        var forward = new AngularSpeed<double, DegreePerSecond<double>>(5.0);
        var reverse = new AngularSpeed<double, DegreePerSecond<double>>(-5.0);

        Measured.ToFrequency(forward).Should().Be(Measured.ToFrequency(reverse));
    }

    /// <summary>
    /// The speed sweep measured on the turntable on 2026-08-19 (<c>current-state.md</c>
    /// §"Speed calibration"), requested Hz against measured °/s. Asserted against the
    /// <b>measurement</b>, never against the model that was fitted to it.
    /// </summary>
    public static TheoryData<double, double> MeasuredSweep => new()
    {
        { 1.00, 0.245 }, { 1.50, 0.574 }, { 3.00, 1.433 }, { 5.00, 2.538 },
        { 10.0, 5.218 }, { 20.0, 10.679 }, { 50.0, 26.968 },
    };

    [Theory]
    [MemberData(nameof(MeasuredSweep))]
    public void TheFitTracksTheMeasurementInsideItsStatedRegion(double hz, double measuredDegPerSecond)
    {
        // The fit is stated for hz >= 2 and claims nothing below it, so that is where it is held to
        // account. Outside the region it is checked by the test below instead of being quietly
        // trusted.
        if (hz < 2.0) return;

        Measured.ToDegPerSecond(hz).Should().BeApproximately(measuredDegPerSecond,
            measuredDegPerSecond * 0.01);
    }

    [Fact]
    public void BelowTwoHertzTheFitOverPredicts_WhichIsWhyAC22RelaxedItsLowestPoint()
    {
        // 1 Hz sits outside the fit's verified region and the fit reads about 40 % high there. This
        // is not a defect in the fit — it is the reason AC-22 relaxed its 1 °/s point to ±10 %
        // pending a bench measurement at ~2.2 Hz, and the reason no adapter code trusts the fit
        // below MinJogHz.
        var overPrediction = (Measured.ToDegPerSecond(1.0) - 0.245) / 0.245;

        overPrediction.Should().BeGreaterThan(0.30);
    }

    [Fact]
    public void TheNominalRatioMoreThanDoublesTheRealSpeedAtOneHertz()
    {
        // The reason FR-5 insists on a per-machine fit rather than the gearing ratio: the nominal
        // figure is a few per cent high at 50 Hz and +128 % at 1 Hz — and low speed is exactly where
        // circumferential welding runs. Both compared against the MEASURED points.
        var nominal = DeltaPositionerDefaults.Turntable.TheoreticalDegPerSecondPerHz;

        ((nominal * 50.0 - 26.968) / 26.968).Should().BeLessThan(0.05);
        ((nominal * 1.0 - 0.245) / 0.245).Should().BeApproximately(1.28, 0.02);
    }

    [Fact]
    public void ADeadBandIsANegativeIntercept_AndBelowItTheAxisStandsStill()
    {
        Measured.DeadBandHz.Should().BeApproximately(0.199 / 0.5435, 1e-9);
        Measured.ToDegPerSecond(0.1).Should().Be(0.0, "below the dead band the axis does not turn");
    }

    [Fact]
    public void WithNoMeasuredFit_TheAxisFallsBackToTheTheoreticalRatio()
    {
        var uncalibrated = DeltaPositionerDefaults.Turntable with { Speed = null };

        uncalibrated.SpeedCalibration.Slope
            .Should().Be(uncalibrated.TheoreticalDegPerSecondPerHz);
        uncalibrated.SpeedCalibration.Intercept.Should().Be(0.0);
    }

    [Fact]
    public void TheAxisBoundsAreTheCalibrationAppliedToTheDrivesOwnLimits()
    {
        using var bed = AxisTestBed.Turntable();
        var cfg = DeltaPositionerDefaults.Turntable;

        bed.Axis.MinSpeed.Value.Should().BeApproximately(Measured.ToDegPerSecond(cfg.MinJogHz.Hertz), 1e-9);
        bed.Axis.MaxSpeed.Value.Should().BeApproximately(Measured.ToDegPerSecond(cfg.MaxMoveHz.Hertz), 1e-9);
    }

    [Fact]
    public async Task ASpeedBelowTheAxisMinimum_IsRejected_AndTheAxisDoesNotMove()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();
        var tooSlow = new AngularSpeed<double, DegreePerSecond<double>>(bed.Axis.MinSpeed.Value / 2);
        var before = bed.Drive.Ops.Count;

        var act = () => bed.Axis.MoveVelocityAsync(tooSlow);

        (await act.Should().ThrowAsync<MotionException>()).Which.Error
            .Should().Be(MotionError.UnreachableSpeed);
        bed.Axis.State.Should().Be(AxisState.Standstill);
        bed.Drive.Ops.Skip(before).Should().NotContain(o => o.IsWrite,
            "AC-6: the axis does not move — nothing reaches the wire at all");
    }

    [Fact]
    public async Task ASpeedAboveTheAxisMaximum_IsRejectedRatherThanClamped()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();
        var tooFast = new AngularSpeed<double, DegreePerSecond<double>>(bed.Axis.MaxSpeed.Value * 2);

        var act = () => bed.Axis.MoveVelocityAsync(tooFast);

        (await act.Should().ThrowAsync<MotionException>()).Which.Error
            .Should().Be(MotionError.UnreachableSpeed);
    }

    [Fact]
    public async Task APercentageResolvesAgainstMaxSpeedFirst_ThenFacesTheSameRule()
    {
        // FR-5's exact wording: Percentage(1) of a fast axis that lands below the drive's floor is
        // REJECTED, not raised to it. Resolve-then-reject, in that order.
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Turntable);
        var onePercent = bed.Axis.MaxSpeed * new Percentage(1);

        onePercent.Value.Should().BeLessThan(bed.Axis.MinSpeed.Value,
            "the arrangement only means anything if 1 % really lands under the floor");

        var act = () => bed.Axis.MoveAbsoluteAsync(Degree<double>.Create(90), new Percentage(1));

        (await act.Should().ThrowAsync<MotionException>()).Which.Error
            .Should().Be(MotionError.UnreachableSpeed);
    }

    [Fact]
    public async Task APercentageThatResolvesInsideTheRange_IsAccepted()
    {
        using var bed = await AxisTestBed.Turntable().PoweredAsync();

        await bed.Axis.MoveVelocityAsync(bed.Axis.MaxSpeed * new Percentage(50));

        bed.Axis.State.Should().Be(AxisState.ContinuousMotion);
        await bed.Axis.StopAsync();
    }

    [Fact]
    public async Task TheCommandedFrequencyOnTheWire_IsTheCalibrationsAnswer()
    {
        // The whole typed chain, end to end: °/s in, Hz on the wire, in 0.01 Hz units.
        using var bed = await AxisTestBed.Turntable().PoweredAsync();
        var wanted = AxisTestBed.DegPerSecond(10.0);
        var expected = (ushort)Math.Round(Measured.ToHz(10.0) * 100);

        await bed.Axis.MoveVelocityAsync(wanted);

        bed.Drive.ReadHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency)
            .Should().Be(expected);
        await bed.Axis.StopAsync();
    }
}
