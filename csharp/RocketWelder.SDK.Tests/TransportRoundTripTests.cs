using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using Xunit;

namespace RocketWelder.SDK.Tests;

/// <summary>
/// Comprehensive round-trip tests for all transport types.
/// Tests that data written via one transport can be correctly read back.
/// </summary>
public class TransportRoundTripTests
{
    private const string TestDefinitionJson = @"{
  ""version"": ""1.0"",
  ""compute_module_name"": ""TestModel"",
  ""points"": {
    ""nose"": 0,
    ""left_eye"": 1,
    ""right_eye"": 2
  }
}";

    #region Stream Transport Tests

    [Fact]
    public async Task StreamTransport_RoundTrip_PreservesData()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var frameSink = new StreamFrameSink(stream, leaveOpen: true);
        var sink = new KeyPointsSink(frameSink);

        var expectedKeypoints = new[]
        {
            (id: 0, point: new Point(100, 200), confidence: 0.95f),
            (id: 1, point: new Point(120, 190), confidence: 0.92f),
            (id: 2, point: new Point(80, 190), confidence: 0.88f)
        };

        // Act - Write via IFrameSink
        using (var writer = sink.CreateWriter(frameId: 1))
        {
            foreach (var (id, point, confidence) in expectedKeypoints)
            {
                writer.Append(id, point, confidence);
            }
        }

        // Act - Read via IFrameSource
        stream.Position = 0;
        using var frameSource = new StreamFrameSource(stream);
        var series = await sink.Read(TestDefinitionJson, frameSource);

        // Assert
        Assert.True(series.ContainsFrame(1));
        var frame = series.GetFrame(1)!;
        Assert.Equal(3, frame.Count);

        foreach (var (id, expectedPoint, expectedConfidence) in expectedKeypoints)
        {
            Assert.True(frame.ContainsKey(id));
            var result = frame[id];
            Assert.Equal(expectedPoint, result.point);
            Assert.Equal(expectedConfidence, result.confidence, precision: 4);
        }
    }

    [Fact]
    public void StreamTransport_ConvenienceConstructor_WorksCorrectly()
    {
        // Arrange
        using var stream = new MemoryStream();
        var sink = new KeyPointsSink(stream); // Convenience constructor

        // Act - Write
        using (var writer = sink.CreateWriter(frameId: 0))
        {
            writer.Append(0, 100, 200, 0.95f);
        }

        // Assert - Verify data was written
        Assert.True(stream.Length > 0);
    }

    #endregion

    #region TCP Transport Tests

    [Fact]
    public async Task TcpTransport_RoundTrip_PreservesData()
    {
        // Arrange - Start TCP server
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            using var serverStream = serverClient.GetStream();
            using var frameSource = new TcpFrameSource(serverStream);

            // Read frame from client
            var frameData = await frameSource.ReadFrameAsync();
            Assert.NotNull(frameData);
            Assert.True(frameData.Length > 0);

            // Send it back
            using var frameSink = new TcpFrameSink(serverStream);
            frameSink.WriteFrame(frameData.Span);
            await frameSink.FlushAsync();
        });

        // Act - Connect and write
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var clientStream = client.GetStream();

        var expectedKeypoints = new[]
        {
            (id: 0, point: new Point(100, 200), confidence: 0.95f),
            (id: 1, point: new Point(120, 190), confidence: 0.92f)
        };

        // Write via TCP
        using (var frameSink = new TcpFrameSink(clientStream, leaveOpen: true))
        {
            var sink = new KeyPointsSink(frameSink);
            using var writer = sink.CreateWriter(frameId: 1);
            foreach (var (id, point, confidence) in expectedKeypoints)
            {
                writer.Append(id, point, confidence);
            }
        }

        // Read response via TCP
        using var responseSource = new TcpFrameSource(clientStream);
        var responseFrame = await responseSource.ReadFrameAsync();
        Assert.NotNull(responseFrame);

        await serverTask;
        listener.Stop();

        // Verify the echoed frame
        using var memStream = new MemoryStream(responseFrame.ToArray());
        var readSink = new KeyPointsSink(memStream);
        using var memFrameSource = new StreamFrameSource(memStream);
        var series = await readSink.Read(TestDefinitionJson, memFrameSource);

        Assert.True(series.ContainsFrame(1));
        var frame = series.GetFrame(1)!;
        Assert.Equal(2, frame.Count);
    }

    [Fact]
    public async Task TcpTransport_MultipleFrames_RoundTrip()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var receivedFrames = 0;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            using var serverStream = serverClient.GetStream();
            using var frameSource = new TcpFrameSource(serverStream);

            // Read 3 frames
            for (int i = 0; i < 3; i++)
            {
                var frame = await frameSource.ReadFrameAsync();
                Assert.NotNull(frame);
                Assert.True(frame.Length > 0);
                Interlocked.Increment(ref receivedFrames);
            }
        });

        // Act - Send 3 frames
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var clientStream = client.GetStream();
        using var frameSink = new TcpFrameSink(clientStream);

        var sink = new KeyPointsSink(frameSink);

        for (ulong frameId = 0; frameId < 3; frameId++)
        {
            using var writer = sink.CreateWriter(frameId);
            writer.Append(0, (int)(100 + frameId * 10), 200, 0.95f);
        }

        await serverTask;
        listener.Stop();

        // Assert
        Assert.Equal(3, receivedFrames);
    }

    [Fact]
    public async Task TcpTransport_LengthPrefix_HandlesLargeFrames()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            using var serverStream = serverClient.GetStream();
            using var frameSource = new TcpFrameSource(serverStream);

            var frame = await frameSource.ReadFrameAsync();
            Assert.NotNull(frame);
            Assert.True(frame.Length > 1000); // Should be large
        });

        // Act - Send large frame with many keypoints
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var clientStream = client.GetStream();
        using var frameSink = new TcpFrameSink(clientStream);

        var sink = new KeyPointsSink(frameSink);

        // Add 100 keypoints to create a large frame
        using (var writer = sink.CreateWriter(frameId: 0))
        {
            for (int i = 0; i < 100; i++)
            {
                writer.Append(i, i * 10, i * 20, 0.95f);
            }
        } // Writer disposed here, frame is sent

        await serverTask;
        listener.Stop();
    }

    #endregion

    #region Cross-Transport Compatibility Tests

    [Fact]
    public async Task StreamToMemory_ThenToTcp_PreservesData()
    {
        // Test that data written via stream can be sent over TCP
        // Arrange - Write to memory stream
        using var memStream = new MemoryStream();
        var streamSink = new KeyPointsSink(memStream);

        using (var writer = streamSink.CreateWriter(frameId: 0))
        {
            writer.Append(0, 100, 200, 0.95f);
            writer.Append(1, 120, 190, 0.92f);
        }

        memStream.Position = 0;
        var frameData = memStream.ToArray();

        // Act - Send same data over TCP
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            using var serverStream = serverClient.GetStream();
            using var frameSource = new TcpFrameSource(serverStream);

            var receivedFrame = await frameSource.ReadFrameAsync();
            Assert.NotNull(receivedFrame);
            Assert.Equal(frameData.Length, receivedFrame.Length);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var clientStream = client.GetStream();
        using var tcpSink = new TcpFrameSink(clientStream);

        tcpSink.WriteFrame(frameData);
        await tcpSink.FlushAsync();

        await serverTask;
        listener.Stop();
    }

    #endregion

    #region File System Round-Trip Tests

    [Fact]
    public async Task FileSystem_RoundTrip_PreservesData()
    {
        // Test writing to actual file and reading back
        var tempFile = Path.GetTempFileName();

        try
        {
            var expectedKeypoints = new[]
            {
                (id: 0, point: new Point(100, 200), confidence: 0.95f),
                (id: 1, point: new Point(120, 190), confidence: 0.92f),
                (id: 2, point: new Point(80, 190), confidence: 0.88f)
            };

            // Act - Write to file
            using (var writeStream = File.Open(tempFile, FileMode.Create))
            {
                var sink = new KeyPointsSink(writeStream);
                using var writer = sink.CreateWriter(frameId: 1);
                foreach (var (id, point, confidence) in expectedKeypoints)
                {
                    writer.Append(id, point, confidence);
                }
            }

            // Act - Read from file
            using (var readStream = File.OpenRead(tempFile))
            {
                var sink = new KeyPointsSink(readStream);
                using var fileFrameSource = new StreamFrameSource(readStream);
                var series = await sink.Read(TestDefinitionJson, fileFrameSource);

                // Assert
                Assert.True(series.ContainsFrame(1));
                var frame = series.GetFrame(1)!;
                Assert.Equal(3, frame.Count);

                foreach (var (id, expectedPoint, expectedConfidence) in expectedKeypoints)
                {
                    var result = frame[id];
                    Assert.Equal(expectedPoint, result.point);
                    Assert.Equal(expectedConfidence, result.confidence, precision: 4);
                }
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #endregion
}
