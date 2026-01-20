# Refactoring Guide: Storage → Sink/Source Pattern

## Overview

This guide shows step-by-step how to refactor from `IKeyPointsStorage` to `IKeyPointsSink` (writing) and `IKeyPointsSource` (reading) using the new transport abstraction.

### Key Design Principles

1. **Sink** = Writer factory (creates per-frame writers)
2. **Source** = Streaming reader (yields frames via `IAsyncEnumerable`)
3. **Writer** = Buffers one frame, writes atomically on dispose
4. **Transport** = Handles frame boundaries (length-prefix, native messages)

## Step 1: Define New Interfaces

### Separate Sink (Write) and Source (Read)

The old `IKeyPointsStorage` combined writing and reading. We split this into:

**OLD (combined):**
```csharp
public interface IKeyPointsStorage
{
    IKeyPointsWriter CreateWriter(ulong frameId);
    Task<KeyPointsSeries> Read(string json, Stream blobStream);  // Loads all into memory
}
```

**NEW (separated):**
```csharp
// Writing - factory for per-frame writers
public interface IKeyPointsSink : IDisposable, IAsyncDisposable
{
    IKeyPointsWriter CreateWriter(ulong frameId);
}

// Reading - streaming via IAsyncEnumerable
public interface IKeyPointsSource : IDisposable, IAsyncDisposable
{
    IAsyncEnumerable<KeyPointsFrame> ReadFramesAsync(CancellationToken ct = default);
}
```

### Why IAsyncEnumerable?

The `Read()` method that returns `Task<KeyPointsSeries>` loads ALL frames into memory. This doesn't work for:
- Real-time TCP/WebSocket streaming (infinite stream)
- Large files (memory exhaustion)
- Backpressure handling

`IAsyncEnumerable` provides:
- **Streaming**: Process one frame at a time
- **Backpressure**: Consumer controls pace
- **Cancellation**: Stop reading anytime
- **Memory efficient**: Only one frame in memory

## Step 2: Refactor KeyPointsWriter

### Current Implementation (Coupled to Stream)

```csharp
internal class KeyPointsWriter : IKeyPointsWriter
{
    private readonly Stream _stream;  // ❌ Directly writes to stream

    private void WriteFrame()
    {
        _stream.WriteByte(frameType);
        _stream.Write(frameData);
        // ... writes incrementally
    }
}
```

### New Implementation (Buffers, then writes via IFrameSink)

```csharp
internal class KeyPointsWriter : IKeyPointsWriter
{
    private readonly IFrameSink _frameSink;        // ✅ Writes via sink
    private readonly MemoryStream _buffer;         // ✅ Buffer complete frame

    public KeyPointsWriter(
        ulong frameId,
        IFrameSink frameSink,  // Changed from Stream
        bool isDelta,
        Dictionary<int, (Point, ushort)>? previousFrame,
        Action<Dictionary<int, (Point, ushort)>>? onFrameWritten = null)
    {
        _frameId = frameId;
        _frameSink = frameSink;
        _buffer = new MemoryStream();  // Internal buffer
        _isDelta = isDelta;
        _previousFrame = previousFrame;
        _onFrameWritten = onFrameWritten;
    }

    private void WriteFrame()
    {
        // Write to buffer instead of direct stream
        _buffer.WriteByte(frameType);

        Span<byte> frameIdBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(frameIdBytes, _frameId);
        _buffer.Write(frameIdBytes);

        _buffer.WriteVarint((uint)_keypoints.Count);

        if (_isDelta && _previousFrame != null)
            WriteDeltaKeypoints(_buffer);  // Pass buffer
        else
            WriteMasterKeypoints(_buffer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Write complete frame to buffer
        WriteFrame();

        // Send complete frame via sink (atomic operation)
        _buffer.Seek(0, SeekOrigin.Begin);
        _frameSink.WriteFrame(_buffer.ToArray());

        // Update state
        if (_onFrameWritten != null)
        {
            var frameState = new Dictionary<int, (Point, ushort)>();
            foreach (var (id, point, confidence) in _keypoints)
                frameState[id] = (point, confidence);
            _onFrameWritten(frameState);
        }

        _buffer.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Write complete frame to buffer
        WriteFrame();

        // Send complete frame via sink (atomic operation)
        _buffer.Seek(0, SeekOrigin.Begin);
        await _frameSink.WriteFrameAsync(_buffer.ToArray());

        // Update state
        if (_onFrameWritten != null)
        {
            var frameState = new Dictionary<int, (Point, ushort)>();
            foreach (var (id, point, confidence) in _keypoints)
                frameState[id] = (point, confidence);
            _onFrameWritten(frameState);
        }

        await _buffer.DisposeAsync();
    }
}
```

