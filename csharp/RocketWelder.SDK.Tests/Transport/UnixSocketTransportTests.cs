using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using Xunit;
using Xunit.Abstractions;

namespace RocketWelder.SDK.Tests.Transport;

/// <summary>
/// Tests for Unix Domain Socket transport.
/// These tests require Linux or macOS (Unix sockets not fully supported on Windows).
/// </summary>
public class UnixSocketTransportTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _socketPath;

    public UnixSocketTransportTests(ITestOutputHelper output)
    {
        _output = output;
        _socketPath = Path.Combine(Path.GetTempPath(), $"rocket-welder-test-{Guid.NewGuid():N}.sock");
    }

    public void Dispose()
    {
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public async Task UnixSocket_RoundTrip_PreservesData()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        // Arrange - Start Unix socket server
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        listener.Listen(1);

        var testData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        byte[]? receivedData = null;

        var serverTask = Task.Run(async () =>
        {
            using var serverSocket = await listener.AcceptAsync();
            using var source = new UnixSocketFrameSource(serverSocket);

            var frame = await source.ReadFrameAsync();
            receivedData = frame.ToArray();

            // Echo back
            using var sink = new UnixSocketFrameSink(serverSocket, leaveOpen: true);
            sink.WriteFrame(frame.Span);
        });

        // Act - Client connects and sends
        using var clientSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await clientSocket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath));

        using var clientSink = new UnixSocketFrameSink(clientSocket, leaveOpen: true);
        clientSink.WriteFrame(testData);

        // Read response
        using var clientSource = new UnixSocketFrameSource(clientSocket);
        var response = await clientSource.ReadFrameAsync();

        await serverTask;

        // Assert
        Assert.NotNull(receivedData);
        Assert.Equal(testData, receivedData);
        Assert.Equal(testData, response.ToArray());

        _output.WriteLine($"Successfully sent and received {testData.Length} bytes via Unix socket");
    }

    [Fact]
    public async Task UnixSocket_MultipleFrames_PreservesOrder()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        // Arrange
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        listener.Listen(1);

        var frames = new List<byte[]>
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6, 7 },
            new byte[] { 8 },
            new byte[] { 9, 10, 11, 12, 13 }
        };

        var receivedFrames = new List<byte[]>();

        var serverTask = Task.Run(async () =>
        {
            using var serverSocket = await listener.AcceptAsync();
            using var source = new UnixSocketFrameSource(serverSocket);

            for (int i = 0; i < frames.Count; i++)
            {
                var frame = await source.ReadFrameAsync();
                receivedFrames.Add(frame.ToArray());
            }
        });

        // Act - Send multiple frames
        using var clientSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await clientSocket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath));

        using var sink = new UnixSocketFrameSink(clientSocket);
        foreach (var frame in frames)
        {
            sink.WriteFrame(frame);
        }

        await serverTask;

        // Assert
        Assert.Equal(frames.Count, receivedFrames.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            Assert.Equal(frames[i], receivedFrames[i]);
        }

        _output.WriteLine($"Successfully sent and received {frames.Count} frames");
    }

    [Fact]
    public async Task UnixSocket_LargeFrame_HandledCorrectly()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        // Arrange - Large frame (1MB)
        var largeFrame = new byte[1024 * 1024];
        new Random(42).NextBytes(largeFrame);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        listener.Listen(1);

        byte[]? receivedData = null;

        var serverTask = Task.Run(async () =>
        {
            using var serverSocket = await listener.AcceptAsync();
            using var source = new UnixSocketFrameSource(serverSocket);
            var frame = await source.ReadFrameAsync();
            receivedData = frame.ToArray();
        });

        // Act
        using var clientSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await clientSocket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath));

        using var sink = new UnixSocketFrameSink(clientSocket);
        await sink.WriteFrameAsync(largeFrame);

        await serverTask;

        // Assert
        Assert.NotNull(receivedData);
        Assert.Equal(largeFrame.Length, receivedData.Length);
        Assert.Equal(largeFrame, receivedData);

        _output.WriteLine($"Successfully transferred {largeFrame.Length / 1024}KB frame via Unix socket");
    }

    [Fact]
    public async Task UnixSocket_StaticConnectMethods_Work()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        // Arrange
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        listener.Listen(1);

        var testData = new byte[] { 42, 43, 44 };

        var serverTask = Task.Run(async () =>
        {
            using var serverSocket = await listener.AcceptAsync();
            using var source = new UnixSocketFrameSource(serverSocket);
            return (await source.ReadFrameAsync()).ToArray();
        });

        // Act - Use static connect method
        using var sink = await UnixSocketFrameSink.ConnectAsync(_socketPath);
        sink.WriteFrame(testData);

        var result = await serverTask;

        // Assert
        Assert.Equal(testData, result);
        _output.WriteLine("Static ConnectAsync method works correctly");
    }

    [Fact]
    public void UnixSocket_NonUnixSocket_ThrowsArgumentException()
    {
        // Arrange
        using var tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new UnixSocketFrameSink(tcpSocket));
        Assert.Throws<ArgumentException>(() => new UnixSocketFrameSource(tcpSocket));
    }
}
