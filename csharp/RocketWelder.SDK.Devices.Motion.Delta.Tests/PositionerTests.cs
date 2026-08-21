using Microsoft.Extensions.Logging.Abstractions;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// The device-level surface: the roster, the marker-derived kind, the indexer, and the two
/// all-axes verbs — <c>HomeAllAsync</c> and <c>StopAllAsync</c> — that a program and the Devices page
/// both go through.
///
/// <para>
/// This class exists because there were no device-level tests at all, and the gap hid a real bug:
/// <c>HomeAllAsync</c> turned a cancellation into an <see cref="InvalidOperationException"/>. Every
/// axis-level test in the suite passed while it did.
/// </para>
/// </summary>
public class PositionerTests
{
    [Fact]
    public void ThePositionerCarriesItsAxesInDeclarationOrder()
    {
        using var bed = PositionerTestBed.TwoAxes();

        bed.Positioner.Axes.Select(a => a.Name)
            .Should().Equal(DeltaPositionerDefaults.TiltAxisName, DeltaPositionerDefaults.TurntableAxisName);
    }

    [Fact]
    public void TheDeviceKindIsDerivedFromTheMarker_NotDeclared()
    {
        using var bed = PositionerTestBed.TwoAxes();
        IMotionDevice device = bed.Positioner;

        device.Kind.Should().Be(MotionDeviceKind.Positioner);
        device.Should().BeAssignableTo<IPositioner>();
    }

    [Fact]
    public void TheIndexerBindsADeclaredName_CaseInsensitively()
    {
        using var bed = PositionerTestBed.TwoAxes();

        bed.Positioner["turntable"].Name.Should().Be(DeltaPositionerDefaults.TurntableAxisName);
        bed.Positioner["TURNTABLE"].Should().BeSameAs(bed.Positioner["turntable"]);
    }

    [Fact]
    public void TheIndexerRejectsAnUndeclaredName_NamingWhatItDoesHave()
    {
        using var bed = PositionerTestBed.TwoAxes();

        var act = () => bed.Positioner["elevation"];

        var ex = act.Should().Throw<MotionException>().Which;
        ex.Error.Should().Be(MotionError.UnknownAxis);
        ex.AxisName.Should().Be("elevation");
        ex.Message.Should().Contain("tilt").And.Contain("turntable");
    }

    [Fact]
    public void TheTypedAccessorsBindThroughTheSameNames()
    {
        using var bed = PositionerTestBed.TwoAxes();

        bed.Positioner.Rotary("tilt").Should().BeSameAs(bed.Positioner["tilt"]);

        // Both axes of this machine are rotary, so asking for a linear one is the wrong-kind case.
        var act = () => bed.Positioner.Linear("tilt");
        act.Should().Throw<MotionException>().Which.Error.Should().Be(MotionError.WrongAxisKind);
    }

