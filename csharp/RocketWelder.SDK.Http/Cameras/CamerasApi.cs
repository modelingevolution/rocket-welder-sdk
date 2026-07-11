using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.Cameras;

internal sealed class CamerasApi(HttpClient http) : ICamerasApi
{
    public Task<CameraCalibration?> GetCalibrationAsync(string? name = null, CancellationToken ct = default)
        => http.GetFromJsonAsync<CameraCalibration>(BuildUrl(name, "calibration"), ct);

    public Task<CameraFrame?> GetFrameAsync(string? name = null, CancellationToken ct = default)
        => http.GetFromJsonAsync<CameraFrame>(BuildUrl(name, "frame"), ct);

    // "default" sentinel — same rationale as RobotsApi.BuildUrl.
    private static string BuildUrl(string? name, string segment)
        => $"api/cameras/{Uri.EscapeDataString(string.IsNullOrEmpty(name) ? "default" : name)}/{segment}";
}
