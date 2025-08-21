#!/bin/bash

# Publish script for Rocket Welder SDK
# Handles git checks, version bumping, and tagging

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Function to print colored output
print_error() { echo -e "${RED}Error: $1${NC}" >&2; }
print_success() { echo -e "${GREEN}$1${NC}"; }
print_warning() { echo -e "${YELLOW}$1${NC}"; }

# Function to show usage
usage() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Options:"
    echo "  --major             Bump major version (X.0.0)"
    echo "  --minor             Bump minor version (0.X.0)"
    echo "  --patch             Bump patch version (0.0.X) [default]"
    echo "  -m, --message MSG   Commit message for the version bump"
    echo "  --dry-run           Show what would be done without doing it"
    echo "  --no-push           Create tag but don't push to origin"
    echo "  --help              Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0 --patch -m \"Fix critical bug\""
    echo "  $0 --minor -m \"Add new feature\""
    echo "  $0 --major -m \"Breaking changes\""
}

# Parse arguments
VERSION_BUMP="patch"
COMMIT_MESSAGE=""
DRY_RUN=false
NO_PUSH=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --major)
            VERSION_BUMP="major"
            shift
            ;;
        --minor)
            VERSION_BUMP="minor"
            shift
            ;;
        --patch)
            VERSION_BUMP="patch"
            shift
            ;;
        -m|--message)
            COMMIT_MESSAGE="$2"
            shift 2
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        --no-push)
            NO_PUSH=true
            shift
            ;;
        --help)
            usage
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            usage
            exit 1
            ;;
    esac
done

# Check if git is available
if ! command -v git &> /dev/null; then
    print_error "git is not installed"
    exit 1
fi

# Check if we're in a git repository
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    print_error "Not in a git repository"
    exit 1
fi

# Check for uncommitted changes
if ! git diff-index --quiet HEAD --; then
    print_error "Working tree is not clean. Please commit or stash your changes."
    echo ""
    echo "Uncommitted changes:"
    git status --short
    exit 1
fi

# Check if we're on a branch
CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD)
if [ "$CURRENT_BRANCH" = "HEAD" ]; then
    print_error "Not on a branch (detached HEAD state)"
    exit 1
fi

print_success "Working tree is clean"
echo "Current branch: $CURRENT_BRANCH"

# Get the latest tag
LATEST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.0.0")
print_success "Latest tag: $LATEST_TAG"

# Remove 'v' prefix if present
VERSION="${LATEST_TAG#v}"
# Also remove any SDK-specific prefixes
VERSION="${VERSION#csharp-v}"
VERSION="${VERSION#cpp-v}"
VERSION="${VERSION#python-v}"

# Parse version components
IFS='.' read -r MAJOR MINOR PATCH <<< "$VERSION"

# Ensure we have valid numbers
MAJOR=${MAJOR:-0}
MINOR=${MINOR:-0}
PATCH=${PATCH:-0}

# Bump version based on argument
case $VERSION_BUMP in
    major)
        MAJOR=$((MAJOR + 1))
        MINOR=0
        PATCH=0
        ;;
    minor)
        MINOR=$((MINOR + 1))
        PATCH=0
        ;;
    patch)
        PATCH=$((PATCH + 1))
        ;;
esac

NEW_VERSION="$MAJOR.$MINOR.$PATCH"
NEW_TAG="v$NEW_VERSION"

print_success "New version: $NEW_VERSION"

# Generate commit message if not provided
if [ -z "$COMMIT_MESSAGE" ]; then
    COMMIT_MESSAGE="Release version $NEW_VERSION"
fi

# Show what will be done
echo ""
echo "Will create tag: $NEW_TAG"
echo "Commit message: $COMMIT_MESSAGE"
echo ""

if [ "$DRY_RUN" = true ]; then
    print_warning "DRY RUN - No changes will be made"
    echo ""
    echo "Would execute:"
    echo "  git tag -a $NEW_TAG -m \"$COMMIT_MESSAGE\""
    if [ "$NO_PUSH" = false ]; then
        echo "  git push origin $NEW_TAG"
    fi
    exit 0
fi

# Create the tag
echo "Creating tag $NEW_TAG..."
git tag -a "$NEW_TAG" -m "$COMMIT_MESSAGE"

if [ $? -eq 0 ]; then
    print_success "Tag $NEW_TAG created successfully"
else
    print_error "Failed to create tag"
    exit 1
fi

# Push the tag unless --no-push was specified
if [ "$NO_PUSH" = false ]; then
    echo "Pushing tag to origin..."
    git push origin "$NEW_TAG"
    
    if [ $? -eq 0 ]; then
        print_success "Tag pushed successfully"
        echo ""
        echo "GitHub Actions will now:"
        echo "  1. Build and test all SDKs"
        echo "  2. Publish to respective package repositories"
        echo "  3. Create a GitHub release"
        echo ""
        echo "Monitor the progress at:"
        echo "  https://github.com/$(git remote get-url origin | sed 's/.*github.com[:/]\(.*\)\.git/\1/')/actions"
    else
        print_error "Failed to push tag"
        echo "You can push it manually with: git push origin $NEW_TAG"
        exit 1
    fi
else
    print_warning "Tag created but not pushed (--no-push specified)"
    echo "Push it manually with: git push origin $NEW_TAG"
fi

print_success "Done!"