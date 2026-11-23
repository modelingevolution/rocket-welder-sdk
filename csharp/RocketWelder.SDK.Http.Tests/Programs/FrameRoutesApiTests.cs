using System.Net;
using System.Text.Json;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Tests.Programs;

/// <summary>
/// rocket-welder2 epic-150 routes against a <see cref="RecordingHandler"/>: a run from a block, the designer's Undo, a copy of a
/// group, and the edit result's end block and renamed points — route, verb, body, <c>If-Match</c> and error mapping.
/// </summary>
public class FrameRoutesApiTests
{
    private static readonly Guid ProgramId = Guid.Parse("6f1c0000-0000-0000-0000-000000000150");
    private static readonly Guid RunId = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly ProgramEtag V1 = new("sha256:v1");
    private static readonly BlockId Frame = new("frm1");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static IProgramsApi Api(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Programs;

    private static string Started => $$"""{"runId":"{{RunId}}","dryRun":true}""";

    // ---- run ----

    [Fact]
    public async Task RunAsync_WithoutABlock_SendsTheBodyItAlwaysSent()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, Started);

        await Api(handler).RunAsync(ProgramId, dryRun: true);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.ToString().Should().Be($"http://welder.test/api/programs/{ProgramId}/run");
        handler.Body.Should().Be("""{"dryRun":true}""", "a run without a block is byte-identical to before");
    }

    [Fact]
    public async Task RunAsync_FromABlock_SendsTheBlockAndReadsTheRun()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, Started);

        var run = await Api(handler).RunAsync(ProgramId, dryRun: true, new BlockId("a1b2c3d4"));

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.ToString().Should().Be($"http://welder.test/api/programs/{ProgramId}/run");
        handler.Body.Should().Be("""{"dryRun":true,"fromBlockId":"a1b2c3d4"}""");
        run.Should().Be(new RunResult(RunId, true));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"error":"A run cannot start at block a1b2c3d4."}""")]
    [InlineData(HttpStatusCode.Conflict, """{"programId":"6f1c0000-0000-0000-0000-000000000150","reason":"no robot is set for this program"}""")]
    public async Task RunAsync_FromABlock_ARefusalCarriesTheStatusAndRw2sBody(HttpStatusCode status, string body)
    {
        var handler = new RecordingHandler(status, body);

        var act = () => Api(handler).RunAsync(ProgramId, dryRun: true, new BlockId("a1b2c3d4"));

        var thrown = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        thrown.StatusCode.Should().Be(status);
        thrown.Message.Should().Contain(body);
    }

    // ---- undo ----

    [Fact]
    public async Task UndoAsync_PostsTheUndoRoute_AndReadsTheLineAndTheEtag()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"undone":"Paste a copy of Frame via API","etag":"sha256:v2"}""");

        var undone = await Api(handler).UndoAsync(ProgramId);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.ToString().Should().Be($"http://welder.test/api/programs/{ProgramId}/undo");
        handler.Body.Should().BeNull("the route takes no body");
        handler.IfMatchPresent.Should().BeFalse();
        undone.Should().Be(new UndoResult("Paste a copy of Frame via API", new ProgramEtag("sha256:v2")));
    }

    [Fact]
    public async Task UndoAsync_NothingUndone_CarriesTheStatusAndRw2sBody()
    {
        const string body = """{"error":"Nothing was undone (Nothing).","outcome":"Nothing"}""";
        var handler = new RecordingHandler(HttpStatusCode.Conflict, body);

        var act = () => Api(handler).UndoAsync(ProgramId);

        var thrown = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        thrown.StatusCode.Should().Be(HttpStatusCode.Conflict);
        thrown.Message.Should().Contain(body);
    }

    // ---- copy ----

    [Fact]
    public async Task CopyBlockAsync_PostsWithIfMatch_AndReadsTheNewIdsAndThePoints()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            """{"block":"5e6f7a8b","etag":"sha256:v2","endBlock":"9c0d1e2f","points":{"p1":"t1","p2":"t2"}}""");

        var copy = await Api(handler).CopyBlockAsync(ProgramId, Frame, V1);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.ToString().Should().Be($"http://welder.test/api/programs/{ProgramId}/blocks/frm1/copy");
        handler.Body.Should().BeNull("the route takes no body");
        handler.IfMatch.Should().Be("\"sha256:v1\"");
        copy.Block.Should().Be(new BlockId("5e6f7a8b"));
        copy.EndBlock.Should().Be(new BlockId("9c0d1e2f"));
        copy.Etag.Should().Be(new ProgramEtag("sha256:v2"));
        copy.Points.Should().BeEquivalentTo(new Dictionary<PointName, PointName>
        {
            [new PointName("p1")] = new PointName("t1"),
            [new PointName("p2")] = new PointName("t2")
        });
    }

    [Fact]
    public async Task CopyBlockAsync_StaleEtag_IsTheEtagMismatch_AsEveryEdit()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict, """{"error":"stale etag"}""");

        var act = () => Api(handler).CopyBlockAsync(ProgramId, Frame, V1);

        await act.Should().ThrowAsync<ProgramEtagMismatchException>();
    }

    [Fact]
    public async Task CopyBlockAsync_UnknownBlock_IsBlockNotFound_AsEveryEdit()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, "Block 'frm1' is not in the program.");

        var act = () => Api(handler).CopyBlockAsync(ProgramId, Frame, V1);

        await act.Should().ThrowAsync<BlockNotFoundException>();
    }

    [Fact]
    public async Task CopyBlockAsync_NotAGroup_CarriesTheStatusAndRw2sBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "Block 'frm1' is not a group.");

        var act = () => Api(handler).CopyBlockAsync(ProgramId, Frame, V1);

        var thrown = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        thrown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        thrown.Message.Should().Contain("Block 'frm1' is not a group.");
    }

    // ---- the edit result's new members ----

    [Fact]
    public void BlockEditResult_ReadsAFramesEndBlock_AndAnEditWithoutOne()
    {
        JsonSerializer.Deserialize<BlockEditResult>("""{"block":"frm1","etag":"sha256:v2","endBlock":"end1"}""", Web)!
            .EndBlock.Should().Be(new BlockId("end1"));
        var plain = JsonSerializer.Deserialize<BlockEditResult>("""{"block":"mv1","etag":"sha256:v2"}""", Web)!;
        plain.EndBlock.Should().BeNull();
        plain.Points.Should().BeNull();
    }

    [Fact]
    public void PointName_MapWritesAndReadsTheNamesThemselves()
    {
        var map = new Dictionary<PointName, PointName> { [new PointName("p1")] = new PointName("t1") };

        JsonSerializer.Serialize(map).Should().Be("""{"p1":"t1"}""");
        JsonSerializer.Deserialize<Dictionary<PointName, PointName>>("""{"p1":"t1"}""")
            .Should().BeEquivalentTo(map);
    }
}
