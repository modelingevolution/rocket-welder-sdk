using System.Buffers;
using System.Drawing;
using Xunit;

namespace RocketWelder.SDK.Tests.BinaryProtocols;

/// <summary>
/// TDD/BDD tests to validate BinaryProtocols API design before implementation.
/// These tests mock the rendering loop to ensure signatures are efficient.
///
/// KEY FINDINGS FROM VECTOROVERLAY ANALYSIS:
///
/// 1. SegmentationDecoder (lines 48-78):
///    - Reuses List&lt;SKPoint&gt; across instances (good)
///    - Calls points.ToArray() per polygon (BAD - allocation per instance)
///    - Renders immediately after parsing each instance (streaming pattern)
///
/// 2. KeypointsDecoder (lines 56-102):
///    - Allocates new Dictionary every frame (BAD)
///    - Stores previousKeypoints state between frames
///    - Renders immediately after parsing each keypoint
///
/// DESIGN CONCERNS WITH CURRENT PROPOSAL:
///
/// - ReadOnlyMemory&lt;Point&gt; requires allocation for each instance
/// - ReadOnlyMemory&lt;SegmentationInstance&gt; requires allocation for frame
/// - Eager parsing model doesn't match streaming rendering pattern
///
/// ALTERNATIVE APPROACHES TO TEST:
///
/// A. Callback/Streaming API (zero allocation for parsing)
/// B. Pooled buffers with ArrayPool
/// C. Ref struct enumerator (lazy parsing)
/// </summary>
public class DesignAlignmentTests
{
    #region APPROACH A: Callback/Streaming API (Recommended)

    /// <summary>
    /// Streaming API - parser calls back for each instance, no allocations.
    /// This matches how VectorOverlay actually renders (immediately per instance).
    /// </summary>
    [Fact]
    public void Segmentation_StreamingApi_ZeroAllocation()
    {
        // Simulated binary data (would be real protocol bytes)
        byte[] data = SimulateSegmentationFrame();

        // Mock rendering context
        int instancesRendered = 0;
        Point[] pointBuffer = new Point[1024]; // Reusable buffer

        // PROPOSED API: Streaming with callback
        // SegmentationReader.Parse(data, (header, instanceReader) => { ... });

        // Mock implementation showing the pattern:
        var reader = new MockSegmentationReader();
        reader.Parse(data, pointBuffer, (in SegmentationInstanceData instance) =>
        {
            // This callback is invoked for each instance
            // Points are already in the provided buffer (no allocation)
            instancesRendered++;

            // Simulate rendering: canvas.DrawPolygon(instance.Points, color)
            Assert.True(instance.PointCount > 0);
            Assert.True(instance.ClassId >= 0);
        });

        Assert.True(instancesRendered > 0);
    }

    /// <summary>
    /// Keypoints streaming API - parse and callback per keypoint.
    /// </summary>
    [Fact]
    public void Keypoints_StreamingApi_ZeroAllocation()
    {
        byte[] data = SimulateKeypointFrame();

        int keypointsRendered = 0;

        var reader = new MockKeypointReader();
        reader.Parse(data, (in KeypointData kp) =>
        {
            keypointsRendered++;

            // Simulate rendering: canvas.DrawCircle(kp.Position.X, kp.Position.Y, radius, color)
            Assert.True(kp.Confidence >= 0);
        });

        Assert.True(keypointsRendered > 0);
    }

    /// <summary>
    /// V2 API: Points passed directly to callback - TRUE zero-allocation.
    /// This is the RECOMMENDED approach.
    /// </summary>
    [Fact]
    public void Segmentation_StreamingApiV2_PointsInCallback()
    {
        byte[] data = SimulateSegmentationFrame();
        Span<Point> pointBuffer = stackalloc Point[1024];

        int instancesRendered = 0;
        int totalPoints = 0;

        // RECOMMENDED API: Points span passed directly to callback
        var reader = new MockSegmentationReader();
        reader.ParseV2(data, pointBuffer, (in SegmentationInstanceData instance, ReadOnlySpan<Point> points) =>
        {
            instancesRendered++;
            totalPoints += points.Length;

            // Simulate rendering - points is directly usable!
            Assert.Equal((int)instance.PointCount, points.Length);
            foreach (var pt in points)
            {
                Assert.True(pt.X >= 0);
            }
        });

        Assert.Equal(2, instancesRendered);
        Assert.Equal(5, totalPoints); // 3 + 2
    }

    #endregion

    #region APPROACH B: Ref Struct Enumerator (Lazy Parsing)

