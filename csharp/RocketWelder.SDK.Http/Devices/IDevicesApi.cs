namespace RocketWelder.SDK.Http.Devices;

/// <summary>
/// <c>/api/devices</c> — read access to the welder's <c>DeviceRegistry</c>.
/// </summary>
public interface IDevicesApi
{
    /// <summary>
    /// Lists every registered device. <c>GET /api/devices</c>.
    /// Returns an empty list if no devices are registered.
    /// </summary>
    Task<IReadOnlyList<DeviceInfo>> ListAsync(CancellationToken ct = default);
}
