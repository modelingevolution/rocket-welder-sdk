namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Snapshot of a program's execution state. Backs <c>GET /api/programs/{id}/status</c>.
/// </summary>
/// <param name="ProgramId">The program this status refers to.</param>
/// <param name="State">Coarse execution state — see <see cref="ProgramExecutionState"/>.</param>
/// <param name="CurrentRunId">Null when <see cref="State"/> is <see cref="ProgramExecutionState.Idle"/>.</param>
/// <param name="LastRunFinishedAtUtc">When the last execution settled (Finished/Failed/Cancelled), UTC.</param>
/// <param name="LastError">Last failure diagnostic, if any.</param>
public sealed record ProgramStatus(
    Guid ProgramId,
    ProgramExecutionState State,
    Guid? CurrentRunId,
    DateTime? LastRunFinishedAtUtc,
    string? LastError);
