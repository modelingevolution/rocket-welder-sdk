using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Repositories;
using RocketWelder.SDK.Http.Tests.Programs;

namespace RocketWelder.SDK.Http.Tests.Repositories;

public class RepositoriesApiTests
{
    private static readonly Guid RepoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid NewRepoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid NewProgramId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static IRepositoriesApi Api(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Repositories;

    private static JsonNode? BodyOf(RecordingHandler handler) => JsonNode.Parse(handler.Body!);

    // --- CreateAsync ---

    [Fact]
    public async Task CreateAsync_Should_POST_Repositories_With_Name_Body_And_Return_RepositoryId()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, $$"""{ "repositoryId": "{{NewRepoId}}" }""");

        var id = await Api(handler).CreateAsync("my-repo");

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.AbsolutePath.Should().Be("/api/repositories");
        BodyOf(handler)!["name"]!.GetValue<string>().Should().Be("my-repo");
        id.Should().Be(NewRepoId);
    }

    [Fact]
    public async Task CreateAsync_Should_Throw_RepositoryAlreadyExists_On_409()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict, "A repository named 'my-repo' already exists.");

        var act = () => Api(handler).CreateAsync("my-repo");

        (await act.Should().ThrowAsync<RepositoryAlreadyExistsException>())
            .Which.Name.Should().Be("my-repo");
    }

    [Fact]
    public async Task CreateAsync_Should_Throw_HttpRequestException_On_400_Without_Swallowing_The_Body()
    {
        // Control: a 400 must NOT be mapped to the 409 type, and the server's message
        // must survive to the caller (RepositoryAlreadyExistsException does not derive
        // from HttpRequestException, so this assertion fails if 400 were mis-mapped).
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "Repository name is required");

        var act = () => Api(handler).CreateAsync("");

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Message.Should().Contain("Repository name is required");
    }

    [Fact]
    public async Task CreateAsync_Should_Throw_On_A_Null_Body_Rather_Than_Return_Empty_Guid()
    {
        // Fail fast rather than hand back Guid.Empty as if a repository were created.
        var handler = new RecordingHandler(HttpStatusCode.OK, "null");
        var act = () => Api(handler).CreateAsync("my-repo");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // --- CreateProgramAsync ---

    [Fact]
    public async Task CreateProgramAsync_Should_POST_Programs_With_Name_Body_And_Return_ProgramId()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, $$"""{ "programId": "{{NewProgramId}}" }""");

        var id = await Api(handler).CreateProgramAsync(RepoId, "seam-1");

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/repositories/{RepoId}/programs");
        BodyOf(handler)!["name"]!.GetValue<string>().Should().Be("seam-1");
        id.Should().Be(NewProgramId);
    }

    [Fact]
    public async Task CreateProgramAsync_Should_Throw_RepositoryNotFound_On_404()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, $"Repository {RepoId} is not cloned.");

        var act = () => Api(handler).CreateProgramAsync(RepoId, "seam-1");

        (await act.Should().ThrowAsync<RepositoryNotFoundException>())
            .Which.RepositoryId.Should().Be(RepoId);
    }

    [Fact]
    public async Task CreateProgramAsync_Should_Throw_DuplicateProgram_On_409()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict, "A program named 'seam-1' already exists.");

        var act = () => Api(handler).CreateProgramAsync(RepoId, "seam-1");

        var ex = (await act.Should().ThrowAsync<DuplicateProgramException>()).Which;
        ex.RepositoryId.Should().Be(RepoId);
        ex.Name.Should().Be("seam-1");
    }

    [Fact]
    public async Task CreateProgramAsync_Should_Throw_HttpRequestException_On_400()
    {
        // Control: 400 is the generic path, distinct from the typed 404/409 mappings.
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "A program 'name' is required.");

        var act = () => Api(handler).CreateProgramAsync(RepoId, "");

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Message.Should().Contain("A program 'name' is required.");
    }

    [Fact]
    public async Task CreateProgramAsync_Should_Throw_HttpRequestException_On_422()
    {
        // Control: 422 (invalid name) is the generic path, NOT the 409 duplicate type.
        var handler = new RecordingHandler(HttpStatusCode.UnprocessableEntity, "Program name contains invalid characters.");

        var act = () => Api(handler).CreateProgramAsync(RepoId, "bad/name");

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ex.Message.Should().Contain("invalid characters");
    }

    [Fact]
    public async Task CreateProgramAsync_Should_Throw_On_A_Null_Body_Rather_Than_Return_Empty_Guid()
    {
        // Fail fast rather than hand back Guid.Empty as if a program were created.
        var handler = new RecordingHandler(HttpStatusCode.OK, "null");
        var act = () => Api(handler).CreateProgramAsync(RepoId, "seam-1");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
