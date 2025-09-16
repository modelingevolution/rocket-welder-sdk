#!/bin/bash

# Build Docker images for sample clients
# Supports both C# and Python sample clients

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
CSHARP_SAMPLE_DIR="${SCRIPT_DIR}/csharp/examples/SimpleClient"
PYTHON_SAMPLE_DIR="${SCRIPT_DIR}/python/examples"

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
MULTI_PLATFORM=false
PLATFORMS="linux/amd64,linux/arm64"
PUSH_TO_REGISTRY=false

# Function to print colored output
print_info() {
    echo -e "${CYAN}$1${NC}"
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠ $1${NC}"
}

# Function to print section headers
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
        --multi-platform)
            MULTI_PLATFORM=true
            shift
            ;;
        --platforms)
            PLATFORMS="$2"
            shift 2
            ;;
        --push)
            PUSH_TO_REGISTRY=true
            shift
            ;;
        --help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Build Docker images for RocketWelder SDK sample clients"
            echo ""
            echo "Options:"
            echo "  --csharp-only       Build only the C# sample client image"
            echo "  --python-only       Build only the Python sample client image"
            echo "  --tag-prefix PREFIX Docker image tag prefix (default: rocket-welder)"
            echo "  --tag-version VER   Docker image tag version (default: latest)"
            echo "  --no-cache          Build without using Docker cache"
            echo "  --platform-tag      Add platform suffix to image names"
            echo "  --multi-platform    Build multi-platform images using buildx"
            echo "  --platforms PLATS   Platforms to build for (default: linux/amd64,linux/arm64)"
            echo "  --push              Push images to registry (required for multi-platform)"
            echo "  --help              Show this help message"
            echo ""
            echo "Examples:"
            echo "  $0                                    # Build all images"
            echo "  $0 --csharp-only                      # Build only C# image"
            echo "  $0 --tag-version 1.0.0                # Build with specific version"
            echo "  $0 --no-cache                         # Force rebuild without cache"
            echo "  $0 --multi-platform --push            # Build and push multi-platform images"
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Prepare Docker build arguments and setup buildx if needed
DOCKER_BUILD_ARGS=""
if [ "$NO_CACHE" = true ]; then
    DOCKER_BUILD_ARGS="--no-cache"
fi

# Setup buildx for multi-platform builds
if [ "$MULTI_PLATFORM" = true ]; then
    print_info "Setting up Docker buildx for multi-platform builds..."
    
    # Check if buildx is available
    if ! docker buildx version &> /dev/null; then
        print_error "Docker buildx is not available. Please install Docker Desktop or Docker CE with buildx plugin."
        exit 1
    fi
    
    # Create or use existing buildx builder
    BUILDER_NAME="rocket-welder-builder"
    if ! docker buildx ls | grep -q "$BUILDER_NAME"; then
        print_info "Creating buildx builder: $BUILDER_NAME"
        docker buildx create --name "$BUILDER_NAME" --use
    else
        print_info "Using existing buildx builder: $BUILDER_NAME"
        docker buildx use "$BUILDER_NAME"
    fi
    
    # Start the builder
    docker buildx inspect --bootstrap
    
    # Add platform flags
    DOCKER_BUILD_ARGS="$DOCKER_BUILD_ARGS --platform=$PLATFORMS"
    
    # Add push flag if requested
    if [ "$PUSH_TO_REGISTRY" = true ]; then
        DOCKER_BUILD_ARGS="$DOCKER_BUILD_ARGS --push"
    else
        print_warning "Multi-platform build without --push will only build, not load images locally"
    fi
fi

print_section "RocketWelder SDK Docker Image Builder"

print_info "Configuration:"
echo "  Current platform: ${PLATFORM}"
echo "  Tag prefix: ${TAG_PREFIX}"
echo "  Tag version: ${TAG_VERSION}"
echo "  Build C# sample: ${BUILD_CSHARP}"
echo "  Build Python sample: ${BUILD_PYTHON}"
echo "  No cache: ${NO_CACHE}"
echo "  Use platform tag: ${USE_PLATFORM_TAG}"
echo "  Multi-platform: ${MULTI_PLATFORM}"
if [ "$MULTI_PLATFORM" = true ]; then
    echo "  Target platforms: ${PLATFORMS}"
    echo "  Push to registry: ${PUSH_TO_REGISTRY}"
fi

# Build C# sample client image
if [ "$BUILD_CSHARP" = true ]; then
    print_section "Building C# Sample Client Docker Image"
    
    # Build image name based on user preference
    if [ "$USE_PLATFORM_TAG" = true ]; then
        CSHARP_IMAGE_TAG="${TAG_PREFIX}-client-csharp-${PLATFORM}:${TAG_VERSION}"
    else
        CSHARP_IMAGE_TAG="${TAG_PREFIX}-client-csharp:${TAG_VERSION}"
    fi
    
    print_info "Building image: ${CSHARP_IMAGE_TAG}"
    print_info "Context: ${SCRIPT_DIR}/csharp"
    
    # Build Docker image (context is at csharp directory level)
    print_info "Building Docker image..."
    cd "${SCRIPT_DIR}/csharp"
    
    if [ "$MULTI_PLATFORM" = true ]; then
        # Use buildx for multi-platform build
        docker buildx build ${DOCKER_BUILD_ARGS} \
            -t "${CSHARP_IMAGE_TAG}" \
            -f examples/SimpleClient/Dockerfile \
            .
    else
        # Use regular docker build for single platform
        docker build ${DOCKER_BUILD_ARGS} \
            -t "${CSHARP_IMAGE_TAG}" \
            -f examples/SimpleClient/Dockerfile \
            .
    fi
    
    if [ $? -eq 0 ]; then
        print_success "C# Docker image built successfully: ${CSHARP_IMAGE_TAG}"
        
        # Show image details (only for single platform builds)
        if [ "$MULTI_PLATFORM" = false ]; then
            echo ""
            print_info "Image details:"
            docker images --filter "reference=${CSHARP_IMAGE_TAG%:*}" --format "table {{.Repository}}:{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}"
        fi
    else
        print_error "Failed to build C# Docker image"
        exit 1
    fi
