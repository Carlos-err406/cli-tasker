#!/bin/bash
set -e

# Require bump type argument
if [ -z "$1" ]; then
    echo "Usage: ./update.sh <major|minor|patch>" >&2
    exit 1
fi

BUMP_TYPE="$1"
cd "$(dirname "$0")"

# Read current version from csproj
CURRENT_VERSION=$(grep -oE '<Version>[^<]+' cli-tasker.csproj | sed 's/<Version>//')
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

# Update CLI
echo "Updating CLI..."
dotnet pack -c Release -o ./nupkg -v q
dotnet tool update -g cli-tasker --add-source ./nupkg

# Update TaskerTray
echo "Stopping TaskerTray..."
pkill -9 TaskerTray 2>/dev/null || true

echo "Building TaskerTray..."
cd src/TaskerTray
dotnet publish -c Release -r osx-arm64 --self-contained -o ./publish -v q

echo "Creating app bundle..."
rm -rf Tasker.app
mkdir -p "Tasker.app/Contents/MacOS" "Tasker.app/Contents/Resources"
cp -R publish/* "Tasker.app/Contents/MacOS/"
cp Info.plist "Tasker.app/Contents/"
cp Assets/AppIcon.icns "Tasker.app/Contents/Resources/" 2>/dev/null || true

echo "Installing to /Applications..."
rm -rf /Applications/Tasker.app
cp -R Tasker.app /Applications/

echo "Refreshing Launch Services..."
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f /Applications/Tasker.app

echo "Launching TaskerTray..."
nohup /Applications/Tasker.app/Contents/MacOS/TaskerTray > /dev/null 2>&1 &

echo "Done! CLI and TaskerTray updated."
