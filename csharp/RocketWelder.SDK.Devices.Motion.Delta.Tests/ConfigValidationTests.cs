using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// The other half of FR-5's "rejected, never clamped".
///
/// <para>
/// The caller-facing check can only police speeds a caller supplies. The default traverse speed, the
/// seek speed, the nudge speed and a speed restored from disk never pass through it, so a
/// configuration with <c>MoveHz</c> under <c>MinJogHz</c> — or a persisted 0 °/s — used to be
/// silently raised to the drive's floor deep inside the jog. That was the one place the promise did
/// not hold, and these tests are what keep it closed.
/// </para>
/// </summary>
public class ConfigValidationTests
{
    [Fact]
    public void TheShippedDefaultsAreInternallyConsistent()
    {
        // If this ever fails, the machine constants contradict each other and every speed the axis
        // commands is suspect.
        var act = () =>
        {
            DeltaPositionerDefaults.Tilt.Validate();
            DeltaPositionerDefaults.Turntable.Validate();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void ADefaultTraverseSpeedBelowTheFloorIsRefusedAtConstruction()
    {
        // The case that used to be clamped in silence: MoveHz never faces RequireReachable, because
        // it is what a caller gets when they supply no speed at all.
        var broken = DeltaPositionerDefaults.Turntable with
        {
            MoveHz = Frequency<double>.FromHertz(DeltaPositionerDefaults.Turntable.MinJogHz.Hertz / 2),
        };

        var act = () => AxisTestBed.Build(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*MoveHz*");
    }

    [Theory]
    [InlineData(nameof(DeltaAxisConfig.SeekHz))]
    [InlineData(nameof(DeltaAxisConfig.NudgeHz))]
    public void EverySpeedTheAxisCommandsItselfMustBeReachable(string field)
    {
        var tooFast = Frequency<double>.FromHertz(DeltaPositionerDefaults.Turntable.MaxMoveHz.Hertz * 2);
        var broken = field == nameof(DeltaAxisConfig.SeekHz)
            ? DeltaPositionerDefaults.Turntable with { SeekHz = tooFast }
            : DeltaPositionerDefaults.Turntable with { NudgeHz = tooFast };

        var act = () => AxisTestBed.Build(broken);

        act.Should().Throw<ArgumentException>().WithMessage($"*{field}*");
    }

    [Fact]
    public void AnUnorderedSpeedRangeIsRefused()
    {
        var broken = DeltaPositionerDefaults.Turntable with
        {
            MaxMoveHz = Frequency<double>.FromHertz(0.5),   // below MinJogHz
        };

        var act = () => AxisTestBed.Build(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*MaxMoveHz*");
    }

    [Fact]
    public void AJogGuardBelowTheAdvertisedMaximumIsRefused()
    {
        // Otherwise the axis advertises a MaxSpeed the guard would reject — an axis that refuses its
        // own documented top speed.
        var broken = DeltaPositionerDefaults.Turntable with
        {
            MaxJogHz = Frequency<double>.FromHertz(DeltaPositionerDefaults.Turntable.MaxMoveHz.Hertz - 1),
        };

        var act = () => AxisTestBed.Build(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*MaxJogHz*");
    }

    [Fact]
    public void ANonPositiveToleranceIsRefused()
    {
        var broken = DeltaPositionerDefaults.Turntable with { Tolerance = Degree<double>.Create(0) };

        var act = () => AxisTestBed.Build(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*Tolerance*");
    }

    [Fact]
    public void ATravelRangeWithMaxBelowMinIsRefused()
    {
        var broken = DeltaPositionerDefaults.Tilt with
        {
            Min = Degree<double>.Create(10), Max = Degree<double>.Create(-10),
        };

        var act = () => AxisTestBed.Build(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*Max*");
    }

    [Fact]
    public void ASpeedCalibrationWithANonPositiveSlopeIsRefused()
    {
        // Every speed conversion divides by the slope. A zero or negative one turns the whole typed
        // chain into nonsense rather than into an error.
        var broken = DeltaPositionerDefaults.Turntable with { Speed = new SpeedCalibration(0, 0) };

        var act = () => AxisTestBed.Build(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*slope*");
    }

    [Fact]
    public void TheMessageNamesTheAxisAndTheField_SoAWrongMachineIsObviousAtStartup()
    {
        var broken = DeltaPositionerDefaults.Tilt with
        {
            MoveHz = Frequency<double>.FromHertz(0.1),
        };

        var act = () => AxisTestBed.Build(broken);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{DeltaPositionerDefaults.TiltAxisName}*")
            .WithMessage("*MoveHz*");
    }

    [Fact]
    public async Task APersistedSpeedOutsideTheRangeIsIgnored_NotClampedIntoAMove()
    {
        // A stored 0 °/s converts to well under the drive's floor. Raising it silently is how an
        // axis ends up traversing at a speed nobody chose, so the configured default is used instead.
        var store = new InMemoryAxisStateStore();
        await store.SaveAsync(DeltaPositionerDefaults.TurntableAxisName,
            new AxisPersistedState(ZeroOffset: 0, Homed: true, SpeedDegPerSecond: 0));

        var commanded = await TraverseSpeedOfAMoveWithNoSpeedAsync(store);

        commanded.Should().BeApproximately(DeltaPositionerDefaults.Turntable.MoveHz.Hertz, 0.01,
            "the configured default is the honest fallback");
        commanded.Should().BeGreaterThan(DeltaPositionerDefaults.Turntable.MinJogHz.Hertz,
            "and it is certainly not the drive's floor, which is what the old silent clamp produced");
    }

    [Fact]
    public async Task APersistedSpeedInsideTheRangeIsHonoured()
    {
        // The control: the rejection above must not quietly become "ignore whatever was stored".
        const double wantedHz = 20.0;
        var store = new InMemoryAxisStateStore();
        await store.SaveAsync(DeltaPositionerDefaults.TurntableAxisName,
            new AxisPersistedState(0, Homed: true,
                SpeedDegPerSecond: DeltaPositionerDefaults.TurntableSpeed.ToDegPerSecond(wantedHz)));

        var commanded = await TraverseSpeedOfAMoveWithNoSpeedAsync(store);

        commanded.Should().BeApproximately(wantedHz, 0.01);
    }

    /// <summary>
    /// The frequency a move with <b>no speed argument</b> puts on the wire — which is the only way
    /// to observe the restored traverse speed, since it never passes through the caller-facing
    /// check. The move is cancelled as soon as it has committed to a speed: the fake drive's
    /// position never changes, so it would otherwise run to its timeout.
    /// </summary>
    private static async Task<double> TraverseSpeedOfAMoveWithNoSpeedAsync(IAxisStateStore store)
    {
        using var bed = AxisTestBed.Build(DeltaPositionerDefaults.Turntable, store: store);
        await bed.Axis.InitialiseAsync(CancellationToken.None);
        await bed.Axis.PowerAsync(true);

        using var cts = new CancellationTokenSource();
        var moving = bed.Axis.MoveRelativeAsync(Degree<double>.Create(90), ct: cts.Token);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        ushort raw = 0;
        while (DateTime.UtcNow < deadline)
        {
            raw = bed.Drive.ReadHolding(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency);
            if (raw > 0) break;
            await Task.Delay(10);
        }

        await cts.CancelAsync();
        try { await moving; } catch (OperationCanceledException) { /* that is how we end it */ }

        raw.Should().BeGreaterThan(0, "the move must actually command a frequency for this to mean anything");
        return raw / 100.0;
    }
}
