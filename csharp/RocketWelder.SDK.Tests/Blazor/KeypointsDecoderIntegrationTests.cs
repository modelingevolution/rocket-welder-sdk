using BlazorBlaze.VectorGraphics;
using RocketWelder.SDK.Blazor;
using RocketWelder.SDK.Protocols;
using Xunit;

using ProtocolKeypoint = RocketWelder.SDK.Protocols.Keypoint;

namespace RocketWelder.SDK.Tests.Blazor;

/// <summary>
/// Integration tests for KeypointsDecoder.
/// Tests the complete round-trip: Encode → Decode → Render
/// Uses concrete mock classes since NSubstitute can't handle ReadOnlySpan parameters.
/// </summary>
public class KeypointsDecoderIntegrationTests
{
    [Fact]
    public void Decode_MasterFrame_DrawsCrossesForAllKeypoints()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new KeypointsDecoder(stage);
        decoder.CrossSize = 6;

        var keypoints = new ProtocolKeypoint[]
        {
            new(id: 0, x: 100, y: 200, confidence: 9500),
            new(id: 1, x: 150, y: 250, confidence: 8500),
            new(id: 2, x: 200, y: 300, confidence: 7500)
        };

        Span<byte> buffer = stackalloc byte[256];
        int written = KeypointsProtocol.WriteMasterFrame(buffer, frameId: 42, keypoints);

        // Act
        var result = decoder.Decode(buffer[..written]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(42UL, result.FrameId);
        // Each keypoint draws 2 lines (cross), so 3 keypoints = 6 lines
        Assert.Equal(6, canvas.LineCalls.Count);

        // Verify first keypoint cross position (horizontal line: x-6 to x+6, y)
        Assert.Equal(100 - 6, canvas.LineCalls[0].X1);
        Assert.Equal(200, canvas.LineCalls[0].Y1);
        Assert.Equal(100 + 6, canvas.LineCalls[0].X2);
        Assert.Equal(200, canvas.LineCalls[0].Y2);
    }

    [Fact]
    public void Decode_DeltaFrame_AppliesDeltasFromPreviousMaster()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new KeypointsDecoder(stage);
        decoder.CrossSize = 6;

        // First send master frame
        var masterKeypoints = new ProtocolKeypoint[]
        {
            new(id: 0, x: 100, y: 200, confidence: 9500)
        };

        Span<byte> masterBuffer = stackalloc byte[128];
        int masterWritten = KeypointsProtocol.WriteMasterFrame(masterBuffer, frameId: 1, masterKeypoints);
        decoder.Decode(masterBuffer[..masterWritten]);

        canvas.Clear();

        // Then send delta frame with small movements
        var deltaKeypoints = new ProtocolKeypoint[]
        {
            new(id: 0, x: 102, y: 201, confidence: 9500)  // Moved +2, +1
        };

        Span<byte> deltaBuffer = stackalloc byte[128];
        int deltaWritten = KeypointsProtocol.WriteDeltaFrame(deltaBuffer, frameId: 2, deltaKeypoints, masterKeypoints);

        // Act
        var result = decoder.Decode(deltaBuffer[..deltaWritten]);

