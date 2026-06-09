# syntax=docker/dockerfile:1.7
# DexiNed contour detection (segmentation sink) - x86_64 CUDA image
# Build context = python/ ; Dockerfile path = examples/09-dexined/Dockerfile
# REQUIRES an NVIDIA GPU - fails fast at startup if CUDA is unavailable.
#
#   docker build -f examples/09-dexined/Dockerfile -t rw-dexined:latest .
#
# Run (host with NVIDIA GPU):
#   docker run --rm -it --gpus all --ipc=host \
#     -e CONNECTION_STRING="shm://buffer?size=10MB&metadata=4KB&mode=Duplex" \
#     -e SEGMENTATION_SINK_URL="tcp://127.0.0.1:9911" \
#     rw-dexined:latest
FROM pytorch/pytorch:2.4.0-cuda12.1-cudnn9-runtime

WORKDIR /app

# System libs for OpenCV runtime
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgl1 \
    libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

# Python deps (torch already in base image)
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# SDK
COPY rocket_welder_sdk/ /app/rocket_welder_sdk/
COPY setup.py pyproject.toml MANIFEST.in README.md ./
RUN pip install --no-cache-dir .

# App + DexiNed network definition + baked BIPED checkpoint (~141 MB)
COPY examples/09-dexined/main.py .
COPY examples/09-dexined/model.py .
COPY examples/09-dexined/checkpoints/ /app/checkpoints/

ENV ROCKET_WELDER_LOG_LEVEL=INFO
ENTRYPOINT ["python", "main.py"]
