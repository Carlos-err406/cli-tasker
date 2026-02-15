#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: ./release.sh <version> (e.g. 3.1.0)" >&2
  exit 1
fi

if [[ -n "$(git status --porcelain)" ]]; then
  echo "Error: working directory is not clean" >&2
  exit 1
fi

VERSION="$1"
TAG="v${VERSION}"

if git rev-parse "$TAG" >/dev/null 2>&1; then
  echo "Error: tag ${TAG} already exists" >&2
  exit 1
fi

# Update version in all package.json files so artifact names match the release tag
for pkg in package.json packages/core/package.json apps/cli/package.json apps/desktop/package.json; do
  jq --arg v "$VERSION" '.version = $v' "$pkg" > "$pkg.tmp" && mv "$pkg.tmp" "$pkg"
done

git add package.json packages/core/package.json apps/cli/package.json apps/desktop/package.json
git commit -m "chore: bump version to ${VERSION}"

git tag "$TAG"
git push origin main "$TAG"

echo "Pushed ${TAG} — release workflow triggered."
echo "https://github.com/Carlos-err406/cli-tasker/actions"
