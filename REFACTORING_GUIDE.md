# Refactoring Guide: Storage → Sink Pattern

## Overview

This guide shows step-by-step how to refactor from `IKeyPointsStorage` to `IKeyPointsSink` using the new transport abstraction.

## Step 1: Rename Interfaces

### KeyPointsProtocol.cs

**FIND:**
```csharp
public interface IKeyPointsStorage
{
    IKeyPointsWriter CreateWriter(ulong frameId);
    Task<KeyPointsSeries> Read(string json, Stream blobStream);
}
```

**REPLACE WITH:**
```csharp
public interface IKeyPointsSink
{
    IKeyPointsWriter CreateWriter(ulong frameId);
    Task<KeyPointsSeries> Read(string json, IFrameSource frameSource);
}
```

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

## Step 4: Update Read Method

The Read method needs to work with `IFrameSource` instead of `Stream`:

```csharp
public async Task<KeyPointsSeries> Read(string json, IFrameSource frameSource)
{
    var definition = JsonSerializer.Deserialize<KeyPointsDefinition>(json);
    var index = new Dictionary<ulong, SortedDictionary<int, (Point, float)>>();

    Dictionary<int, (Point, ushort)>? previousFrame = null;

    // Read frames until no more available
    while (frameSource.HasMoreFrames)
    {
        var frameBytes = await frameSource.ReadFrameAsync();
        if (frameBytes.Length == 0) break;

        // Parse frame from bytes
        using var frameStream = new MemoryStream(frameBytes.ToArray());

        // Read frame type
        int frameTypeByte = frameStream.ReadByte();
        if (frameTypeByte == -1) break;

        byte frameType = (byte)frameTypeByte;

        // Read frame ID
        byte[] frameIdBytes = new byte[8];
        await frameStream.ReadAsync(frameIdBytes, 0, 8);
        ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(frameIdBytes);

        // Read keypoint count
        uint keypointCount = await frameStream.ReadVarintAsync();

        var frameKeypoints = new SortedDictionary<int, (Point, float)>();

        if (frameType == MasterFrameType)
        {
            // Read master frame keypoints
            previousFrame = await ReadMasterFrameKeypoints(
                frameStream, (int)keypointCount, frameKeypoints);
        }
        else if (frameType == DeltaFrameType)
        {
            // Read delta frame keypoints
            await ReadDeltaFrameKeypoints(
                frameStream, (int)keypointCount, previousFrame, frameKeypoints);
        }

        index[frameId] = frameKeypoints;
    }

    return new KeyPointsSeries(
        definition.Version,
        definition.ComputeModuleName,
        definition.Points,
        index
    );
}
```

## Step 5: Update All Usages

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

### C# Files
1. ✅ `/csharp/RocketWelder.SDK/Transport/IFrameSink.cs` - NEW
2. ✅ `/csharp/RocketWelder.SDK/Transport/IFrameSource.cs` - NEW
3. ✅ `/csharp/RocketWelder.SDK/Transport/StreamFrameSink.cs` - NEW
4. ✅ `/csharp/RocketWelder.SDK/Transport/StreamFrameSource.cs` - NEW
5. ✅ `/csharp/RocketWelder.SDK/Transport/TcpFrameSink.cs` - NEW
6. ✅ `/csharp/RocketWelder.SDK/Transport/TcpFrameSource.cs` - NEW
7. ✅ `/csharp/RocketWelder.SDK/Transport/WebSocketFrameSink.cs` - NEW
8. ✅ `/csharp/RocketWelder.SDK/Transport/WebSocketFrameSource.cs` - NEW
9. ✅ `/csharp/RocketWelder.SDK/Transport/NngFrameSink.cs` - NEW (stub)
10. ✅ `/csharp/RocketWelder.SDK/Transport/NngFrameSource.cs` - NEW (stub)
11. ⏳ `/csharp/RocketWelder.SDK/KeyPointsProtocol.cs` - REFACTOR
12. ⏳ `/csharp/RocketWelder.SDK/SegmentationResult.cs` - REFACTOR
13. ⏳ `/csharp/RocketWelder.SDK/RocketWelderClient.cs` - UPDATE interface
14. ⏳ `/csharp/RocketWelder.SDK.Tests/*` - UPDATE tests
15. ⏳ `/csharp/examples/SimpleClient/Program.cs` - UPDATE usage

### Python Files
16. ⏳ `/python/rocket_welder_sdk/transport/frame_sink.py` - NEW
17. ⏳ `/python/rocket_welder_sdk/transport/frame_source.py` - NEW
18. ⏳ `/python/rocket_welder_sdk/transport/stream_transport.py` - NEW
19. ⏳ `/python/rocket_welder_sdk/transport/tcp_transport.py` - NEW
20. ⏳ `/python/rocket_welder_sdk/transport/websocket_transport.py` - NEW
21. ⏳ `/python/rocket_welder_sdk/transport/nng_transport.py` - NEW
22. ⏳ `/python/rocket_welder_sdk/keypoints_protocol.py` - REFACTOR
23. ⏳ `/python/rocket_welder_sdk/segmentation_result.py` - REFACTOR
24. ⏳ `/python/tests/test_transport_*.py` - NEW cross-platform tests

## Testing Checklist

- [ ] Unit tests for each transport sink/source
- [ ] KeyPoints roundtrip with each transport
- [ ] Segmentation roundtrip with each transport
- [ ] C# write → Python read (all transports)
- [ ] Python write → C# read (all transports)
- [ ] Existing file-based tests still pass
- [ ] Code quality checks pass (mypy, black, ruff)

Legend:
- ✅ = Complete
- ⏳ = In Progress / To Do
