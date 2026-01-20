# RocketWelder SDK Architecture

## Overview

The RocketWelder SDK provides high-performance video streaming with support for multiple AI protocols (KeyPoints, Segmentation, Graphics) over various transport mechanisms (File, TCP, Unix Socket, WebSocket).

## API Layers

```
┌─────────────────────────────────────────────────────────────────────┐
│  High-Level API (User-facing)                                       │
│  RocketWelderClient, Schema, DataContext                            │
│  - Simple DX, type-safe, configuration via environment              │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ uses internally
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Protocol Layer (Internal)                                          │
│  KeyPointsSink, SegmentationResultSink, StageSink (Graphics)        │
│  - Frame encoding, delta compression, vector graphics               │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ uses internally
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Transport Layer (Internal)                                         │
│  IFrameSink, IFrameSource (Stream, TCP, Unix Socket, WebSocket)     │
│  - Frame boundaries, delivery                                       │
└─────────────────────────────────────────────────────────────────────┘
```

---

## High-Level API (RocketWelderClient)

The high-level API provides a clean developer experience hiding transport, writers, and frame management.

### Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  RocketWelderClient (Facade)                                        │
│                                                                     │
│  Properties (Schema - Static):                                      │
│  ├─ IKeyPointsSchema KeyPoints { get; }                             │
│  └─ ISegmentationSchema Segmentation { get; }                       │
│                                                                     │
│  Methods:                                                           │
│  └─ Start(Action<Mat, ISegmentationDataContext,                     │
│                  IKeyPointsDataContext, Mat>)                       │
└─────────────────────────────────────────────────────────────────────┘
                              │
           ┌──────────────────┴──────────────────┐
           │                                     │
           ▼                                     ▼
┌─────────────────────────────┐   ┌─────────────────────────────┐
│  IKeyPointsSchema           │   │  ISegmentationSchema        │
│  (Definition - Static)      │   │  (Definition - Static)      │
│                             │   │                             │
│  DefinePoint(name)          │   │  DefineClass(id, name)      │
│  → KeyPoint                 │   │  → SegmentClass             │
└─────────────────────────────┘   └─────────────────────────────┘
           │                                     │
           │ creates per frame (UoW)             │ creates per frame (UoW)
           ▼                                     ▼
┌─────────────────────────────┐   ┌─────────────────────────────┐
│  IKeyPointsDataContext      │   │  ISegmentationDataContext   │
│  (UoW - Scoped to Frame)    │   │  (UoW - Scoped to Frame)    │
│                             │   │                             │
│  Add(KeyPoint, x, y, conf)  │   │  Add(SegmentClass,          │
│                             │   │      instanceId, points)    │
│  [auto-commits on dispose]  │   │  [auto-commits on dispose]  │
└─────────────────────────────┘   └─────────────────────────────┘
```

### Value Types

```csharp
/// <summary>Defined keypoint in the schema.</summary>
public readonly record struct KeyPoint(int Id, string Name);

/// <summary>Defined segmentation class in the schema.</summary>
public readonly record struct SegmentClass(byte ClassId, string Name);
```

### Schema Interfaces

```csharp
public interface IKeyPointsSchema
{
    KeyPoint DefinePoint(string name);
    IReadOnlyList<KeyPoint> DefinedPoints { get; }
}

public interface ISegmentationSchema
{
    SegmentClass DefineClass(byte classId, string name);
    IReadOnlyList<SegmentClass> DefinedClasses { get; }
}
```

### Data Context Interfaces (Unit of Work)

```csharp
public interface IKeyPointsDataContext
{
    ulong FrameId { get; }
    void Add(KeyPoint point, int x, int y, float confidence);
}

public interface ISegmentationDataContext
{
    ulong FrameId { get; }
    uint Width { get; }
    uint Height { get; }
    void Add(SegmentClass segmentClass, byte instanceId, ReadOnlySpan<Point> points);
}
```

### Usage Example

```csharp
using var client = RocketWelderClient.FromEnvironment();

