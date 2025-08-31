#!/bin/bash
# Test to track exact timing of buffer creation

set -e

BUFFER_NAME="test_timing_$(date +%s)"
echo "Testing buffer: $BUFFER_NAME"
echo "Start time: $(date '+%H:%M:%S.%N')"
echo ""

# Start container
echo "[$(date '+%H:%M:%S.%N')] Starting Docker container..."
docker run -d \
    --name test-timing \
    --privileged \
    --ipc=host \
    -v /dev/shm:/dev/shm \
    -e CONNECTION_STRING="shm://${BUFFER_NAME}?mode=Duplex" \
    -e ROCKET_WELDER_LOG_LEVEL=DEBUG \
    rocket-welder-sdk-test:latest \
    --exit-after=100 > /dev/null

# Monitor buffer creation
echo "[$(date '+%H:%M:%S.%N')] Monitoring buffer creation..."
echo ""

# Watch for request buffer
for i in {1..30}; do
    if [ -e "/dev/shm/${BUFFER_NAME}_request" ]; then
        echo "[$(date '+%H:%M:%S.%N')] REQUEST buffer created: $(ls -la /dev/shm/${BUFFER_NAME}_request 2>/dev/null)"
        break
    fi
    sleep 0.1
done

# Wait for OIEB initialization
echo "[$(date '+%H:%M:%S.%N')] Waiting for OIEB initialization..."
sleep 1.5

# Start GStreamer in background and monitor
echo "[$(date '+%H:%M:%S.%N')] Starting GStreamer..."
(
    GST_DEBUG="*:3,zerobuffer*:5" GST_PLUGIN_PATH="/mnt/d/source/modelingevolution/streamer/src/out/build/Linux-WSL-Debug/app/plugins" \
    sudo -E gst-launch-1.0 \
        videotestsrc num-buffers=5 pattern=ball ! \
        video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
        zerofilter channel-name="${BUFFER_NAME}" ! \
        fakesink 2>&1 | while IFS= read -r line; do
            echo "[$(date '+%H:%M:%S.%N')] GST: $line"
        done
) &

GST_PID=$!

# Monitor for response buffer creation
echo ""
echo "[$(date '+%H:%M:%S.%N')] Monitoring for response buffer..."
for i in {1..100}; do
    if [ -e "/dev/shm/${BUFFER_NAME}_response" ]; then
        echo "[$(date '+%H:%M:%S.%N')] RESPONSE buffer created!"
        stat_info=$(stat -c "Birth: %w, Access: %x, Modify: %y, Change: %z" /dev/shm/${BUFFER_NAME}_response 2>/dev/null || echo "stat not available")
        ls_info=$(ls -la --full-time /dev/shm/${BUFFER_NAME}_response 2>/dev/null)
        echo "  File info: $ls_info"
        echo "  Stat info: $stat_info"
        
        # Check OIEB of response buffer
        echo ""
        echo "[$(date '+%H:%M:%S.%N')] Checking response buffer OIEB:"
        sudo ./oieb_reader "${BUFFER_NAME}_response" 2>&1 | head -20
        break
    fi
    sleep 0.1
done

# Let it run for a few seconds
sleep 3

# Get container logs with timestamps
echo ""
echo "[$(date '+%H:%M:%S.%N')] Container logs:"
docker logs test-timing 2>&1 | tail -20

# Clean up
echo ""
echo "[$(date '+%H:%M:%S.%N')] Cleaning up..."
kill $GST_PID 2>/dev/null || true
docker stop test-timing > /dev/null 2>&1
docker rm test-timing > /dev/null 2>&1

echo ""
echo "[$(date '+%H:%M:%S.%N')] Test complete"