#!/bin/bash

# Buff Notifications Release Script
# Usage: ./release.sh [options] <version>
# Examples:
#   Prepare mode: ./release.sh --prepare 1.0.0
#   Prepare mode (dry run): ./release.sh --prepare --dry-run 1.0.0
#   Release mode: ./release.sh --release 1.0.0
#   Release mode (dry run): ./release.sh --release --dry-run 1.0.0

set -e  # Exit on any error

# Default values
PREPARE_MODE=false
RELEASE_MODE=false
DRY_RUN=false

# Parse command line options
while [[ $# -gt 0 ]]; do
    case $1 in
        --prepare)
            PREPARE_MODE=true
            shift
            ;;
        --release)
            RELEASE_MODE=true
            shift
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        *)
            VERSION=$1
            shift
            ;;
    esac
done

# Check if version parameter is provided
if [ -z "$VERSION" ]; then
    echo "Error: Version parameter is required"
    echo "Usage: ./release.sh [options] <version>"
    echo "Options:"
    echo "  --prepare      Prepare mode: Updates manifest, builds, copies files, updates BBCode"
    echo "  --release      Release mode: Creates tag and GitHub release"
    echo "  --dry-run      Simulate operations without making permanent changes"
    echo "                 For prepare mode: Stops before git commit"
    echo "                 For release mode: Shows what would happen without making changes"
    echo "Examples:"
    echo "  Prepare mode: ./release.sh --prepare 1.0.0"
    echo "  Prepare mode (dry run): ./release.sh --prepare --dry-run 1.0.0"
    echo "  Release mode: ./release.sh --release 1.0.0"
    echo "  Release mode (dry run): ./release.sh --release --dry-run 1.0.0"
    exit 1
fi

# Check if a mode is specified
if [ "$PREPARE_MODE" = false ] && [ "$RELEASE_MODE" = false ]; then
    echo "Error: You must specify either --prepare or --release mode"
    exit 1
fi

# Check if both modes are specified
if [ "$PREPARE_MODE" = true ] && [ "$RELEASE_MODE" = true ]; then
    echo "Error: You cannot specify both --prepare and --release modes"
    exit 1
fi

MOD_NAME="BuffNotifications"
RELEASES_DIR="./releases"
MANIFEST_PATH="./BuffNotifications/manifest.json"
CHANGELOG_PATH="./CHANGELOG.md"

# Validate version format (major.minor.patch or major.minor.patch-suffix)
if ! [[ $VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9]+)?$ ]]; then
    echo "Error: Version must be in format major.minor.patch (e.g., 1.0.0) or major.minor.patch-suffix (e.g., 1.0.0-beta1)"
    exit 1
fi

echo "Using version: $VERSION"

# Check if GitHub CLI is installed (only if in release mode and not dry run)
if [ "$RELEASE_MODE" = true ] && [ "$DRY_RUN" = false ]; then
    if ! command -v gh &> /dev/null; then
        echo "Error: GitHub CLI (gh) is not installed. Please install it to create GitHub releases."
        echo "Installation instructions: https://github.com/cli/cli#installation"
        exit 1
    fi
fi

# Create releases directory if it doesn't exist
mkdir -p "$RELEASES_DIR"

# Get current branch
CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD)
echo "Current branch: $CURRENT_BRANCH"

