using ModelingEvolution.Drawing;
using ModelingEvolution.Drawing.Units;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Motion.Tests;

/// <summary>
/// Test doubles for the contract. They implement the shape and nothing else — every command throws,
/// because these tests are about what the <b>type system</b> and the derived-<c>Kind</c> switches
/// say, never about motion.
/// </summary>
internal abstract class AxisDouble : IMotionAxis
{
    public string Name { get; init; } = "axis";
    public string DisplayName { get; init; } = "Axis";
    public AxisState State { get; init; } = AxisState.Standstill;
    public AxisCapabilities Capabilities { get; init; } = AxisCapabilities.None;
    public AxisStatus Status { get; init; }

    public Task<AxisStatus> ReadStatusAsync(CancellationToken ct = default) => Task.FromResult(Status);

    public event EventHandler<AxisStatus>? StatusChanged;

    /// <summary>Raises <see cref="StatusChanged"/>; also keeps the compiler from warning the event unused.</summary>
    public void RaiseStatusChanged(AxisStatus status) => StatusChanged?.Invoke(this, status);

    public Task PowerAsync(bool on, CancellationToken ct = default) => throw new NotSupportedException();
    public Task HomeAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task StopAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task ResetAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>An axis that implements the base and NO typed leaf — outside the contract's closed set.</summary>
internal sealed class LeaflessAxisDouble : AxisDouble;

internal sealed class RotaryAxisDouble : AxisDouble, IRotaryAxis
{
    public Degree<double>? Angle => null;
    public Degree<double> Min => Degree<double>.Create(0);
    public Degree<double> Max => Degree<double>.Create(360);
    public Degree<double> Tolerance => Degree<double>.Create(0.05);
    public AngularSpeed<double, DegreePerSecond<double>> MinSpeed => new(0.5);
    public AngularSpeed<double, DegreePerSecond<double>> MaxSpeed => new(15);

    public Task MoveAbsoluteAsync(Degree<double> target, AngularSpeed<double, DegreePerSecond<double>>? speed = null,
        RotationSense sense = RotationSense.Shortest, CancellationToken ct = default) => throw new NotSupportedException();
    public Task MoveAbsoluteAsync(Degree<double> target, Percentage speedOfMax,
        RotationSense sense = RotationSense.Shortest, CancellationToken ct = default) => throw new NotSupportedException();
    public Task MoveRelativeAsync(Degree<double> delta, AngularSpeed<double, DegreePerSecond<double>>? speed = null,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task MoveRelativeAsync(Degree<double> delta, Percentage speedOfMax,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task MoveVelocityAsync(AngularSpeed<double, DegreePerSecond<double>> velocity,
        CancellationToken ct = default) => throw new NotSupportedException();
}

internal sealed class LinearAxisDouble : AxisDouble, ILinearAxis
{
    public Length<double, Millimetre<double>>? Offset => null;
    public Length<double, Millimetre<double>> Min => new(0);
    public Length<double, Millimetre<double>> Max => new(2000);
    public Length<double, Millimetre<double>> Tolerance => new(0.1);
    public Speed<double, MillimetrePerSecond<double>> MinSpeed => new(1);
    public Speed<double, MillimetrePerSecond<double>> MaxSpeed => new(250);

    public Task MoveAbsoluteAsync(Length<double, Millimetre<double>> target,
        Speed<double, MillimetrePerSecond<double>>? speed = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task MoveAbsoluteAsync(Length<double, Millimetre<double>> target, Percentage speedOfMax,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task MoveRelativeAsync(Length<double, Millimetre<double>> delta,
        Speed<double, MillimetrePerSecond<double>>? speed = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task MoveRelativeAsync(Length<double, Millimetre<double>> delta, Percentage speedOfMax,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task MoveVelocityAsync(Speed<double, MillimetrePerSecond<double>> velocity,
        CancellationToken ct = default) => throw new NotSupportedException();
}

internal class MotionDeviceDouble : IMotionDevice
{
    public MotionDeviceDouble(params IMotionAxis[] axes) => Axes = axes;

    public DeviceId Id { get; } = new("test-motion-device", Guid.Empty);
    public IReadOnlyList<IMotionAxis> Axes { get; }

    public IMotionAxis this[string name] =>
        Axes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal))
        ?? throw new MotionException(MotionError.UnknownAxis, $"No axis '{name}' on this device.", name);

    public Task HomeAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task StopAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public void Dispose() { }
}

/// <summary>A device with no marker interface — outside the contract's closed set.</summary>
internal sealed class MarkerlessDeviceDouble(params IMotionAxis[] axes) : MotionDeviceDouble(axes);

internal sealed class PositionerDouble(params IMotionAxis[] axes) : MotionDeviceDouble(axes), IPositioner;

internal sealed class LinearTrackDouble(params IMotionAxis[] axes) : MotionDeviceDouble(axes), ILinearTrack;
