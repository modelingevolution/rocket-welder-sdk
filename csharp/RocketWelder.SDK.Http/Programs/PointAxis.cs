namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// One external-axis sample of a taught point (<c>/api/programs/{id}/points</c>, epic-128 feature-001 design.md §1.1).
/// </summary>
/// <param name="DeviceRole">The device the axis belongs to, e.g. <c>Positioner</c>.</param>
/// <param name="AxisName">The axis within the device, e.g. <c>A1</c>.</param>
/// <param name="Kind"><c>Rotary</c> (value in deg) or <c>Linear</c> (value in mm).</param>
/// <param name="Value">The axis position, in deg or mm per <paramref name="Kind"/>.</param>
public sealed record PointAxis(string DeviceRole, string AxisName, string Kind, double Value);
