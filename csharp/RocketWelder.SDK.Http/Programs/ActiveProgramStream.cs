namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Wire shape for one currently-active program graphics stream — i.e. an
/// <c>IProgram</c> that has emitted at least one frame and whose graphics
/// channel a UI can subscribe to. Backs <c>GET /api/programs/active-streams</c>.
/// </summary>
/// <remarks>
/// Programs run in-process with the welder host (no separate OS process), so
/// there is no PID to expose here — consumers route to the program graphics
/// WebSocket via <c>/ws/program-graphics/{ProgramId}</c>. Pipelines have an
/// equivalent need keyed on OS PID; see <see cref="Pipelines.PipelineInfo.ProcessId"/>
/// for that asymmetry.
/// </remarks>
/// <param name="ProgramId">Program identifier (GUID, as string for transport stability).</param>
/// <param name="ProgramName">Display name of the program emitting graphics.</param>
public sealed record ActiveProgramStream(
    string ProgramId,
    string ProgramName);
