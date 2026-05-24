namespace RocketWelder.SDK.Http.Pipelines;

/// <summary>
/// Wire shape for a single GStreamer pipeline registered on the welder.
/// Backs <c>GET /api/pipelines</c> and <c>GET /api/pipeline/{id}</c>.
/// </summary>
/// <param name="Id">Pipeline identifier (GUID). Matches <c>PipelineId.Value</c> on the server.</param>
/// <param name="Name">Display name as configured by the operator.</param>
/// <param name="State">Coarse lifecycle state — see <see cref="PipelineState"/>.</param>
public sealed record PipelineInfo(
    Guid Id,
    string Name,
    PipelineState State);
