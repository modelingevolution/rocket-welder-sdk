namespace RocketWelder.SDK.Http.Pipelines;

/// <summary>
/// <c>/api/pipelines</c> + <c>/api/pipeline/{id}</c> — list, inspect, start, stop
/// GStreamer pipelines on the welder.
/// </summary>
public interface IPipelinesApi
{
    /// <summary><c>GET /api/pipelines</c> — every registered pipeline. Empty list if none.</summary>
    Task<IReadOnlyList<PipelineInfo>> ListAsync(CancellationToken ct = default);

    /// <summary><c>GET /api/pipeline/{id}</c> — null if no pipeline with that id exists.</summary>
    Task<PipelineInfo?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /api/pipeline/{id}/start</c> — request a state transition to
    /// <see cref="PipelineState.Running"/>. Returns when the command has been
    /// accepted; poll <see cref="GetAsync"/> for the resulting state.
    /// </summary>
    Task StartAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /api/pipeline/{id}/stop</c> — request a state transition to
    /// <see cref="PipelineState.Stopped"/>.
    /// </summary>
    Task StopAsync(Guid id, CancellationToken ct = default);
}
