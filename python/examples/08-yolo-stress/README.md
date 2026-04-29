# RocketWelder YOLO Segmentation Stress Generator

Sink-only YOLOv8 instance segmentation feed for stress-testing the
native-player overlay rendering path. Reads frames from a GStreamer
`zerosink` and writes dense, high-vertex polygons to the segmentation
sink. The input frame is never modified.

**⚠️ GPU Required**: NVIDIA GPU with CUDA support; container must run
with `--runtime=nvidia --gpus all`.

## Stress knobs

| Var | Default | Meaning |
|---|---|---|
| `YOLO_MODEL` | `yolov8x-seg.pt` | Ultralytics model (`yolov8m-seg.pt` for less load) |
| `CONF_THRESHOLD` | `0.05` | Lower → more instances per frame |
| `INSTANCE_MULTIPLIER` | `1` | Re-emit each polygon N times with jitter |
| `CONTOUR_MODE` | `none` | `none` keeps every mask vertex; `simple` decimates |

`CONTOUR_MODE=none` produces hundreds–thousands of vertices per polygon
— the path that native-player PR #30 un-truncated.

## Build

From the SDK root:

```bash
./build_docker_samples.sh --python-only --example 08-yolo-stress
```

Produces `rocket-welder-client-python-yolo-stress:latest`.

## Run

The container is managed by **rocket-welder2** — configure it on the
`zerosink` element in the pipeline editor:

- Mode: **Docker**
- Image: `rw-yolo-stress`
- Tag: `latest`

When the pipeline starts, rocket-welder2 spawns the container with
`CONNECTION_STRING`, `SEGMENTATION_SINK_URL`, and the `/tmp` /
`/dev/shm` bind mounts injected at runtime. The shared-memory buffer
name and segmentation socket path are generated per session.

For ad-hoc local testing without rocket-welder2, run a producer
pipeline first (`gst-launch-1.0 ... ! zerosink buffer-name=rw-stress
buffer-size=67108864 metadata-size=4096`) and then:

```bash
docker run --rm -it \
  --gpus all \
  --ipc=host \
  -e CONNECTION_STRING="shm://rw-stress?size=64MB&metadata=4KB" \
  -e SEGMENTATION_SINK_URL="socket:///tmp/rw-seg.sock" \
  -e INSTANCE_MULTIPLIER=4 \
  -v /tmp:/tmp \
  rw-yolo-stress
```

## Tuning

- Defaults first; verify native-player renders without dropping.
- Bump `INSTANCE_MULTIPLIER` (2 → 4 → 8) to saturate the overlay path.
- Switch `YOLO_MODEL` between `yolov8m-seg.pt` and `yolov8x-seg.pt` to
  balance inference cost vs instance count.
- `CONF_THRESHOLD=0.01` for worst-case instance density.

Stats (frames, fps, instances/frame, vertices/frame) are emitted via the
stage writer and appear in native-player's HUD.
