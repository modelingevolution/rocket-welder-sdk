using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Tests.Programs;

public class ProgramsHistoryWarningTests
{
    private const string EditWarning =
        "The edit was saved, but it was not recorded in the program's history: Unable to create '/data/repo/.git/index.lock': File exists.";
    private const string PoseWarning =
        "The pose was saved, but it was not recorded in the program's history: Unable to create '/data/repo/.git/index.lock': File exists.";

    private static readonly Guid ProgramId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly ProgramEtag V1 = new("sha256:v1");
    private static readonly BlockId Blk = new("blk-2");

    private static IProgramsApi Api(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Programs;

    private static RecordingHandler NotRecorded(string json) => new(_ =>
    {
        var res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        res.Headers.Add("X-History-Not-Recorded", "true");
        return res;
    });

    private static string Body(string members, string warning) =>
        $"{{{members},\"historyWarning\":{JsonSerializer.Serialize(warning)}}}";

    private static RecordingHandler Recorded(string json) => new(HttpStatusCode.OK, json);

    [Fact]
    public async Task AddBlockAsync_Should_Carry_The_HistoryWarning_Of_A_Not_Recorded_Edit()
    {
        var handler = NotRecorded(Body("\"block\":\"blk-3\",\"etag\":\"sha256:v2\"", EditWarning));

        var result = await Api(handler).AddBlockAsync(ProgramId,
            new AddBlockRequest("Delay", new JsonObject(), BlockAnchor.Tail), V1);

        result.Block.Should().Be(new BlockId("blk-3"));
        result.HistoryWarning.Should().Be(EditWarning);
    }

    [Fact]
    public async Task AddBlockAsync_Should_Carry_No_HistoryWarning_When_The_Edit_Was_Recorded()
    {
        var handler = Recorded("""{ "block": "blk-3", "etag": "sha256:v2" }""");

        var result = await Api(handler).AddBlockAsync(ProgramId,
            new AddBlockRequest("Delay", new JsonObject(), BlockAnchor.Tail), V1);

        result.Etag.Should().Be(new ProgramEtag("sha256:v2"));
        result.HistoryWarning.Should().BeNull();
    }

    [Fact]
    public async Task EditBlockAsync_Should_Carry_The_HistoryWarning_Of_A_Not_Recorded_Edit()
    {
        var handler = NotRecorded(Body("\"block\":\"blk-2\",\"etag\":\"sha256:v2\"", EditWarning));

        var result = await Api(handler).EditBlockAsync(ProgramId, Blk, new EditBlockRequest(new JsonObject()), V1);

        result.HistoryWarning.Should().Be(EditWarning);
    }

    [Fact]
    public async Task EditBlockAsync_Should_Carry_No_HistoryWarning_When_The_Edit_Was_Recorded()
    {
        var handler = Recorded("""{ "block": "blk-2", "etag": "sha256:v2" }""");

        var result = await Api(handler).EditBlockAsync(ProgramId, Blk, new EditBlockRequest(new JsonObject()), V1);

        result.Etag.Should().Be(new ProgramEtag("sha256:v2"));
        result.HistoryWarning.Should().BeNull();
    }

    [Fact]
    public async Task RemoveBlockAsync_Should_Carry_The_HistoryWarning_Of_A_Not_Recorded_Edit()
    {
        var handler = NotRecorded(Body("\"block\":null,\"etag\":\"sha256:v2\"", EditWarning));

        var result = await Api(handler).RemoveBlockAsync(ProgramId, Blk, V1);

        result.Etag.Should().Be(new ProgramEtag("sha256:v2"));
        result.HistoryWarning.Should().Be(EditWarning);
    }

    [Fact]
    public async Task RemoveBlockAsync_Should_Carry_No_HistoryWarning_When_The_Edit_Was_Recorded()
    {
        var handler = Recorded("""{ "block": null, "etag": "sha256:v2" }""");

        var result = await Api(handler).RemoveBlockAsync(ProgramId, Blk, V1);

        result.Etag.Should().Be(new ProgramEtag("sha256:v2"));
        result.HistoryWarning.Should().BeNull();
    }

    [Fact]
    public async Task MoveBlockAsync_Should_Carry_The_HistoryWarning_Of_A_Not_Recorded_Edit()
    {
        var handler = NotRecorded(Body("\"block\":null,\"etag\":\"sha256:v2\"", EditWarning));

        var result = await Api(handler).MoveBlockAsync(ProgramId, Blk, new MoveBlockRequest(null, -1), V1);

        result.Etag.Should().Be(new ProgramEtag("sha256:v2"));
        result.HistoryWarning.Should().Be(EditWarning);
    }

    [Fact]
    public async Task MoveBlockAsync_Should_Carry_No_HistoryWarning_When_The_Edit_Was_Recorded()
    {
        var handler = Recorded("""{ "block": null, "etag": "sha256:v2" }""");

        var result = await Api(handler).MoveBlockAsync(ProgramId, Blk, new MoveBlockRequest(null, -1), V1);

        result.Etag.Should().Be(new ProgramEtag("sha256:v2"));
        result.HistoryWarning.Should().BeNull();
    }

    [Fact]
    public async Task CapturePointAsync_Should_Carry_The_HistoryWarning_Of_A_Not_Recorded_Capture()
    {
        var handler = NotRecorded(Body("\"name\":\"t1\"", PoseWarning));

        var result = await Api(handler).CapturePointAsync(ProgramId, new CapturePointRequest(null));

        result.Name.Should().Be("t1");
        result.HistoryWarning.Should().Be(PoseWarning);
    }

    [Fact]
    public async Task CapturePointAsync_Should_Carry_No_HistoryWarning_When_The_Capture_Was_Recorded()
    {
        var handler = Recorded("""{ "name": "t1" }""");

        var result = await Api(handler).CapturePointAsync(ProgramId, new CapturePointRequest(null));

        result.Name.Should().Be("t1");
        result.HistoryWarning.Should().BeNull();
    }
}
