# Dockerfile for Python RocketWelder SDK SimpleClient
FROM python:3.12-slim-bookworm

WORKDIR /app

# Install runtime dependencies
RUN apt-get update && apt-get install -y \
    # OpenCV and X11 dependencies
    libgomp1 \
    libglib2.0-0 \
    libsm6 \
    libxext6 \
    libxrender1 \
    libgl1 \
    libx11-6 \
    libxcb1 \
    # Video processing libraries
    libavcodec-dev \
    libavformat-dev \
    libswscale-dev \
    libv4l-dev \
    # Image libraries
    libjpeg-dev \
    libpng-dev \
    libtiff-dev \
    # Additional dependencies
    libatlas-base-dev \
    gfortran \
    # GStreamer libraries
    libgstreamer1.0-0 \
    libgstreamer-plugins-base1.0-0 \
    # Useful tools for debugging
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
RUN pip install --no-cache-dir .

# Copy the example application
COPY examples/simple_client.py .

# Set up logging - SDK will propagate to ZEROBUFFER_LOG_LEVEL
ENV ROCKET_WELDER_LOG_LEVEL=INFO

# Health check (optional)
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD pgrep -f simple_client.py || exit 1

# Entry point - runs the client with CONNECTION_STRING env var
ENTRYPOINT ["python", "simple_client.py"]

# No default CMD - will use CONNECTION_STRING from environment