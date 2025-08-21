#!/bin/bash

# Publish script for C# Rocket Welder SDK
# Publishes to NuGet.org

set -e

echo "Publishing C# Rocket Welder SDK to NuGet..."

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Check for NuGet API key
if [ -z "$NUGET_API_KEY" ]; then
    echo "Error: NUGET_API_KEY environment variable not set"
    echo "Get your API key from https://www.nuget.org/account/apikeys"
    exit 1
fi

# Check for NuGet source (optional, defaults to nuget.org)
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

# Build release version first
echo "Building release version..."
./build.sh

# Find the latest package
PACKAGE=$(ls -t RocketWelder.SDK/bin/Release/*.nupkg 2>/dev/null | head -n1)

if [ -z "$PACKAGE" ]; then
    echo "Error: No NuGet package found in RocketWelder.SDK/bin/Release/"
    exit 1
fi

echo "Publishing package: $PACKAGE"

# Push to NuGet
dotnet nuget push "$PACKAGE" \
    --api-key "$NUGET_API_KEY" \
    --source "$NUGET_SOURCE" \
    --skip-duplicate

echo ""
echo "C# library published to NuGet!"
echo "Package: $(basename $PACKAGE)"
echo ""
echo "To use in your project:"
echo "  dotnet add package RocketWelder.SDK"