using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RocketWelder.SDK.Http.Programs;

internal sealed class ProgramsApi(HttpClient http) : IProgramsApi
{
    // JsonContent.Create defaults to PascalCase (unlike Post/GetAsJsonAsync which use
    // web defaults), so the manually-built edit requests carry these explicitly to
    // stay camelCase-consistent with the rw2 wire shape. WhenWritingNull keeps the
    // move body a clean anchor-XOR-delta and drops the null ref on a Tail anchor.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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

    public async Task<ProgramTreeDto> GetTreeAsync(Guid programId, CancellationToken ct = default)
    {
        var dto = await http.GetFromJsonAsync<ProgramTreeDto>($"api/programs/{programId}/tree", ct).ConfigureAwait(false);
        return dto ?? throw new InvalidOperationException($"Server returned empty body for GET /api/programs/{programId}/tree.");
    }

    public async Task<BlockEditResult> AddBlockAsync(Guid programId, AddBlockRequest request, ProgramEtag etag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var res = await SendEditAsync(HttpMethod.Post, $"api/programs/{programId}/blocks", etag, request, ct).ConfigureAwait(false);
        ThrowIfEditError(res, programId, request.Anchor.Ref, etag);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<BlockEditResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for POST /api/programs/{id}/blocks.");
    }

    public async Task<BlockEditResult> EditBlockAsync(Guid programId, BlockId blockId, EditBlockRequest request, ProgramEtag etag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var res = await SendEditAsync(HttpMethod.Patch, BlockUrl(programId, blockId), etag, request, ct).ConfigureAwait(false);
        ThrowIfEditError(res, programId, blockId, etag);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<BlockEditResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for PATCH /api/programs/{id}/blocks/{blockId}.");
    }

    public async Task<ProgramEtag> RemoveBlockAsync(Guid programId, BlockId blockId, ProgramEtag etag, CancellationToken ct = default)
    {
        using var res = await SendEditAsync(HttpMethod.Delete, BlockUrl(programId, blockId), etag, ct).ConfigureAwait(false);
        ThrowIfEditError(res, programId, blockId, etag);
        res.EnsureSuccessStatusCode();
        return await ReadEtagAsync(res, ct).ConfigureAwait(false);
    }

    public async Task<ProgramEtag> MoveBlockAsync(Guid programId, BlockId blockId, MoveBlockRequest request, ProgramEtag etag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var res = await SendEditAsync(HttpMethod.Post, $"{BlockUrl(programId, blockId)}/move", etag, request, ct).ConfigureAwait(false);
        ThrowIfEditError(res, programId, blockId, etag);
        res.EnsureSuccessStatusCode();
        return await ReadEtagAsync(res, ct).ConfigureAwait(false);
    }

    public async Task<CapturePointResult> CapturePointAsync(Guid programId, CapturePointRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var res = await http.PostAsJsonAsync($"api/programs/{programId}/capture", request, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<CapturePointResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for POST /api/programs/{id}/capture.");
    }

    private static async Task<ProgramEtag> ReadEtagAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var body = await res.Content.ReadFromJsonAsync<EtagResponse>(ct).ConfigureAwait(false);
        return body?.Etag ?? throw new InvalidOperationException("Server returned empty body for an edit that returns an etag.");
    }

    private static string BlockUrl(Guid programId, BlockId blockId)
        => $"api/programs/{programId}/blocks/{Uri.EscapeDataString(blockId)}";

    private async Task<HttpResponseMessage> SendEditAsync<TBody>(HttpMethod method, string url, ProgramEtag etag, TBody body, CancellationToken ct)
        where TBody : class
    {
        using var req = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: Json) };
        req.Headers.TryAddWithoutValidation("If-Match", etag.ToString());
        return await http.SendAsync(req, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendEditAsync(HttpMethod method, string url, ProgramEtag etag, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("If-Match", etag.ToString());
        return await http.SendAsync(req, ct).ConfigureAwait(false);
    }

    private static void ThrowIfEditError(HttpResponseMessage res, Guid programId, BlockId? block, ProgramEtag etag)
    {
        switch (res.StatusCode)
        {
            case HttpStatusCode.Conflict:
                throw new ProgramEtagMismatchException(programId, etag);
            case HttpStatusCode.NotFound:
                throw new BlockNotFoundException(programId, block);
        }
    }
}
