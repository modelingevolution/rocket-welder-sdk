#!/bin/bash
# Script to run RocketWelder Docker containers with X11 display support
# Usage: ./run_docker_x11.sh [python|csharp] [video_path]
#
# This script delegates to the appropriate run_docker_x11.sh in the python or csharp directory

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default to python if no argument provided
CLIENT_TYPE="${1:-python}"
VIDEO_PATH="${2:-}"

echo -e "${BLUE}=== RocketWelder Docker X11 Runner ===${NC}"
echo -e "${BLUE}Client Type: ${CLIENT_TYPE}${NC}"

# If video path is provided and is relative, prepend ../ since we'll cd into subdirectory
if [ ! -z "$VIDEO_PATH" ]; then
    if [[ "$VIDEO_PATH" != /* ]]; then
        # It's a relative path, prepend ../
        VIDEO_PATH="../$VIDEO_PATH"
        echo -e "${BLUE}Adjusted relative path: ${VIDEO_PATH}${NC}"
    else
        echo -e "${BLUE}Using absolute path: ${VIDEO_PATH}${NC}"
    fi
fi

# Get the script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Validate client type and run the appropriate script
if [ "$CLIENT_TYPE" == "python" ]; then
    RUNNER_SCRIPT="$SCRIPT_DIR/python/run_docker_x11.sh"

    if [ ! -f "$RUNNER_SCRIPT" ]; then
        echo -e "${RED}Error: Python run_docker_x11.sh not found at $RUNNER_SCRIPT${NC}"
        exit 1
    fi

    echo -e "${GREEN}Running Python Docker X11 client...${NC}"
    cd "$SCRIPT_DIR/python"
    if [ ! -z "$VIDEO_PATH" ]; then
        exec ./run_docker_x11.sh "$VIDEO_PATH"
    else
        exec ./run_docker_x11.sh
    fi

elif [ "$CLIENT_TYPE" == "csharp" ]; then
    RUNNER_SCRIPT="$SCRIPT_DIR/csharp/run_docker_x11.sh"

    if [ ! -f "$RUNNER_SCRIPT" ]; then
        echo -e "${RED}Error: C# run_docker_x11.sh not found at $RUNNER_SCRIPT${NC}"
        exit 1
    fi

    echo -e "${GREEN}Running C# Docker X11 client...${NC}"
    cd "$SCRIPT_DIR/csharp"
    if [ ! -z "$VIDEO_PATH" ]; then
        exec ./run_docker_x11.sh "$VIDEO_PATH"
    else
        exec ./run_docker_x11.sh
    fi

else
    echo -e "${RED}Error: Invalid client type '${CLIENT_TYPE}'${NC}"
    echo "Usage: $0 [python|csharp] [video_path]"
    echo "  python - Run the Python client with preview"
    echo "  csharp - Run the C# client with preview"
    echo "  video_path - Optional path to video file (default: data/test_stream.mp4)"
    exit 1
fi