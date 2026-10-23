namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of the bulk <c>PUT /api/programs/{id}/points</c> (epic-128 feature-001 design.md §1.1): every point as written,
/// in one commit, and the program's etag after the save.
/// </summary>
/// <param name="Points">The points as stored after the write.</param>
/// <param name="Etag">The program's etag after the edit — carry it into the next edit.</param>
public sealed record PointsEditResult(IReadOnlyList<ProgramPoint> Points, ProgramEtag Etag)
{
    /// <summary>rw2's own text when the write landed but its history entry did not (<c>X-History-Not-Recorded</c>);
    /// <see langword="null"/> when the write was recorded.</summary>
    public string? HistoryWarning { get; init; }
}