// Define schema (static, once)
var nose = client.KeyPoints.DefinePoint("nose");
var leftEye = client.KeyPoints.DefinePoint("left_eye");
var personClass = client.Segmentation.DefineClass(1, "person");

// Start processing loop
await client.StartAsync((inputFrame, segmentation, keypoints, outputFrame) =>
{
    // Detect and add keypoints
    var detected = detector.Detect(inputFrame);
    keypoints.Add(nose, detected.Nose.X, detected.Nose.Y, detected.Nose.Confidence);
    keypoints.Add(leftEye, detected.LeftEye.X, detected.LeftEye.Y, detected.LeftEye.Confidence);

    // Segment and add instances
    var masks = segmenter.Segment(inputFrame);
    foreach (var mask in masks.Where(m => m.ClassId == 1))
        segmentation.Add(personClass, mask.InstanceId, mask.ContourPoints);

    // Draw visualization
    inputFrame.CopyTo(outputFrame);
    DrawDetections(outputFrame, detected, masks);

    // Data contexts auto-commit when delegate returns
});
```

### Environment Variables (Connection Strings)

| Variable | Description | Example |
|----------|-------------|---------|
| `VIDEO_SOURCE` | Video input | `0`, `file:///video.mp4`, `shm://buffer` |
| `KEYPOINTS_CONNECTION_STRING` | KeyPoints output | `unix:///tmp/kp.sock?masterFrameInterval=300` |
| `SEGMENTATION_CONNECTION_STRING` | Segmentation output | `unix:///tmp/seg.sock` |

**Connection String Format:** `protocol://address?param=value`

Supported protocols:
- `unix://` - Unix domain socket (high-performance local IPC, recommended)
- `tcp://` - TCP with 4-byte LE framing (network streaming)
- `file://` - File output with varint framing

### Metadata Format

Schemas emit metadata as JSON for readers/consumers:

```json
{
    "version": 1,
    "type": "keypoints",
    "points": [
        {"id": 0, "name": "nose"},
        {"id": 1, "name": "left_eye"}
    ]
}
```

---

## Core Architectural Principles

### ⚠️ MANDATORY: ALL Data Uses Framing

**THIS IS NON-NEGOTIABLE. DO NOT SKIP FRAMING.**

Every protocol (KeyPoints, Segmentation, Graphics) MUST use framing for ALL data:
- **Files**: Varint length-prefix (`StreamFrameSink`/`StreamFrameSource`)
- **TCP/Unix Socket**: 4-byte LE length-prefix (`TcpFrameSink`/`TcpFrameSource`, `UnixSocketFrameSink`/`UnixSocketFrameSource`)
- **WebSocket**: Native message boundaries (automatic)

**Why?**
1. Frame boundary detection is essential for reading multiple frames
2. Cross-platform compatibility requires consistent framing
3. Python and C# MUST use the same framing - varint for files

**NEVER write raw bytes without framing. NEVER.**

If you're tempted to "simplify" by removing framing, STOP. The whole purpose of this refactor is to have consistent framing everywhere.

---

### 1. Separation of Concerns

The SDK separates **protocol logic** from **transport mechanisms** through a two-layer abstraction:

```
┌─────────────────────────────────────┐
│   Protocol Layer (What)            │
│   - KeyPointsSink                  │
│   - SegmentationResultSink         │
│   - Frame encoding/compression     │
└──────────────┬──────────────────────┘
               │
               │ uses
               ▼
┌─────────────────────────────────────┐
│   Transport Layer (Where)          │
│   - IFrameSink / IFrameSource      │
│   - Stream, TCP, Unix Socket, WS   │
│   - Frame boundaries & delivery    │
└─────────────────────────────────────┘
```

### 2. Frame-Based Communication

All protocols communicate in discrete **frames**:
- **Master frames**: Complete keypoints for a frame (full data)
- **Delta frames**: Differences from previous frame (compressed)

Each frame is written atomically to the transport.

## Transport Abstraction

### IFrameSink

Low-level interface for writing frames to any destination:

