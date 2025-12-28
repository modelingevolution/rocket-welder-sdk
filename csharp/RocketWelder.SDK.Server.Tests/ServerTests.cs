using RocketWelder.SDK.Server;
using Xunit;

namespace RocketWelder.SDK.Server.Tests;

public class ServerTests
{
    [Fact]
    public void Server_CanBeInstantiated()
    {
        // Arrange & Act
        using var server = new Server("test-channel", 640, 480, "RGB");

        // Assert
        Assert.NotNull(server);
        Assert.Equal(0UL, server.FrameNumber);
        Assert.False(server.IsClientConnected);
    }

    [Fact]
    public void Server_WithCustomFormat_CanBeInstantiated()
    {
        // Arrange & Act
        using var server = new Server("test-channel", 1920, 1080, "BGR");

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public void Server_ThrowsOnNullChannelName()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Server(null!, 640, 480));
    }

    [Fact]
    public void Server_SendFrame_ThrowsIfNotStarted()
    {
        // Arrange
        using var server = new Server("test-channel", 640, 480);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => server.SendFrame(null!));
        Assert.Contains("not started", ex.Message);
    }

    [Fact]
    public void Server_TryReadKeypointsFrame_ThrowsIfNotConnected()
    {
        // Arrange
        using var server = new Server("test-channel", 640, 480);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => server.TryReadKeypointsFrame(TimeSpan.FromSeconds(1)));
        Assert.Contains("Not connected to keypoints socket", ex.Message);
    }

    [Fact]
    public void Server_TryReadSegmentationFrame_ThrowsIfNotConnected()
    {
        // Arrange
        using var server = new Server("test-channel", 640, 480);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => server.TryReadSegmentationFrame(TimeSpan.FromSeconds(1)));
        Assert.Contains("Not connected to segmentation socket", ex.Message);
    }

    [Fact]
    public void Server_CanBeDisposedMultipleTimes()
    {
        // Arrange
        var server = new Server("test-channel", 640, 480);

        // Act & Assert (should not throw)
        server.Dispose();
        server.Dispose();
    }

    [Fact]
    public void Server_Stop_CanBeCalledMultipleTimes()
    {
        // Arrange
        using var server = new Server("test-channel", 640, 480);

        // Act & Assert (should not throw)
        server.Stop();
        server.Stop();
    }
}
