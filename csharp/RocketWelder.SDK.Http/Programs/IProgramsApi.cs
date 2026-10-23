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
    /// <c>DELETE /api/programs/{programId}</c> — deregister a program and remove its
    /// on-disk directory. Refused with 409 while the program is running.
    /// </summary>
    /// <exception cref="ProgramRunningException">The program is running and cannot be deleted (HTTP 409).</exception>
    /// <exception cref="HttpRequestException">The id was rejected (HTTP 400) or unknown (HTTP 404), or the server failed otherwise.</exception>
    Task DeleteAsync(Guid programId, CancellationToken ct = default);

    /// <summary>
    /// <c>DELETE /api/programs/{programId}</c> with <c>If-Match: <paramref name="etag"/></c> (epic-128 feature-001
    /// design.md §3, ADR-10) — delete the program only if it has not changed since <paramref name="etag"/> was read. The
    /// repository records one <c>Delete program</c> commit.
    /// </summary>
    /// <exception cref="ProgramEtagMismatchException">The etag is stale; nothing was deleted (HTTP 409 whose body carries <c>etag</c>).</exception>
    /// <exception cref="ProgramRunningException">The program is running and cannot be deleted (any other HTTP 409).</exception>
    /// <exception cref="HttpRequestException">The id was rejected (HTTP 400) or unknown (HTTP 404), or the server failed otherwise.</exception>
    Task DeleteAsync(Guid programId, ProgramEtag etag, CancellationToken ct = default);

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

    // --- Program authoring (epic-035): BlockId-keyed, etag-guarded edits over the
    //     TeachV2 block tree. Read the tree, then carry its etag into each edit. ---

    /// <summary>
    /// <c>GET /api/programs/{id}/tree</c> — the program's ordered block tree plus the
    /// current etag. Read-only; the contract every edit is written against.
    /// </summary>
    Task<ProgramTreeDto> GetTreeAsync(Guid programId, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /api/programs/{id}/blocks</c> — insert a block at a position relative to
    /// an existing block or at the tail. Sends <c>If-Match: <paramref name="etag"/></c>.
    /// </summary>
    /// <exception cref="ProgramEtagMismatchException">The etag is stale (HTTP 409).</exception>
    /// <exception cref="BlockNotFoundException">The anchor reference block is unknown (HTTP 404).</exception>
    Task<BlockEditResult> AddBlockAsync(Guid programId, AddBlockRequest request, ProgramEtag etag, CancellationToken ct = default);

    /// <summary>
    /// <c>PATCH /api/programs/{id}/blocks/{blockId}</c> — replace a block's properties,
    /// preserving its id. Sends <c>If-Match: <paramref name="etag"/></c>.
    /// </summary>
    /// <exception cref="ProgramEtagMismatchException">The etag is stale (HTTP 409).</exception>
    /// <exception cref="BlockNotFoundException">The block id is unknown (HTTP 404).</exception>
    Task<BlockEditResult> EditBlockAsync(Guid programId, BlockId blockId, EditBlockRequest request, ProgramEtag etag, CancellationToken ct = default);

    /// <summary>
    /// <c>DELETE /api/programs/{id}/blocks/{blockId}</c> — remove a block (a point
    /// removal also drops adaptation claims on its name). Sends
    /// <c>If-Match: <paramref name="etag"/></c>. Returns the tree's new etag and any history warning.
    /// </summary>
    /// <exception cref="ProgramEtagMismatchException">The etag is stale (HTTP 409).</exception>
    /// <exception cref="BlockNotFoundException">The block id is unknown (HTTP 404).</exception>
    Task<EtagEditResult> RemoveBlockAsync(Guid programId, BlockId blockId, ProgramEtag etag, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /api/programs/{id}/blocks/{blockId}/move</c> — reposition a block by
    /// anchor or signed delta. Sends <c>If-Match: <paramref name="etag"/></c>. Returns
    /// the tree's new etag and any history warning.
    /// </summary>
    /// <exception cref="ProgramEtagMismatchException">The etag is stale (HTTP 409).</exception>
    /// <exception cref="BlockNotFoundException">The block id (or anchor reference) is unknown (HTTP 404).</exception>
    Task<EtagEditResult> MoveBlockAsync(Guid programId, BlockId blockId, MoveBlockRequest request, ProgramEtag etag, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /api/programs/{id}/capture</c> (FR-6) — capture rocket-welder2's own
    /// <c>IRobot</c> pose into the program's taught-points store, so a subsequently
    /// added point block can bake it. Not a tree edit, so it carries no etag.
    /// </summary>
    Task<CapturePointResult> CapturePointAsync(Guid programId, CapturePointRequest request, CancellationToken ct = default);

    // --- Values (epic-029 feature-011, design.md §12.2–§12.3): bands as canonical "accept:review" text in mm. ---

    /// <summary>
    /// <c>GET /api/programs/{id}/blocks/{blockId}/values</c> — the block's value keys: what is stored on the block and
    /// what the run resolves, with the layer it comes from.
    /// </summary>
    /// <exception cref="BlockNotFoundException">The program or block is unknown (HTTP 404).</exception>
    Task<BlockValues> GetBlockValuesAsync(Guid programId, BlockId blockId, CancellationToken ct = default);

    /// <summary>
    /// <c>PUT /api/programs/{id}/blocks/{blockId}/values/{key}</c> — store a value on the block. No recompile, no etag
    /// (last writer wins).
    /// </summary>
    /// <exception cref="ValueRefusedException">The value does not parse (HTTP 400); <see cref="ValueRefusedException.Reason"/> is rw2's sentence.</exception>
    /// <exception cref="BlockNotFoundException">The program or block is unknown, or the block has no such key (HTTP 404).</exception>
    Task<BlockValues> SetBlockValueAsync(Guid programId, BlockId blockId, string key, string value, CancellationToken ct = default);

    /// <summary>
    /// <c>DELETE /api/programs/{id}/blocks/{blockId}/values/{key}</c> — clear the block's value; the key inherits again.
    /// </summary>
    /// <exception cref="BlockNotFoundException">The program or block is unknown, or the block has no such key (HTTP 404).</exception>
    Task<BlockValues> ClearBlockValueAsync(Guid programId, BlockId blockId, string key, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /api/programs/{id}/defaults</c> — the program's default values, their resolution and
    /// the program's etag.
    /// </summary>
    /// <exception cref="HttpRequestException">The program is unknown (HTTP 404), or the server failed otherwise.</exception>
    Task<ProgramDefaults> GetProgramDefaultsAsync(Guid programId, CancellationToken ct = default);

    /// <summary>
    /// <c>PUT /api/programs/{id}/defaults/{key}</c> — store a program default. Recompiles the program; sends
    /// <c>If-Match: <paramref name="etag"/></c> and returns the new etag.
    /// </summary>
    /// <exception cref="ValueRefusedException">The value does not parse (HTTP 400).</exception>
    /// <exception cref="ProgramEtagMismatchException">The etag is stale (HTTP 409).</exception>
    /// <exception cref="HttpRequestException">The program or key is unknown (HTTP 404), or the server failed otherwise.</exception>
    Task<ProgramDefaults> SetProgramDefaultAsync(Guid programId, string key, string value, ProgramEtag etag, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /api/programs/{id}/defaults/reset</c> — copy the station's current Programming defaults into the
    /// program, replacing every program default (no program-level DELETE: the program layer holds values). Recompiles
    /// the program; sends <c>If-Match: <paramref name="etag"/></c> and returns the new etag.
    /// </summary>
    /// <exception cref="ProgramEtagMismatchException">The etag is stale (HTTP 409).</exception>
    /// <exception cref="HttpRequestException">The If-Match is missing (HTTP 428), the program is unknown (HTTP 404), or the server failed otherwise.</exception>
    Task<ProgramDefaults> ResetProgramDefaultsAsync(Guid programId, ProgramEtag etag, CancellationToken ct = default);

    // --- Points (epic-128 feature-001, design.md §1.1): read and write taught points; writes are etag-guarded edits. ---

    /// <summary><c>GET /api/programs/{id}/points</c> — every taught point of the program.</summary>
    /// <exception cref="HttpRequestException">The program is unknown (HTTP 404, rw2's sentence in the message), or the server failed otherwise.</exception>
    Task<IReadOnlyList<ProgramPoint>> GetPointsAsync(Guid programId, CancellationToken ct = default);

    /// <summary><c>GET /api/programs/{id}/points/{name}</c> — one taught point.</summary>
    /// <exception cref="HttpRequestException">The program or the point is unknown (HTTP 404, rw2's sentence in the message), or the server failed otherwise.</exception>
    Task<ProgramPoint> GetPointAsync(Guid programId, string name, CancellationToken ct = default);

    /// <summary>
    /// <c>PUT /api/programs/{id}/points/{name}</c> — write one declared point; members of <paramref name="point"/> left
    /// <see langword="null"/> keep their stored value (its <see cref="PointWrite.Name"/> is ignored). One commit. Sends
    /// <c>If-Match: <paramref name="etag"/></c>.
    /// </summary>
    /// <exception cref="ValueRefusedException">A malformed field (HTTP 400), or an undeclared name or a first write without a pose (HTTP 422); <see cref="ValueRefusedException.Reason"/> is rw2's sentence.</exception>
    /// <exception cref="ProgramEtagMismatchException">The etag is stale (HTTP 409).</exception>
    /// <exception cref="HttpRequestException">The program is unknown (HTTP 404), or the server failed otherwise.</exception>
    Task<PointEditResult> PutPointAsync(Guid programId, string name, PointWrite point, ProgramEtag etag, CancellationToken ct = default);

    /// <summary>
    /// <c>PUT /api/programs/{id}/points</c> — write up to 200 declared points in one call and one commit; all or nothing
    /// (ADR-2). Sends <c>If-Match: <paramref name="etag"/></c>.
    /// </summary>
    /// <exception cref="PointsRefusedException">One or more items were refused; nothing was written (HTTP 422).</exception>
    /// <exception cref="ValueRefusedException">The body was refused whole — empty, or more than 200 items (HTTP 400).</exception>
    /// <exception cref="ProgramEtagMismatchException">The etag is stale (HTTP 409).</exception>
    /// <exception cref="HttpRequestException">The program is unknown (HTTP 404), or the server failed otherwise.</exception>
    Task<PointsEditResult> PutPointsAsync(Guid programId, IReadOnlyList<PointWrite> points, ProgramEtag etag, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /api/programs/{id}/blocks/{blockId}/calculate-start</c> — compute a Circle block's start point as the
    /// editor's button does and write it as <c>circle_&lt;blockId&gt;_start</c>. One commit. Sends
    /// <c>If-Match: <paramref name="etag"/></c>.
    /// </summary>
    /// <exception cref="ValueRefusedException">Not a Circle block (HTTP 400), or the ring cannot be planned (HTTP 422).</exception>
    /// <exception cref="BlockNotFoundException">The program or block is unknown (HTTP 404).</exception>
    /// <exception cref="ProgramEtagMismatchException">The etag is stale (HTTP 409).</exception>
    Task<RingStartResult> CalculateRingStartAsync(Guid programId, BlockId ringBlockId, ProgramEtag etag, CancellationToken ct = default);

    // --- Run progress (epic-128 feature-001, design.md §2.6–§2.7). ---

    /// <summary>
    /// <c>GET /api/programs/{id}/runs/{runId}/progress</c> — the run's state, times, arc-on seconds and per-block progress
    /// in program order.
    /// </summary>
    /// <exception cref="HttpRequestException">The program is unknown, or the run is not its latest run (HTTP 404, rw2's sentence in the message), or the server failed otherwise.</exception>
    Task<RunProgress> GetRunProgressAsync(Guid programId, Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Waits for a run to end (ADR-9): polls <see cref="GetRunProgressAsync"/> every second and returns the first answer
    /// whose <see cref="RunProgress.State"/> is not <see cref="ProgramExecutionState.Running"/>, or the last answer when
    /// <paramref name="timeout"/> passes. A returned <c>Running</c> state means "still running"; the run continues.
    /// </summary>
    /// <param name="timeout">1 to 600 seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is outside 1–600 s; no request was sent.</exception>
    /// <exception cref="HttpRequestException">As <see cref="GetRunProgressAsync"/>.</exception>
    Task<RunProgress> WaitForRunAsync(Guid programId, Guid runId, TimeSpan timeout, CancellationToken ct = default);

    // --- History (epic-128 feature-001, design.md §3). ---

    /// <summary>
    /// <c>GET /api/programs/{id}/history[?limit=n]</c> — the commits that changed the program's folder, newest first.
    /// </summary>
    /// <param name="limit">1–1000; <see langword="null"/> takes rw2's default.</param>
    /// <exception cref="HttpRequestException">The id or limit was rejected (HTTP 400), or the program is unknown or deleted (HTTP 404), or the server failed otherwise.</exception>
    Task<HistoryLog> GetHistoryAsync(Guid programId, int? limit = null, CancellationToken ct = default);
}
