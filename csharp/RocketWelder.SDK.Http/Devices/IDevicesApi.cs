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
}
