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

# Restore dependencies
echo "Restoring dependencies..."
dotnet restore

# Build
echo "Building..."
dotnet build --configuration Release

echo ""
echo "C# examples built successfully!"
echo "Example: $SCRIPT_DIR/examples/SimpleClient/bin/Release/net9.0/SimpleClient"
echo ""
echo "To run:"
echo "  export CONNECTION_STRING='shm://test' # or other connection string"
echo "  dotnet run --project $SCRIPT_DIR/examples/SimpleClient"
echo "Or:"
echo "  cd $SCRIPT_DIR/examples/SimpleClient"
echo "  dotnet run"