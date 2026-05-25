namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Coarse program execution state surfaced over the wire.
/// </summary>
public enum ProgramExecutionState
{
    Unknown = 0,
    Idle,
    Running,
    Finished,
    Failed,
    Cancelled,
}
