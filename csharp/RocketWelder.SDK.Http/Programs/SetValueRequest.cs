namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Body of <c>PUT …/values/{key}</c> and <c>PUT /api/programs/{id}/defaults/{key}</c> (design.md §12.2–§12.3).
/// </summary>
/// <param name="Value">The value text, e.g. a band <c>"1.2:3.5"</c> (mm, <c>inf</c> allowed).</param>
public sealed record SetValueRequest(string Value)
{
    /// <summary>For a station cell value (<c>PUT /api/station/programming-defaults/Cell.*</c>): where the value comes from; the station refuses a
    /// cell value without one. Ignored elsewhere; omitted from the body when null, so other requests are unchanged.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }
}
