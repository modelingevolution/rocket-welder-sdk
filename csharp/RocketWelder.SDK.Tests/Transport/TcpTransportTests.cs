using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using Xunit;
using Xunit.Abstractions;

namespace RocketWelder.SDK.Tests.Transport;

/// <summary>
/// Tests for TCP transport.
/// </summary>
public class TcpTransportTests
{
    private readonly ITestOutputHelper _output;

    public TcpTransportTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TcpTransport_RoundTrip_PreservesData()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var testData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        byte[]? receivedData = null;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var source = new TcpFrameSource(stream);

            var frame = await source.ReadFrameAsync();
            receivedData = frame.ToArray();

            // Echo back
            using var sink = new TcpFrameSink(stream, leaveOpen: true);
            sink.WriteFrame(frame.Span);
            await sink.FlushAsync();
        });

        // Act - Client
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, port);
        using var clientStream = tcpClient.GetStream();

        using (var sink = new TcpFrameSink(clientStream, leaveOpen: true))
        {
            sink.WriteFrame(testData);
            await sink.FlushAsync();
        }

        // Read response
        using var responseSource = new TcpFrameSource(clientStream);
        var response = await responseSource.ReadFrameAsync();

        await serverTask;
        listener.Stop();

        // Assert
        Assert.NotNull(receivedData);
        Assert.Equal(testData, receivedData);
        Assert.Equal(testData, response.ToArray());

        _output.WriteLine($"Successfully sent and received {testData.Length} bytes via TCP");
    }

    [Fact]
    public async Task TcpTransport_MultipleFrames_PreservesOrder()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var frames = new[]
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6, 7 },
            new byte[] { 8 },
            new byte[] { 9, 10, 11, 12, 13 }
        };

        var receivedFrames = new List<byte[]>();

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var source = new TcpFrameSource(stream);

            for (int i = 0; i < frames.Length; i++)
            {
                var frame = await source.ReadFrameAsync();
                receivedFrames.Add(frame.ToArray());
            }
        });

        // Act
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, port);
        using var clientStream = tcpClient.GetStream();
        using var sink = new TcpFrameSink(clientStream);

        foreach (var frame in frames)
        {
            sink.WriteFrame(frame);
        }
        await sink.FlushAsync();

        await serverTask;
        listener.Stop();

        // Assert
        Assert.Equal(frames.Length, receivedFrames.Count);
        for (int i = 0; i < frames.Length; i++)
        {
            Assert.Equal(frames[i], receivedFrames[i]);
        }

        _output.WriteLine($"Successfully sent and received {frames.Length} frames via TCP");
    }

    [Fact]
    public async Task TcpTransport_LargeFrame_HandledCorrectly()
    {
        // Arrange - Large frame (1MB)
        var largeFrame = new byte[1024 * 1024];
        new Random(42).NextBytes(largeFrame);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        byte[]? receivedData = null;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var source = new TcpFrameSource(stream);

            var frame = await source.ReadFrameAsync();
            receivedData = frame.ToArray();
        });

        // Act
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, port);
        using var clientStream = tcpClient.GetStream();
        using var sink = new TcpFrameSink(clientStream);

        await sink.WriteFrameAsync(largeFrame);
        await sink.FlushAsync();

        await serverTask;
        listener.Stop();

        // Assert
        Assert.NotNull(receivedData);
        Assert.Equal(largeFrame.Length, receivedData.Length);
        Assert.Equal(largeFrame, receivedData);

        _output.WriteLine($"Successfully transferred {largeFrame.Length / 1024}KB via TCP");
    }

    [Fact]
    public async Task TcpTransport_TcpClientConstructor_Works()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var testData = new byte[] { 42, 43, 44 };
        byte[]? receivedData = null;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            using var source = new TcpFrameSource(serverClient); // TcpClient constructor
            var frame = await source.ReadFrameAsync();
            receivedData = frame.ToArray();
        });

        // Act - Use TcpClient constructor
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, port);

        using var sink = new TcpFrameSink(tcpClient); // TcpClient constructor
        sink.WriteFrame(testData);
        await sink.FlushAsync();

        await serverTask;
        listener.Stop();

        // Assert
        Assert.Equal(testData, receivedData);
        _output.WriteLine("TcpClient constructor works correctly");
    }

    [Fact]
    public async Task TcpTransport_ConnectionClosed_ReturnsEmpty()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            // Close immediately
        });

        // Act
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, port);

        await serverTask; // Wait for server to close

        using var clientStream = tcpClient.GetStream();
        using var source = new TcpFrameSource(clientStream);

        var frame = await source.ReadFrameAsync();

        listener.Stop();

        // Assert
        Assert.True(frame.IsEmpty);
        Assert.False(source.HasMoreFrames);

        _output.WriteLine("Connection close returns empty frame as expected");
    }

    [Fact]
    public async Task TcpTransport_4ByteLengthPrefix_CorrectFormat()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var testData = new byte[] { 0xAA, 0xBB, 0xCC };
        byte[]? rawBytes = null;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            using var stream = serverClient.GetStream();

            // Read raw bytes to verify format
            rawBytes = new byte[7]; // 4 byte length + 3 byte data
            int totalRead = 0;
            while (totalRead < 7)
            {
                int read = await stream.ReadAsync(rawBytes, totalRead, 7 - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
        });

        // Act
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, port);
        using var sink = new TcpFrameSink(tcpClient);
        sink.WriteFrame(testData);
        await sink.FlushAsync();

        await serverTask;
        listener.Stop();

        // Assert - Verify 4-byte little-endian length prefix
        Assert.NotNull(rawBytes);
        Assert.Equal(0x03, rawBytes[0]); // Length = 3 (little-endian)
        Assert.Equal(0x00, rawBytes[1]);
        Assert.Equal(0x00, rawBytes[2]);
        Assert.Equal(0x00, rawBytes[3]);
        Assert.Equal(0xAA, rawBytes[4]);
        Assert.Equal(0xBB, rawBytes[5]);
        Assert.Equal(0xCC, rawBytes[6]);

        _output.WriteLine($"TCP frame format: {BitConverter.ToString(rawBytes)}");
    }

    [Fact]
    public async Task TcpTransport_Bidirectional_Communication()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var clientMessages = new[] { new byte[] { 1, 2 }, new byte[] { 3, 4 } };
        var serverMessages = new[] { new byte[] { 10, 20 }, new byte[] { 30, 40 } };
        var receivedByServer = new List<byte[]>();
        var receivedByClient = new List<byte[]>();

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            using var stream = serverClient.GetStream();
            using var source = new TcpFrameSource(stream, leaveOpen: true);
            using var sink = new TcpFrameSink(stream, leaveOpen: true);

            // Receive first, then respond
            for (int i = 0; i < clientMessages.Length; i++)
            {
                var frame = await source.ReadFrameAsync();
                receivedByServer.Add(frame.ToArray());

                sink.WriteFrame(serverMessages[i]);
                await sink.FlushAsync();
            }
        });

        // Act
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, port);
        using var clientStream = tcpClient.GetStream();
        using var clientSource = new TcpFrameSource(clientStream, leaveOpen: true);
        using var clientSink = new TcpFrameSink(clientStream, leaveOpen: true);

        for (int i = 0; i < clientMessages.Length; i++)
        {
            clientSink.WriteFrame(clientMessages[i]);
            await clientSink.FlushAsync();

            var response = await clientSource.ReadFrameAsync();
            receivedByClient.Add(response.ToArray());
        }

        await serverTask;
        listener.Stop();

        // Assert
        for (int i = 0; i < clientMessages.Length; i++)
        {
            Assert.Equal(clientMessages[i], receivedByServer[i]);
            Assert.Equal(serverMessages[i], receivedByClient[i]);
        }

        _output.WriteLine("Bidirectional communication works correctly");
    }
}
