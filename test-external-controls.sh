#!/bin/bash

# Cross-platform serialization test script for External Controls contracts
# This script verifies that C# and Python produce compatible JSON serialization

set -e  # Exit on error

echo "=== External Controls Cross-Platform Serialization Test ==="
echo

# Navigate to SDK root directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Clean previous test outputs
echo "1. Cleaning previous test outputs..."
rm -rf csharp/RocketWelder.SDK.Tests/bin/Debug/net9.0/test_output
rm -rf python/test_output
echo "   Previous test outputs removed."
echo

# Run C# tests
echo "2. Running C# serialization tests..."
cd csharp
dotnet test RocketWelder.SDK.Tests/RocketWelder.SDK.Tests.csproj --filter "FullyQualifiedName~ExternalControlsSerializationTests"
cd ..
echo "   C# tests completed."
echo

# Run Python tests
echo "3. Running Python serialization tests..."
cd python

# Check if virtual environment exists, create if not
if [ ! -d "venv" ]; then
    echo "   Creating Python virtual environment..."
    python3 -m venv venv
fi

# Activate virtual environment
source venv/bin/activate

# Install dependencies if pytest is not available
if ! python -m pytest --version &> /dev/null; then
    echo "   Installing Python dependencies..."
    pip install pytest
fi

# Run tests (without coverage to avoid failing on coverage requirements)
python -m pytest tests/test_external_controls_serialization_v2.py -v --no-cov

# Deactivate virtual environment
deactivate

cd ..
echo "   Python tests completed."
echo

# Compare JSON outputs
echo "4. Comparing JSON outputs..."
echo

CSHARP_OUTPUT="csharp/RocketWelder.SDK.Tests/bin/Debug/net9.0/test_output"
PYTHON_OUTPUT="python/test_output"

# Check if output directories exist
if [ ! -d "$CSHARP_OUTPUT" ]; then
    echo "ERROR: C# test output directory not found: $CSHARP_OUTPUT"
    exit 1
fi

if [ ! -d "$PYTHON_OUTPUT" ]; then
    echo "ERROR: Python test output directory not found: $PYTHON_OUTPUT"
    exit 1
fi

# List of event types to compare
# Note: ArrowDown/ArrowUp are internal SDK events, not external contracts
EVENT_TYPES=("DefineControl" "DeleteControls" "ChangeControls" "ButtonDown" "ButtonUp")

FAILED=0

for EVENT_TYPE in "${EVENT_TYPES[@]}"; do
    CSHARP_FILE="$CSHARP_OUTPUT/${EVENT_TYPE}_csharp.json"
    PYTHON_FILE="$PYTHON_OUTPUT/${EVENT_TYPE}_python.json"
    
    echo "   Comparing $EVENT_TYPE..."
    
    if [ ! -f "$CSHARP_FILE" ]; then
        echo "   ❌ C# file missing: $CSHARP_FILE"
        FAILED=1
        continue
    fi
    
    if [ ! -f "$PYTHON_FILE" ]; then
        echo "   ❌ Python file missing: $PYTHON_FILE"
        FAILED=1
        continue
    fi
    
    # Compare JSON structures (normalized with jq if available, otherwise use diff)
    if command -v jq &> /dev/null; then
        # Use jq to normalize and compare JSON
        if jq -S . "$CSHARP_FILE" | diff -q - <(jq -S . "$PYTHON_FILE") > /dev/null; then
            echo "   ✓ $EVENT_TYPE: JSON structures match"
        else
            echo "   ❌ $EVENT_TYPE: JSON structures differ"
            echo "      C# JSON:"
            jq . "$CSHARP_FILE" | head -5
            echo "      Python JSON:"
            jq . "$PYTHON_FILE" | head -5
            FAILED=1
        fi
    else
        # Fallback to simple diff if jq is not available
        if diff -q "$CSHARP_FILE" "$PYTHON_FILE" > /dev/null; then
            echo "   ✓ $EVENT_TYPE: Files match"
        else
            echo "   ❌ $EVENT_TYPE: Files differ"
            echo "      Differences:"
            diff "$CSHARP_FILE" "$PYTHON_FILE" | head -10
            FAILED=1
        fi
    fi
done

echo
echo "=== Test Summary ==="
if [ $FAILED -eq 0 ]; then
    echo "✅ All serialization tests passed! C# and Python produce compatible JSON."
    exit 0
else
    echo "❌ Some tests failed. C# and Python JSON outputs are not compatible."
    exit 1
fi