#!/bin/bash

# Test script for C# client with zerosink
set -e

BUFFER_NAME="test_csharp_buffer"
FRAME_COUNT=10
CONNECTION_STRING="shm://${BUFFER_NAME}"

echo "========================================="
echo "Testing C# Client with ZeroBuffer Sink"
echo "========================================="
echo ""
echo "Buffer: ${BUFFER_NAME}"
echo "Frames: ${FRAME_COUNT}"
echo "Connection: ${CONNECTION_STRING}"
echo ""

# Build the C# client
echo "Building C# client..."
cd /mnt/d/source/modelingevolution/rocket-welder-sdk/csharp
dotnet build -q

# Start GStreamer pipeline with zerosink in background
echo "Starting GStreamer pipeline with zerosink..."
GST_PLUGIN_PATH=/mnt/d/source/modelingevolution/streamer/src/gstreamer/zerobuffer/build \
gst-launch-1.0 videotestsrc num-buffers=${FRAME_COUNT} pattern=ball ! \
    video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
    zerosink buffer-name=${BUFFER_NAME} sync=false &> gst_output.log &

GST_PID=$!

# Give pipeline time to initialize
sleep 2

# Run the C# client
echo "Running C# client..."
if timeout 10 dotnet run --project examples/SimpleClient -- "${CONNECTION_STRING}" --exit-after=${FRAME_COUNT} > client_output.log 2>&1; then
    CLIENT_EXIT_CODE=0
else
    CLIENT_EXIT_CODE=$?
fi

# Stop GStreamer pipeline
kill $GST_PID 2>/dev/null || true
wait $GST_PID 2>/dev/null || true

# Check results
echo ""
echo "Results:"
echo "--------"

if [ $CLIENT_EXIT_CODE -eq 0 ]; then
    if grep -q "Total frames processed: ${FRAME_COUNT}" client_output.log; then
        echo "✓ Test PASSED"
        echo "  - Client connected successfully"
        echo "  - Processed ${FRAME_COUNT} frames"
        echo "  - Exited cleanly"
    elif grep -q "Processed frame" client_output.log; then
        PROCESSED=$(grep -c "Processed frame" client_output.log || echo "0")
        echo "⚠ Test PARTIAL"
        echo "  - Client processed ${PROCESSED}/${FRAME_COUNT} frames"
    else
        echo "✗ Test FAILED"
        echo "  - No frames processed"
    fi
else
    echo "✗ Test FAILED"
    echo "  - Client exited with code: $CLIENT_EXIT_CODE"
    if [ $CLIENT_EXIT_CODE -eq 124 ]; then
        echo "  - Client timed out"
    fi
fi

echo ""
echo "Check client_output.log and gst_output.log for details"