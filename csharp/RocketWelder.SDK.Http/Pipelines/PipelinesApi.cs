using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.Pipelines;

internal sealed class PipelinesApi(HttpClient http) : IPipelinesApi
{
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var res = await http.DeleteAsync($"api/pipeline/{id}", ct).ConfigureAwait(false);
        await EnsureSuccessAsync(res, $"Delete pipeline '{id}'", ct).ConfigureAwait(false);
    }

    // Preserve the server's descriptive body (e.g. "Pipeline {id} not found") in a typed
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

    public async Task<IReadOnlyList<PipelineInfo>> ListAsync(CancellationToken ct = default)
    {
        var list = await http.GetFromJsonAsync<PipelineInfo[]>("api/pipelines", ct).ConfigureAwait(false);
        return list ?? Array.Empty<PipelineInfo>();
    }

    public Task<PipelineInfo?> GetAsync(Guid id, CancellationToken ct = default)
        => http.GetFromJsonAsync<PipelineInfo>($"api/pipeline/{id}", ct);

    public async Task StartAsync(Guid id, CancellationToken ct = default)
    {
        // Server registers MapPostCommand<StartPipelineCommand> which binds the
        // command record from [FromBody]; ASP.NET's JSON binder rejects an empty
        // body on a non-nullable type, so post "{}" — the (parameterless) command
        // record deserialises cleanly.
        using var res = await http.PostAsJsonAsync($"api/pipeline/{id}/start", new { }, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }

    public async Task StopAsync(Guid id, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync($"api/pipeline/{id}/stop", new { }, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }
}
