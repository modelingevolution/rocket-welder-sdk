#!/bin/bash

# Main publish script for Rocket Welder SDK
# Publishes all three libraries to their respective package repositories

set -e

echo "========================================="
echo "Publishing Rocket Welder SDK"
echo "========================================="

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Check for required environment variables
if [ -z "$VCPKG_REPO" ] && [ -z "$NUGET_API_KEY" ] && [ -z "$PYPI_API_TOKEN" ]; then
    echo "Warning: No publishing credentials found."
    echo "Set one or more of: VCPKG_REPO, NUGET_API_KEY, PYPI_API_TOKEN"
    echo ""
fi

# Publish C++ library
if [ ! -z "$VCPKG_REPO" ]; then
    echo ""
    echo "Publishing C++ library to vcpkg..."
    echo "-----------------------------------------"
    cd "$SCRIPT_DIR/cpp"
    ./publish.sh
else
    echo ""
    echo "Skipping C++ publish (VCPKG_REPO not set)"
fi

# Publish C# library
if [ ! -z "$NUGET_API_KEY" ]; then
    echo ""
    echo "Publishing C# library to NuGet..."
    echo "-----------------------------------------"
    cd "$SCRIPT_DIR/csharp"
    ./publish.sh
else
    echo ""
    echo "Skipping C# publish (NUGET_API_KEY not set)"
fi

# Publish Python library
if [ ! -z "$PYPI_API_TOKEN" ]; then
    echo ""
    echo "Publishing Python library to PyPI..."
    echo "-----------------------------------------"
    cd "$SCRIPT_DIR/python"
    ./publish.sh
else
    echo ""
    echo "Skipping Python publish (PYPI_API_TOKEN not set)"
fi

echo ""
echo "========================================="
echo "Publishing completed!"
echo "========================================="