        // Assert: 1 keypoint = 2 lines (cross)
        Assert.True(result.Success);
        Assert.Equal(2, canvas.LineCalls.Count);
        // Verify horizontal line position (x-6 to x+6, y)
        Assert.Equal(102 - 6, canvas.LineCalls[0].X1);
        Assert.Equal(201, canvas.LineCalls[0].Y1);
        Assert.Equal(102 + 6, canvas.LineCalls[0].X2);
        Assert.Equal(201, canvas.LineCalls[0].Y2);
    }

    [Fact]
    public void Decode_CrossSizeProperty_AffectsCrossSize()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new KeypointsDecoder(stage);
        decoder.CrossSize = 10; // Custom size

        var keypoints = new ProtocolKeypoint[]
        {
            new(id: 0, x: 100, y: 100, confidence: 10000)
        };

        Span<byte> buffer = stackalloc byte[128];
        int written = KeypointsProtocol.WriteMasterFrame(buffer, frameId: 1, keypoints);

        // Act
        decoder.Decode(buffer[..written]);

        // Assert: Cross should span from x-10 to x+10
        Assert.Equal(2, canvas.LineCalls.Count);
        // Horizontal line
        Assert.Equal(100 - 10, canvas.LineCalls[0].X1);
        Assert.Equal(100 + 10, canvas.LineCalls[0].X2);
        // Vertical line
        Assert.Equal(100 - 10, canvas.LineCalls[1].Y1);
        Assert.Equal(100 + 10, canvas.LineCalls[1].Y2);
    }

    [Fact]
    public void Decode_SmallCrossSize_DrawsSmallerCross()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new KeypointsDecoder(stage);
        decoder.CrossSize = 3; // Small cross

        var keypoints = new ProtocolKeypoint[]
        {
            new(id: 0, x: 100, y: 100, confidence: 500)
        };

        Span<byte> buffer = stackalloc byte[128];
        int written = KeypointsProtocol.WriteMasterFrame(buffer, frameId: 1, keypoints);

        // Act
        decoder.Decode(buffer[..written]);

        // Assert: Cross should span from x-3 to x+3
        Assert.Equal(2, canvas.LineCalls.Count);
        // Horizontal line
        Assert.Equal(100 - 3, canvas.LineCalls[0].X1);
        Assert.Equal(100 + 3, canvas.LineCalls[0].X2);
    }

    [Fact]
    public void Decode_DifferentKeypointIds_UsesDifferentColors()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var color0 = new RgbColor(255, 100, 100);  // Red
        var color1 = new RgbColor(100, 255, 100);  // Green
        var decoder = new KeypointsDecoder(stage);
        decoder.Brushes.Add(0, color0);
        decoder.Brushes.Add(1, color1);

        var keypoints = new ProtocolKeypoint[]
        {
            new(id: 0, x: 100, y: 100, confidence: 9000),
            new(id: 1, x: 200, y: 200, confidence: 9000)
        };

        Span<byte> buffer = stackalloc byte[256];
        int written = KeypointsProtocol.WriteMasterFrame(buffer, frameId: 1, keypoints);

        // Act
        decoder.Decode(buffer[..written]);

        // Assert: Different keypoint IDs use different colors from Brushes
        // KeypointsDecoder draws crosses (2 lines per keypoint), so 4 lines total
        Assert.Equal(4, canvas.LineCalls.Count);
        // First keypoint (id=0): first 2 lines use color0
        Assert.Equal(color0, canvas.LineCalls[0].Stroke);
        Assert.Equal(color0, canvas.LineCalls[1].Stroke);
        // Second keypoint (id=1): next 2 lines use color1
        Assert.Equal(color1, canvas.LineCalls[2].Stroke);
        Assert.Equal(color1, canvas.LineCalls[3].Stroke);
    }

    [Fact]
    public void Decode_EmptyMasterFrame_ReturnsOkWithNoDrawCalls()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new KeypointsDecoder(stage);

        Span<byte> buffer = stackalloc byte[32];
        int written = KeypointsProtocol.WriteMasterFrame(buffer, frameId: 1, ReadOnlySpan<ProtocolKeypoint>.Empty);

        // Act
        var result = decoder.Decode(buffer[..written]);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(canvas.LineCalls);
    }

    [Fact]
    public void Decode_TooShortData_ReturnsNeedMoreData()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new KeypointsDecoder(stage);

        // Only 5 bytes - less than minimum header
        ReadOnlySpan<byte> shortData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act
        var result = decoder.Decode(shortData);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void Decode_CallsStageLifecycleMethods()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new KeypointsDecoder(stage);

        var keypoints = new ProtocolKeypoint[]
        {
            new(id: 0, x: 100, y: 100, confidence: 9000)
        };

        Span<byte> buffer = stackalloc byte[128];
        int written = KeypointsProtocol.WriteMasterFrame(buffer, frameId: 42, keypoints);

        // Act
        decoder.Decode(buffer[..written]);

        // Assert: Stage lifecycle methods called
        Assert.Equal(42UL, stage.LastFrameId);
        Assert.Equal(1, stage.FrameStartCount);
        Assert.Equal(1, stage.FrameEndCount);
        Assert.Contains((byte)0, stage.ClearedLayers);
    }

    [Fact]
    public void Decode_ManyKeypoints_HandlesCorrectly()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new KeypointsDecoder(stage);

        // Create 50 keypoints
        var keypoints = new ProtocolKeypoint[50];
        for (int i = 0; i < 50; i++)
        {
            keypoints[i] = new ProtocolKeypoint(id: i, x: i * 10, y: i * 5, confidence: 8000);
        }

        var buffer = new byte[2048];
        int written = KeypointsProtocol.WriteMasterFrame(buffer, frameId: 1, keypoints);

        // Act
        var result = decoder.Decode(buffer.AsSpan(0, written));

        // Assert: 50 keypoints * 2 lines per cross = 100 lines
        Assert.True(result.Success);
        Assert.Equal(100, canvas.LineCalls.Count);
    }

    [Fact]
    public void Decode_VerifiesAllKeypointPositions()
    {
        // Arrange
        var canvas = new MockCanvas();
        var stage = new MockStage(canvas);
        var decoder = new KeypointsDecoder(stage);
        decoder.CrossSize = 6;

        var keypoints = new ProtocolKeypoint[]
        {
            new(id: 0, x: 10, y: 20, confidence: 9000),
            new(id: 1, x: 30, y: 40, confidence: 8000),
            new(id: 2, x: 50, y: 60, confidence: 7000)
        };

        Span<byte> buffer = stackalloc byte[256];
        int written = KeypointsProtocol.WriteMasterFrame(buffer, frameId: 1, keypoints);

        // Act
        decoder.Decode(buffer[..written]);

        // Assert: 3 keypoints * 2 lines = 6 lines
        Assert.Equal(6, canvas.LineCalls.Count);
        // First keypoint (10,20): horizontal line at Y=20, vertical at X=10
        Assert.Equal(10 - 6, canvas.LineCalls[0].X1); // horizontal line start
        Assert.Equal(20, canvas.LineCalls[0].Y1);
        Assert.Equal(10, canvas.LineCalls[1].X1); // vertical line
        Assert.Equal(20 - 6, canvas.LineCalls[1].Y1);
        // Second keypoint (30,40)
        Assert.Equal(30 - 6, canvas.LineCalls[2].X1);
        Assert.Equal(40, canvas.LineCalls[2].Y1);
        // Third keypoint (50,60)
        Assert.Equal(50 - 6, canvas.LineCalls[4].X1);
        Assert.Equal(60, canvas.LineCalls[4].Y1);
    }
}
