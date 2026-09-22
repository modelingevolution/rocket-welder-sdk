namespace RocketWelder.SDK.Http.Repositories;

/// <summary>
/// Thrown when <see cref="IRepositoriesApi.CreateProgramAsync"/> targets a repository
/// that is unknown or not cloned on the welder's disk (HTTP 404).
/// </summary>
public sealed class RepositoryNotFoundException : Exception
{
    public RepositoryNotFoundException(Guid repositoryId, string? detail = null)
        : base($"Repository '{repositoryId}' was not found (HTTP 404).{Detail(detail)}")
    {
        RepositoryId = repositoryId;
    }

    /// <summary>The repository that could not be resolved.</summary>
    public Guid RepositoryId { get; }

    private static string Detail(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
}
