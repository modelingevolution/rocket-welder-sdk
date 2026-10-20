using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Devices;

/// <summary>
/// <c>/api/devices</c> — read and delete access to the welder's <c>DeviceRegistry</c>.
/// </summary>
public interface IDevicesApi
{
    /// <summary>
    /// Lists every registered device. <c>GET /api/devices</c>.
    /// Returns an empty list if no devices are registered.
    /// </summary>
    Task<IReadOnlyList<DeviceInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// <c>DELETE /api/devices/{id}</c> — removes a configured peripheral device by the
    /// <see cref="DeviceInfo.Id"/> returned from <see cref="ListAsync"/>. The server does not
    /// force-stop or otherwise guard against an in-progress weld — cancel any running program on
    /// this device before deleting it.
    /// </summary>
    /// <exception cref="HttpRequestException">
    /// The id was rejected (HTTP 400) or does not match a configured device (HTTP 404), or the
    /// server failed otherwise.
    /// </exception>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /api/devices/config/{id}</c> — every declared configuration key of the device with its stored value
    /// (epic-029 feature-011, design.md §12.4).
    /// </summary>
    /// <exception cref="HttpRequestException">The device is unknown (HTTP 404), or the server failed otherwise.</exception>
    Task<DeviceConfig> GetConfigAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// <c>PATCH /api/devices/config/{id}</c> — set and unset configuration keys in one write; returns the device's
    /// configuration after it (design.md §12.4).
    /// </summary>
    /// <exception cref="ValueRefusedException">An unknown key or a value that does not convert (HTTP 400); <see cref="ValueRefusedException.Reason"/> is rw2's sentence.</exception>
    /// <exception cref="HttpRequestException">The device is unknown (HTTP 404), or the server failed otherwise.</exception>
    Task<DeviceConfig> ConfigureAsync(string id, ConfigureDeviceRequest request, CancellationToken ct = default);
}
