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

## Run

### 1. Start a GStreamer pipeline that writes into the zerobuffer

Either via rocket-welder2 (recommended — uses its lifecycle/UI):

```
rw pipeline create "filesrc location=/path/to/video.mp4 ! decodebin \
  ! videoconvert ! videoscale \
  ! video/x-raw,format=RGB,width=1920,height=1080 \
  ! zerosink buffer-name=rw-stress buffer-size=67108864 metadata-size=4096"
rw pipeline start <id>
```

or directly on the host with `gst-launch-1.0`:

```bash
GST_PLUGIN_PATH=/mnt/d/source/modelingevolution/streamer/src/out/build/Linux-WSL-Debug/app/plugins \
gst-launch-1.0 -v \
  filesrc location=/path/to/sample-1080p.mp4 \
  ! decodebin ! videoconvert ! videoscale \
  ! video/x-raw,format=RGB,width=1920,height=1080 \
  ! zerosink buffer-name=rw-stress buffer-size=67108864 metadata-size=4096
```

### 2. Start the YOLO stress container

```bash
cp .env.example .env      # tweak knobs if needed
docker compose up --build
```

`--build` only on first run / after code changes.

`--ipc=host` (already in compose) is what lets zerobuffer's POSIX shared
memory work across host ↔ container. `/tmp` is bind-mounted so the unix
socket native-player consumes is visible to it.

### 3. Point native-player at the same buffer + socket

`shm://${BUFFER_NAME}?size=64MB&metadata=4KB` for frames,
`unix://${SEG_SOCKET}` for the segmentation overlay.

## Standalone `docker run` (no compose)

```bash
docker run --rm -it \
  --gpus all \
  --ipc=host \
  -e CONNECTION_STRING="shm://rw-stress?size=64MB&metadata=4KB" \
  -e SEGMENTATION_SINK_URL="unix:///tmp/rw-seg.sock" \
  -e INSTANCE_MULTIPLIER=4 \
  -v /tmp:/tmp \
  rw-yolo-stress
```

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
