namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// A history answer (<c>GET /api/programs/{id}/history</c>, <c>GET /api/repositories/{id}/history</c>, epic-128
/// feature-001 design.md §3): commits newest first.
/// </summary>
/// <param name="Directory">The program's repository-relative folder; empty for a repository.</param>
/// <param name="Commits">The commits, newest first.</param>
public sealed record HistoryLog(string Directory, IReadOnlyList<HistoryCommit> Commits);
