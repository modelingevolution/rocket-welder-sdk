#!/bin/bash

# Run script for Python Rocket Welder SDK example
# Usage: ./run.sh <connection_string> [--exit-after=N]

set -e

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Ensure virtual environment exists and package is installed
if [ ! -d "$SCRIPT_DIR/venv" ]; then
    echo "Building Python environment first..."
    "$SCRIPT_DIR/build.sh"
fi

# Activate virtual environment and run
source "$SCRIPT_DIR/venv/bin/activate"
exec python "$SCRIPT_DIR/examples/simple_client.py" "$@"