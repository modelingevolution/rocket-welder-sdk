#!/bin/bash

# Build script for C++ Rocket Welder SDK

set -e

echo "Building C++ Rocket Welder SDK..."

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Create build directory
mkdir -p build
cd build

# Configure with CMake (without examples)
echo "Configuring with CMake..."
cmake .. -DCMAKE_BUILD_TYPE=Release -DBUILD_EXAMPLES=OFF

# Build
echo "Building..."
make -j$(nproc)

echo "C++ build completed successfully!"
echo "Library: $SCRIPT_DIR/build/librocket_welder_sdk.a"