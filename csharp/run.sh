#!/bin/bash

# Run script for C# Rocket Welder SDK example
# Usage: ./run.sh <connection_string> [--exit-after=N]

set -e

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Check if example is published
if [ ! -f "$SCRIPT_DIR/examples/SimpleClient/publish/SimpleClient" ]; then
    echo "Building example first..."
    "$SCRIPT_DIR/build_examples.sh"
fi

# Convert parameters from --key=value to --key value format for .NET
# Also map first positional argument to CONNECTION_STRING
ARGS=()
FIRST_ARG=true

for arg in "$@"; do
    if [[ "$arg" == --*=* ]]; then
        # Split --key=value into --key value
        key="${arg%%=*}"
        value="${arg#*=}"
        ARGS+=("$key" "$value")
    elif $FIRST_ARG && [[ "$arg" != --* ]]; then
        # First positional argument becomes CONNECTION_STRING
        export CONNECTION_STRING="$arg"
        FIRST_ARG=false
    else
        ARGS+=("$arg")
        FIRST_ARG=false
    fi
done

# Run the example with converted arguments
exec "$SCRIPT_DIR/examples/SimpleClient/publish/SimpleClient" "${ARGS[@]}"