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

# Try to find vcpkg toolchain
VCPKG_TOOLCHAIN=""
if [ -n "$VCPKG_ROOT" ]; then
    echo "Using vcpkg from VCPKG_ROOT: $VCPKG_ROOT"
    VCPKG_TOOLCHAIN="$VCPKG_ROOT/scripts/buildsystems/vcpkg.cmake"
elif [ -f "/mnt/d/source/vcpkg/scripts/buildsystems/vcpkg.cmake" ]; then
    echo "Using vcpkg from /mnt/d/source/vcpkg"
    VCPKG_TOOLCHAIN="/mnt/d/source/vcpkg/scripts/buildsystems/vcpkg.cmake"
elif [ -f "/usr/local/share/vcpkg/scripts/buildsystems/vcpkg.cmake" ]; then
    echo "Using system vcpkg"
    VCPKG_TOOLCHAIN="/usr/local/share/vcpkg/scripts/buildsystems/vcpkg.cmake"
fi

if [ -n "$VCPKG_TOOLCHAIN" ]; then
    echo "Configuring with vcpkg toolchain: $VCPKG_TOOLCHAIN"
    echo "This will download and build zerobuffer from the custom registry on first run..."
    cmake .. -DCMAKE_BUILD_TYPE=Release -DBUILD_EXAMPLES=OFF \
        -DCMAKE_TOOLCHAIN_FILE="$VCPKG_TOOLCHAIN"
else
    echo "Warning: vcpkg not found, trying without it..."
    echo "Note: This will likely fail if zerobuffer is not installed manually"
    cmake .. -DCMAKE_BUILD_TYPE=Release -DBUILD_EXAMPLES=OFF
fi

# Build
echo "Building..."
make -j$(nproc)

echo "C++ build completed successfully!"
echo "Library: $SCRIPT_DIR/build/librocket_welder_sdk.a"