using System.Net;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Tests.Programs;

public class ProgramsApiDeleteTests
{
    private static readonly Guid ProgramId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static IProgramsApi Api(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Programs;

    [Fact]
    public async Task DeleteAsync_Should_DELETE_The_Program_By_Id()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent);

        await Api(handler).DeleteAsync(ProgramId);

        handler.Method.Should().Be(HttpMethod.Delete);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}");
    }

    [Fact]
    public async Task DeleteAsync_Should_Throw_ProgramRunning_On_409()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict, "Operation in progress");

        var act = () => Api(handler).DeleteAsync(ProgramId);

        (await act.Should().ThrowAsync<ProgramRunningException>())
            .Which.ProgramId.Should().Be(ProgramId);
    }

    [Fact]
    public async Task DeleteAsync_Should_Throw_HttpRequestException_On_404_Without_Swallowing_The_Body()
    {
        // Control: a 404 must NOT be mapped to the 409 running type, and the server's
        // message must survive (ProgramRunningException does not derive from
        // HttpRequestException, so this fails if 404 were mis-mapped).
        var handler = new RecordingHandler(HttpStatusCode.NotFound, "Program not found");

        var act = () => Api(handler).DeleteAsync(ProgramId);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Message.Should().Contain("Program not found");
    }
    // ---- epic-128 feature-001 design.md §3, ADR-10: DeleteAsync(id, etag) ----

    private static readonly ProgramEtag V1 = new("sha256:v1");

    [Fact]
    public async Task DeleteAsync_WithEtag_On409CarryingEtag_ThrowsEtagMismatch_AndSentIfMatch_F001_U_012()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict,
            """{"error":"Program changed since it was read (stale etag).","etag":"sha256:v2"}""");

        var act = () => Api(handler).DeleteAsync(ProgramId, V1);

        var ex = (await act.Should().ThrowAsync<ProgramEtagMismatchException>()).Which;
        ex.ProgramId.Should().Be(ProgramId);
        ex.Expected.Should().Be(V1);
        handler.Method.Should().Be(HttpMethod.Delete);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/programs/{ProgramId}");
        handler.IfMatch.Should().Be("\"sha256:v1\"");
    }

    [Fact]
    public async Task DeleteAsync_WithEtag_On409ProgramFault_ThrowsProgramRunning_AndSentIfMatch_F001_U_012()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict,
            $$"""{"programId":"{{ProgramId}}","reason":"Operation in progress","currentOperation":1,"programGone":false,"changedWhileSaving":false,"adaptation":null}""");

        var act = () => Api(handler).DeleteAsync(ProgramId, V1);

        var ex = (await act.Should().ThrowAsync<ProgramRunningException>()).Which;
        ex.ProgramId.Should().Be(ProgramId);
        ex.Message.Should().Contain("Operation in progress");
        handler.IfMatch.Should().Be("\"sha256:v1\"");
    }

    [Fact]
    public async Task DeleteAsync_WithEtag_On204_Succeeds_F001_U_012()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent);

        await Api(handler).DeleteAsync(ProgramId, V1);

        handler.IfMatch.Should().Be("\"sha256:v1\"");
    }
}
