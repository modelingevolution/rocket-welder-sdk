# RocketWelder SDK Architecture

## Overview

The RocketWelder SDK provides high-performance video streaming with support for multiple AI protocols (KeyPoints, Segmentation Results) over various transport mechanisms (File, TCP, WebSocket, NNG).

## Core Architectural Principles

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
│   - Stream, TCP, WebSocket, NNG    │
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
| **TCP** | `TcpFrameSink` | 4-byte LE length prefix | Point-to-point streaming |
| **WebSocket** | `WebSocketFrameSink` | Native message boundaries | Browser/web clients |
| **NNG** | `NngFrameSink` | Native message boundaries | High-performance IPC, multicast |

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
- Live TCP/WebSocket/NNG streaming with backpressure
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

### Writer Implementation Pattern

All protocol writers follow the same pattern:

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
        // Send complete frame atomically
        _frameSink.WriteFrame(_buffer.ToArray());
        _buffer.Dispose();
    }
}
```

### Reader Implementation Pattern

All protocol readers follow the same pattern:

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
            var frameData = await _frameSource.ReadFrameAsync(ct);
            if (frameData.IsEmpty) yield break;

            // Parse frame
            var frame = ParseFrame(frameData);
            yield return frame;
        }
    }

    private Frame ParseFrame(ReadOnlyMemory<byte> data)
    {
        // Decode binary protocol from frame bytes
        using var stream = new MemoryStream(data.ToArray());
        // ... parse and return Frame
    }
}
```

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

### NNG Pub/Sub (Multicast)

```csharp
// C# Publisher - Broadcasting to all subscribers
using var publisher = new NngPublisher("tcp://localhost:5555");
using var frameSink = new NngFrameSink(publisher);
using var sink = new SegmentationResultSink(frameSink);

while (processingVideo)
{
    using var writer = sink.CreateWriter(frameId++, width, height);
    foreach (var contour in detectedContours)
        writer.Append(contour.ClassId, contour.InstanceId, contour.Points);
}
```

```python
# Python Subscriber - Receiving from publisher (streaming)
import pynng
sub = pynng.Sub0()
sub.dial("tcp://localhost:5555")
sub.subscribe(b"")  # Subscribe to all topics

frame_source = NngFrameSource(sub)
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

### WebSocket - Native Message Boundaries
- **Framing**: One frame = one WebSocket binary message
- **Use case**: Browser/web clients
- **No additional framing needed**: WebSocket protocol provides message boundaries

### NNG - Native Message Boundaries
- **Framing**: One frame = one NNG message
- **Use case**: High-performance IPC, Pub/Sub multicast
- **No additional framing needed**: NNG is message-oriented
- **Pub/Sub pattern**: One-to-many distribution with automatic reliability

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
4. **Atomicity**: Frames written as complete units (important for NNG, WebSocket)
5. **Reusability**: Same transport layer for all protocols (KeyPoints, Segmentation, future protocols)

## Performance Considerations

### Memory Buffering

**Trade-off**: Writers now buffer complete frames in memory before sending.

- **Pro**: Atomic writes, transport independence
- **Con**: Temporary memory overhead per frame
- **Mitigation**: Frames are typically small (< 10 KB for keypoints)

### Zero-Copy Where Possible

- `ReadOnlySpan<byte>` and `ReadOnlyMemory<byte>` for efficient data handling
- `stackalloc` for small buffers (frame headers)
- `ArrayPool<byte>` for larger temporary buffers (WebSocket)

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
- Implementations for `socket`, `pynng`, `websockets` (async)
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

## Future Extensions

### Additional Transports
- **Unix Domain Sockets**: High-performance local IPC
- **MQTT**: IoT scenarios
- **gRPC**: Streaming RPC with built-in load balancing
- **QUIC**: UDP-based with TCP-like reliability

### Additional Protocols
- **Bounding Boxes**: Object detection results
- **Depth Maps**: Compressed depth information
- **3D Poses**: 3D keypoints with skeletal tracking

All future protocols benefit from existing transport infrastructure!
