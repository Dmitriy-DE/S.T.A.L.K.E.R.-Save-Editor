#!/usr/bin/env bash
# .deb: the application folder in /usr/lib/stalker-save-editor (the updater recognises this root),
# launchers in /usr/bin, desktop entry and icon.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="${1:?version}"
ARCH="amd64"
OUTPUT_DIR="${ROOT}/dist"
PKG_ROOT="${ROOT}/build/deb/stalker-save-editor_${VERSION}_${ARCH}"
APP="${PKG_ROOT}/usr/lib/stalker-save-editor"

rm -rf "${ROOT}/build/deb"
mkdir -p "${PKG_ROOT}/DEBIAN" "${PKG_ROOT}/usr/bin" "${PKG_ROOT}/usr/share/applications" \
  "${PKG_ROOT}/usr/share/icons/hicolor/256x256/apps" "${OUTPUT_DIR}"

"${ROOT}/packaging/publish_app.sh" linux-x64 "${APP}" "${VERSION}"
ln -s ../lib/stalker-save-editor/StalkerSaveEditor "${PKG_ROOT}/usr/bin/stalker-save-editor"
ln -s ../lib/stalker-save-editor/stalker-save-editor-cli "${PKG_ROOT}/usr/bin/stalker-save-editor-cli"

cat > "${PKG_ROOT}/DEBIAN/control" <<CONTROL
Package: stalker-save-editor
Version: ${VERSION}
Section: games
Priority: optional
Architecture: ${ARCH}
Maintainer: Dmitriy-DE <Dmitriy-DE@users.noreply.github.com>
Depends: libc6, libgcc-s1, libstdc++6, libfontconfig1, libice6, libsm6, libx11-6
Homepage: https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor
Description: Save editor and game companion for S.T.A.L.K.E.R.
 Save editor for Shadow of Chernobyl, Clear Sky, Call of Pripyat (including the
 Enhanced Editions) and S.T.A.L.K.E.R. 2, with verified backups, Steam Cloud
 access and the in-game companion mod.
CONTROL

cp "${ROOT}/packaging/linux/stalker-save-editor.desktop" "${PKG_ROOT}/usr/share/applications/"
cp "${ROOT}/packaging/linux/stalker-save-editor.png" "${PKG_ROOT}/usr/share/icons/hicolor/256x256/apps/"

DEB_FILE="${OUTPUT_DIR}/stalker-save-editor_${VERSION}_${ARCH}.deb"
dpkg-deb --build --root-owner-group "${PKG_ROOT}" "${DEB_FILE}"
echo "DEB package created: ${DEB_FILE}"
