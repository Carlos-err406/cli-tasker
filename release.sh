#!/usr/bin/env bash
set -euo pipefail

if [[ -n "$(git status --porcelain)" ]]; then
  echo "Error: working directory is not clean" >&2
  exit 1
fi

CURRENT_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "none")
echo "Current tag: ${CURRENT_TAG}"

read -rp "New version (e.g. 3.1.0): " VERSION

TAG="v${VERSION}"

if git rev-parse "$TAG" >/dev/null 2>&1; then
  echo "Error: tag ${TAG} already exists" >&2
  exit 1
fi

git tag "$TAG"
git push origin "$TAG"

echo "Pushed ${TAG} — release workflow triggered."
echo "https://github.com/Carlos-err406/cli-tasker/actions"
