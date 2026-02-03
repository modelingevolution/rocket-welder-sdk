using System.Drawing;
using BlazorBlaze.VectorGraphics;
using RocketWelder.SDK.Blazor;
using RocketWelder.SDK.Protocols;
using SkiaSharp;
using Xunit;

// Use aliases to avoid conflict with RocketWelder.SDK types
using ProtocolSegmentationFrame = RocketWelder.SDK.Protocols.SegmentationFrame;
using ProtocolSegmentationInstance = RocketWelder.SDK.Protocols.SegmentationInstance;

namespace RocketWelder.SDK.Tests.Blazor;

/// <summary>
/// Integration tests for SegmentationDecoder.
/// Tests the complete round-trip: Encode → Decode → Render
/// Uses concrete mock classes since NSubstitute can't handle ReadOnlySpan parameters.
/// </summary>
public class SegmentationDecoderIntegrationTests
{
    [Fact]
    public void Decode_SinglePolygon_DrawsPolygonWithCorrectPoints()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new SegmentationDecoder(stage);

        // Create and encode a segmentation frame
        var frame = new ProtocolSegmentationFrame(
            FrameId: 42,
            Width: 1920,
            Height: 1080,
            Instances: new[]
            {
                new ProtocolSegmentationInstance(
                    ClassId: 0,
                    InstanceId: 1,
                    Confidence: (Confidence)0.95f,
                    Points: new Point[] { new(100, 100), new(200, 100), new(150, 200) }
                )
            });

        Span<byte> buffer = stackalloc byte[256];
        int written = SegmentationProtocol.Write(buffer, frame);

        // Act
        var result = decoder.Decode(buffer[..written]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(written, result.BytesConsumed);
        Assert.Equal(42UL, result.FrameId);

        // Verify DrawPolygon was called with correct points
        Assert.Single(canvas.PolygonCalls);
        var call = canvas.PolygonCalls[0];
        Assert.Equal(3, call.Points.Length);
        Assert.Equal(new SKPoint(100, 100), call.Points[0]);
        Assert.Equal(new SKPoint(200, 100), call.Points[1]);
        Assert.Equal(new SKPoint(150, 200), call.Points[2]);
    }

    [Fact]
    public void Decode_MultiplePolygons_DrawsAllPolygons()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new SegmentationDecoder(stage);

        var frame = new ProtocolSegmentationFrame(
            FrameId: 1,
            Width: 1920,
            Height: 1080,
            Instances: new[]
            {
                new ProtocolSegmentationInstance(0, 0, (Confidence)0.95f, new Point[] { new(10, 10), new(20, 10), new(15, 20) }),
                new ProtocolSegmentationInstance(1, 0, (Confidence)0.95f, new Point[] { new(100, 100), new(200, 100) }),
                new ProtocolSegmentationInstance(2, 0, (Confidence)0.95f, new Point[] { new(50, 50), new(60, 50), new(60, 60), new(50, 60) })
            });

        Span<byte> buffer = stackalloc byte[512];
        int written = SegmentationProtocol.Write(buffer, frame);

