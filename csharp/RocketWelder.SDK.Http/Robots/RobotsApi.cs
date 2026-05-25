using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.Robots;

internal sealed class RobotsApi(HttpClient http) : IRobotsApi
{
    public Task<RobotPose?> GetPoseAsync(string? name = null, CancellationToken ct = default)
        => http.GetFromJsonAsync<RobotPose>(BuildUrl(name, "pose"), ct);

    public Task<RobotJoints?> GetJointsAsync(string? name = null, CancellationToken ct = default)
        => http.GetFromJsonAsync<RobotJoints>(BuildUrl(name, "joints"), ct);

    public async Task<IReadOnlyList<TeachingPointInfo>> GetTeachingPointsAsync(string? name = null, CancellationToken ct = default)
    {
        var list = await http.GetFromJsonAsync<TeachingPointInfo[]>(BuildUrl(name, "teaching-points"), ct).ConfigureAwait(false);
        return list ?? Array.Empty<TeachingPointInfo>();
    }

    // "default" is the sentinel for the unnamed/single robot — server resolves
    // it to whichever IRobot is registered without a name. Removes the
    // ASP.NET route-template ambiguity that {name?} introduces.
    private static string BuildUrl(string? name, string segment)
        => $"api/robots/{Uri.EscapeDataString(string.IsNullOrEmpty(name) ? "default" : name)}/{segment}";
}
