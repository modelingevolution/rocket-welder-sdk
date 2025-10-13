#!/bin/bash

# Integration test script for Python Rocket Welder SDK with Aravis camera source
# Tests different connection modes with zerosink/zerofilter using aravissrc

set -e

# Configuration
BUFFER_NAME="test_aravis"
FRAME_COUNT=10
PLUGIN_PATH="/mnt/d/source/modelingevolution/streamer/src/out/build/Linux-WSL-Debug/app/plugins"
CAMERA_NAME="localhost"  # Aravis camera name
DELAY_MS=0  # Delay in milliseconds before starting GStreamer pipeline
TIMEOUT_MS=5000  # Timeout in milliseconds for connection (default 5000ms)

# Parse command line arguments
for arg in "$@"; do
    case $arg in
        delay=*)
            DELAY_MS="${arg#*=}"
            shift
            ;;
        timeout=*)
            TIMEOUT_MS="${arg#*=}"
            shift
            ;;
    esac
done

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "========================================="
echo "Python Rocket Welder SDK Aravis Test"
echo "========================================="
if [ "$DELAY_MS" -gt 0 ]; then
    echo "Delay before GStreamer: ${DELAY_MS}ms"
fi
if [ "$TIMEOUT_MS" -ne 5000 ]; then
    echo "Connection timeout: ${TIMEOUT_MS}ms"
fi

# Test function
run_test() {
    local MODE=$1
    local CONNECTION_STRING="shm://${BUFFER_NAME}?mode=${MODE}&timeout=${TIMEOUT_MS}"

    echo -e "\n${YELLOW}Testing mode: ${MODE}${NC}"
    echo "Connection: ${CONNECTION_STRING}"
    echo "Camera: ${CAMERA_NAME}"
    echo "Frames: ${FRAME_COUNT}"
    echo "Timeout: ${TIMEOUT_MS}ms"
    echo ""

    # Clean up any existing shared memory resources
    echo "  Cleaning up existing resources..."
    rm -f /dev/shm/${BUFFER_NAME} 2>/dev/null || true
    rm -f /dev/shm/${BUFFER_NAME}_request 2>/dev/null || true
    rm -f /dev/shm/${BUFFER_NAME}_response 2>/dev/null || true
    rm -f /dev/shm/sem.${BUFFER_NAME}* 2>/dev/null || true
    rm -f /dev/shm/sem.sem-*${BUFFER_NAME}* 2>/dev/null || true

    # Start Python client FIRST (it creates the shared memory buffer)
    echo -e "${GREEN}Step 1: Starting Python client (creates buffer)${NC}"
    echo -e "${YELLOW}  Command:${NC} venv/bin/python examples/integration_client.py \"${CONNECTION_STRING}\" --exit-after ${FRAME_COUNT}"
    echo ""
    # Calculate shell timeout: connection timeout + delay + 5 seconds buffer
    SHELL_TIMEOUT=$(echo "scale=0; ($TIMEOUT_MS + $DELAY_MS) / 1000 + 5" | bc)
    echo "  Using shell timeout: ${SHELL_TIMEOUT} seconds"
    timeout ${SHELL_TIMEOUT} venv/bin/python examples/integration_client.py "${CONNECTION_STRING}" --exit-after ${FRAME_COUNT} > output_aravis_${MODE}.log 2>&1 &
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

    # Add delay if specified
    if [ "$DELAY_MS" -gt 0 ]; then
        DELAY_SECONDS=$(echo "scale=3; $DELAY_MS / 1000" | bc)
        echo "  Delaying GStreamer launch by ${DELAY_MS}ms..."
        sleep $DELAY_SECONDS
    fi

    # Then start GStreamer pipeline with aravissrc
    echo -e "${GREEN}Step 2: Starting GStreamer pipeline with aravissrc${NC}"

    # Use the appropriate element based on mode
    if [ "$MODE" = "Duplex" ]; then
        GST_ELEMENT="zerofilter channel-name=${BUFFER_NAME}"
        GST_SINK="fakesink"
    else
        GST_ELEMENT="zerosink buffer-name=${BUFFER_NAME} sync=false"
        GST_SINK=""
    fi

    echo -e "${YELLOW}  Command:${NC} GST_DEBUG=\"zerosink:5,zerobuffer:5,statsfilter:3\" GST_PLUGIN_PATH=${PLUGIN_PATH} \\"
    echo "           gst-launch-1.0 aravissrc camera-name=${CAMERA_NAME} ! \\"
    echo "           video/x-raw,format=GRAY8 ! \\"
    echo "           statsfilter ! \\"
    echo "           ${GST_ELEMENT}${GST_SINK:+ ! ${GST_SINK}}"
    echo ""

    if [ "$MODE" = "Duplex" ]; then
        GST_DEBUG="zerofilter:5,zerobuffer:5,statsfilter:3" GST_PLUGIN_PATH=${PLUGIN_PATH} gst-launch-1.0 \
            aravissrc camera-name=${CAMERA_NAME} ! \
            video/x-raw,format=GRAY8 ! \
            statsfilter ! \
            zerofilter channel-name=${BUFFER_NAME} ! \
            fakesink &> gst_aravis_${MODE}.log &
    else
        GST_DEBUG="zerosink:5,zerobuffer:5,statsfilter:3" GST_PLUGIN_PATH=${PLUGIN_PATH} gst-launch-1.0 \
            aravissrc camera-name=${CAMERA_NAME} ! \
            video/x-raw,format=GRAY8 ! \
            statsfilter ! \
            zerosink buffer-name=${BUFFER_NAME} sync=false &> gst_aravis_${MODE}.log &
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
    if [ "$MODE" = "OneWay" ] && grep -q "Processed frame ${FRAME_COUNT}" output_aravis_${MODE}.log 2>/dev/null; then
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
    if grep -q "Processed frame" output_aravis_${MODE}.log 2>/dev/null; then
        PROCESSED=$(grep -c "Processed frame" output_aravis_${MODE}.log || echo "0")
        echo "  - Frames processed: ${PROCESSED}/${FRAME_COUNT}"
    fi

    # Show statsfilter output if available
    if grep -q "statsfilter" gst_aravis_${MODE}.log 2>/dev/null; then
        echo ""
        echo -e "${YELLOW}  Stats filter output:${NC}"
        grep "statsfilter" gst_aravis_${MODE}.log | tail -3
    fi

    # If test failed, show relevant logs
    if [ $CLIENT_EXIT_CODE -ne 0 ]; then
        echo ""
        echo -e "${YELLOW}=== GStreamer errors ===${NC}"
        if [ -f gst_aravis_${MODE}.log ]; then
            grep -E "ERROR|Failed" gst_aravis_${MODE}.log | head -5 || echo "  No errors found"
        fi

        echo ""
        echo -e "${YELLOW}=== Client errors ===${NC}"
        if [ -f output_aravis_${MODE}.log ]; then
            grep -E "ERROR|Error|Failed" output_aravis_${MODE}.log | head -5 || echo "  No errors found"
        fi
    fi

    return $CLIENT_EXIT_CODE
}

