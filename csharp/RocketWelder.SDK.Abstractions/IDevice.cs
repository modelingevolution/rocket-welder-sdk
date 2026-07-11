namespace RocketWelder.SDK.Abstractions;

/// <summary>
/// Common base for every host-registered device. Carries the stable identity
/// (<see cref="Id"/>) that the host mints at registration and that downstream
/// catalogues (adaptive-points, calibration, telemetry) use to scope state
/// per-device.
///
/// <para>
/// All concrete device contracts (<see cref="IRobot"/>, <see cref="ICamera"/>,
/// <see cref="IDistanceSensor"/>, <see cref="IWeldingMachine"/>) extend
/// <see cref="IDevice"/>. New device interfaces should follow the same pattern
/// — inherit <see cref="IDevice"/> rather than redeclaring an <c>Id</c> property,
/// so the identity contract stays uniform.
/// </para>
/// </summary>
public interface IDevice : IDisposable
{
    /// <summary>
    /// Stable identity of this device, assigned by the host at registration.
    /// In rocket-welder2 this is the <c>CreatePeripheralDevice</c> command's
    /// recipient id.
    /// </summary>
    DeviceId Id { get; }
}
