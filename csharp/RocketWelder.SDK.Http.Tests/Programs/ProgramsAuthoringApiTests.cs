using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Tests.Programs;

public class ProgramsAuthoringApiTests
{
    private static readonly Guid ProgramId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly ProgramEtag V1 = new("sha256:v1");

    private static IProgramsApi Api(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Programs;

    private static JsonObject Props(string name) => new() { ["name"] = name };

    private static JsonNode? BodyOf(RecordingHandler handler) => JsonNode.Parse(handler.Body!);

    // --- GetTree ---

    [Fact]
    public async Task GetTreeAsync_Should_GET_Tree_And_Parse_Blocks_And_Etag()
    {
        const string json = """
        {
          "blocks": [
            { "blockId": "blk-1", "type": "Point", "properties": { "name": "start" }, "disabled": false },
            { "blockId": "blk-2", "type": "Delay", "properties": { "seconds": 2 }, "disabled": true }
          ],
          "etag": "sha256:v1"
        }
        """;
        var handler = new RecordingHandler(HttpStatusCode.OK, json);

        var tree = await Api(handler).GetTreeAsync(ProgramId);

        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/tree");
        tree.Etag.Should().Be(V1);
        tree.Blocks.Should().HaveCount(2);
        tree.Blocks[0].BlockId.Should().Be(new BlockId("blk-1"));
        tree.Blocks[0].Type.Should().Be("Point");
        tree.Blocks[0].Properties["name"]!.GetValue<string>().Should().Be("start");
        tree.Blocks[0].Disabled.Should().BeFalse();
        tree.Blocks[1].BlockId.Should().Be(new BlockId("blk-2"));
        tree.Blocks[1].Disabled.Should().BeTrue();
    }

    // --- AddBlock ---

    [Fact]
    public async Task AddBlockAsync_Should_POST_With_IfMatch_And_Anchored_Body_And_Return_Result()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{ "block": "blk-3", "etag": "sha256:v2" }""");
        var request = new AddBlockRequest("Point", Props("weld-start"), BlockAnchor.After(new BlockId("blk-1")));

        var result = await Api(handler).AddBlockAsync(ProgramId, request, V1);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/blocks");
        handler.IfMatch.Should().Be("sha256:v1");

        var body = BodyOf(handler)!;
        body["type"]!.GetValue<string>().Should().Be("Point");
        body["anchor"]!["kind"]!.GetValue<string>().Should().Be("After");
        body["anchor"]!["ref"]!.GetValue<string>().Should().Be("blk-1");
        body["properties"]!["name"]!.GetValue<string>().Should().Be("weld-start");

        result.Block.Should().Be(new BlockId("blk-3"));
        result.Etag.Should().Be(new ProgramEtag("sha256:v2"));
    }

    [Fact]
    public async Task AddBlockAsync_Should_Serialize_Tail_Anchor_As_Tail()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{ "block": "blk-3", "etag": "sha256:v2" }""");
        var request = new AddBlockRequest("ArcOn", Props("x"), BlockAnchor.Tail);

        await Api(handler).AddBlockAsync(ProgramId, request, V1);

