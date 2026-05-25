using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.Programs;

internal sealed class ProgramsApi(HttpClient http) : IProgramsApi
{
    public async Task<IReadOnlyList<ProgramInfo>> ListAsync(Guid? repositoryId = null, CancellationToken ct = default)
    {
        var url = repositoryId is null
            ? "api/programs"
            : $"api/programs?repositoryId={repositoryId.Value}";
        var list = await http.GetFromJsonAsync<ProgramInfo[]>(url, ct).ConfigureAwait(false);
        return list ?? Array.Empty<ProgramInfo>();
    }

    public async Task<CompileResult> CompileAsync(Guid repositoryId, string? csprojPath = null, CancellationToken ct = default)
    {
        // Server's CompileRequest record has property name `ProjectPath`; serialise
        // camelCase so case-insensitive binding lands on the right field.
        var body = new { projectPath = csprojPath };
        using var res = await http.PostAsJsonAsync($"api/repositories/{repositoryId}/compile", body, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<CompileResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for POST /api/repositories/{id}/compile.");
    }

    public async Task<RunResult> RunAsync(Guid programId, bool dryRun, CancellationToken ct = default)
    {
        var body = new { dryRun };
        using var res = await http.PostAsJsonAsync($"api/programs/{programId}/run", body, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<RunResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for POST /api/programs/{id}/run.");
    }

    public async Task CancelAsync(Guid programId, CancellationToken ct = default)
    {
        using var res = await http.PostAsync($"api/programs/{programId}/cancel", content: null, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }

    public Task<ProgramStatus?> GetStatusAsync(Guid programId, CancellationToken ct = default)
        => http.GetFromJsonAsync<ProgramStatus>($"api/programs/{programId}/status", ct);

    public async Task<IReadOnlyList<ActiveProgramStream>> GetActiveStreamsAsync(CancellationToken ct = default)
    {
        var list = await http.GetFromJsonAsync<ActiveProgramStream[]>("api/programs/active-streams", ct).ConfigureAwait(false);
        return list ?? Array.Empty<ActiveProgramStream>();
    }
}
