namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// One commit of a program's or a repository's history (epic-128 feature-001 design.md §3). rw2 returns this type
/// (ADR-1).
/// </summary>
/// <param name="Sha">The full commit sha.</param>
/// <param name="Parents">The parent shas.</param>
/// <param name="Message">The commit message, e.g. <c>Edit point p1 via API</c>.</param>
/// <param name="AuthorName">The author's name.</param>
/// <param name="AuthorEmail">The author's e-mail.</param>
/// <param name="Time">When the commit was made.</param>
/// <param name="Files">The repository-relative files the commit changed.</param>
public sealed record HistoryCommit(
    string Sha,
    IReadOnlyList<string> Parents,
    string Message,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset Time,
    IReadOnlyList<string> Files);
