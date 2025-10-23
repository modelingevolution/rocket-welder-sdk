# RocketWelder YOLO Segmentation Client

This Docker sample demonstrates real-time YOLO instance segmentation using the RocketWelder SDK.

**⚠️ GPU Required**: This application requires NVIDIA GPU with CUDA support and will fail fast if GPU is not available.

## Features

- Real-time instance segmentation using YOLOv8-seg (nano model)
- Automatic color-coded segmentation masks for different object classes
- Bounding boxes with class labels and confidence scores
- FPS counter and performance statistics
- Support for both ONE-WAY and DUPLEX connection modes

## Building

Build the Docker image using the main build script:

```bash
# From the repository root
./build_docker_samples.sh --python-only

# Or build only the YOLO image manually
cd python
docker build -t rocket-welder-client-python-yolo:latest \
  -f examples/rocket-welder-client-python-yolo/Dockerfile \
  .
```

## Requirements

**REQUIRED**:
- NVIDIA GPU with CUDA support
- NVIDIA drivers installed on host
- NVIDIA Container Toolkit installed
- Docker configured with NVIDIA runtime

Without GPU, the application will fail immediately with a clear error message.

## Running

### Basic usage (shared memory with GPU):
```bash
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

## Notes

- The client uses `--ipc=host` to share memory with the host system
- Logs are written to `/tmp/yolo_client.log` inside the container
- Press 'q' to stop when using preview mode
- Press Ctrl+C to stop in headless mode
