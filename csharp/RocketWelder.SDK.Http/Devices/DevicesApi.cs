using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.Devices;

internal sealed class DevicesApi(HttpClient http) : IDevicesApi
{
    public async Task<IReadOnlyList<DeviceInfo>> ListAsync(CancellationToken ct = default)
    {
        var list = await http.GetFromJsonAsync<DeviceInfo[]>("api/devices", ct).ConfigureAwait(false);
        return list ?? Array.Empty<DeviceInfo>();
    }
}
