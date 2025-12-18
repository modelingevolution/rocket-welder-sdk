#!/bin/bash

# Release script for Rocket Welder SDK
# Creates a version tag to trigger GitHub Actions publish workflow

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Default values
DRY_RUN=false
AUTO_CONFIRM=false
MESSAGE=""
VERSION=""
INCREMENT=""

show_help() {
    echo "Usage: $0 [VERSION] [OPTIONS]"
    echo ""
    echo "Arguments:"
    echo "  VERSION              Explicit version (e.g., 1.0.1)"
    echo ""
    echo "Options:"
    echo "  -m, --message TEXT   Release notes/message"
    echo "  -p, --patch          Auto-increment patch version"
    echo "  -n, --minor          Auto-increment minor version"
    echo "  -M, --major          Auto-increment major version"
    echo "  -y, --yes            Auto-confirm without prompts"
    echo "  --dry-run            Preview without executing"
    echo "  -h, --help           Show help"
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -m|--message)
            MESSAGE="$2"
            shift 2
            ;;
        -p|--patch)
            INCREMENT="patch"
            shift
            ;;
        -n|--minor)
            INCREMENT="minor"
            shift
            ;;
        -M|--major)
            INCREMENT="major"
            shift
            ;;
        -y|--yes)
            AUTO_CONFIRM=true
            shift
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        -*)
            echo -e "${RED}Unknown option: $1${NC}"
            show_help
            exit 1
            ;;
        *)
            VERSION="$1"
            shift
            ;;
    esac
done

# Check for uncommitted changes
if ! git diff --quiet || ! git diff --staged --quiet; then
    echo -e "${RED}Error: Working directory has uncommitted changes${NC}"
    echo "Please commit or stash your changes first."
    exit 1
fi

# Check for unpushed commits
LOCAL=$(git rev-parse @)
REMOTE=$(git rev-parse @{u} 2>/dev/null || echo "")
if [ -n "$REMOTE" ] && [ "$LOCAL" != "$REMOTE" ]; then
    echo -e "${RED}Error: You have unpushed commits${NC}"
    echo "Please push your commits first: git push"
    exit 1
fi

# Get current branch
BRANCH=$(git branch --show-current)
if [ "$BRANCH" != "master" ] && [ "$BRANCH" != "main" ]; then
    echo -e "${YELLOW}Warning: You are on branch '$BRANCH', not master/main${NC}"
    if [ "$AUTO_CONFIRM" = false ]; then
        read -p "Continue anyway? (y/N) " confirm
        if [ "$confirm" != "y" ] && [ "$confirm" != "Y" ]; then
            exit 1
        fi
    fi
fi

# Get latest tag version
get_latest_version() {
    git tag -l 'v*.*.*' | sort -V | tail -n1 | sed 's/^v//' || echo "0.0.0"
}

# Increment version
increment_version() {
    local version=$1
    local part=$2
    local major minor patch

    IFS='.' read -r major minor patch <<< "$version"

    case $part in
        major)
            echo "$((major + 1)).0.0"
            ;;
        minor)
            echo "$major.$((minor + 1)).0"
            ;;
        patch)
            echo "$major.$minor.$((patch + 1))"
            ;;
    esac
}

# Determine version
if [ -z "$VERSION" ]; then
    LATEST=$(get_latest_version)
    if [ -n "$INCREMENT" ]; then
        VERSION=$(increment_version "$LATEST" "$INCREMENT")
    else
        # Default to patch increment
        VERSION=$(increment_version "$LATEST" "patch")
    fi
fi

# Validate version format
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo -e "${RED}Error: Invalid version format '$VERSION'${NC}"
    echo "Version must be in format X.Y.Z (e.g., 1.0.1)"
    exit 1
fi

TAG="v$VERSION"

# Check if tag already exists
if git tag -l "$TAG" | grep -q "$TAG"; then
    echo -e "${RED}Error: Tag $TAG already exists${NC}"
    exit 1
fi

# Display summary
echo ""
echo -e "${GREEN}Release Summary:${NC}"
echo "  Version: $VERSION"
echo "  Tag: $TAG"
echo "  Branch: $BRANCH"
if [ -n "$MESSAGE" ]; then
    echo "  Message: $MESSAGE"
fi
echo ""

if [ "$DRY_RUN" = true ]; then
    echo -e "${YELLOW}[DRY RUN] Would create and push tag: $TAG${NC}"
    exit 0
fi

# Confirm
if [ "$AUTO_CONFIRM" = false ]; then
    read -p "Create and push tag $TAG? (y/N) " confirm
    if [ "$confirm" != "y" ] && [ "$confirm" != "Y" ]; then
        echo "Aborted."
        exit 1
    fi
fi

# Create tag
if [ -n "$MESSAGE" ]; then
    git tag -a "$TAG" -m "$MESSAGE"
else
    git tag "$TAG"
fi

# Push tag
git push origin "$TAG"

echo ""
echo -e "${GREEN}Release $TAG created and pushed!${NC}"
echo ""
echo "GitHub Actions will now:"
echo "  1. Build and test"
echo "  2. Publish RocketWelder.BinaryProtocol to NuGet.org"
echo "  3. Publish RocketWelder.SDK to NuGet.org"
echo ""
echo "Monitor at: https://github.com/modelingevolution/rocket-welder-sdk/actions"
