# 09 - DexiNed Contour Detection (segmentation sink)

Runs the **DexiNed** neural edge detector (the BIPED-trained "default" model used by
`KonturyPoc`) on every incoming frame, converts the predicted edges into closed
contours, and emits each contour as a **segmentation instance** over the RocketWelder
segmentation sink.

It is the neural-network counterpart of [`05-ball-detector`](../05-ball-detector):
same segmentation-writer pipeline, but the hand-written threshold detector is replaced
with DexiNed, and instead of a single ball it emits one polygon per detected contour.

> **⚠️ GPU required.** DexiNed is far too slow on CPU for streaming, so the example
> calls `torch.cuda.is_available()` at startup and **fails fast** if no GPU is present.
> Always run with `--gpus all`.

## How it works

```
RocketWelder platform
   │  CONNECTION_STRING=shm://...   (+ SEGMENTATION_SINK_URL)
   ▼
┌──────────────────────── 09-dexined container ────────────────────────┐
│  rw.Client(conn)                                                      │
│     └ DUPLEX → start_with_writers      ONE-WAY → start_with_writers_oneway │
│                                                                       │
│  per frame:                                                           │
│    1. frame → 3-ch BGR, downscale longest side ≤ MAX_SIDE, pad ×16   │
│    2. img -= MEAN_BGR ; HWC→CHW ; tensor.cuda()                       │
│    3. preds = DexiNed(tensor)         # 7 side-output heads           │
│    4. edge = sigmoid(preds[-1])       # fused head, prob map [0..1]   │
│    5. mask = edge ≥ EDGE_THRESHOLD ; morphologyEx(CLOSE)             │
│    6. contours = findContours(RETR_EXTERNAL)                         │
│         · drop area < MIN_AREA · approxPolyDP · rescale → frame px    │
│    7. seg_writer.append(class_id=1, instance_id=i, conf, points)     │
│    8. (DUPLEX) draw contours + count on the output frame             │
└──────────────────────────────────────────────────────────────────────┘
   │
   ▼  one SegmentationResult per frame (N "contour" polygon instances)
RocketWelder native-player overlay / downstream consumers
```

- **Model** – `checkpoints/BIPED/10/10_model.pth` is baked into the image. DexiNed is
  class-agnostic: it just returns edge probability, so every contour is reported under a
  single class id `1 = "contour"`.
- **Fused head** – the network returns 7 heads; we keep `preds[-1]` (the fused output),
  the same one `KonturyPoc/run_dexined.py` uses.
- **Confidence** – mean edge probability inside each contour region.
- **DUPLEX vs ONE-WAY** – DUPLEX additionally draws the contours and a count on the
  output frame for live preview; ONE-WAY is sink-only.

## Files

| File | Purpose |
|------|---------|
| `main.py` | Detector + RocketWelder segmentation-writer wiring |
| `model.py` | DexiNed network definition (copied from KonturyPoc) |
| `checkpoints/BIPED/10/10_model.pth` | BIPED-trained weights (~141 MB, baked into image) |
| `Dockerfile` | x86_64 CUDA image (`pytorch/pytorch:2.4.0-cuda12.1`) |
| `Dockerfile.jetson` | NVIDIA Jetson / L4T image |
| `requirements.txt` | deps for local (non-Docker) runs |

## Build

```bash
# from the python/ directory (build context = python/)
cd python

# x86_64 + NVIDIA GPU
docker build -f examples/09-dexined/Dockerfile -t rw-dexined:latest .

# NVIDIA Jetson (Orin / Xavier / Nano)
docker build -f examples/09-dexined/Dockerfile.jetson -t rw-dexined:jetson .
```

## Run

```bash
docker run --rm -it \
  --gpus all \
  --ipc=host \
  -e CONNECTION_STRING="shm://buffer?size=10MB&metadata=4KB&mode=Duplex" \
  -e SEGMENTATION_SINK_URL="tcp://127.0.0.1:9911" \
  rw-dexined:latest
```

## Configuration

| Env var | Default | Meaning |
|---------|---------|---------|
| `CONNECTION_STRING` | – | Frame transport (required), e.g. `shm://buffer?...&mode=Duplex` |
| `SEGMENTATION_SINK_URL` | NullSink | Where contour polygons are streamed |
| `GRAPHICS_SINK_URL` | NullSink | Where the text overlay is streamed |
| `MAX_SIDE` | `512` | Longest side fed to the network (lower = faster) |
| `EDGE_THRESHOLD` | `0.5` | Sigmoid edge-probability cut-off |
| `MORPH_KERNEL` | `5` | CLOSE kernel size (px) used to bound edges into regions |
| `MIN_AREA` | `80` | Drop contours smaller than this |
| `APPROX_EPS` | `2.0` | `approxPolyDP` epsilon (px) — fewer vertices when larger |
| `MAX_INSTANCES` | `64` | Cap polygons emitted per frame |
| `EXIT_AFTER` | `-1` | Exit after N frames (`-1` = unlimited) |

## Performance

DexiNed is heavy. On a Jetson Orin GPU expect tens of ms per frame at `MAX_SIDE=512`;
on CPU it would be 1–3 s per frame, which is why the example requires CUDA. To trade
detail for throughput, lower `MAX_SIDE`; to reduce vertex count downstream, raise
`APPROX_EPS`.
