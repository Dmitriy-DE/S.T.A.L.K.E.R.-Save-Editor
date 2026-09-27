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
# Suppress IL3000 (Assembly.Location warning in SteamWorkerProcessRunner) for single-file publish while keeping TreatWarningsAsErrors=true
echo "Publishing .NET project for linux-x64..."
dotnet publish "${ROOT}/src/StalkerSaveEditor.Desktop/StalkerSaveEditor.Desktop.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:NoWarn="IL3000" \
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

# 4. Pack AppImage using pinned appimagetool version
APPIMAGE_BIN="${OUTPUT_DIR}/StalkerSaveEditor-${VERSION}-${ARCH}.AppImage"

if ! command -v appimagetool &> /dev/null; then
    APPIMAGETOOL_VERSION="1.9.1"
    echo "appimagetool not found in PATH, downloading pinned release v${APPIMAGETOOL_VERSION}..."
    TOOL_URL="https://github.com/AppImage/appimagetool/releases/download/${APPIMAGETOOL_VERSION}/appimagetool-x86_64.AppImage"
    curl -fsSL -o "${BUILD_DIR}/appimagetool" "${TOOL_URL}" || {
        echo "Error: Failed to download appimagetool v${APPIMAGETOOL_VERSION} from ${TOOL_URL}" >&2
        exit 1
    }
    chmod +x "${BUILD_DIR}/appimagetool"
    APPIMAGETOOL_CMD="${BUILD_DIR}/appimagetool --appimage-extract-and-run"
else
    APPIMAGETOOL_CMD="appimagetool"
fi

echo "Generating AppImage..."
ARCH=x86_64 ${APPIMAGETOOL_CMD} "${APP_DIR}" "${APPIMAGE_BIN}"

if [ ! -f "${APPIMAGE_BIN}" ]; then
    echo "Error: AppImage packaging failed; target file ${APPIMAGE_BIN} was not generated." >&2
    exit 1
fi

echo "AppImage created successfully: ${APPIMAGE_BIN}"
