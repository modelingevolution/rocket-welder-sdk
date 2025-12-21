using System.Drawing;
using RocketWelder.SDK.Protocols;
using Xunit;

// Use aliases to avoid conflict with RocketWelder.SDK types
using ProtocolSegmentationFrame = RocketWelder.SDK.Protocols.SegmentationFrame;
using ProtocolSegmentationInstance = RocketWelder.SDK.Protocols.SegmentationInstance;
using ProtocolKeypoint = RocketWelder.SDK.Protocols.Keypoint;
using ProtocolKeypointsFrame = RocketWelder.SDK.Protocols.KeypointsFrame;

namespace RocketWelder.SDK.Tests.BinaryProtocols;

/// <summary>
/// TDD tests to validate BinaryProtocol API design for round-trip testing.
///
/// GOAL: Enable cross-platform round-trip testing:
/// - SDK (Linux container) encodes with SegmentationResultWriter/KeyPointsWriter
/// - BinaryProtocol (WASM-compatible) can decode the bytes
/// - Assert the decoded values match what was encoded
///
/// NEW ABSTRACTIONS NEEDED:
/// - BinaryFrameWriter (symmetric to BinaryFrameReader)
/// - SegmentationProtocol.Read/Write (pure protocol, no transport)
/// - KeypointsProtocol.Read/Write (pure protocol, no transport)
/// - Data structures: SegmentationFrame, SegmentationInstance, KeypointsFrame, Keypoint
/// </summary>
public class DesignAlignmentTests
{
    #region BinaryFrameWriter Tests

    [Fact]
    public void BinaryFrameWriter_WritePrimitives_ReadBack()
    {
        Span<byte> buffer = stackalloc byte[32];
        var writer = new BinaryFrameWriter(buffer);

        writer.WriteUInt64LE(42);
        writer.WriteVarint(1920);
        writer.WriteVarint(1080);
        writer.WriteByte(0x01);

        var reader = new BinaryFrameReader(writer.WrittenSpan);
        Assert.Equal(42UL, reader.ReadUInt64LE());
        Assert.Equal(1920U, reader.ReadVarint());
        Assert.Equal(1080U, reader.ReadVarint());
        Assert.Equal(0x01, reader.ReadByte());
    }

    [Fact]
    public void BinaryFrameWriter_ZigZagVarint_SignedValues()
    {
        Span<byte> buffer = stackalloc byte[32];
        var writer = new BinaryFrameWriter(buffer);

        writer.WriteZigZagVarint(100);   // positive
        writer.WriteZigZagVarint(-50);   // negative
        writer.WriteZigZagVarint(0);     // zero

        var reader = new BinaryFrameReader(writer.WrittenSpan);
        Assert.Equal(100, reader.ReadZigZagVarint());
        Assert.Equal(-50, reader.ReadZigZagVarint());
        Assert.Equal(0, reader.ReadZigZagVarint());
    }

    #endregion

    #region SegmentationProtocol Tests

    [Fact]
    public void SegmentationProtocol_WriteRead_RoundTrip()
    {
        // Create frame with instances
        var frame = new ProtocolSegmentationFrame(
            frameId: 42,
            width: 1920,
            height: 1080,
            instances: new[]
            {
                new ProtocolSegmentationInstance(
                    classId: 0,
                    instanceId: 1,
                    points: new Point[] { new(100, 100), new(200, 100), new(150, 200) }
                ),
                new ProtocolSegmentationInstance(
                    classId: 1,
                    instanceId: 0,
                    points: new Point[] { new(300, 300), new(400, 350) }
                )
            }
        );

        // Write
        Span<byte> buffer = stackalloc byte[512];
        int written = SegmentationProtocol.Write(buffer, frame);

        // Read back
        var decoded = SegmentationProtocol.Read(buffer[..written]);

        // Assert round-trip
        Assert.Equal(frame.FrameId, decoded.FrameId);
        Assert.Equal(frame.Width, decoded.Width);
        Assert.Equal(frame.Height, decoded.Height);
        Assert.Equal(frame.Instances.Length, decoded.Instances.Length);

        for (int i = 0; i < frame.Instances.Length; i++)
        {
            Assert.Equal(frame.Instances[i].ClassId, decoded.Instances[i].ClassId);
            Assert.Equal(frame.Instances[i].InstanceId, decoded.Instances[i].InstanceId);
            Assert.Equal(frame.Instances[i].Points.Length, decoded.Instances[i].Points.Length);

            for (int j = 0; j < frame.Instances[i].Points.Length; j++)
            {
                Assert.Equal(frame.Instances[i].Points[j], decoded.Instances[i].Points[j]);
            }
        }
    }

