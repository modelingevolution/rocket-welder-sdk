#!/bin/bash

# Live streaming demo with duplex processing and display (v2)
# Shows real-time video processing through C# client

set -e

# Configuration
BUFFER_NAME="live_demo"
PLUGIN_PATH="/mnt/d/source/modelingevolution/streamer/src/gstreamer/zerobuffer/build"
CLIENT_PATH="/mnt/d/source/modelingevolution/rocket-welder-sdk/csharp/examples/SimpleClient/publish"
DURATION_SECONDS=30  # Run for 30 seconds by default

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
MAGENTA='\033[0;35m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --duration)
            DURATION_SECONDS="$2"
            shift 2
            ;;
        --buffer)
            BUFFER_NAME="$2"
            shift 2
            ;;
        --help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --duration SECONDS  Duration to run the demo (default: 30)"
            echo "  --buffer NAME       Buffer name to use (default: live_demo)"
            echo "  --help              Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

clear

echo -e "${CYAN}=========================================${NC}"
echo -e "${CYAN}   Live Duplex Video Processing Demo    ${NC}"
echo -e "${CYAN}=========================================${NC}"
echo ""
echo -e "${YELLOW}Configuration:${NC}"
echo -e "  Buffer name: ${GREEN}${BUFFER_NAME}${NC}"
echo -e "  Duration: ${GREEN}${DURATION_SECONDS} seconds${NC}"
echo ""

# Function to cleanup on exit
cleanup() {
    echo ""
    echo -e "${YELLOW}Stopping processes...${NC}"
    
    # Kill all child processes
    jobs -p | xargs -r kill 2>/dev/null || true
    
    # Wait a moment
    sleep 1
    
    # Force kill any remaining
    jobs -p | xargs -r kill -9 2>/dev/null || true
    
    echo -e "${GREEN}Cleanup completed!${NC}"
}

# Set trap for cleanup
trap cleanup EXIT INT TERM

# Step 1: Start the C# duplex server
echo -e "${BLUE}Step 1: Starting C# duplex server${NC}"
echo -e "${MAGENTA}  This will process frames and add overlay text${NC}"
echo ""

# Start client in background with proper output handling
cd "$CLIENT_PATH"
CONNECTION_STRING="shm://${BUFFER_NAME}?mode=duplex&size=20MB&metadata=4KB" \
    ./SimpleClient 2>&1 | {
        frame_count=0
        while IFS= read -r line; do
            if [[ "$line" == *"Processed frame"* ]]; then
                ((frame_count++))
                printf "\r${GREEN}●${NC} C# Server: Processing frame %d..." "$frame_count"
            elif [[ "$line" == *"Starting RocketWelder"* ]]; then
                echo -e "${CYAN}Server started: ${BUFFER_NAME}${NC}"
            fi
        done
    } &

# Wait for server to be ready
echo -e "${YELLOW}Waiting for server to initialize...${NC}"
sleep 3

# Step 2: Start GStreamer pipeline with live source
echo ""
echo -e "${BLUE}Step 2: Starting live GStreamer pipeline${NC}"
echo -e "${MAGENTA}  Live source → Duplex processing → Display${NC}"
echo ""

# Determine which sink to use
if [[ -n "$DISPLAY" ]]; then
    SINK="fpsdisplaysink text-overlay=true sync=false"
    echo -e "${GREEN}Display detected - using video output${NC}"
else
    SINK="fakesink sync=false"
    echo -e "${YELLOW}No display - using fakesink for testing${NC}"
fi

# Build pipeline
PIPELINE="videotestsrc pattern=ball is-live=true ! \
    video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
    zerofilter channel-name=${BUFFER_NAME} ! \
    videoconvert ! \
    ${SINK}"

echo -e "${CYAN}Pipeline configuration:${NC}"
echo -e "  ${YELLOW}Pattern:${NC} Moving ball (live)"
echo -e "  ${YELLOW}Resolution:${NC} 640x480 @ 30fps"
echo -e "  ${YELLOW}Processing:${NC} Duplex via ${BUFFER_NAME}"
echo ""

echo -e "${GREEN}Running for ${DURATION_SECONDS} seconds...${NC}"
echo ""

# Run pipeline with timeout
env GST_PLUGIN_PATH="${PLUGIN_PATH}" \
    timeout ${DURATION_SECONDS} \
    gst-launch-1.0 -e ${PIPELINE} 2>&1 | {
        while IFS= read -r line; do
            if [[ "$line" == *"Pipeline is PREROLLED"* ]]; then
                echo -e "${GREEN}✓ Pipeline ready${NC}"
            elif [[ "$line" == *"Setting pipeline to PLAYING"* ]]; then
                echo -e "${GREEN}✓ Pipeline running${NC}"
            elif [[ "$line" == *"ERROR"* ]]; then
                echo -e "${RED}Pipeline error detected${NC}"
            elif [[ "$line" == *"Got EOS"* ]]; then
                echo -e "${YELLOW}Pipeline finished${NC}"
            fi
        done
    }

echo ""
echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}       Demo Completed Successfully!      ${NC}"
echo -e "${GREEN}=========================================${NC}"
echo ""
echo -e "${CYAN}Summary:${NC}"
echo "  • C# server processed video frames in real-time"
echo "  • Each frame was modified with 'DUPLEX' overlay"
echo "  • Zero-copy architecture for optimal performance"
echo ""