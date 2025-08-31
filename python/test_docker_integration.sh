#!/bin/bash
# Docker Integration Test for Python Rocket Welder SDK
# Tests shared memory IPC between GStreamer (host) and Docker container

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Configuration
DOCKER_IMAGE="rocket-welder-sdk-test:latest"
BUFFER_NAME="test_docker_$(date +%s)"  # Unique buffer name with timestamp
FRAMES=5
TIMEOUT=15
GST_PLUGIN_PATH="${GST_PLUGIN_PATH:-/mnt/d/source/modelingevolution/streamer/src/out/build/Linux-WSL-Debug/app/plugins}"

# Log files
LOG_DIR="$SCRIPT_DIR/docker_test_logs"
mkdir -p "$LOG_DIR"
CONTAINER_LOG="$LOG_DIR/container.log"
GST_LOG="$LOG_DIR/gstreamer.log"

echo "========================================="
echo "Docker Integration Test for RocketWelder SDK"
echo "========================================="
echo "Buffer name: $BUFFER_NAME"
echo "Frames: $FRAMES"
echo "Timeout: ${TIMEOUT}s"
echo "Logs: $LOG_DIR"
echo ""

# Function to cleanup resources
cleanup() {
    echo -e "${YELLOW}Cleaning up...${NC}"
    
    # Stop any running containers (but don't remove them for debugging)
    docker ps -q --filter "name=rocket-welder-test" | xargs -r docker stop 2>/dev/null || true
    echo "  Containers stopped but not removed (use 'docker rm' to remove)"
    echo "  To inspect: docker logs rocket-welder-test-oneway"
    echo "             docker logs rocket-welder-test-duplex"
    
    # Clean up shared memory
    for shm in /dev/shm/${BUFFER_NAME}*; do
        [ -e "$shm" ] && rm -f "$shm" 2>/dev/null || true
    done
    
    # Clean up semaphores
    for sem in /dev/shm/sem.*${BUFFER_NAME}*; do
        [ -e "$sem" ] && rm -f "$sem" 2>/dev/null || true
    done
}

# Set up cleanup trap
trap cleanup EXIT INT TERM

# Check if sudo is needed for gst-launch-1.0
check_gstreamer_permissions() {
    echo -e "${YELLOW}Checking GStreamer permissions...${NC}"
    
    # Try to run gst-launch-1.0 without sudo
    if gst-launch-1.0 --version >/dev/null 2>&1; then
        echo "✓ GStreamer can run without sudo"
        GST_CMD="gst-launch-1.0"
    else
        # Check if we have passwordless sudo for gst-launch-1.0
        if sudo -n gst-launch-1.0 --version >/dev/null 2>&1; then
            echo "✓ Passwordless sudo configured for GStreamer"
            GST_CMD="sudo gst-launch-1.0"
        else
            echo -e "${RED}✗ GStreamer requires sudo permission${NC}"
            echo ""
            echo "To enable passwordless sudo for gst-launch-1.0, run:"
            echo ""
            echo '  echo "$USER ALL=(ALL) NOPASSWD: /usr/bin/gst-launch-1.0" | sudo tee /tmp/gstreamer-test'
            echo '  sudo visudo -c -f /tmp/gstreamer-test && sudo mv /tmp/gstreamer-test /etc/sudoers.d/gstreamer-test'
            echo '  sudo chmod 440 /etc/sudoers.d/gstreamer-test'
            echo ""
            exit 1
        fi
    fi
}

