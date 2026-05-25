namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of <c>POST /api/programs/{id}/run</c>. Returns when the
/// <c>RunProgram</c> command has been accepted by the server; the
/// program then runs asynchronously and progress is observed via
/// <see cref="IProgramsApi.GetStatusAsync"/>.
/// </summary>
/// <param name="RunId">Identifier for this execution; correlates with <c>ProgramExecutionStarted</c> on the server stream.</param>
/// <param name="DryRun">Echoes back what was requested so the caller can confirm.</param>
public sealed record RunResult(
    Guid RunId,
    bool DryRun);
