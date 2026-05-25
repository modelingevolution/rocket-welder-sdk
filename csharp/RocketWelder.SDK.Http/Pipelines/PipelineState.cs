namespace RocketWelder.SDK.Http.Pipelines;

/// <summary>
/// Coarse pipeline lifecycle state surfaced over the wire. Server-side GStreamer
/// states (Null, Ready, Paused, Playing) collapse to these for tool/UI clarity.
/// </summary>
public enum PipelineState
{
    Unknown = 0,
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
}
