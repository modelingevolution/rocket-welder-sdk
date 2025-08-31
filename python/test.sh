#!/bin/bash

# Test script for Python RocketWelder SDK
# Supports both unit testing and integration testing

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

# Check if first argument is 'unit' for unit testing
if [ "$1" = "unit" ]; then
    echo "========================================="
    echo "Running Unit Tests"
    echo "========================================="
    
    # Ensure virtual environment exists
    if [ ! -d "venv" ]; then
        echo "Creating virtual environment..."
        python3 -m venv venv
    fi
    
    # Install dependencies
    echo "Installing dependencies..."
    venv/bin/pip install --quiet --upgrade pip
    venv/bin/pip install --quiet pytest pytest-cov numpy opencv-python 2>/dev/null || {
        echo "Installing packages..."
        venv/bin/pip install pytest pytest-cov numpy opencv-python
    }
    
    # Run unit tests
    echo ""
    echo "Running unit tests with pytest..."
    venv/bin/python -m pytest tests/ -v --tb=short
    exit $?
fi

# Integration test mode (original functionality)
# Default values
MODE="OneWay"
EXIT_AFTER=10
BUFFER_NAME="test"
CONNECTION_STRING=""

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --mode)
            MODE="$2"
            shift 2
            ;;
        --exit-after)
            EXIT_AFTER="$2"
            shift 2
            ;;
        --buffer-name)
            BUFFER_NAME="$2"
            shift 2
            ;;
        *)
            CONNECTION_STRING="$1"
            shift
            ;;
    esac
done

# Set default connection string if not provided
if [ -z "$CONNECTION_STRING" ]; then
    CONNECTION_STRING="shm://${BUFFER_NAME}?mode=${MODE}"
fi

echo "========================================="
echo "Testing Python RocketWelder SDK"
echo "========================================="
echo "Mode: $MODE"
echo "Exit after: $EXIT_AFTER frames"
echo "Connection: $CONNECTION_STRING"
echo "========================================="

# Clean up any existing resources before starting
echo "Cleaning up existing resources..."
rm -f /dev/shm/${BUFFER_NAME}* 2>/dev/null || true
rm -f /tmp/zerobuffer/${BUFFER_NAME}.lock 2>/dev/null || true

# Export connection string for the client
export CONNECTION_STRING="$CONNECTION_STRING"

# Ensure virtual environment exists and has dependencies
if [ ! -d "venv" ]; then
    echo "Creating virtual environment..."
    python3 -m venv venv
    venv/bin/pip install --quiet --upgrade pip
    venv/bin/pip install --quiet -e . 2>/dev/null || venv/bin/pip install -e .
fi

# Start the Python client (reader) in background using venv
echo "Starting Python client (reader)..."
venv/bin/python examples/simple_client.py --exit-after "$EXIT_AFTER" &
CLIENT_PID=$!

# Give client time to create the buffer
echo "Waiting for buffer creation..."
sleep 3

# Set GStreamer plugin path
GST_PLUGIN_PATH="/mnt/d/source/modelingevolution/streamer/src/out/build/Linux-WSL-Debug/app/plugins"
export GST_PLUGIN_PATH

# Start GStreamer (writer)
echo "Starting GStreamer (writer)..."
gst-launch-1.0 \
    videotestsrc num-buffers="$EXIT_AFTER" pattern=ball ! \
    video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
    zerosink buffer-name="$BUFFER_NAME" sync=false

# Wait for client to finish
echo "Waiting for client to finish..."
wait $CLIENT_PID

echo "Test completed successfully!"