    /// <summary>
    /// Ref struct enumerator - parse lazily as you iterate.
    /// Similar to Utf8JsonReader pattern.
    /// </summary>
    [Fact]
    public void Segmentation_RefStructEnumerator_LazyParsing()
    {
        byte[] data = SimulateSegmentationFrame();
        Point[] pointBuffer = new Point[1024];

        // PROPOSED API: Ref struct that parses lazily
        // foreach (var instance in SegmentationReader.Enumerate(data, pointBuffer)) { ... }

        var enumerator = new MockSegmentationEnumerator(data, pointBuffer);
        int count = 0;

        while (enumerator.MoveNext())
        {
            var instance = enumerator.Current;
            count++;

            // Points are in the shared buffer, valid until next MoveNext()
            Assert.True(instance.PointCount > 0);
        }

        Assert.True(count > 0);
    }

    #endregion

    #region APPROACH C: Pooled Buffers (Original Design + Pooling)

    /// <summary>
    /// Original design with ArrayPool to reduce allocations.
    /// Still allocates, but from pool.
    /// </summary>
    [Fact]
    public void Segmentation_PooledBuffers_ReducedAllocation()
    {
        byte[] data = SimulateSegmentationFrame();

        // PROPOSED API: Parse returns frame, uses pooled arrays
        // using var frame = SegmentationReader.Parse(data);
        // frame.Dispose() returns arrays to pool

        using var frame = MockSegmentationReader.ParsePooled(data);

        foreach (var instance in frame.Instances)
        {
            // Points are from pool, must not escape the using block
            Assert.True(instance.Points.Length > 0);
        }
    }

    #endregion

    #region RENDERING LOOP MOCK (How VectorOverlay would use the API)

    /// <summary>
    /// This test simulates exactly how SegmentationDecoder would use the new API.
    /// Shows the ideal integration pattern using V2 API.
    /// V2: Points passed directly to callback - TRUE zero-allocation!
    /// </summary>
    [Fact]
    public void RenderingLoop_Segmentation_IntegrationMock()
    {
        byte[] data = SimulateSegmentationFrame();

        // Mock stage/canvas
        var mockCanvas = new MockCanvas();
        ulong frameId = 0;
        uint width = 0, height = 0;

        // Reusable point buffer (can be stackalloc or pooled)
        Span<Point> pointBuffer = stackalloc Point[4096];

        // IDEAL API USAGE (V2): Points passed directly to callback
        SegmentationReader.Parse(data, pointBuffer,
            onHeader: (in SegmentationHeader h) =>
            {
                frameId = h.FrameId;
                width = h.Width;
                height = h.Height;
                mockCanvas.OnFrameStart(frameId);
            },
            onInstance: (in SegmentationInstanceData instance, ReadOnlySpan<Point> points) =>
            {
                // V2: Points passed directly - no need to slice from shared buffer!
                mockCanvas.DrawPolygon(points, instance.ClassId);
            },
            onComplete: () =>
            {
                mockCanvas.OnFrameEnd();
            });

        Assert.Equal(42UL, frameId);
        Assert.Equal(1920u, width);
        Assert.True(mockCanvas.PolygonsDrawn > 0);
    }

    /// <summary>
    /// This test simulates exactly how KeypointsDecoder would use the new API.
    /// Shows stateful reader pattern.
    /// </summary>
    [Fact]
    public void RenderingLoop_Keypoints_IntegrationMock()
    {
        // Simulate sequence: Master frame, then Delta frame
        ReadOnlySpan<byte> masterFrame = SimulateKeypointFrame(isMaster: true);
        ReadOnlySpan<byte> deltaFrame = SimulateKeypointFrame(isMaster: false);

        var mockCanvas = new MockCanvas();

        // Stateful reader (maintains previous frame for delta decoding)
        var reader = new MockKeypointReader();

        // Frame 1: Master
        reader.Parse(masterFrame,
            onHeader: (in KeypointHeader h) =>
            {
                mockCanvas.OnFrameStart(h.FrameId);
            },
            onKeypoint: (in KeypointData kp) =>
            {
                var radius = (int)(kp.Confidence / 10000f * 8) + 3;
                mockCanvas.DrawCircle(kp.Position.X, kp.Position.Y, radius, kp.Id);
            },
            onComplete: () =>
            {
                mockCanvas.OnFrameEnd();
            });

        Assert.True(mockCanvas.CirclesDrawn > 0);
        int afterMaster = mockCanvas.CirclesDrawn;

        // Frame 2: Delta (reader applies deltas internally)
        reader.Parse(deltaFrame,
            onHeader: (in KeypointHeader h) =>
            {
                mockCanvas.OnFrameStart(h.FrameId);
            },
            onKeypoint: (in KeypointData kp) =>
            {
                // kp.Position is already absolute (reader applied delta)
                var radius = (int)(kp.Confidence / 10000f * 8) + 3;
                mockCanvas.DrawCircle(kp.Position.X, kp.Position.Y, radius, kp.Id);
            },
            onComplete: () =>
            {
                mockCanvas.OnFrameEnd();
            });

        Assert.True(mockCanvas.CirclesDrawn > afterMaster);
    }