    [Fact]
    public void SegmentationProtocol_WriteInstance_DeltaEncoding()
    {
        Span<byte> buffer = stackalloc byte[64];
        var points = new Point[] { new(100, 100), new(200, 150), new(150, 200) };

        int written = SegmentationProtocol.WriteInstance(buffer, classId: 0, instanceId: 1, points);

        // Verify structure manually
        var reader = new BinaryFrameReader(buffer[..written]);
        Assert.Equal(0, reader.ReadByte());   // classId
        Assert.Equal(1, reader.ReadByte());   // instanceId
        Assert.Equal(3U, reader.ReadVarint()); // pointCount

        // First point is absolute (zigzag)
        Assert.Equal(100, reader.ReadZigZagVarint());
        Assert.Equal(100, reader.ReadZigZagVarint());

        // Second point is delta from first: (200-100, 150-100) = (100, 50)
        Assert.Equal(100, reader.ReadZigZagVarint());
        Assert.Equal(50, reader.ReadZigZagVarint());

        // Third point is delta from second: (150-200, 200-150) = (-50, 50)
        Assert.Equal(-50, reader.ReadZigZagVarint());
        Assert.Equal(50, reader.ReadZigZagVarint());
    }

    #endregion

    #region KeypointsProtocol Tests

    [Fact]
    public void KeypointsProtocol_MasterFrame_RoundTrip()
    {
        var keypoints = new ProtocolKeypoint[]
        {
            new(id: 0, x: 100, y: 200, confidence: 9500),
            new(id: 1, x: 80, y: 180, confidence: 8500)
        };

        Span<byte> buffer = stackalloc byte[256];
        int written = KeypointsProtocol.WriteMasterFrame(buffer, frameId: 1, keypoints);

        var decoded = KeypointsProtocol.Read(buffer[..written]);

        Assert.Equal(1UL, decoded.FrameId);
        Assert.True(decoded.IsMasterFrame);
        Assert.Equal(keypoints.Length, decoded.Keypoints.Length);

        for (int i = 0; i < keypoints.Length; i++)
        {
            Assert.Equal(keypoints[i].Id, decoded.Keypoints[i].Id);
            Assert.Equal(keypoints[i].Position, decoded.Keypoints[i].Position);
            Assert.Equal(keypoints[i].Confidence, decoded.Keypoints[i].Confidence);
        }
    }

    [Fact]
    public void KeypointsProtocol_DeltaFrame_RoundTrip()
    {
        var previous = new ProtocolKeypoint[]
        {
            new(id: 0, x: 100, y: 200, confidence: 9500)
        };
        var current = new ProtocolKeypoint[]
        {
            new(id: 0, x: 102, y: 201, confidence: 9500)
        };

        Span<byte> buffer = stackalloc byte[64];
        int written = KeypointsProtocol.WriteDeltaFrame(buffer, frameId: 2, current, previous);

        var decoded = KeypointsProtocol.ReadWithPreviousState(buffer[..written], previous);

        Assert.Equal(2UL, decoded.FrameId);
        Assert.False(decoded.IsMasterFrame);
        Assert.Single(decoded.Keypoints);
        Assert.Equal(102, decoded.Keypoints[0].Position.X);
        Assert.Equal(201, decoded.Keypoints[0].Position.Y);
        Assert.Equal(9500, decoded.Keypoints[0].Confidence);
    }

    #endregion

    #region Round-Trip Integration Tests

    /// <summary>
    /// This test simulates the ACTUAL use case:
    /// 1. SDK encodes using the same logic as SegmentationResultWriter
    /// 2. BinaryProtocol decodes using SegmentationProtocol.Read()
    /// 3. Assert values match
    ///
    /// NOTE: Full round-trip testing with ICanvas.DrawPolygon verification
    /// is done in rocket-welder2 using NSubstitute mocks.
    /// </summary>
    [Fact]
    public void SDK_Encoding_BinaryProtocol_Decoding_RoundTrip()
    {
        // Simulate what SDK's SegmentationResultWriter does
        Span<byte> buffer = stackalloc byte[256];
        var writer = new BinaryFrameWriter(buffer);

        // Write header (same as SDK)
        ulong frameId = 42;
        uint width = 1920;
        uint height = 1080;
        writer.WriteUInt64LE(frameId);
        writer.WriteVarint(width);
        writer.WriteVarint(height);

        // Write instance (same as SDK)
        byte classId = 0;
        byte instanceId = 1;
        Point[] points = { new(100, 100), new(200, 100), new(150, 200) };

        writer.WriteByte(classId);
        writer.WriteByte(instanceId);
        writer.WriteVarint((uint)points.Length);

        // Delta encoding (same as SDK)
        int prevX = 0, prevY = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (i == 0)
            {
                writer.WriteZigZagVarint(points[i].X);
                writer.WriteZigZagVarint(points[i].Y);
            }
            else
            {
                writer.WriteZigZagVarint(points[i].X - prevX);
                writer.WriteZigZagVarint(points[i].Y - prevY);
            }
            prevX = points[i].X;
            prevY = points[i].Y;
        }

        // Now decode using BinaryProtocol
        var decoded = SegmentationProtocol.Read(writer.WrittenSpan);

        // Assert round-trip matches
        Assert.Equal(frameId, decoded.FrameId);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Single(decoded.Instances);
        Assert.Equal(classId, decoded.Instances[0].ClassId);
        Assert.Equal(instanceId, decoded.Instances[0].InstanceId);
        Assert.Equal(3, decoded.Instances[0].Points.Length);
        Assert.Equal(new Point(100, 100), decoded.Instances[0].Points[0]);
        Assert.Equal(new Point(200, 100), decoded.Instances[0].Points[1]);
        Assert.Equal(new Point(150, 200), decoded.Instances[0].Points[2]);
    }

    #endregion
}
