#!/bin/bash

# Run script for C# Rocket Welder SDK example
# Usage: ./run.sh <connection_string> [--exit-after=N]

set -e

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Check if example is built
if [ ! -f "$SCRIPT_DIR/examples/SimpleClient/bin/Release/net9.0/SimpleClient.dll" ]; then
    echo "Building example first..."
    "$SCRIPT_DIR/build_examples.sh"
fi

# Run the example with passed arguments
cd "$SCRIPT_DIR/examples/SimpleClient"
exec dotnet run --configuration Release -- "$@"