# Prepare mode operations
if [ "$PREPARE_MODE" = true ]; then
    echo "Running in prepare mode"
    if [ "$DRY_RUN" = true ]; then
        echo "[DRY RUN] Will perform all prepare operations except git commit"
    fi
    
    # Update manifest.json with new version
    echo "Updating manifest.json with version $VERSION"
    sed -i.bak "s/\"Version\": \"[^\"]*\"/\"Version\": \"$VERSION\"/" "$MANIFEST_PATH" && rm "${MANIFEST_PATH}.bak"
    
    # Build the mod
    echo "Building mod with Release configuration"
    dotnet build BuffNotifications.sln -c Release
    
    # Copy the release zip
    RELEASE_ZIP_PATH="$RELEASES_DIR/$MOD_NAME-$VERSION.zip"
    echo "Copying release zip to $RELEASE_ZIP_PATH"
    
    # Check if the zip file exists - try both with and without version
    if [ -f "./BuffNotifications/bin/Release/net6.0/$MOD_NAME $VERSION.zip" ]; then
        cp "./BuffNotifications/bin/Release/net6.0/$MOD_NAME $VERSION.zip" "$RELEASE_ZIP_PATH"
    else
        echo "Error: Release zip not found at ./BuffNotifications/bin/Release/net6.0/$MOD_NAME $VERSION.zip"
        echo "Build process may have failed or didn't generate a zip file"
        exit 1
    fi
    
    # Generate BBCode version of README for Nexus Mods
    echo "Generating BBCode version of README for Nexus Mods"
    
    # Setup paths
    README_PATH="./README.md"
    README_BBCODE="./docs/bbcode/README_BBCODE.txt"
    README_BBCODE_TEMP="$README_BBCODE.tmp"
    
    # Create the bbcode directory if it doesn't exist
    mkdir -p "./docs/bbcode"
    
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
    
    # Use the Python script for BBCode conversion
    python3 ./convert_to_bbcode.py "$README_PATH" "$README_BBCODE_TEMP" "$GITHUB_RAW_BASE" || { 
        echo "Error: Python conversion failed"; 
        exit 1; 
    }
    
    # Check if BBCode content changed
    if [ -f "$README_BBCODE" ] && cmp -s "$README_BBCODE_TEMP" "$README_BBCODE"; then
        echo "BBCode README has not changed"
        rm "$README_BBCODE_TEMP"
    else
        mv "$README_BBCODE_TEMP" "$README_BBCODE"
        echo "BBCode README updated at $README_BBCODE"
    fi
    
    # Git operations - only if not in dry run mode
    if [ "$DRY_RUN" = false ]; then
        # Check if there are changes to commit
        if git status --porcelain | grep -q .; then
            echo "Changes detected, committing to git"
            
            # Add changed files
            git add .
            
            # Commit message
            echo "Committing version changes"
            git commit -m "Update version to $VERSION"
            
            echo "Changes committed successfully"
        else
            echo "No changes detected, skipping commit"
        fi
    else
        # In dry run mode, show what would be committed but don't commit
        if git status --porcelain | grep -q .; then
            echo "[DRY RUN] Changes detected, would commit these files:"
            git status --porcelain
        else
            echo "[DRY RUN] No changes detected, would skip commit"
        fi
    fi
    
    echo "Prepare mode operations completed successfully!"
    echo "Version: $MOD_NAME v$VERSION"
    echo "Release zip: $RELEASE_ZIP_PATH"
fi

