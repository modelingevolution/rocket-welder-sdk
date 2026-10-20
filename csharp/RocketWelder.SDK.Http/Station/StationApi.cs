using System.Net;
using System.Net.Http.Json;
using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Station;

internal sealed class StationApi(HttpClient http) : IStationApi
{
    private const string Route = "api/station/programming-defaults";

    public async Task<StationDefaults> GetProgrammingDefaultsAsync(CancellationToken ct = default)
    {
        using var res = await http.GetAsync(Route, ct).ConfigureAwait(false);
        await ThrowIfErrorAsync(res, "Read station programming defaults", ct).ConfigureAwait(false);
        return await ReadAsync(res, "GET", ct).ConfigureAwait(false);
    }

    public async Task<StationDefaults> SetProgrammingDefaultAsync(string key, string value, CancellationToken ct = default)
    {
        using var res = await http.PutAsJsonAsync($"{Route}/{Uri.EscapeDataString(key)}", new SetValueRequest(value), ct).ConfigureAwait(false);
        await ThrowIfErrorAsync(res, $"Set station programming default '{key}'", ct).ConfigureAwait(false);
        return await ReadAsync(res, "PUT", ct).ConfigureAwait(false);
    }

    public async Task<StationDefaults> ClearProgrammingDefaultAsync(string key, CancellationToken ct = default)
    {
        using var res = await http.DeleteAsync($"{Route}/{Uri.EscapeDataString(key)}", ct).ConfigureAwait(false);
        await ThrowIfErrorAsync(res, $"Clear station programming default '{key}'", ct).ConfigureAwait(false);
        return await ReadAsync(res, "DELETE", ct).ConfigureAwait(false);
    }

    private static async Task<StationDefaults> ReadAsync(HttpResponseMessage res, string verb, CancellationToken ct) =>
        await res.Content.ReadFromJsonAsync<StationDefaults>(ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Server returned empty body for {verb} /{Route}.");

    // design.md §12.6: 400 → ValueRefusedException with the sentence; any other failure → HttpRequestException with the
    // status and the message (ServerSentence.FailureAsync, shared with ProgramsApi).
    private static async Task ThrowIfErrorAsync(HttpResponseMessage res, string action, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode)
            return;
        if (res.StatusCode == HttpStatusCode.BadRequest)
            throw await ValueRefusedException.FromAsync(res, ct).ConfigureAwait(false);
        throw await ServerSentence.FailureAsync(res, action, ct).ConfigureAwait(false);
    }
}
