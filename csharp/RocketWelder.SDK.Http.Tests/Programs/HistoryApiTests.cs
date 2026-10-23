using System.Net;
using System.Text.Json;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Tests.Programs;

/// <summary>
/// epic-128 feature-001 design.md §3 — the history DTOs against rw2's history JSON and the two history routes
/// (F001-U-010, F001-U-011).
/// </summary>
public class HistoryApiTests
{
    private static readonly Guid ProgramId = Guid.Parse("6f1c0000-0000-0000-0000-000000000001");
    private static readonly Guid RepositoryId = Guid.Parse("4e2a0000-0000-0000-0000-000000000003");

    // rw2's HistoryResponse(Directory, ProgramLogEntry[]) under the web defaults.
    private const string HistoryJson = """
        {"directory":"programs/seam-a","commits":[
          {"sha":"c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3","parents":["b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2"],
           "message":"Edit 48 points (p1, p2, p3, …) via API","authorName":"RocketWelder","authorEmail":"station@rocketwelder.local",
           "time":"2026-10-23T10:05:00+02:00","files":["programs/seam-a/points/p1.json","programs/seam-a/points/p2.json"]},
          {"sha":"b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2","parents":["a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1","9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f"],
           "message":"Add point p1 via API","authorName":"RocketWelder","authorEmail":"station@rocketwelder.local",
           "time":"2026-10-23T10:00:00+02:00","files":[]}]}
        """;

    private static IRocketWelderClient Client(RecordingHandler handler) => new RocketWelderClient(RecordingHandler.Client(handler));

    // ---- F001-U-010: every field is read; the order is kept ----

    [Fact]
    public void HistoryLog_ReadsEveryField_InOrder_F001_U_010()
    {
        var log = JsonSerializer.Deserialize<HistoryLog>(HistoryJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        log.Directory.Should().Be("programs/seam-a");
        log.Commits.Select(c => c.Message).Should().Equal("Edit 48 points (p1, p2, p3, …) via API", "Add point p1 via API");
        var newest = log.Commits[0];
        newest.Sha.Should().Be("c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3");
        newest.Parents.Should().Equal("b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2");
        newest.AuthorName.Should().Be("RocketWelder");
        newest.AuthorEmail.Should().Be("station@rocketwelder.local");
        newest.Time.Should().Be(new DateTimeOffset(2026, 10, 23, 10, 5, 0, TimeSpan.FromHours(2)));
        newest.Files.Should().Equal("programs/seam-a/points/p1.json", "programs/seam-a/points/p2.json");
        log.Commits[1].Parents.Should().HaveCount(2);
        log.Commits[1].Files.Should().BeEmpty();
    }

    // ---- F001-U-011: the two routes, with and without a limit ----

    [Fact]
    public async Task ProgramsGetHistoryAsync_WithLimit_GETsTheProgramHistoryRoute_F001_U_011()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, HistoryJson);

        var log = await Client(handler).Programs.GetHistoryAsync(ProgramId, 20);

        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri!.PathAndQuery.Should().Be($"/api/programs/{ProgramId}/history?limit=20");
        log.Commits.Should().HaveCount(2);
    }

    [Fact]
    public async Task RepositoriesGetHistoryAsync_WithoutLimit_GETsTheRepositoryHistoryRoute_F001_U_011()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, HistoryJson.Replace("programs/seam-a\",\"commits", "\",\"commits"));

        var log = await Client(handler).Repositories.GetHistoryAsync(RepositoryId, null);

        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri!.PathAndQuery.Should().Be($"/api/repositories/{RepositoryId}/history");
        log.Directory.Should().BeEmpty();
        log.Commits.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "'limit' must be between 1 and 1000.")]
    [InlineData(HttpStatusCode.NotFound, "Program 6f1c not found")]
    public async Task ProgramsGetHistoryAsync_OnFailure_ThrowsHttpRequestException_WithStatusAndSentence_F001_U_011(HttpStatusCode status, string sentence)
    {
        var handler = new RecordingHandler(status, JsonSerializer.Serialize(sentence));

        var act = () => Client(handler).Programs.GetHistoryAsync(ProgramId, 5000);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(status);
        ex.Message.Should().Contain(sentence);
    }

    [Fact]
    public async Task RepositoriesGetHistoryAsync_On404_ThrowsHttpRequestException_WithTheSentence_F001_U_011()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, "\"Repository 4e2a not found\"");

        var act = () => Client(handler).Repositories.GetHistoryAsync(RepositoryId);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Message.Should().Contain("Repository 4e2a not found");
    }
}