# Release mode operations
if [ "$RELEASE_MODE" = true ]; then
    echo "Running in release mode"
    if [ "$DRY_RUN" = true ]; then
        echo "[DRY RUN] Simulating release operations (no changes will be made)"
    fi
    
    # Verify we're on the main branch
    if [ "$CURRENT_BRANCH" != "main" ]; then
        echo "Warning: You are not on the main branch. Current branch: $CURRENT_BRANCH"
        if [ "$DRY_RUN" = false ]; then
            read -p "Do you want to continue anyway? (y/n) " -n 1 -r
            echo
            if [[ ! $REPLY =~ ^[Yy]$ ]]; then
                echo "Operation cancelled"
                exit 1
            fi
        else
            echo "[DRY RUN] Would prompt for confirmation to continue on non-main branch"
        fi
    fi
    
    # Tag management (delete if exists, create new)
    TAG_NAME="v$VERSION"
    echo "Managing git tag: $TAG_NAME"
    
    # Delete local tag if it exists
    if [ "$DRY_RUN" = false ]; then
        if git tag -l "$TAG_NAME" | grep -q "$TAG_NAME"; then
            echo "Deleting existing local tag: $TAG_NAME"
            git tag -d "$TAG_NAME"
        fi
        
        # Create new tag
        echo "Creating new tag: $TAG_NAME"
        git tag "$TAG_NAME"
        
        # Delete remote tag if it exists
        echo "Pushing tag to remote"
        git push origin ":refs/tags/$TAG_NAME" 2>/dev/null || true
        git push origin "$TAG_NAME"
    else
        echo "[DRY RUN] Would delete existing tag: $TAG_NAME"
        echo "[DRY RUN] Would create new tag: $TAG_NAME"
        echo "[DRY RUN] Would push tag to remote"
    fi
    
    # GitHub release operations
    if [ "$DRY_RUN" = false ]; then
        # Delete existing release with the same tag if it exists
        if gh release view "$TAG_NAME" &>/dev/null; then
            echo "Deleting existing release: $TAG_NAME"
            gh release delete "$TAG_NAME" --yes
        fi
    else
        echo "[DRY RUN] Would delete existing release: $TAG_NAME"
    fi
    
    # Get the release zip path
    RELEASE_ZIP_PATH="$RELEASES_DIR/$MOD_NAME-$VERSION.zip"
    
    # Check if the release zip exists and is not empty
    if [ ! -f "$RELEASE_ZIP_PATH" ] && [ "$DRY_RUN" = false ]; then
        echo "Error: Release zip not found at $RELEASE_ZIP_PATH"
        echo "Make sure to run in prepare mode first to create the release zip"
        exit 1
    elif [ ! -s "$RELEASE_ZIP_PATH" ] && [ "$DRY_RUN" = false ]; then
        echo "Error: Release zip is empty at $RELEASE_ZIP_PATH"
        echo "Make sure the prepare mode completed successfully"
        exit 1
    elif [ "$DRY_RUN" = true ] && ([ ! -f "$RELEASE_ZIP_PATH" ] || [ ! -s "$RELEASE_ZIP_PATH" ]); then
        echo "[DRY RUN] Release zip not found or empty, would fail in normal mode"
        echo "[DRY RUN] Continuing for demonstration purposes only"
    else
        echo "Found release zip at $RELEASE_ZIP_PATH"
    fi
    
    # Extract release notes from CHANGELOG.md
    if [ -f "$CHANGELOG_PATH" ]; then
        # Try to extract notes from CHANGELOG
        RELEASE_NOTES=$(awk "/## $VERSION/{flag=1;next}/## [0-9]+\.[0-9]+\.[0-9]+/{flag=0}flag" "$CHANGELOG_PATH")
        # If no specific release notes found, use generic message
        if [ -z "$RELEASE_NOTES" ]; then
            RELEASE_NOTES="Release version $VERSION of $MOD_NAME."
        fi
    else
        # If no CHANGELOG exists, use generic message
        RELEASE_NOTES="Release version $VERSION of $MOD_NAME."
    fi
    
    # Check if version contains a suffix (like -beta1) to determine if it's a pre-release
    IS_PRERELEASE=false
    if [[ $VERSION == *-* ]]; then
        IS_PRERELEASE=true
    fi
    
    # Create GitHub release
    if [ "$DRY_RUN" = false ]; then
        if [ "$IS_PRERELEASE" = true ]; then
            echo "Creating GitHub pre-release"
            gh release create "$TAG_NAME" "$RELEASE_ZIP_PATH" \
                --title "$MOD_NAME v$VERSION" \
                --notes "$RELEASE_NOTES" \
                --prerelease \
                --target "$CURRENT_BRANCH"
        else
            echo "Creating GitHub release"
            gh release create "$TAG_NAME" "$RELEASE_ZIP_PATH" \
                --title "$MOD_NAME v$VERSION" \
                --notes "$RELEASE_NOTES" \
                --target "$CURRENT_BRANCH"
        fi
    else
        if [ "$IS_PRERELEASE" = true ]; then
            echo "[DRY RUN] Would create GitHub pre-release: $TAG_NAME"
        else
            echo "[DRY RUN] Would create GitHub release: $TAG_NAME"
        fi
        echo "[DRY RUN] Release notes would be:"
        echo "---"
        echo "$RELEASE_NOTES"
        echo "---"
    fi
    
    # Output success message
    echo "Release process completed successfully!"
    echo "Released: $MOD_NAME v$VERSION"
    
    if [ "$DRY_RUN" = false ]; then
        echo "Release zip: $RELEASE_ZIP_PATH"
        echo "GitHub release created: $TAG_NAME"
        
        # Get the GitHub repository URL
        GITHUB_REPO_URL=$(git config --get remote.origin.url | sed 's/\.git$//' | sed 's/git@github.com:/https:\/\/github.com\//')
        if [[ "$GITHUB_REPO_URL" == *"https://"* ]]; then
            echo "Release URL: $GITHUB_REPO_URL/releases/tag/$TAG_NAME"
        else
            # Extract username and repo from SSH URL
            GITHUB_USERNAME=$(echo "$GITHUB_REPO_URL" | sed -n 's/.*github.com[:\/]\([^\/]*\)\/\([^\/]*\).*/\1/p')
            GITHUB_REPO_NAME=$(echo "$GITHUB_REPO_URL" | sed -n 's/.*github.com[:\/]\([^\/]*\)\/\([^\/]*\).*/\2/p')
            echo "Release URL: https://github.com/$GITHUB_USERNAME/$GITHUB_REPO_NAME/releases/tag/$TAG_NAME"
        fi
    else
        echo "[DRY RUN] Would create GitHub release URL for $TAG_NAME"
    fi
fi
