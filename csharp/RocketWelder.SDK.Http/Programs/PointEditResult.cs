namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of <c>PUT /api/programs/{id}/points/{name}</c> (epic-128 feature-001 design.md §1.1): the point as written and
/// the program's etag after the save.
/// </summary>
/// <param name="Point">The point as stored after the write.</param>
/// <param name="Etag">The program's etag after the edit — carry it into the next edit.</param>
public sealed record PointEditResult(ProgramPoint Point, ProgramEtag Etag)
{
    /// <summary>rw2's own text when the write landed but its history entry did not (<c>X-History-Not-Recorded</c>);
    /// <see langword="null"/> when the write was recorded.</summary>
    public string? HistoryWarning { get; init; }
}
