namespace RocketWelder.SDK.Http.Pipelines;

/// <summary>
/// Wire shape for a single GStreamer pipeline registered on the welder.
/// Backs <c>GET /api/pipelines</c> and <c>GET /api/pipeline/{id}</c>.
/// </summary>
/// <param name="Id">Pipeline identifier (GUID). Matches <c>PipelineId.Value</c> on the server.</param>
/// <param name="Name">Display name as configured by the operator.</param>
/// <param name="State">Coarse lifecycle state — see <see cref="PipelineState"/>.</param>
/// <param name="ProcessId">
/// OS process id of the GStreamer pipeline (<c>gst-launch</c>) process, as a string.
/// Null when the pipeline has no process yet — i.e. <see cref="State"/> is
/// <see cref="PipelineState.Stopped"/>, <see cref="PipelineState.Failed"/>, or
/// <see cref="PipelineState.Starting"/> before the OS process is up.
/// <para>
/// Public field on the contract because consumers (e.g. vector-graphics WebSocket
/// overlays at <c>/ws/graphics/{processId}</c>) compose URLs keyed on this id —
/// it is the runtime identifier the streaming layer uses, not an internal detail.
/// </para>
/// </param>
public sealed record PipelineInfo(
    Guid Id,
    string Name,
    PipelineState State,
    string? ProcessId);
