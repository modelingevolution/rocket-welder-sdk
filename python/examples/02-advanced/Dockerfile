# Dockerfile for Python RocketWelder SDK - advanced
# Adds timestamp overlay to video frames
# Build from SDK root: docker build -f examples/02-advanced/Dockerfile -t rw-simple .
FROM python:3.12-slim-bookworm

WORKDIR /app

# Install runtime dependencies
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

# Copy requirements and install Python dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy and install the SDK
COPY rocket_welder_sdk/ /app/rocket_welder_sdk/
COPY setup.py pyproject.toml MANIFEST.in README.md ./
RUN pip install --no-cache-dir ".[nng]"

# Copy the example application
COPY examples/02-advanced/main.py .

ENV ROCKET_WELDER_LOG_LEVEL=INFO

ENTRYPOINT ["python", "main.py"]
