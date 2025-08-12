#!/bin/bash

# Build/prepare examples for Python Rocket Welder SDK

set -e

echo "Preparing Python Rocket Welder SDK examples..."

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Ensure library is built and installed first
if [ ! -d "venv" ]; then
    echo "Virtual environment not found, building library first..."
    ./build.sh
fi

# Activate virtual environment
source venv/bin/activate

# Ensure the package is installed
echo "Ensuring rocket-welder-sdk is installed..."
pip install -e . --quiet

# Make examples executable
chmod +x examples/*.py

echo ""
echo "Python examples ready!"
echo "Example: $SCRIPT_DIR/examples/simple_client.py"
echo ""
echo "To run:"
echo "  source $SCRIPT_DIR/venv/bin/activate"
echo "  export CONNECTION_STRING='shm://test' # or other connection string"
echo "  python $SCRIPT_DIR/examples/simple_client.py"
echo ""
echo "Or with arguments:"
echo "  python $SCRIPT_DIR/examples/simple_client.py 'mjpeg+http://192.168.1.100:8080'"