namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of an add/edit edit — the affected block plus the tree's new etag to
/// carry into the next edit.
/// </summary>
/// <param name="Block">The block that was added or edited.</param>
/// <param name="Etag">The tree's etag after the edit.</param>
public sealed record BlockEditResult(
    BlockId Block,
    ProgramEtag Etag)
{
    /// <summary>rw2's own text when the edit landed but its history entry did not
    /// (<c>X-History-Not-Recorded</c>); <see langword="null"/> when the edit was recorded.</summary>
    public string? HistoryWarning { get; init; }

    /// <summary>rocket-welder2 epic-150 — the end block added together with a new group (adding a <c>Frame</c>, or a copy of
    /// one); <see langword="null"/> for every other edit.</summary>
    public BlockId? EndBlock { get; init; }

    /// <summary>rocket-welder2 epic-150 — the point names a copied group renamed, source → copy (<see cref="IProgramsApi.CopyBlockAsync"/>);
    /// <see langword="null"/> for every other edit.</summary>
    public IReadOnlyDictionary<PointName, PointName>? Points { get; init; }
}
