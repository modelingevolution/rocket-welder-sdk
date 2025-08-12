#!/bin/bash

# Build script for Python Rocket Welder SDK

set -e

echo "Building Python Rocket Welder SDK..."

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Clean previous builds
echo "Cleaning previous builds..."
rm -rf build dist *.egg-info 2>/dev/null || true

# Create/activate virtual environment if it doesn't exist
if [ ! -d "venv" ]; then
    echo "Creating virtual environment..."
    python3 -m venv venv
fi

# Activate virtual environment
source venv/bin/activate

# Upgrade pip and install build tools
echo "Installing build tools..."
pip install --upgrade pip setuptools wheel build

# Install dependencies
echo "Installing dependencies..."
pip install numpy opencv-python

# Build the package
echo "Building package..."
python -m build

# Install in development mode for testing
echo "Installing in development mode..."
pip install -e .

echo "Python build completed successfully!"
echo "Wheel package: $SCRIPT_DIR/dist/rocket_welder_sdk-*.whl"
echo "To use: source $SCRIPT_DIR/venv/bin/activate"