```csharp
public interface IFrameSink : IDisposable, IAsyncDisposable
{
    void WriteFrame(ReadOnlySpan<byte> frameData);
    ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData);
    void Flush();
    Task FlushAsync();
}
```

**Implementations:**

| Transport | Class | Framing | Use Case |
|-----------|-------|---------|----------|
| **File/Stream** | `StreamFrameSink` | Varint length prefix | Persistent storage, replay |
| **TCP** | `TcpFrameSink` | 4-byte LE length prefix | Network streaming |
| **Unix Socket** | `UnixSocketFrameSink` | 4-byte LE length prefix | High-performance local IPC (recommended) |
| **WebSocket** | `WebSocketFrameSink` | Native message boundaries | Browser/web clients |

### IFrameSource

Low-level interface for reading frames from any source:

```csharp
public interface IFrameSource : IDisposable, IAsyncDisposable
{
    ReadOnlyMemory<byte> ReadFrame(CancellationToken cancellationToken = default);
    ValueTask<ReadOnlyMemory<byte>> ReadFrameAsync(CancellationToken cancellationToken = default);
    bool HasMoreFrames { get; }
}
```

## Protocol Layer

### Design Philosophy: Real-Time Streaming

The SDK is designed for **real-time streaming**, not just file loading. This means:

1. **Writers**: Buffer one frame, write atomically via `IFrameSink`
2. **Readers**: Stream frames via `IAsyncEnumerable<T>` as they arrive from `IFrameSource`

This design supports:
- Live TCP/Unix Socket/WebSocket streaming with backpressure
- File replay with the same API
- Cancellation support via `CancellationToken`
- Memory-efficient processing (one frame at a time)

---

### KeyPoints Protocol

#### IKeyPointsSink (Writer Factory)

```csharp
public interface IKeyPointsSink : IDisposable, IAsyncDisposable
{
    IKeyPointsWriter CreateWriter(ulong frameId);
}
```

#### IKeyPointsSource (Streaming Reader)

```csharp
public interface IKeyPointsSource : IDisposable, IAsyncDisposable
{
    IAsyncEnumerable<KeyPointsFrame> ReadFramesAsync(CancellationToken ct = default);
}

public readonly struct KeyPointsFrame
{
    public ulong FrameId { get; }
    public bool IsDelta { get; }
    public IReadOnlyList<KeyPoint> KeyPoints { get; }
}

public readonly struct KeyPoint
{
    public int Id { get; }
    public int X { get; }
    public int Y { get; }
    public float Confidence { get; }
}
```

#### Usage - Writing

```csharp
// Create sink with transport
using var frameSink = new TcpFrameSink(tcpClient);
using var sink = new KeyPointsSink(frameSink, masterFrameInterval: 300);

// Write frames
for (ulong frameId = 0; frameId < 1000; frameId++)
{
    using var writer = sink.CreateWriter(frameId);
    writer.Append(keypointId: 0, x: 100, y: 200, confidence: 0.95f);
    writer.Append(keypointId: 1, x: 120, y: 190, confidence: 0.92f);
    // Frame sent atomically on dispose
}
```

#### Usage - Reading (Streaming)

```csharp
// Create source with transport
using var frameSource = new TcpFrameSource(tcpClient);
using var source = new KeyPointsSource(frameSource);

// Stream frames as they arrive
await foreach (var frame in source.ReadFramesAsync(cancellationToken))
{
    Console.WriteLine($"Frame {frame.FrameId}: {frame.KeyPoints.Count} keypoints");

    foreach (var kp in frame.KeyPoints)
    {
        ProcessKeyPoint(kp.Id, kp.X, kp.Y, kp.Confidence);
    }
}
```

---

### Segmentation Protocol

#### ISegmentationResultSink (Writer Factory)

```csharp
public interface ISegmentationResultSink : IDisposable, IAsyncDisposable
{
    ISegmentationResultWriter CreateWriter(ulong frameId, uint width, uint height);
}
```

#### ISegmentationResultSource (Streaming Reader)

