#!/bin/bash

# Publish script for C++ Rocket Welder SDK
# Publishes to vcpkg repository

set -e

echo "Publishing C++ Rocket Welder SDK..."

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Check for vcpkg repository
if [ -z "$VCPKG_REPO" ]; then
    echo "Error: VCPKG_REPO environment variable not set"
    echo "Please set VCPKG_REPO to your vcpkg registry path"
    exit 1
fi

# Build release version first
echo "Building release version..."
./build.sh

# Create vcpkg port files
echo "Creating vcpkg port..."
PORT_DIR="$VCPKG_REPO/ports/rocket-welder-sdk"
mkdir -p "$PORT_DIR"

# Create portfile.cmake
cat > "$PORT_DIR/portfile.cmake" << 'EOF'
vcpkg_from_github(
    OUT_SOURCE_PATH SOURCE_PATH
    REPO modelingevolution/rocket-welder-sdk
    REF v1.0.0
    SHA512 0
    HEAD_REF main
)

vcpkg_cmake_configure(
    SOURCE_PATH "${SOURCE_PATH}/cpp"
    OPTIONS
        -DBUILD_EXAMPLES=OFF
)

vcpkg_cmake_install()
vcpkg_cmake_config_fixup()

file(REMOVE_RECURSE "${CURRENT_PACKAGES_DIR}/debug/include")

vcpkg_install_copyright(FILE_LIST "${SOURCE_PATH}/LICENSE")
EOF

# Create vcpkg.json
cat > "$PORT_DIR/vcpkg.json" << 'EOF'
{
  "name": "rocket-welder-sdk",
  "version": "1.0.0",
  "description": "Client library for RocketWelder video streaming services",
  "homepage": "https://github.com/modelingevolution/rocket-welder-sdk",
  "license": "MIT",
  "dependencies": [
    {
      "name": "opencv4",
      "features": ["core", "imgproc"]
    }
  ]
}
EOF

echo "C++ library published to vcpkg!"
echo "Port location: $PORT_DIR"
echo ""
echo "To use in your project:"
echo "  vcpkg install rocket-welder-sdk"