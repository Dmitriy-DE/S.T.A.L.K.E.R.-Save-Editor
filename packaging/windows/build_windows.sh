#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="${1:-1.4.0}"
DIST="${ROOT}/dist"
WIN_DIST="${ROOT}/packaging/windows/dist"

echo "=== Publishing S.T.A.L.K.E.R. Save Editor Windows binary v${VERSION} ==="

rm -rf "${WIN_DIST}"
mkdir -p "${WIN_DIST}"
mkdir -p "${DIST}"

# Suppress IL3000 (Assembly.Location warning in SteamWorkerProcessRunner) for single-file publish while keeping TreatWarningsAsErrors=true
dotnet publish "${ROOT}/src/StalkerSaveEditor.Desktop/StalkerSaveEditor.Desktop.csproj" \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:NoWarn="IL3000" \
    -o "${WIN_DIST}"

cp "${WIN_DIST}/StalkerSaveEditor.Desktop.exe" "${DIST}/StalkerSaveEditor-v${VERSION}-win-x64.exe"
echo "Published Windows executable: ${DIST}/StalkerSaveEditor-v${VERSION}-win-x64.exe"
