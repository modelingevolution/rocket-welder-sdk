# Multi-stage build for C# SimpleClient
FROM mcr.microsoft.com/dotnet/sdk:9.0-noble AS build
WORKDIR /src

# Copy the SDK project files first
COPY RocketWelder.SDK/RocketWelder.SDK.csproj RocketWelder.SDK/

# Copy the SimpleClient project file
COPY examples/SimpleClient/SimpleClient.csproj examples/SimpleClient/

# Restore dependencies
WORKDIR /src
RUN dotnet restore examples/SimpleClient/SimpleClient.csproj

# Copy the source code
COPY RocketWelder.SDK/ RocketWelder.SDK/
COPY examples/SimpleClient/ examples/SimpleClient/

# Build and publish
WORKDIR /src/examples/SimpleClient
RUN dotnet publish -c Release -o /app/publish

# Runtime stage - Using Ubuntu 24.04 (Noble) for GLIBC 2.38+ compatibility
FROM mcr.microsoft.com/dotnet/runtime:9.0-noble
WORKDIR /app

# Install OpenCV dependencies and tools for debugging
RUN apt-get update && apt-get install -y \
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
    # Useful tools for debugging
    procps \
    iputils-ping \
    net-tools \
    && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app/publish .

# Install minimal OpenCV/EmguCV dependencies and X11 for preview (NO VTK9!)
RUN apt-get update && apt-get install -y \
    libgeotiff5 \
    libdc1394-25 \
    libopenexr-3-1-30 \
    libhdf5-103-1 \
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
    && rm -rf /var/lib/apt/lists/*

# Create symlink for Emgu.CV native library
RUN ln -s /app/runtimes/ubuntu-x64/native/libcvextern.so /app/libcvextern.so || true

# Ensure Emgu.CV native libraries are accessible
# The runtime package should handle this, but we'll set library path just in case
ENV LD_LIBRARY_PATH=/app/runtimes/ubuntu-x64/native:/app:$LD_LIBRARY_PATH

# Set up logging
ENV ZEROBUFFER_LOG_LEVEL=INFO
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Health check (optional)
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD pgrep -f SimpleClient || exit 1

# Entry point - runs the client with CONNECTION_STRING env var
ENTRYPOINT ["dotnet", "SimpleClient.dll"]

# No default CMD - will use CONNECTION_STRING from environment