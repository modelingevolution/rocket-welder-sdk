namespace RocketWelder.SDK.Http.Modbus;

/// <summary>
/// <c>/api/modbus/{deviceName}/{tagName}</c> — read tags from tagged Modbus
/// devices (welding machines, peripheral controllers).
/// </summary>
public interface IModbusApi
{
    /// <summary>
    /// <c>GET /api/modbus/{deviceName}/{tagName}</c> — read a single named tag.
    /// Returns null if the device is unknown, the tag is not in the device's
    /// mapping, or the device is unreachable.
    /// </summary>
    Task<ModbusTagValue?> ReadTagAsync(string deviceName, string tagName, CancellationToken ct = default);
}
