using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.DistanceSensors;

internal sealed class DistanceSensorsApi(HttpClient http) : IDistanceSensorsApi
{
    public Task<DistanceReading?> ReadAsync(string? name = null, CancellationToken ct = default)
        => http.GetFromJsonAsync<DistanceReading>(
            // "default" sentinel — same rationale as RobotsApi.BuildUrl.
            $"api/distance-sensors/{Uri.EscapeDataString(string.IsNullOrEmpty(name) ? "default" : name)}/reading",
            ct);
}
