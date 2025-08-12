#!/bin/bash

# Publish script for Python Rocket Welder SDK
# Publishes to PyPI

set -e

echo "Publishing Python Rocket Welder SDK to PyPI..."

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Check for PyPI token
if [ -z "$PYPI_API_TOKEN" ]; then
    echo "Error: PYPI_API_TOKEN environment variable not set"
    echo "Get your API token from https://pypi.org/manage/account/token/"
    exit 1
fi

# Build the package first
echo "Building package..."
./build.sh

# Activate virtual environment
source venv/bin/activate

# Install/upgrade twine
echo "Installing twine..."
pip install --upgrade twine

# Find the latest wheel and source distribution
WHEEL=$(ls -t dist/*.whl 2>/dev/null | head -n1)
SDIST=$(ls -t dist/*.tar.gz 2>/dev/null | head -n1)

if [ -z "$WHEEL" ]; then
    echo "Error: No wheel package found in dist/"
    exit 1
fi

echo "Publishing packages:"
echo "  Wheel: $(basename $WHEEL)"
[ ! -z "$SDIST" ] && echo "  Source: $(basename $SDIST)"

# Upload to PyPI using token
twine upload \
    --username __token__ \
    --password "$PYPI_API_TOKEN" \
    --skip-existing \
    dist/*

echo ""
echo "Python library published to PyPI!"
echo ""
echo "To use in your project:"
echo "  pip install rocket-welder-sdk"