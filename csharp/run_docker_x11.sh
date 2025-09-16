#!/bin/bash
# Script to run RocketWelder C# Docker container with X11 display support

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

# Check if test video exists in data folder
VIDEO_PATH="$(pwd)/data/test_stream.mp4"
if [ ! -f "$VIDEO_PATH" ]; then
    echo -e "${YELLOW}Warning: Test video not found at $VIDEO_PATH${NC}"
    echo "Creating a test video..."
    mkdir -p data
    # Create a simple test video if ffmpeg is available
    if command -v ffmpeg &> /dev/null; then
        ffmpeg -f lavfi -i testsrc=duration=10:size=640x480:rate=25 \
               -f lavfi -i sine=frequency=1000:duration=10 \
               -pix_fmt yuv420p -y "$VIDEO_PATH" 2>/dev/null
        echo -e "${GREEN}Test video created at $VIDEO_PATH${NC}"
    else
        echo -e "${YELLOW}ffmpeg not found. Please create a test video at $VIDEO_PATH${NC}"
        exit 1
    fi
fi

# Clean up any existing container with the same name
docker stop rocket-welder-csharp-preview 2>/dev/null || true
docker rm rocket-welder-csharp-preview 2>/dev/null || true

# Run the container with X11 forwarding
echo -e "${GREEN}Running Docker container with X11 forwarding...${NC}"
echo "Using video: $VIDEO_PATH"
echo "Press 'q' in the preview window to quit"

docker run --rm \
    --name rocket-welder-csharp-preview \
    -e DISPLAY=$DISPLAY \
    -e CONNECTION_STRING="file:///data/test_stream.mp4?preview=true&loop=false&mode=duplex" \
    -v /tmp/.X11-unix:/tmp/.X11-unix:rw \
    -v "$VIDEO_PATH:/data/test_stream.mp4:ro" \
    --network host \
    rocket-welder-client-csharp:latest

echo -e "${GREEN}Container stopped${NC}"

# Restore X server security
echo "Restoring X server security..."
xhost -local:docker 2>/dev/null || xhost -local: 2>/dev/null || true