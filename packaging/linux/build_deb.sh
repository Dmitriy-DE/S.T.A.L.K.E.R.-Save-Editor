#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="${1:-1.4.0}"
ARCH="amd64"
OUTPUT_DIR="${ROOT}/dist"
PKG_ROOT="${ROOT}/build/deb/stalker-save-editor_${VERSION}_${ARCH}"

echo "=== Building S.T.A.L.K.E.R. Save Editor .deb package v${VERSION} (${ARCH}) ==="

# 1. Clean previous build
rm -rf "${ROOT}/build/deb"
mkdir -p "${PKG_ROOT}/DEBIAN"
mkdir -p "${PKG_ROOT}/usr/bin"
mkdir -p "${PKG_ROOT}/usr/share/applications"
mkdir -p "${PKG_ROOT}/usr/share/icons/hicolor/256x256/apps"
mkdir -p "${OUTPUT_DIR}"

# 2. Control file
cat << EOF > "${PKG_ROOT}/DEBIAN/control"
Package: stalker-save-editor
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: ${ARCH}
Maintainer: Dmitriy-DE <dmitriy@example.com>
Depends: libc6, libgcc-s1, libstdc++6
Description: S.T.A.L.K.E.R. Save Editor & Game Companion
 Save editor and real-time companion for the S.T.A.L.K.E.R. original trilogy
 (Shadow of Chernobyl, Clear Sky, Call of Pripyat) and S.T.A.L.K.E.R. 2:
 Heart of Chornobyl. Supports inventory mutation, fast travel, stashes,
 relation editing, and automated verified backups.
EOF

# 3. Publish .NET self-contained single file
echo "Publishing .NET project for linux-x64..."
dotnet publish "${ROOT}/src/StalkerSaveEditor.Desktop/StalkerSaveEditor.Desktop.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:TreatWarningsAsErrors=false \
    -o "${PKG_ROOT}/usr/bin"

mv "${PKG_ROOT}/usr/bin/StalkerSaveEditor.Desktop" "${PKG_ROOT}/usr/bin/stalker-save-editor"
chmod 755 "${PKG_ROOT}/usr/bin/stalker-save-editor"

# 4. Install desktop entry and icons
cp "${ROOT}/packaging/linux/stalker-save-editor.desktop" "${PKG_ROOT}/usr/share/applications/"
cp "${ROOT}/packaging/linux/stalker-save-editor.png" "${PKG_ROOT}/usr/share/icons/hicolor/256x256/apps/"

# 5. Build .deb package
DEB_FILE="${OUTPUT_DIR}/stalker-save-editor_${VERSION}_${ARCH}.deb"
dpkg-deb --build --root-owner-group "${PKG_ROOT}" "${DEB_FILE}"

echo "DEB package created: ${DEB_FILE}"
