#!/bin/bash
set -e

cd "$(dirname "$0")"

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
