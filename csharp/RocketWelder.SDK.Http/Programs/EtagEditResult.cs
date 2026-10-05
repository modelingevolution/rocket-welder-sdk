namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of an edit that returns only the tree's new etag — remove and move.
/// </summary>
/// <param name="Etag">The tree's etag after the edit.</param>
/// <param name="HistoryWarning">rw2's own text when the edit landed but its history entry did not
/// (<c>X-History-Not-Recorded</c>); <see langword="null"/> when the edit was recorded.</param>
public sealed record EtagEditResult(ProgramEtag Etag, string? HistoryWarning);
