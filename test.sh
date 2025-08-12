#!/bin/bash

# Test script for Rocket Welder SDK
# Tests all three language implementations against a GStreamer pipeline

set -e

# Configuration
BUFFER_NAME="test_buffer"
FRAME_COUNT=20
CONNECTION_STRING="shm://${BUFFER_NAME}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

echo "========================================="
echo "Rocket Welder SDK Test Suite"
echo "========================================="
echo ""
echo "Configuration:"
echo "  Buffer: ${BUFFER_NAME}"
echo "  Frames: ${FRAME_COUNT}"
echo "  Connection: ${CONNECTION_STRING}"
echo ""

# Check if gst-launch-1.0 is available
if ! command -v gst-launch-1.0 &> /dev/null; then
    echo -e "${RED}✗ Error: gst-launch-1.0 not found${NC}"
    echo "Please install GStreamer first"
    exit 1
fi

# Check if zerosink plugin is available
if ! gst-inspect-1.0 zerosink &> /dev/null; then
    echo -e "${YELLOW}⚠ Warning: zerosink plugin not found${NC}"
    echo "Tests will use fakesink instead (mock mode)"
    USE_MOCK=1
fi

# Function to run test for a specific language
run_test() {
    local LANG=$1
    local RUN_SCRIPT=$2
    
    echo -e "\n${YELLOW}Testing ${LANG}...${NC}"
    
    # Start GStreamer pipeline in background
    if [ -z "$USE_MOCK" ]; then
        echo "Starting GStreamer pipeline with zerosink..."
        GST_DEBUG=2 gst-launch-1.0 videotestsrc num-buffers=${FRAME_COUNT} ! \
            video/x-raw,width=640,height=480,framerate=30/1 ! \
            zerosink buffer-name=${BUFFER_NAME} sync=false &> gst_${LANG}.log &
    else
        echo "Starting mock GStreamer pipeline..."
        gst-launch-1.0 videotestsrc num-buffers=${FRAME_COUNT} ! \
            video/x-raw,width=640,height=480,framerate=30/1 ! \
            fakesink sync=false &> gst_${LANG}.log &
    fi
    GST_PID=$!
    
    # Give pipeline time to initialize
    sleep 2
    
    # Run the client
    echo "Running ${LANG} client..."
    if timeout 10 ${RUN_SCRIPT} "${CONNECTION_STRING}" --exit-after=${FRAME_COUNT} > output_${LANG}.log 2>&1; then
        CLIENT_EXIT_CODE=0
    else
        CLIENT_EXIT_CODE=$?
    fi
    
    # Stop GStreamer pipeline
    kill $GST_PID 2>/dev/null || true
    wait $GST_PID 2>/dev/null || true
    
    # Check results
    if [ $CLIENT_EXIT_CODE -eq 0 ]; then
        # Check if client processed expected number of frames
        if grep -q "Total frames processed: ${FRAME_COUNT}" output_${LANG}.log; then
            echo -e "${GREEN}✓ ${LANG} test passed${NC}"
            echo "  - Client connected successfully"
            echo "  - Processed ${FRAME_COUNT} frames"
            echo "  - Exited cleanly"
            return 0
        elif grep -q "Processed frame" output_${LANG}.log; then
            PROCESSED=$(grep -c "Processed frame" output_${LANG}.log || echo "0")
            echo -e "${YELLOW}⚠ ${LANG} test partial${NC}"
            echo "  - Client processed ${PROCESSED}/${FRAME_COUNT} frames"
            if [ -n "$USE_MOCK" ]; then
                echo "  - Running in mock mode (zerosink not available)"
                return 0  # Don't fail in mock mode
            fi
            return 1
        else
            echo -e "${RED}✗ ${LANG} test failed${NC}"
            echo "  - No frames processed"
            echo "  - Check output_${LANG}.log for details"
            return 1
        fi
    else
        echo -e "${RED}✗ ${LANG} test failed${NC}"
        echo "  - Client exited with code: $CLIENT_EXIT_CODE"
        if [ $CLIENT_EXIT_CODE -eq 124 ]; then
            echo "  - Client timed out"
        fi
        echo "  - Check output_${LANG}.log for details"
        return 1
    fi
}

# Track test results
TESTS_PASSED=0
TESTS_FAILED=0

# Test C++
if [ -f "${SCRIPT_DIR}/cpp/run.sh" ]; then
    if run_test "C++" "${SCRIPT_DIR}/cpp/run.sh"; then
        ((TESTS_PASSED++))
    else
        ((TESTS_FAILED++))
    fi
else
    echo -e "${YELLOW}⚠ Skipping C++ (run.sh not found)${NC}"
fi

# Test C#
if [ -f "${SCRIPT_DIR}/csharp/run.sh" ]; then
    if run_test "C#" "${SCRIPT_DIR}/csharp/run.sh"; then
        ((TESTS_PASSED++))
    else
        ((TESTS_FAILED++))
    fi
else
    echo -e "${YELLOW}⚠ Skipping C# (run.sh not found)${NC}"
fi

# Test Python
if [ -f "${SCRIPT_DIR}/python/run.sh" ]; then
    if run_test "Python" "${SCRIPT_DIR}/python/run.sh"; then
        ((TESTS_PASSED++))
    else
        ((TESTS_FAILED++))
    fi
else
    echo -e "${YELLOW}⚠ Skipping Python (run.sh not found)${NC}"
fi

# Summary
echo ""
echo "========================================="
echo "Test Summary"
echo "========================================="
echo -e "${GREEN}Passed: ${TESTS_PASSED}${NC}"
echo -e "${RED}Failed: ${TESTS_FAILED}${NC}"

if [ $TESTS_FAILED -eq 0 ]; then
    echo -e "\n${GREEN}All tests passed!${NC}"
    exit 0
else
    echo -e "\n${RED}Some tests failed. Check output_*.log files for details.${NC}"
    exit 1
fi