    #endregion

    #region WRITER API TESTS

    /// <summary>
    /// Test writer API with IBufferWriter for zero-copy output.
    /// </summary>
    [Fact]
    public void Segmentation_Writer_ZeroCopy()
    {
        var buffer = new ArrayBufferWriter<byte>();

        // Prepare instances to write
        Span<Point> polygon1 = stackalloc Point[] { new(100, 100), new(200, 100), new(150, 200) };
        Span<Point> polygon2 = stackalloc Point[] { new(300, 300), new(400, 350) };

        // PROPOSED API: Static write method with spans
        SegmentationWriter.WriteHeader(buffer, frameId: 42, width: 1920, height: 1080);
        SegmentationWriter.WriteInstance(buffer, classId: 0, instanceId: 1, polygon1);
        SegmentationWriter.WriteInstance(buffer, classId: 1, instanceId: 0, polygon2);

        Assert.True(buffer.WrittenCount > 0);

        // Verify round-trip
        Span<Point> pointBuffer = stackalloc Point[100];
        int instanceCount = 0;

        SegmentationReader.Parse(buffer.WrittenSpan, pointBuffer,
            onHeader: (in SegmentationHeader h) =>
            {
                Assert.Equal(42UL, h.FrameId);
                Assert.Equal(1920u, h.Width);
                Assert.Equal(1080u, h.Height);
            },
            onInstance: (in SegmentationInstanceData inst) =>
            {
                instanceCount++;
            },
            onComplete: () => { });

        Assert.Equal(2, instanceCount);
    }

    #endregion

    #region MOCK TYPES (These define the proposed API signatures)

    // ============ HEADER STRUCTS ============

    public readonly struct SegmentationHeader
    {
        public ulong FrameId { get; init; }
        public uint Width { get; init; }
        public uint Height { get; init; }
    }

    public readonly struct KeypointHeader
    {
        public ulong FrameId { get; init; }
        public bool IsDelta { get; init; }
        public uint KeypointCount { get; init; }
    }

    // ============ DATA STRUCTS (ref-friendly) ============

    /// <summary>
    /// Instance data passed to callback. Points are in external buffer.
    /// </summary>
    public readonly ref struct SegmentationInstanceData
    {
        public byte ClassId { get; init; }
        public byte InstanceId { get; init; }
        public uint PointCount { get; init; }
        // Points are in the buffer passed to Parse(), indices 0..PointCount-1
    }

    public readonly struct KeypointData
    {
        public int Id { get; init; }
        public Point Position { get; init; }
        public ushort Confidence { get; init; }
    }

    // ============ MOCK READER (Streaming API) ============

    /// <summary>
    /// KEY INSIGHT: Points Span is passed INTO callback as parameter, not captured from outside.
    /// This allows zero-allocation while still using callbacks.
    /// </summary>
    public class MockSegmentationReader
    {
        // V2: Callback receives points Span as parameter (not captured!)
        public delegate void InstanceCallbackV2(in SegmentationInstanceData instance, ReadOnlySpan<Point> points);

        // V1: Simple callback, caller reads from shared buffer by index (for backwards compat)
        public delegate void InstanceCallback(in SegmentationInstanceData instance);

        public void Parse(ReadOnlySpan<byte> data, Span<Point> pointBuffer, InstanceCallback onInstance)
        {
            // Mock: simulate parsing 2 instances
            var inst1 = new SegmentationInstanceData { ClassId = 0, InstanceId = 1, PointCount = 3 };
            pointBuffer[0] = new Point(100, 100);
            pointBuffer[1] = new Point(200, 100);
            pointBuffer[2] = new Point(150, 200);
            onInstance(in inst1);

            var inst2 = new SegmentationInstanceData { ClassId = 1, InstanceId = 0, PointCount = 2 };
            pointBuffer[0] = new Point(300, 300);
            pointBuffer[1] = new Point(400, 350);
            onInstance(in inst2);
        }

