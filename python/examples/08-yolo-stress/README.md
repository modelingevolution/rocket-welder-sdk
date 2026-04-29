# 08 - YOLO Segmentation Stress Generator

Heavy YOLOv8 instance segmentation feed for stress-testing the native-player
overlay rendering pipeline. Sink-only: no frame mutation.

Architecture mirrors `05-ball-detector`: the GStreamer pipeline runs on the
host (or inside rocket-welder2), this container only reads frames from the
shared zerobuffer and writes dense, high-vertex polygons to the
segmentation sink.

**Requires** an NVIDIA GPU + NVIDIA Container Toolkit on the host.

## Stress knobs (env vars)

| Var                   | Default          | Meaning                                                |
|-----------------------|------------------|--------------------------------------------------------|
| `YOLO_MODEL`          | `yolov8x-seg.pt` | Ultralytics model (try `yolov8m-seg.pt` for less load) |
| `CONF_THRESHOLD`      | `0.05`           | Lower = more instances per frame                       |
| `INSTANCE_MULTIPLIER` | `4`              | Re-emit each polygon N times with jitter               |
| `CONTOUR_MODE`        | `none`           | `none` keeps every mask vertex; `simple` decimates     |
| `BUFFER_NAME`         | `rw-stress`      | zerobuffer shm name; must match the GStreamer pipeline |
| `SEG_SOCKET`          | `/tmp/rw-seg.sock` | Unix socket native-player consumes                   |

`CONTOUR_MODE=none` produces hundreds–thousands of vertices per polygon —
the path that native-player PR #30 un-truncated.

## Build

From the SDK root:

```bash
./build_docker_samples.sh --python-only --example 08-yolo-stress
```

Produces `rocket-welder-client-python-yolo-stress:latest` (auto-detected
as a GPU example, so the Jetson variant is also built when on Jetson).

## Run

The container is managed by **rocket-welder2** — configure it on the
`zerosink` element in the pipeline editor:

- Mode: **Docker**
- Image: `rw-yolo-stress`
- Tag: `latest`

When the pipeline starts, rocket-welder2 spawns the container with the
right `CONNECTION_STRING`, `SEGMENTATION_SINK_URL`, and `/tmp` /
`/dev/shm` bind mounts (see `ZeroBufferBehavior` /
`ZeroRocketContainer`). When the pipeline stops, the container is torn
down. The shared-memory buffer name and segmentation socket path are
generated per session — no manual coordination needed.

For ad-hoc local testing without rocket-welder2:

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

A producer pipeline must be writing to the same `BUFFER_NAME` first
(e.g. `gst-launch-1.0 ... ! zerosink buffer-name=rw-stress ...`). The
container blocks on the SDK reader until the producer attaches.

## Tuning

- Defaults first; verify native-player renders without dropping.
- Bump `INSTANCE_MULTIPLIER` (2 → 4 → 8) until the overlay path saturates.
- Switch `YOLO_MODEL` between `yolov8m-seg.pt` and `yolov8x-seg.pt` to keep
  inference fast or maximize instance count.
- `CONF_THRESHOLD=0.01` for worst-case instance density.

## Troubleshooting

**`could not select device driver "nvidia"`** — NVIDIA Container Toolkit
not registered with Docker. Run
`sudo bash /tmp/install-nvidia-container-toolkit.sh`. On Docker Desktop /
WSL2, enable GPU integration in Docker Desktop's settings.

**Frames never arrive** — buffer name mismatch or the pipeline isn't
running yet. Confirm `BUFFER_NAME` matches `zerosink buffer-name=...`.
The container will block on the SDK reader until the producer attaches.

## Notes

- Sink-only: the input frame is never modified.
- Stats (frames, fps, instances/frame, vertices/frame) emitted via the
  stage writer — show up in native-player's HUD.
- `seg.append(class_id, instance_id, conf, points)` matches the
  `05-ball-detector` wire format.