```csharp
public interface ISegmentationResultSource : IDisposable, IAsyncDisposable
{
    IAsyncEnumerable<SegmentationFrame> ReadFramesAsync(CancellationToken ct = default);
}

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

#### Usage - Writing

```csharp
// Create sink with transport
using var frameSink = new WebSocketFrameSink(webSocket);
using var sink = new SegmentationResultSink(frameSink);

// Write frames
using var writer = sink.CreateWriter(frameId: 0, width: 1920, height: 1080);
writer.Append(classId: 1, instanceId: 0, points: contour1);
writer.Append(classId: 1, instanceId: 1, points: contour2);
writer.Append(classId: 2, instanceId: 0, points: contour3);
// Frame sent atomically on dispose
```

#### Usage - Reading (Streaming)

```csharp
// Create source with transport
using var frameSource = new WebSocketFrameSource(webSocket);
using var source = new SegmentationResultSource(frameSource);

// Stream frames as they arrive
await foreach (var frame in source.ReadFramesAsync(cancellationToken))
{
    Console.WriteLine($"Frame {frame.FrameId}: {frame.Instances.Count} instances");

    foreach (var instance in frame.Instances)
    {
        ProcessContour(instance.ClassId, instance.InstanceId, instance.Points.Span);
    }
}
```

---

### Graphics Protocol (Vector Overlays)

The Graphics protocol streams vector graphics commands to browser clients for real-time overlay rendering.

#### IStageSink (Writer Factory)

```csharp
public interface IStageSink : IDisposable, IAsyncDisposable
{
    IStageWriter CreateWriter(ulong frameId);
}
```

#### IStageWriter (Per-Frame Writer)

```csharp
public interface IStageWriter : IDisposable, IAsyncDisposable
{
    ulong FrameId { get; }
    ILayerCanvas this[byte layerId] { get; }
    ILayerCanvas Layer(byte layerId);
}
```

#### ILayerCanvas (Drawing API)

```csharp
public interface ILayerCanvas
{
    // Frame types
    void Master();  // Full redraw
    void Remain();  // Keep previous content
    void Clear();   // Clear layer

    // Styling
    void SetStroke(RgbColor color);
    void SetFill(RgbColor color);
    void SetThickness(int width);
    void SetFontSize(int size);
    void SetFontColor(RgbColor color);

    // Transforms
    void Translate(float dx, float dy);
    void Rotate(float degrees);
    void Scale(float sx, float sy);

    // Drawing operations
    void DrawPolygon(ReadOnlySpan<SKPoint> points);
    void DrawText(string text, int x, int y);
    void DrawCircle(int centerX, int centerY, int radius);
    void DrawRectangle(int x, int y, int width, int height);
    void DrawLine(int x1, int y1, int x2, int y2);
    void DrawJpeg(ReadOnlySpan<byte> jpegData, int x, int y, int width, int height);
}
```

#### Usage - Writing

```csharp
// Create sink with transport
using var frameSink = new WebSocketFrameSink(webSocket);
using var sink = new StageSink(frameSink);

// Write frames with vector graphics
using var writer = sink.CreateWriter(frameId: 0);

// Draw on layer 0 (background)
writer[0].SetStroke(RgbColor.Red);
writer[0].SetThickness(2);
writer[0].DrawPolygon(contourPoints);

// Draw on layer 1 (labels)
writer[1].SetFontSize(16);
writer[1].SetFontColor(RgbColor.White);
writer[1].DrawText($"Frame: {writer.FrameId}", 10, 20);

// Frame sent atomically on dispose
```

---

### Writer Implementation Pattern

All protocol writers follow the same pattern with **zero-copy buffer access**:

```csharp
internal class ProtocolWriter : IProtocolWriter
{
    private readonly IFrameSink _frameSink;
    private readonly MemoryStream _buffer = new();

    public void Append(/* data */)
    {
        // Write to internal buffer
        _buffer.Write(/* encoded data */);
    }

