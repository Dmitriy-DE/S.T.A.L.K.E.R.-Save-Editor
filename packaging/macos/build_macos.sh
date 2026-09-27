#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="${1:-1.4.0}"
RID="${2:-osx-arm64}"
DIST="${ROOT}/dist"
BUILD_DIR="${ROOT}/build/macos"
APP_NAME="StalkerSaveEditor.app"
APP_DIR="${BUILD_DIR}/${APP_NAME}"

echo "=== Building S.T.A.L.K.E.R. Save Editor macOS Bundle v${VERSION} (${RID}) ==="

rm -rf "${BUILD_DIR}"
mkdir -p "${APP_DIR}/Contents/MacOS"
mkdir -p "${APP_DIR}/Contents/Resources"
mkdir -p "${DIST}"

# 1. Publish .NET self-contained
dotnet publish "${ROOT}/src/StalkerSaveEditor.Desktop/StalkerSaveEditor.Desktop.csproj" \
    -c Release \
    -r "${RID}" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:TreatWarningsAsErrors=false \
    -o "${APP_DIR}/Contents/MacOS"

mv "${APP_DIR}/Contents/MacOS/StalkerSaveEditor.Desktop" "${APP_DIR}/Contents/MacOS/StalkerSaveEditor"
chmod +x "${APP_DIR}/Contents/MacOS/StalkerSaveEditor"

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
