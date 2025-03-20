# Buff Notifications Release Process

## Quick Start

1. Update CHANGELOG.md with your changes
2. Prepare the release: `./release.sh --prepare 1.0.0`
3. Create GitHub release: `./release.sh --release 1.0.0`

## Branching Strategy

We use the following branching strategy:

- `main`: Production code, always stable
- `release/x.y.z`: Release branches for planned releases
- `hotfix/x.y.z`: Hotfix branches for urgent fixes
- `feature/name`: Feature branches for new development

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

### 3. Prepare Your Branch

#### For hotfixes:
- Create a hotfix branch from main: `git checkout -b hotfix/1.0.1 main`
- Make your fixes directly on this branch
- Commit changes: `git commit -am "Fix critical bug"`

#### For regular releases:
- Create a release branch: `git checkout -b release/1.1.0 main`
- Merge feature branches with squash: `git merge --squash feature/new-icons`
- Commit the squashed changes: `git commit -m "Add new icons feature"`

### 4. Run the prepare script

When your hotfix/release branch is ready:

```bash
./release.sh --prepare 1.0.0
```

For a dry run (does everything except commit changes):
```bash
./release.sh --prepare --dry-run 1.0.0
```

### 5. Merge to Main

Merge your hotfix/release branch to main with no fast-forward:

```bash
git checkout main
git merge --no-ff hotfix/1.0.1
```

### 6. Run the release script

After merging to main:

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