    public void Dispose()
    {
        // Send complete frame atomically (zero-copy using GetBuffer)
        _frameSink.WriteFrame(new ReadOnlySpan<byte>(
            _buffer.GetBuffer(), 0, (int)_buffer.Length));
        _buffer.Dispose();
    }
}
```

**Note**: Use `GetBuffer()` instead of `ToArray()` to avoid memory allocation.

### Reader Implementation Pattern

All protocol readers follow the same pattern with **zero-copy memory access**:

```csharp
internal class ProtocolSource : IProtocolSource
{
    private readonly IFrameSource _frameSource;

    public async IAsyncEnumerable<Frame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            // Read next frame from transport
            var frameData = await _frameSource.ReadFrameAsync(ct).ConfigureAwait(false);
            if (frameData.IsEmpty) yield break;

            // Parse frame
            var frame = ParseFrame(frameData);
            yield return frame;
        }
    }

    private Frame ParseFrame(ReadOnlyMemory<byte> data)
    {
        // Zero-copy: get underlying array segment without allocation
        if (!MemoryMarshal.TryGetArray(data, out var segment))
            throw new InvalidOperationException("Cannot get array segment");

        using var stream = new MemoryStream(
            segment.Array!, segment.Offset, segment.Count, writable: false);
        // ... parse and return Frame
    }
}
```

**Notes**:
- Use `MemoryMarshal.TryGetArray()` instead of `ToArray()` for zero-copy memory access
- Use `ConfigureAwait(false)` in all async library code to avoid deadlocks

## Usage Examples

### File Storage (Write and Replay)

```csharp
// C# - Writing to file
using var fileStream = File.Open("keypoints.bin", FileMode.Create);
using var frameSink = new StreamFrameSink(fileStream);
using var sink = new KeyPointsSink(frameSink, masterFrameInterval: 300);

for (ulong frameId = 0; frameId < 100; frameId++)
{
    using var writer = sink.CreateWriter(frameId);
    writer.Append(keypointId: 0, x: 100, y: 200, confidence: 0.95f);
    writer.Append(keypointId: 1, x: 120, y: 190, confidence: 0.92f);
}
```

```csharp
// C# - Reading from file (streaming replay)
using var fileStream = File.Open("keypoints.bin", FileMode.Open);
using var frameSource = new StreamFrameSource(fileStream);
using var source = new KeyPointsSource(frameSource);

await foreach (var frame in source.ReadFramesAsync())
{
    Console.WriteLine($"Frame {frame.FrameId}: {frame.KeyPoints.Count} keypoints");
}
```

```python
# Python - Writing
with open("keypoints.bin", "wb") as f:
    frame_sink = StreamFrameSink(f)
    sink = KeyPointsSink(frame_sink, master_frame_interval=300)

    for frame_id in range(100):
        with sink.create_writer(frame_id) as writer:
            writer.append(0, 100, 200, 0.95)
            writer.append(1, 120, 190, 0.92)
```

```python
# Python - Reading (streaming replay)
with open("keypoints.bin", "rb") as f:
    frame_source = StreamFrameSource(f)
    source = KeyPointsSource(frame_source)

    async for frame in source.read_frames_async():
        print(f"Frame {frame.frame_id}: {len(frame.keypoints)} keypoints")
```

### TCP Streaming (Real-Time)

```csharp
// C# Server - Sending keypoints
var server = new TcpListener(IPAddress.Any, 5000);
server.Start();
var client = await server.AcceptTcpClientAsync();

using var frameSink = new TcpFrameSink(client);
using var sink = new KeyPointsSink(frameSink);

while (processingVideo)
{
    using var writer = sink.CreateWriter(frameId++);
    foreach (var kp in detectedKeyPoints)
        writer.Append(kp.Id, kp.X, kp.Y, kp.Confidence);
}
```

```csharp
// C# Client - Receiving keypoints (streaming)
using var client = new TcpClient();
await client.ConnectAsync("localhost", 5000);

using var frameSource = new TcpFrameSource(client);
using var source = new KeyPointsSource(frameSource);

await foreach (var frame in source.ReadFramesAsync(cancellationToken))
{
    // Process each frame as it arrives
    UpdateVisualization(frame.KeyPoints);
}
```

```python
# Python Client - Receiving keypoints (streaming)
import socket
sock = socket.socket()
sock.connect(("localhost", 5000))

