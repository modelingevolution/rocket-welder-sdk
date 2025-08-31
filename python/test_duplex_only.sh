#!/bin/bash
# Quick test for duplex mode with full logging

set -e

# Configuration
BUFFER_NAME="test_duplex_$(date +%s)"
FRAMES=5
TIMEOUT=15
GST_PLUGIN_PATH="${GST_PLUGIN_PATH:-/mnt/d/source/modelingevolution/streamer/src/out/build/Linux-WSL-Debug/app/plugins}"
DOCKER_IMAGE="rocket-welder-client-python:latest"

echo "Testing Duplex Mode"
echo "Buffer: $BUFFER_NAME"
echo "Start time: $(date '+%H:%M:%S.%3N')"
echo ""

# Clean up old containers
docker stop rocket-welder-test-duplex 2>/dev/null || true
docker rm rocket-welder-test-duplex 2>/dev/null || true

# Start Docker container as duplex server (privileged mode)
echo "[$(date '+%H:%M:%S.%3N')] Starting privileged Docker container..."
docker run -d \
    --name rocket-welder-test-duplex \
    --privileged \
    --ipc=host \
    --cap-add=IPC_OWNER \
    -v /dev/shm:/dev/shm \
    -v /mnt/d/source/modelingevolution/rocket-welder-sdk/python/check_buffer.py:/check_buffer.py:ro \
    -e CONNECTION_STRING="shm://${BUFFER_NAME}?mode=Duplex" \
    -e ROCKET_WELDER_LOG_LEVEL=DEBUG \
    "$DOCKER_IMAGE" \
    --exit-after="$FRAMES" \
    --log-level=DEBUG

# Wait for request buffer
echo "[$(date '+%H:%M:%S.%3N')] Waiting for request buffer..."
for i in {1..10}; do
    if [ -e "/dev/shm/${BUFFER_NAME}_request" ]; then
        echo "[$(date '+%H:%M:%S.%3N')] ✓ Request buffer created"
        break
    fi
    sleep 0.5
done

# Check buffer permissions
echo ""
echo "[$(date '+%H:%M:%S.%3N')] Shared memory buffers:"
ls -la --full-time /dev/shm/${BUFFER_NAME}* 2>/dev/null || echo "No buffers found"

# Wait for Python to initialize OIEB
echo "[$(date '+%H:%M:%S.%3N')] Waiting for OIEB initialization..."
sleep 2

# Check OIEB structure
echo ""
echo "[$(date '+%H:%M:%S.%3N')] Checking OIEB structure in request buffer:"
sudo ./oieb_reader "${BUFFER_NAME}_request"

# Start GStreamer with sudo (needed for root-owned buffers)
echo ""
echo "[$(date '+%H:%M:%S.%3N')] Starting GStreamer pipeline with sudo..."

# Set environment variables in current shell
export GST_DEBUG="*:3,zerobuffer*:5"
export GST_PLUGIN_PATH="$GST_PLUGIN_PATH"

# Run with sudo -E to preserve environment
sudo -E gst-launch-1.0 \
    videotestsrc num-buffers="$FRAMES" pattern=ball ! \
    video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
    zerofilter channel-name="$BUFFER_NAME" ! \
    fakesink 2>&1 | tee gstreamer_duplex.log &

GST_PID=$!

# Monitor for response buffer creation
echo "[$(date '+%H:%M:%S.%3N')] Monitoring for response buffer creation..."
for i in {1..20}; do
    if [ -e "/dev/shm/${BUFFER_NAME}_response" ]; then
        echo "[$(date '+%H:%M:%S.%3N')] ✓ RESPONSE buffer created!"
        ls -la --full-time /dev/shm/${BUFFER_NAME}_response
        echo ""
        echo "[$(date '+%H:%M:%S.%3N')] Waiting for OIEB initialization in response buffer..."
        for j in {1..10}; do
            echo "[$(date '+%H:%M:%S.%3N')] Attempt $j - Checking OIEB of response buffer:"
            OIEB_CHECK=$(sudo ./oieb_reader "${BUFFER_NAME}_response" 2>&1 | grep "OIEB size field:")
            echo "  $OIEB_CHECK"
            if [[ "$OIEB_CHECK" == *"128"* ]]; then
                echo "[$(date '+%H:%M:%S.%3N')] ✓ OIEB initialized correctly!"
                sudo ./oieb_reader "${BUFFER_NAME}_response" 2>&1 | head -30
                break
            fi
            sleep 0.1
        done
        echo ""
        echo "[$(date '+%H:%M:%S.%3N')] Checking response buffer from inside container:"
        docker exec rocket-welder-test-duplex python3 /check_buffer.py "${BUFFER_NAME}_response" 2>&1
        break
    fi
    sleep 0.1
done

echo ""
echo "[$(date '+%H:%M:%S.%3N')] All buffers:"
ls -la --full-time /dev/shm/${BUFFER_NAME}* 2>/dev/null || echo "No buffers found"

# Wait for GStreamer to finish
echo "[$(date '+%H:%M:%S.%3N')] Waiting for GStreamer to complete..."
wait $GST_PID
echo "[$(date '+%H:%M:%S.%3N')] GStreamer finished"

# Show container logs
echo ""
echo "[$(date '+%H:%M:%S.%3N')] Container logs:"
docker logs rocket-welder-test-duplex

# Clean up
echo ""
echo "[$(date '+%H:%M:%S.%3N')] Cleaning up..."
docker stop rocket-welder-test-duplex
