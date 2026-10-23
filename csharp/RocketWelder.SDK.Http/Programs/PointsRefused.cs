namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// The HTTP 422 body of the bulk <c>PUT /api/programs/{id}/points</c> (epic-128 feature-001 design.md §1.1, ADR-2): every
/// refused item. When any item is refused, nothing is written and no commit is made.
/// </summary>
/// <param name="Refused">Every refused item with its reason.</param>
public sealed record PointsRefused(IReadOnlyList<PointRefusal> Refused);
