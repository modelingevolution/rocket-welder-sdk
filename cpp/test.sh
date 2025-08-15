#!/bin/bash

# Test script for C++ RocketWelder SDK
# Tests one-way mode with zerobuffer

set -e

# Default values
MODE="oneway"
EXIT_AFTER=5
BUFFER_NAME="cpptest"

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
            shift
            ;;
    esac
done

echo "========================================="
echo "Testing C++ RocketWelder SDK"
echo "========================================="
echo "Mode: $MODE"
echo "Exit after: $EXIT_AFTER frames"
echo "Buffer name: $BUFFER_NAME"
echo "========================================="

# Clean up any stale resources
rm -f /dev/shm/${BUFFER_NAME} /dev/shm/sem.sem-* 2>/dev/null || true

# Build the example if not already built
if [ ! -f examples/simple_client ]; then
    echo "Building C++ example..."
    mkdir -p build_simple
    cd build_simple
    
    # Simple build without full vcpkg
    g++ -std=c++20 \
        -I../include \
        -I/mnt/d/source/modelingevolution/streamer/src/zerobuffer/cpp/include \
        -I/usr/include/opencv4 \
        ../examples/simple_client.cpp \
        -lopencv_core -lopencv_imgproc -lopencv_videoio \
        -ljsoncpp \
        -pthread \
        -o ../examples/simple_client
    cd ..
fi

# Start the C++ client (reader) in background
echo "Starting C++ client (reader)..."
CONNECTION_STRING="shm://${BUFFER_NAME}?mode=${MODE}" ./examples/simple_client --exit-after ${EXIT_AFTER} &
CLIENT_PID=$!

# Give client time to create the buffer
sleep 2

# Start GStreamer (writer)
echo "Starting GStreamer (writer)..."
GST_PLUGIN_PATH=/mnt/d/source/modelingevolution/streamer/src/gstreamer/zerobuffer/build \
gst-launch-1.0 \
    videotestsrc num-buffers=${EXIT_AFTER} pattern=ball ! \
    video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
    zerosink buffer-name=${BUFFER_NAME} sync=false

# Wait for client to finish
echo "Waiting for client to finish..."
wait $CLIENT_PID

echo "Test completed successfully!"