### Key Changes:

1. **Constructor**: Takes `IFrameSink` instead of `Stream`
2. **Buffer**: Added `MemoryStream _buffer` to buffer complete frame
3. **WriteFrame()**: Now writes to `_buffer` instead of `_stream`
4. **Dispose()**: Writes complete buffered frame via `_frameSink.WriteFrame()`
5. **WriteMasterKeypoints/WriteDeltaKeypoints**: Now take `Stream buffer` parameter

## Step 3: Refactor KeyPointsSink (formerly FileKeyPointsStorage)

### Before:

```csharp
public class FileKeyPointsStorage : IKeyPointsStorage
{
    private readonly Stream _stream;

    public FileKeyPointsStorage(Stream stream, int masterFrameInterval = 300)
    {
        _stream = stream;
        // ...
    }

    public IKeyPointsWriter CreateWriter(ulong frameId)
    {
        bool isDelta = /* ... */;
        return new KeyPointsWriter(frameId, _stream, isDelta, _previousFrame, ...);
    }
}
```

### After:

```csharp
public class KeyPointsSink : IKeyPointsSink
{
    private readonly IFrameSink _frameSink;
    private readonly int _masterFrameInterval;
    private Dictionary<int, (Point, ushort)>? _previousFrame;
    private int _frameCount;

    // Constructor for file/stream (most common)
    public KeyPointsSink(Stream stream, int masterFrameInterval = 300, bool leaveOpen = false)
        : this(new StreamFrameSink(stream, leaveOpen), masterFrameInterval)
    {
    }

    // Constructor for any transport
    public KeyPointsSink(IFrameSink frameSink, int masterFrameInterval = 300)
    {
        _frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        _masterFrameInterval = masterFrameInterval;
    }

    public IKeyPointsWriter CreateWriter(ulong frameId)
    {
        bool isDelta = _frameCount > 0 && (_frameCount % _masterFrameInterval) != 0;
        _frameCount++;

        return new KeyPointsWriter(
            frameId,
            _frameSink,  // ✅ Pass sink instead of stream
            isDelta,
            _previousFrame,
            newState => _previousFrame = newState
        );
    }

    public async Task<KeyPointsSeries> Read(string json, IFrameSource frameSource)
    {
        // Refactor to read from IFrameSource instead of Stream
        // ... (implementation below)
    }

    public void Dispose()
    {
        _frameSink?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_frameSink != null)
            await _frameSink.DisposeAsync();
    }
}
```

## Step 4: Implement KeyPointsSource (Streaming Reader)

Instead of a `Read()` method that loads everything into memory, implement `IKeyPointsSource` with `IAsyncEnumerable`:

```csharp
public class KeyPointsSource : IKeyPointsSource
{
    private readonly IFrameSource _frameSource;
    private Dictionary<int, (Point, ushort)>? _previousFrame;

    public KeyPointsSource(IFrameSource frameSource)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
    }

    public async IAsyncEnumerable<KeyPointsFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            // Read next frame from transport
            var frameBytes = await _frameSource.ReadFrameAsync(ct);
            if (frameBytes.IsEmpty) yield break;

            // Parse frame
            var frame = ParseFrame(frameBytes);
            yield return frame;
        }
    }

    private KeyPointsFrame ParseFrame(ReadOnlyMemory<byte> frameBytes)
    {
        using var stream = new MemoryStream(frameBytes.ToArray());

        // Read frame type
        int frameTypeByte = stream.ReadByte();
        if (frameTypeByte == -1)
            throw new EndOfStreamException("Unexpected end of frame");

        byte frameType = (byte)frameTypeByte;
        bool isDelta = frameType == DeltaFrameType;

        // Read frame ID (8 bytes LE)
        Span<byte> frameIdBytes = stackalloc byte[8];
        stream.Read(frameIdBytes);
        ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(frameIdBytes);

        // Read keypoint count
        uint keypointCount = stream.ReadVarint();

        // Read keypoints
        var keypoints = new List<KeyPoint>((int)keypointCount);

        if (isDelta && _previousFrame != null)
        {
            ReadDeltaKeypoints(stream, (int)keypointCount, keypoints);
        }
        else
        {
            ReadMasterKeypoints(stream, (int)keypointCount, keypoints);
        }

        // Update state for delta decoding
        UpdatePreviousFrame(keypoints);

        return new KeyPointsFrame(frameId, isDelta, keypoints);
    }

    public void Dispose() => _frameSource.Dispose();
    public ValueTask DisposeAsync() => _frameSource.DisposeAsync();
}
```

