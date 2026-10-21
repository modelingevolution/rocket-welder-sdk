namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Body of <c>PUT …/values/{key}</c> and <c>PUT /api/programs/{id}/defaults/{key}</c> (design.md §12.2–§12.3).
/// </summary>
/// <param name="Value">The value text, e.g. a band <c>"1.2:3.5"</c> (mm, <c>inf</c> allowed).</param>
public sealed record SetValueRequest(string Value);