        /// <summary>
        /// V2 API: Points are passed directly to callback - no need to access shared buffer.
        /// This is the RECOMMENDED approach.
        /// </summary>
        public void ParseV2(ReadOnlySpan<byte> data, Span<Point> pointBuffer, InstanceCallbackV2 onInstance)
        {
            // Instance 1
            pointBuffer[0] = new Point(100, 100);
            pointBuffer[1] = new Point(200, 100);
            pointBuffer[2] = new Point(150, 200);
            var inst1 = new SegmentationInstanceData { ClassId = 0, InstanceId = 1, PointCount = 3 };
            onInstance(in inst1, pointBuffer.Slice(0, 3));

            // Instance 2
            pointBuffer[0] = new Point(300, 300);
            pointBuffer[1] = new Point(400, 350);
            var inst2 = new SegmentationInstanceData { ClassId = 1, InstanceId = 0, PointCount = 2 };
            onInstance(in inst2, pointBuffer.Slice(0, 2));
        }

        public static PooledSegmentationFrame ParsePooled(ReadOnlySpan<byte> data)
        {
            // Mock: return pooled frame
            var points = ArrayPool<Point>.Shared.Rent(5);
            points[0] = new Point(100, 100);
            points[1] = new Point(200, 100);
            points[2] = new Point(150, 200);
            points[3] = new Point(300, 300);
            points[4] = new Point(400, 350);

            return new PooledSegmentationFrame
            {
                FrameId = 42,
                Width = 1920,
                Height = 1080,
                Instances = new[]
                {
                    new PooledInstance { ClassId = 0, InstanceId = 1, Points = points.AsMemory(0, 3), _rentedArray = points },
                    new PooledInstance { ClassId = 1, InstanceId = 0, Points = points.AsMemory(3, 2), _rentedArray = null }
                },
                _rentedPoints = points
            };
        }
    }

    public class MockKeypointReader
    {
        private Dictionary<int, (int x, int y, ushort conf)>? _previous;

        public delegate void HeaderCallback(in KeypointHeader header);
        public delegate void KeypointCallback(in KeypointData keypoint);
        public delegate void CompleteCallback();

        public void Parse(ReadOnlySpan<byte> data, KeypointCallback onKeypoint)
        {
            // Mock: simulate parsing keypoints
            var kp1 = new KeypointData { Id = 0, Position = new Point(100, 200), Confidence = 9500 };
            var kp2 = new KeypointData { Id = 1, Position = new Point(80, 180), Confidence = 8000 };
            onKeypoint(in kp1);
            onKeypoint(in kp2);
        }

        public void Parse(ReadOnlySpan<byte> data, HeaderCallback onHeader, KeypointCallback onKeypoint, CompleteCallback onComplete)
        {
            var header = new KeypointHeader { FrameId = 1, IsDelta = false, KeypointCount = 2 };
            onHeader(in header);

            var kp1 = new KeypointData { Id = 0, Position = new Point(100, 200), Confidence = 9500 };
            var kp2 = new KeypointData { Id = 1, Position = new Point(80, 180), Confidence = 8000 };
            onKeypoint(in kp1);
            onKeypoint(in kp2);

            // Store for delta decoding
            _previous = new Dictionary<int, (int, int, ushort)>
            {
                [0] = (100, 200, 9500),
                [1] = (80, 180, 8000)
            };

            onComplete();
        }
    }

    public ref struct MockSegmentationEnumerator
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly Span<Point> _pointBuffer;
        private int _position;
        private SegmentationInstanceData _current;

        public MockSegmentationEnumerator(ReadOnlySpan<byte> data, Span<Point> pointBuffer)
        {
            _data = data;
            _pointBuffer = pointBuffer;
            _position = 0;
            _current = default;
        }

        // Return by value - ref struct members can't return by reference
        public SegmentationInstanceData Current => _current;

