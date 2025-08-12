#!/bin/bash

# Build examples for all Rocket Welder SDK libraries

set -e

echo "========================================="
echo "Building Rocket Welder SDK Examples"
echo "========================================="

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# First ensure libraries are built
echo ""
echo "Ensuring libraries are built first..."
"$SCRIPT_DIR/build.sh"

# Build C++ examples
echo ""
echo "Building C++ examples..."
echo "-----------------------------------------"
cd "$SCRIPT_DIR/cpp"
./build_examples.sh

# Build C# examples
echo ""
echo "Building C# examples..."
echo "-----------------------------------------"
cd "$SCRIPT_DIR/csharp"
./build_examples.sh

# Build Python examples
echo ""
echo "Building Python examples..."
echo "-----------------------------------------"
cd "$SCRIPT_DIR/python"
./build_examples.sh

echo ""
echo "========================================="
echo "Examples built successfully!"
echo "========================================="
echo ""
echo "Examples location:"
echo "  - C++:    cpp/build/examples/"
echo "  - C#:     csharp/examples/SimpleClient/bin/"
echo "  - Python: python/examples/"
echo ""
echo "To run examples:"
echo "  C++:    cpp/build/examples/simple_client"
echo "  C#:     dotnet run --project csharp/examples/SimpleClient"
echo "  Python: python python/examples/simple_client.py"
echo ""