fi

# Build Python sample client image
if [ "$BUILD_PYTHON" = true ]; then
    print_section "Building Python Sample Client Docker Image"

    # Build image name based on user preference
    if [ "$USE_PLATFORM_TAG" = true ]; then
        PYTHON_IMAGE_TAG="${TAG_PREFIX}-client-python-${PLATFORM}:${TAG_VERSION}"
    else
        PYTHON_IMAGE_TAG="${TAG_PREFIX}-client-python:${TAG_VERSION}"
    fi

    print_info "Building image: ${PYTHON_IMAGE_TAG}"
    print_info "Context: ${SCRIPT_DIR}/python"

    # Build Docker image (context is at python directory level)
    print_info "Building Docker image..."
    cd "${SCRIPT_DIR}/python"

    if [ "$MULTI_PLATFORM" = true ]; then
        # Use buildx for multi-platform build
        docker buildx build ${DOCKER_BUILD_ARGS} \
            -t "${PYTHON_IMAGE_TAG}" \
            -f examples/Dockerfile \
            .
    else
        # Use regular docker build for single platform
        docker build ${DOCKER_BUILD_ARGS} \
            -t "${PYTHON_IMAGE_TAG}" \
            -f examples/Dockerfile \
            .
    fi

    if [ $? -eq 0 ]; then
        print_success "Python Docker image built successfully: ${PYTHON_IMAGE_TAG}"

        # Show image details (only for single platform builds)
        if [ "$MULTI_PLATFORM" = false ]; then
            echo ""
            print_info "Image details:"
            docker images --filter "reference=${PYTHON_IMAGE_TAG%:*}" --format "table {{.Repository}}:{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}"
        fi
    else
        print_error "Failed to build Python Docker image"
        exit 1
    fi

    # Build Python 3.8 legacy image
    print_section "Building Python 3.8 Sample Client Docker Image"

    # Build image name for Python 3.8
    if [ "$USE_PLATFORM_TAG" = true ]; then
        PYTHON38_IMAGE_TAG="${TAG_PREFIX}-client-python-${PLATFORM}:python38"
    else
        PYTHON38_IMAGE_TAG="${TAG_PREFIX}-client-python:python38"
    fi

    print_info "Building image: ${PYTHON38_IMAGE_TAG}"
    print_info "Context: ${SCRIPT_DIR}/python"

    # Build Docker image for Python 3.8
    print_info "Building Python 3.8 Docker image..."
    cd "${SCRIPT_DIR}/python"

    if [ "$MULTI_PLATFORM" = true ]; then
        # Use buildx for multi-platform build
        docker buildx build ${DOCKER_BUILD_ARGS} \
            -t "${PYTHON38_IMAGE_TAG}" \
            -f examples/Dockerfile-python38 \
            .
    else
        # Use regular docker build for single platform
        docker build ${DOCKER_BUILD_ARGS} \
            -t "${PYTHON38_IMAGE_TAG}" \
            -f examples/Dockerfile-python38 \
            .
    fi

    if [ $? -eq 0 ]; then
        print_success "Python 3.8 Docker image built successfully: ${PYTHON38_IMAGE_TAG}"

        # Show image details (only for single platform builds)
        if [ "$MULTI_PLATFORM" = false ]; then
            echo ""
            print_info "Image details:"
            docker images --filter "reference=${TAG_PREFIX}-client-python" --format "table {{.Repository}}:{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}" | grep python38
        fi
    else
        print_error "Failed to build Python 3.8 Docker image"
        exit 1
    fi
fi

print_section "Build Complete!"

print_info "Built images:"
if [ "$BUILD_CSHARP" = true ]; then
    echo "  • ${TAG_PREFIX}-client-csharp:${TAG_VERSION}"
fi
if [ "$BUILD_PYTHON" = true ]; then
    echo "  • ${TAG_PREFIX}-client-python:${TAG_VERSION}"
    echo "  • ${TAG_PREFIX}-client-python:python38"
fi

echo ""
print_info "To run the containers:"
echo ""

if [ "$BUILD_CSHARP" = true ]; then
    echo "C# client:"
    echo "  docker run --rm -it \\"
    echo "    -e CONNECTION_STRING=\"shm://test_buffer?size=10MB&metadata=4KB\" \\"
    echo "    --ipc=host \\"
    echo "    ${TAG_PREFIX}-client-csharp:${TAG_VERSION}"
    echo ""
fi

if [ "$BUILD_PYTHON" = true ]; then
    echo "Python client (latest):"
    echo "  docker run --rm -it \\"
    echo "    -e CONNECTION_STRING=\"shm://test_buffer?size=10MB&metadata=4KB\" \\"
    echo "    --ipc=host \\"
    echo "    ${TAG_PREFIX}-client-python:${TAG_VERSION}"
    echo ""
    echo "Python client (Python 3.8):"
    echo "  docker run --rm -it \\"
    echo "    -e CONNECTION_STRING=\"shm://test_buffer?size=10MB&metadata=4KB\" \\"
    echo "    --ipc=host \\"
    echo "    ${TAG_PREFIX}-client-python:python38"
    echo ""
fi

print_info "Note: Use --ipc=host to share IPC namespace with the host for shared memory access"