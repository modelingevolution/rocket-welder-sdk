namespace RocketWelder.SDK.Http.DistanceSensors;

/// <summary>
/// <c>/api/distance-sensors/{name?}/...</c> — read-only access to registered
/// 1-D distance sensors (e.g. ADAM-6017 + Panasonic HL-G212B).
/// </summary>
public interface IDistanceSensorsApi
{
    /// <summary>
    /// <c>GET /api/distance-sensors/{name?}/reading</c> — fresh distance measurement.
    /// The server may transparently reconnect a disconnected sensor before reading
    /// (self-healing pre-flight per the SDK's <c>ReadAsync</c> semantics). Null
    /// when the sensor cannot be reached.
    /// </summary>
    Task<DistanceReading?> ReadAsync(string? name = null, CancellationToken ct = default);
}
