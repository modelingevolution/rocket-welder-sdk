namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// The two direction knobs the documentation conflates and the machine does not: <b>mounting</b>
/// (<see cref="DeltaAxisConfig.InvertAngle"/>, permanently true on the tilt axis) and <b>wiring</b>
/// (<see cref="DeltaAxisConfig.InvertDirection"/>, the re-soldered defect). Risk R-4 names this
/// explicitly, and conflating them makes a positive velocity turn the machine the wrong way.
///
/// <para>
/// Asserted on the wire — the value of the <c>M5</c> direction coil — because that is the only place
/// the two knobs meet.
/// </para>
/// </summary>
public class DirectionConventionTests
{
    private static bool DirectionCoilAfterVelocity(DeltaAxisConfig config, double degPerSecond)
    {
        var bed = AxisTestBed.Build(config);
        try
        {
            bed.Axis.PowerAsync(true).GetAwaiter().GetResult();
            bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(degPerSecond)).GetAwaiter().GetResult();
            var coil = bed.Drive.ReadCoil(DeltaRegisters.PlcUnit, DeltaRegisters.M5_Direction);
            bed.Axis.StopAsync().GetAwaiter().GetResult();
            return coil;
        }
        finally
        {
            bed.Dispose();
        }
    }

    [Fact]
    public void OnAPlainlyMountedAndPlainlyWiredAxis_APositiveVelocityIsForward()
    {
        // M5 off = forward. Turntable: InvertAngle false, InvertDirection false.
        var config = DeltaPositionerDefaults.Turntable;
        config.InvertAngle.Should().BeFalse();
        config.InvertDirection.Should().BeFalse();

        DirectionCoilAfterVelocity(config, +5).Should().BeFalse();
        DirectionCoilAfterVelocity(config, -5).Should().BeTrue();
    }

    [Fact]
    public void AnInvertedMounting_FlipsTheCoilWithoutTouchingTheWiring()
    {
        // Tilt: InvertAngle true (the gearbox is mounted so a positive angle is a FALLING count),
        // InvertDirection false (the wiring is correct). A positive velocity must therefore command
        // the drive in reverse.
        var config = DeltaPositionerDefaults.Tilt;
        config.InvertAngle.Should().BeTrue();
        config.InvertDirection.Should().BeFalse();

        DirectionCoilAfterVelocity(config, +5).Should().BeTrue();
        DirectionCoilAfterVelocity(config, -5).Should().BeFalse();
    }

    [Fact]
    public void InvertedWiring_FlipsTheCoilIndependentlyOfTheMounting()
    {
        // The two knobs are separate facts: flipping the wiring on an otherwise identical axis
        // flips the coil, and flipping BOTH cancels out — which is precisely why one knob would be
        // wrong.
        var plain = DeltaPositionerDefaults.Turntable;
        var rewired = plain with { InvertDirection = true };
        var remounted = plain with { InvertAngle = true };
        var both = plain with { InvertAngle = true, InvertDirection = true };

        DirectionCoilAfterVelocity(plain, +5).Should().BeFalse();
        DirectionCoilAfterVelocity(rewired, +5).Should().BeTrue();
        DirectionCoilAfterVelocity(remounted, +5).Should().BeTrue();
        DirectionCoilAfterVelocity(both, +5).Should().BeFalse("two inversions cancel");
    }

    [Fact]
    public async Task AReportedSpeedIsSigned_InAngleSpaceRatherThanCountSpace()
    {
        // AxisStatus has no direction field: the sign IS the direction (P-2). On the tilt axis a
        // "forward" drive command lowers the angle, so the reported speed must be negative.
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt);
        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));

        var status = await bed.Axis.ReadStatusAsync();

        status.Speed.Should().BePositive("a +5 °/s command must read back as a positive angular speed");
        await bed.Axis.StopAsync();

        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(-5));
        (await bed.Axis.ReadStatusAsync()).Speed.Should().BeNegative();
        await bed.Axis.StopAsync();
    }

    [Fact]
    public async Task AStoppedAxisReportsZeroSpeed()
    {
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt);

        (await bed.Axis.ReadStatusAsync()).Speed.Should().Be(0.0);
    }
}
