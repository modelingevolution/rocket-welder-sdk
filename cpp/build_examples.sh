#!/bin/bash

# Build examples for C++ Rocket Welder SDK

set -e

echo "Building C++ Rocket Welder SDK examples..."

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Ensure library is built first
if [ ! -f "build/librocket_welder_sdk.a" ]; then
    echo "Library not found, building it first..."
    ./build.sh
fi

# Create build directory if it doesn't exist
mkdir -p build
cd build

# Configure with CMake (with examples enabled)
echo "Configuring with CMake (examples enabled)..."
cmake .. -DCMAKE_BUILD_TYPE=Release -DBUILD_EXAMPLES=ON

# Build only examples
echo "Building examples..."
make -j$(nproc) simple_client

echo ""
echo "C++ examples built successfully!"
echo "Example binary: $SCRIPT_DIR/build/examples/simple_client"
echo ""
echo "To run:"
echo "  export CONNECTION_STRING='shm://test' # or other connection string"
echo "  $SCRIPT_DIR/build/examples/simple_client"