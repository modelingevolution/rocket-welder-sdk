#!/bin/bash

# Test script for Python RocketWelder SDK
# Mirrors the C# test.sh functionality

set -e

# Default values
MODE="oneway"
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

# Export connection string for the client
export CONNECTION_STRING="$CONNECTION_STRING"

# Start the Python client (reader) in background
echo "Starting Python client (reader)..."
python3 examples/simple_client.py --exit-after "$EXIT_AFTER" &
CLIENT_PID=$!

# Give client time to create the buffer
sleep 2

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