frame_source = TcpFrameSource(sock)
source = KeyPointsSource(frame_source)

async for frame in source.read_frames_async():
    process_keypoints(frame.keypoints)
```

### Unix Socket (High-Performance IPC)

```csharp
// C# Server - Listening on Unix socket
using var server = new UnixSocketServer("/tmp/segmentation.sock");
var client = await server.AcceptAsync();
using var frameSink = new UnixSocketFrameSink(client);
using var sink = new SegmentationResultSink(frameSink);

while (processingVideo)
{
    using var writer = sink.CreateWriter(frameId++, width, height);
    foreach (var contour in detectedContours)
        writer.Append(contour.ClassId, contour.InstanceId, contour.Points);
}
```

```python
# Python Client - Connecting to Unix socket
import socket
sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
sock.connect("/tmp/segmentation.sock")

frame_source = UnixSocketFrameSource(sock)
source = SegmentationResultSource(frame_source)

async for frame in source.read_frames_async():
    for instance in frame.instances:
        draw_contour(instance.class_id, instance.points)
```

### WebSocket (Browser Integration)

```csharp
// C# Server - Streaming to browser
var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
using var frameSink = new WebSocketFrameSink(webSocket);
using var sink = new KeyPointsSink(frameSink);

while (!cancellationToken.IsCancellationRequested)
{
    var keypoints = await DetectKeyPointsAsync(currentFrame);
    using var writer = sink.CreateWriter(frameId++);
    foreach (var kp in keypoints)
        writer.Append(kp.Id, kp.X, kp.Y, kp.Confidence);
}
```

```javascript
// Browser JavaScript - Receiving keypoints
const ws = new WebSocket('ws://localhost:8080/keypoints');
ws.binaryType = 'arraybuffer';