# Build Docker image
build_docker_image() {
    echo -e "${YELLOW}Building Docker image...${NC}"
    
    # Create a test-specific Dockerfile with DEBUG logging
    cat > "$SCRIPT_DIR/Dockerfile.test" <<'EOF'
FROM python:3.12-slim-bookworm

WORKDIR /app

# Install runtime dependencies
RUN apt-get update && apt-get install -y \
    libgomp1 libglib2.0-0 libsm6 libxext6 libxrender1 libgl1 \
    libavcodec-dev libavformat-dev libswscale-dev libv4l-dev \
    libjpeg-dev libpng-dev libtiff-dev \
    libatlas-base-dev gfortran \
    libgstreamer1.0-0 libgstreamer-plugins-base1.0-0 \
    procps iputils-ping net-tools \
    && rm -rf /var/lib/apt/lists/*

# Copy and install dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy and install the SDK
COPY rocket_welder_sdk/ /app/rocket_welder_sdk/
COPY setup.py pyproject.toml MANIFEST.in README.md ./
RUN pip install --no-cache-dir .

# Copy the example application
COPY examples/simple_client.py .

# Enable DEBUG logging for testing
ENV ROCKET_WELDER_LOG_LEVEL=DEBUG
ENV PYTHONUNBUFFERED=1

ENTRYPOINT ["python", "simple_client.py"]
EOF

    # Build the image
    docker build -f Dockerfile.test -t "$DOCKER_IMAGE" . >/dev/null 2>&1
    if [ $? -eq 0 ]; then
        echo "✓ Docker image built successfully"
    else
        echo -e "${RED}✗ Failed to build Docker image${NC}"
        exit 1
    fi
    
    # Clean up test Dockerfile
    rm -f "$SCRIPT_DIR/Dockerfile.test"
}

# Test OneWay mode
test_oneway_mode() {
    echo ""
    echo -e "${GREEN}=== Testing OneWay Mode ===${NC}"
    echo "Docker container reads, GStreamer writes"
    echo ""
    
    local buffer="${BUFFER_NAME}_oneway"
    local success=false
    
    # Start Docker container as reader (WITHOUT --rm for debugging)
    echo "Starting Docker container as reader (user: $(id -u):$(id -g))..."
    docker run -d \
        --name rocket-welder-test-oneway \
        --ipc=host \
        -v /dev/shm:/dev/shm \
        --user "$(id -u):$(id -g)" \
        -e CONNECTION_STRING="shm://${buffer}?mode=OneWay" \
        -e ROCKET_WELDER_LOG_LEVEL=DEBUG \
        "$DOCKER_IMAGE" \
        --exit-after="$FRAMES" \
        > "$CONTAINER_LOG" 2>&1 &
    
    CONTAINER_PID=$!
    
    # Wait for buffer to be created
    echo "Waiting for buffer creation..."
    for i in {1..10}; do
        if [ -e "/dev/shm/$buffer" ]; then
            echo "✓ Buffer created: /dev/shm/$buffer"
            break
        fi
        sleep 0.5
    done
    
    if [ ! -e "/dev/shm/$buffer" ]; then
        echo -e "${RED}✗ Buffer was not created${NC}"
        docker logs rocket-welder-test-oneway 2>&1 | tail -20
        docker stop rocket-welder-test-oneway 2>/dev/null || true
        return 1
    fi
    
    # Start GStreamer as writer
    echo "Starting GStreamer pipeline as writer..."
    GST_DEBUG="*:3,zerobuffer*:5" GST_PLUGIN_PATH="$GST_PLUGIN_PATH" \
    timeout "$TIMEOUT" $GST_CMD \
        videotestsrc num-buffers="$FRAMES" pattern=ball ! \
        video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
        zerosink buffer-name="$buffer" sync=false \
        > "$GST_LOG" 2>&1 &
    
    GST_PID=$!
    
    # Wait for completion
    echo "Processing frames..."
    
    # Monitor container logs in real-time showing DEBUG messages
    docker logs -f rocket-welder-test-oneway 2>&1 | while IFS= read -r line; do
        # Show all log lines for debugging
        echo "  [CONTAINER] $line"
        if echo "$line" | grep -q "Processed frame $FRAMES"; then
            success=true
            break
        fi
    done &
    LOG_PID=$!
    
    # Wait for processes to complete
    wait $CONTAINER_PID 2>/dev/null || true
    wait $GST_PID 2>/dev/null || true
    kill $LOG_PID 2>/dev/null || true
    
    # Check results
    echo ""
    if docker logs rocket-welder-test-oneway 2>&1 | grep -q "Total frames processed: $FRAMES"; then
        echo -e "${GREEN}✓ OneWay test PASSED${NC}"
        echo "  Successfully processed $FRAMES frames"
        return 0
    else
        echo -e "${RED}✗ OneWay test FAILED${NC}"
        echo ""
        echo "Container logs (last 30 lines):"
        docker logs rocket-welder-test-oneway 2>&1 | tail -30
        echo ""
        echo "GStreamer logs (full output):"
        cat "$GST_LOG" 2>/dev/null || echo "No GStreamer logs"
        echo ""
        echo "Shared memory status:"
        ls -la /dev/shm/${BUFFER_NAME}* 2>/dev/null || echo "No shared memory buffers found"
        return 1
    fi
}

# Test Duplex mode
test_duplex_mode() {
    echo ""
    echo -e "${GREEN}=== Testing Duplex Mode ===${NC}"
    echo "Bidirectional communication between Docker and GStreamer"
    echo ""
    
    local buffer="${BUFFER_NAME}_duplex"
    local success=false
    
    # Start Docker container as duplex server (WITHOUT --rm for debugging)
    # Using --privileged for duplex mode as discussed
    echo "Starting privileged Docker container as duplex server..."
    docker run -d \
        --name rocket-welder-test-duplex \
        --privileged \
        --ipc=host \
        --cap-add=IPC_OWNER \
        -v /dev/shm:/dev/shm \
        -e CONNECTION_STRING="shm://${buffer}?mode=Duplex" \
        -e ROCKET_WELDER_LOG_LEVEL=DEBUG \
        "$DOCKER_IMAGE" \
        --exit-after="$FRAMES" \
        > "$CONTAINER_LOG" 2>&1 &
    
    CONTAINER_PID=$!
    
    # Wait for request buffer to be created
    echo "Waiting for request buffer creation..."
    for i in {1..10}; do
        if [ -e "/dev/shm/${buffer}_request" ]; then
            echo "✓ Request buffer created: /dev/shm/${buffer}_request"
            break
        fi
        sleep 0.5
    done
    
    if [ ! -e "/dev/shm/${buffer}_request" ]; then
        echo -e "${RED}✗ Request buffer was not created${NC}"
        docker logs rocket-welder-test-duplex 2>&1 | tail -20
        docker stop rocket-welder-test-duplex 2>/dev/null || true
        return 1
    fi
    
    # Start GStreamer with zerofilter
    # Note: For privileged containers, GStreamer needs sudo to access root-owned buffers
    echo "Starting GStreamer pipeline with zerofilter..."
    GST_DEBUG="*:3,zerobuffer*:5" GST_PLUGIN_PATH="$GST_PLUGIN_PATH" \
    timeout "$TIMEOUT" sudo gst-launch-1.0 \
        videotestsrc num-buffers="$FRAMES" pattern=ball ! \
        video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! \
        zerofilter channel-name="$buffer" ! \
        fakesink > "$GST_LOG" 2>&1 &
    
    GST_PID=$!
    
    # Monitor container logs in real-time with DEBUG details
    echo "Processing frames (showing DEBUG logs)..."
    docker logs -f rocket-welder-test-duplex 2>&1 | while IFS= read -r line; do
        # Show all DEBUG, INFO, WARNING, ERROR lines
        if echo "$line" | grep -E "DEBUG|INFO|WARNING|ERROR"; then
            echo "  [CONTAINER] $line"
        fi
        if echo "$line" | grep -q "Processed frame $FRAMES"; then
            success=true
            break
        fi
    done &
    LOG_PID=$!
    
    # Wait for processes to complete
    wait $CONTAINER_PID 2>/dev/null || true
    wait $GST_PID 2>/dev/null || true
    kill $LOG_PID 2>/dev/null || true
    
    # Check results
    echo ""
    if docker logs rocket-welder-test-duplex 2>&1 | grep -q "Total frames processed: $FRAMES"; then
        echo -e "${GREEN}✓ Duplex test PASSED${NC}"
        echo "  Successfully processed $FRAMES frames bidirectionally"
        return 0
    else
        echo -e "${RED}✗ Duplex test FAILED${NC}"
        echo ""
        echo "Container logs (last 30 lines):"
        docker logs rocket-welder-test-duplex 2>&1 | tail -30
        echo ""
        echo "GStreamer logs (full output):"
        cat "$GST_LOG" 2>/dev/null || echo "No GStreamer logs"
        echo ""
        echo "Shared memory status:"
        ls -la /dev/shm/${BUFFER_NAME}* 2>/dev/null || echo "No shared memory buffers found"
        return 1
    fi
}

# Main test execution
main() {
    # Check permissions
    check_gstreamer_permissions
    
    # Build Docker image
    build_docker_image
    
    # Clean up any previous test artifacts
    cleanup
    
    # Run tests
    local oneway_result=1
    local duplex_result=1
    
    if test_oneway_mode; then
        oneway_result=0
    fi
    
    # Clean between tests
    cleanup
    
    if test_duplex_mode; then
        duplex_result=0
    fi
    
    # Summary
    echo ""
    echo "========================================="
    echo "Test Summary"
    echo "========================================="
    
    if [ $oneway_result -eq 0 ]; then
        echo -e "OneWay mode:  ${GREEN}PASS${NC}"
    else
        echo -e "OneWay mode:  ${RED}FAIL${NC}"
    fi
    
    if [ $duplex_result -eq 0 ]; then
        echo -e "Duplex mode:  ${GREEN}PASS${NC}"
    else
        echo -e "Duplex mode:  ${RED}FAIL${NC}"
    fi
    
    echo ""
    echo "Detailed logs available in: $LOG_DIR"
    echo ""
    echo "Docker containers kept for inspection:"
    echo "  docker logs rocket-welder-test-oneway    # OneWay mode container logs"
    echo "  docker logs rocket-welder-test-duplex    # Duplex mode container logs"
    echo "  docker inspect rocket-welder-test-oneway # Container details"
    echo "  docker inspect rocket-welder-test-duplex # Container details"
    echo ""
    echo "To remove containers:"
    echo "  docker rm rocket-welder-test-oneway rocket-welder-test-duplex"
    echo ""
    
    # Exit with failure if any test failed
    if [ $oneway_result -ne 0 ] || [ $duplex_result -ne 0 ]; then
        echo -e "${RED}Some tests failed.${NC}"
        exit 1
    else
        echo -e "${GREEN}All tests passed!${NC}"
        exit 0
    fi
}

# Run main function
main "$@"