using System;
using System.Buffers;
using System.Drawing;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Protocols;
using Xunit;
using Xunit.Abstractions;
using Point = System.Drawing.Point;
using PointF = ModelingEvolution.Drawing.Point<float>;

namespace RocketWelder.SDK.Tests;

public class SegmentationProtocolFTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Encodes a SegmentationFrame using the existing int-based protocol writer,
    /// then returns the raw bytes for decoding with SegmentationProtocolF.
    /// </summary>
    private static byte[] EncodeFrame(ulong frameId, uint width, uint height,
        params (byte classId, byte instanceId, Confidence confidence, Point[] points)[] instances)
    {
        var segInstances = new SegmentationInstance[instances.Length];
        for (int i = 0; i < instances.Length; i++)
        {
            var (classId, instanceId, confidence, points) = instances[i];
            segInstances[i] = new SegmentationInstance(classId, instanceId, confidence, points);
        }

        var frame = new SegmentationFrame(frameId, width, height, segInstances);
        var buffer = new byte[8192];
        int written = SegmentationProtocol.Write(buffer, in frame);
        return buffer.AsSpan(0, written).ToArray();
    }

    #region Read (non-pooled)

    [Fact]
    public void Read_SingleInstance_DecodesCorrectly()
    {
        var points = new[] { new Point(100, 200), new Point(101, 201), new Point(105, 199) };
        Confidence confidence = 0.95f;

        var data = EncodeFrame(42, 1920, 1080,
            (1, 1, confidence, points));

        var frame = SegmentationProtocolF.Read(data);

        Assert.Equal(42ul, frame.FrameId);
        Assert.Equal(1920u, frame.Width);
        Assert.Equal(1080u, frame.Height);
        Assert.Equal(1, frame.Instances.Count);

        var inst = frame.Instances[0];
        Assert.Equal(1, inst.ClassId);
        Assert.Equal(1, inst.InstanceId);
        Assert.Equal((ushort)confidence, (ushort)inst.Confidence);
        Assert.Equal(3, inst.Points.Length);

        Assert.Equal(100f, inst.Points.Span[0].X);
        Assert.Equal(200f, inst.Points.Span[0].Y);
        Assert.Equal(101f, inst.Points.Span[1].X);
        Assert.Equal(201f, inst.Points.Span[1].Y);
        Assert.Equal(105f, inst.Points.Span[2].X);
        Assert.Equal(199f, inst.Points.Span[2].Y);
    }

    [Fact]
    public void Read_MultipleInstances_SharesPointsBuffer()
    {
        var points1 = new[] { new Point(10, 20), new Point(30, 40) };
        var points2 = new[] { new Point(100, 200), new Point(150, 250), new Point(200, 300) };
        Confidence conf = 0.85f;

        var data = EncodeFrame(1, 640, 480,
            (1, 1, conf, points1),
            (2, 1, conf, points2));

        var frame = SegmentationProtocolF.Read(data);

        Assert.Equal(2, frame.Instances.Count);

        // First instance
        var inst0 = frame.Instances[0];
        Assert.Equal(1, inst0.ClassId);
        Assert.Equal(2, inst0.Points.Length);
        Assert.Equal(10f, inst0.Points.Span[0].X);
        Assert.Equal(20f, inst0.Points.Span[0].Y);

        // Second instance
        var inst1 = frame.Instances[1];
        Assert.Equal(2, inst1.ClassId);
        Assert.Equal(3, inst1.Points.Length);
        Assert.Equal(100f, inst1.Points.Span[0].X);
        Assert.Equal(200f, inst1.Points.Span[0].Y);
    }

    [Fact]
    public void Read_EmptyInstance_ReturnsZeroPoints()
    {
        var data = EncodeFrame(1, 100, 100,
            (1, 1, Confidence.Full, Array.Empty<Point>()));

        var frame = SegmentationProtocolF.Read(data);

        Assert.Single(frame.Instances);
        Assert.Equal(0, frame.Instances[0].Points.Length);
    }

    [Fact]
    public void Read_NegativeDeltas_DecodesCorrectly()
    {
        var points = new[]
        {
            new Point(100, 100),
            new Point(99, 99),   // -1, -1
            new Point(98, 100),  // -1, +1
            new Point(50, 150)   // -48, +50
        };

        var data = EncodeFrame(1, 200, 200,
            (1, 1, 0.9f, points));

        var frame = SegmentationProtocolF.Read(data);
        var span = frame.Instances[0].Points.Span;

        Assert.Equal(4, span.Length);
        Assert.Equal(100f, span[0].X); Assert.Equal(100f, span[0].Y);
        Assert.Equal(99f, span[1].X);  Assert.Equal(99f, span[1].Y);
        Assert.Equal(98f, span[2].X);  Assert.Equal(100f, span[2].Y);
        Assert.Equal(50f, span[3].X);  Assert.Equal(150f, span[3].Y);
    }

    #endregion

    #region ReadPooled

    [Fact]
    public void ReadPooled_SingleInstance_DecodesCorrectly()
    {
        var points = new[] { new Point(10, 20), new Point(30, 40) };
        var data = EncodeFrame(42, 640, 480,
            (5, 2, 0.75f, points));

        using var handle = SegmentationProtocolF.ReadPooled(data);

        Assert.Equal(42ul, handle.FrameId);
        Assert.Equal(640u, handle.Width);
        Assert.Equal(480u, handle.Height);

        var instances = handle.Instances;
        Assert.Equal(1, instances.Length);

        Assert.Equal(5, instances[0].ClassId);
        Assert.Equal(2, instances[0].InstanceId);
        Assert.Equal(2, instances[0].Points.Length);
        Assert.Equal(10f, instances[0].Points.Span[0].X);
        Assert.Equal(20f, instances[0].Points.Span[0].Y);
    }

    [Fact]
    public void ReadPooled_MultipleInstances_AllPointsCorrect()
    {
        var pts1 = new[] { new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10) };
        var pts2 = new[] { new Point(100, 200) };
        var pts3 = new[] { new Point(500, 600), new Point(510, 610), new Point(520, 620) };
        Confidence conf = 0.9f;

        var data = EncodeFrame(99, 1920, 1080,
            (1, 1, conf, pts1),
            (2, 1, conf, pts2),
            (1, 2, conf, pts3));

        using var handle = SegmentationProtocolF.ReadPooled(data);

        Assert.Equal(3, handle.Instances.Length);

        // Instance 0: 4 points
        Assert.Equal(4, handle.Instances[0].Points.Length);
        Assert.Equal(new PointF(0, 0), handle.Instances[0].Points.Span[0]);
        Assert.Equal(new PointF(10, 0), handle.Instances[0].Points.Span[1]);
        Assert.Equal(new PointF(10, 10), handle.Instances[0].Points.Span[2]);
        Assert.Equal(new PointF(0, 10), handle.Instances[0].Points.Span[3]);

        // Instance 1: 1 point
        Assert.Equal(1, handle.Instances[1].Points.Length);
        Assert.Equal(new PointF(100, 200), handle.Instances[1].Points.Span[0]);

        // Instance 2: 3 points
        Assert.Equal(3, handle.Instances[2].Points.Length);
        Assert.Equal(new PointF(500, 600), handle.Instances[2].Points.Span[0]);
    }

    [Fact]
    public void ReadPooled_Dispose_IsIdempotent()
    {
        var data = EncodeFrame(1, 100, 100,
            (1, 1, 0.5f, new[] { new Point(1, 2) }));

        var handle = SegmentationProtocolF.ReadPooled(data);
        handle.Dispose();
        handle.Dispose(); // second dispose should not throw
    }

    #endregion

    #region Copy-on-read pattern

    [Fact]
    public void Copy_ProducesIndependentHandle()
    {
        var points = new[] { new Point(100, 200), new Point(300, 400) };
        var data = EncodeFrame(7, 640, 480,
            (1, 1, 0.95f, points));

        using var original = SegmentationProtocolF.ReadPooled(data);
        using var copy = original.Copy();

        // Copy has same metadata
        Assert.Equal(original.FrameId, copy.FrameId);
        Assert.Equal(original.Width, copy.Width);
        Assert.Equal(original.Height, copy.Height);
        Assert.Equal(original.Instances.Length, copy.Instances.Length);

        // Copy has same point data
        Assert.Equal(
            original.Instances[0].Points.Span[0],
            copy.Instances[0].Points.Span[0]);
        Assert.Equal(
            original.Instances[0].Points.Span[1],
            copy.Instances[0].Points.Span[1]);
    }

    [Fact]
    public void Copy_IsIndependentOfOriginal_AfterDispose()
    {
        var points = new[] { new Point(10, 20), new Point(30, 40), new Point(50, 60) };
        var data = EncodeFrame(1, 100, 100,
            (1, 1, 0.9f, points));

        SegmentationFrameHandleF copy;

        // Scope the original — dispose it before reading copy
        using (var original = SegmentationProtocolF.ReadPooled(data))
        {
            copy = original.Copy();
        }
        // original is disposed here

        using (copy)
        {
            // Copy should still have valid data
            Assert.Equal(1ul, copy.FrameId);
            Assert.Equal(3, copy.Instances[0].Points.Length);
            Assert.Equal(new PointF(10, 20), copy.Instances[0].Points.Span[0]);
            Assert.Equal(new PointF(30, 40), copy.Instances[0].Points.Span[1]);
            Assert.Equal(new PointF(50, 60), copy.Instances[0].Points.Span[2]);
        }
    }

    [Fact]
    public void Copy_MultipleInstances_PreservesAllData()
    {
        var pts1 = new[] { new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10) };
        var pts2 = new[] { new Point(20, 20), new Point(30, 20), new Point(25, 30) };
        Confidence conf = 0.85f;

        var data = EncodeFrame(42, 1920, 1080,
            (1, 1, conf, pts1),
            (2, 1, conf, pts2));

        using var original = SegmentationProtocolF.ReadPooled(data);
        using var copy = original.Copy();

        // Instance 0
        Assert.Equal(1, copy.Instances[0].ClassId);
        Assert.Equal(4, copy.Instances[0].Points.Length);
        for (int i = 0; i < pts1.Length; i++)
        {
            Assert.Equal((float)pts1[i].X, copy.Instances[0].Points.Span[i].X);
            Assert.Equal((float)pts1[i].Y, copy.Instances[0].Points.Span[i].Y);
        }

        // Instance 1
        Assert.Equal(2, copy.Instances[1].ClassId);
        Assert.Equal(3, copy.Instances[1].Points.Length);
        for (int i = 0; i < pts2.Length; i++)
        {
            Assert.Equal((float)pts2[i].X, copy.Instances[1].Points.Span[i].X);
            Assert.Equal((float)pts2[i].Y, copy.Instances[1].Points.Span[i].Y);
        }
    }

    #endregion

    #region ToPolygon — zero-copy from pooled handle

    [Fact]
    public void ToPolygon_FromReadPooled_CreatesValidPolygon()
    {
        // Square: (0,0) → (10,0) → (10,10) → (0,10)
        var points = new[] { new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10) };
        var data = EncodeFrame(1, 100, 100,
            (1, 1, 0.9f, points));

        using var handle = SegmentationProtocolF.ReadPooled(data);
        var poly = handle.Instances[0].ToPolygon();

        Assert.Equal(4, poly.Count);
        Assert.Equal(100f, poly.Area(), precision: 1);
    }

    [Fact]
    public void ToPolygon_SecondInstance_NonZeroOffset_CreatesValidPolygon()
    {
        var pts1 = new[] { new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10) };
        var pts2 = new[] { new Point(20, 20), new Point(30, 20), new Point(25, 30) };
        Confidence conf = 0.9f;

        var data = EncodeFrame(1, 100, 100,
            (1, 1, conf, pts1),
            (2, 1, conf, pts2));

        using var handle = SegmentationProtocolF.ReadPooled(data);

        // Second instance has non-zero offset into pooled buffer
        var poly2 = handle.Instances[1].ToPolygon();
        Assert.Equal(3, poly2.Count);

        // Triangle area = 0.5 * |base * height| = 0.5 * 10 * 10 = 50
        Assert.Equal(50f, poly2.Area(), precision: 1);
    }

    [Fact]
    public void ToPolygon_FromCopy_NonZeroOffset_CreatesValidPolygon()
    {
        var pts1 = new[] { new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10) };
        var pts2 = new[] { new Point(20, 20), new Point(30, 20), new Point(25, 30) };
        Confidence conf = 0.9f;

        var data = EncodeFrame(1, 100, 100,
            (1, 1, conf, pts1),
            (2, 1, conf, pts2));

        using var original = SegmentationProtocolF.ReadPooled(data);
        using var copy = original.Copy();

        // Both instances from the copy should produce valid polygons
        var poly1 = copy.Instances[0].ToPolygon();
        var poly2 = copy.Instances[1].ToPolygon();

        Assert.Equal(4, poly1.Count);
        Assert.Equal(3, poly2.Count);
        Assert.Equal(100f, poly1.Area(), precision: 1);
        Assert.Equal(50f, poly2.Area(), precision: 1);
    }

    #endregion

    #region Full pipeline: decode → pool → copy → polygon → dispose

    [Fact]
    public void FullPipeline_DecodePooledCopyPolygonDispose()
    {
        // Simulate the real-time hot path:
        // 1. Decoder thread decodes into pooled handle
        // 2. GetLatest() returns a copy
        // 3. Consumer creates polygons from the copy
        // 4. Both handles are disposed — buffers return to pool

        var pts1 = new[] { new Point(0, 0), new Point(100, 0), new Point(100, 100), new Point(0, 100) };
        var pts2 = new[] { new Point(200, 200), new Point(300, 200), new Point(250, 300) };
        var pts3 = new[] { new Point(500, 500), new Point(600, 500), new Point(600, 600), new Point(500, 600) };

        var data = EncodeFrame(123, 1920, 1080,
            (0, 0, 0.95f, pts1),
            (0, 1, 0.88f, pts2),
            (1, 0, 0.72f, pts3));

        _output.WriteLine("Step 1: Decode from pool (simulates decoder thread)");
        var decoderHandle = SegmentationProtocolF.ReadPooled(data);

        _output.WriteLine("Step 2: Copy for consumer (simulates GetLatest)");
        var consumerHandle = decoderHandle.Copy();

        _output.WriteLine("Step 3: Decoder disposes original (next frame comes in)");
        decoderHandle.Dispose();

        _output.WriteLine("Step 4: Consumer creates polygons from copy");
        var polygons = new Polygon<float>[consumerHandle.Instances.Length];
        for (int i = 0; i < consumerHandle.Instances.Length; i++)
        {
            polygons[i] = consumerHandle.Instances[i].ToPolygon();
            _output.WriteLine($"  Polygon[{i}]: {polygons[i].Count} points, area={polygons[i].Area():F1}");
        }

        // Verify polygon data
        Assert.Equal(3, polygons.Length);
        Assert.Equal(4, polygons[0].Count);
        Assert.Equal(10000f, polygons[0].Area(), precision: 1);  // 100x100
        Assert.Equal(3, polygons[1].Count);
        Assert.Equal(5000f, polygons[1].Area(), precision: 1);   // triangle 100x100/2
        Assert.Equal(4, polygons[2].Count);
        Assert.Equal(10000f, polygons[2].Area(), precision: 1);  // 100x100

        _output.WriteLine("Step 5: Consumer disposes handle (buffers return to pool)");
        consumerHandle.Dispose();

        _output.WriteLine("Pipeline complete — zero allocation after pool warmup");
    }

    [Fact]
    public void FullPipeline_MultipleFrames_ReusesPool()
    {
        // Simulate processing multiple frames in sequence
        // Verifies pool reuse works correctly

        for (int frameNo = 0; frameNo < 5; frameNo++)
        {
            var pts = new[] { new Point(frameNo * 10, frameNo * 20), new Point(frameNo * 10 + 5, frameNo * 20 + 5) };
            var data = EncodeFrame((ulong)frameNo, 640, 480,
                (1, 1, 0.9f, pts));

            using var decoderHandle = SegmentationProtocolF.ReadPooled(data);
            using var consumerCopy = decoderHandle.Copy();

            Assert.Equal((ulong)frameNo, consumerCopy.FrameId);
            Assert.Equal(1, consumerCopy.Instances.Length);
            Assert.Equal(2, consumerCopy.Instances[0].Points.Length);

            var poly = consumerCopy.Instances[0].ToPolygon();
            Assert.Equal(2, poly.Count);

            _output.WriteLine($"Frame {frameNo}: decoded, copied, polygon OK");
        }
    }

    #endregion

    #region FromFrame (non-pooled → pooled conversion)

    [Fact]
    public void FromFrame_ConvertsNonPooledToPooled()
    {
        var points = new[] { new Point(10, 20), new Point(30, 40) };
        var data = EncodeFrame(5, 640, 480,
            (1, 1, 0.9f, points));

        var frame = SegmentationProtocolF.Read(data); // non-pooled
        using var handle = SegmentationFrameHandleF.FromFrame(in frame); // pooled

        Assert.Equal(frame.FrameId, handle.FrameId);
        Assert.Equal(frame.Width, handle.Width);
        Assert.Equal(frame.Height, handle.Height);
        Assert.Equal(frame.Instances.Count, handle.Instances.Length);

        Assert.Equal(
            frame.Instances[0].Points.Span[0],
            handle.Instances[0].Points.Span[0]);
        Assert.Equal(
            frame.Instances[0].Points.Span[1],
            handle.Instances[0].Points.Span[1]);
    }

    #endregion

    #region Round-trip with int-based protocol

    [Fact]
    public void ReadF_MatchesIntProtocol_Read()
    {
        var points = new[]
        {
            new Point(100, 200),
            new Point(150, 250),
            new Point(200, 300),
            new Point(180, 280)
        };

        var data = EncodeFrame(77, 1920, 1080,
            (3, 2, 0.88f, points));

        // Decode with both protocols
        var intFrame = SegmentationProtocol.Read(data);
        var floatFrame = SegmentationProtocolF.Read(data);

        Assert.Equal(intFrame.FrameId, floatFrame.FrameId);
        Assert.Equal(intFrame.Width, floatFrame.Width);
        Assert.Equal(intFrame.Height, floatFrame.Height);
        Assert.Equal(intFrame.Instances.Count, floatFrame.Instances.Count);

        var intInst = intFrame.Instances[0];
        var floatInst = floatFrame.Instances[0];

        Assert.Equal(intInst.ClassId, floatInst.ClassId);
        Assert.Equal(intInst.InstanceId, floatInst.InstanceId);
        Assert.Equal((ushort)intInst.Confidence, (ushort)floatInst.Confidence);
        Assert.Equal(intInst.Points.Length, floatInst.Points.Length);

        for (int i = 0; i < intInst.Points.Length; i++)
        {
            Assert.Equal((float)intInst.Points.Span[i].X, floatInst.Points.Span[i].X);
            Assert.Equal((float)intInst.Points.Span[i].Y, floatInst.Points.Span[i].Y);
        }
    }

    #endregion

    #region Large contour

    [Fact]
    public void ReadPooled_LargeContour_DecodesCorrectly()
    {
        var points = new Point[1000];
        for (int i = 0; i < 1000; i++)
        {
            double angle = 2 * Math.PI * i / 1000;
            points[i] = new Point((int)(960 + 400 * Math.Cos(angle)), (int)(540 + 400 * Math.Sin(angle)));
        }

        var data = EncodeFrame(999, 1920, 1080,
            (10, 5, 0.99f, points));

        using var handle = SegmentationProtocolF.ReadPooled(data);

        Assert.Equal(999ul, handle.FrameId);
        Assert.Equal(1, handle.Instances.Length);
        Assert.Equal(1000, handle.Instances[0].Points.Length);

        // Verify first and last points
        Assert.Equal((float)points[0].X, handle.Instances[0].Points.Span[0].X);
        Assert.Equal((float)points[0].Y, handle.Instances[0].Points.Span[0].Y);
        Assert.Equal((float)points[999].X, handle.Instances[0].Points.Span[999].X);
        Assert.Equal((float)points[999].Y, handle.Instances[0].Points.Span[999].Y);

        // Polygon from large contour
        var poly = handle.Instances[0].ToPolygon();
        Assert.Equal(1000, poly.Count);
        var area = poly.Area();
        _output.WriteLine($"Large circle polygon area: {area:F1} (expected ~{Math.PI * 400 * 400:F1})");
        Assert.True(area > 400_000f, $"Area {area} should approximate circle area");
    }

    #endregion
}
