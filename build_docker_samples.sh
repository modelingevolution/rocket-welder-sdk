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
    "05-all:all:true"
    "06-yolo:yolo:true"
    "07-simple-with-data:simple-with-data:false"
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

# Build C# sample client image
if [ "$BUILD_CSHARP" = true ] && [ -z "$EXAMPLE_FILTER" ]; then
    print_section "Building C# Sample Client Docker Image"

    if [ "$USE_PLATFORM_TAG" = true ]; then
        CSHARP_IMAGE_TAG="${TAG_PREFIX}-client-csharp-${PLATFORM}:${TAG_VERSION}"
    else
        CSHARP_IMAGE_TAG="${TAG_PREFIX}-client-csharp:${TAG_VERSION}"
    fi

    print_info "Building image: ${CSHARP_IMAGE_TAG}"
    cd "${SCRIPT_DIR}/csharp"

    docker build ${DOCKER_BUILD_ARGS} \
        -t "${CSHARP_IMAGE_TAG}" \
        -f examples/SimpleClient/Dockerfile \
        .

    if [ $? -eq 0 ]; then
        print_success "C# Docker image built successfully: ${CSHARP_IMAGE_TAG}"
    else
        print_error "Failed to build C# Docker image"
        exit 1
    fi
fi

# Build Python sample client images
if [ "$BUILD_PYTHON" = true ]; then
    cd "${SCRIPT_DIR}/python"

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

        # Build standard Dockerfile
        if [ -f "examples/$folder/Dockerfile" ]; then
            if [ "$USE_PLATFORM_TAG" = true ]; then
                IMAGE_TAG="${TAG_PREFIX}-client-python-${name}-${PLATFORM}:${TAG_VERSION}"
            else
                IMAGE_TAG="${TAG_PREFIX}-client-python-${name}:${TAG_VERSION}"
            fi

            print_info "Building: ${IMAGE_TAG}"
            docker build ${DOCKER_BUILD_ARGS} \
                -t "${IMAGE_TAG}" \
                -f "examples/$folder/Dockerfile" \
                .

            if [ $? -eq 0 ]; then
                print_success "Built: ${IMAGE_TAG}"
            else
                print_error "Failed to build: ${IMAGE_TAG}"
                exit 1
            fi
        fi

        # Build Jetson variant (if enabled and GPU example)
        if [ "$BUILD_JETSON" = true ] && [ "$needs_gpu" = "true" ] && [ -f "examples/$folder/Dockerfile.jetson" ]; then
            JETSON_IMAGE_TAG="${TAG_PREFIX}-client-python-${name}:jetson"

            print_info "Building Jetson variant: ${JETSON_IMAGE_TAG}"
            docker build ${DOCKER_BUILD_ARGS} \
                -t "${JETSON_IMAGE_TAG}" \
                -f "examples/$folder/Dockerfile.jetson" \
                .

            if [ $? -eq 0 ]; then
                print_success "Built: ${JETSON_IMAGE_TAG}"
            else
                print_error "Failed to build: ${JETSON_IMAGE_TAG}"
                exit 1
            fi
        fi

        # Build Python 3.8 variant (if enabled)
        if [ "$BUILD_PYTHON38" = true ] && [ -f "examples/$folder/Dockerfile.python38" ]; then
            PYTHON38_IMAGE_TAG="${TAG_PREFIX}-client-python-${name}:python38"

            print_info "Building Python 3.8 variant: ${PYTHON38_IMAGE_TAG}"
            docker build ${DOCKER_BUILD_ARGS} \
                -t "${PYTHON38_IMAGE_TAG}" \
                -f "examples/$folder/Dockerfile.python38" \
                .

            if [ $? -eq 0 ]; then
                print_success "Built: ${PYTHON38_IMAGE_TAG}"
            else
                print_error "Failed to build: ${PYTHON38_IMAGE_TAG}"
                exit 1
            fi
        fi
    done
fi

print_section "Build Complete!"

print_info "To list built images:"
echo "  docker images | grep ${TAG_PREFIX}"
echo ""
print_info "To run a container:"
echo "  docker run --rm -it \\"
echo "    -e CONNECTION_STRING=\"shm://test_buffer\" \\"
echo "    --ipc=host \\"
echo "    ${TAG_PREFIX}-client-python-simple:${TAG_VERSION}"
