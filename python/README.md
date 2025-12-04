# Rocket Welder SDK

[![NuGet](https://img.shields.io/nuget/v/RocketWelder.SDK.svg)](https://www.nuget.org/packages/RocketWelder.SDK/)
[![PyPI](https://img.shields.io/pypi/v/rocket-welder-sdk.svg)](https://pypi.org/project/rocket-welder-sdk/)
[![vcpkg](https://img.shields.io/badge/vcpkg-rocket--welder--sdk-blue)](https://github.com/modelingevolution/rocket-welder-sdk-vcpkg-registry)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Client libraries for building custom AI/ML video processing containers that integrate with RocketWelder (Neuron) devices.**

## Overview

The Rocket Welder SDK enables AI/ML developers to build custom video processing containers for Neuron industrial vision devices. It provides high-performance, **zero-copy** frame access via shared memory, supporting real-time computer vision, object detection, and AI inference workloads.

**Target Audience**: AI/ML developers building containerized applications for:
- Real-time object detection (YOLO, custom models)
- Computer vision processing
- AI inference on video streams
- Industrial vision applications

## Table of Contents

- [Quick Start](#quick-start)
- [Your First AI Processing Container](#your-first-ai-processing-container)
- [Development Workflow](#development-workflow)
- [Deploying to Neuron Device](#deploying-to-neuron-device)
- [RocketWelder Integration](#rocketwelder-integration)
- [API Reference](#api-reference)
- [Production Best Practices](#production-best-practices)

## Quick Start

### Installation

| Language | Package Manager | Package Name |
|----------|----------------|--------------|
| C++ | vcpkg | rocket-welder-sdk |
| C# | NuGet | RocketWelder.SDK |
| Python | pip | rocket-welder-sdk |

#### Python
```bash
pip install rocket-welder-sdk
```

#### C#
```bash
dotnet add package RocketWelder.SDK
```

#### C++
```bash
vcpkg install rocket-welder-sdk
```

## Your First AI Processing Container

### Starting with Examples

The SDK includes ready-to-use examples in the `/examples` directory:

```
examples/
├── python/
│   ├── simple_client.py       # Timestamp overlay example
│   ├── integration_client.py  # Testing with --exit-after
│   └── Dockerfile             # Ready-to-build container
├── csharp/
│   └── SimpleClient/
│       ├── Program.cs          # Full example with UI controls
│       └── Dockerfile          # Ready-to-build container
└── cpp/
    ├── simple_client.cpp
    └── CMakeLists.txt
```

### Python Example - Simple Timestamp Overlay

```python
#!/usr/bin/env python3
import sys
import cv2
import numpy as np
from datetime import datetime
import rocket_welder_sdk as rw

# Create client - reads CONNECTION_STRING from environment or args
client = rw.Client.from_(sys.argv)

def process_frame(frame: np.ndarray) -> None:
    """Add timestamp overlay to frame - zero copy!"""
    timestamp = datetime.now().strftime("%H:%M:%S")
    cv2.putText(frame, timestamp, (10, 30),
                cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)

# Start processing
client.start(process_frame)

# Keep running
while client.is_running:
    time.sleep(0.1)
```

### Building Your Container

```bash
# Navigate to examples directory
cd python/examples

# Build Docker image
docker build -t my-ai-app:v1 -f Dockerfile ..

# Test locally with file
docker run --rm \
    -e CONNECTION_STRING="file:///data/test.mp4?loop=true" \
    -v /path/to/video.mp4:/data/test.mp4:ro \
    my-ai-app:v1
```

## Development Workflow

### Step 1: Test Locally with Video File

Start by testing your container locally before deploying to Neuron:

```bash
# Build your container
docker build -t my-ai-app:v1 -f python/examples/Dockerfile .

# Test with a video file
docker run --rm \
    -e CONNECTION_STRING="file:///data/test.mp4?loop=true&preview=false" \
    -v $(pwd)/examples/test_stream.mp4:/data/test.mp4:ro \
    my-ai-app:v1
```

You can also see preview in your terminal. 

```bash
# Install x11-apps
sudo apt install x11-apps

# Test with a video file
docker run --rm \
    -e CONNECTION_STRING="file:///data/test.mp4?loop=true&preview=true" \
    -e DISPLAY=$DISPLAY \
    -v /path/to/your/file.mp4:/data/test.mp4:ro -v /tmp/.X11-unix:/tmp/.X11-unix     my-ai-app:v1
```

### Step 2: Test with Live Stream from Neuron

Once your container works locally, test it with a live stream from your Neuron device:

#### Configure RocketWelder Pipeline for Streaming

1. Access RocketWelder UI on your Neuron device (usually `http://neuron-ip:8080`)
2. Open **Pipeline Designer**
3. Click **"Add Element"**
4. Choose your video source (e.g., `pylonsrc` for Basler cameras)
5. Add **caps filter** to specify format: `video/x-raw,width=1920,height=1080,format=GRAY8`
6. Add **jpegenc** element
7. Add **tcpserversink** element with properties:
   - `host`: `0.0.0.0`
   - `port`: `5000`
8. Start the pipeline

Example pipeline:
```
pylonsrc → video/x-raw,width=1920,height=1080,format=GRAY8 → queue max-buffers-size=1, Leaky=Upstream → jpegenc → tcpserversink host=0.0.0.0 port=5000 sync=false
```

#### Connect from Your Dev Laptop

```bash
# On your laptop - connect to Neuron's TCP stream
docker run --rm \
    -e CONNECTION_STRING="mjpeg+tcp://neuron-ip:5000" \
    --network host \
    my-ai-app:v1
```

You can also see preview in your terminal. 
```bash
docker run --rm \
    -e CONNECTION_STRING="mjpeg+tcp://<neuron-ip>:<tcp-server-sink-port>?preview=true" \
    -e DISPLAY=$DISPLAY \
    -v /tmp/.X11-unix:/tmp/.X11-unix \
    --network host my-ai-app:v1
```

This allows you to:
- Test your AI processing with real camera feeds
- Debug frame processing logic
- Measure performance with actual hardware

## Deploying to Neuron Device

### Option 1: Local Docker Registry (Recommended for Development)

This is the fastest workflow for iterative development:

#### Setup Registry on Your Laptop (One-time)

```bash
# Start a local Docker registry
docker run -d \
    -p 5000:5000 \
    --restart=always \
    --name registry \
    registry:2

# Verify it's running
curl http://localhost:5000/v2/_catalog
```

#### Configure Neuron to Use Your Laptop Registry (One-time)

```bash
# SSH to Neuron device
ssh user@neuron-ip

# Edit Docker daemon config
sudo nano /etc/docker/daemon.json

# Add your laptop's IP to insecure registries:
{
  "insecure-registries": ["laptop-ip:5000"]
}

# Restart Docker
sudo systemctl restart docker
```

**Note**: Replace `laptop-ip` with your laptop's actual IP address (e.g., `192.168.1.100`).
To find it: `ip addr show` or `ifconfig`

#### Push Image to Your Registry

```bash
# On your laptop - tag for local registry
docker tag my-ai-app:v1 localhost:5000/my-ai-app:v1

# Push to registry
docker push localhost:5000/my-ai-app:v1

# Verify push
curl http://localhost:5000/v2/my-ai-app/tags/list
```

#### Pull on Neuron Device

```bash
# SSH to Neuron
ssh user@neuron-ip

# Pull from laptop registry
docker pull laptop-ip:5000/my-ai-app:v1

# Verify image
docker images | grep my-ai-app
```

#### Workflow Summary

```bash
# Iterative development loop:
1. Edit code on laptop
2. docker build -t localhost:5000/my-ai-app:v1 .
3. docker push localhost:5000/my-ai-app:v1
4. Configure in RocketWelder UI (once)
5. RocketWelder pulls and runs your container
```

### Option 2: Export/Import (For One-off Transfers)

Useful when you don't want to set up a registry:

```bash
# On your laptop - save image to tar
docker save my-ai-app:v1 | gzip > my-ai-app-v1.tar.gz

# Transfer to Neuron
scp my-ai-app-v1.tar.gz user@neuron-ip:/tmp/

# SSH to Neuron and load
ssh user@neuron-ip
docker load < /tmp/my-ai-app-v1.tar.gz

# Verify
docker images | grep my-ai-app
```

### Option 3: Azure Container Registry (Production)

For production deployments:

```bash
# Login to ACR (Azure Container Registry)
az acr login --name your-registry

# Tag and push
docker tag my-ai-app:v1 your-registry.azurecr.io/my-ai-app:v1
docker push your-registry.azurecr.io/my-ai-app:v1

# Configure Neuron to use ACR (credentials required)
```

## RocketWelder Integration

### Understanding zerosink vs zerofilter

RocketWelder provides two GStreamer elements for container integration:

| Element | Mode | Use Case |
|---------|------|----------|
| **zerosink** | One-way | RocketWelder → Your Container<br/>Read frames, process, log results |
| **zerofilter** | Duplex | RocketWelder ↔ Your Container<br/>Read frames, modify them, return modified frames |

**Most AI use cases use `zerosink`** (one-way mode):
- Object detection (draw bounding boxes)
- Classification (overlay labels)
- Analytics (count objects, log events)

**Use `zerofilter`** (duplex mode) when:
- You need to modify frames and return them to the pipeline
- Real-time visual effects/filters
- Frame enhancement before encoding

### Configuring Your Container in RocketWelder

#### Step-by-Step UI Configuration

1. **Access RocketWelder UI**
   - Navigate to `http://neuron-ip:8080`
   - Log in to your Neuron device

2. **Open Pipeline Designer**
   - Go to **Pipelines** section
   - Create new pipeline or edit existing

3. **Add Video Source**
   - Click **"Add Element"**
   - Choose your camera source (e.g., `pylonsrc`, `aravissrc`)
   - Configure camera properties

4. **Add Format** 
   - Add caps filter: `video/x-raw,format=RGB`

5. **Add queueue**
   - max-num-buffers: 1
   - leaky: upstream

5. **Add ZeroBuffer Element**
   - Click **"Add Element"**
   - Select **"zerosink"** (or **"zerofilter"** for duplex mode)
   - Scroll down in properties panel on the right

6. **Configure Consumer**
   - Toggle **"Enable ZeroBuffer Consumer"** ✓
   - Select **"Consumer Mode"** dropdown
   - Choose **"Docker Container"** (not Process)

7. **Configure Docker Settings**
   - **Image**: Enter your image name
     - Local registry: `laptop-ip:5000/my-ai-app`
     - ACR: `your-registry.azurecr.io/my-ai-app`
     - Loaded image: `my-ai-app`
   - **Tag**: `v1` (or your version tag)
   - **Environment Variables**: (optional) Add custom env vars if needed
   - **Auto-remove**: ✓ (recommended - cleans up container on stop)

8. **Save Pipeline Configuration**

9. **Start Pipeline**
   - Click **"Start"** button
   - RocketWelder will automatically:
     - Pull your Docker image (if not present)
     - Create shared memory buffer
     - Launch your container with `CONNECTION_STRING` env var
     - Start streaming frames

### Automatic Environment Variables

When RocketWelder launches your container, it automatically sets:

```bash
CONNECTION_STRING=shm://zerobuffer-abc123-456?size=20MB&metadata=4KB&mode=oneway
SessionId=def789-012                    # For UI controls (if enabled)
EventStore=esdb://host.docker.internal:2113?tls=false  # For external controls
```

Your SDK code simply reads `CONNECTION_STRING`:

```python
# Python - automatically reads CONNECTION_STRING from environment
client = rw.Client.from_(sys.argv)
```

```csharp
// C# - automatically reads CONNECTION_STRING
var client = RocketWelderClient.From(args);
```

### Example Pipeline Configurations

#### AI Object Detection Pipeline

```
pylonsrc
  → video/x-raw,width=1920,height=1080,format=Gray8
  → videoconvert
  → zerosink
     └─ Docker: laptop-ip:5000/yolo-detector:v1
```

Your YOLO container receives frames, detects objects, draws bounding boxes.

#### Dual Output: AI Processing

```
pylonsrc
  → video/x-raw,width=1920,height=1080,format=Gray8
  → tee name=t
      t. → queue → jpegenc → tcpserversink
      t. → queue → zerofilter → queue → jpegenc → tcpserversink
           └─ Docker: laptop-ip:5000/my-ai-app:v1
```

#### Real-time Frame Enhancement with Live Preview (Duplex Mode)

```
  → pylonsrc hdr-sequence="5000,5500" hdr-sequence2="19,150" hdr-profile=0
  → video/x-raw,width=1920,height=1080,format=Gray8
  → queue max-num-buffers=1 leaky=upstream
  → hdr mode=burst num-frames=2
  → sortingbuffer 
  → queue max-num-buffers=1 leaky=upstream
  → zerofilter
     └─ Docker: laptop-ip:5000/frame-enhancer:v1
  → queue max-num-buffers=1 leaky=upstream
  → jpegenc
  → multipartmux enable-html=true
  → tcpserversink host=0.0.0.0 port=5000 sync=false
```

In duplex mode with `zerofilter`, your container:
1. Receives input frames via shared memory (automatically configured by RocketWelder)
2. Processes them in real-time (e.g., AI enhancement, object detection, overlays)
3. Writes modified frames back to shared memory
4. Modified frames flow back into RocketWelder pipeline for streaming/display

**Pipeline elements explained:**
- `pylonsrc hdr-sequence="5000,5500"`: Configures HDR Profile 0 with 5000μs and 5500μs exposures (cycles automatically via camera sequencer)
- `hdr-sequence2="19,150"`: Configures HDR Profile 1 with 2 exposures for runtime switching
- `hdr-profile=0`: Starts with Profile 0 (can be changed at runtime to switch between lighting conditions), requires a branch with histogram, dre and pylontarget.
- `hdr processing-mode=burst num-frames=2`: HDR blending element - combines multiple exposures into single HDR frame
- `sortingbuffer skip-behaviour=hdr`: Reorders out-of-order frames from Pylon camera using HDR metadata (MasterSequence, ExposureSequenceIndex) - automatically detects frame order using `image_number` from Pylon metadata 
- `zerofilter`: Bidirectional shared memory connection to your Docker container
- `jpegenc`: JPEG compression for network streaming
- `multipartmux enable-html=true`: Creates MJPEG stream with CORS headers for browser viewing
- `tcpserversink`: Streams to RocketWelder UI at `http://neuron-ip:5000`

**View live preview:**
Open in browser: `http://neuron-ip:5000` to see the processed video stream with your AI enhancements in real-time!

**HDR Profile Switching:**
The dual-profile system allows runtime switching between lighting conditions:
- Profile 0 (2 exposures): Fast cycling for normal conditions
- Profile 1 (2 exposures): More exposures for challenging lighting
- Switch dynamically via `hdr-profile` property without stopping the pipeline (requires another branch, histogram, dre, pylon-target)

**Use case examples:**
- **AI object detection**: Draw bounding boxes that appear in RocketWelder preview
- **Real-time enhancement**: AI super-resolution, denoising, stabilization
- **Visual feedback**: Add crosshairs, tracking overlays, status indicators
- **Quality control**: Highlight defects or areas of interest in industrial inspection

## Connection String Format

The SDK uses URI-style connection strings:

```
protocol://[host[:port]]/[path][?param1=value1&param2=value2]
```

### Supported Protocols

#### Shared Memory (Production - Automatic)
```
shm://buffer-name?size=20MB&metadata=4KB&mode=oneway
```

When deployed with RocketWelder, this is set automatically via `CONNECTION_STRING` environment variable.

**Parameters:**
- `size`: Buffer size (default: 20MB, supports: B, KB, MB, GB)
- `metadata`: Metadata size (default: 4KB)
- `mode`: `oneway` (zerosink) or `duplex` (zerofilter)

#### File Protocol (Local Testing)
```
file:///path/to/video.mp4?loop=true&preview=false
```

**Parameters:**
- `loop`: Loop playback (`true`/`false`, default: `false`)
- `preview`: Show preview window (`true`/`false`, default: `false`)

#### MJPEG over TCP (Development/Testing)
```
mjpeg+tcp://neuron-ip:5000
```

Connect to RocketWelder's `tcpserversink` for development testing.

#### MJPEG over HTTP
```
mjpeg+http://camera-ip:8080
```

For network cameras or HTTP streamers.

## API Reference

### Python API

```python
import rocket_welder_sdk as rw

# Create client (reads CONNECTION_STRING from env or args)
client = rw.Client.from_(sys.argv)

# Or specify connection string directly
client = rw.Client.from_connection_string("shm://buffer-name?size=20MB")

# Process frames - one-way mode
@client.on_frame
def process_frame(frame: np.ndarray) -> None:
    # frame is a numpy array (height, width, channels)
    # Modify in-place for zero-copy performance
    cv2.putText(frame, "AI Processing", (10, 30),
                cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)

# Process frames - duplex mode
def process_frame_duplex(input_frame: np.ndarray, output_frame: np.ndarray) -> None:
    # Copy input to output and modify
    np.copyto(output_frame, input_frame)
    # Add AI overlay to output_frame
    cv2.putText(output_frame, "Processed", (10, 30),
                cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)

# Start processing
client.start(process_frame)  # or process_frame_duplex for duplex mode

# Keep running
while client.is_running:
    time.sleep(0.1)

# Stop
client.stop()
```

### C# API

```csharp
using RocketWelder.SDK;
using Emgu.CV;

// Create client (reads CONNECTION_STRING from env or config)
var client = RocketWelderClient.From(args);

// Or specify connection string directly
var client = RocketWelderClient.FromConnectionString("shm://buffer-name?size=20MB");

// Process frames - one-way mode
client.Start((Mat frame) =>
{
    // frame is an Emgu.CV.Mat (zero-copy)
    CvInvoke.PutText(frame, "AI Processing", new Point(10, 30),
                     FontFace.HersheySimplex, 1.0, new MCvScalar(0, 255, 0), 2);
});

// Process frames - duplex mode
client.Start((Mat input, Mat output) =>
{
    input.CopyTo(output);
    CvInvoke.PutText(output, "Processed", new Point(10, 30),
                     FontFace.HersheySimplex, 1.0, new MCvScalar(0, 255, 0), 2);
});
```

### C++ API

```cpp
#include <rocket_welder/client.hpp>
#include <opencv2/opencv.hpp>

// Create client (reads CONNECTION_STRING from env or args)
auto client = rocket_welder::Client::from(argc, argv);

// Or specify connection string directly
auto client = rocket_welder::Client::from_connection_string("shm://buffer-name?size=20MB");

// Process frames - one-way mode
client.on_frame([](cv::Mat& frame) {
    // frame is a cv::Mat reference (zero-copy)
    cv::putText(frame, "AI Processing", cv::Point(10, 30),
                cv::FONT_HERSHEY_SIMPLEX, 1.0, cv::Scalar(0, 255, 0), 2);
});

// Process frames - duplex mode
client.on_frame([](const cv::Mat& input, cv::Mat& output) {
    input.copyTo(output);
    cv::putText(output, "Processed", cv::Point(10, 30),
                cv::FONT_HERSHEY_SIMPLEX, 1.0, cv::Scalar(0, 255, 0), 2);
});

// Start processing
client.start();
```

## Production Best Practices

### Performance Optimization

1. **Zero-Copy Processing**
   - Modify frames in-place when possible
   - Avoid unnecessary memory allocations in the frame processing loop
   - Use OpenCV operations that work directly on the frame buffer

2. **Frame Rate Management**
   ```python
   # Process every Nth frame for expensive AI operations
   frame_count = 0

   def process_frame(frame):
       global frame_count
       frame_count += 1
       if frame_count % 5 == 0:  # Process every 5th frame
           run_expensive_ai_model(frame)
   ```

3. **Logging**
   - Use structured logging with appropriate levels
   - Avoid logging in the frame processing loop for production
   - Log only important events (errors, detections, etc.)

### Error Handling

```python
import logging
import rocket_welder_sdk as rw

logger = logging.getLogger(__name__)

client = rw.Client.from_(sys.argv)

def on_error(sender, error):
    logger.error(f"Client error: {error.Exception}")
    # Implement recovery logic or graceful shutdown

client.OnError += on_error
```

### Monitoring

```python
import time
from datetime import datetime

class FrameStats:
    def __init__(self):
        self.frame_count = 0
        self.start_time = time.time()

    def update(self):
        self.frame_count += 1
        if self.frame_count % 100 == 0:
            elapsed = time.time() - self.start_time
            fps = self.frame_count / elapsed
            logger.info(f"Processed {self.frame_count} frames, {fps:.1f} FPS")

stats = FrameStats()

def process_frame(frame):
    stats.update()
    # Your processing logic
```

### Docker Best Practices

1. **Use Multi-stage Builds**
   ```dockerfile
   FROM python:3.12-slim as builder
   # Build dependencies

   FROM python:3.12-slim
   # Copy only runtime artifacts
   ```

2. **Minimize Image Size**
   - Use slim base images
   - Remove build tools in final stage
   - Clean apt cache: `rm -rf /var/lib/apt/lists/*`

3. **Health Checks**
   ```dockerfile
   HEALTHCHECK --interval=30s --timeout=3s \
       CMD pgrep -f my_app.py || exit 1
   ```

4. **Resource Limits** (in RocketWelder docker-compose or deployment)
   ```yaml
   deploy:
     resources:
       limits:
         cpus: '2.0'
         memory: 2G
   ```

## Examples

The `examples/` directory contains complete working examples:

- **python/simple_client.py** - Minimal timestamp overlay
- **python/integration_client.py** - Testing with --exit-after flag
- **python/advanced_client.py** - Full-featured with UI controls
- **csharp/SimpleClient/** - Complete C# example with crosshair controls
- **cpp/simple_client.cpp** - C++ example

## Troubleshooting

### Container Doesn't Start

**Check Docker logs:**
```bash
docker ps -a | grep my-ai-app
docker logs <container-id>
```

**Common issues:**
- Image not found (check `docker images`)
- Insecure registry not configured on Neuron

### Cannot Pull from Laptop Registry

```bash
# On Neuron - test connectivity
ping laptop-ip

# Test registry access
curl http://laptop-ip:5000/v2/_catalog

# Check Docker daemon config
cat /etc/docker/daemon.json

# Restart Docker after config change
sudo systemctl restart docker
```

### SDK Connection Timeout

**Check shared memory buffer exists:**
```bash
# On Neuron device
ls -lh /dev/shm/

# Should see zerobuffer-* files
```

**Check RocketWelder pipeline status:**
- Is pipeline running?
- Is zerosink element configured correctly?
- Check RocketWelder logs for errors

### Low Frame Rate / Performance

1. **Check CPU usage:** `htop` or `docker stats`
2. **Reduce AI model complexity** or process every Nth frame
3. **Profile your code** to find bottlenecks
4. **Use GPU acceleration** if available (NVIDIA runtime)

## Support

- **Issues**: [GitHub Issues](https://github.com/modelingevolution/rocket-welder-sdk/issues)
- **Discussions**: [GitHub Discussions](https://github.com/modelingevolution/rocket-welder-sdk/discussions)
- **Documentation**: [https://docs.rocket-welder.io](https://docs.rocket-welder.io)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- GStreamer Project for the multimedia framework
- ZeroBuffer contributors for the zero-copy buffer implementation
- OpenCV community for computer vision tools
