using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Tests.Programs;

/// <summary>
/// epic-029 feature-011 design.md §9 — the §12.6 Programs DTOs against the §12 JSON examples, and the client methods' verb,
/// route, <c>If-Match</c> and error mapping against a <see cref="RecordingHandler"/>.
/// </summary>
public class ToleranceDtoRoundTripTests
{
    private static readonly Guid ProgramId = Guid.Parse("0b5c0000-0000-0000-0000-000000000001");
    private static readonly BlockId Adapt = new("adapt-7f3c");
    private static readonly ProgramEtag V1 = new("sha256:9e1");
    private const string OrderRefusal = "accept must not exceed review — write it as accept:review, e.g. 1.2:3.5.";

    // The rw2 wire uses the web defaults (camelCase), which is what the client reads with.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // §12.2 response
    private const string BlockValuesJson = """
        {"blockId":"adapt-7f3c","blockType":"Adapt","values":[
          {"key":"offsetTolerance","stored":null,"effective":"5:20","layer":"program","warning":null},
          {"key":"pathTolerance","stored":"1:3","effective":"1:3","layer":"block","warning":null}]}
        """;

    // §12.3 response
    private const string ProgramDefaultsJson = """
        {"etag":"sha256:41a","values":[
          {"key":"Adaptation.OffsetTolerance","stored":"5:20","effective":"5:20","layer":"program","warning":null},
          {"key":"Adaptation.PathTolerance","stored":null,"effective":"1:3","layer":"station","warning":null}]}
        """;

    // §12.5 — "state" is the ordinal rw2 writes (ProgramExecutionState.Failed = 4).
    private const string ProgramStatusJson = """
        {"programId":"0b5c0000-0000-0000-0000-000000000001","state":4,"currentRunId":null,"lastRunFinishedAtUtc":"2026-10-20T17:02:11Z",
         "lastError":"Adaptation adapt-7f3c blocked the run: path 3.6 mm is above 3 mm.",
         "adaptations":[{"blockId":"adapt-7f3c","attempt":2,"offset":0.4,"offsetBand":"5:20","offsetLayer":"program",
           "path":3.6,"pathBand":"1:3","pathLayer":"block","verdict":"block","trigger":"path"}]}
        """;

    // ---- DTOs: the §12 examples deserialise, and serialise back to the same JSON ----

    [Fact]
    public void BlockValues_RoundTripsTheBlockValuesExample()
    {
        var dto = JsonSerializer.Deserialize<BlockValues>(BlockValuesJson, Web)!;

        dto.BlockId.Should().Be("adapt-7f3c");
        dto.BlockType.Should().Be("Adapt");
        dto.Values.Should().HaveCount(2);
        dto.Values[0].Should().Be(new EffectiveValue("offsetTolerance", null, "5:20", "program", null));
        dto.Values[1].Should().Be(new EffectiveValue("pathTolerance", "1:3", "1:3", "block", null));
        dto.HistoryWarning.Should().BeNull();
        ShouldSerialiseTo(dto, BlockValuesJson, extraNulls: "historyWarning");
    }

    [Fact]
    public void BlockValues_CarriesTheHistoryWarning()
    {
        var dto = JsonSerializer.Deserialize<BlockValues>(
            """{"blockId":"b","blockType":"Adapt","values":[],"historyWarning":"not recorded"}""", Web)!;

        dto.HistoryWarning.Should().Be("not recorded");
    }

    [Fact]
    public void ProgramDefaults_RoundTripsTheProgramDefaultsExample()
    {
        var dto = JsonSerializer.Deserialize<ProgramDefaults>(ProgramDefaultsJson, Web)!;

        dto.Etag.Should().Be(new ProgramEtag("sha256:41a"));
        dto.Values[0].Should().Be(new EffectiveValue("Adaptation.OffsetTolerance", "5:20", "5:20", "program", null));
        dto.Values[1].Should().Be(new EffectiveValue("Adaptation.PathTolerance", null, "1:3", "station", null));
        ShouldSerialiseTo(dto, ProgramDefaultsJson, extraNulls: "historyWarning");
    }

    [Fact]
    public void SetValueRequest_SerialisesAsTheValueBody()
    {
        var json = JsonSerializer.Serialize(new SetValueRequest("1:3 mm"), Web);

        json.Should().Be("""{"value":"1:3 mm"}""");
        JsonSerializer.Deserialize<SetValueRequest>(json, Web).Should().Be(new SetValueRequest("1:3 mm"));
    }

