using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Programs;
using RocketWelder.SDK.Http.Robots;

namespace RocketWelder.SDK.Http.Tests.Programs;

/// <summary>
/// epic-128 feature-001 design.md §1.1 — the point DTOs against rw2's wire, and the point methods' route, body,
/// <c>If-Match</c> and error mapping against a <see cref="RecordingHandler"/> (F001-U-001 … F001-U-005).
/// </summary>
public class ProgramPointsApiTests
{
    private static readonly Guid ProgramId = Guid.Parse("6f1c0000-0000-0000-0000-000000000001");
    private static readonly ProgramEtag V1 = new("sha256:v1");
    private static readonly BlockId Ring = new("blk-7");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // The JSON rw2's PointWire.ToDto answers today: camelCase; joints six numbers or [].
    private const string PointJson = """
        {"name":"p1","pose":{"x":412.5,"y":-38.25,"z":120,"rx":180,"ry":-2.5,"rz":91.75},
         "capturedAt":"2026-10-23T10:00:00.123+00:00","origin":"Taught",
         "axes":[{"deviceRole":"Positioner","axisName":"A1","kind":"Rotary","value":45.5},
                 {"deviceRole":"Track","axisName":"X","kind":"Linear","value":1200}],
         "joints":[10,-20.5,30,0,45,-90]}
        """;

    private const string PointWithoutJointsJson = """
        {"name":"p2","pose":{"x":1,"y":2,"z":3,"rx":0,"ry":0,"rz":0},
         "capturedAt":"2026-10-23T10:00:00+00:00","origin":"View","axes":[],"joints":[]}
        """;

    private const string StaleEtagBody = """{"error":"Program changed since it was read (stale etag).","etag":"sha256:v2"}""";

    private static IProgramsApi Api(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Programs;

    private static string EditResult(string point) => $$"""{"point":{{point}},"etag":"sha256:v2"}""";

    // ---- F001-U-001: ProgramPoint round-trips rw2's JSON ----

    [Theory]
    [InlineData(PointJson)]
    [InlineData(PointWithoutJointsJson)]
    public void ProgramPoint_RoundTripsRw2Json_F001_U_001(string json)
    {
        var point = JsonSerializer.Deserialize<ProgramPoint>(json, Web)!;

        JsonNode.DeepEquals(JsonNode.Parse(JsonSerializer.Serialize(point, Web)), JsonNode.Parse(json))
            .Should().BeTrue("every field round-trips: {0}", JsonSerializer.Serialize(point, Web));
    }

    [Fact]
    public void ProgramPoint_ReadsEveryField_F001_U_001()
    {
        var point = JsonSerializer.Deserialize<ProgramPoint>(PointJson, Web)!;

        point.Name.Should().Be("p1");
        point.Pose.Should().Be(new RobotPose(412.5, -38.25, 120, 180, -2.5, 91.75));
        point.CapturedAt.Should().Be(new DateTimeOffset(2026, 10, 23, 10, 0, 0, 123, TimeSpan.Zero));
        point.Origin.Should().Be("Taught");
        point.Axes.Should().Equal(
            new PointAxis("Positioner", "A1", "Rotary", 45.5),
            new PointAxis("Track", "X", "Linear", 1200));
        point.Joints.Should().Equal(10, -20.5, 30, 0, 45, -90);
    }

    [Fact]
    public async Task GetPointsAsync_GETsThePointsRoute_F001_U_001()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, $"[{PointJson},{PointWithoutJointsJson}]");

