#!/bin/bash

# Buff Notifications Release Script
# Usage: ./release.sh <version>
# Example: ./release.sh 1.0.0

set -e  # Exit on any error

# Check if version parameter is provided
if [ $# -ne 1 ]; then
    echo "Error: Version parameter is required"
    echo "Usage: ./release.sh <version>"
    echo "Example: ./release.sh 1.0.0"
    exit 1
fi

VERSION=$1
MOD_NAME="BuffNotifications"
RELEASES_DIR="./releases"
MANIFEST_PATH="./BuffNotifications/manifest.json"
CHANGELOG_PATH="./CHANGELOG.md"

# Validate semantic version format (major.minor.patch)
if ! [[ $VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: Version must be in semantic format (e.g., 1.0.0)"
    exit 1
fi

# Check if GitHub CLI is installed
if ! command -v gh &> /dev/null; then
    echo "Error: GitHub CLI (gh) is not installed. Please install it to create GitHub releases."
    echo "Installation instructions: https://github.com/cli/cli#installation"
    exit 1
fi

echo "Starting release process for $MOD_NAME v$VERSION"

# Update version in manifest.json
echo "Updating manifest.json version to $VERSION"
# Use temp file to avoid issues with in-place editing
cat "$MANIFEST_PATH" | sed "s/\"Version\": \"[^\"]*\"/\"Version\": \"$VERSION\"/" > "$MANIFEST_PATH.tmp"
mv "$MANIFEST_PATH.tmp" "$MANIFEST_PATH"

# Build the mod with Release configuration
echo "Building mod with Release configuration"
dotnet build -c Release

# Create releases directory if it doesn't exist
mkdir -p "$RELEASES_DIR"

# Copy the zip file to releases directory
ZIP_PATH="./BuffNotifications/bin/Release/net6.0/$MOD_NAME $VERSION.zip"
RELEASE_ZIP_PATH="$RELEASES_DIR/$MOD_NAME-$VERSION.zip"

echo "Copying release zip to $RELEASE_ZIP_PATH"
cp "$ZIP_PATH" "$RELEASE_ZIP_PATH"

# Git operations
echo "Adding files to git"
git add "$MANIFEST_PATH"
git add "$RELEASE_ZIP_PATH"
git add "$RELEASES_DIR"

echo "Committing version change"
git commit -m "Release version $VERSION"

# Tag management (delete if exists, create new, force push)
TAG_NAME="v$VERSION"
echo "Managing git tag: $TAG_NAME"

# Delete local tag if it exists
if git tag -l "$TAG_NAME" | grep -q "$TAG_NAME"; then
    echo "Deleting existing local tag: $TAG_NAME"
    git tag -d "$TAG_NAME"
fi

# Create new tag
echo "Creating new tag: $TAG_NAME"
git tag "$TAG_NAME"

# Delete remote tag if it exists and force push
echo "Force pushing tag to remote"
git push origin ":refs/tags/$TAG_NAME" 2>/dev/null || true
git push origin "$TAG_NAME"

# Extract release notes from CHANGELOG.md
echo "Extracting release notes from CHANGELOG.md"
RELEASE_NOTES=$(awk -v version="$VERSION" '
    BEGIN { found=0; capture=0; notes="" }
    /^## \[[0-9]+\.[0-9]+\.[0-9]+\]/ {
        if (found) { exit }
        if ($0 ~ "\\[" version "\\]") { 
            found=1; 
            next 
        }
    }
    found && /^###/ { capture=1; notes = notes $0 "\n"; next }
    found && /^$/ { notes = notes "\n"; next }
    found && capture { notes = notes $0 "\n" }
    END { print notes }
' "$CHANGELOG_PATH")

# Create GitHub release
echo "Creating GitHub release"

# Check if a release with this tag already exists and delete it
if gh release view "$TAG_NAME" &>/dev/null; then
    echo "Deleting existing GitHub release for $TAG_NAME"
    gh release delete "$TAG_NAME" --yes
fi

gh release create "$TAG_NAME" "$RELEASE_ZIP_PATH" \
    --title "$MOD_NAME v$VERSION" \
    --notes "$RELEASE_NOTES" \
    --target "$(git rev-parse --abbrev-ref HEAD)"

# Generate BBCode version of README for Nexus Mods
echo "Generating BBCode version of README for Nexus Mods"
mkdir -p ./docs/bbcode
README_BBCODE="./docs/bbcode/README_BBCODE.txt"

# Get the GitHub username and repo name
GITHUB_REMOTE_URL=$(git config --get remote.origin.url)
GITHUB_USERNAME=$(echo "$GITHUB_REMOTE_URL" | sed -n 's/.*github.com[:\/]\([^\/]*\)\/\([^\/]*\).*/\1/p')
GITHUB_REPO=$(echo "$GITHUB_REMOTE_URL" | sed -n 's/.*github.com[:\/]\([^\/]*\)\/\([^\/]*\).*/\2/p' | sed 's/\.git$//')

# If we couldn't extract the username/repo, use defaults
if [ -z "$GITHUB_USERNAME" ]; then
    GITHUB_USERNAME="24v"
fi
if [ -z "$GITHUB_REPO" ]; then
    GITHUB_REPO="BuffNotifications"
fi

GITHUB_RAW_BASE="https://raw.githubusercontent.com/$GITHUB_USERNAME/$GITHUB_REPO/main"

# Convert README.md to BBCode
cat README.md | sed -E '
    # Headers
    s/^# (.*)$/[size=6][b]\1[\/b][\/size]/g
    s/^## (.*)$/[size=5][b]\1[\/b][\/size]/g
    s/^### (.*)$/[size=4][b]\1[\/b][\/size]/g
    
    # Bold and Italic
    s/\*\*([^*]+)\*\*/[b]\1[\/b]/g
    s/\*([^*]+)\*/[i]\1[\/i]/g
    
    # Lists
    s/^- (.*)$/[*] \1/g
    s/^[0-9]+\. (.*)$/[list=1][*] \1/g
    
    # Links
    s/\[([^\]]+)\]\(([^)]+)\)/[url=\2]\1[\/url]/g
    
    # Code blocks
    s/```([^`]*)```/[code]\1[\/code]/g
    s/`([^`]*)`/[font=Courier New]\1[\/font]/g
' > "$README_BBCODE.tmp"

# Process images with absolute GitHub URLs
cat "$README_BBCODE.tmp" | sed -E "s|!\[([^\]]+)\]\(([^)]+)\)|[img]$GITHUB_RAW_BASE/\2[/img]|g" > "$README_BBCODE"
rm "$README_BBCODE.tmp"

echo "BBCode version of README generated at $README_BBCODE"

echo "Release process completed successfully!"
echo "Released: $MOD_NAME v$VERSION"
echo "Release zip: $RELEASE_ZIP_PATH"
echo "GitHub release created: $TAG_NAME"
