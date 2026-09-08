namespace RocketWelder.SDK.Http.Repositories;

/// <summary>
/// <c>/api/repositories</c> — headless authoring of program repositories and the
/// programs inside them. The counterpart to <see cref="Programs.IProgramsApi"/>:
/// create a local repository, create a block-editable program in it, then edit and
/// compile through <c>Programs</c>. Everything here stays on the welder's own disk —
/// no git remote is ever touched.
/// </summary>
public interface IRepositoriesApi
{
    /// <summary>
    /// <c>POST /api/repositories</c> (body <c>{ name }</c>) — create a new LOCAL,
    /// git-init'd program repository (no remote, no clone, no commit/push). Returns
    /// the new repository id.
    /// </summary>
    /// <param name="name">Repository name; also its directory under the welder's repository store.</param>
    /// <exception cref="RepositoryAlreadyExistsException">A repository of that name already exists (HTTP 409).</exception>
    /// <exception cref="HttpRequestException">The name was rejected (HTTP 400) or the server failed otherwise.</exception>
    Task<Guid> CreateAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /api/repositories/{repositoryId}/programs</c> (body <c>{ name }</c>) —
    /// create a new block-editable program in an existing repository. Returns the new
    /// program id, ready for the <see cref="Programs.IProgramsApi"/> authoring calls.
    /// </summary>
    /// <param name="repositoryId">The repository to create the program in (from <see cref="CreateAsync"/>).</param>
    /// <param name="name">Program name.</param>
    /// <exception cref="RepositoryNotFoundException">The repository is unknown or not cloned on disk (HTTP 404).</exception>
    /// <exception cref="DuplicateProgramException">A program of that name already exists in the repository (HTTP 409).</exception>
    /// <exception cref="HttpRequestException">The id or name was rejected (HTTP 400/422) or the server failed otherwise.</exception>
    Task<Guid> CreateProgramAsync(Guid repositoryId, string name, CancellationToken ct = default);
}
