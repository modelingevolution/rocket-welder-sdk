using System.Net;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Pipelines;
using RocketWelder.SDK.Http.Tests.Programs;

namespace RocketWelder.SDK.Http.Tests.Pipelines;

public class PipelinesApiTests
{
    private static readonly Guid PipelineId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static IPipelinesApi Api(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Pipelines;

    [Fact]
    public async Task DeleteAsync_Should_DELETE_The_Pipeline_By_Id()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent);

        await Api(handler).DeleteAsync(PipelineId);

        handler.Method.Should().Be(HttpMethod.Delete);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/pipeline/{PipelineId}");
    }

    [Fact]
    public async Task DeleteAsync_Should_Throw_On_404_With_The_Server_Body()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, $"Pipeline {PipelineId} not found");

        var act = () => Api(handler).DeleteAsync(PipelineId);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Message.Should().Contain("not found");
    }
}