    [Fact]
    public void AnOwnerIdOfZeroIsRejected_BecauseItIsTheUnownedMarker()
    {
        var act = () => PositionerTestBed.TwoAxes(ownerId: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void APositionerNeedsAtLeastOneAxis()
    {
        var act = () => new DeltaPositioner(new DeviceId("t", Guid.NewGuid()), [], ownerId: 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task HomeAllHomesEveryAxisThatDeclaresHoming()
    {
        using var bed = PositionerTestBed.TwoAxes(d => AxisTestBed.ScriptTheCam(d));
        await bed.PowerAllAsync();

        await bed.Positioner.HomeAllAsync();

        bed.Positioner.Axes.Cast<DeltaAxis>().Should().OnlyContain(a => a.IsHomed);
        bed.Positioner.Axes.Should().OnlyContain(a => a.State == AxisState.Standstill);
    }

    [Fact]
    public async Task HomeAllPropagatesACancellation_RatherThanASequenceError()
    {
        // THE REGRESSION. RunOperationAsync rethrows OperationCanceledException, so a cancelled home
        // completes CANCELED and not Faulted — and the old First(t => t.IsFaulted) then threw
        // "Sequence contains no matching element", handing the caller an InvalidOperationException
        // in place of the cancellation it asked for. AC-10's path, on the device-level verb.
        using var bed = PositionerTestBed.TwoAxes();   // no cam script: homing searches forever
        await bed.PowerAllAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var thrown = await Record.ExceptionAsync(() => bed.Positioner.HomeAllAsync(cts.Token));

        thrown.Should().BeAssignableTo<OperationCanceledException>(
            "a cancelled HomeAll is cancelled, not broken");
    }

    [Fact]
    public async Task HomeAllLeavesEveryAxisCommandableAfterACancellation()
    {
        // AC-10 proper: "a state a subsequent command accepts".
        using var bed = PositionerTestBed.TwoAxes();
        await bed.PowerAllAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        await Record.ExceptionAsync(() => bed.Positioner.HomeAllAsync(cts.Token));

        bed.Positioner.Axes.Should().OnlyContain(a => a.State == AxisState.Standstill);
    }

    [Fact]
    public async Task HomeAllSurfacesAFaultFromOneAxis_WithItsMachineReadableReason()
    {
        // One axis's ladder never latches; the other homes fine. The failure the caller sees must be
        // the real one, with its axis name — not a wrapper, and not the healthy axis's silence.
        using var bed = PositionerTestBed.TwoAxes(configure: (name, drive) =>
            AxisTestBed.ScriptTheCam(drive, latchFires: name != DeltaPositionerDefaults.TiltAxisName));
        await bed.PowerAllAsync();

        var act = () => bed.Positioner.HomeAllAsync();

        var ex = (await act.Should().ThrowAsync<MotionException>()).Which;
        ex.AxisName.Should().Be(DeltaPositionerDefaults.TiltAxisName);
        ex.Message.Should().Contain("never latched");
    }

    [Fact]
    public async Task HomeAllWaitsForEveryAxisBeforeSurfacingAFailure()
    {
        // Leaving the others running while one fails is how a positioner ends up in a state nobody
        // commanded. By the time the exception arrives, no axis is still moving.
        using var bed = PositionerTestBed.TwoAxes(configure: (name, drive) =>
            AxisTestBed.ScriptTheCam(drive, latchFires: name != DeltaPositionerDefaults.TiltAxisName));
        await bed.PowerAllAsync();

        await Record.ExceptionAsync(() => bed.Positioner.HomeAllAsync());

        bed.Positioner.Axes.Should().NotContain(a => a.State == AxisState.Homing);
    }

    [Fact]
    public async Task StopAllStopsEveryAxis()
    {
        using var bed = PositionerTestBed.TwoAxes();
        await bed.PowerAllAsync();

        foreach (var axis in bed.Positioner.Axes.Cast<IRotaryAxis>())
            await axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));

        bed.Positioner.Axes.Should().OnlyContain(a => a.State == AxisState.ContinuousMotion);

        await bed.Positioner.StopAllAsync();

        bed.Positioner.Axes.Should().OnlyContain(a => a.State == AxisState.Standstill);
        bed.Drives.Should().OnlyContain(d => !d.ReadCoil(DeltaRegisters.PlcUnit, DeltaRegisters.M4_Move));
    }

    [Fact]
    public async Task IsReadyIsDerivedFromEveryAxisState()
    {
        using var bed = PositionerTestBed.TwoAxes();

        bed.Positioner.IsReady.Should().BeFalse("nothing is powered yet");
        await bed.PowerAllAsync();
        bed.Positioner.IsReady.Should().BeTrue();

        await ((IRotaryAxis)bed.Positioner["tilt"]).MoveVelocityAsync(AxisTestBed.DegPerSecond(5));
        bed.Positioner.IsReady.Should().BeFalse("one moving axis is enough");

        await bed.Positioner.StopAllAsync();
        bed.Positioner.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAllStatusReadsEveryAxis()
    {
        using var bed = PositionerTestBed.TwoAxes();
        await bed.PowerAllAsync();

        var statuses = await bed.Positioner.ReadAllStatusAsync();

        statuses.Should().HaveCount(2);
        statuses.Should().OnlyContain(s => s.State == AxisState.Standstill);
    }

    [Fact]
    public async Task DisconnectingStopsTheAxesBeforeReleasingTheLease()
    {
        // Ordering, and it matters: dropping the beat and releasing the lease on a still-moving
        // positioner hands a turning machine to whatever attaches next, and leaves the drive's
        // watchdog doing a shutdown's job.
        using var bed = PositionerTestBed.TwoAxes();
        await bed.PowerAllAsync();
        await ((IRotaryAxis)bed.Positioner["turntable"]).MoveVelocityAsync(AxisTestBed.DegPerSecond(5));

        await bed.Positioner.DisconnectAsync();

        bed.Positioner.Axes.Should().OnlyContain(a => a.State == AxisState.Standstill);
        bed.Drives.Should().OnlyContain(d => !d.ReadCoil(DeltaRegisters.PlcUnit, DeltaRegisters.M4_Move));

        var turntable = bed.Drives.Single(d => d.Host.Contains("turntable", StringComparison.Ordinal));
        var writes = turntable.Writes.ToArray();
        var lastMotionCoil = Array.FindLastIndex(writes,
            o => o.Address == DeltaRegisters.M4_Move && !o.Flag);
        var leaseRelease = Array.FindLastIndex(writes,
            o => o.Address == DeltaRegisters.D131_OwnerId && o.Value == AdvisoryLease.Unowned);

        if (leaseRelease >= 0)
            lastMotionCoil.Should().BeLessThan(leaseRelease, "the axis is stopped before the lease goes");
    }

    /// <summary>A <see cref="DeltaPositioner"/> over fake drives, one per axis.</summary>
    private sealed class PositionerTestBed : IDisposable
    {
        private PositionerTestBed(DeltaPositioner positioner, IReadOnlyList<FakeDrive> drives)
        {
            Positioner = positioner;
            Drives = drives;
        }

        public DeltaPositioner Positioner { get; }

        public IReadOnlyList<FakeDrive> Drives { get; }

        public static PositionerTestBed TwoAxes(Action<FakeDrive>? arrange = null, ushort ownerId = 1) =>
            TwoAxes(configure: (_, drive) => arrange?.Invoke(drive), ownerId);

        public static PositionerTestBed TwoAxes(Action<string, FakeDrive> configure, ushort ownerId = 1)
        {
            // Hosts carry the axis name so a test can tell the two drives apart afterwards.
            var axes = new[]
            {
                DeltaPositionerDefaults.Tilt with { Host = "fake-tilt" },
                DeltaPositionerDefaults.Turntable with { Host = "fake-turntable" },
            };

            var drives = new List<FakeDrive>();
            var positioner = new DeltaPositioner(
                new DeviceId("DeltaPositioner_VFDC2000", Guid.NewGuid()), axes, ownerId,
                new InMemoryAxisStateStore(), leaseTimeout: TimeSpan.Zero,
                NullLogger<DeltaPositioner>.Instance,
                cfg =>
                {
                    var drive = new FakeDrive(cfg.Host);
                    configure(cfg.Name, drive);
                    drives.Add(drive);
                    return drive;
                });

            return new PositionerTestBed(positioner, drives);
        }

        public async Task PowerAllAsync()
        {
            foreach (var axis in Positioner.Axes) await axis.PowerAsync(true);
        }

        public void Dispose() => Positioner.Dispose();
    }
}
