using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.Devices;

internal sealed class DevicesApi(HttpClient http) : IDevicesApi
{
    public async Task<IReadOnlyList<DeviceInfo>> ListAsync(CancellationToken ct = default)
    {
        var list = await http.GetFromJsonAsync<DeviceInfo[]>("api/devices", ct).ConfigureAwait(false);
        return list ?? Array.Empty<DeviceInfo>();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        using var res = await http.DeleteAsync($"api/devices/{id}", ct).ConfigureAwait(false);
        await EnsureSuccessAsync(res, $"Delete device '{id}'", ct).ConfigureAwait(false);
    }

    // Preserve the server's descriptive body (e.g. "No device with id '...'") in a typed
    // HttpRequestException carrying the status, rather than discarding it through
    // EnsureSuccessStatusCode.
    private static async Task EnsureSuccessAsync(HttpResponseMessage res, string action, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode)
            return;
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" {body}";
        throw new HttpRequestException(
            $"{action} failed: HTTP {(int)res.StatusCode} ({res.StatusCode}).{detail}",
            inner: null,
            statusCode: res.StatusCode);
    }
}
