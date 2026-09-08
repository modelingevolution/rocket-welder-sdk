using System.Net;
using System.Net.Http.Headers;
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
        await ThrowIfEditErrorAsync(res, programId, request.Anchor.Ref, etag, $"Add block to program '{programId}'", ct).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<BlockEditResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for POST /api/programs/{id}/blocks.");
    }

    public async Task<BlockEditResult> EditBlockAsync(Guid programId, BlockId blockId, EditBlockRequest request, ProgramEtag etag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var res = await SendEditAsync(HttpMethod.Patch, BlockUrl(programId, blockId), etag, request, ct).ConfigureAwait(false);
        await ThrowIfEditErrorAsync(res, programId, blockId, etag, $"Edit block '{blockId}' in program '{programId}'", ct).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<BlockEditResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for PATCH /api/programs/{id}/blocks/{blockId}.");
    }

    public async Task<ProgramEtag> RemoveBlockAsync(Guid programId, BlockId blockId, ProgramEtag etag, CancellationToken ct = default)
    {
        using var res = await SendEditAsync(HttpMethod.Delete, BlockUrl(programId, blockId), etag, ct).ConfigureAwait(false);
        await ThrowIfEditErrorAsync(res, programId, blockId, etag, $"Remove block '{blockId}' from program '{programId}'", ct).ConfigureAwait(false);
        return await ReadEtagAsync(res, ct).ConfigureAwait(false);
    }

    public async Task<ProgramEtag> MoveBlockAsync(Guid programId, BlockId blockId, MoveBlockRequest request, ProgramEtag etag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var res = await SendEditAsync(HttpMethod.Post, $"{BlockUrl(programId, blockId)}/move", etag, request, ct).ConfigureAwait(false);
        // A move carrying an anchor ref has two possible unknown ids (the moved block
        // or the ref); the 404 alone can't say which, so don't misattribute it to the
        // moved block — leave it unnamed. A delta/tail move can only be the moved block.
        var unresolved = request.Anchor?.Ref is null ? blockId : (BlockId?)null;
        await ThrowIfEditErrorAsync(res, programId, unresolved, etag, $"Move block '{blockId}' in program '{programId}'", ct).ConfigureAwait(false);
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
        return body?.Etag ?? throw new InvalidOperationException("Server returned no etag for an edit that returns one.");
    }

    // A bare (unquoted) value never parses as an ETag, so rw2's typed
    // EntityTagHeaderValue accessor reads it as absent and the concurrency guard is
    // silently bypassed. Send a spec-compliant quoted strong entity-tag.
    private static EntityTagHeaderValue IfMatch(ProgramEtag etag) => new($"\"{etag}\"");

    private static string BlockUrl(Guid programId, BlockId blockId)
        => $"api/programs/{programId}/blocks/{Uri.EscapeDataString(blockId)}";

    private async Task<HttpResponseMessage> SendEditAsync<TBody>(HttpMethod method, string url, ProgramEtag etag, TBody body, CancellationToken ct)
        where TBody : class
    {
        using var req = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: Json) };
        req.Headers.IfMatch.Add(IfMatch(etag));
        return await http.SendAsync(req, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendEditAsync(HttpMethod method, string url, ProgramEtag etag, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.IfMatch.Add(IfMatch(etag));
        return await http.SendAsync(req, ct).ConfigureAwait(false);
    }

    private static async Task ThrowIfEditErrorAsync(HttpResponseMessage res, Guid programId, BlockId? block, ProgramEtag etag, string action, CancellationToken ct)
    {
        switch (res.StatusCode)
        {
            case HttpStatusCode.Conflict:
                throw new ProgramEtagMismatchException(programId, etag);
            case HttpStatusCode.NotFound:
                throw new BlockNotFoundException(programId, block);
        }
        await EnsureSuccessAsync(res, action, ct).ConfigureAwait(false);
    }

    // Preserve the server's descriptive body (e.g. "point has no taught pose") in a
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
