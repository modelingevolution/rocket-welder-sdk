#!/bin/bash

# Build Docker images for sample clients
# Supports C# and Python sample clients with multiple variants

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Detect platform
PLATFORM=""
ARCH=$(uname -m)
case "$ARCH" in
    x86_64)
        PLATFORM="amd64"
        ;;
    aarch64|arm64)
        PLATFORM="arm64"
        ;;
    *)
        PLATFORM="$ARCH"
        ;;
esac

# Default values
BUILD_CSHARP=true
BUILD_PYTHON=true
TAG_PREFIX="rocket-welder"
TAG_VERSION="latest"
NO_CACHE=false
USE_PLATFORM_TAG=false
BUILD_JETSON=false
BUILD_PYTHON38=false
EXAMPLE_FILTER=""

# Auto-detect Jetson platform
if [ "$PLATFORM" = "arm64" ] && [ -f /etc/nv_tegra_release ]; then
    BUILD_JETSON=true
fi

# Python examples definition: folder:name:needs_gpu
PYTHON_EXAMPLES=(
    "01-simple:simple:false"
    "02-advanced:advanced:false"
    "03-integration:integration:false"
    "04-ui-controls:ui-controls:false"
    "05-ball-detector:ball-detector:true"
    "06-yolo:yolo:true"
    "07-simple-with-data:simple-with-data:false"
)

# C# examples definition: folder:name
CSHARP_EXAMPLES=(
    "SimpleClient:simple"
    "BallDetection:ball-detection"
)

print_info() { echo -e "${CYAN}$1${NC}"; }
print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠ $1${NC}"; }

print_section() {
    echo ""
    echo -e "${BLUE}=========================================${NC}"
    echo -e "${BLUE}  $1${NC}"
    echo -e "${BLUE}=========================================${NC}"
    echo ""
}

# Extract SDK versions
get_csharp_sdk_version() {
    local csproj_file="$1"
    if [ -f "$csproj_file" ]; then
        grep -oP 'PackageReference Include="RocketWelder\.SDK" Version="\K[^"]+' "$csproj_file" 2>/dev/null || echo "unknown"
    else
        echo "unknown"
    fi
}

get_python_sdk_version() {
    local init_file="${SCRIPT_DIR}/python/rocket_welder_sdk/__init__.py"
    if [ -f "$init_file" ]; then
        grep -oP '__version__\s*=\s*"\K[^"]+' "$init_file" 2>/dev/null || echo "unknown"
    else
        echo "unknown"
    fi
}

# Store built images for summary
declare -a BUILT_IMAGES=()
declare -a BUILT_SDK_VERSIONS=()

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --csharp-only)
            BUILD_PYTHON=false
            shift
            ;;
        --python-only)
            BUILD_CSHARP=false
            shift
            ;;
        --tag-prefix)
            TAG_PREFIX="$2"
            shift 2
            ;;
        --tag-version)
            TAG_VERSION="$2"
            shift 2
            ;;
        --no-cache)
            NO_CACHE=true
            shift
            ;;
        --platform-tag)
            USE_PLATFORM_TAG=true
            shift
            ;;
        --jetson)
            BUILD_JETSON=true
            shift
            ;;
        --no-jetson)
            BUILD_JETSON=false
            shift
            ;;
        --python38)
            BUILD_PYTHON38=true
            shift
            ;;
        --example)
            EXAMPLE_FILTER="$2"
            shift 2
            ;;
        --help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Build Docker images for RocketWelder SDK sample clients"
            echo ""
            echo "Options:"
            echo "  --csharp-only       Build only the C# sample client image"
            echo "  --python-only       Build only the Python sample client images"
            echo "  --tag-prefix PREFIX Docker image tag prefix (default: rocket-welder)"
            echo "  --tag-version VER   Docker image tag version (default: latest)"
            echo "  --no-cache          Build without using Docker cache"
            echo "  --platform-tag      Add platform suffix to image names"
            echo "  --jetson            Build Jetson-optimized images"
            echo "  --no-jetson         Skip building Jetson-optimized images"
            echo "  --python38          Also build Python 3.8 images"
            echo "  --example NAME      Build only specific example (e.g., 01-simple, yolo)"
            echo "  --help              Show this help message"
            echo ""
            echo "C# examples:"
            for example in "${CSHARP_EXAMPLES[@]}"; do
                IFS=':' read -r folder name <<< "$example"
                echo "  - $folder ($name)"
            done
            echo ""
            echo "Python examples:"
            for example in "${PYTHON_EXAMPLES[@]}"; do
                IFS=':' read -r folder name needs_gpu <<< "$example"
                gpu_note=""
                if [ "$needs_gpu" = "true" ]; then
                    gpu_note=" (GPU required)"
                fi
                echo "  - $folder ($name)$gpu_note"
            done
            echo ""
            echo "Examples:"
            echo "  $0                                    # Build all images"
            echo "  $0 --python-only                      # Build only Python images"
            echo "  $0 --example 01-simple                # Build only simple example"
            echo "  $0 --example yolo --jetson            # Build YOLO with Jetson variant"
            echo "  $0 --python38                         # Include Python 3.8 variants"
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Prepare Docker build arguments
DOCKER_BUILD_ARGS=""
if [ "$NO_CACHE" = true ]; then
    DOCKER_BUILD_ARGS="--no-cache"
