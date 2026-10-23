namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Thrown when rw2 refuses a bulk point write (HTTP 422, epic-128 feature-001 design.md §1.1, ADR-2). Nothing was
/// written and no commit was made; <see cref="Refused"/> lists every refused name with its reason.
/// </summary>
public sealed class PointsRefusedException(IReadOnlyList<PointRefusal> refused) : Exception(Describe(refused))
{
    /// <summary>Every refused item with rw2's reason.</summary>
    public IReadOnlyList<PointRefusal> Refused { get; } = refused;

    private static string Describe(IReadOnlyList<PointRefusal> refused) =>
        $"{refused.Count} point(s) refused; nothing was written (HTTP 422): "
        + string.Join("; ", refused.Select(r => $"{r.Name}: {r.Reason}"));
}
