#!/bin/bash

# Local test script for C# Rocket Welder SDK
# Tests different connection modes with zerosink

set -e

# Configuration
BUFFER_NAME="test_csharp"
FRAME_COUNT=5
PLUGIN_PATH="/mnt/d/source/modelingevolution/streamer/src/gstreamer/zerobuffer/build"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "========================================="
echo "C# Rocket Welder SDK Test"
echo "========================================="

# Test function
run_test() {
    local MODE=$1
    local CONNECTION_STRING="shm://${BUFFER_NAME}?mode=${MODE}"
    
    echo -e "\n${YELLOW}Testing mode: ${MODE}${NC}"
    echo "Connection: ${CONNECTION_STRING}"
    echo "Frames: ${FRAME_COUNT}"
    echo ""
    
    # Start C# client FIRST (it creates the shared memory buffer)
    echo -e "${GREEN}Step 1: Starting C# client (creates buffer)${NC}"
    echo -e "${YELLOW}  Command:${NC} ./run.sh \"${CONNECTION_STRING}\" --exit-after=${FRAME_COUNT}"
    echo ""
    timeout 10 ./run.sh "${CONNECTION_STRING}" --exit-after=${FRAME_COUNT} > output_${MODE}.log 2>&1 &
    CLIENT_PID=$!
    
    # Give client time to initialize and create the buffer
    echo "  Waiting for client to initialize..."
    sleep 2
    
    # Then start GStreamer pipeline to write to the buffer
    echo -e "${GREEN}Step 2: Starting GStreamer pipeline (writes to buffer)${NC}"
    echo -e "${YELLOW}  Command:${NC} GST_PLUGIN_PATH=${PLUGIN_PATH} \\"
    echo "           gst-launch-1.0 videotestsrc num-buffers=${FRAME_COUNT} pattern=ball ! \\"
    echo "           video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \\"
    echo "           zerosink buffer-name=${BUFFER_NAME} sync=false"
    echo ""
    
    GST_PLUGIN_PATH=${PLUGIN_PATH} gst-launch-1.0 \
        videotestsrc num-buffers=${FRAME_COUNT} pattern=ball ! \
        video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
        zerosink buffer-name=${BUFFER_NAME} sync=false &> gst_${MODE}.log &
    
    GST_PID=$!
    
    # Wait for client to finish or timeout
    if wait $CLIENT_PID 2>/dev/null; then
        CLIENT_EXIT_CODE=$?
    else
        CLIENT_EXIT_CODE=$?
    fi
    
    # Stop GStreamer pipeline if still running
    kill $GST_PID 2>/dev/null || true
    wait $GST_PID 2>/dev/null || true
    
    # Check results
    if [ $CLIENT_EXIT_CODE -eq 0 ]; then
        echo -e "${GREEN}✓ Test passed${NC}"
        echo "  - Client exited cleanly"
    elif [ $CLIENT_EXIT_CODE -eq 124 ]; then
        echo -e "${RED}✗ Test failed${NC}"
        echo "  - Client timed out"
        echo "  - Check output_${MODE}.log for details"
    else
        echo -e "${RED}✗ Test failed${NC}"
        echo "  - Client exited with code: $CLIENT_EXIT_CODE"
        echo "  - Check output_${MODE}.log for details"
    fi
    
    # Show any errors from the log
    if grep -q "Error" output_${MODE}.log 2>/dev/null; then
        echo -e "${YELLOW}Errors found:${NC}"
        grep "Error" output_${MODE}.log | head -3
    fi
    
    # Show frame processing info
    if grep -q "Processed frame" output_${MODE}.log 2>/dev/null; then
        PROCESSED=$(grep -c "Processed frame" output_${MODE}.log || echo "0")
        echo "  - Frames processed: ${PROCESSED}/${FRAME_COUNT}"
    fi
    
    return $CLIENT_EXIT_CODE
}

# Test duplex mode
echo -e "${GREEN}=== Testing Duplex Mode ===${NC}"
if run_test "duplex"; then
    DUPLEX_RESULT="PASS"
else
    DUPLEX_RESULT="FAIL"
fi

echo ""

# Test oneway mode
echo -e "${GREEN}=== Testing Oneway Mode ===${NC}"
if run_test "oneway"; then
    ONEWAY_RESULT="PASS"
else
    ONEWAY_RESULT="FAIL"
fi

# Summary
echo ""
echo "========================================="
echo "Test Summary"
echo "========================================="
echo -e "Duplex mode:  ${DUPLEX_RESULT}"
echo -e "Oneway mode:  ${ONEWAY_RESULT}"
echo ""
echo "Logs available:"
echo "  - gst_duplex.log, gst_oneway.log (GStreamer output)"
echo "  - output_duplex.log, output_oneway.log (Client output)"

if [ "$DUPLEX_RESULT" = "PASS" ] && [ "$ONEWAY_RESULT" = "PASS" ]; then
    echo -e "\n${GREEN}All tests passed!${NC}"
    exit 0
else
    echo -e "\n${RED}Some tests failed.${NC}"
    exit 1
fi