# 08 - YOLO Segmentation Stress Generator

Heavy YOLOv8 instance segmentation feed for stress-testing the native-player
overlay rendering pipeline. Sink-only: no frame mutation. Video stays in
GStreamer (`filesrc -> zerosink`); this app only consumes frames and writes
dense, high-vertex polygons to the segmentation sink.

**Requires** an NVIDIA GPU + NVIDIA Container Toolkit.

## Build

From the `python/` SDK root:

```bash
docker build -f examples/08-yolo-stress/Dockerfile -t rw-yolo-stress .
```

## Stress knobs (env vars)

| Var                   | Default          | Meaning                                                |
|-----------------------|------------------|--------------------------------------------------------|
| `YOLO_MODEL`          | `yolov8x-seg.pt` | Ultralytics model (try `yolov8m-seg.pt` for less load) |
| `CONF_THRESHOLD`      | `0.05`           | Lower = more instances per frame                       |
| `INSTANCE_MULTIPLIER` | `1`              | Re-emit each polygon N times with jitter               |
| `CONTOUR_MODE`        | `none`           | `none` keeps every mask vertex; `simple` decimates     |

`CONTOUR_MODE=none` produces hundreds–thousands of vertices per polygon —
the path that PR #30 (native-player) un-truncated.

## Run with docker-compose (easiest)

```bash
cp .env.example .env      # edit VIDEO_PATH and PLUGINS_PATH
docker compose up --build
```

`--build` is only needed the first time (or after code changes); subsequent
runs can use plain `docker compose up`.

This starts the GStreamer feeder (`zerosink`) and the YOLO stress
container together. Point native-player at `shm://rw-stress?...` and the
unix socket from `SEG_SOCKET` (default `/tmp/rw-seg.sock`).

To overlay it onto the rocket-welder2 stack:

```bash
cd /path/to/rocket-welder2/src
docker compose \
  -f docker-compose.rw.yml -f docker-compose.rw.x64.yml \
  -f docker-compose.rw.nvidia.yml \
  -f /path/to/rocket-welder-sdk/python/examples/08-yolo-stress/docker-compose.yml \
  up
```

## Run manually

Three components: GStreamer feeder, this stress generator, native-player.

### 1. GStreamer feeder (host or container)

The `zerosink` element ships in the streamer plugin set:

```bash
GST_PLUGIN_PATH=/mnt/d/source/modelingevolution/streamer/src/out/build/Linux-WSL-Debug/app/plugins \
gst-launch-1.0 -v \
  filesrc location=/path/to/sample-1080p.mp4 \
  ! decodebin ! videoconvert ! videoscale \
  ! video/x-raw,format=RGB,width=1920,height=1080 \
  ! zerosink buffer-name=rw-stress buffer-size=67108864 metadata-size=4096
```

If you put the same plugins folder in another machine/container, point
`GST_PLUGIN_PATH` at it. The plugin folder also contains `gstmultipart.so`,
which collides with the system `multipartdemux` — harmless warnings, but to
silence them mount **only** `gstzerobuffer.so` into a clean directory.

### 2. YOLO stress generator

```bash
docker run --rm -it \
  --runtime=nvidia --gpus all \
  --ipc=host \
  -e CONNECTION_STRING="shm://rw-stress?size=64MB&metadata=4KB" \
  -e SEGMENTATION_SINK_URL="unix:///tmp/seg.sock" \
  -e YOLO_MODEL=yolov8x-seg.pt \
  -e INSTANCE_MULTIPLIER=4 \
  -e CONF_THRESHOLD=0.05 \
  -e CONTOUR_MODE=none \
  -v /tmp:/tmp \
  rw-yolo-stress
```

`--ipc=host` is required so zerobuffer's POSIX shared memory is visible
across containers. The `/tmp` mount exposes the unix socket that
native-player connects to.

### 3. native-player

Point native-player at the same `SEGMENTATION_SINK_URL` and the same
`shm://rw-stress` for the video frames. Use your existing native-player
launch flow.

## Tuning checklist

- Start with defaults; verify native-player renders without dropping.
- Bump `INSTANCE_MULTIPLIER` (2 -> 4 -> 8) until the overlay path saturates.
- Switch `YOLO_MODEL` between `yolov8m-seg.pt` and `yolov8x-seg.pt` to keep
  inference fast or maximize instance count.
- `CONF_THRESHOLD=0.01` for absolute worst-case instance density.

## Notes

- Sink-only: the input frame is never modified.
- Overlay stats (frames, fps, instances/frame, vertices/frame) are written
  via the stage writer so they show up in native-player's HUD.
- Output via `seg.append(class_id, instance_id, conf, points)` matches
  `05-ball-detector`; native-player consumes it the same way.
