#!/bin/bash

# Main build script for Rocket Welder SDK
# Builds all three libraries: C++, C#, and Python

set -e

echo "========================================="
echo "Building Rocket Welder SDK"
echo "========================================="

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Build C++ library
echo ""
echo "Building C++ library..."
echo "-----------------------------------------"
cd "$SCRIPT_DIR/cpp"
./build.sh

# Build C# library
echo ""
echo "Building C# library..."
echo "-----------------------------------------"
cd "$SCRIPT_DIR/csharp"
./build.sh

# Build Python library
echo ""
echo "Building Python library..."
echo "-----------------------------------------"
cd "$SCRIPT_DIR/python"
./build.sh

echo ""
echo "========================================="
echo "Build completed successfully!"
echo "========================================="
echo ""
echo "Libraries built:"
echo "  - C++:    cpp/build/librocket_welder_sdk.a"
echo "  - C#:     csharp/bin/Debug/net9.0/RocketWelder.SDK.dll"
echo "  - Python: python/dist/rocket-welder-sdk-*.whl"
echo ""