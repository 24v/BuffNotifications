# Release Process for Buff Notifications

This document outlines the process for creating a new release of the Buff Notifications mod.

## Prerequisites

- [GitHub CLI](https://github.com/cli/cli#installation) installed and authenticated
- Git configured with proper access to the repository
- .NET SDK installed for building the mod

## Release Steps

### 1. Update the CHANGELOG.md

Before creating a release, update the CHANGELOG.md file with the new version and details about what has changed:

```markdown
# Changelog

## [x.y.z] - YYYY-MM-DD

### Added
- New feature 1
- New feature 2

### Changed
- Change 1
- Change 2

### Fixed
- Bug fix 1
- Bug fix 2
```

Make sure to:
- Use semantic versioning (x.y.z format)
- Include the current date
- Organize changes under appropriate sections (Added, Changed, Fixed, etc.)
- Be specific and descriptive about each change

### 2. Commit Your Changes

Commit all your changes to the repository:

```bash
git add CHANGELOG.md
# Add any other modified files
git commit -m "Update changelog for version x.y.z"
```

### 3. Run the Release Script

Execute the release script with the new version number:

```bash
./release.sh x.y.z
```

For example:
```bash
./release.sh 1.0.0
```

### 4. What the Script Does

The release script will:

1. Update the version in `manifest.json`
2. Build the mod with Release configuration
3. Create a releases directory if it doesn't exist
4. Copy the built .zip file to the releases folder
5. Add the manifest file, release zip, and releases folder to git
6. Commit all changes
7. Create and force push a git tag
8. Extract release notes from CHANGELOG.md
9. Create a GitHub release with the extracted notes and attach the zip file

### 5. Verify the Release

After running the script:

1. Check that the GitHub release was created correctly
2. Verify that the attached zip file can be downloaded and installed
3. Confirm that the version number in the manifest.json file matches the release

## Troubleshooting

- If the script fails with a GitHub CLI error, make sure you have the GitHub CLI installed and authenticated
- If the build fails, check for compilation errors in your code
- If tag pushing fails, ensure you have proper write access to the repository

## Notes

- The script will overwrite existing tags with the same version number
- Make sure your CHANGELOG.md follows the expected format for proper extraction of release notes
