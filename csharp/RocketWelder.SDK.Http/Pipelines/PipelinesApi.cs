using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.Pipelines;

internal sealed class PipelinesApi(HttpClient http) : IPipelinesApi
{
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
