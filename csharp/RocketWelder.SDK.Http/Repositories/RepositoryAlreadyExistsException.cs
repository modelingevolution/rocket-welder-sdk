namespace RocketWelder.SDK.Http.Repositories;

/// <summary>
/// Thrown when <see cref="IRepositoriesApi.CreateAsync"/> is refused because a
/// repository of that name already exists (HTTP 409).
/// </summary>
public sealed class RepositoryAlreadyExistsException : Exception
{
    public RepositoryAlreadyExistsException(string name, string? detail = null)
        : base($"A repository named '{name}' already exists (HTTP 409).{Detail(detail)}")
    {
        Name = name;
    }

    /// <summary>The repository name that collided.</summary>
    public string Name { get; }

    private static string Detail(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
}
