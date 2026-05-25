using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.Modbus;

internal sealed class ModbusApi(HttpClient http) : IModbusApi
{
    public Task<ModbusTagValue?> ReadTagAsync(string deviceName, string tagName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceName);
        ArgumentException.ThrowIfNullOrEmpty(tagName);
        return http.GetFromJsonAsync<ModbusTagValue>(
            $"api/modbus/{Uri.EscapeDataString(deviceName)}/{Uri.EscapeDataString(tagName)}",
            ct);
    }
}
