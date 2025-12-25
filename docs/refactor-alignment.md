# Rocket-Welder-SDK: Python/C# Alignment & Cross-Platform Testing

## Executive Summary

This document identifies gaps between the Python and C# SDK implementations and outlines the work required for full API parity and cross-platform integration testing.

---

## 1. Critical Missing: Start Overloads (DX Priority)

The `RocketWelderClient.Start()` method is the primary entry point for developers. C# has multiple overloads for different use cases, but Python is incomplete.

### C# Start Overloads (RocketWelderClient.cs)

```csharp
// Duplex: simple in/out processing
void Start(Action<Mat, Mat> onFrame, CancellationToken ct = default)

// One-way: input only
void Start(Action<Mat> onFrame, CancellationToken ct = default)

// AI with writers: full power (MOST IMPORTANT)
void Start(Action<Mat, ISegmentationResultWriter, IKeyPointsWriter, Mat> onFrame, CancellationToken ct = default)
```

### Python Current State (rocket_welder_client.py)

```python
# Has this (wraps internally to FrameMetadata variant):
def start(self, on_frame: Union[Callable[[Mat], None], Callable[[Mat, Mat], None]], ...)

# MISSING - the AI output variant:
def start(self, on_frame: Callable[[Mat, ISegmentationResultWriter, IKeyPointsWriter, Mat], None], ...)
```

### Required Work

| Task | Priority | Effort |
|------|----------|--------|
| Add `Start(Mat, ISegmentationResultWriter, IKeyPointsWriter, Mat)` overload | **P0** | Medium |
| Create `ISegmentationResultSink` interface in Python | P0 | Small |
| Create `IKeyPointsSink` interface in Python | P0 | Small |
| Add factory methods for creating sinks from connection strings | P0 | Small |

---

## 2. Socket Transport: Server-Side Binding

The SDK acts as a **server** (producer) that binds to a socket, while rocket-welder2 (relay) acts as a **client** (consumer) that connects.

### C# Has Both Modes

```csharp
// Client mode (connect to existing server)
UnixSocketFrameSink.Connect(socketPath)

// Server mode (bind and wait for client) - USED BY FACTORY
UnixSocketFrameSink.Bind(socketPath)  // FrameSinkFactory uses this!
```

### Python Only Has Client Mode

```python
# Client mode only
UnixSocketFrameSink.connect(socket_path)

# MISSING: Server mode (bind)
# UnixSocketFrameSink.bind(socket_path)  # Not implemented!
```

### FrameSinkFactory Difference

| Language | Socket Creation |
|----------|-----------------|
| C# | `UnixSocketFrameSink.Bind(address)` - acts as SERVER |
| Python | `UnixSocketFrameSink.connect(address)` - acts as CLIENT |

### Required Work

| Task | Priority | Effort |
|------|----------|--------|
| Add `UnixSocketFrameSink.bind(socket_path)` classmethod | **P0** | Small |
| Update `FrameSinkFactory.create()` to use `bind()` instead of `connect()` | P0 | Small |

---

