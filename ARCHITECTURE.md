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

### IKeyPointsSink

High-level interface for writing KeyPoints protocol:

```csharp
public interface IKeyPointsSink : IDisposable, IAsyncDisposable
{
    IKeyPointsWriter CreateWriter(ulong frameId);
    Task<KeyPointsSeries> Read(string json, IFrameSource frameSource);
}
```

### KeyPointsSink Implementation

Uses IFrameSink internally to achieve transport independence:

```csharp
public class KeyPointsSink : IKeyPointsSink
{
    private readonly IFrameSink _frameSink;
    private readonly int _masterFrameInterval;
    private Dictionary<int, (Point, ushort)>? _previousFrame;

    public KeyPointsSink(IFrameSink frameSink, int masterFrameInterval = 300)
    {
        _frameSink = frameSink;
        _masterFrameInterval = masterFrameInterval;
    }

    public IKeyPointsWriter CreateWriter(ulong frameId)
    {
        bool isDelta = /* determine based on frame count and interval */;
        return new KeyPointsWriter(frameId, _frameSink, isDelta, _previousFrame, ...);
    }
}
```

### KeyPointsWriter Refactored

**Before (coupled to Stream):**
```csharp
// Writes directly to stream
_stream.WriteByte(frameType);
_stream.Write(frameData);
```

**After (buffered, then written via IFrameSink):**
```csharp
// Buffer to memory
var buffer = new MemoryStream();
buffer.WriteByte(frameType);
buffer.Write(frameData);

// On dispose: write complete frame atomically
public void Dispose()
{
    buffer.Seek(0, SeekOrigin.Begin);
    _frameSink.WriteFrame(buffer.ToArray());
    _onFrameWritten?.Invoke(_currentState);
}
```

## Usage Examples

### File Storage (Original Use Case)

```csharp
// C#
using var fileStream = File.Open("keypoints.bin", FileMode.Create);
using var frameSink = new StreamFrameSink(fileStream);
using var keypointsSink = new KeyPointsSink(frameSink, masterFrameInterval: 300);

using (var writer = keypointsSink.CreateWriter(frameId: 0))
{
    writer.Append(keypointId: 0, x: 100, y: 200, confidence: 0.95f);
    writer.Append(keypointId: 1, x: 120, y: 190, confidence: 0.92f);
}
```

```python
# Python
with open("keypoints.bin", "wb") as f:
    frame_sink = StreamFrameSink(f)
    keypoints_sink = KeyPointsSink(frame_sink, master_frame_interval=300)

    with keypoints_sink.create_writer(frame_id=0) as writer:
        writer.append(0, 100, 200, 0.95)
        writer.append(1, 120, 190, 0.92)
```

### TCP Streaming

```csharp
// C# Server
var server = new TcpListener(IPAddress.Any, 5000);
server.Start();
var client = await server.AcceptTcpClientAsync();

using var frameSink = new TcpFrameSink(client);
using var keypointsSink = new KeyPointsSink(frameSink);

// Write keypoints...
```

```python
# Python Client
import socket
sock = socket.socket()
sock.connect(("localhost", 5000))

frame_source = TcpFrameSource(sock)
keypoints_series = keypoints_sink.read(json_def, frame_source)
```

### NNG Pub/Sub

```csharp
// C# Publisher
using var publisher = new NngPublisher("tcp://localhost:5555");
using var frameSink = new NngFrameSink(publisher);
using var keypointsSink = new KeyPointsSink(frameSink);

// Publish keypoints to all subscribers
```

```python
# Python Subscriber
import pynng
sub = pynng.Sub0()
sub.dial("tcp://localhost:5555")

frame_source = NngFrameSource(sub)
# Receive keypoints continuously...
```

### WebSocket (Browser Integration)

```csharp
// C# Server
var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
using var frameSink = new WebSocketFrameSink(webSocket);
using var keypointsSink = new KeyPointsSink(frameSink);

// Stream keypoints to browser
```

```javascript
// Browser JavaScript
const ws = new WebSocket('ws://localhost:8080/keypoints');
ws.binaryType = 'arraybuffer';

ws.onmessage = (event) => {
    const frameData = new Uint8Array(event.data);
    // Parse KeyPoints protocol...
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
