---
title: Update script with automatic version bumping
type: chore
date: 2026-02-05
task: "426"
---

# Update Script with Automatic Version Bumping

## Overview

Make `update.sh` require a version bump type (`major`, `minor`, `patch`) as a mandatory argument. The script should automatically update the `<Version>` in `cli-tasker.csproj` before building, eliminating the manual version bump step.

Also update CLAUDE.md to reflect the new usage.

## Problem Statement

Currently, the agent (or developer) must manually edit the version in `cli-tasker.csproj` before running `./update.sh`. This is easy to forget, and when forgotten, `dotnet tool update` silently does nothing because the version hasn't changed.

## Proposed Solution

### Update: `update.sh`

```bash
#!/bin/bash
set -e

# Require bump type argument
BUMP_TYPE="${1:?Usage: ./update.sh <major|minor|patch>}"

cd "$(dirname "$0")"

# Read current version from csproj
CURRENT_VERSION=$(grep -oP '(?<=<Version>)[^<]+' cli-tasker.csproj)
IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT_VERSION"

# Bump version
case "$BUMP_TYPE" in
    major) MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0 ;;
    minor) MINOR=$((MINOR + 1)); PATCH=0 ;;
    patch) PATCH=$((PATCH + 1)) ;;
    *) echo "Error: Invalid bump type '$BUMP_TYPE'. Use major, minor, or patch." >&2; exit 1 ;;
esac

NEW_VERSION="$MAJOR.$MINOR.$PATCH"

# Update version in csproj
sed -i '' "s|<Version>$CURRENT_VERSION</Version>|<Version>$NEW_VERSION</Version>|" cli-tasker.csproj
echo "Version: $CURRENT_VERSION → $NEW_VERSION"

# ... rest of existing script unchanged ...
```

### Update: `CLAUDE.md`

Update the "Updating CLI and TaskerTray" section:

```markdown
### Updating CLI and TaskerTray

```bash
./update.sh patch   # 2.29.0 → 2.29.1
./update.sh minor   # 2.29.0 → 2.30.0
./update.sh major   # 2.29.0 → 3.0.0
```

Also update the "Version bumping" section to note it's now automatic.

## Acceptance Criteria

- [x] `./update.sh` without arguments shows usage error and exits
- [x] `./update.sh patch` increments patch version (e.g., 2.29.0 → 2.29.1)
- [x] `./update.sh minor` increments minor, resets patch (e.g., 2.29.1 → 2.30.0)
- [x] `./update.sh major` increments major, resets minor and patch (e.g., 2.30.0 → 3.0.0)
- [x] Invalid bump type shows error message
- [x] Version is printed to stdout (old → new)
- [x] Rest of the script (pack, install, TaskerTray build) works as before
- [x] CLAUDE.md updated with new usage

## Files to Change

| File | Change |
|------|--------|
| `update.sh` | Add argument parsing and version bumping |
| `CLAUDE.md` | Update usage docs for update.sh |

## Edge Cases

| Case | Behavior |
|------|----------|
| No argument | Print usage, exit 1 |
| Invalid argument (e.g., "big") | Print error with valid options, exit 1 |
| Version already at x.0.0 | Works fine, patch → x.0.1 |

## References

- Current script: `update.sh`
- Version location: `cli-tasker.csproj:14`
- CLAUDE.md sections: "Version bumping", "Updating CLI and TaskerTray"
