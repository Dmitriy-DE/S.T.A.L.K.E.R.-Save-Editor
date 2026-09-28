#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="${1:?version}"
RID="${2:-osx-arm64}"
DIST="${ROOT}/dist"
BUILD_DIR="${ROOT}/build/macos"
APP_NAME="StalkerSaveEditor.app"
APP_DIR="${BUILD_DIR}/${APP_NAME}"

echo "=== Building S.T.A.L.K.E.R. Save Editor macOS Bundle v${VERSION} (${RID}) ==="

rm -rf "${BUILD_DIR}"
mkdir -p "${APP_DIR}/Contents/Resources"
mkdir -p "${DIST}"

# 1. Application folder inside the bundle; the updater reads Contents/Resources/BUILD_MANIFEST.json.
"${ROOT}/packaging/publish_app.sh" "${RID}" "${APP_DIR}/Contents/MacOS" "${VERSION}"
mv "${APP_DIR}/Contents/MacOS/BUILD_MANIFEST.json" "${APP_DIR}/Contents/Resources/BUILD_MANIFEST.json"

# 2. Copy Info.plist and resources
cp "${ROOT}/packaging/macos/Info.plist" "${APP_DIR}/Contents/Info.plist"
cp "${ROOT}/packaging/macos/app_icon.png" "${APP_DIR}/Contents/Resources/AppIcon.png"

# 3. Create DMG (on macOS) or ZIP/tarball (cross-platform)
DMG_PATH="${DIST}/StalkerSaveEditor-v${VERSION}-${RID}.dmg"
if command -v hdiutil &> /dev/null; then
    echo "Creating DMG using hdiutil..."
    hdiutil create -volname "StalkerSaveEditor" -srcfolder "${BUILD_DIR}" -ov -format UDZO "${DMG_PATH}"
    echo "Created DMG: ${DMG_PATH}"
else
    echo "hdiutil not available; creating tarball archive..."
    TAR_PATH="${DIST}/StalkerSaveEditor-v${VERSION}-${RID}.tar.gz"
    tar -czf "${TAR_PATH}" -C "${BUILD_DIR}" "${APP_NAME}"
    echo "Created archive: ${TAR_PATH}"
fi