        var points = await Api(handler).GetPointsAsync(ProgramId);

        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/points");
        points.Select(p => p.Name).Should().Equal("p1", "p2");
        points[1].Joints.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPointAsync_GETsTheEscapedPointRoute_F001_U_001()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, PointJson);

        var point = await Api(handler).GetPointAsync(ProgramId, "weld start");

        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/points/weld%20start");
        point.Name.Should().Be("p1");
    }

    // ---- F001-U-002: a PointWrite with only Pose sends only pose ----

    [Fact]
    public async Task PutPointAsync_SendsOnlyThePose_ToTheEscapedRoute_WithQuotedIfMatch_F001_U_002()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, EditResult(PointJson));
        var write = new PointWrite("ignored") { Pose = new RobotPose(1, 2, 3, 4, 5, 6) };

        var result = await Api(handler).PutPointAsync(ProgramId, "seam/1", write, V1);

        handler.Method.Should().Be(HttpMethod.Put);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/points/seam%2F1");
        handler.IfMatch.Should().Be("\"sha256:v1\"");
        var body = JsonNode.Parse(handler.Body!)!.AsObject();
        body.Select(p => p.Key).Should().NotContain(["axes", "joints", "origin"]);
        JsonNode.DeepEquals(body["pose"], JsonNode.Parse("""{"x":1,"y":2,"z":3,"rx":4,"ry":5,"rz":6}""")).Should().BeTrue();
        result.Point.Name.Should().Be("p1");
        result.Etag.Should().Be(new ProgramEtag("sha256:v2"));
        result.HistoryWarning.Should().BeNull();
    }

    [Fact]
    public void PointWrite_SerialisesWithoutNullMembers_UnderPlainWebOptions_F001_U_002()
    {
        // rw2 and the MCP serialise it with their own options; the attributes keep an absent field absent everywhere.
        var json = JsonSerializer.Serialize(new PointWrite("p1") { Joints = [] }, Web);

        json.Should().Be("""{"name":"p1","joints":[]}""");
    }

    [Fact]
    public async Task PutPointAsync_CarriesTheHistoryWarning_F001_U_002()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            $$"""{"point":{{PointJson}},"etag":"sha256:v2","historyWarning":"not recorded"}""");

        var result = await Api(handler).PutPointAsync(ProgramId, "p1", new PointWrite("p1") { Origin = "View" }, V1);

        result.HistoryWarning.Should().Be("not recorded");
    }

    // ---- F001-U-003: a bulk 422 lists every refused name ----

    [Fact]
    public async Task PutPointsAsync_On422_ThrowsPointsRefused_ListingEveryName_F001_U_003()
    {
        var handler = new RecordingHandler(HttpStatusCode.UnprocessableEntity, """
            {"refused":[{"name":"a","reason":"Point 'a' is not declared in the program."},
                        {"name":"b","reason":"'pose.x' is required and must be a number (mm / deg)."}]}
            """);
        var items = new[]
        {
            new PointWrite("a") { Pose = new RobotPose(1, 2, 3, 0, 0, 0) },
            new PointWrite("b") { Origin = "Taught" },
        };

        var act = () => Api(handler).PutPointsAsync(ProgramId, items, V1);

        var ex = (await act.Should().ThrowAsync<PointsRefusedException>()).Which;
        ex.Refused.Should().Equal(
            new PointRefusal("a", "Point 'a' is not declared in the program."),
            new PointRefusal("b", "'pose.x' is required and must be a number (mm / deg)."));
        ex.Message.Should().Contain("a: Point 'a' is not declared").And.Contain("b: 'pose.x'");
        handler.Method.Should().Be(HttpMethod.Put);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/points");
        handler.IfMatch.Should().Be("\"sha256:v1\"");
        var body = JsonNode.Parse(handler.Body!)!.AsArray();
        body.Should().HaveCount(2);
        body[0]!["name"]!.GetValue<string>().Should().Be("a");
        body[1]!.AsObject().Select(p => p.Key).Should().Equal("name", "origin");
    }

    [Fact]
    public async Task PutPointsAsync_ReturnsEveryPointAndTheNewEtag_F001_U_003()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            $$"""{"points":[{{PointJson}},{{PointWithoutJointsJson}}],"etag":"sha256:v2"}""");

        var result = await Api(handler).PutPointsAsync(ProgramId,
            [new PointWrite("p1") { Origin = "Taught" }, new PointWrite("p2") { Origin = "View" }], V1);

        result.Points.Select(p => p.Name).Should().Equal("p1", "p2");
        result.Etag.Should().Be(new ProgramEtag("sha256:v2"));
    }

    [Fact]
    public async Task PutPointsAsync_On400_ThrowsValueRefused_WithTheSentence_F001_U_003()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "\"At most 200 points per write.\"");

        var act = () => Api(handler).PutPointsAsync(ProgramId, [new PointWrite("a")], V1);

        (await act.Should().ThrowAsync<ValueRefusedException>()).Which.Reason.Should().Be("At most 200 points per write.");
    }

    // ---- F001-U-004: a 409 on every point write is a stale etag ----

    public static TheoryData<string> PointWrites => new() { "PutPoint", "PutPoints", "CalculateRingStart" };

    [Theory]
    [MemberData(nameof(PointWrites))]
    public async Task PointWrite_On409_ThrowsEtagMismatch_WithProgramAndSentEtag_F001_U_004(string method)
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict, StaleEtagBody);

        var act = Call(Api(handler), method);

        var ex = (await act.Should().ThrowAsync<ProgramEtagMismatchException>()).Which;
        ex.ProgramId.Should().Be(ProgramId);
        ex.Expected.Should().Be(V1);
        handler.IfMatch.Should().Be("\"sha256:v1\"");
    }

    // ---- F001-U-005: 404 keeps the sentence; 400/422 are refusals ----

    [Fact]
    public async Task GetPointAsync_On404_ThrowsHttpRequestException_WithStatusAndSentence_F001_U_005()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, "\"Point 'zz' not found.\"");

        var act = () => Api(handler).GetPointAsync(ProgramId, "zz");

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Message.Should().Contain("Point 'zz' not found.");
    }

    [Fact]
    public async Task GetPointsAsync_On404_ThrowsHttpRequestException_WithStatusAndSentence_F001_U_005()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, "\"Program 6f1c not found\"");

        var act = () => Api(handler).GetPointsAsync(ProgramId);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Message.Should().Contain("Program 6f1c not found");
    }

    [Theory]
    [InlineData("PutPoint", HttpStatusCode.BadRequest, "'pose.rz' is required and must be a number (mm / deg).")]
    [InlineData("PutPoint", HttpStatusCode.UnprocessableEntity, "Point 'zz' is not declared in the program — add the Point block first.")]
    [InlineData("CalculateRingStart", HttpStatusCode.BadRequest, "Block 'blk-7' is not a ring (centre + radius circle).")]
    [InlineData("CalculateRingStart", HttpStatusCode.UnprocessableEntity, "The ring's centre has no taught pose.")]
    public async Task PointWrite_On400Or422_ThrowsValueRefused_WithTheSentence_F001_U_005(string method, HttpStatusCode status, string sentence)
    {
        var handler = new RecordingHandler(status, JsonSerializer.Serialize(sentence));

        var act = Call(Api(handler), method);

        (await act.Should().ThrowAsync<ValueRefusedException>()).Which.Reason.Should().Be(sentence);
    }

    [Fact]
    public async Task CalculateRingStartAsync_POSTsTheRoute_AndReadsTheResult_F001_U_005()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            """{"name":"circle_blk-7_start","pose":{"x":10,"y":20,"z":30,"rx":180,"ry":0,"rz":90},"etag":"sha256:v2"}""");

        var result = await Api(handler).CalculateRingStartAsync(ProgramId, Ring, V1);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/blocks/blk-7/calculate-start");
        handler.IfMatch.Should().Be("\"sha256:v1\"");
        result.Should().Be(new RingStartResult("circle_blk-7_start", new RobotPose(10, 20, 30, 180, 0, 90), new ProgramEtag("sha256:v2")));
    }

    [Fact]
    public async Task CalculateRingStartAsync_On404_ThrowsBlockNotFound_F001_U_005()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, "\"Block 'blk-7' not found.\"");

        var act = () => Api(handler).CalculateRingStartAsync(ProgramId, Ring, V1);

        var ex = (await act.Should().ThrowAsync<BlockNotFoundException>()).Which;
        ex.Block.Should().Be(Ring);
        ex.Message.Should().Be("Block 'blk-7' not found.");
    }

    private static Func<Task> Call(IProgramsApi api, string method) => method switch
    {
        "PutPoint" => () => api.PutPointAsync(ProgramId, "zz", new PointWrite("zz") { Pose = new RobotPose(1, 2, 3, 0, 0, 0) }, V1),
        "PutPoints" => () => api.PutPointsAsync(ProgramId, [new PointWrite("zz") { Pose = new RobotPose(1, 2, 3, 0, 0, 0) }], V1),
        "CalculateRingStart" => () => api.CalculateRingStartAsync(ProgramId, Ring, V1),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };
}
