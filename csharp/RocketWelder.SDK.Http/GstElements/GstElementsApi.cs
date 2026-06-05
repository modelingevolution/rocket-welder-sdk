using System.Net;
using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.GstElements;

internal sealed class GstElementsApi(HttpClient http) : IGstElementsApi
{
    public async Task<IReadOnlyList<GstElementSummary>> ListElementsAsync(CancellationToken ct = default)
    {
        var list = await http.GetFromJsonAsync<GstElementSummary[]>("api/gst/elements", ct).ConfigureAwait(false);
        return list ?? Array.Empty<GstElementSummary>();
    }

    public async Task<GstElementHelp?> GetElementHelpAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        // The server answers 404 for an unknown element name; map that to null per
        // the interface contract rather than letting GetFromJsonAsync throw. Other
        // non-success statuses are still surfaced via EnsureSuccessStatusCode.
        using var res = await http.GetAsync($"api/gst/elements/{Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound)
            return null;

        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<GstElementHelp>(ct).ConfigureAwait(false);
    }
}
