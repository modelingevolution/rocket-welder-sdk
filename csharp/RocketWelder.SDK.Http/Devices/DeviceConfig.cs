namespace RocketWelder.SDK.Http.Devices;

/// <summary>
/// A configured device's declared keys and stored values (<c>GET/PATCH /api/devices/config/{id}</c>, design.md §12.4).
/// </summary>
/// <param name="Id">The configured device id (<c>DeviceType/Guid</c>).</param>
/// <param name="DeviceType">The device type, e.g. <c>FairinoRobot</c>.</param>
/// <param name="Entries">Every declared key of the device type.</param>
public sealed record DeviceConfig(string Id, string DeviceType, IReadOnlyList<DeviceConfigEntry> Entries);
