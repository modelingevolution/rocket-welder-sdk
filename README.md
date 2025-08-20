# Rocket Welder SDK

[![NuGet](https://img.shields.io/nuget/v/RocketWelder.SDK.svg)](https://www.nuget.org/packages/RocketWelder.SDK/)
[![PyPI](https://img.shields.io/pypi/v/rocket-welder-sdk.svg)](https://pypi.org/project/rocket-welder-sdk/)
[![vcpkg](https://img.shields.io/badge/vcpkg-rocket--welder--sdk-blue)](https://github.com/modelingevolution/rocket-welder-sdk-vcpkg-registry)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Multi-language client libraries for interacting with RocketWelder video streaming services.

## Overview

The Rocket Welder SDK provides high-performance video streaming capabilities for containerized applications. It offers native client libraries in C++, C#, and Python, enabling seamless integration with RocketWelder video streaming pipelines.

## Features

- **High Performance**: Optimized for minimal latency and maximum throughput
- **Multi-Language Support**: Native libraries for C++, C#, and Python  
- **Protocol Flexibility**: Support for multiple streaming protocols via connection strings
- **Container-Ready**: Designed for Docker/Kubernetes deployments
- **Simple Integration**: Easy-to-use API with minimal configuration

## Client Libraries

| Language | Package Manager | Package Name |
|----------|----------------|--------------|
| C++ | vcpkg | rocket-welder-sdk |
| C# | NuGet | RocketWelder.SDK |
| Python | pip | rocket-welder-sdk |

## Connection String Format

The SDK uses URI-style connection strings to specify data sources and protocols:

```
protocol://[host[:port]]/[path][?param1=value1&param2=value2]
```

### Supported Protocols

#### Shared Memory (High-Performance Local)
```
shm://<buffer_name>
shm://<buffer_name>?buffer_size=10MB&metadata_size=1024KB
shm://<buffer_name>?mode=duplex&buffer_size=10MB
```

**Optional Parameters:**
- `mode`: Communication mode (`duplex` for bidirectional/mutable, `oneway` for one-way communication; default: `duplex`)
- `buffer_size`: Size of the data buffer (default: 20MB, supports units: B, KB, MB, GB)
- `metadata_size`: Size of the metadata buffer (default: 4KB, supports units: B, KB, MB)

#### MJPEG over HTTP
```
mjpeg+http://192.168.1.100:8080
mjpeg+http://camera.local:8080
```

#### MJPEG over TCP
```
mjpeg+tcp://192.168.1.100:5000
mjpeg+tcp://camera.local:5000
```

### Environment Variable

When deployed in a Rocket Welder container, the connection string is provided via:
```bash
CONNECTION_STRING=shm://camera_feed?buffer_size=20MB&metadata_size=4KB
```

## Installation

### C++ with vcpkg

Configure the custom registry in your `vcpkg-configuration.json`:
```json
{
  "registries": [
    {
      "kind": "git",
      "repository": "https://github.com/modelingevolution/rocket-welder-sdk-vcpkg-registry",
      "baseline": "YOUR_BASELINE_HERE",
      "packages": ["rocket-welder-sdk"]
    }
  ]
}
```

Then install:
```bash
# Install via vcpkg
vcpkg install rocket-welder-sdk

# Or integrate with CMake
find_package(rocket-welder-sdk CONFIG REQUIRED)
target_link_libraries(your_app PRIVATE rocket-welder-sdk::rocket-welder-sdk)
```

### C# with NuGet

[![NuGet Downloads](https://img.shields.io/nuget/dt/RocketWelder.SDK.svg)](https://www.nuget.org/packages/RocketWelder.SDK/)

```bash
# Package Manager Console
Install-Package RocketWelder.SDK

# .NET CLI
dotnet add package RocketWelder.SDK

# PackageReference in .csproj
<PackageReference Include="RocketWelder.SDK" Version="1.0.*" />
```

### Python with pip

[![PyPI Downloads](https://img.shields.io/pypi/dm/rocket-welder-sdk.svg)](https://pypi.org/project/rocket-welder-sdk/)

```bash
# Install from PyPI
pip install rocket-welder-sdk

# Install with optional dependencies
pip install rocket-welder-sdk[opencv]  # Includes OpenCV
pip install rocket-welder-sdk[all]     # All optional dependencies

# Install specific version
pip install rocket-welder-sdk==1.0.0
```

## Quick Start

### C++ Quick Start
```cpp
#include <rocket_welder/client.hpp>

auto client = rocket_welder::Client::from_connection_string("shm://my-buffer");
client.on_frame([](cv::Mat& frame) {
    // Process frame
});
client.start();
```

### C# Quick Start  
```csharp
using RocketWelder.SDK;

var client = RocketWelderClient.FromConnectionString("shm://my-buffer");
client.Start(frame => {
    // Process frame
});
```

### Python Quick Start
```python
import rocket_welder_sdk as rw

client = rw.Client.from_connection_string("shm://my-buffer")

@client.on_frame
def process(frame):
    # Process frame
    pass

client.start()
```

## Usage Examples

### C++

```cpp
#include <rocket_welder/client.hpp>
#include <opencv2/opencv.hpp>

int main(int argc, char* argv[]) {
    // Best practice: use from() which:
    // 1. Checks environment variable (CONNECTION_STRING)
    // 2. Overrides with command line args if provided
    auto client = rocket_welder::Client::from(argc, argv);
    
    // Or specify connection string directly
    auto client = rocket_welder::Client::from_connection_string(
        "shm://camera_feed?buffer_size=20MB&metadata_size=4KB"
    );
    
    // Process frames as OpenCV Mat (mutable by default)
    client.on_frame([](cv::Mat& frame) {
        // Add overlay text - zero copy!
        cv::putText(frame, "Processing", cv::Point(10, 30),
                    cv::FONT_HERSHEY_SIMPLEX, 1.0, cv::Scalar(0, 255, 0), 2);
        
        // Add timestamp overlay
        auto now = std::chrono::system_clock::now();
        auto time_t = std::chrono::system_clock::to_time_t(now);
        cv::putText(frame, std::ctime(&time_t), cv::Point(10, 60),
                    cv::FONT_HERSHEY_SIMPLEX, 0.5, cv::Scalar(255, 255, 255), 1);
    });
    
    client.start();
    return 0;
}
```

### C#

```csharp
using RocketWelder.SDK;
using OpenCvSharp;

class Program
{
    static void Main(string[] args)
    {
        // Best practice: use From() which:
        // 1. Checks environment variable (CONNECTION_STRING)
        // 2. Overrides with command line args if provided
        var client = RocketWelderClient.From(args);
        
        // Or specify connection string directly
        var client = RocketWelderClient.FromConnectionString(
            "shm://camera_feed?buffer_size=20MB&metadata_size=4KB"
        );
        
        int frameCount = 0;
        
        // Process frames as OpenCV Mat
        client.Start((Mat frame) => 
        {
            // Add overlay text
            Cv2.PutText(frame, "Processing", new Point(10, 30),
                       HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 0), 2);
            
            // Add frame counter overlay
            Cv2.PutText(frame, $"Frame: {frameCount++}", new Point(10, 60),
                       HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 255), 1);
        });
    }
}
```

### Python

```python
import rocket_welder_sdk as rw
import cv2
import sys

# Best practice: use from_args() which:
# 1. Checks environment variable (CONNECTION_STRING)
# 2. Overrides with command line args if provided
client = rw.Client.from_args(sys.argv)

# Or specify connection string directly
client = rw.Client.from_connection_string("shm://camera_feed?buffer_size=20MB&metadata_size=4KB")

# Process frames as numpy arrays (OpenCV compatible)
@client.on_frame
def process_frame(frame: np.ndarray):
    # Add overlay text - zero copy!
    cv2.putText(frame, "Processing", (10, 30),
                cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)
    
    # Add timestamp overlay
    from datetime import datetime
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    cv2.putText(frame, timestamp, (10, 60),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)

client.start()

# Or use iterator pattern
for frame in client.frames():
    # Each frame is a numpy array
    print(f"Received frame: {frame.shape}")
```

## Docker Integration

### C++ Dockerfile

```dockerfile
FROM ubuntu:22.04 AS builder

# Install build tools and OpenCV
RUN apt-get update && apt-get install -y \
    build-essential \
    cmake \
    libopencv-dev

# Install Rocket Welder SDK via vcpkg
RUN vcpkg install rocket-welder-sdk

# Build your application
WORKDIR /app
COPY . .
RUN cmake . && make

FROM ubuntu:22.04
RUN apt-get update && apt-get install -y libopencv-dev
COPY --from=builder /app/my_app /usr/local/bin/
CMD ["my_app"]
```

### C# Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

WORKDIR /app
COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=builder /app/out .
CMD ["dotnet", "MyApp.dll"]
```

### Python Dockerfile

```dockerfile
FROM python:3.11-slim

# Install OpenCV and other dependencies
RUN apt-get update && apt-get install -y \
    python3-opencv \
    && rm -rf /var/lib/apt/lists/*

# Install Rocket Welder SDK and ML frameworks
RUN pip install --no-cache-dir \
    rocket-welder-sdk \
    numpy \
    ultralytics  # Example: YOLO

WORKDIR /app
COPY . .

CMD ["python", "app.py"]
```

## Protocol Details

### Shared Memory Protocol (shm://)

High-performance local data transfer between processes:

- **Performance**: Minimal latency, maximum throughput
- **Use Cases**: Local processing, multi-container applications on same host

### MJPEG over HTTP (mjpeg+http://)

Motion JPEG streaming over HTTP:

- **Performance**: Good balance of quality and bandwidth
- **Advantages**: Wide compatibility, firewall-friendly, browser support
- **Use Cases**: Network streaming, web applications, remote monitoring

### MJPEG over TCP (mjpeg+tcp://)

Motion JPEG streaming over raw TCP socket:

- **Performance**: Lower latency than HTTP, less protocol overhead
- **Advantages**: Direct socket connection, minimal overhead, suitable for local networks
- **Use Cases**: Low-latency streaming, embedded systems, industrial applications

## Building from Source

### Prerequisites

- CMake 3.20+
- C++20 compiler
- Python 3.8+ (for Python bindings)
- .NET 6.0+ SDK (for C# bindings)
- OpenCV 4.0+ (optional, for image processing)

### Build Instructions

```bash
git clone https://github.com/modelingevolution/rocket-welder-sdk.git
cd rocket-welder-sdk

# Build all libraries
mkdir build && cd build
cmake ..
make -j$(nproc)

# Run tests
ctest

# Install
sudo make install
```

## API Reference

Detailed API documentation for each language:

- [C++ API Reference](docs/cpp-api.md)
- [C# API Reference](docs/csharp-api.md)
- [Python API Reference](docs/python-api.md)

## Examples

See the [examples](examples/) directory for complete working examples:

- [Simple Frame Reader](examples/simple-reader/)
- [Frame Processor](examples/frame-processor/)
- [Multi-Stream Handler](examples/multi-stream/)
- [Performance Benchmark](examples/benchmark/)

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

- **Issues**: [GitHub Issues](https://github.com/modelingevolution/rocket-welder-sdk/issues)
- **Discussions**: [GitHub Discussions](https://github.com/modelingevolution/rocket-welder-sdk/discussions)
- **Documentation**: [https://docs.rocket-welder.io](https://docs.rocket-welder.io)

## Technical Details

### GStreamer Integration

The SDK integrates with GStreamer pipelines through specialized elements:
- **zerosink**: Simple sink element for writing video frames
- **zerobuffer**: Processing element with bidirectional communication using DuplexChannel

### Zero-Copy Buffer Technology

For shared memory protocol, the SDK uses:
- **C++**: Zero-Copy-Buffer (via vcpkg) - Returns cv::Mat with zero-copy access
- **C#**: ZeroBuffer (via NuGet) - Returns OpenCvSharp.Mat with zero-copy access
- **Python**: zero-buffer (via pip) - Returns numpy arrays compatible with OpenCV

The SDK leverages DuplexChannel for bidirectional communication, enabling:
- Zero-copy frame access as OpenCV Mat objects
- In-place frame processing without memory allocation
- Direct memory mapping between producer and consumer
- Efficient metadata passing alongside frame data

This technology enables direct memory access without data duplication, providing maximum performance for local processing scenarios.

## Acknowledgments

- GStreamer Project for the multimedia framework
- ZeroBuffer contributors for the zero-copy buffer implementation