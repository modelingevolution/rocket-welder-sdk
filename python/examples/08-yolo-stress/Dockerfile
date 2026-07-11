# Dockerfile for Python RocketWelder SDK - YOLO Stress Generator
# Heavy YOLOv8 segmentation feed for native-player overlay perf testing.
# Build from SDK root: docker build -f examples/08-yolo-stress/Dockerfile -t rw-yolo-stress .
# REQUIRES NVIDIA GPU - run with: docker run --runtime=nvidia --gpus all --ipc=host ...
FROM python:3.12-slim-bookworm

WORKDIR /app

RUN apt-get update && apt-get install -y \
    libgomp1 \
    libglib2.0-0 \
    libsm6 \
    libxext6 \
    libxrender1 \
    libgl1 \
    libx11-6 \
    libxcb1 \
    libavcodec-dev \
    libavformat-dev \
    libswscale-dev \
    libv4l-dev \
    libjpeg-dev \
    libpng-dev \
    libtiff-dev \
    libatlas-base-dev \
    gfortran \
    libgstreamer1.0-0 \
    libgstreamer-plugins-base1.0-0 \
    procps \
    iputils-ping \
    net-tools \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
RUN pip install --no-cache-dir ultralytics

COPY rocket_welder_sdk/ /app/rocket_welder_sdk/
COPY setup.py pyproject.toml MANIFEST.in README.md ./
RUN pip install --no-cache-dir .

COPY examples/08-yolo-stress/main.py .

ENV ROCKET_WELDER_LOG_LEVEL=INFO
ENV YOLO_MODEL=yolov8x-seg.pt
ENV CONF_THRESHOLD=0.05
ENV INSTANCE_MULTIPLIER=1
ENV CONTOUR_MODE=none

# Pre-fetch the heavy model to avoid first-run download
RUN python -c "from ultralytics import YOLO; YOLO('yolov8x-seg.pt')"

ENTRYPOINT ["python", "main.py"]
