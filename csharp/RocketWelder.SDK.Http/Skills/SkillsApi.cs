using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.Skills;

internal sealed class SkillsApi(HttpClient http) : ISkillsApi
{
    public async Task<IReadOnlyList<SkillEntry>> ListAsync(CancellationToken ct = default)
    {
        var list = await http.GetFromJsonAsync<SkillEntry[]>("api/skills", ct).ConfigureAwait(false);
        return list ?? Array.Empty<SkillEntry>();
    }

    public async Task<string> LoadAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        using var res = await http.GetAsync($"api/skills/{Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
