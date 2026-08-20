using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// A mechanism with axes — <b>heterogeneous by design</b> (FR-3): a column-and-boom mixes a linear
/// and a rotary axis in one device, so <see cref="Axes"/> is a list of the unit-free base.
///
/// <para>
/// A positioner is not a special kind of thing. Every mature cell model — ABB mechanical units,
/// KUKA external axes, MoveIt planning groups, OPC UA <c>MotionDevice</c> — puts the robot and the
/// positioner in the <i>same</i> category, and that generalises for free to a linear track
/// (1 axis), a headstock/tailstock pair (2 axes) or a 3-axis manipulator.
/// </para>
/// </summary>
public interface IMotionDevice : IDevice
{
    /// <summary>The device's axes, in the order the plugin declared them.</summary>
    IReadOnlyList<IMotionAxis> Axes { get; }

    /// <summary>Binds an axis by its plugin-frozen name.</summary>
    /// <param name="name">The frozen axis name (FR-8).</param>
    /// <exception cref="MotionException">No axis of that name is declared
    /// (<see cref="MotionError.UnknownAxis"/>).</exception>
    IMotionAxis this[string name] { get; }

    /// <summary>Homes every axis that declares <see cref="AxisCapabilities.Homing"/>.</summary>
    Task HomeAllAsync(CancellationToken ct = default);

    /// <summary>Stops every axis.</summary>
    Task StopAllAsync(CancellationToken ct = default);

    /// <summary>
    /// The device kind, <b>derived</b> from the marker interface this device implements. The marker
    /// is the primary classification and this enum is computed from it, mirroring
    /// <see cref="IMotionAxis.Kind"/> — which is also how the existing registry works, since
    /// <c>DeviceTypeInfo.InterfaceClrType</c> <i>is</i> the marker.
    /// </summary>
    /// <exception cref="NotSupportedException">The implementation extends
    /// <see cref="IMotionDevice"/> without a device marker, so no kind can be derived.</exception>
    MotionDeviceKind Kind => this switch
    {
        IPositioner => MotionDeviceKind.Positioner,
        ILinearTrack => MotionDeviceKind.Track,
        _ => throw new NotSupportedException($"{GetType().Name} implements no device marker"),
    };
}
