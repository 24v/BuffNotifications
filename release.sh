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

# Create a Python script to convert Markdown to BBCode
cat > convert_to_bbcode.py << 'EOF'
import sys
import re

def convert_to_bbcode(markdown_text, github_raw_base):
    # Process the file line by line to handle special cases
    lines = markdown_text.split('\n')
    result_lines = []
    
    for line in lines:
        # Handle images first
        line = re.sub(r'!\[(.*?)\]\((.*?)\)', r'[img]' + github_raw_base + r'/\2[/img]', line)
        
        # Handle headers
        if line.startswith('# '):
            line = '[size=6][b]' + line[2:] + '[/b][/size]'
        elif line.startswith('## '):
            line = '[size=5][b]' + line[3:] + '[/b][/size]'
        elif line.startswith('### '):
            line = '[size=4][b]' + line[4:] + '[/b][/size]'
        # Handle lists
        elif line.startswith('- '):
            # Process the rest of the line for other markdown elements
            rest_of_line = line[2:]
            # Handle links in list items
            rest_of_line = re.sub(r'\[(.*?)\]\((.*?)\)', r'[url=\2]\1[/url]', rest_of_line)
            # Handle bold and italic
            rest_of_line = re.sub(r'\*\*(.*?)\*\*', r'[b]\1[/b]', rest_of_line)
            rest_of_line = re.sub(r'\*(.*?)\*', r'[i]\1[/i]', rest_of_line)
            line = '[*] ' + rest_of_line
        elif re.match(r'^\d+\. ', line):
            # Extract the rest of the line after the number and period
            rest_of_line = re.sub(r'^\d+\. ', '', line)
            # Handle links in list items
            rest_of_line = re.sub(r'\[(.*?)\]\((.*?)\)', r'[url=\2]\1[/url]', rest_of_line)
            # Handle bold and italic
            rest_of_line = re.sub(r'\*\*(.*?)\*\*', r'[b]\1[/b]', rest_of_line)
            rest_of_line = re.sub(r'\*(.*?)\*', r'[i]\1[/i]', rest_of_line)
            line = '[*] ' + rest_of_line
        else:
            # For regular lines, handle links, bold, and italic
            line = re.sub(r'\[(.*?)\]\((.*?)\)', r'[url=\2]\1[/url]', line)
            line = re.sub(r'\*\*(.*?)\*\*', r'[b]\1[/b]', line)
            line = re.sub(r'\*(.*?)\*', r'[i]\1[/i]', line)
        
        # Handle code blocks and inline code
        line = re.sub(r'`([^`]*)`', r'[font=Courier New]\1[/font]', line)
        
        result_lines.append(line)
    
    # Join the lines back together
    result = '\n'.join(result_lines)
    
    # Handle multi-line code blocks
    result = re.sub(r'```(.*?)```', r'[code]\1[/code]', result, flags=re.DOTALL)
    
    return result

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python convert_to_bbcode.py <github_raw_base> <input_file>")
        sys.exit(1)
        
    github_raw_base = sys.argv[1]
    input_file = sys.argv[2]
    
    with open(input_file, 'r') as f:
        markdown_text = f.read()
    
    bbcode_text = convert_to_bbcode(markdown_text, github_raw_base)
    print(bbcode_text)
EOF

# Run the Python script to convert README.md to BBCode
python3 convert_to_bbcode.py "$GITHUB_RAW_BASE" README.md > "$README_BBCODE"
rm convert_to_bbcode.py

echo "BBCode version of README generated at $README_BBCODE"

echo "Release process completed successfully!"
echo "Released: $MOD_NAME v$VERSION"
echo "Release zip: $RELEASE_ZIP_PATH"
echo "GitHub release created: $TAG_NAME"
