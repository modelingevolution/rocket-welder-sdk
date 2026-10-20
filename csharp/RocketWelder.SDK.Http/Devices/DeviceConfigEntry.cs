namespace RocketWelder.SDK.Http.Devices;

/// <summary>
/// One declared configuration key of a device (<c>GET/PATCH /api/devices/config/{id}</c>, design.md §12.4).
/// </summary>
/// <param name="Key">The property name, e.g. <c>DefaultOffsetTolerance</c>.</param>
/// <param name="DisplayName">Human-readable name from the device schema.</param>
/// <param name="Group">Schema group, e.g. <c>Adaptation defaults</c>.</param>
/// <param name="ValueType">Value-type hint, e.g. <c>tolerance-band</c>.</param>
/// <param name="Value">The stored value as text; <see langword="null"/> when unset (the built-in applies).</param>
public sealed record DeviceConfigEntry(string Key, string DisplayName, string? Group, string? ValueType, string? Value);
