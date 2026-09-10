using System.Net;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Devices;
using RocketWelder.SDK.Http.Tests.Programs;

namespace RocketWelder.SDK.Http.Tests.Devices;

public class DevicesApiTests
{
    private const string DeviceId = "fairino/55555555-5555-5555-5555-555555555555";

    private static IDevicesApi Api(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Devices;

    [Fact]
    public async Task DeleteAsync_Should_DELETE_The_Device_By_Id()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent);

        await Api(handler).DeleteAsync(DeviceId);

        handler.Method.Should().Be(HttpMethod.Delete);
        handler.RequestUri!.AbsolutePath.Should().Be($"/api/devices/{DeviceId}");
    }

    [Fact]
    public async Task DeleteAsync_Should_Throw_On_404_With_The_Server_Body()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, $"No device with id '{DeviceId}'.");

        var act = () => Api(handler).DeleteAsync(DeviceId);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Message.Should().Contain("No device with id");
    }

    [Fact]
    public async Task DeleteAsync_Should_Throw_On_400_Invalid_Id()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "Invalid device id 'bogus'.");

        var act = () => Api(handler).DeleteAsync("bogus");

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
