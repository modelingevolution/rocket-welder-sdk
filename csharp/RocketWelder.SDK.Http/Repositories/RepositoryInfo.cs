namespace RocketWelder.SDK.Http.Repositories;

/// <summary>
/// Wire shape for a single repository registered on the welder. Backs
/// <c>GET /api/repositories</c> (server <c>RepositoryDto</c>).
/// </summary>
/// <param name="Id">Repository identifier (GUID). Matches <c>GitRepositoryId</c> on the server.</param>
/// <param name="Name">Repository name; also its directory under the welder's repository store.</param>
/// <param name="RepositoryUrl">Git remote URL, or empty for a local-only (git-init'd) repository.</param>
/// <param name="Status">Coarse sync status as reported by the server (e.g. <c>Local</c>, <c>Synced</c>, <c>Error</c>).</param>
/// <param name="CurrentBranch">Currently checked-out branch, or null when unknown.</param>
/// <param name="ErrorMessage">Last error message, or null when the repository is healthy.</param>
public sealed record RepositoryInfo(
    Guid Id,
    string Name,
    string? RepositoryUrl,
    string? Status,
    string? CurrentBranch,
    string? ErrorMessage);
