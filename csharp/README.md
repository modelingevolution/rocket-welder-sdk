# RocketWelder SDK for .NET

High-performance video streaming client library for RocketWelder services with zero-copy shared memory support.

## Features

- 🚀 **Zero-Copy Performance** - Direct shared memory access for minimal latency
- 📹 **Multiple Protocols** - Support for SHM, TCP, HTTP, and MJPEG streaming
- 🔄 **Automatic Reconnection** - Resilient connection handling with retry logic
- 📊 **GStreamer Caps Parsing** - Compatible with GStreamer pipeline configurations
- 🖼️ **OpenCV Integration** - Direct Mat support via Emgu.CV

## Installation

```bash
dotnet add package RocketWelder.SDK
```

## Quick Start

```csharp
using RocketWelder.SDK;

// Parse connection string
var connectionString = ConnectionString.Parse("shm://MyBuffer?width=640&height=480&framerate=30");

// Create client
var client = new RocketWelderClient(connectionString);

// Connect with frame callback
await client.ConnectAsync(frame => 
{
    Console.WriteLine($"Received frame: {frame.Width}x{frame.Height}");
    // Process frame data (zero-copy access)
});

// Start receiving frames
await Task.Delay(TimeSpan.FromSeconds(10));

// Cleanup
client.Dispose();
```

## Connection String Format

The SDK supports multiple connection protocols:

### Shared Memory (SHM)
```
shm://BufferName?width=640&height=480&format=RGB&framerate=30
```

### TCP Streaming
```
tcp://192.168.1.100:5000?width=640&height=480
```

### HTTP MJPEG
```
http://192.168.1.100:8080/stream.mjpeg
```

### Combined Protocols
```
shm+tcp://BufferName?tcp_host=192.168.1.100&tcp_port=5000&width=640&height=480
```

## Video Formats

Supported video formats include:
- **RGB/BGR** - Standard color formats
- **RGBA/BGRA** - With alpha channel
- **GRAY8/GRAY16** - Grayscale
- **YUV** - I420, YV12, NV12, NV21, YUY2, UYVY
- **Bayer** - Raw sensor formats (BGGR, RGGB, GRBG, GBRG)

## GStreamer Caps Compatibility

The SDK can parse GStreamer caps strings directly:

```csharp
var caps = "video/x-raw, format=(string)RGB, width=(int)640, height=(int)480, framerate=(fraction)30/1";
var format = GstCaps.Parse(caps);

// Use with client
var client = new RocketWelderClient(connectionString)
{
    VideoFormat = format
};
```

## Zero-Copy Frame Processing

Process frames without memory allocation:

```csharp
await client.ConnectAsync(mat => 
{
    // mat is an OpenCV Mat with direct access to shared memory
    // No copying occurs - maximum performance
    
    // Example: Simple brightness check
    var mean = CvInvoke.Mean(mat);
    Console.WriteLine($"Average brightness: {mean.V0}");
});
```

## Advanced Configuration

```csharp
var client = new RocketWelderClient(connectionString)
{
    RetryAttempts = 5,
    RetryDelay = TimeSpan.FromSeconds(1),
    FrameTimeout = TimeSpan.FromSeconds(5),
    BufferSize = 10 * 1024 * 1024, // 10MB buffer
    VideoFormat = GstCaps.Parse(capsString)
};
```

## Dependencies

- **ZeroBuffer** (>= 1.0.0) - High-performance shared memory IPC
- **Emgu.CV** (>= 4.11.0) - OpenCV wrapper for .NET
- **Microsoft.Extensions.Configuration** - Configuration abstractions
- **Microsoft.Extensions.Logging** - Logging abstractions

## Platform Support

- **.NET 9.0+**
- **Windows** (x64)
- **Linux** (x64, arm64)
- **macOS** (x64, arm64)

## Examples

### Stream from GStreamer Pipeline

```csharp
// GStreamer pipeline sending to shared memory
// gst-launch-1.0 videotestsrc ! video/x-raw,format=RGB,width=640,height=480 ! shmsink socket-path=/tmp/gst-shm

var connectionString = "shm:///tmp/gst-shm?width=640&height=480&format=RGB";
var client = new RocketWelderClient(connectionString);

await client.ConnectAsync(ProcessFrame);
```

### Multi-Protocol Fallback

```csharp
// Try SHM first, fallback to TCP
var connectionString = "shm+tcp://MyBuffer?tcp_host=localhost&tcp_port=5000&width=640&height=480";

var client = new RocketWelderClient(connectionString);
await client.ConnectAsync(ProcessFrame);
```

## Performance

- **Zero-copy** shared memory access
- **< 1ms** latency for local SHM connections
- **60+ FPS** for 1080p video streams
- **Minimal CPU usage** due to direct memory access

## License

MIT License - Copyright © 2024 ModelingEvolution

See LICENSE file for details.

## Support

- GitHub Issues: https://github.com/modelingevolution/rocket-welder-sdk/issues
- Documentation: https://github.com/modelingevolution/rocket-welder-sdk

## About RocketWelder

RocketWelder provides high-performance video streaming solutions for industrial computer vision applications.

---

© 2024 ModelingEvolution. All rights reserved.