#!/bin/bash
# Script to run RocketWelder C# Docker container with X11 display support
# Usage: ./run_docker_x11.sh [video_path]

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== RocketWelder C# Docker X11 Runner ===${NC}"

# Check if we're in WSL
if grep -qi microsoft /proc/version; then
    echo -e "${YELLOW}Detected WSL environment${NC}"
    # For WSL, DISPLAY is usually set by WSLg
    if [ -z "$DISPLAY" ]; then
        export DISPLAY=:0
        echo "Setting DISPLAY=:0 for WSL"
    fi
else
    # For Linux, ensure DISPLAY is set
    if [ -z "$DISPLAY" ]; then
        export DISPLAY=:0
        echo "Setting DISPLAY=:0"
    fi
fi

# Allow X server connections from Docker
echo "Allowing X server connections..."
xhost +local:docker 2>/dev/null || xhost +local: 2>/dev/null || true

# Check if Docker image exists
if ! docker images --format "{{.Repository}}:{{.Tag}}" | grep -q "^rocket-welder-client-csharp:latest$"; then
    echo -e "${RED}Error: Docker image 'rocket-welder-client-csharp:latest' not found${NC}"
    echo "Please run '../build_docker_samples.sh --csharp-only' first to build the image"
    exit 1
fi

# Use provided video path or default to test video
if [ ! -z "$1" ]; then
    VIDEO_PATH="$1"
    # Convert to absolute path if relative
    if [[ "$VIDEO_PATH" != /* ]]; then
        VIDEO_PATH="$(pwd)/$VIDEO_PATH"
    fi
    echo -e "${GREEN}Using provided video: $VIDEO_PATH${NC}"
else
    VIDEO_PATH="$(pwd)/data/test_stream.mp4"
    echo -e "${GREEN}Using default video: $VIDEO_PATH${NC}"
fi
# Check if video exists
if [ ! -f "$VIDEO_PATH" ]; then
    echo -e "${RED}Error: Video file not found at $VIDEO_PATH${NC}"
    exit 1
fi

# Clean up any existing container with the same name
docker stop rocket-welder-csharp-preview 2>/dev/null || true
docker rm rocket-welder-csharp-preview 2>/dev/null || true

# Run the container with X11 forwarding
echo -e "${GREEN}Running Docker container with X11 forwarding...${NC}"
echo "Using video: $VIDEO_PATH"
echo "Press 'q' in the preview window to quit"

# Construct and display the full docker command
DOCKER_CMD="docker run --rm \
    --name rocket-welder-csharp-preview \
    -e DISPLAY=$DISPLAY \
    -e CONNECTION_STRING=\"file:///data/test_stream.mp4?preview=true&loop=false&mode=duplex\" \
    -v /tmp/.X11-unix:/tmp/.X11-unix:rw \
    -v \"$VIDEO_PATH:/data/test_stream.mp4:ro\" \
    --network host \
    rocket-welder-client-csharp:latest"

echo -e "${YELLOW}Full Docker command:${NC}"
echo "$DOCKER_CMD"
echo ""

# Execute the docker command
docker run --rm \
    --name rocket-welder-csharp-preview \
    -e DISPLAY=$DISPLAY \
    -e CONNECTION_STRING="file:///data/video_input.mp4?preview=true&loop=false&mode=duplex" \
    -v /tmp/.X11-unix:/tmp/.X11-unix:rw \
    -v "$VIDEO_PATH:/data/video_input.mp4:ro" \
    --network host \
    rocket-welder-client-csharp:latest

echo -e "${GREEN}Container stopped${NC}"

# Restore X server security
echo "Restoring X server security..."
xhost -local:docker 2>/dev/null || xhost -local: 2>/dev/null || true