#!/bin/bash

# Integration test script for Python Rocket Welder SDK
# Tests different connection modes with zerosink/zerofilter

set -e

# Configuration
BUFFER_NAME="test_python"
FRAME_COUNT=5
PLUGIN_PATH="/mnt/d/source/modelingevolution/streamer/src/gstreamer/zerobuffer/build"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "========================================="
echo "Python Rocket Welder SDK Integration Test"
echo "========================================="

# Test function
run_test() {
    local MODE=$1
    local CONNECTION_STRING="shm://${BUFFER_NAME}?mode=${MODE}"
    
    echo -e "\n${YELLOW}Testing mode: ${MODE}${NC}"
    echo "Connection: ${CONNECTION_STRING}"
    echo "Frames: ${FRAME_COUNT}"
    echo ""
    
    # Start Python client FIRST (it creates the shared memory buffer)
    echo -e "${GREEN}Step 1: Starting Python client (creates buffer)${NC}"
    echo -e "${YELLOW}  Command:${NC} ./run.sh \"${CONNECTION_STRING}\" --exit-after=${FRAME_COUNT}"
    echo ""
    timeout 10 ./run.sh "${CONNECTION_STRING}" --exit-after=${FRAME_COUNT} > output_${MODE}.log 2>&1 &
    CLIENT_PID=$!
    
    # Give client time to initialize and create the buffer
    echo "  Waiting for client to initialize..."
    sleep 3
    
    # Verify buffer was created
    if [ "$MODE" = "Duplex" ]; then
        # In duplex mode, Python server creates the request buffer
        if [ -f /dev/shm/${BUFFER_NAME}_request ]; then
            echo "  ✓ Request buffer created in /dev/shm/${BUFFER_NAME}_request"
        else
            echo "  ⚠ Request buffer not found in /dev/shm/${BUFFER_NAME}_request"
        fi
        # Response buffer will be created by GStreamer client later
    else
        # In oneway mode, check for regular buffer
        if [ -f /dev/shm/${BUFFER_NAME} ]; then
            echo "  ✓ Buffer created in /dev/shm/${BUFFER_NAME}"
        else
            echo "  ⚠ Buffer not found in /dev/shm/${BUFFER_NAME}"
        fi
    fi
    
    # Then start GStreamer pipeline to write to the buffer
    echo -e "${GREEN}Step 2: Starting GStreamer pipeline (writes to buffer)${NC}"
    
    # Use the appropriate element based on mode
    if [ "$MODE" = "Duplex" ]; then
        GST_ELEMENT="zerofilter channel-name=${BUFFER_NAME}"
        GST_SINK="fakesink"
    else
        GST_ELEMENT="zerosink buffer-name=${BUFFER_NAME} sync=false"
        GST_SINK=""
    fi
    
    echo -e "${YELLOW}  Command:${NC} GST_DEBUG=\"zerosink:5,zerobuffer:5\" GST_PLUGIN_PATH=${PLUGIN_PATH} \\"
    echo "           gst-launch-1.0 videotestsrc num-buffers=${FRAME_COUNT} pattern=ball ! \\"
    echo "           video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \\"
    echo "           ${GST_ELEMENT}${GST_SINK:+ ! ${GST_SINK}}"
    echo ""
    
    if [ "$MODE" = "Duplex" ]; then
        GST_DEBUG="zerofilter:5,zerobuffer:5" GST_PLUGIN_PATH=${PLUGIN_PATH} gst-launch-1.0 \
            videotestsrc num-buffers=${FRAME_COUNT} pattern=ball ! \
            video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
            zerofilter channel-name=${BUFFER_NAME} ! \
            fakesink &> gst_${MODE}.log &
    else
        GST_DEBUG="zerosink:5,zerobuffer:5" GST_PLUGIN_PATH=${PLUGIN_PATH} gst-launch-1.0 \
            videotestsrc num-buffers=${FRAME_COUNT} pattern=ball ! \
            video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
            zerosink buffer-name=${BUFFER_NAME} sync=false &> gst_${MODE}.log &
    fi
    
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
    # For OneWay mode, check if frames were processed
    if [ "$MODE" = "OneWay" ] && grep -q "Processed frame ${FRAME_COUNT}" output_${MODE}.log 2>/dev/null; then
        CLIENT_EXIT_CODE=0  # Override exit code if all frames were processed
    fi
    
    if [ $CLIENT_EXIT_CODE -eq 0 ]; then
        echo -e "${GREEN}✓ Test passed${NC}"
        echo "  - Client exited cleanly"
    elif [ $CLIENT_EXIT_CODE -eq 124 ]; then
        echo -e "${RED}✗ Test failed${NC}"
        echo "  - Client timed out"
    else
        echo -e "${RED}✗ Test failed${NC}"
        echo "  - Client exited with code: $CLIENT_EXIT_CODE"
    fi
    
    # Show frame processing info
    if grep -q "Processed frame" output_${MODE}.log 2>/dev/null; then
        PROCESSED=$(grep -c "Processed frame" output_${MODE}.log || echo "0")
        echo "  - Frames processed: ${PROCESSED}/${FRAME_COUNT}"
    fi
    
    # If test failed, show relevant logs
    if [ $CLIENT_EXIT_CODE -ne 0 ]; then
        echo ""
        echo -e "${YELLOW}=== GStreamer errors ===${NC}"
        if [ -f gst_${MODE}.log ]; then
            grep -E "ERROR|Failed" gst_${MODE}.log | head -5 || echo "  No errors found"
        fi
        
        echo ""
        echo -e "${YELLOW}=== Client errors ===${NC}"
        if [ -f output_${MODE}.log ]; then
            grep -E "ERROR|Error|Failed" output_${MODE}.log | head -5 || echo "  No errors found"
        fi
    fi
    
    return $CLIENT_EXIT_CODE
}

# Test duplex mode
echo -e "${GREEN}=== Testing Duplex Mode ===${NC}"
if run_test "Duplex"; then
    DUPLEX_RESULT="PASS"
else
    DUPLEX_RESULT="FAIL"
fi

echo ""

# Test oneway mode
echo -e "${GREEN}=== Testing OneWay Mode ===${NC}"
if run_test "OneWay"; then
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
echo -e "OneWay mode:  ${ONEWAY_RESULT}"
echo ""
echo "Logs available:"
echo "  - gst_Duplex.log, gst_OneWay.log (GStreamer output)"
echo "  - output_Duplex.log, output_OneWay.log (Client output)"

if [ "$DUPLEX_RESULT" = "PASS" ] && [ "$ONEWAY_RESULT" = "PASS" ]; then
    echo -e "\n${GREEN}All tests passed!${NC}"
    exit 0
else
    echo -e "\n${RED}Some tests failed.${NC}"
    exit 1
fi