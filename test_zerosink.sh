#!/bin/bash

# Test script for zerosink one-way communication

set -e

echo "========================================="
echo "  Testing ZeroSink One-Way Channel      "
echo "========================================="

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Configuration
BUFFER_NAME="test-buffer"
TEST_FRAMES=30
GST_PLUGIN_PATH="/mnt/d/source/modelingevolution/streamer/src/gstreamer/zerobuffer/build"

# Function to cleanup on exit
cleanup() {
    echo -e "\n${YELLOW}Cleaning up...${NC}"
    # Kill background processes if they exist
    if [ ! -z "$CLIENT_PID" ]; then
        kill $CLIENT_PID 2>/dev/null || true
    fi
    if [ ! -z "$GST_PID" ]; then
        kill $GST_PID 2>/dev/null || true
    fi
    exit
}

trap cleanup EXIT INT TERM

echo -e "${YELLOW}Step 1: Building SimpleClient...${NC}"
cd /mnt/d/source/modelingevolution/rocket-welder-sdk/csharp
dotnet build examples/SimpleClient/SimpleClient.csproj --verbosity quiet

echo -e "\n${YELLOW}Step 2: Starting SimpleClient as reader...${NC}"
# Run SimpleClient with one-way connection string
cd examples/SimpleClient
dotnet run -- "shm://${BUFFER_NAME}" --exit-after ${TEST_FRAMES} &
CLIENT_PID=$!

echo "SimpleClient PID: $CLIENT_PID"
echo "Waiting for client to initialize..."
sleep 3

# Check if client is still running
if ! kill -0 $CLIENT_PID 2>/dev/null; then
    echo -e "${RED}Error: SimpleClient crashed on startup${NC}"
    exit 1
fi

echo -e "\n${YELLOW}Step 3: Running GStreamer pipeline with zerosink...${NC}"
echo "Pipeline: videotestsrc → zerosink"
echo ""

# Run GStreamer pipeline
# Note: zerosink will send frames to the SimpleClient
GST_PLUGIN_PATH=$GST_PLUGIN_PATH gst-launch-1.0 -v \
    videotestsrc num-buffers=${TEST_FRAMES} pattern=ball ! \
    video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
    zerosink buffer-name=${BUFFER_NAME} sync=false &

GST_PID=$!

echo "GStreamer PID: $GST_PID"
echo -e "\n${GREEN}Test running...${NC}"
echo "Sending $TEST_FRAMES frames through zerosink"
echo ""

# Wait for GStreamer to complete
wait $GST_PID 2>/dev/null
GST_EXIT=$?

# Wait a bit for client to finish processing
echo -e "${YELLOW}Waiting for client to process remaining frames...${NC}"
sleep 3

# Check if client is done
if kill -0 $CLIENT_PID 2>/dev/null; then
    echo "Client still running, waiting..."
    # Give it more time
    sleep 5
    # Force kill if still running
    if kill -0 $CLIENT_PID 2>/dev/null; then
        echo "Client didn't exit, terminating..."
        kill $CLIENT_PID 2>/dev/null || true
    fi
fi

CLIENT_EXIT=$?

# Report results
echo -e "\n========================================="
if [ $GST_EXIT -eq 0 ]; then
    echo -e "${GREEN}✓ Test PASSED!${NC}"
    echo "Successfully sent $TEST_FRAMES frames through zerosink"
    echo "Check client output above for processing details"
else
    echo -e "${RED}✗ Test FAILED${NC}"
    echo "GStreamer exit code: $GST_EXIT"
fi
echo "========================================="