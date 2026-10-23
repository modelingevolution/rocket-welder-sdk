using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Tests.Programs;

/// <summary>
/// epic-128 feature-001 design.md §2.6–§2.7 — the run-progress DTOs and the client-side wait (F001-U-006 … F001-U-009).
/// </summary>
public class RunProgressApiTests
{
    private static readonly Guid ProgramId = Guid.Parse("6f1c0000-0000-0000-0000-000000000001");
    private static readonly Guid RunId = Guid.Parse("a90e0000-0000-0000-0000-000000000002");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // design.md §2.6 example, with every BlockRunState value.
    private const string ProgressJson = """
        {"programId":"6f1c0000-0000-0000-0000-000000000001","runId":"a90e0000-0000-0000-0000-000000000002",
         "state":"Cancelled","dryRun":true,
         "startedAt":"2026-10-23T10:00:00+00:00","endedAt":"2026-10-23T10:00:14.25+00:00",
         "durationSeconds":14.25,"arcOnSeconds":10.02,
         "blocks":[
          {"blockId":"b1","type":"MovePtp","order":0,"state":"Done","startedAt":"2026-10-23T10:00:00.01+00:00","endedAt":"2026-10-23T10:00:02.1+00:00","durationSeconds":2.09},
          {"blockId":"b2","type":"ArcOn","order":1,"state":"Failed","startedAt":"2026-10-23T10:00:02.1+00:00","endedAt":"2026-10-23T10:00:02.4+00:00","durationSeconds":0.3},
          {"blockId":"b3","type":"Move","order":2,"state":"Interrupted","startedAt":"2026-10-23T10:00:02.4+00:00","endedAt":null,"durationSeconds":null},
          {"blockId":"b4","type":"ArcOff","order":3,"state":"NotRun","startedAt":null,"endedAt":null,"durationSeconds":null},
          {"blockId":"b5","type":"Delay","order":4,"state":"Pending","startedAt":null,"endedAt":null,"durationSeconds":null},
          {"blockId":"b6","type":"","order":5,"state":"Running","startedAt":"2026-10-23T10:00:03+00:00","endedAt":null,"durationSeconds":null}]}
        """;

    private static IProgramsApi Api(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Programs;

    private static string Progress(ProgramExecutionState state) => $$"""
        {"programId":"{{ProgramId}}","runId":"{{RunId}}","state":"{{state}}","dryRun":false,
         "startedAt":"2026-10-23T10:00:00+00:00","endedAt":null,"durationSeconds":null,"arcOnSeconds":0,"blocks":[]}
        """;

    // ---- F001-U-006: RunProgress round-trips; states are strings; null times stay null ----

    [Fact]
    public void RunProgress_RoundTrips_WithStatesAsStrings_AndNullTimes_F001_U_006()
    {
        var dto = JsonSerializer.Deserialize<RunProgress>(ProgressJson, Web)!;

        dto.State.Should().Be(ProgramExecutionState.Cancelled);
        dto.DryRun.Should().BeTrue();
        dto.ArcOnSeconds.Should().Be(10.02);
        dto.Blocks.Select(b => b.State).Should().Equal(
            BlockRunState.Done, BlockRunState.Failed, BlockRunState.Interrupted,
            BlockRunState.NotRun, BlockRunState.Pending, BlockRunState.Running);
        dto.Blocks[3].Should().Be(new BlockProgress("b4", "ArcOff", 3, BlockRunState.NotRun, null, null, null));

        var written = JsonNode.Parse(JsonSerializer.Serialize(dto, Web))!;
        JsonNode.DeepEquals(written, JsonNode.Parse(ProgressJson)).Should().BeTrue(written.ToJsonString());
        written["blocks"]![2]!["state"]!.GetValue<string>().Should().Be("Interrupted");
        written["blocks"]![3]!["state"]!.GetValue<string>().Should().Be("NotRun");
        written["blocks"]![3]!["startedAt"].Should().BeNull();
    }

    [Fact]
    public void RunProgress_ReadsAnOrdinalRunState_F001_U_006()
    {
        // ProgramStatus writes the run state as its ordinal; RunProgress writes the name and still reads the ordinal.
        var dto = JsonSerializer.Deserialize<RunProgress>(Progress(ProgramExecutionState.Running).Replace("\"Running\"", "2"), Web)!;

        dto.State.Should().Be(ProgramExecutionState.Running);
    }

    [Fact]
    public async Task GetRunProgressAsync_GETsTheProgressRoute_F001_U_006()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ProgressJson);

        var progress = await Api(handler).GetRunProgressAsync(ProgramId, RunId);

        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/runs/{RunId}/progress");
        progress.Blocks.Should().HaveCount(6);
    }

    [Fact]
    public async Task GetRunProgressAsync_On404_ThrowsHttpRequestException_WithTheSentence_F001_U_006()
    {
        const string sentence = "Run a90e is not the latest run of program 6f1c; only the latest run's progress is kept.";
        var handler = new RecordingHandler(HttpStatusCode.NotFound, JsonSerializer.Serialize(sentence));

        var act = () => Api(handler).GetRunProgressAsync(ProgramId, RunId);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Message.Should().Contain(sentence);
    }

    // ---- F001-U-007: the wait returns the first answer that is not Running ----

    [Fact]
    public async Task WaitForRunAsync_ReturnsTheFinishedAnswer_AfterTheThirdRequest_F001_U_007()
    {
        var requests = 0;
        var handler = new RecordingHandler(_ => Json(Interlocked.Increment(ref requests) < 3
            ? Progress(ProgramExecutionState.Running)
            : Progress(ProgramExecutionState.Finished)));

        var result = await Api(handler).WaitForRunAsync(ProgramId, RunId, TimeSpan.FromSeconds(10));

        result.State.Should().Be(ProgramExecutionState.Finished);
        requests.Should().Be(3);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}/runs/{RunId}/progress");
    }

    // ---- F001-U-008: the wait returns Running when the timeout passes, and throws nothing ----

    [Fact]
    public async Task WaitForRunAsync_ReturnsRunning_WithinTheTimeoutPlusOnePoll_F001_U_008()
    {
        var handler = new RecordingHandler(_ => Json(Progress(ProgramExecutionState.Running)));
        var clock = Stopwatch.StartNew();

        var result = await Api(handler).WaitForRunAsync(ProgramId, RunId, TimeSpan.FromSeconds(2));

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.9));
        result.State.Should().Be(ProgramExecutionState.Running);
    }

    // ---- F001-U-009: a timeout outside 1–600 s is refused before any request ----

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public async Task WaitForRunAsync_OutsideOneTo600Seconds_ThrowsAndSendsNothing_F001_U_009(int seconds)
    {
        var handler = new RecordingHandler(_ => Json(Progress(ProgramExecutionState.Finished)));

        var act = () => Api(handler).WaitForRunAsync(ProgramId, RunId, TimeSpan.FromSeconds(seconds));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.RequestUri.Should().BeNull("no request is sent");
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
