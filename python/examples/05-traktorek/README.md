# RocketWelder YOLO Segmentation Client

This Docker sample demonstrates real-time YOLO instance segmentation using the RocketWelder SDK.

**⚠️ GPU Required**: This application requires NVIDIA GPU with CUDA support and will fail fast if GPU is not available.

## Files Overview

### Application Files
- **`main.py`** - Main RocketWelder YOLO segmentation client application
  - Integrates YOLO with RocketWelder SDK for real-time video processing
  - Supports shared memory (IPC) connections
  - Production-ready application

- **`test_yolo_gpu.py`** - Standalone YOLO GPU acceleration test
  - Tests YOLO inference on GPU without RocketWelder SDK
  - Useful for verifying GPU acceleration works
  - Processes video files or webcam input

### Docker Files
- **`Dockerfile`** - Standard x86_64 Dockerfile
  - For Intel/AMD systems with NVIDIA GPUs
  - Uses Python 3.12 base image

- **`Dockerfile.jetson`** - Jetson-optimized Dockerfile
  - **Use this for NVIDIA Jetson devices** (Orin, Xavier, Nano, etc.)
  - Uses L4T PyTorch base with pre-installed CUDA support
  - Avoids OpenCV version conflicts
  - Built automatically with `--jetson` flag or auto-detected

- **`Dockerfile.test`** - Minimal test Dockerfile for Jetson
  - Simple standalone test without RocketWelder SDK
  - Useful for debugging GPU issues
  - Runs `test_yolo_gpu.py`

## Features

- Real-time instance segmentation using YOLOv8-seg (nano model)
- Automatic color-coded segmentation masks for different object classes
- Bounding boxes with class labels and confidence scores
- FPS counter and performance statistics
- Support for both ONE-WAY and DUPLEX connection modes

## Building

### For NVIDIA Jetson Devices (Orin, Xavier, Nano)

The build script auto-detects Jetson devices and builds the optimized image:

```bash
# From the repository root - auto-detects Jetson
./build_docker_samples.sh --python-only

# Or explicitly enable Jetson build
./build_docker_samples.sh --python-only --jetson

# Or build manually
cd python
docker build -t rocket-welder-client-python-yolo:jetson \
  -f examples/rocket-welder-client-python-yolo/Dockerfile.jetson \
  .
```

### For Standard x86_64 Systems with NVIDIA GPU

```bash
# From the repository root
./build_docker_samples.sh --python-only --no-jetson

# Or build manually
cd python
docker build -t rocket-welder-client-python-yolo:latest \
  -f examples/rocket-welder-client-python-yolo/Dockerfile \
  .
```

### Testing GPU Acceleration (Jetson)

Before running the full application, test that GPU acceleration works:

```bash
# Build the test image
cd python/examples/rocket-welder-client-python-yolo
docker build -t yolo-gpu-test:jetson -f Dockerfile.test .

# Test with a video file
docker run --rm --runtime=nvidia --gpus all \
  -v /path/to/video.mp4:/app/test.mp4:ro \
  yolo-gpu-test:jetson /app/test.mp4
```

## Requirements

**REQUIRED**:
- NVIDIA GPU with CUDA support
- NVIDIA drivers installed on host
- NVIDIA Container Toolkit installed
- Docker configured with NVIDIA runtime

Without GPU, the application will fail immediately with a clear error message.

## Running

### On Jetson Devices

```bash
# Basic usage (shared memory with GPU)
docker run --rm -it \
  --runtime=nvidia \
  --gpus all \
  -e CONNECTION_STRING="shm://test_buffer?size=10MB&metadata=4KB" \
  --ipc=host \
  rocket-welder-client-python-yolo:jetson
```

### On x86_64 Systems

```bash
# Basic usage (shared memory with GPU)
docker run --rm -it \
  --runtime=nvidia \
  --gpus all \
  -e CONNECTION_STRING="shm://test_buffer?size=10MB&metadata=4KB" \
  --ipc=host \
  rocket-welder-client-python-yolo:latest
```

### With preview window (requires X11 + GPU):
```bash
# First allow Docker to access display
xhost +local:docker

docker run --rm -it \
  --runtime=nvidia \
  --gpus all \
  -e CONNECTION_STRING="shm://test_buffer?size=10MB&metadata=4KB&preview=true" \
  -e DISPLAY=$DISPLAY \
  -v /tmp/.X11-unix:/tmp/.X11-unix:rw \
  --ipc=host \
  rocket-welder-client-python-yolo:latest
```

## Model Information

- **Model**: YOLOv8n-seg (nano segmentation model)
- **Classes**: 80 COCO dataset classes
- **Download**: Model is automatically downloaded on first run (or pre-downloaded during build)

## Performance

The nano model (yolov8n-seg.pt) provides a good balance between speed and accuracy:
- Fast inference suitable for real-time processing
- Smaller model size (~7MB)
- Good for deployment scenarios

For higher accuracy, you can modify `main.py` to use:
- `yolov8s-seg.pt` (small)
- `yolov8m-seg.pt` (medium)
- `yolov8l-seg.pt` (large)
- `yolov8x-seg.pt` (extra-large)

## Output

The client processes frames and overlays:
1. Colored segmentation masks (semi-transparent)
2. Bounding boxes for each detected object
3. Class labels with confidence scores
4. Real-time FPS statistics

## Troubleshooting

### Jetson-Specific Issues

**CUDA not available error:**
- Make sure you're using `Dockerfile.jetson` (or the `:jetson` tag)
- Verify NVIDIA Container Toolkit is installed: `dpkg -l | grep nvidia-container-toolkit`
- Test with the standalone GPU test first (see "Testing GPU Acceleration" above)

**OpenCV import errors:**
- The Jetson Dockerfile (`Dockerfile.jetson`) uses the L4T base image's OpenCV (with CUDA support)
- Do NOT use the standard `Dockerfile` on Jetson devices - it will have OpenCV conflicts

**Python 3.8 compatibility:**
- The L4T base image uses Python 3.8
- The code includes `from __future__ import annotations` for compatibility
- If you see `TypeError: 'type' object is not subscriptable`, rebuild the image

### General Issues

**GPU not detected:**
- Run: `docker run --rm --runtime=nvidia --gpus all ubuntu:20.04 nvidia-smi`
- If this fails, your Docker NVIDIA runtime is not configured correctly

## Notes

- The client uses `--ipc=host` to share memory with the host system
- Logs are written to `/tmp/yolo_client.log` inside the container
- Press 'q' to stop when using preview mode
- Press Ctrl+C to stop in headless mode
- For Jetson: First run may be slow as YOLO model downloads (~6MB)
