#!/bin/bash
set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Parse arguments
DRY_RUN=false
VERSION=""
INCREMENT="patch"
MESSAGE=""

while [[ $# -gt 0 ]]; do
    case $1 in
        -p|--patch) INCREMENT="patch"; shift ;;
        -n|--minor) INCREMENT="minor"; shift ;;
        -M|--major) INCREMENT="major"; shift ;;
        -m|--message) MESSAGE="$2"; shift 2 ;;
        --dry-run) DRY_RUN=true; shift ;;
        -*) echo "Unknown option: $1"; exit 1 ;;
        *) VERSION="$1"; shift ;;
    esac
done

# Check for uncommitted changes
if ! git diff --quiet || ! git diff --staged --quiet; then
    echo "Error: Uncommitted changes. Commit or stash first."
    exit 1
fi

# Get latest version from tags
get_latest_version() {
    git tag -l 'csharp-v*.*.*' | sort -V | tail -n1 | sed 's/^csharp-v//' || echo "0.0.0"
}

# Increment version
increment_version() {
    local v=$1 part=$2
    IFS='.' read -r major minor patch <<< "$v"
    case $part in
        major) echo "$((major + 1)).0.0" ;;
        minor) echo "$major.$((minor + 1)).0" ;;
        patch) echo "$major.$minor.$((patch + 1))" ;;
    esac
}

# Determine version
if [ -z "$VERSION" ]; then
    VERSION=$(increment_version "$(get_latest_version)" "$INCREMENT")
fi

TAG="csharp-v$VERSION"

# Check tag doesn't exist
if git tag -l "$TAG" | grep -q "$TAG"; then
    echo "Error: Tag $TAG already exists"
    exit 1
fi

echo "Creating release: $TAG"

if [ "$DRY_RUN" = true ]; then
    echo "[DRY RUN] Would create and push tag: $TAG"
    exit 0
fi

# Create and push tag
if [ -n "$MESSAGE" ]; then
    git tag -a "$TAG" -m "$MESSAGE"
else
    git tag "$TAG"
fi
git push origin "$TAG"

echo "Release $TAG created! GitHub Actions will publish to NuGet.org"
