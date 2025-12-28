using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using RocketWelder.SDK.Protocols;
using Xunit;

using DeltaKeyPointsFrame = RocketWelder.SDK.Protocols.DeltaFrame<RocketWelder.SDK.Protocols.Keypoint>;

namespace RocketWelder.SDK.Tests;

/// <summary>
/// Comprehensive round-trip tests for all transport types.
/// Tests that data written via one transport can be correctly read back.
/// </summary>
public class TransportRoundTripTests
{
    /// <summary>
    /// Helper to find keypoint by ID in a span.
    /// </summary>
    private static Keypoint FindKeypointById(ReadOnlySpan<Keypoint> items, int id)
    {
        foreach (var kp in items)
            if (kp.Id == id)
                return kp;
        throw new InvalidOperationException($"Keypoint with Id {id} not found");
    }

    /// <summary>
    /// Helper to read all KeyPoints frames from a stream.
    /// Returns DeltaFrame&lt;KeyPoint&gt; which includes IsDelta metadata.
    /// </summary>
    private async Task<List<DeltaKeyPointsFrame>> ReadAllKeyPointsFramesAsync(Stream stream)
    {
        stream.Position = 0;
        var source = new StreamFrameSource(stream, leaveOpen: true);
        var kpSource = new KeyPointsSource(source);

        var frames = new List<DeltaKeyPointsFrame>();
        await foreach (var frame in kpSource.ReadFramesAsync())
        {
            frames.Add(frame);
        }

        return frames;
    }

    #region Stream Transport Tests

    [Fact]
    public async Task StreamTransport_RoundTrip_PreservesData()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var frameSink = new StreamFrameSink(stream, leaveOpen: true);
        using var sink = new KeyPointsSink(frameSink, ownsSink: true);

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

        // Act - Read via KeyPointsSource
        var frames = await ReadAllKeyPointsFramesAsync(stream);

        // Assert
        Assert.Single(frames);
        var frame = frames[0];
        Assert.Equal(1ul, frame.FrameId);
        Assert.Equal(3, frame.Items.Length);

        foreach (var (id, expectedPoint, expectedConfidence) in expectedKeypoints)
        {
            var kp = FindKeypointById(frame.Items.Span, id);
            Assert.Equal(expectedPoint, kp.Position);
            Assert.Equal(expectedConfidence, kp.Confidence, precision: 4);
        }
    }

    [Fact]
    public void StreamTransport_ConvenienceConstructor_WorksCorrectly()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var sink = new KeyPointsSink(stream); // Convenience constructor

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
            using var sink = new KeyPointsSink(frameSink, ownsSink: true);
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

        // Verify the echoed frame - parse using KeyPointsSource
        using var memStream = new MemoryStream();
        // Write with length-prefix framing so StreamFrameSource can read it
        using (var tempFrameSink = new StreamFrameSink(memStream, leaveOpen: true))
        {
            tempFrameSink.WriteFrame(responseFrame.Span);
        }

        var frames = await ReadAllKeyPointsFramesAsync(memStream);
        Assert.Single(frames);
        Assert.Equal(1ul, frames[0].FrameId);
        Assert.Equal(2, frames[0].Items.Length);
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

        using var sink = new KeyPointsSink(frameSink, ownsSink: true);

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

        using var sink = new KeyPointsSink(frameSink, ownsSink: true);

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
        using var streamSink = new KeyPointsSink(memStream, leaveOpen: true);

        using (var writer = streamSink.CreateWriter(frameId: 0))
        {
            writer.Append(0, 100, 200, 0.95f);
            writer.Append(1, 120, 190, 0.92f);
        }

        memStream.Position = 0;

        // Read frame data (with length prefix)
        using var readSource = new StreamFrameSource(memStream, leaveOpen: true);
        var frameData = await readSource.ReadFrameAsync();
        Assert.NotNull(frameData);

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

        tcpSink.WriteFrame(frameData.Span);
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
                using var sink = new KeyPointsSink(writeStream);
                using var writer = sink.CreateWriter(frameId: 1);
                foreach (var (id, point, confidence) in expectedKeypoints)
                {
                    writer.Append(id, point, confidence);
                }
            }

            // Act - Read from file using streaming API
            using var readStream = File.OpenRead(tempFile);
            var source = new StreamFrameSource(readStream, leaveOpen: false);
            var kpSource = new KeyPointsSource(source);

            var frames = new List<DeltaKeyPointsFrame>();
            await foreach (var frame in kpSource.ReadFramesAsync())
            {
                frames.Add(frame);
            }

            // Assert
            Assert.Single(frames);
            var readFrame = frames[0];
            Assert.Equal(1ul, readFrame.FrameId);
            Assert.Equal(3, readFrame.Items.Length);

            foreach (var (id, expectedPoint, expectedConfidence) in expectedKeypoints)
            {
                var kp = FindKeypointById(readFrame.Items.Span, id);
                Assert.Equal(expectedPoint, kp.Position);
                Assert.Equal(expectedConfidence, kp.Confidence, precision: 4);
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
