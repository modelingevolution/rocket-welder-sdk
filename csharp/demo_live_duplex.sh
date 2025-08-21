#!/bin/bash

# Live streaming demo with duplex processing and display
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
    # Only show cleanup message if we're actually cleaning up
    if [[ -n "${CLIENT_PID}" ]] || [[ -n "${GST_PID}" ]]; then
        echo ""
        echo -e "${YELLOW}Cleaning up...${NC}"
        
        # Kill GStreamer pipeline first
        if [[ -n "${GST_PID}" ]]; then
            kill -INT $GST_PID 2>/dev/null || true
            wait $GST_PID 2>/dev/null || true
        fi
        
        # Then kill C# client
        if [[ -n "${CLIENT_PID}" ]]; then
            kill -INT $CLIENT_PID 2>/dev/null || true
            # Give it a moment to clean up
            sleep 0.5
            # Force kill if still running
            if kill -0 $CLIENT_PID 2>/dev/null; then
                kill -KILL $CLIENT_PID 2>/dev/null || true
            fi
            wait $CLIENT_PID 2>/dev/null || true
        fi
        
        echo -e "${GREEN}Cleanup completed!${NC}"
    fi
}

# Set trap for cleanup
trap cleanup EXIT INT TERM

# Step 1: Start the C# duplex server
echo -e "${BLUE}Step 1: Starting C# duplex server${NC}"
echo -e "${MAGENTA}  This will process frames and add overlay text${NC}"
echo ""

cd "$CLIENT_PATH"
(CONNECTION_STRING="shm://${BUFFER_NAME}?mode=duplex&size=20MB&metadata=4KB" \
    exec ./SimpleClient 2>&1 | while IFS= read -r line; do
        if [[ "$line" == *"Processed frame"* ]]; then
            # Only show frame processing messages with a simple indicator
            frame_num=$(echo "$line" | grep -oE 'frame [0-9]+' | grep -oE '[0-9]+')
            echo -ne "\r${GREEN}●${NC} Processing frames... ($frame_num)    "
        elif [[ "$line" == *"Starting RocketWelder"* ]] || [[ "$line" == *"Can be tested with"* ]]; then
            echo -e "${CYAN}$line${NC}"
        fi
    done) &
CLIENT_PID=$!

# Wait for server to be ready
echo -e "${YELLOW}Waiting for server to initialize...${NC}"
sleep 3

# Step 2: Start GStreamer pipeline with live source and display
echo ""
echo -e "${BLUE}Step 2: Starting live GStreamer pipeline${NC}"
echo -e "${MAGENTA}  Live source → Duplex processing → Display${NC}"
echo ""

# Build the GStreamer pipeline - simplified version first
PIPELINE="videotestsrc pattern=ball is-live=true ! \
    video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
    zerofilter channel-name=${BUFFER_NAME} ! \
    videoconvert ! \
    fpsdisplaysink text-overlay=true sync=false"

echo -e "${CYAN}Pipeline:${NC}"
echo -e "  ${YELLOW}Source:${NC} videotestsrc (ball pattern, live)"
echo -e "  ${YELLOW}Processing:${NC} zerofilter (duplex channel: ${BUFFER_NAME})"
echo -e "  ${YELLOW}Display:${NC} autovideosink + FPS counter"
echo ""

# Check if we're in a display-capable environment
if [[ -z "$DISPLAY" ]] && [[ ! -f /.dockerenv ]]; then
    echo -e "${YELLOW}Warning: No DISPLAY variable set. Using fakesink for testing...${NC}"
    PIPELINE="videotestsrc pattern=ball is-live=true ! \
        video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
        zerofilter channel-name=${BUFFER_NAME} ! \
        fakesink sync=false"
fi

echo -e "${GREEN}Starting pipeline (press Ctrl+C to stop)...${NC}"
echo ""

# Run the pipeline for the specified duration (timeout will kill it)
env GST_PLUGIN_PATH="${PLUGIN_PATH}" \
    GST_DEBUG_NO_COLOR=1 \
    timeout --signal=INT ${DURATION_SECONDS} \
    gst-launch-1.0 -e ${PIPELINE} 2>&1 | while IFS= read -r line; do
        if [[ "$line" == *"Pipeline is PREROLLED"* ]]; then
            echo -e "${GREEN}✓ Pipeline ready and processing${NC}"
        elif [[ "$line" == *"ERROR"* ]] || [[ "$line" == *"error"* ]]; then
            echo -e "${RED}Error: $(echo "$line" | sed 's/.*ERROR//' | sed 's/.*error//')${NC}"
        elif [[ "$line" == *"WARNING"* ]]; then
            echo -e "${YELLOW}Warning: $(echo "$line" | sed 's/.*WARNING//')${NC}"
        elif [[ "$line" == *"Setting pipeline to PLAYING"* ]]; then
            echo -e "${GREEN}✓ Pipeline is running${NC}"
        elif [[ "$line" == *"fps"* ]]; then
            # Extract and display FPS info
            fps=$(echo "$line" | grep -oE '[0-9]+\.[0-9]+ fps' | head -1)
            if [[ -n "$fps" ]]; then
                echo -ne "\r${CYAN}Performance: ${fps}${NC}          "
            fi
        fi
    done &
GST_PID=$!

# Wait for pipeline to complete or timeout
wait $GST_PID 2>/dev/null || true

# After GStreamer finishes, kill the C# client
echo ""
echo -e "${YELLOW}Stopping C# server...${NC}"
if [[ -n "${CLIENT_PID}" ]]; then
    kill -INT $CLIENT_PID 2>/dev/null || true
    # Give it a moment to clean up gracefully
    sleep 1
    # Force kill if still running
    kill -KILL $CLIENT_PID 2>/dev/null || true
fi

echo ""
echo ""
echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}  Demo completed successfully!           ${NC}"
echo -e "${GREEN}=========================================${NC}"
echo ""
echo -e "${CYAN}What happened:${NC}"
echo "  1. C# client created a duplex server"
echo "  2. GStreamer sent live video frames to the server"
echo "  3. C# client processed each frame (added 'DUPLEX' overlay)"
echo "  4. Processed frames were sent back to GStreamer"
echo "  5. GStreamer displayed the processed video"
echo ""
echo -e "${YELLOW}The 'DUPLEX' text on the video was added by the C# client!${NC}"