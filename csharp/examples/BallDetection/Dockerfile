# Multi-stage build for C# BallDetection example
# Sink-only example - detects ball and outputs via NNG (no frame modification)
# Build context: csharp/ directory (run via build_docker_samples.sh)
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy the project file and restore dependencies (SDK from NuGet)
COPY examples/BallDetection/BallDetection.csproj .
RUN dotnet restore

# Copy the source code and build
COPY examples/BallDetection/ .
RUN dotnet publish -c Release -o /app/publish

# Runtime stage - Using Ubuntu 24.04 (Noble) for GLIBC 2.38+ compatibility
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble
WORKDIR /app

# Copy published app first (to leverage cache for apt-get layer)
COPY --from=build /app/publish .

# Install all dependencies in a single RUN command to reduce layers
RUN apt-get update && apt-get install -y --no-install-recommends \
    # Core dependencies
    libgomp1 \
    libgdiplus \
    libc6-dev \
    libicu-dev \
    libssl-dev \
    ca-certificates \
    # OpenCV dependencies
    libgtk-3-0 \
    libavcodec-dev \
    libavformat-dev \
    libswscale-dev \
    libv4l-dev \
    libxvidcore-dev \
    libx264-dev \
    libjpeg-dev \
    libpng-dev \
    libtiff-dev \
    libatlas-base-dev \
    gfortran \
    libgstreamer1.0-0 \
    libgstreamer-plugins-base1.0-0 \
    # EmguCV/OpenCV native dependencies
    libgeotiff5 \
    libdc1394-25 \
    libopenexr-3-1-30 \
    libhdf5-103-1 \
    libvtk9.1t64 \
    # X11 for preview
    libx11-6 \
    libxext6 \
    libxrender1 \
    libxtst6 \
    libxi6 \
    libxrandr2 \
    libxcursor1 \
    libxinerama1 \
    libxkbcommon-x11-0 \
    libglu1-mesa \
    # Debugging tools
    procps \
    iputils-ping \
    net-tools \
    && rm -rf /var/lib/apt/lists/*

# Create symlink for Emgu.CV native library
RUN ln -s /app/runtimes/ubuntu-x64/native/libcvextern.so /app/libcvextern.so || true

# Ensure Emgu.CV native libraries are accessible
ENV LD_LIBRARY_PATH=/app/runtimes/ubuntu-x64/native:/app:${LD_LIBRARY_PATH:-}

# Set up logging
ENV ZEROBUFFER_LOG_LEVEL=INFO
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Disable RocketWelder UI by default (sink-only example doesn't need UI)
ENV DisableRocketWelderUI=true

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD pgrep -f BallDetection || exit 1

# Entry point
ENTRYPOINT ["dotnet", "BallDetection.dll"]