    [Fact]
    public void ProgramStatus_RoundTripsTheStatusExample_WithAdaptations()
    {
        var dto = JsonSerializer.Deserialize<ProgramStatus>(ProgramStatusJson, Web)!;

        dto.State.Should().Be(ProgramExecutionState.Failed);
        dto.Adaptations.Should().ContainSingle().Which.Should().Be(new AdaptationJudgement(
            "adapt-7f3c", 2, 0.4, "5:20", "program", 3.6, "1:3", "block", "block", "path"));
        ShouldSerialiseTo(dto, ProgramStatusJson);
    }

    [Fact]
    public void ProgramStatus_WithoutAdaptations_ReadsAsNull()
    {
        var dto = JsonSerializer.Deserialize<ProgramStatus>(
            """{"programId":"0b5c0000-0000-0000-0000-000000000001","state":1,"currentRunId":null,"lastRunFinishedAtUtc":null,"lastError":null}""",
            Web)!;

        dto.State.Should().Be(ProgramExecutionState.Idle);
        dto.Adaptations.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_ReadsTheAdaptations()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ProgramStatusJson);

        var status = await Programs(handler).GetStatusAsync(ProgramId);

        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/status");
        status!.Adaptations.Should().ContainSingle().Which.Verdict.Should().Be("block");
    }

    // ---- block values: GET / PUT / DELETE, no If-Match ----

    [Fact]
    public async Task GetBlockValuesAsync_GETsTheBlockValuesRoute()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, BlockValuesJson);

        var result = await Programs(handler).GetBlockValuesAsync(ProgramId, Adapt);

        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/blocks/adapt-7f3c/values");
        handler.RequestUri.Query.Should().BeEmpty();
        result.Values.Should().HaveCount(2);
    }

    [Fact]
    public async Task SetBlockValueAsync_PUTsTheValueBody_WithoutIfMatch()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, BlockValuesJson);

        var result = await Programs(handler).SetBlockValueAsync(ProgramId, Adapt, "pathTolerance", "1:3 mm");

        handler.Method.Should().Be(HttpMethod.Put);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/blocks/adapt-7f3c/values/pathTolerance");
        handler.IfMatchPresent.Should().BeFalse();
        JsonNode.Parse(handler.Body!)!["value"]!.GetValue<string>().Should().Be("1:3 mm");
        result.Values[1].Effective.Should().Be("1:3");
    }

    [Fact]
    public async Task ClearBlockValueAsync_DELETEsTheKey_WithoutIfMatch()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, BlockValuesJson);

        var result = await Programs(handler).ClearBlockValueAsync(ProgramId, Adapt, "pathTolerance");

        handler.Method.Should().Be(HttpMethod.Delete);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/blocks/adapt-7f3c/values/pathTolerance");
        handler.IfMatchPresent.Should().BeFalse();
        result.BlockId.Should().Be("adapt-7f3c");
    }

    [Fact]
    public async Task SetBlockValueAsync_On400_ThrowsValueRefused_WithTheBodySentence()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, JsonSerializer.Serialize(OrderRefusal));

        var act = () => Programs(handler).SetBlockValueAsync(ProgramId, Adapt, "offsetTolerance", "3.5:1.2");

        var ex = (await act.Should().ThrowAsync<ValueRefusedException>()).Which;
        ex.Reason.Should().Be(OrderRefusal);
        ex.Message.Should().Be(OrderRefusal);
    }

    [Fact]
    public async Task SetBlockValueAsync_On400WithAPlainTextBody_ReasonIsTheRawBody()
    {
        const string body = "Value refused by an older endpoint.";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/plain"),
        });

        var act = () => Programs(handler).SetBlockValueAsync(ProgramId, Adapt, "offsetTolerance", "1:2");

        (await act.Should().ThrowAsync<ValueRefusedException>()).Which.Reason.Should().Be(body);
    }

    [Fact]
    public async Task SetBlockValueAsync_On404WithAPlainTextBody_UsesTheRawBodyAsTheMessage()
    {
        const string body = "No block adapt-7f3c in this program.";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/plain"),
        });

        var act = () => Programs(handler).SetBlockValueAsync(ProgramId, Adapt, "offsetTolerance", "1:2");

        var ex = (await act.Should().ThrowAsync<BlockNotFoundException>()).Which;
        ex.Message.Should().Be(body);
        ex.Block.Should().Be(Adapt);
        ex.ProgramId.Should().Be(ProgramId);
    }

    [Fact]
    public async Task GetBlockValuesAsync_On404_ThrowsBlockNotFound()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound);

        var act = () => Programs(handler).GetBlockValuesAsync(ProgramId, Adapt);

        var ex = (await act.Should().ThrowAsync<BlockNotFoundException>()).Which;
        ex.Block.Should().Be(Adapt);
        ex.Message.Should().Be($"Block 'adapt-7f3c' was not found in program '{ProgramId}' (HTTP 404).",
            "a 404 with no JSON-string body falls back to the existing message");
    }

    [Fact]
    public async Task SetBlockValueAsync_On404WithAJsonStringBody_UsesTheServerSentenceAsTheMessage()
    {
        const string sentence = "Block adapt-7f3c (Move) has no value 'offsetTolerance'.";
        var handler = new RecordingHandler(HttpStatusCode.NotFound, "\"Block adapt-7f3c (Move) has no value 'offsetTolerance'.\"");

        var act = () => Programs(handler).SetBlockValueAsync(ProgramId, Adapt, "offsetTolerance", "1:2");

        var ex = (await act.Should().ThrowAsync<BlockNotFoundException>()).Which;
        ex.Message.Should().Be(sentence);
        ex.Block.Should().Be(Adapt);
        ex.ProgramId.Should().Be(ProgramId);
    }

    // ---- program defaults: GET / PUT / DELETE, If-Match on writes ----

    [Fact]
    public async Task GetProgramDefaultsAsync_GETsTheDefaultsRoute()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ProgramDefaultsJson);

        var result = await Programs(handler).GetProgramDefaultsAsync(ProgramId);

        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/defaults");
        handler.RequestUri.Query.Should().BeEmpty();
        handler.IfMatchPresent.Should().BeFalse();
        result.Etag.Should().Be(new ProgramEtag("sha256:41a"));
    }

    [Fact]
    public async Task SetProgramDefaultAsync_PUTsWithIfMatch_AndReturnsTheNewEtag()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ProgramDefaultsJson);

        var result = await Programs(handler).SetProgramDefaultAsync(ProgramId, "Adaptation.OffsetTolerance", "5:20", V1);

        handler.Method.Should().Be(HttpMethod.Put);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/defaults/Adaptation.OffsetTolerance");
        handler.IfMatch.Should().Be("\"sha256:9e1\"");
        JsonNode.Parse(handler.Body!)!["value"]!.GetValue<string>().Should().Be("5:20");
        result.Etag.Should().Be(new ProgramEtag("sha256:41a"));
    }

    [Fact]
    public async Task ClearProgramDefaultAsync_DELETEsWithIfMatch()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ProgramDefaultsJson);

        var result = await Programs(handler).ClearProgramDefaultAsync(ProgramId, "Adaptation.OffsetTolerance", V1);

        handler.Method.Should().Be(HttpMethod.Delete);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/defaults/Adaptation.OffsetTolerance");
        handler.IfMatch.Should().Be("\"sha256:9e1\"");
        result.Values.Should().HaveCount(2);
    }

    [Fact]
    public async Task SetProgramDefaultAsync_On409_ThrowsEtagMismatch()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict, """{"error":"stale","etag":"sha256:41a"}""");

        var act = () => Programs(handler).SetProgramDefaultAsync(ProgramId, "Adaptation.OffsetTolerance", "5:20", V1);

        (await act.Should().ThrowAsync<ProgramEtagMismatchException>()).Which.Expected.Should().Be(V1);
    }

    [Fact]
    public async Task SetProgramDefaultAsync_On400_ThrowsValueRefused_WithTheBodySentence()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, JsonSerializer.Serialize(OrderRefusal));

        var act = () => Programs(handler).SetProgramDefaultAsync(ProgramId, "Adaptation.OffsetTolerance", "3.5:1.2", V1);

        (await act.Should().ThrowAsync<ValueRefusedException>()).Which.Reason.Should().Be(OrderRefusal);
    }

    [Fact]
    public async Task GetProgramDefaultsAsync_On404_ThrowsHttpRequestException_NotBlockNotFound()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound);

        var act = () => Programs(handler).GetProgramDefaultsAsync(ProgramId);

        (await act.Should().ThrowExactlyAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static IProgramsApi Programs(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Programs;

    // Serialises with the wire options and compares structurally with the example. extraNulls names init-only members
    // the example omits and the serialiser writes as null (e.g. historyWarning) — they must be null, then are dropped.
    private static void ShouldSerialiseTo<T>(T dto, string example, params string[] extraNulls)
    {
        var actual = JsonNode.Parse(JsonSerializer.Serialize(dto, Web))!.AsObject();
        foreach (var name in extraNulls)
        {
            actual.ContainsKey(name).Should().BeTrue(name);
            actual[name].Should().BeNull(name);
            actual.Remove(name);
        }

        JsonNode.DeepEquals(actual, JsonNode.Parse(example)).Should().BeTrue(actual.ToJsonString());
    }
}
