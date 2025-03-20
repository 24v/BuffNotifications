# Buff Notifications Release Process

## Quick Start

1. Update CHANGELOG.md with your changes
2. Prepare the release: `./release.sh --prepare 1.0.0`
3. Create GitHub release: `./release.sh --release 1.0.0`

## Detailed Steps

### 1. Update CHANGELOG.md

Add your new version with today's date and list your changes:

```markdown
## [1.0.0] - 2025-03-20

### Added
- New buff icons for all vanilla buffs

### Fixed
- Notification positioning with UI scaling enabled
```

### 2. Commit your changelog

```bash
git add CHANGELOG.md
git commit -m "Update changelog for 1.0.0"
```

### 3. Run the release script

#### Prepare mode

This updates the manifest, builds the mod, and generates documentation:

```bash
./release.sh --prepare 1.0.0
```

For a dry run (does everything except commit changes):
```bash
./release.sh --prepare --dry-run 1.0.0
```

#### Release mode

This creates git tags and the GitHub release:

```bash
./release.sh --release 1.0.0
```

For a dry run:
```bash
./release.sh --release --dry-run 1.0.0
```

## What the script does

### Prepare mode
- Updates version in manifest.json
- Builds the mod
- Copies the zip to releases folder
- Generates BBCode for Nexus Mods
- Commits changes (unless in dry run)

### Release mode
- Checks if you're on main branch
- Creates and pushes a git tag
- Creates a GitHub release with notes from CHANGELOG
- Attaches the zip file

## Typical workflow

1. Work on features in your branch
2. Run `./release.sh --prepare 1.0.0` to test
3. Merge to main when ready
4. Run `./release.sh --prepare 1.0.0` again
5. Run `./release.sh --release 1.0.0` to publish

## Version format

- Regular releases: `1.0.0`
- Pre-releases: `1.0.0-beta1` (marked as pre-release on GitHub)

