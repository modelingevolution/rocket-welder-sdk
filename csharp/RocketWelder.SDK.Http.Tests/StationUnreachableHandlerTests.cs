using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Programs;
using RocketWelder.SDK.Http.Robots;

namespace RocketWelder.SDK.Http.Tests;

/// <summary>
/// epic-128 feature-001 design.md §4, ADR-13 — <see cref="StationUnreachableHandler"/> as registered by
/// <see cref="ServiceCollectionExtensions.AddRocketWelderClient"/> (F001-U-013, F001-U-014).
/// </summary>
public class StationUnreachableHandlerTests
{
    private static readonly Guid ProgramId = Guid.Parse("6f1c0000-0000-0000-0000-000000000001");

    // ---- F001-U-013: no listener → StationUnreachableException, nothing was done ----

    [Fact]
    public async Task AnyCall_ToABaseAddressWithNoListener_ThrowsStationUnreachable_NothingWasDone_F001_U_013()
    {
        var baseAddress = new Uri($"http://127.0.0.1:{FreeLoopbackPort()}/");
        using var services = Provider(http => http.BaseAddress = baseAddress);
        var client = services.GetRequiredService<IRocketWelderClient>();

        var act = () => client.Programs.GetPointsAsync(ProgramId);

        var ex = (await act.Should().ThrowAsync<StationUnreachableException>()).Which;
        ex.Message.Should().StartWith($"Station unreachable at {baseAddress}").And.EndWith("Nothing was done.");
        ex.Station.Should().Be(baseAddress);
        ex.MayHaveBeenApplied.Should().BeFalse();
        ex.HttpRequestError.Should().Be(HttpRequestError.ConnectionError);
    }

    [Fact]
    public async Task AnyCall_ToAnUnresolvableHost_ThrowsStationUnreachable_NothingWasDone_F001_U_013()
    {
        var baseAddress = new Uri("http://station.invalid:5000/");
        using var services = Provider(http => http.BaseAddress = baseAddress);
        var client = services.GetRequiredService<IRocketWelderClient>();

        var act = () => client.Repositories.ListAsync();

        var ex = (await act.Should().ThrowAsync<StationUnreachableException>()).Which;
        ex.Message.Should().Contain("http://station.invalid:5000/").And.EndWith("Nothing was done.");
    }

    [Fact]
    public async Task ARegistrationMadeTwice_MapsTheFailureOnce_WithTheLastConfiguration_F001_U_013()
    {
        var baseAddress = new Uri($"http://127.0.0.1:{FreeLoopbackPort()}/");
        var services = new ServiceCollection();
        services.AddRocketWelderClient(http => http.BaseAddress = new Uri("http://first.invalid/"));
        services.AddRocketWelderClient(http => http.BaseAddress = baseAddress);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IRocketWelderClient>().Programs.GetPointsAsync(ProgramId);

        var ex = (await act.Should().ThrowAsync<StationUnreachableException>()).Which;
        ex.Station.Should().Be(baseAddress);
        ex.InnerException.Should().NotBeOfType<StationUnreachableException>();
        ex.Message.Split("Nothing was done.").Should().HaveCount(2, "the failure is wrapped once: {0}", ex.Message);
    }

    // ---- F001-U-014: no answer within the timeout → "may or may not have been applied" ----

    [Fact]
    public async Task AnyCall_ThatIsNeverAnswered_WithinA100msTimeout_SaysItMayOrMayNotHaveBeenApplied_F001_U_014()
    {
        var baseAddress = new Uri("http://h:5000/");
        using var services = Provider(http =>
        {
            http.BaseAddress = baseAddress;
            http.Timeout = TimeSpan.FromMilliseconds(100);
        }, primary: new NeverAnswers());
        var client = services.GetRequiredService<IRocketWelderClient>();

        var act = () => client.Programs.PutPointAsync(ProgramId, "p1",
            new PointWrite("p1") { Pose = new RobotPose(1, 2, 3, 0, 0, 0) }, new ProgramEtag("sha256:v1"));

        var ex = (await act.Should().ThrowAsync<StationUnreachableException>()).Which;
        ex.Message.Should().Contain("http://h:5000/").And.Contain("may or may not have been applied");
        ex.Message.Should().Contain("0.1 s");
        ex.MayHaveBeenApplied.Should().BeTrue();
    }

    [Fact]
    public async Task ACallCancelledByTheCaller_StaysAnOperationCanceledException_F001_U_014()
    {
        using var services = Provider(http => http.BaseAddress = new Uri("http://h:5000/"), primary: new NeverAnswers());
        var client = services.GetRequiredService<IRocketWelderClient>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => client.Programs.GetPointsAsync(ProgramId, cts.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.Should().NotBeOfType<StationUnreachableException>();
    }

    [Fact]
    public async Task AnAnsweredCall_PassesThrough_F001_U_014()
    {
        using var services = Provider(http => http.BaseAddress = new Uri("http://h:5000/"),
            primary: new Answers("""[{"id":"4e2a0000-0000-0000-0000-000000000003","name":"seams","status":"Cloned","currentBranch":"master"}]"""));
        var client = services.GetRequiredService<IRocketWelderClient>();

        var repositories = await client.Repositories.ListAsync();

        repositories.Should().ContainSingle();
    }

    private static ServiceProvider Provider(Action<HttpClient> configure, HttpMessageHandler? primary = null)
    {
        var services = new ServiceCollection();
        services.AddRocketWelderClient(configure);
        if (primary is not null)
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => primary));
        return services.BuildServiceProvider();
    }

    // A loopback port that nothing listens on: bind an ephemeral port, then release it.
    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class Answers(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