## 3. Cross-Platform Integration Testing

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                 RocketWelder.SDK.Server (C#)                     │
├─────────────────────────────────────────────────────────────────┤
│  1. Server creates DuplexChannel and sends frames                │
│  2. Python/C# SDK client processes frames                        │
│  3. SDK client writes keypoints/segmentation to sockets          │
│  4. Server connects to sockets and reads AI results              │
└─────────────────────────────────────────────────────────────────┘

      RocketWelder.SDK.Server              Python/C# SDK Client
      ┌──────────────────┐                 ┌──────────────────┐
      │     Server       │                 │ RocketWelderClient│
      │                  │                 │                  │
      │ DuplexChannel    │◄───────────────►│ DuplexController │
      │ (sends frames)   │  Shared Memory  │ (processes)      │
      │                  │                 │                  │
      │ SocketSource     │◄────────────────│ SocketSink       │
      │ (reads results)  │  Unix Socket    │ (writes AI out)  │
      └──────────────────┘                 └──────────────────┘
```

### Required Components

#### A. RocketWelder.SDK.Server NuGet Package

A new package that acts as a server-side component, simulating what GStreamer does. Uses `DuplexChannel` under the hood. Useful for:
- Integration tests without Docker/GStreamer
- Unit testing container code
- Development without camera hardware
- C# applications that want to communicate with Python/C# SDK containers

**Package**: `RocketWelder.SDK.Server`

```csharp
namespace RocketWelder.SDK.Server
{
    /// <summary>
    /// Server that communicates with RocketWelderClient via DuplexChannel.
    /// Acts as the "GStreamer side" - sends frames and receives processed results.
    /// Uses ZeroBuffer.DuplexChannel under the hood.
    /// </summary>
    public class Server : IDisposable
    {
        /// <summary>
        /// Creates a server with the specified channel configuration.
        /// </summary>
        /// <param name="channelName">Shared memory channel name (e.g., "video-input")</param>
        /// <param name="width">Frame width in pixels</param>
        /// <param name="height">Frame height in pixels</param>
        /// <param name="format">Pixel format (default: RGB)</param>
        public Server(string channelName, int width, int height, string format = "RGB");

        /// <summary>
        /// Creates server from connection string.
        /// </summary>
        public static Server FromConnectionString(string connectionString);

        /// <summary>
        /// Starts the server and waits for client to connect.
        /// </summary>
        public void Start();
        public Task StartAsync(CancellationToken ct = default);

        /// <summary>
        /// Sends a frame to the connected RocketWelderClient and receives processed output.
        /// </summary>
        /// <param name="inputFrame">Input frame (BGR/RGB pixel data)</param>
        /// <returns>Processed output frame from the client</returns>
        public Mat SendFrame(Mat inputFrame);
        public Mat SendFrame(byte[] pixelData, FrameMetadata metadata);
        public Task<Mat> SendFrameAsync(Mat inputFrame, CancellationToken ct = default);

        /// <summary>
        /// Connects to SDK container's output sockets to receive AI results.
        /// </summary>
        public void ConnectToKeyPointsSocket(string socketPath);
        public void ConnectToSegmentationSocket(string socketPath);
        public Task ConnectToKeyPointsSocketAsync(string socketPath, TimeSpan timeout);
        public Task ConnectToSegmentationSocketAsync(string socketPath, TimeSpan timeout);

        /// <summary>
        /// Reads keypoints frames from connected socket.
        /// </summary>
        public IAsyncEnumerable<KeyPointsFrame> ReadKeyPointsFramesAsync(CancellationToken ct = default);
        public KeyPointsFrame? TryReadKeyPointsFrame(TimeSpan timeout);

        /// <summary>
        /// Reads segmentation frames from connected socket.
        /// </summary>
        public IAsyncEnumerable<SegmentationFrame> ReadSegmentationFramesAsync(CancellationToken ct = default);
        public SegmentationFrame? TryReadSegmentationFrame(TimeSpan timeout);

        /// <summary>
        /// Current frame number (incremented on each SendFrame).
        /// </summary>
        public ulong FrameNumber { get; }

        /// <summary>
        /// Whether a client is currently connected.
        /// </summary>
        public bool IsClientConnected { get; }

        public void Stop();
        public void Dispose();
    }
}
```

**Usage Example**:

```csharp
// Simple usage - send frames and read AI results
using var server = new Server("video-channel", 1920, 1080);
server.Start();

// Connect to SDK container's output sockets
await server.ConnectToKeyPointsSocketAsync("/tmp/keypoints.sock", TimeSpan.FromSeconds(30));
await server.ConnectToSegmentationSocketAsync("/tmp/segmentation.sock", TimeSpan.FromSeconds(30));

// Processing loop
foreach (var frame in videoSource.ReadFrames())
{
    // Send frame to SDK client, get processed output
    var outputFrame = server.SendFrame(frame);

    // Read AI results (non-blocking)
    var keypoints = server.TryReadKeyPointsFrame(TimeSpan.FromMilliseconds(100));
    var segmentation = server.TryReadSegmentationFrame(TimeSpan.FromMilliseconds(100));

    if (keypoints != null)
        Console.WriteLine($"Frame {keypoints.FrameId}: {keypoints.Points.Length} keypoints");
}
```

#### B. Python Test Script Template

```python
#!/usr/bin/env python3
"""Integration test: Python writes, C# reads."""

import sys
from rocket_welder_sdk import RocketWelderClient

def process_frame(input_mat, seg_writer, kp_writer, output_mat):
    # Simulate AI detection
    kp_writer.append(point_id=0, x=100, y=200, confidence=0.95)
    seg_writer.append(class_id=1, instance_id=0, points=[(10, 20), (30, 40)])

    # Copy input to output
    output_mat[:] = input_mat

def main():
    client = RocketWelderClient.from_environment()
    client.start(process_frame)
    client.wait()  # Block until stopped

if __name__ == "__main__":
    main()
```

#### C. C# Integration Test

```csharp
[Fact]
public async Task Python_And_CSharp_Binary_Protocols_Are_Compatible()
{
    // Arrange
    var kpSocketPath = "/tmp/test-keypoints.sock";
    var segSocketPath = "/tmp/test-segmentation.sock";
    var pythonScript = "tests/integration/python_producer.py";

    // Create server that acts as GStreamer
    using var server = new Server("test-channel", width: 640, height: 480, format: "RGB");

    // Start Python process with environment
    var pythonEnv = new Dictionary<string, string>
    {
        ["VIDEO_SOURCE"] = "shm://test-channel?size=4MB&metadata=4KB&mode=Duplex",
        ["KEYPOINTS_SINK_URL"] = $"socket://{kpSocketPath}",
        ["SEGMENTATION_SINK_URL"] = $"socket://{segSocketPath}"
    };
    using var python = Process.Start(new ProcessStartInfo("python3", pythonScript)
    {
        EnvironmentVariables = { pythonEnv }
    });

    // Start server (waits for Python client to connect)
    await server.StartAsync();

    // Connect to Python's output sockets
    await server.ConnectToKeyPointsSocketAsync(kpSocketPath, TimeSpan.FromSeconds(10));
    await server.ConnectToSegmentationSocketAsync(segSocketPath, TimeSpan.FromSeconds(10));

    // Act: Send a test frame and receive processed output
    var testFrame = CreateTestFrame(640, 480);
    var outputFrame = await server.SendFrameAsync(testFrame);

    // Assert: Read keypoints from Python's socket output
    var kpFrame = server.TryReadKeyPointsFrame(TimeSpan.FromSeconds(5));
    Assert.NotNull(kpFrame);
    Assert.NotEmpty(kpFrame.Points);
    Assert.Equal(100, kpFrame.Points[0].X);
    Assert.Equal(200, kpFrame.Points[0].Y);

    // Assert: Read segmentation from Python's socket output
    var segFrame = server.TryReadSegmentationFrame(TimeSpan.FromSeconds(5));
    Assert.NotNull(segFrame);
    Assert.NotEmpty(segFrame.Instances);
}

private static Mat CreateTestFrame(int width, int height)
{
    // Create a test pattern frame
    var frame = new Mat(height, width, DepthType.Cv8U, 3);
    frame.SetTo(new MCvScalar(128, 128, 128)); // Gray
    return frame;
}
```

---

## 4. Implementation Plan

### Phase 1: Python API Parity (1-2 days)

1. Add `UnixSocketFrameSink.bind()` classmethod
2. Update `FrameSinkFactory.create()` to use server mode
3. Add `ISegmentationResultSink` and `IKeyPointsSink` interfaces
4. Implement `Start(Mat, ISegmentationResultWriter, IKeyPointsWriter, Mat)` overload

### Phase 2: RocketWelder.SDK.Server Package (2-3 days)

1. Create `RocketWelder.SDK.Server` NuGet package
2. Implement `Server` class with DuplexChannel under the hood
3. Add socket connectivity for reading AI results (keypoints, segmentation)
4. Write cross-platform test harness

### Phase 3: Integration Tests in RocketWelder.SDK.Server.Tests (1-2 days)

1. `ServerTests.cs` - Unit tests for Server class (DuplexChannel mocking)
2. `SocketReaderTests.cs` - Unit tests for reading keypoints/segmentation
3. `CrossPlatformTests.cs`:
   - C# Server sends frame → Python SDK processes → C# reads keypoints
   - C# Server sends frame → Python SDK processes → C# reads segmentation
   - Full roundtrip validation with binary protocol assertions
4. CI/CD integration (GitHub Actions with Python + .NET)

---

## 5. API Comparison Summary

| Feature | C# | Python | Gap |
|---------|-----|--------|-----|
| `Start(Mat, Mat)` | ✅ | ✅ | - |
| `Start(Mat)` | ✅ | ✅ | - |
| `Start(Mat, SegWriter, KpWriter, Mat)` | ✅ | ❌ | **MISSING** |
| `UnixSocketFrameSink.Connect()` | ✅ | ✅ | - |
| `UnixSocketFrameSink.Bind()` | ✅ | ❌ | **MISSING** |
| `ISegmentationResultSink` | ✅ | ❌ | **MISSING** |
| `IKeyPointsSink` | ✅ | ❌ | **MISSING** |
| `FrameSinkFactory` (server mode) | ✅ | ❌ | Uses connect instead of bind |
| KeyPoints binary protocol | ✅ | ✅ | Compatible |
| Segmentation binary protocol | ✅ | ✅ | Compatible |
| Connection strings | ✅ | ✅ | Compatible |
| Transport: Stream | ✅ | ✅ | - |
| Transport: TCP | ✅ | ✅ | - |
| Transport: Unix Socket | ✅ | ✅ | - |
| Transport: NNG | ✅ | ✅ | (deprecated, moving to sockets) |
| Controllers (Duplex/OneWay) | ✅ | ✅ | - |
| UI Controls | ✅ | ✅ | - |
| External Controls (EventStore) | ✅ | ✅ | - |

---

## 6. File Changes Required

### Python SDK

| File | Changes |
|------|---------|
| `transport/unix_socket_transport.py` | Add `UnixSocketFrameSink.bind()` classmethod |
| `high_level/frame_sink_factory.py` | Use `bind()` instead of `connect()` |
| `rocket_welder_client.py` | Add `start()` overload with writers |
| `keypoints_protocol.py` | Add `IKeyPointsSink` interface + implementation |
| `segmentation_result.py` | Add `ISegmentationResultSink` interface + implementation |
| `high_level/__init__.py` | Export new interfaces |

### C# SDK (New Package: RocketWelder.SDK.Server)

| File | Description |
|------|-------------|
| `RocketWelder.SDK.Server.csproj` | New NuGet package |
| `Server.cs` | Main class - uses DuplexChannel under the hood |
| `ServerOptions.cs` | Configuration options (channel name, dimensions, format) |
| `SocketReader.cs` | Reads keypoints/segmentation from SDK output sockets |

### C# SDK (Test Project: RocketWelder.SDK.Server.Tests)

| File | Description |
|------|-------------|
| `RocketWelder.SDK.Server.Tests.csproj` | Test project for Server package |
| `ServerTests.cs` | Unit tests for Server class |
| `SocketReaderTests.cs` | Unit tests for socket reading |
| `CrossPlatformTests.cs` | Integration tests: C# Server ↔ Python SDK |
| `Fixtures/PythonProcessFixture.cs` | Test fixture for spawning Python processes |

### Python Test Scripts (for cross-platform testing)

| File | Description |
|------|-------------|
| `python/tests/integration/simple_producer.py` | Minimal Python script for integration tests |
| `python/tests/integration/full_pipeline.py` | Full pipeline with keypoints + segmentation |

---

## 7. Success Criteria

1. **API Parity**: Python `RocketWelderClient.start()` has same overloads as C#
2. **Socket Behavior**: Python SDK binds (server), C# tests connect (client)
3. **Binary Compatibility**: Keypoints/Segmentation frames written by Python can be read by C#
4. **CI Green**: Cross-platform tests pass in GitHub Actions
5. **DX Identical**: Developer using Python SDK has same experience as C# SDK

---

## 8. Out of Scope

- WebSocket transport (not needed for container-to-relay communication)
- Blazor decoders (WASM consumes from relay, not SDK)
- NNG transport (deprecated, moving to Unix sockets)
- Source interfaces (SDK is producer-only)