fi

print_section "RocketWelder SDK Docker Image Builder"

print_info "Configuration:"
echo "  Current platform: ${PLATFORM}"
echo "  Tag prefix: ${TAG_PREFIX}"
echo "  Tag version: ${TAG_VERSION}"
echo "  Build C# sample: ${BUILD_CSHARP}"
echo "  Build Python samples: ${BUILD_PYTHON}"
echo "  Build Jetson images: ${BUILD_JETSON}"
echo "  Build Python 3.8: ${BUILD_PYTHON38}"
echo "  No cache: ${NO_CACHE}"
if [ -n "$EXAMPLE_FILTER" ]; then
    echo "  Example filter: ${EXAMPLE_FILTER}"
fi

# Pre-build SDK version check
echo ""
print_info "SDK Versions:"

HAS_DISCREPANCY=false

# Check C# SDK versions
if [ "$BUILD_CSHARP" = true ]; then
    declare -A CSHARP_VERSIONS_CHECK
    for example in "${CSHARP_EXAMPLES[@]}"; do
        IFS=':' read -r folder name <<< "$example"
        if [ -n "$EXAMPLE_FILTER" ]; then
            if [[ "$folder" != *"$EXAMPLE_FILTER"* ]] && [[ "$name" != *"$EXAMPLE_FILTER"* ]]; then
                continue
            fi
        fi
        csproj_file="${SCRIPT_DIR}/csharp/examples/$folder/$folder.csproj"
        if [ -f "$csproj_file" ]; then
            ver=$(get_csharp_sdk_version "$csproj_file")
            CSHARP_VERSIONS_CHECK["$ver"]+="$folder "
            echo "  C# $folder: NuGet ${ver}"
        fi
    done

    if [ ${#CSHARP_VERSIONS_CHECK[@]} -gt 1 ]; then
        HAS_DISCREPANCY=true
        echo ""
        print_warning "C# NuGet SDK version discrepancy detected!"
        for ver in "${!CSHARP_VERSIONS_CHECK[@]}"; do
            echo -e "  ${YELLOW}Version ${ver}:${NC} ${CSHARP_VERSIONS_CHECK[$ver]}"
        done
    fi
fi

# Check Python SDK version
if [ "$BUILD_PYTHON" = true ]; then
    py_ver=$(get_python_sdk_version)
    echo "  Python (all examples): PyPI ${py_ver}"
fi

if [ "$HAS_DISCREPANCY" = true ]; then
    echo ""
    print_warning "Version discrepancies found. Continue anyway? (y/N)"
    read -r response
    if [[ ! "$response" =~ ^[Yy]$ ]]; then
        print_error "Build aborted. Please align SDK versions first."
        exit 1
    fi
fi

# Build C# sample client images
if [ "$BUILD_CSHARP" = true ]; then
    cd "${SCRIPT_DIR}/csharp"

    for example in "${CSHARP_EXAMPLES[@]}"; do
        IFS=':' read -r folder name <<< "$example"

        # Skip if filter is set and doesn't match
        if [ -n "$EXAMPLE_FILTER" ]; then
            if [[ "$folder" != *"$EXAMPLE_FILTER"* ]] && [[ "$name" != *"$EXAMPLE_FILTER"* ]]; then
                continue
            fi
        fi

        # Check if example folder exists
        if [ ! -d "examples/$folder" ]; then
            print_warning "C# example folder not found: examples/$folder - skipping"
            continue
        fi

        # Check if Dockerfile exists
        if [ ! -f "examples/$folder/Dockerfile" ]; then
            print_warning "No Dockerfile found in examples/$folder - skipping"
            continue
        fi

        # Get SDK version from csproj
        SDK_VERSION=$(get_csharp_sdk_version "examples/$folder/$folder.csproj")

        print_section "Building C# Example: $folder ($name)"
        print_info "RocketWelder.SDK NuGet version: ${SDK_VERSION}"

        if [ "$USE_PLATFORM_TAG" = true ]; then
            CSHARP_IMAGE_TAG="${TAG_PREFIX}-client-csharp-${name}-${PLATFORM}:${TAG_VERSION}"
        else
            CSHARP_IMAGE_TAG="${TAG_PREFIX}-client-csharp-${name}:${TAG_VERSION}"
        fi

        print_info "Building: ${CSHARP_IMAGE_TAG}"
        if docker build ${DOCKER_BUILD_ARGS} \
            -t "${CSHARP_IMAGE_TAG}" \
            -f "examples/$folder/Dockerfile" \
            .; then
            print_success "Built: ${CSHARP_IMAGE_TAG}"
            BUILT_IMAGES+=("${CSHARP_IMAGE_TAG}")
            BUILT_SDK_VERSIONS+=("NuGet: ${SDK_VERSION}")
        else
            print_error "Failed to build: ${CSHARP_IMAGE_TAG}"
            exit 1
        fi
    done
fi

# Build Python sample client images
if [ "$BUILD_PYTHON" = true ]; then
    cd "${SCRIPT_DIR}/python"

    # Get Python SDK version once (same for all Python images)
    PYTHON_SDK_VERSION=$(get_python_sdk_version)

    for example in "${PYTHON_EXAMPLES[@]}"; do
        IFS=':' read -r folder name needs_gpu <<< "$example"

        # Skip if filter is set and doesn't match
        if [ -n "$EXAMPLE_FILTER" ]; then
            if [[ "$folder" != *"$EXAMPLE_FILTER"* ]] && [[ "$name" != *"$EXAMPLE_FILTER"* ]]; then
                continue
            fi
        fi

        # Check if example folder exists
        if [ ! -d "examples/$folder" ]; then
            print_warning "Example folder not found: examples/$folder - skipping"
            continue
        fi

        print_section "Building Python Example: $folder ($name)"
        print_info "rocket-welder-sdk PyPI version: ${PYTHON_SDK_VERSION}"

        # Build standard Dockerfile
        if [ -f "examples/$folder/Dockerfile" ]; then
            if [ "$USE_PLATFORM_TAG" = true ]; then
                IMAGE_TAG="${TAG_PREFIX}-client-python-${name}-${PLATFORM}:${TAG_VERSION}"
            else
                IMAGE_TAG="${TAG_PREFIX}-client-python-${name}:${TAG_VERSION}"
            fi

            print_info "Building: ${IMAGE_TAG}"
            if docker build ${DOCKER_BUILD_ARGS} \
                -t "${IMAGE_TAG}" \
                -f "examples/$folder/Dockerfile" \
                .; then
                print_success "Built: ${IMAGE_TAG}"
                BUILT_IMAGES+=("${IMAGE_TAG}")
                BUILT_SDK_VERSIONS+=("PyPI: ${PYTHON_SDK_VERSION}")
            else
                print_error "Failed to build: ${IMAGE_TAG}"
                exit 1
            fi
        fi

        # Build Jetson variant (if enabled and GPU example)
        if [ "$BUILD_JETSON" = true ] && [ "$needs_gpu" = "true" ] && [ -f "examples/$folder/Dockerfile.jetson" ]; then
            JETSON_IMAGE_TAG="${TAG_PREFIX}-client-python-${name}:jetson"

            print_info "Building Jetson variant: ${JETSON_IMAGE_TAG}"
            if docker build ${DOCKER_BUILD_ARGS} \
                -t "${JETSON_IMAGE_TAG}" \
                -f "examples/$folder/Dockerfile.jetson" \
                .; then
                print_success "Built: ${JETSON_IMAGE_TAG}"
                BUILT_IMAGES+=("${JETSON_IMAGE_TAG}")
                BUILT_SDK_VERSIONS+=("PyPI: ${PYTHON_SDK_VERSION}")
            else
                print_error "Failed to build: ${JETSON_IMAGE_TAG}"
                exit 1
            fi
        fi

        # Build Python 3.8 variant (if enabled)
        if [ "$BUILD_PYTHON38" = true ] && [ -f "examples/$folder/Dockerfile.python38" ]; then
            PYTHON38_IMAGE_TAG="${TAG_PREFIX}-client-python-${name}:python38"

            print_info "Building Python 3.8 variant: ${PYTHON38_IMAGE_TAG}"
            if docker build ${DOCKER_BUILD_ARGS} \
                -t "${PYTHON38_IMAGE_TAG}" \
                -f "examples/$folder/Dockerfile.python38" \
                .; then
                print_success "Built: ${PYTHON38_IMAGE_TAG}"
                BUILT_IMAGES+=("${PYTHON38_IMAGE_TAG}")
                BUILT_SDK_VERSIONS+=("PyPI: ${PYTHON_SDK_VERSION}")
            else
                print_error "Failed to build: ${PYTHON38_IMAGE_TAG}"
                exit 1
            fi
        fi
    done
fi

print_section "Build Complete!"

# Display summary of built images with SDK versions
if [ ${#BUILT_IMAGES[@]} -gt 0 ]; then
    print_info "Built images with SDK versions:"
    echo ""
    for i in "${!BUILT_IMAGES[@]}"; do
        echo -e "  ${GREEN}✓${NC} ${BUILT_IMAGES[$i]}"
        echo -e "    └─ SDK: ${BUILT_SDK_VERSIONS[$i]}"
    done
    echo ""

    # Check for version discrepancies
    declare -A NUGET_VERSIONS
    declare -A PYPI_VERSIONS

    for i in "${!BUILT_SDK_VERSIONS[@]}"; do
        version="${BUILT_SDK_VERSIONS[$i]}"
        image="${BUILT_IMAGES[$i]}"
        if [[ "$version" == NuGet:* ]]; then
            ver="${version#NuGet: }"
            NUGET_VERSIONS["$ver"]+="$image "
        elif [[ "$version" == PyPI:* ]]; then
            ver="${version#PyPI: }"
            PYPI_VERSIONS["$ver"]+="$image "
        fi
    done

    # Warn about NuGet version discrepancies
    if [ ${#NUGET_VERSIONS[@]} -gt 1 ]; then
        print_warning "NuGet SDK version discrepancy detected!"
        for ver in "${!NUGET_VERSIONS[@]}"; do
            echo -e "  ${YELLOW}Version ${ver}:${NC}"
            for img in ${NUGET_VERSIONS[$ver]}; do
                echo "    - $img"
            done
        done
        echo ""
    fi

    # Warn about PyPI version discrepancies
    if [ ${#PYPI_VERSIONS[@]} -gt 1 ]; then
        print_warning "PyPI SDK version discrepancy detected!"
        for ver in "${!PYPI_VERSIONS[@]}"; do
            echo -e "  ${YELLOW}Version ${ver}:${NC}"
            for img in ${PYPI_VERSIONS[$ver]}; do
                echo "    - $img"
            done
        done
        echo ""
    fi
fi

print_info "To list built images:"
echo "  docker images | grep ${TAG_PREFIX}"
echo ""
print_info "To run a container:"
echo "  docker run --rm -it \\"
echo "    -e CONNECTION_STRING=\"shm://test_buffer\" \\"
echo "    --ipc=host \\"
echo "    ${TAG_PREFIX}-client-python-simple:${TAG_VERSION}"
