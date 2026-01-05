using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using RocketWelder.SDK.Protocols;
using Xunit;

using DeltaKeyPointsFrame = RocketWelder.SDK.Protocols.DeltaFrame<RocketWelder.SDK.Protocols.KeyPoint>;

namespace RocketWelder.SDK.Tests;

public class KeyPointsProtocolTests
{
    /// <summary>
    /// Helper to find keypoint by ID in a span.
    /// </summary>
    private static KeyPoint FindKeyPointById(ReadOnlySpan<KeyPoint> items, int id)
    {
        foreach (var kp in items)
            if (kp.Id == id)
                return kp;
        throw new InvalidOperationException($"KeyPoint with Id {id} not found");
    }

    /// <summary>
    /// Helper to check if a span contains a keypoint with the given ID.
    /// </summary>
    private static bool ContainsKeyPointById(ReadOnlySpan<KeyPoint> items, int id)
    {
        foreach (var kp in items)
            if (kp.Id == id)
                return true;
        return false;
    }

    /// <summary>
    /// Helper to read all frames from a stream using the streaming API.
    /// Returns DeltaFrame&lt;KeyPoint&gt; which includes IsDelta metadata.
    /// </summary>
    private async Task<List<DeltaKeyPointsFrame>> ReadAllFramesAsync(Stream stream)
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

    [Fact]
    public async Task SingleFrame_RoundTrip_PreservesData()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var storage = new KeyPointsSink(stream, leaveOpen: true);

        var expectedKeyPoints = new[]
        {
            (id: 0, point: new Point(100, 200), confidence: 0.95f),
            (id: 1, point: new Point(120, 190), confidence: 0.92f),
            (id: 2, point: new Point(80, 190), confidence: 0.88f),
            (id: 3, point: new Point(150, 300), confidence: 1.0f),
            (id: 4, point: new Point(50, 300), confidence: 0.75f)
        };

        // Act - Write
        using (var writer = storage.CreateWriter(frameId: 1))
        {
            foreach (var (id, point, confidence) in expectedKeyPoints)
            {
                writer.Append(id, point, confidence);
            }
        }

        // Act - Read
        var frames = await ReadAllFramesAsync(stream);

        // Assert
        Assert.Single(frames);
        var frame = frames[0];
        Assert.Equal(1ul, frame.FrameId);
        Assert.False(frame.IsDelta);
        Assert.Equal(5, frame.Items.Length);

