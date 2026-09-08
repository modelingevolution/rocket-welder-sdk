using System.Net;
using System.Net.Http.Json;

namespace RocketWelder.SDK.Http.Repositories;

internal sealed class RepositoriesApi(HttpClient http) : IRepositoriesApi
{
    public async Task<Guid> CreateAsync(string name, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync("api/repositories", new { name }, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.Conflict)
            throw new RepositoryAlreadyExistsException(name, await BodyAsync(res, ct).ConfigureAwait(false));
        await EnsureSuccessAsync(res, $"Create repository '{name}'", ct).ConfigureAwait(false);
        var result = await res.Content.ReadFromJsonAsync<CreateRepositoryResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for POST /api/repositories.");
        return result.RepositoryId;
    }

    public async Task<Guid> CreateProgramAsync(Guid repositoryId, string name, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync($"api/repositories/{repositoryId}/programs", new { name }, ct).ConfigureAwait(false);
        switch (res.StatusCode)
        {
            case HttpStatusCode.NotFound:
                throw new RepositoryNotFoundException(repositoryId, await BodyAsync(res, ct).ConfigureAwait(false));
            case HttpStatusCode.Conflict:
                throw new DuplicateProgramException(repositoryId, name, await BodyAsync(res, ct).ConfigureAwait(false));
        }
        await EnsureSuccessAsync(res, $"Create program '{name}' in repository '{repositoryId}'", ct).ConfigureAwait(false);
        var result = await res.Content.ReadFromJsonAsync<CreateProgramResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for POST /api/repositories/{id}/programs.");
        return result.ProgramId;
    }

    // Preserve the server's descriptive body (e.g. "Invalid repository ID format") in a
    // typed HttpRequestException carrying the status, rather than swallowing it or
    // discarding it through EnsureSuccessStatusCode.
    private static async Task EnsureSuccessAsync(HttpResponseMessage res, string action, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode)
            return;
        var body = await BodyAsync(res, ct).ConfigureAwait(false);
        var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" {body}";
        throw new HttpRequestException(
            $"{action} failed: HTTP {(int)res.StatusCode} ({res.StatusCode}).{detail}",
            inner: null,
            statusCode: res.StatusCode);
    }

    private static async Task<string?> BodyAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }
}