### Frame Data Structure

```csharp
public readonly struct KeyPointsFrame
{
    public ulong FrameId { get; }
    public bool IsDelta { get; }
    public IReadOnlyList<KeyPoint> KeyPoints { get; }

    public KeyPointsFrame(ulong frameId, bool isDelta, IReadOnlyList<KeyPoint> keyPoints)
    {
        FrameId = frameId;
        IsDelta = isDelta;
        KeyPoints = keyPoints;
    }
}

public readonly struct KeyPoint
{
    public int Id { get; }
    public int X { get; }
    public int Y { get; }
    public float Confidence { get; }

    public KeyPoint(int id, int x, int y, float confidence)
    {
        Id = id;
        X = x;
        Y = y;
        Confidence = confidence;
    }
}
```

### Usage

```csharp
// Real-time streaming from TCP
using var client = new TcpClient();
await client.ConnectAsync("localhost", 5000);
using var frameSource = new TcpFrameSource(client);
using var source = new KeyPointsSource(frameSource);

await foreach (var frame in source.ReadFramesAsync(cancellationToken))
{
    // Process each frame as it arrives
    Console.WriteLine($"Frame {frame.FrameId}: {frame.KeyPoints.Count} keypoints");

    foreach (var kp in frame.KeyPoints)
    {
        UpdateVisualization(kp.Id, kp.X, kp.Y, kp.Confidence);
    }
}
```

## Step 5: Implement SegmentationResultSource (Streaming Reader)

Same pattern as KeyPointsSource:

```csharp
public class SegmentationResultSource : ISegmentationResultSource
{
    private readonly IFrameSource _frameSource;

    public SegmentationResultSource(IFrameSource frameSource)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
    }

    public async IAsyncEnumerable<SegmentationFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            // Read next frame from transport
            var frameBytes = await _frameSource.ReadFrameAsync(ct);
            if (frameBytes.IsEmpty) yield break;

            // Parse frame
            var frame = ParseFrame(frameBytes);
            yield return frame;
        }
    }

    private SegmentationFrame ParseFrame(ReadOnlyMemory<byte> frameBytes)
    {
        using var stream = new MemoryStream(frameBytes.ToArray());

        // Read header
        Span<byte> frameIdBytes = stackalloc byte[8];
        stream.Read(frameIdBytes);
        ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(frameIdBytes);
        uint width = stream.ReadVarint();
        uint height = stream.ReadVarint();

        // Read instances until end of frame
        var instances = new List<SegmentationInstance>();

        while (stream.Position < stream.Length)
        {
            byte classId = (byte)stream.ReadByte();
            byte instanceId = (byte)stream.ReadByte();
            uint pointCount = stream.ReadVarint();

            var points = new Point[pointCount];
            if (pointCount > 0)
            {
                // First point (absolute)
                int x = stream.ReadVarint().ZigZagDecode();
                int y = stream.ReadVarint().ZigZagDecode();
                points[0] = new Point(x, y);

                // Remaining points (delta encoded)
                for (int i = 1; i < pointCount; i++)
                {
                    x += stream.ReadVarint().ZigZagDecode();
                    y += stream.ReadVarint().ZigZagDecode();
                    points[i] = new Point(x, y);
                }
            }

            instances.Add(new SegmentationInstance(classId, instanceId, points));
        }

        return new SegmentationFrame(frameId, width, height, instances);
    }

    public void Dispose() => _frameSource.Dispose();
    public ValueTask DisposeAsync() => _frameSource.DisposeAsync();
}
```