ws.onmessage = (event) => {
    const frameData = new Uint8Array(event.data);
    const frame = parseKeyPointsFrame(frameData);  // Parse binary protocol

    frame.keypoints.forEach(kp => {
        drawKeyPoint(kp.id, kp.x, kp.y, kp.confidence);
    });
};
```

## Framing Protocols

All stream-based transports use **length-prefix framing** for consistent frame boundary detection.

### Stream (File) - Length-Prefixed
- **Framing**: `[varint length][frame data]`
- **Use case**: Sequential file storage, replay
- **Length encoding**: Varint (variable-length integer, Protocol Buffers format)
- **Rationale**: Efficient for most frame sizes, space-saving for small frames

### TCP - Length-Prefixed
- **Framing**: `[4-byte LE length][frame data]`
- **Use case**: Network streaming, point-to-point
- **Length encoding**: 4-byte little-endian uint32
- **Rationale**: Fixed-size header for network protocols, max frame size 4GB

### Unix Socket - Length-Prefixed
- **Framing**: `[4-byte LE length][frame data]`
- **Use case**: High-performance local IPC (recommended for container-to-host communication)
- **Length encoding**: 4-byte little-endian uint32
- **Rationale**: Same framing as TCP but lower latency for local communication

### WebSocket - Native Message Boundaries
- **Framing**: One frame = one WebSocket binary message
- **Use case**: Browser/web clients
- **No additional framing needed**: WebSocket protocol provides message boundaries

## Migration Guide

### Renaming (Breaking Changes)

| Old Name | New Name |
|----------|----------|
| `IKeyPointsStorage` | `IKeyPointsSink` |
| `ISegmentationResultStorage` | `ISegmentationResultSink` |
| `FileKeyPointsStorage` | `KeyPointsSink` (takes `IFrameSink`) |
| `FileSegmentationResultStorage` | `SegmentationResultSink` (takes `IFrameSink`) |

### Code Migration

**Before:**
```csharp
using var stream = File.Open("data.bin", FileMode.Create);
using var storage = new FileKeyPointsStorage(stream);
```

**After:**
```csharp
using var stream = File.Open("data.bin", FileMode.Create);
using var frameSink = new StreamFrameSink(stream);
using var sink = new KeyPointsSink(frameSink);
```

### Benefits of New Architecture

1. **Transport Independence**: Same protocol code works over any transport
2. **Easy Testing**: Mock `IFrameSink` for unit tests
3. **Extensibility**: Add new transports without changing protocol logic
4. **Atomicity**: Frames written as complete units (important for WebSocket)
5. **Reusability**: Same transport layer for all protocols (KeyPoints, Segmentation, Graphics)

## Performance Considerations

### Memory Buffering

**Trade-off**: Writers now buffer complete frames in memory before sending.

- **Pro**: Atomic writes, transport independence
- **Con**: Temporary memory overhead per frame
- **Mitigation**: Frames are typically small (< 10 KB for keypoints)

### Zero-Copy Optimizations

The SDK uses several techniques to minimize memory allocations:

1. **Writers**: Use `MemoryStream.GetBuffer()` instead of `ToArray()`:
   ```csharp
   // BAD: allocates new array
   _frameSink.WriteFrame(_buffer.ToArray());

   // GOOD: zero-copy using existing buffer
   _frameSink.WriteFrame(new ReadOnlySpan<byte>(
       _buffer.GetBuffer(), 0, (int)_buffer.Length));
   ```

2. **Readers**: Use `MemoryMarshal.TryGetArray()` instead of `ToArray()`:
   ```csharp
   // BAD: allocates new array
   using var stream = new MemoryStream(data.ToArray());

   // GOOD: zero-copy using underlying array
   if (MemoryMarshal.TryGetArray(data, out var segment))
       using var stream = new MemoryStream(
           segment.Array!, segment.Offset, segment.Count, writable: false);
   ```

3. **Span/Memory types**:
   - `ReadOnlySpan<byte>` for synchronous write operations
   - `ReadOnlyMemory<byte>` for async operations and storage
   - `stackalloc` for small buffers (frame headers)
   - `ArrayPool<byte>` for larger temporary buffers (WebSocket)

### Async Best Practices

All async library code uses `ConfigureAwait(false)` to:
- Avoid deadlocks when called from UI contexts
- Improve performance by avoiding context switching

## Cross-Platform Compatibility

### Binary Protocol

All protocols use **little-endian** encoding for cross-platform compatibility:
- Frame IDs: 8-byte LE
- Coordinates: 4-byte LE (int32)
- Confidence: 2-byte LE (ushort, 0-10000)
- Length prefixes: 4-byte LE (TCP framing)

### Python Implementation

Python transports mirror C# design:
- `IFrameSink` / `IFrameSource` abstract base classes
- Implementations for `socket` (TCP/Unix), `websockets` (async)
- Type hints throughout for IDE support

## Testing Strategy

### Unit Tests
- Test each transport independently
- Mock sinks/sources for protocol tests

### Integration Tests
- Test each transport pair (C# writer → Python reader)
- Verify all 4 transports × 2 protocols = 8 combinations

### Cross-Platform Tests
- C# writes → Python reads (validate byte-for-byte compatibility)
- Python writes → C# reads
- Test files in `/tmp/rocket-welder-test/` shared directory

## C# vs Python Implementation Differences

### Overview

Both implementations follow the same architecture and binary protocols, ensuring full cross-platform compatibility. However, they differ in language-specific patterns and optimizations.

### Binary Protocol Compatibility

| Aspect | C# | Python | Status |
|--------|----|----|--------|
| Varint encoding | ✓ Identical | ✓ Identical | **Compatible** |
| ZigZag encoding | ✓ Identical | ✓ Identical | **Compatible** |
| Little-endian encoding | ✓ | ✓ | **Compatible** |
| Frame type (Master=0x00, Delta=0x01) | ✓ | ✓ | **Compatible** |
| Confidence scaling (0-10000 → 0.0-1.0) | ✓ | ✓ | **Compatible** |

### Transport Implementations

| Transport | C# | Python | Framing |
|-----------|-----|--------|---------|
| Stream (File) | `StreamFrameSink`/`Source` | `StreamFrameSink`/`Source` | Varint length-prefix |
| TCP | `TcpFrameSink`/`Source` | `TcpFrameSink`/`Source` | 4-byte LE length-prefix |
| Unix Socket | `UnixSocketFrameSink`/`Source` | `UnixSocketTransport` | 4-byte LE length-prefix |
| WebSocket | `WebSocketFrameSink`/`Source` | Not implemented | Native message boundaries |

### API Design Differences

#### Async Patterns

**C# (Async-first):**
```csharp
await foreach (var frame in source.ReadFramesAsync(cancellationToken))
{
    // Process frame
}
```

**Python (Mixed sync/async):**
```python
async for frame in source.read_frames_async():
    # Process frame
