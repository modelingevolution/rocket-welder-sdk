namespace RocketWelder.SDK.Http.Repositories;

/// <summary>
/// Thrown when <see cref="IRepositoriesApi.CreateProgramAsync"/> is refused because a
/// program of that name already exists in the repository (HTTP 409). Distinct from
/// <see cref="RepositoryNotFoundException"/> (unknown repo, 404).
/// </summary>
public sealed class DuplicateProgramException : Exception
{
    public DuplicateProgramException(Guid repositoryId, string name, string? detail = null)
        : base($"A program named '{name}' already exists in repository '{repositoryId}' (HTTP 409).{Detail(detail)}")
    {
        RepositoryId = repositoryId;
        Name = name;
    }

    /// <summary>The repository the program was being created in.</summary>
    public Guid RepositoryId { get; }

    /// <summary>The program name that collided.</summary>
    public string Name { get; }

    private static string Detail(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
}