### Segmentation Data Structures

```csharp
public readonly struct SegmentationFrame
{
    public ulong FrameId { get; }
    public uint Width { get; }
    public uint Height { get; }
    public IReadOnlyList<SegmentationInstance> Instances { get; }
}

public readonly struct SegmentationInstance
{
    public byte ClassId { get; }
    public byte InstanceId { get; }
    public ReadOnlyMemory<Point> Points { get; }
}
```

## Step 6: Update All Usages

### In Controllers

**Before:**
```csharp
public void Start(Action<Mat, ISegmentationResultStorage, Mat> onFrame, ...)
```

**After:**
```csharp
public void Start(Action<Mat, ISegmentationResultWriter, IKeyPointsWriter, Mat> onFrame, ...)
```

### In Tests

**Before:**
```csharp
[Fact]
public void Test_WriteKeyPoints()
{
    using var stream = new MemoryStream();
    using var storage = new FileKeyPointsStorage(stream);

    using (var writer = storage.CreateWriter(0))
    {
        writer.Append(0, 100, 200, 0.95f);
    }
}
```

**After:**
```csharp
[Fact]
public void Test_WriteKeyPoints()
{
    using var stream = new MemoryStream();
    using var sink = new KeyPointsSink(stream);  // Or: new StreamFrameSink(stream)

    using (var writer = sink.CreateWriter(0))
    {
        writer.Append(0, 100, 200, 0.95f);
    }
}
```

### In Example Code

**Before:**
```csharp
using var file = File.Open("keypoints.bin", FileMode.Create);
using var storage = new FileKeyPointsStorage(file);
```

**After (Option 1 - Convenience constructor):**
```csharp
using var file = File.Open("keypoints.bin", FileMode.Create);
using var sink = new KeyPointsSink(file);  // Uses StreamFrameSink internally
```

**After (Option 2 - Explicit transport):**
```csharp
using var file = File.Open("keypoints.bin", FileMode.Create);
using var frameSink = new StreamFrameSink(file);
using var sink = new KeyPointsSink(frameSink);
```

**After (Option 3 - TCP transport):**
```csharp
using var client = new TcpClient();
await client.ConnectAsync("localhost", 5000);
using var frameSink = new TcpFrameSink(client);
using var sink = new KeyPointsSink(frameSink);
```

## Step 6: Python Equivalent

Apply the same refactoring to Python:

```python
# Before
class FileKeyPointsStorage(IKeyPointsStorage):
    def __init__(self, stream: BinaryIO, master_frame_interval: int = 300):
        self._stream = stream

# After
class KeyPointsSink(IKeyPointsSink):
    def __init__(
        self,
        frame_sink: IFrameSink,  # Or: BinaryIO for convenience
        master_frame_interval: int = 300
    ):
        if isinstance(frame_sink, io.IOBase):
            frame_sink = StreamFrameSink(frame_sink)
        self._frame_sink = frame_sink
```

## Complete File List to Update

### C# Transport Layer (Complete)
1. ✅ `/csharp/RocketWelder.SDK/Transport/IFrameSink.cs` - Write interface
2. ✅ `/csharp/RocketWelder.SDK/Transport/IFrameSource.cs` - Read interface
3. ✅ `/csharp/RocketWelder.SDK/Transport/StreamFrameSink.cs` - File/stream write
4. ✅ `/csharp/RocketWelder.SDK/Transport/StreamFrameSource.cs` - File/stream read
5. ✅ `/csharp/RocketWelder.SDK/Transport/TcpFrameSink.cs` - TCP write
6. ✅ `/csharp/RocketWelder.SDK/Transport/TcpFrameSource.cs` - TCP read
7. ✅ `/csharp/RocketWelder.SDK/Transport/WebSocketFrameSink.cs` - WebSocket write
8. ✅ `/csharp/RocketWelder.SDK/Transport/WebSocketFrameSource.cs` - WebSocket read

