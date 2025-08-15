#!/bin/bash

# Test build script for C++ SDK
set -e

echo "Building C++ SDK tests..."

# Create build directory
mkdir -p build
cd build

# Configure with direct paths to dependencies
cmake .. \
    -DCMAKE_BUILD_TYPE=Debug \
    -DBUILD_TESTS=ON \
    -DBUILD_EXAMPLES=OFF \
    -DCMAKE_PREFIX_PATH="/mnt/d/source/modelingevolution/streamer/src/zerobuffer/cpp/build" \
    -Dzerobuffer_DIR="/mnt/d/source/modelingevolution/streamer/src/zerobuffer/cpp/build" \
    -DCMAKE_CXX_FLAGS="-I/mnt/d/source/modelingevolution/streamer/src/zerobuffer/cpp/include"

# Build
make -j$(nproc)

# Run tests
echo "Running unit tests..."
ctest --output-on-failure

echo "Tests completed successfully!"