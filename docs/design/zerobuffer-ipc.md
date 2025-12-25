# ZeroBuffer IPC Architecture

This document describes the zero-copy shared memory IPC system used by RocketWelder SDK for high-performance video frame communication between C# Server and Python SDK clients.

## Overview

ZeroBuffer is a cross-platform (C#, Python, C++) shared memory IPC library that enables:
- Zero-copy frame transfer between processes
- Bidirectional request-response communication (Duplex Channel)
- Metadata exchange for stream configuration
- Process lifecycle detection (crash detection)

## Architecture Components

### Shared Memory Buffers

Each duplex channel creates two shared memory buffers in `/dev/shm/` (Linux) or named memory-mapped files (Windows):

```
Channel Name: "video-stream"
├── video-stream_request   (Client writes → Server reads)
└── video-stream_response  (Server writes → Client reads)
```

### Buffer Structure

Each buffer consists of:
1. **Header** - Control metadata (sequence numbers, sizes, state flags)
2. **Metadata Region** - Stream configuration (GstCaps, format info)
3. **Payload Region** - Actual frame data

```
┌──────────────────────────────────────────────────┐
│ Header (fixed size)                              │
│   - Magic number                                 │
│   - Version                                      │
│   - Writer PID (for crash detection)             │
│   - Sequence number                              │
│   - Data offset/size                             │
├──────────────────────────────────────────────────┤
│ Metadata (configurable size, e.g., 4KB)          │
│   - GstMetadata JSON (type, version, caps)       │
├──────────────────────────────────────────────────┤
│ Payload (configurable size, e.g., 256MB)         │
│   - Frame data with FrameMetadata prefix         │
│   - FrameMetadata (24 bytes): frame_number, pts  │
│   - Pixel data: RGB/BGR bytes                    │
└──────────────────────────────────────────────────┘
```

### Synchronization

Communication is synchronized using POSIX semaphores:
- `{buffer_name}_data` - Signals new data available
- `{buffer_name}_space` - Signals space available for writing

## Duplex Channel Roles

### RocketWelder SDK Role Mapping

| Component | ZeroBuffer Role | Buffer Created | Buffer Connected |
|-----------|-----------------|----------------|------------------|
| C# Server | DuplexClient | `_response` (Reader) | `_request` (Writer) |
| Python Client | ImmutableDuplexServer | `_request` (Reader) | `_response` (Writer) |

This may seem counter-intuitive, but reflects the data flow:
- C# Server **sends** video frames (writes to `_request`)
- Python Client **receives** frames, processes, and sends back (writes to `_response`)

### Connection Sequence

```
1. C# Server starts
   ├── Creates DuplexClient("channel-name")
   │   ├── Creates Reader for "_response" buffer
   │   └── Waits for "_request" buffer
   └── Sets metadata (GstCaps)

2. Python Client starts
   ├── Creates ImmutableDuplexServer("channel-name")
   │   ├── Creates Reader for "_request" buffer
   │   └── Connects to "_response" buffer (Writer)
   └── Reads metadata from "_request"

3. Handshake complete
   ├── C# Server sees IsServerConnected=true
   └── Frame exchange can begin
```

### Frame Exchange Flow

```
┌─────────────────┐          Shared Memory         ┌─────────────────┐
│   C# Server     │                                │  Python Client  │
│   (DuplexClient)│                                │  (DuplexServer) │
├─────────────────┤                                ├─────────────────┤
│                 │                                │                 │
│ 1. AcquireRequestBuffer()                        │                 │
│    ├── Get frame buffer                          │                 │
│    └── Write FrameMetadata + pixel data          │                 │
│                 │                                │                 │
│ 2. CommitRequest()                               │                 │
│    └── Signal semaphore ──────────────────────►  │ 3. read_frame() │
│                 │        "_request_data"         │    ├── Parse FrameMetadata │
│                 │                                │    └── Get pixel data      │
│                 │                                │                 │
│                 │                                │ 4. Process frame│
│                 │                                │    └── OpenCV/ML│
│                 │                                │                 │
│                 │                                │ 5. get_frame_buffer() │
│                 │                                │    └── Write response   │
│                 │                                │                 │
│ 6. ReceiveResponse()  ◄────────────────────────  │ commit_frame()  │
│    └── Get processed                             │    └── Signal   │
│        frame                                     │       semaphore │
│                 │                                │                 │
└─────────────────┘                                └─────────────────┘
```

## Code Examples

### C# Server Side

```csharp
using RocketWelder.SDK.Server;
using ZeroBuffer.DuplexChannel;

// Create server (uses DuplexClient internally)
var server = new Server("video-stream", width: 1920, height: 1080);
server.Start();  // Creates buffers, waits for client

// Send frame and get processed result
Mat outputFrame = server.SendFrame(inputFrame);
```

### Python Client Side

```python
from zerobuffer import DuplexChannelFactory, BufferConfig

factory = DuplexChannelFactory.get_instance()
config = BufferConfig(metadata_size=4096, payload_size=256*1024*1024)

# Create server (reads from _request, writes to _response)
server = factory.create_immutable_server("video-stream", config)

def process_frame(request: Frame, response_writer: Writer):
    # Read input frame
    metadata = FrameMetadata.from_bytes(request.data)
    pixel_data = request.data[24:]  # Skip FrameMetadata

    # Process with OpenCV/ML
    output = process(pixel_data)

    # Write response (zero-copy)
    with response_writer.get_frame_buffer(len(output)) as buffer:
        buffer[:] = output
    response_writer.commit_frame()

server.start(process_frame, on_init=parse_metadata)
```

## BDD Feature Tests

The zerobuffer library includes comprehensive cross-platform BDD tests using SpecFlow and JSON-RPC. These tests verify interoperability between C#, Python, and C++ implementations.

### Test Location

```
/mnt/d/source/modelingevolution/streamer/src/zerobuffer/
├── csharp/ZeroBuffer.ProtocolTests/
│   ├── Features/
│   │   ├── DuplexChannel.feature      # Duplex communication tests
│   │   ├── DuplexAdvanced.feature     # Mutable/immutable, cleanup
│   │   ├── BasicCommunication.feature # Simple read/write cycles
│   │   ├── ErrorHandling.feature      # Crash detection, recovery
│   │   └── ...
│   └── DESIGN.md                      # Test framework architecture
└── python/
    ├── features/                      # Shared feature files
    └── tests/
        ├── test_duplex_channel_integration.py
        └── test_immutable_duplex_server.py
```

### Example Feature: Basic Request-Response

```gherkin
Scenario: Test 14.1 - Basic Request-Response
    Given the server is 'csharp'
    And create duplex channel 'duplex-basic' with metadata size '4096' and payload size '1048576'
    And start echo handler

    When the client is 'python'
    And create duplex channel client 'duplex-basic'
    And send request with size '1'

    Then response should match request with size '1'
```

### Cross-Language Testing Architecture

The tests use a generic JSON-RPC protocol over stdin/stdout:

```
┌─────────────────┐     JSON-RPC      ┌─────────────────┐
│  SpecFlow       │ ─────────────────► │  C# Test        │
│  Test Runner    │                    │  Service        │
│                 │                    │                 │
│  (orchestrator) │ ─────────────────► │  Python Test    │
│                 │     JSON-RPC       │  Service        │
└─────────────────┘                    └─────────────────┘
```

Configuration-driven target selection:
```json
{
  "targets": {
    "csharp": { "executable": "dotnet", "arguments": "run serve" },
    "python": { "executable": "python", "arguments": "protocol_tests.py serve" },
    "cpp": { "executable": "./zerobuffer_tests", "arguments": "serve" }
  }
}
```

## Key Design Decisions

### 1. Immutable Server Pattern

Python implements `ImmutableDuplexServer` which means:
- Request frame is **immutable** (read-only)
- Response must be written to a **separate** buffer
- No in-place modification (better for AI frameworks)

### 2. FrameMetadata Prefix

Each frame includes a 24-byte `FrameMetadata` prefix:
```
struct FrameMetadata {
    uint64_t frame_number;  // Monotonically increasing
    uint64_t pts;           // Presentation timestamp (nanoseconds)
    uint64_t reserved;      // For future use
}
```

### 3. GstMetadata for Stream Configuration

Stream configuration is exchanged via JSON metadata:
```json
{
  "type": "zerosrc",
  "version": "1.0",
  "caps": {
    "width": 1920,
    "height": 1080,
    "format": "RGB",
    "framerate_numerator": 30,
    "framerate_denominator": 1
  },
  "element_name": "sdk-server"
}
```

### 4. Process Crash Detection

Writer PID is stored in buffer header. Reader can detect writer death by:
1. Checking if PID is still alive
2. Timeout on semaphore wait
3. Checking buffer state flags

## Troubleshooting

### Common Issues

1. **BufferNotFoundException**: Client starts before server creates the buffer
   - Solution: Server must start first, or client must retry

2. **Timeout on SendFrame**: Python not processing frames
   - Check: Is Python's background thread running?
   - Check: Are both buffers created in `/dev/shm/`?

3. **Deadlock on socket binding**: `UnixSocketFrameSink.bind()` blocks
   - Solution: Call `bind()` AFTER `client.start()`, not before

### Debugging

Check shared memory buffers:
```bash
ls -la /dev/shm/ | grep channel-name
# Should show:
# channel-name_request  (4MB typical)
# channel-name_response (256MB typical)
```

Check semaphores:
```bash
ls /dev/shm/sem.channel-name*
# Should show data and space semaphores
```

## References

- ZeroBuffer repository: `/mnt/d/source/modelingevolution/streamer/src/zerobuffer/`
- BDD test design: `ZeroBuffer.ProtocolTests/DESIGN.md`
- Feature files: `ZeroBuffer.ProtocolTests/Features/`
