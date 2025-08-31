#!/bin/bash
# Test OIEB structure check

BUFFER_NAME="test_oieb_$(date +%s)"
echo "Buffer: $BUFFER_NAME"

# Start container that will wait longer
echo "Starting container..."
docker run -d \
    --name test-oieb \
    --privileged \
    --ipc=host \
    -v /dev/shm:/dev/shm \
    -e CONNECTION_STRING="shm://${BUFFER_NAME}?mode=Duplex" \
    -e ROCKET_WELDER_LOG_LEVEL=DEBUG \
    rocket-welder-sdk-test:latest \
    --exit-after=100

# Wait for buffer
echo "Waiting for buffer..."
for i in {1..10}; do
    if [ -e "/dev/shm/${BUFFER_NAME}_request" ]; then
        echo "Buffer created"
        break
    fi
    sleep 0.5
done

# Give Python time to initialize OIEB
echo "Waiting for OIEB initialization..."
sleep 2

# Check OIEB with our C++ reader
echo ""
echo "OIEB check (requires sudo):"
sudo ./oieb_reader "${BUFFER_NAME}_request" || echo "Need sudo permissions"

# Clean up
docker stop test-oieb 2>/dev/null
docker rm test-oieb 2>/dev/null
