namespace RocketWelder.SDK.Http.Station;

/// <summary>
/// Every station Programming default, all kinds (<c>/api/station/programming-defaults</c>, design.md §12.4).
/// </summary>
/// <param name="Values">One entry per index key.</param>
public sealed record StationDefaults(IReadOnlyList<StationDefault> Values);