        foreach (var (id, expectedPoint, expectedConfidence) in expectedKeyPoints)
        {
            var kp = FindKeyPointById(frame.Items.Span, id);
            Assert.Equal(expectedPoint.X, kp.Position.X);
            Assert.Equal(expectedPoint.Y, kp.Position.Y);
            Assert.Equal(expectedConfidence, kp.Confidence, precision: 4);
        }
    }

    [Fact]
    public async Task MultipleFrames_WithMasterDelta_RoundTrip()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var storage = new KeyPointsSink(stream, masterFrameInterval: 2, leaveOpen: true);

        // Frame 1 - Master
        var frame1 = new[]
        {
            (id: 0, point: new Point(100, 200), confidence: 0.95f),
            (id: 1, point: new Point(120, 190), confidence: 0.92f)
        };

        // Frame 2 - Delta (small changes)
        var frame2 = new[]
        {
            (id: 0, point: new Point(101, 201), confidence: 0.94f),
            (id: 1, point: new Point(121, 191), confidence: 0.93f)
        };

        // Frame 3 - Master (interval hit)
        var frame3 = new[]
        {
            (id: 0, point: new Point(105, 205), confidence: 0.96f),
            (id: 1, point: new Point(125, 195), confidence: 0.91f)
        };

        // Act - Write
        using (var writer1 = storage.CreateWriter(frameId: 0))
        {
            foreach (var (id, point, confidence) in frame1)
                writer1.Append(id, point, confidence);
        }

        using (var writer2 = storage.CreateWriter(frameId: 1))
        {
            foreach (var (id, point, confidence) in frame2)
                writer2.Append(id, point, confidence);
        }

        using (var writer3 = storage.CreateWriter(frameId: 2))
        {
            foreach (var (id, point, confidence) in frame3)
                writer3.Append(id, point, confidence);
        }

        // Act - Read
        var frames = await ReadAllFramesAsync(stream);

        // Assert
        Assert.Equal(3, frames.Count);

        // Verify Frame 1 (master)
        Assert.Equal(0ul, frames[0].FrameId);
        Assert.False(frames[0].IsDelta);
        var actualFrame1 = FindKeyPointById(frames[0].Items.Span, 0);
        Assert.Equal(frame1[0].point.X, actualFrame1.Position.X);
        Assert.Equal(frame1[0].point.Y, actualFrame1.Position.Y);
        Assert.Equal(frame1[0].confidence, actualFrame1.Confidence, precision: 4);

        // Verify Frame 2 (delta decoded correctly)
        Assert.Equal(1ul, frames[1].FrameId);
        Assert.True(frames[1].IsDelta);
        var actualFrame2 = FindKeyPointById(frames[1].Items.Span, 0);
        Assert.Equal(frame2[0].point.X, actualFrame2.Position.X);
        Assert.Equal(frame2[0].point.Y, actualFrame2.Position.Y);
        Assert.Equal(frame2[0].confidence, actualFrame2.Confidence, precision: 4);

        // Verify Frame 3 (master)
        Assert.Equal(2ul, frames[2].FrameId);
        Assert.False(frames[2].IsDelta);
        var actualFrame3 = FindKeyPointById(frames[2].Items.Span, 0);
        Assert.Equal(frame3[0].point.X, actualFrame3.Position.X);
        Assert.Equal(frame3[0].point.Y, actualFrame3.Position.Y);
        Assert.Equal(frame3[0].confidence, actualFrame3.Confidence, precision: 4);
    }

    [Fact]
    public async Task StreamingApi_ReturnsFramesAsTheyArrive()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var storage = new KeyPointsSink(stream, leaveOpen: true);

        // Write 3 frames with nose (keypointId=0) moving
        for (ulong frameId = 0; frameId < 3; frameId++)
        {
            using var writer = storage.CreateWriter(frameId);
            writer.Append(keypointId: 0, x: (int)(100 + frameId * 10), y: (int)(200 + frameId * 5), confidence: 0.95f);
            writer.Append(keypointId: 1, x: 150, y: 250, confidence: 0.90f); // Static point
        }

        // Act - Read using streaming API
        var frames = await ReadAllFramesAsync(stream);

        // Assert
        Assert.Equal(3, frames.Count);

        // Verify trajectory - nose moving
        Assert.Equal(100, FindKeyPointById(frames[0].Items.Span, 0).Position.X);
        Assert.Equal(200, FindKeyPointById(frames[0].Items.Span, 0).Position.Y);
        Assert.Equal(110, FindKeyPointById(frames[1].Items.Span, 0).Position.X);
        Assert.Equal(205, FindKeyPointById(frames[1].Items.Span, 0).Position.Y);
        Assert.Equal(120, FindKeyPointById(frames[2].Items.Span, 0).Position.X);
        Assert.Equal(210, FindKeyPointById(frames[2].Items.Span, 0).Position.Y);
    }

    [Fact]
    public async Task KeyPoint_HasCorrectProperties()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var storage = new KeyPointsSink(stream, leaveOpen: true);

        using (var writer = storage.CreateWriter(frameId: 10))
        {
            writer.Append(keypointId: 0, x: 100, y: 200, confidence: 0.95f);
            writer.Append(keypointId: 1, x: 120, y: 190, confidence: 0.92f);
        }

        // Act
        var frames = await ReadAllFramesAsync(stream);

        // Assert
        Assert.Single(frames);
        var frame = frames[0];
        Assert.Equal(10ul, frame.FrameId);
        Assert.Equal(2, frame.Items.Length);

        var kp0 = FindKeyPointById(frame.Items.Span, 0);
        Assert.Equal(100, kp0.Position.X);
        Assert.Equal(200, kp0.Position.Y);
        Assert.Equal(0.95f, kp0.Confidence, precision: 4);
        Assert.Equal(new Point(100, 200), kp0.Position);

        var kp1 = FindKeyPointById(frame.Items.Span, 1);
        Assert.Equal(120, kp1.Position.X);
        Assert.Equal(190, kp1.Position.Y);
        Assert.Equal(0.92f, kp1.Confidence, precision: 4);
    }

    [Fact]
    public async Task ConfidenceEncoding_PreservesFloatPrecision()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var storage = new KeyPointsSink(stream, leaveOpen: true);

        var testConfidences = new[] { 0.0f, 0.5f, 0.9999f, 1.0f, 0.1234f };

        using (var writer = storage.CreateWriter(frameId: 1))
        {
            for (int i = 0; i < testConfidences.Length; i++)
            {
                writer.Append(keypointId: i, x: 100, y: 200, confidence: testConfidences[i]);
            }
        }

        // Act
        var frames = await ReadAllFramesAsync(stream);

        // Assert - Check precision (should be within 0.0001 due to ushort encoding)
        Assert.Single(frames);
        var frame = frames[0];

        for (int i = 0; i < testConfidences.Length; i++)
        {
            var kp = FindKeyPointById(frame.Items.Span, i);
            Assert.Equal(testConfidences[i], kp.Confidence, precision: 4);
        }
    }

    [Fact]
    public async Task VariableKeyPointCount_HandledCorrectly()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var storage = new KeyPointsSink(stream, leaveOpen: true);

        // Frame 1 - 2 keypoints
        using (var writer1 = storage.CreateWriter(frameId: 0))
        {
            writer1.Append(keypointId: 0, x: 100, y: 200, confidence: 0.95f);
            writer1.Append(keypointId: 1, x: 120, y: 190, confidence: 0.92f);
        }

        // Frame 2 - 4 keypoints (2 new ones appeared)
        using (var writer2 = storage.CreateWriter(frameId: 1))
        {
            writer2.Append(keypointId: 0, x: 101, y: 201, confidence: 0.94f);
            writer2.Append(keypointId: 1, x: 121, y: 191, confidence: 0.93f);
            writer2.Append(keypointId: 3, x: 150, y: 300, confidence: 0.88f);
            writer2.Append(keypointId: 4, x: 50, y: 300, confidence: 0.85f);
        }

        // Frame 3 - 1 keypoint (most disappeared)
        using (var writer3 = storage.CreateWriter(frameId: 2))
        {
            writer3.Append(keypointId: 0, x: 102, y: 202, confidence: 0.96f);
        }

        // Act
        var frames = await ReadAllFramesAsync(stream);

        // Assert
        Assert.Equal(3, frames.Count);
        Assert.Equal(2, frames[0].Items.Length);
        Assert.Equal(4, frames[1].Items.Length);
        Assert.Equal(1, frames[2].Items.Length);

        // Verify keypoint 3 only exists in frame 2
        Assert.False(ContainsKeyPointById(frames[0].Items.Span, 3));
        Assert.True(ContainsKeyPointById(frames[1].Items.Span, 3));
        Assert.False(ContainsKeyPointById(frames[2].Items.Span, 3));
    }

    [Fact]
    public async Task LargeCoordinates_PreservesPrecision()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var storage = new KeyPointsSink(stream, leaveOpen: true);

        var testPoints = new[]
        {
            new Point(0, 0),
            new Point(-1000, -2000),
            new Point(int.MaxValue / 2, int.MaxValue / 2),
            new Point(int.MinValue / 2, int.MinValue / 2)
        };

        using (var writer = storage.CreateWriter(frameId: 1))
        {
            for (int i = 0; i < testPoints.Length; i++)
            {
                writer.Append(keypointId: i, testPoints[i], confidence: 1.0f);
            }
        }

        // Act
        var frames = await ReadAllFramesAsync(stream);

        // Assert
        Assert.Single(frames);
        var frame = frames[0];

        for (int i = 0; i < testPoints.Length; i++)
        {
            var kp = FindKeyPointById(frame.Items.Span, i);
            Assert.Equal(testPoints[i], kp.Position);
        }
    }

    [Fact]
    public async Task AsyncWriter_RoundTrip_PreservesData()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var storage = new KeyPointsSink(stream, leaveOpen: true);

        var expectedKeyPoints = new[]
        {
            (id: 0, point: new Point(100, 200), confidence: 0.95f),
            (id: 1, point: new Point(120, 190), confidence: 0.92f),
            (id: 2, point: new Point(80, 190), confidence: 0.88f)
        };

        // Act - Write using async methods
        await using (var writer = storage.CreateWriter(frameId: 1))
        {
            foreach (var (id, point, confidence) in expectedKeyPoints)
            {
                await writer.AppendAsync(id, point, confidence);
            }
        }

        // Act - Read
        var frames = await ReadAllFramesAsync(stream);

        // Assert
        Assert.Single(frames);
        var frame = frames[0];
        Assert.Equal(1ul, frame.FrameId);
        Assert.Equal(3, frame.Items.Length);

        foreach (var (id, expectedPoint, expectedConfidence) in expectedKeyPoints)
        {
            var kp = FindKeyPointById(frame.Items.Span, id);
            Assert.Equal(expectedPoint, kp.Position);
            Assert.Equal(expectedConfidence, kp.Confidence, precision: 4);
        }
    }

    [Fact]
    public async Task Sink_CreatesMultipleWriters()
    {
        // Arrange
        using var stream = new MemoryStream();
        var frameSink = new StreamFrameSink(stream, leaveOpen: true);
        using var sink = new KeyPointsSink(frameSink, ownsSink: true);

        // Act - Write multiple frames via sink
        using (var writer1 = sink.CreateWriter(1))
        {
            writer1.Append(0, 100, 200, 0.95f);
        }

        using (var writer2 = sink.CreateWriter(2))
        {
            writer2.Append(0, 110, 210, 0.96f);
        }

        // Assert - Read back
        var frames = await ReadAllFramesAsync(stream);

        Assert.Equal(2, frames.Count);
        Assert.Equal(1ul, frames[0].FrameId);
        Assert.Equal(2ul, frames[1].FrameId);
    }

    [Fact]
    public async Task Source_StreamsFramesAsyncEnumerable()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var storage = new KeyPointsSink(stream, leaveOpen: true);

        // Write 3 frames
        for (int i = 0; i < 3; i++)
        {
            using var writer = storage.CreateWriter((ulong)i);
            writer.Append(0, i * 10, i * 20, 0.95f);
        }

        // Act - Stream frames
        stream.Position = 0;
        var source = new StreamFrameSource(stream, leaveOpen: true);
        var kpSource = new KeyPointsSource(source);

        int frameCount = 0;
        await foreach (var frame in kpSource.ReadFramesAsync())
        {
            Assert.Equal((ulong)frameCount, frame.FrameId);
            frameCount++;
        }

        // Assert
        Assert.Equal(3, frameCount);
    }
}
