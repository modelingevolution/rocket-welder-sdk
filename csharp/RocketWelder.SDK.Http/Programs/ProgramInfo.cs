namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Wire shape for a compiled <c>IProgram</c> plugin loaded into
/// <c>ProgramService</c>. Backs <c>GET /api/programs</c>.
/// </summary>
/// <param name="Id">Program identifier (GUID).</param>
/// <param name="ProjectId">The project this program was discovered in.</param>
/// <param name="Name">Class name or <c>[Program(Name = ...)]</c> override.</param>
/// <param name="Description">Optional human description from the <c>[Program]</c> attribute.</param>
/// <param name="SupportsDryRun">True if <c>IProgram.ExecuteAsync</c> honours <c>ctx.IsDryRun</c>.</param>
/// <param name="IsReady">False if the program is still being indexed or its assembly failed to load.</param>
/// <param name="RepositoryId">Git repository the source lives in.</param>
public sealed record ProgramInfo(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    bool SupportsDryRun,
    bool IsReady,
    Guid? RepositoryId);
