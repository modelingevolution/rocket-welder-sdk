using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RocketWelder.SDK.Http.Programs;

// clock: the wait's poll delay and deadline (ADR-9); TimeProvider.System unless a test supplies one.
internal sealed class ProgramsApi(HttpClient http, TimeProvider? clock = null) : IProgramsApi
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

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
        await EnsureSuccessAsync(res, $"Run program '{programId}'", ct).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<RunResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for POST /api/programs/{id}/run.");
    }

    public async Task CancelAsync(Guid programId, CancellationToken ct = default)
    {
        using var res = await http.PostAsync($"api/programs/{programId}/cancel", content: null, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid programId, CancellationToken ct = default)
    {
        using var res = await http.DeleteAsync($"api/programs/{programId}", ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.Conflict)
            throw new ProgramRunningException(programId, await BodyAsync(res, ct).ConfigureAwait(false));
        await EnsureSuccessAsync(res, $"Delete program '{programId}'", ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid programId, ProgramEtag etag, CancellationToken ct = default)
    {
        using var res = await SendEditAsync(HttpMethod.Delete, $"api/programs/{programId}", etag, ct).ConfigureAwait(false);
        // design.md §3 / ADR-10: a stale etag answers 409 {error, etag}; a running program answers 409 with the
        // ProgramFault body, which has no etag property.
        if (res.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await BodyAsync(res, ct).ConfigureAwait(false);
            if (HasEtagProperty(body))
                throw new ProgramEtagMismatchException(programId, etag);
            throw new ProgramRunningException(programId, body);
        }
        await EnsureSuccessAsync(res, $"Delete program '{programId}'", ct).ConfigureAwait(false);
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

    public async Task<EtagEditResult> RemoveBlockAsync(Guid programId, BlockId blockId, ProgramEtag etag, CancellationToken ct = default)
    {
        using var res = await SendEditAsync(HttpMethod.Delete, BlockUrl(programId, blockId), etag, ct).ConfigureAwait(false);
        await ThrowIfEditErrorAsync(res, programId, blockId, etag, $"Remove block '{blockId}' from program '{programId}'", ct).ConfigureAwait(false);
        return await ReadEtagAsync(res, ct).ConfigureAwait(false);
    }

    public async Task<EtagEditResult> MoveBlockAsync(Guid programId, BlockId blockId, MoveBlockRequest request, ProgramEtag etag, CancellationToken ct = default)
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
        await EnsureSuccessAsync(res, $"Capture point in program '{programId}'", ct).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<CapturePointResult>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned empty body for POST /api/programs/{id}/capture.");
    }

    public async Task<BlockValues> GetBlockValuesAsync(Guid programId, BlockId blockId, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(BlockValuesUrl(programId, blockId), ct).ConfigureAwait(false);
        await ThrowIfValueErrorAsync(res, programId, blockId, etag: null, $"Read values of block '{blockId}' in program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<BlockValues>(res, "GET /api/programs/{id}/blocks/{blockId}/values", ct).ConfigureAwait(false);
    }

    public async Task<BlockValues> SetBlockValueAsync(Guid programId, BlockId blockId, string key, string value, CancellationToken ct = default)
    {
        using var res = await http.PutAsJsonAsync($"{BlockValuesUrl(programId, blockId)}/{Uri.EscapeDataString(key)}", new SetValueRequest(value), Json, ct).ConfigureAwait(false);
        await ThrowIfValueErrorAsync(res, programId, blockId, etag: null, $"Set value '{key}' of block '{blockId}' in program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<BlockValues>(res, "PUT /api/programs/{id}/blocks/{blockId}/values/{key}", ct).ConfigureAwait(false);
    }

    public async Task<BlockValues> ClearBlockValueAsync(Guid programId, BlockId blockId, string key, CancellationToken ct = default)
    {
        using var res = await http.DeleteAsync($"{BlockValuesUrl(programId, blockId)}/{Uri.EscapeDataString(key)}", ct).ConfigureAwait(false);
        await ThrowIfValueErrorAsync(res, programId, blockId, etag: null, $"Clear value '{key}' of block '{blockId}' in program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<BlockValues>(res, "DELETE /api/programs/{id}/blocks/{blockId}/values/{key}", ct).ConfigureAwait(false);
    }

    public async Task<ProgramDefaults> GetProgramDefaultsAsync(Guid programId, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(DefaultsUrl(programId), ct).ConfigureAwait(false);
        await ThrowIfValueErrorAsync(res, programId, block: null, etag: null, $"Read defaults of program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<ProgramDefaults>(res, "GET /api/programs/{id}/defaults", ct).ConfigureAwait(false);
    }

    public async Task<ProgramDefaults> SetProgramDefaultAsync(Guid programId, string key, string value, ProgramEtag etag, CancellationToken ct = default)
    {
        using var res = await SendEditAsync(HttpMethod.Put, $"{DefaultsUrl(programId)}/{Uri.EscapeDataString(key)}", etag, new SetValueRequest(value), ct).ConfigureAwait(false);
        await ThrowIfValueErrorAsync(res, programId, block: null, etag, $"Set default '{key}' of program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<ProgramDefaults>(res, "PUT /api/programs/{id}/defaults/{key}", ct).ConfigureAwait(false);
    }

    public async Task<ProgramDefaults> ResetProgramDefaultsAsync(Guid programId, ProgramEtag etag, CancellationToken ct = default)
    {
        using var res = await SendEditAsync(HttpMethod.Post, $"{DefaultsUrl(programId)}/reset", etag, ct).ConfigureAwait(false);
        await ThrowIfValueErrorAsync(res, programId, block: null, etag, $"Reset defaults of program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<ProgramDefaults>(res, "POST /api/programs/{id}/defaults/reset", ct).ConfigureAwait(false);
    }

    // --- Points (design.md §1.1) ---

    public async Task<IReadOnlyList<ProgramPoint>> GetPointsAsync(Guid programId, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(PointsUrl(programId), ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw await ServerSentence.FailureAsync(res, $"Read points of program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<ProgramPoint[]>(res, "GET /api/programs/{id}/points", ct).ConfigureAwait(false);
    }

    public async Task<ProgramPoint> GetPointAsync(Guid programId, string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        using var res = await http.GetAsync(PointUrl(programId, name), ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw await ServerSentence.FailureAsync(res, $"Read point '{name}' of program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<ProgramPoint>(res, "GET /api/programs/{id}/points/{name}", ct).ConfigureAwait(false);
    }

    public async Task<PointEditResult> PutPointAsync(Guid programId, string name, PointWrite point, ProgramEtag etag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(point);
        using var res = await SendEditAsync(HttpMethod.Put, PointUrl(programId, name), etag, point, ct).ConfigureAwait(false);
        await ThrowIfPointErrorAsync(res, programId, etag, $"Write point '{name}' of program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<PointEditResult>(res, "PUT /api/programs/{id}/points/{name}", ct).ConfigureAwait(false);
    }

    public async Task<PointsEditResult> PutPointsAsync(Guid programId, IReadOnlyList<PointWrite> points, ProgramEtag etag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        using var res = await SendEditAsync(HttpMethod.Put, PointsUrl(programId), etag, points, ct).ConfigureAwait(false);
        // ADR-2: a 422 lists every refused item; nothing was written.
        if (res.StatusCode == HttpStatusCode.UnprocessableEntity && await RefusedAsync(res, ct).ConfigureAwait(false) is { } refused)
            throw new PointsRefusedException(refused);
        await ThrowIfPointErrorAsync(res, programId, etag, $"Write {points.Count} points of program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<PointsEditResult>(res, "PUT /api/programs/{id}/points", ct).ConfigureAwait(false);
    }

    public async Task<RingStartResult> CalculateRingStartAsync(Guid programId, BlockId ringBlockId, ProgramEtag etag, CancellationToken ct = default)
    {
        using var res = await SendEditAsync(HttpMethod.Post, $"{BlockUrl(programId, ringBlockId)}/calculate-start", etag, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            var message = await ServerSentence.MessageAsync(res, ct).ConfigureAwait(false);
            throw message is null
                ? new BlockNotFoundException(programId, ringBlockId)
                : new BlockNotFoundException(programId, ringBlockId, message);
        }
        await ThrowIfPointErrorAsync(res, programId, etag, $"Calculate the start of ring '{ringBlockId}' in program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<RingStartResult>(res, "POST /api/programs/{id}/blocks/{blockId}/calculate-start", ct).ConfigureAwait(false);
    }

    // --- Run progress (design.md §2.6–§2.7) ---

    /// <summary>The wait's poll period (ADR-9).</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>The shortest and longest wait (FR-4.4).</summary>
    internal static readonly TimeSpan MinWait = TimeSpan.FromSeconds(1), MaxWait = TimeSpan.FromSeconds(600);

    public async Task<RunProgress> GetRunProgressAsync(Guid programId, Guid runId, CancellationToken ct = default)
    {
        using var res = await http.GetAsync($"api/programs/{programId}/runs/{runId}/progress", ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw await ServerSentence.FailureAsync(res, $"Read progress of run '{runId}' of program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<RunProgress>(res, "GET /api/programs/{id}/runs/{runId}/progress", ct).ConfigureAwait(false);
    }

    public async Task<RunProgress> WaitForRunAsync(Guid programId, Guid runId, TimeSpan timeout, CancellationToken ct = default)
    {
        if (timeout < MinWait || timeout > MaxWait)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                $"The wait must be between {MinWait.TotalSeconds:0} and {MaxWait.TotalSeconds:0} seconds.");

        // ADR-9: a client-side poll on the injected clock. Every poll after the first is cut to the time left, so the
        // wait never overruns its timeout by a slow request; a cut poll returns the last answer (design.md §2.7).
        var deadline = _clock.GetUtcNow() + timeout;
        var last = await GetRunProgressAsync(programId, runId, ct).ConfigureAwait(false);
        while (true)
        {
            if (last.State != ProgramExecutionState.Running)
                return last;
            var remaining = deadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                return last;
            await Task.Delay(remaining < PollInterval ? remaining : PollInterval, _clock, ct).ConfigureAwait(false);

            remaining = deadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                return last;
            using var bound = new CancellationTokenSource(remaining, _clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, bound.Token);
            try
            {
                last = await GetRunProgressAsync(programId, runId, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (bound.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return last;
            }
        }
    }

    // --- History (design.md §3) ---

    public async Task<HistoryLog> GetHistoryAsync(Guid programId, int? limit = null, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(HistoryUrl($"api/programs/{programId}/history", limit), ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw await ServerSentence.FailureAsync(res, $"Read history of program '{programId}'", ct).ConfigureAwait(false);
        return await ReadAsync<HistoryLog>(res, "GET /api/programs/{id}/history", ct).ConfigureAwait(false);
    }

    internal static string HistoryUrl(string route, int? limit) =>
        limit is { } n ? $"{route}?limit={n}" : route;

    private static string PointsUrl(Guid programId) => $"api/programs/{programId}/points";

    private static string PointUrl(Guid programId, string name) => $"{PointsUrl(programId)}/{Uri.EscapeDataString(name)}";

    // design.md §1.1: 400/422 → rw2's refusal sentence; 409 → stale etag; any other failure → HttpRequestException with
    // the status and the sentence.
    private static async Task ThrowIfPointErrorAsync(HttpResponseMessage res, Guid programId, ProgramEtag etag, string action, CancellationToken ct)
    {
        switch (res.StatusCode)
        {
            case HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity:
                throw await ValueRefusedException.FromAsync(res, ct).ConfigureAwait(false);
            case HttpStatusCode.Conflict:
                throw new ProgramEtagMismatchException(programId, etag);
        }
        if (!res.IsSuccessStatusCode)
            throw await ServerSentence.FailureAsync(res, action, ct).ConfigureAwait(false);
    }

    // The bulk 422 body; null when the body is not a PointsRefused list (then the 422 is read as a sentence).
    private static async Task<IReadOnlyList<PointRefusal>?> RefusedAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var raw = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<PointsRefused>(raw, Json)?.Refused is { Count: > 0 } refused ? refused : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasEtagProperty(string? body)
    {
        if (body is null)
            return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.EnumerateObject().Any(p => string.Equals(p.Name, "etag", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BlockValuesUrl(Guid programId, BlockId blockId) => $"{BlockUrl(programId, blockId)}/values";

    private static string DefaultsUrl(Guid programId) => $"api/programs/{programId}/defaults";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage res, string route, CancellationToken ct) where T : class =>
        await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Server returned empty body for {route}.");

    // design.md §12.6: 400 → the refusal sentence; 409 → stale etag (defaults writes only); 404 → unknown block on a
    // block route; any other failure → HttpRequestException with the status and the message, as StationApi.
    private static async Task ThrowIfValueErrorAsync(HttpResponseMessage res, Guid programId, BlockId? block, ProgramEtag? etag, string action, CancellationToken ct)
    {
        switch (res.StatusCode)
        {
            case HttpStatusCode.BadRequest:
                throw await ValueRefusedException.FromAsync(res, ct).ConfigureAwait(false);
            case HttpStatusCode.Conflict when etag is { } expected:
                throw new ProgramEtagMismatchException(programId, expected);
            case HttpStatusCode.NotFound when block is { } b:
                // Same message rule as 400; an empty body keeps the generic message.
                var message = await ServerSentence.MessageAsync(res, ct).ConfigureAwait(false);
                throw message is null
                    ? new BlockNotFoundException(programId, b)
                    : new BlockNotFoundException(programId, b, message);
        }
        if (!res.IsSuccessStatusCode)
            throw await ServerSentence.FailureAsync(res, action, ct).ConfigureAwait(false);
    }

    private static async Task<EtagEditResult> ReadEtagAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var body = await res.Content.ReadFromJsonAsync<EtagResponse>(ct).ConfigureAwait(false);
        var etag = body?.Etag ?? throw new InvalidOperationException("Server returned no etag for an edit that returns one.");
        return new EtagEditResult(etag, body.HistoryWarning);
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
