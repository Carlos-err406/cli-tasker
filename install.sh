#!/usr/bin/env bash
set -euo pipefail

APP_NAME="Tasker"
INSTALL_DIR="/Applications"
ARCH=$(uname -m)

if [[ "$ARCH" == "arm64" ]]; then
  BUILD_DIR="apps/desktop/dist-release/mac-arm64"
else
  BUILD_DIR="apps/desktop/dist-release/mac"
fi

cd "$(dirname "$0")"

echo "==> Installing dependencies..."
pnpm install

echo "==> Building core..."
pnpm --filter @tasker/core run build

echo "==> Building desktop (vite)..."
pnpm --filter @tasker/desktop run build

echo "==> Packaging app (electron-builder)..."
pnpm --filter @tasker/desktop run package

echo "==> Installing ${APP_NAME}.app to ${INSTALL_DIR}..."
if [[ -d "${INSTALL_DIR}/${APP_NAME}.app" ]]; then
  rm -rf "${INSTALL_DIR}/${APP_NAME}.app"
fi
cp -R "${BUILD_DIR}/${APP_NAME}.app" "${INSTALL_DIR}/"

echo "==> Done! ${APP_NAME} is installed at ${INSTALL_DIR}/${APP_NAME}.app"