        // Act
        var result = decoder.Decode(buffer[..written]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, canvas.PolygonCalls.Count);
    }

    [Fact]
    public void Decode_DifferentClassIds_UsesBrushesForEachClass()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var color0 = new RgbColor(255, 100, 100);  // Red
        var color1 = new RgbColor(100, 255, 100);  // Green
        var decoder = new SegmentationDecoder(stage);
        decoder.Brushes.Add(0, color0);
        decoder.Brushes.Add(1, color1);

        var frame = new ProtocolSegmentationFrame(
            FrameId: 1,
            Width: 1920,
            Height: 1080,
            Instances: new[]
            {
                new ProtocolSegmentationInstance(0, 0, (Confidence)0.95f, new Point[] { new(10, 10), new(20, 10), new(15, 20) }),
                new ProtocolSegmentationInstance(1, 0, (Confidence)0.95f, new Point[] { new(100, 100), new(200, 100), new(150, 200) })
            });

        Span<byte> buffer = stackalloc byte[256];
        int written = SegmentationProtocol.Write(buffer, frame);

        // Act
        decoder.Decode(buffer[..written]);

        // Assert: Different class IDs use different colors from Brushes
        Assert.Equal(2, canvas.PolygonCalls.Count);
        Assert.Equal(color0, canvas.PolygonCalls[0].Stroke);
        Assert.Equal(color1, canvas.PolygonCalls[1].Stroke);
    }

    [Fact]
    public void Decode_EmptyFrame_ReturnsOkWithNoDrawCalls()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new SegmentationDecoder(stage);

        var frame = new ProtocolSegmentationFrame(
            FrameId: 1,
            Width: 1920,
            Height: 1080,
            Instances: Array.Empty<ProtocolSegmentationInstance>());

        Span<byte> buffer = stackalloc byte[32];
        int written = SegmentationProtocol.Write(buffer, frame);

        // Act
        var result = decoder.Decode(buffer[..written]);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(canvas.PolygonCalls);
    }

    [Fact]
    public void Decode_TooShortData_ReturnsNeedMoreData()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new SegmentationDecoder(stage);

        // Only 5 bytes - less than minimum header
        ReadOnlySpan<byte> shortData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act
        var result = decoder.Decode(shortData);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void Decode_LargePolygon_HandlesCorrectly()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new SegmentationDecoder(stage);

        // Create a polygon with many points
        var points = new Point[100];
        for (int i = 0; i < 100; i++)
        {
            points[i] = new Point(i * 10, i * 5);
        }

        var frame = new ProtocolSegmentationFrame(
            FrameId: 1,
            Width: 1920,
            Height: 1080,
            Instances: new[]
            {
                new ProtocolSegmentationInstance(0, 0, (Confidence)0.95f, points)
            });

        var buffer = new byte[2048];
        int written = SegmentationProtocol.Write(buffer, frame);

        // Act
        var result = decoder.Decode(buffer.AsSpan(0, written));

        // Assert
        Assert.True(result.Success);
        Assert.Single(canvas.PolygonCalls);
        Assert.Equal(100, canvas.PolygonCalls[0].Points.Length);
    }

    [Fact]
    public void Decode_CallsStageLifecycleMethods()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new SegmentationDecoder(stage);

        var frame = new ProtocolSegmentationFrame(
            FrameId: 42,
            Width: 1920,
            Height: 1080,
            Instances: new[]
            {
                new ProtocolSegmentationInstance(0, 0, (Confidence)0.95f, new Point[] { new(10, 10), new(20, 10) })
            });

        Span<byte> buffer = stackalloc byte[128];
        int written = SegmentationProtocol.Write(buffer, frame);

        // Act
        decoder.Decode(buffer[..written]);

        // Assert: Stage lifecycle methods called
        Assert.Equal(42UL, stage.LastFrameId);
        Assert.Equal(1, stage.FrameStartCount);
        Assert.Equal(1, stage.FrameEndCount);
        Assert.Contains((byte)0, stage.ClearedLayers);
    }

    [Fact]
    public void Decode_VerifiesPointCoordinates_AreCorrectAfterDeltaDecoding()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new SegmentationDecoder(stage);

        // Points with varying deltas to test delta encoding
        var frame = new ProtocolSegmentationFrame(
            FrameId: 1,
            Width: 1920,
            Height: 1080,
            Instances: new[]
            {
                new ProtocolSegmentationInstance(0, 0, (Confidence)0.95f, new Point[]
                {
                    new(100, 100),  // absolute
                    new(150, 120),  // delta: +50, +20
                    new(130, 180),  // delta: -20, +60
                    new(100, 100)   // delta: -30, -80 (back to start)
                })
            });

        Span<byte> buffer = stackalloc byte[256];
        int written = SegmentationProtocol.Write(buffer, frame);

        // Act
        var result = decoder.Decode(buffer[..written]);

        // Assert
        Assert.True(result.Success);
        Assert.Single(canvas.PolygonCalls);
        var points = canvas.PolygonCalls[0].Points;
        Assert.Equal(4, points.Length);
        Assert.Equal(new SKPoint(100, 100), points[0]);
        Assert.Equal(new SKPoint(150, 120), points[1]);
        Assert.Equal(new SKPoint(130, 180), points[2]);
        Assert.Equal(new SKPoint(100, 100), points[3]);
    }
}
