#!/bin/bash

# Test script for zerofilter duplex communication

set -e

echo "========================================="
echo "  Testing ZeroFilter Duplex Channel     "
echo "========================================="

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Configuration
CHANNEL_NAME="test-processor"
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

echo -e "\n${YELLOW}Step 2: Starting SimpleClient in duplex mode...${NC}"
# Run SimpleClient with duplex connection string
cd examples/SimpleClient
export CONNECTION_STRING="shm://${CHANNEL_NAME}?mode=duplex"
dotnet run -- --exit-after ${TEST_FRAMES} &
CLIENT_PID=$!

echo "SimpleClient PID: $CLIENT_PID"
echo "Waiting for client to initialize..."
sleep 3

# Check if client is still running
if ! kill -0 $CLIENT_PID 2>/dev/null; then
    echo -e "${RED}Error: SimpleClient crashed on startup${NC}"
    exit 1
fi

echo -e "\n${YELLOW}Step 3: Running GStreamer pipeline with zerofilter...${NC}"
echo "Pipeline: videotestsrc → zerofilter → fakesink"
echo ""

# Run GStreamer pipeline
# Note: zerofilter will process frames through the SimpleClient
GST_PLUGIN_PATH=$GST_PLUGIN_PATH gst-launch-1.0 -v \
    videotestsrc num-buffers=${TEST_FRAMES} pattern=ball ! \
    video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
    zerofilter channel-name=${CHANNEL_NAME} timeout-ms=5000 ! \
    fakesink &

GST_PID=$!

echo "GStreamer PID: $GST_PID"
echo -e "\n${GREEN}Test running...${NC}"
echo "Processing $TEST_FRAMES frames through duplex channel"
echo ""

# Wait for GStreamer to complete
wait $GST_PID 2>/dev/null
GST_EXIT=$?

# Wait a bit for client to finish
sleep 2

# Check if client processed frames
if kill -0 $CLIENT_PID 2>/dev/null; then
    echo -e "${YELLOW}Waiting for client to finish...${NC}"
    wait $CLIENT_PID 2>/dev/null
    CLIENT_EXIT=$?
else
    CLIENT_EXIT=0
fi

# Report results
echo -e "\n========================================="
if [ $GST_EXIT -eq 0 ] && [ $CLIENT_EXIT -eq 0 ]; then
    echo -e "${GREEN}✓ Test PASSED!${NC}"
    echo "Successfully processed $TEST_FRAMES frames through zerofilter"
else
    echo -e "${RED}✗ Test FAILED${NC}"
    echo "GStreamer exit code: $GST_EXIT"
    echo "Client exit code: $CLIENT_EXIT"
fi
echo "========================================="