# Check if aravissrc is available
echo "Checking for aravissrc plugin..."
if ! gst-inspect-1.0 aravissrc &>/dev/null; then
    echo -e "${RED}Error: aravissrc plugin not found!${NC}"
    echo "Please install gstreamer1.0-aravis package or build aravis with GStreamer support"
    exit 1
fi

# Test duplex mode
echo -e "${GREEN}=== Testing Duplex Mode with Aravis ===${NC}"
if run_test "Duplex"; then
    DUPLEX_RESULT="PASS"
else
    DUPLEX_RESULT="FAIL"
fi

echo ""

# Test oneway mode
echo -e "${GREEN}=== Testing OneWay Mode with Aravis ===${NC}"
if run_test "OneWay"; then
    ONEWAY_RESULT="PASS"
else
    ONEWAY_RESULT="FAIL"
fi

# Summary
echo ""
echo "========================================="
echo "Test Summary - Aravis Camera Source"
echo "========================================="
echo -e "Duplex mode:  ${DUPLEX_RESULT}"
echo -e "OneWay mode:  ${ONEWAY_RESULT}"
echo ""
echo "Logs available:"
echo "  - gst_aravis_Duplex.log, gst_aravis_OneWay.log (GStreamer output)"
echo "  - output_aravis_Duplex.log, output_aravis_OneWay.log (Client output)"

if [ "$DUPLEX_RESULT" = "PASS" ] && [ "$ONEWAY_RESULT" = "PASS" ]; then
    echo -e "\n${GREEN}All tests passed!${NC}"
    exit 0
else
    echo -e "\n${RED}Some tests failed.${NC}"
    exit 1
fi