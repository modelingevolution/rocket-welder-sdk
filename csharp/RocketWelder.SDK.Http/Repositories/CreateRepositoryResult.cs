namespace RocketWelder.SDK.Http.Repositories;

/// <summary>
/// Wire shape of <c>POST /api/repositories</c> (<c>{ "repositoryId": "..." }</c>).
/// Unwrapped to a bare <see cref="Guid"/> by the SDK.
/// </summary>
internal sealed record CreateRepositoryResult(Guid RepositoryId);
