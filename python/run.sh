#!/bin/bash

# Run script for Python Rocket Welder SDK example
# Usage: ./run.sh <connection_string> [--exit-after=N]

set -e

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Make sure virtual environment exists
if [ ! -d "$SCRIPT_DIR/venv" ]; then
    echo "Creating virtual environment..."
    python3 -m venv "$SCRIPT_DIR/venv"
    "$SCRIPT_DIR/venv/bin/pip" install --quiet --upgrade pip
    "$SCRIPT_DIR/venv/bin/pip" install --quiet numpy opencv-python
fi

# Configure logging - use ROCKET_WELDER_LOG_LEVEL if set, otherwise default to DEBUG
if [ -z "$ROCKET_WELDER_LOG_LEVEL" ]; then
    export ROCKET_WELDER_LOG_LEVEL="DEBUG"
fi

# The SDK will propagate ROCKET_WELDER_LOG_LEVEL to ZEROBUFFER_LOG_LEVEL automatically

# Convert parameters from --key=value to --key value format for Python argparse
ARGS=()
FIRST_ARG=true

for arg in "$@"; do
    if [[ "$arg" == --*=* ]]; then
        # Split --key=value into --key value
        key="${arg%%=*}"
        value="${arg#*=}"
        ARGS+=("$key" "$value")
    elif $FIRST_ARG && [[ "$arg" != --* ]]; then
        # First positional argument is the connection string
        ARGS+=("$arg")
        FIRST_ARG=false
    else
        ARGS+=("$arg")
        FIRST_ARG=false
    fi
done

echo "Logging enabled: ROCKET_WELDER_LOG_LEVEL=$ROCKET_WELDER_LOG_LEVEL"

# Run the example with converted arguments
exec "$SCRIPT_DIR/venv/bin/python" "$SCRIPT_DIR/examples/simple_client.py" "${ARGS[@]}"