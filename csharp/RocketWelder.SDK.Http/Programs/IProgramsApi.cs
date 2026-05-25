namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// <c>/api/programs</c> and <c>/api/repositories/{id}/compile</c> — discover,
/// compile, run, cancel, and query <c>IProgram</c> plugins on the welder.
/// </summary>
public interface IProgramsApi
{
    /// <summary><c>GET /api/programs</c> — every compiled program. Optionally filter by repository.</summary>
    Task<IReadOnlyList<ProgramInfo>> ListAsync(Guid? repositoryId = null, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /api/repositories/{repositoryId}/compile</c> — compile a C# project
    /// containing <c>IProgram</c> plugins. Blocks until compilation settles; the
    /// returned <see cref="CompileResult"/> reports success + the discovered programs
    /// or failure + the diagnostic.
    /// </summary>
    /// <param name="repositoryId">Repository id (from <c>GET /api/repositories</c>).</param>
    /// <param name="csprojPath">Optional .csproj path within the repo; server auto-detects when omitted.</param>
    Task<CompileResult> CompileAsync(Guid repositoryId, string? csprojPath = null, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /api/programs/{programId}/run</c> — request execution. Returns when
    /// the command has been accepted; poll <see cref="GetStatusAsync"/> for progress.
    /// </summary>
    /// <param name="dryRun">When true, <c>IProgram.ExecuteAsync</c> runs with <c>ctx.IsDryRun = true</c> (no physical actuation).</param>
    Task<RunResult> RunAsync(Guid programId, bool dryRun, CancellationToken ct = default);

    /// <summary><c>POST /api/programs/{programId}/cancel</c> — abort a running execution.</summary>
    Task CancelAsync(Guid programId, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /api/programs/{programId}/status</c> — current execution state plus last-run
    /// summary. Null if the program id is unknown.
    /// </summary>
    Task<ProgramStatus?> GetStatusAsync(Guid programId, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /api/programs/active-streams</c> — every program currently emitting
    /// graphics frames (i.e. has produced at least one frame on the stream). A UI
    /// subscribing to program-graphics WebSockets uses this to discover which
    /// channels are live and reconnect after a page refresh. Empty list when no
    /// program is actively emitting.
    /// </summary>
    Task<IReadOnlyList<ActiveProgramStream>> GetActiveStreamsAsync(CancellationToken ct = default);
}
