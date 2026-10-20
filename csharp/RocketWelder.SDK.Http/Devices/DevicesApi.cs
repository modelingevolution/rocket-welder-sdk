using System.Net;
using System.Net.Http.Json;
using RocketWelder.SDK.Http.Programs;

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

    public async Task<DeviceConfig> GetConfigAsync(string id, CancellationToken ct = default)
    {
        using var res = await http.GetAsync($"api/devices/config/{id}", ct).ConfigureAwait(false);
        await EnsureSuccessAsync(res, $"Read configuration of device '{id}'", ct).ConfigureAwait(false);
        return await ReadConfigAsync(res, "GET", ct).ConfigureAwait(false);
    }

    public async Task<DeviceConfig> ConfigureAsync(string id, ConfigureDeviceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var res = await http.PatchAsJsonAsync($"api/devices/config/{id}", request, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.BadRequest)
            throw await ValueRefusedException.FromAsync(res, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(res, $"Configure device '{id}'", ct).ConfigureAwait(false);
        return await ReadConfigAsync(res, "PATCH", ct).ConfigureAwait(false);
    }

    private static async Task<DeviceConfig> ReadConfigAsync(HttpResponseMessage res, string verb, CancellationToken ct) =>
        await res.Content.ReadFromJsonAsync<DeviceConfig>(ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Server returned empty body for {verb} /api/devices/config/{{id}}.");

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
