#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="${1:-1.4.0}"
ARCH="x86_64"
OUTPUT_DIR="${ROOT}/dist"
BUILD_DIR="${ROOT}/build/appimage"
APP_DIR="${BUILD_DIR}/StalkerSaveEditor.AppDir"

echo "=== Building S.T.A.L.K.E.R. Save Editor AppImage v${VERSION} (${ARCH}) ==="

# 1. Clean previous build
rm -rf "${BUILD_DIR}"
mkdir -p "${APP_DIR}/usr/bin"
mkdir -p "${APP_DIR}/usr/lib"
mkdir -p "${OUTPUT_DIR}"

# 2. Publish .NET self-contained single file
echo "Publishing .NET project for linux-x64..."
dotnet publish "${ROOT}/src/StalkerSaveEditor.Desktop/StalkerSaveEditor.Desktop.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:TreatWarningsAsErrors=false \
    -o "${APP_DIR}/usr/bin"

# Rename executable to match launcher
mv "${APP_DIR}/usr/bin/StalkerSaveEditor.Desktop" "${APP_DIR}/usr/bin/stalker-save-editor"
chmod +x "${APP_DIR}/usr/bin/stalker-save-editor"

# 3. Setup AppDir metadata
cp "${ROOT}/packaging/linux/AppRun" "${APP_DIR}/AppRun"
chmod +x "${APP_DIR}/AppRun"

cp "${ROOT}/packaging/linux/stalker-save-editor.desktop" "${APP_DIR}/stalker-save-editor.desktop"
cp "${ROOT}/packaging/linux/stalker-save-editor.png" "${APP_DIR}/stalker-save-editor.png"
cp "${ROOT}/packaging/linux/stalker-save-editor.png" "${APP_DIR}/.DirIcon"

# 4. Pack AppImage using appimagetool
APPIMAGE_BIN="${OUTPUT_DIR}/StalkerSaveEditor-${VERSION}-${ARCH}.AppImage"

if ! command -v appimagetool &> /dev/null; then
    echo "appimagetool not found in PATH, downloading standalone tool..."
    TOOL_URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
    curl -sSL -o "${BUILD_DIR}/appimagetool" "${TOOL_URL}" || true
    if [ -f "${BUILD_DIR}/appimagetool" ]; then
        chmod +x "${BUILD_DIR}/appimagetool"
        ARCH=x86_64 "${BUILD_DIR}/appimagetool" "${APP_DIR}" "${APPIMAGE_BIN}"
    else
        echo "Warning: Could not download appimagetool; AppDir prepared at ${APP_DIR}"
        exit 0
    fi
else
    ARCH=x86_64 appimagetool "${APP_DIR}" "${APPIMAGE_BIN}"
fi

echo "AppImage created: ${APPIMAGE_BIN}"
