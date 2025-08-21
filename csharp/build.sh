#!/bin/bash

# Build script for C# Rocket Welder SDK

set -e

echo "Building C# Rocket Welder SDK..."

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Clean previous builds
echo "Cleaning previous builds..."
dotnet clean 2>/dev/null || true

# Restore dependencies
echo "Restoring dependencies..."
dotnet restore

# Build the library
echo "Building library..."
dotnet build --configuration Release

# Build NuGet package
echo "Creating NuGet package..."
dotnet pack RocketWelder.SDK/RocketWelder.SDK.csproj --configuration Release --no-build

echo "C# build completed successfully!"
echo "Library: $SCRIPT_DIR/RocketWelder.SDK/bin/Release/net9.0/RocketWelder.SDK.dll"
echo "NuGet package: $SCRIPT_DIR/RocketWelder.SDK/bin/Release/RocketWelder.SDK.*.nupkg"