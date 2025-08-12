#!/bin/bash

# Build examples for C# Rocket Welder SDK

set -e

echo "Building C# Rocket Welder SDK examples..."

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Ensure library is built first
if [ ! -f "bin/Release/net9.0/RocketWelder.SDK.dll" ]; then
    echo "Library not found, building it first..."
    ./build.sh
fi

# Build examples
echo "Building SimpleClient example..."
cd examples/SimpleClient

# Publish as self-contained executable
echo "Publishing..."
dotnet publish --configuration Release --output ./publish --self-contained false

echo ""
echo "C# examples built successfully!"
echo "Executable: $SCRIPT_DIR/examples/SimpleClient/publish/SimpleClient"
echo ""
echo "To run:"
echo "  $SCRIPT_DIR/examples/SimpleClient/publish/SimpleClient 'shm://test' --exit-after=10"