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

    /// <summary>
    /// <c>DELETE /api/pipeline/{id}</c> — remove a STOPPED pipeline. The server never force-stops
    /// as a side effect: an active pipeline (Running or starting) is refused with HTTP 409. To delete
    /// a running pipeline, call <see cref="StopAsync"/> first and wait until <see cref="GetAsync"/>
    /// reports <see cref="PipelineState.Stopped"/>, then delete.
    /// </summary>
    /// <exception cref="HttpRequestException">The pipeline is active (HTTP 409), the id was rejected (HTTP 400) or unknown (HTTP 404), or the server failed otherwise.</exception>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
