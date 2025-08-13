#!/bin/bash

# Run script for Python RocketWelder SDK
# Maps command line arguments to environment variables (mirrors C# run.sh)

set -e

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Parse first positional argument as CONNECTION_STRING if it looks like a connection string
if [[ $1 =~ ^(shm|mjpeg\+http|mjpeg\+tcp):// ]]; then
    export CONNECTION_STRING="$1"
    shift
fi

# Run the Python client with remaining arguments
exec python3 "$SCRIPT_DIR/examples/simple_client.py" "$@"