namespace RocketWelder.SDK.Http.Devices;

/// <summary>
/// Body of <c>PATCH /api/devices/config/{id}</c> (design.md §12.4): keys to set (text values) and keys to unset.
/// </summary>
/// <param name="Set">Keys to set, e.g. <c>{ "DefaultOffsetTolerance": "1.2:3.5" }</c>.</param>
/// <param name="Unset">Keys to clear back to the built-in.</param>
public sealed record ConfigureDeviceRequest(IReadOnlyDictionary<string, string>? Set, IReadOnlyList<string>? Unset);