        var anchor = BodyOf(handler)!["anchor"]!.AsObject();
        anchor["kind"]!.GetValue<string>().Should().Be("Tail");
        anchor.ContainsKey("ref").Should().BeFalse();
    }

    [Fact]
    public async Task AddBlockAsync_Should_Throw_Mismatch_On_409()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict);
        var request = new AddBlockRequest("Point", Props("p"), BlockAnchor.Tail);

        var act = () => Api(handler).AddBlockAsync(ProgramId, request, V1);

        (await act.Should().ThrowAsync<ProgramEtagMismatchException>())
            .Which.Expected.Should().Be(V1);
    }

    [Fact]
    public async Task AddBlockAsync_Should_Throw_NotFound_On_404_With_Anchor_Ref()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound);
        var request = new AddBlockRequest("Point", Props("p"), BlockAnchor.After(new BlockId("ghost")));

        var act = () => Api(handler).AddBlockAsync(ProgramId, request, V1);

        (await act.Should().ThrowAsync<BlockNotFoundException>())
            .Which.Block.Should().Be(new BlockId("ghost"));
    }

    // --- EditBlock ---

    [Fact]
    public async Task EditBlockAsync_Should_PATCH_Block_With_IfMatch_And_Properties()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{ "block": "blk-1", "etag": "sha256:v2" }""");
        var request = new EditBlockRequest(new JsonObject { ["seconds"] = 5 });

        var result = await Api(handler).EditBlockAsync(ProgramId, new BlockId("blk-1"), request, V1);

        handler.Method.Should().Be(HttpMethod.Patch);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/blocks/blk-1");
        handler.IfMatch.Should().Be("sha256:v1");
        BodyOf(handler)!["properties"]!["seconds"]!.GetValue<int>().Should().Be(5);
        result.Block.Should().Be(new BlockId("blk-1"));
        result.Etag.Should().Be(new ProgramEtag("sha256:v2"));
    }

    [Fact]
    public async Task EditBlockAsync_Should_Throw_Mismatch_On_409()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict);
        var act = () => Api(handler).EditBlockAsync(ProgramId, new BlockId("blk-1"), new EditBlockRequest(Props("p")), V1);
        await act.Should().ThrowAsync<ProgramEtagMismatchException>();
    }

    [Fact]
    public async Task EditBlockAsync_Should_Throw_NotFound_On_404_With_BlockId()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound);
        var act = () => Api(handler).EditBlockAsync(ProgramId, new BlockId("blk-1"), new EditBlockRequest(Props("p")), V1);
        (await act.Should().ThrowAsync<BlockNotFoundException>())
            .Which.Block.Should().Be(new BlockId("blk-1"));
    }

    // --- RemoveBlock ---

    [Fact]
    public async Task RemoveBlockAsync_Should_DELETE_With_IfMatch_And_Return_New_Etag()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{ "etag": "sha256:v2" }""");

        var etag = await Api(handler).RemoveBlockAsync(ProgramId, new BlockId("blk-2"), V1);

        handler.Method.Should().Be(HttpMethod.Delete);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/blocks/blk-2");
        handler.IfMatch.Should().Be("sha256:v1");
        etag.Should().Be(new ProgramEtag("sha256:v2"));
    }

    [Fact]
    public async Task RemoveBlockAsync_Should_Throw_Mismatch_On_409()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict);
        var act = () => Api(handler).RemoveBlockAsync(ProgramId, new BlockId("blk-2"), V1);
        await act.Should().ThrowAsync<ProgramEtagMismatchException>();
    }

    [Fact]
    public async Task RemoveBlockAsync_Should_Throw_NotFound_On_404()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound);
        var act = () => Api(handler).RemoveBlockAsync(ProgramId, new BlockId("blk-2"), V1);
        (await act.Should().ThrowAsync<BlockNotFoundException>())
            .Which.Block.Should().Be(new BlockId("blk-2"));
    }

    // --- MoveBlock ---

    [Fact]
    public async Task MoveBlockAsync_Should_POST_Move_With_IfMatch_And_Body_And_Return_New_Etag()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{ "etag": "sha256:v2" }""");
        var request = new MoveBlockRequest(BlockAnchor.Before(new BlockId("blk-2")), Delta: null);

        var etag = await Api(handler).MoveBlockAsync(ProgramId, new BlockId("blk-3"), request, V1);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/blocks/blk-3/move");
        handler.IfMatch.Should().Be("sha256:v1");
        var body = BodyOf(handler)!;
        body["anchor"]!["kind"]!.GetValue<string>().Should().Be("Before");
        body["anchor"]!["ref"]!.GetValue<string>().Should().Be("blk-2");
        body.AsObject().ContainsKey("delta").Should().BeFalse();
        etag.Should().Be(new ProgramEtag("sha256:v2"));
    }

    [Fact]
    public async Task MoveBlockAsync_Should_Serialize_Signed_Delta_And_Omit_Anchor()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{ "etag": "sha256:v2" }""");
        var request = new MoveBlockRequest(Anchor: null, Delta: -2);

        await Api(handler).MoveBlockAsync(ProgramId, new BlockId("blk-3"), request, V1);

        var body = BodyOf(handler)!;
        body["delta"]!.GetValue<int>().Should().Be(-2);
        body.AsObject().ContainsKey("anchor").Should().BeFalse();
    }

    [Fact]
    public async Task MoveBlockAsync_Should_Throw_Mismatch_On_409()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict);
        var act = () => Api(handler).MoveBlockAsync(ProgramId, new BlockId("blk-3"), new MoveBlockRequest(BlockAnchor.Tail, null), V1);
        await act.Should().ThrowAsync<ProgramEtagMismatchException>();
    }

    [Fact]
    public async Task MoveBlockAsync_Should_Throw_NotFound_On_404()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound);
        var act = () => Api(handler).MoveBlockAsync(ProgramId, new BlockId("blk-3"), new MoveBlockRequest(BlockAnchor.Tail, null), V1);
        (await act.Should().ThrowAsync<BlockNotFoundException>())
            .Which.Block.Should().Be(new BlockId("blk-3"));
    }

    // --- CapturePoint (FR-6) ---

    [Fact]
    public async Task CapturePointAsync_Should_POST_Capture_Without_IfMatch_And_Return_Name()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{ "name": "weld-start" }""");
        var request = new CapturePointRequest("weld-start");

        var result = await Api(handler).CapturePointAsync(ProgramId, request);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/capture");
        handler.IfMatchPresent.Should().BeFalse();
        BodyOf(handler)!["name"]!.GetValue<string>().Should().Be("weld-start");
        result.Name.Should().Be("weld-start");
    }
}