```

#### Resource Cleanup

**C#:** Uses `IDisposable` pattern with `using` statements
```csharp
using var sink = new KeyPointsSink(frameSink);
```

**Python:** Uses context managers with explicit `close()` methods
```python
with KeyPointsSink(frame_sink) as sink:
    # Use sink
# or
sink = KeyPointsSink(frame_sink)
try:
    # Use sink
finally:
    sink.close()
```

#### Data Context Visibility

**C#:** `Commit()` is `internal` - called automatically by the framework
```csharp
internal void Commit();  // Users cannot call this
```

**Python:** `commit()` is public - users can call it (but shouldn't need to)
```python
def commit(self) -> None:  # Available but auto-called
```

### Memory Optimization Patterns

#### C# Specific (Not in Python)

1. **Stack allocation:**
   ```csharp
   Span<byte> lengthPrefix = stackalloc byte[4];
   ```

2. **Zero-copy memory access:**
   ```csharp
   if (MemoryMarshal.TryGetArray(data, out var segment))
   ```

3. **ValueTask for low-allocation async:**
   ```csharp
   public ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData);
   ```

4. **Readonly structs:**
   ```csharp
   public readonly record struct KeyPoint(int Id, string Name);
   ```

#### Python Specific (Not in C#)

1. **NumPy integration:**
   ```python
   def to_normalized(self, width: int, height: int) -> npt.NDArray[np.float32]:
       normalized = self.points.astype(np.float32)
       normalized[:, 0] /= width
       normalized[:, 1] /= height
       return normalized
   ```

2. **Frozen dataclasses:**
   ```python
   @dataclass(frozen=True)
   class KeyPoint:
       id: int
       name: str
   ```

### Reader Pattern Difference

**C#:** Streaming reader with `IAsyncEnumerable<T>`
- Reads one frame at a time
- Ideal for real-time streaming
- Memory efficient

**Python:** Batch loading via `KeyPointsSink.read()`
- Loads entire series into memory as `KeyPointsSeries`
- Ideal for post-processing analysis
- Provides fast random access by frame ID

### Type Safety

| Feature | C# | Python |
|---------|-----|--------|
| Interface contracts | `interface` | `ABC` |
| Nullable safety | Built-in (C# 8+) | Type hints + mypy |
| Immutable returns | `IReadOnlyList<T>` | `List[T]` (mutable) |
| Parsing pattern | `IParsable<T>` | Static methods |

### Naming Conventions

| Concept | C# | Python |
|---------|-----|--------|
| Method names | `DefinePoint()` | `define_point()` |
| Properties | `FrameId` | `frame_id` |
| Constants | `MasterFrameInterval` | `MASTER_FRAME_TYPE` |

### Cross-Platform Testing

All combinations are tested:
- C# writes KeyPoints → Python reads ✓
- Python writes KeyPoints → C# reads ✓
- C# writes Segmentation → Python reads ✓
- Python writes Segmentation → C# reads ✓
- All transports (TCP, Unix Socket) ✓

---

## Future Extensions

### Additional Transports
- **MQTT**: IoT scenarios
- **gRPC**: Streaming RPC with built-in load balancing
- **QUIC**: UDP-based with TCP-like reliability

### Additional Protocols
- **Bounding Boxes**: Object detection results
- **Depth Maps**: Compressed depth information
- **3D Poses**: 3D keypoints with skeletal tracking

All future protocols benefit from existing transport infrastructure!
