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

        // Arrange - SDK creates server, consumer connects
        var testData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        byte[]? receivedData = null;

        // Producer (SDK) - binds and waits for consumer, then writes frames
        var serverTask = Task.Run(() =>
        {
            // Bind creates server, waits for client connection
            using var sink = UnixSocketFrameSink.Bind(_socketPath);
            sink.WriteFrame(testData);
        });

        // Give server time to start listening
        await Task.Delay(100);

        // Consumer (rocket-welder2) - connects and reads frames
        using var source = await UnixSocketFrameSource.ConnectAsync(
            _socketPath,
            timeout: TimeSpan.FromSeconds(5),
            retry: true);

        var frame = await source.ReadFrameAsync();
        receivedData = frame.ToArray();

        await serverTask;

        // Assert
        Assert.NotNull(receivedData);
        Assert.Equal(testData, receivedData);

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

    #region Connection Retry Tests

    [Fact]
    public async Task UnixSocketSource_ConnectAsync_WithRetry_SucceedsWhenServerStartsLater()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        // Start connection attempt before server is ready
        var connectTask = UnixSocketFrameSource.ConnectAsync(
            _socketPath,
            timeout: TimeSpan.FromSeconds(5),
            retry: true);

        // Wait a bit then start server
        await Task.Delay(500);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        listener.Listen(1);

        _output.WriteLine("Server started after 500ms delay");

        // Connection should succeed with retry
        using var source = await connectTask;
        Assert.NotNull(source);

        _output.WriteLine("Connection succeeded with retry");
    }

    [Fact]
    public async Task UnixSocketSink_ConnectAsync_WithRetry_SucceedsWhenServerStartsLater()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        // Start connection attempt before server is ready
        var connectTask = UnixSocketFrameSink.ConnectAsync(
            _socketPath,
            timeout: TimeSpan.FromSeconds(5),
            retry: true);

        // Wait a bit then start server
        await Task.Delay(500);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        listener.Listen(1);

        _output.WriteLine("Server started after 500ms delay");

        // Connection should succeed with retry
        using var sink = await connectTask;
        Assert.NotNull(sink);

        _output.WriteLine("Connection succeeded with retry");
    }

    [Fact]
    public async Task UnixSocketSource_ConnectAsync_WithoutRetry_FailsImmediately()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        // Try to connect without retry to non-existent socket
        var ex = await Assert.ThrowsAsync<SocketException>(async () =>
        {
            await UnixSocketFrameSource.ConnectAsync(
                _socketPath,
                timeout: TimeSpan.FromSeconds(5),
                retry: false);
        });

        _output.WriteLine($"Got expected SocketException: {ex.SocketErrorCode}");
    }

    [Fact]
    public async Task UnixSocketSource_ConnectAsync_TimesOut_WhenServerNeverStarts()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        var startTime = DateTime.UtcNow;

        // Try to connect with short timeout - server never starts
        var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await UnixSocketFrameSource.ConnectAsync(
                _socketPath,
                timeout: TimeSpan.FromSeconds(1),
                retry: true);
        });

        var elapsed = DateTime.UtcNow - startTime;

        _output.WriteLine($"Got expected TimeoutException after {elapsed.TotalSeconds:F2}s: {ex.Message}");
        Assert.True(elapsed >= TimeSpan.FromSeconds(0.9), "Should have waited close to timeout");
        Assert.True(elapsed < TimeSpan.FromSeconds(2), "Should not wait much longer than timeout");
    }

    [Fact]
    public async Task UnixSocketSource_ConnectAsync_CanBeCancelled()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        using var cts = new CancellationTokenSource();
        var startTime = DateTime.UtcNow;

        // Start connect then cancel after 300ms
        var connectTask = UnixSocketFrameSource.ConnectAsync(
            _socketPath,
            timeout: TimeSpan.FromSeconds(10),
            retry: true,
            cancellationToken: cts.Token);

        await Task.Delay(300);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await connectTask;
        });

        var elapsed = DateTime.UtcNow - startTime;
        _output.WriteLine($"Cancelled after {elapsed.TotalMilliseconds:F0}ms");
        Assert.True(elapsed < TimeSpan.FromSeconds(1), "Should have cancelled quickly");
    }

    [Fact]
    public async Task UnixSocket_ConnectAsync_WithRetry_WorksWithDataTransfer()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        byte[]? receivedData = null;

        // Start server with delay
        var serverTask = Task.Run(async () =>
        {
            await Task.Delay(300);

            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            listener.Listen(1);

            _output.WriteLine("Server listening");

            using var serverSocket = await listener.AcceptAsync();
            using var source = new UnixSocketFrameSource(serverSocket);

            var frame = await source.ReadFrameAsync();
            receivedData = frame.ToArray();
            _output.WriteLine($"Server received {receivedData.Length} bytes");
        });

        // Client connects with retry
        using var sink = await UnixSocketFrameSink.ConnectAsync(
            _socketPath,
            timeout: TimeSpan.FromSeconds(5),
            retry: true);

        _output.WriteLine("Client connected");

        sink.WriteFrame(testData);
        _output.WriteLine("Client sent data");

        await serverTask;

        Assert.Equal(testData, receivedData);
        _output.WriteLine("Data transfer successful with retry connect");
    }

    #endregion
}