        public bool MoveNext()
        {
            if (_position >= 2) return false; // Mock: only 2 instances

            _pointBuffer[0] = new Point(100 + _position * 100, 100);
            _pointBuffer[1] = new Point(200 + _position * 100, 150);
            _current = new SegmentationInstanceData
            {
                ClassId = (byte)_position,
                InstanceId = 0,
                PointCount = 2
            };
            _position++;
            return true;
        }
    }

    // ============ POOLED FRAME (for approach C) ============

    public struct PooledSegmentationFrame : IDisposable
    {
        public ulong FrameId;
        public uint Width;
        public uint Height;
        public PooledInstance[] Instances;
        internal Point[]? _rentedPoints;

        public void Dispose()
        {
            if (_rentedPoints != null)
            {
                ArrayPool<Point>.Shared.Return(_rentedPoints);
                _rentedPoints = null;
            }
        }
    }

    public struct PooledInstance
    {
        public byte ClassId;
        public byte InstanceId;
        public Memory<Point> Points;
        internal Point[]? _rentedArray;
    }

    // ============ MOCK CANVAS ============

    public class MockCanvas
    {
        public int PolygonsDrawn { get; private set; }
        public int CirclesDrawn { get; private set; }

        public void OnFrameStart(ulong frameId) { }
        public void OnFrameEnd() { }

        public void DrawPolygon(ReadOnlySpan<Point> points, byte classId)
        {
            PolygonsDrawn++;
        }

        public void DrawCircle(int x, int y, int radius, int keypointId)
        {
            CirclesDrawn++;
        }
    }

    // ============ MOCK WRITER ============

    public static class SegmentationWriter
    {
        public static void WriteHeader(IBufferWriter<byte> buffer, ulong frameId, uint width, uint height)
        {
            var span = buffer.GetSpan(16);
            // Mock: write header bytes
            span[0] = 0x2A; // Mock data
            buffer.Advance(12);
        }

        public static void WriteInstance(IBufferWriter<byte> buffer, byte classId, byte instanceId, ReadOnlySpan<Point> points)
        {
            var span = buffer.GetSpan(2 + points.Length * 8);
            // Mock: write instance bytes
            span[0] = classId;
            span[1] = instanceId;
            buffer.Advance(2 + points.Length * 2); // Simplified
        }
    }

    /// <summary>
    /// PROPOSED FINAL API: Static reader with streaming callbacks.
    /// Points are passed directly to callback - zero-allocation pattern.
    /// </summary>
    public static class SegmentationReader
    {
        public delegate void HeaderCallback(in SegmentationHeader header);

        /// <summary>
        /// V2 (RECOMMENDED): Points passed directly to callback.
        /// Allows true zero-allocation - callback doesn't need to access shared buffer.
        /// </summary>
        public delegate void InstanceCallbackV2(in SegmentationInstanceData instance, ReadOnlySpan<Point> points);

        /// <summary>
        /// V1: Simple callback, caller accesses shared buffer by instance.PointCount.
        /// Useful when callback needs to store points to a different buffer.
        /// </summary>
        public delegate void InstanceCallback(in SegmentationInstanceData instance);

        public delegate void CompleteCallback();

        /// <summary>
        /// V2 Parse (RECOMMENDED): Points passed directly to callback.
        /// </summary>
        public static void Parse(
            ReadOnlySpan<byte> data,
            Span<Point> pointBuffer,
            HeaderCallback onHeader,
            InstanceCallbackV2 onInstance,
            CompleteCallback onComplete)
        {
            // Mock implementation
            var header = new SegmentationHeader { FrameId = 42, Width = 1920, Height = 1080 };
            onHeader(in header);

            // Instance 1: fill buffer then pass slice to callback
            pointBuffer[0] = new Point(100, 100);
            pointBuffer[1] = new Point(200, 100);
            pointBuffer[2] = new Point(150, 200);
            var inst1 = new SegmentationInstanceData { ClassId = 0, InstanceId = 1, PointCount = 3 };
            onInstance(in inst1, pointBuffer.Slice(0, 3));

            // Instance 2
            pointBuffer[0] = new Point(300, 300);
            pointBuffer[1] = new Point(400, 350);
            var inst2 = new SegmentationInstanceData { ClassId = 1, InstanceId = 0, PointCount = 2 };
            onInstance(in inst2, pointBuffer.Slice(0, 2));

            onComplete();
        }

        /// <summary>
        /// V1 Parse: Simple callback, buffer accessible via shared state.
        /// </summary>
        public static void Parse(
            ReadOnlySpan<byte> data,
            Span<Point> pointBuffer,
            HeaderCallback onHeader,
            InstanceCallback onInstance,
            CompleteCallback onComplete)
        {
            // Convert to V2 internally
            Parse(data, pointBuffer, onHeader,
                (in SegmentationInstanceData inst, ReadOnlySpan<Point> _) => onInstance(in inst),
                onComplete);
        }
    }

    #endregion

    #region HELPER METHODS

    private static byte[] SimulateSegmentationFrame()
    {
        // Return mock binary data
        return new byte[] { 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x07, 0x38, 0x04 };
    }

    private static byte[] SimulateKeypointFrame(bool isMaster = true)
    {
        return new byte[] { (byte)(isMaster ? 0x00 : 0x01), 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 };
    }

    #endregion
}