### C# Protocol Layer (In Progress)
11. ⏳ `/csharp/RocketWelder.SDK/KeyPointsProtocol.cs` - REFACTOR
    - ✅ `IKeyPointsSink` interface
    - ✅ `KeyPointsSink` implementation
    - ✅ `KeyPointsWriter` uses `IFrameSink`
    - ⏳ `IKeyPointsSource` interface - NEW
    - ⏳ `KeyPointsSource` with `IAsyncEnumerable` - NEW
    - ⏳ `KeyPointsFrame` / `KeyPoint` structs - NEW

12. ⏳ `/csharp/RocketWelder.SDK/RocketWelderClient.cs` - REFACTOR
    - ⏳ `ISegmentationResultSink` interface
    - ⏳ `SegmentationResultSink` implementation
    - ✅ `SegmentationResultWriter` uses `IFrameSink` (partial - has bug)
    - ⏳ `ISegmentationResultSource` interface - NEW
    - ⏳ `SegmentationResultSource` with `IAsyncEnumerable` - NEW
    - ⏳ `SegmentationFrame` / `SegmentationInstance` structs - NEW

### C# Tests & Examples
13. ⏳ `/csharp/RocketWelder.SDK.Tests/KeyPointsProtocolTests.cs` - UPDATE
14. ⏳ `/csharp/RocketWelder.SDK.Tests/SegmentationResultTests.cs` - UPDATE
15. ⏳ `/csharp/RocketWelder.SDK.Tests/TransportRoundTripTests.cs` - UPDATE
16. ⏳ `/csharp/examples/SimpleClient/Program.cs` - UPDATE

### Python Transport Layer (Partial)
17. ✅ `/python/rocket_welder_sdk/transport/frame_sink.py` - IFrameSink ABC
18. ✅ `/python/rocket_welder_sdk/transport/frame_source.py` - IFrameSource ABC
19. ✅ `/python/rocket_welder_sdk/transport/stream_transport.py` - Stream transport
20. ✅ `/python/rocket_welder_sdk/transport/tcp_transport.py` - TCP transport
21. ⏳ `/python/rocket_welder_sdk/transport/unix_socket_transport.py` - Unix socket (needed)
22. ⏳ `/python/rocket_welder_sdk/transport/websocket_transport.py` - WebSocket (not started)

### Python Protocol Layer (Needs Update)
23. ⏳ `/python/rocket_welder_sdk/keypoints_protocol.py` - REFACTOR
    - ✅ `KeyPointsSink` uses `IFrameSink`
    - ⏳ `KeyPointsSource` with async generator - NEW

24. ⏳ `/python/rocket_welder_sdk/segmentation_result.py` - REFACTOR
    - ✅ `SegmentationResultWriter` uses `IFrameSink`
    - ⏳ `SegmentationResultSource` with async generator - NEW

### Python Tests
25. ⏳ `/python/tests/test_keypoints_protocol.py` - UPDATE for streaming
26. ⏳ `/python/tests/test_segmentation_result.py` - UPDATE for streaming
27. ⏳ `/python/tests/test_cross_platform.py` - ADD streaming tests

## Testing Checklist

### Unit Tests
- [ ] `KeyPointsSource.ReadFramesAsync()` - single frame
- [ ] `KeyPointsSource.ReadFramesAsync()` - multiple frames
- [ ] `KeyPointsSource.ReadFramesAsync()` - cancellation
- [ ] `SegmentationResultSource.ReadFramesAsync()` - single frame
- [ ] `SegmentationResultSource.ReadFramesAsync()` - multiple frames
- [ ] `SegmentationResultSource.ReadFramesAsync()` - cancellation

### Integration Tests
- [ ] Write via Sink → Read via Source (same process)
- [ ] TCP streaming (separate processes)
- [ ] WebSocket streaming
- [ ] File write → File replay

### Cross-Platform Tests
- [ ] C# write → Python read (all transports)
- [ ] Python write → C# read (all transports)
- [ ] Byte-for-byte compatibility verification

### Code Quality
- [ ] C# builds with no errors
- [ ] Python: mypy, black, ruff pass
- [ ] Test coverage ≥ 55%

Legend:
- ✅ = Complete and tested
- ⏳ = In Progress / To Do
