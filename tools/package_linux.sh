#!/usr/bin/env bash
# S.T.A.L.K.E.R. Save Editor - Linux Packaging Script (.NET 10 / Avalonia)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_PATH="${ROOT_DIR}/src/StalkerSaveEditor.Desktop/StalkerSaveEditor.Desktop.csproj"
OUT_BASE="${ROOT_DIR}/artifacts/dist"

MODE="${1:-single-file}"

mkdir -p "${OUT_BASE}"

case "${MODE}" in
  "dir"|"directory")
    echo "==> Building Linux self-contained directory..."
    TARGET_DIR="${OUT_BASE}/linux-x64-dir"
    rm -rf "${TARGET_DIR}"
    dotnet publish "${PROJECT_PATH}" \
      -c Release \
      -r linux-x64 \
      --self-contained true \
      -o "${TARGET_DIR}"
    echo "==> Successfully published to: ${TARGET_DIR}"
    du -sh "${TARGET_DIR}"
    ;;

  "single"|"single-file")
    echo "==> Building Linux self-contained single-file executable..."
    TARGET_DIR="${OUT_BASE}/linux-x64-single"
    rm -rf "${TARGET_DIR}"
    dotnet publish "${PROJECT_PATH}" \
      -c Release \
      -r linux-x64 \
      --self-contained true \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:NoWarn=IL3000 \
      -o "${TARGET_DIR}"
    echo "==> Successfully published to: ${TARGET_DIR}"
    ls -lh "${TARGET_DIR}/StalkerSaveEditor.Desktop"
    ;;

  "aot"|"native-aot")
    echo "==> Building Linux NativeAOT binary..."
    TARGET_DIR="${OUT_BASE}/linux-x64-aot"
    rm -rf "${TARGET_DIR}"
    dotnet publish "${PROJECT_PATH}" \
      -c Release \
      -r linux-x64 \
      -p:PublishAot=true \
      -p:TreatWarningsAsErrors=false \
      -o "${TARGET_DIR}"
    echo "==> Successfully published to: ${TARGET_DIR}"
    ls -lh "${TARGET_DIR}/StalkerSaveEditor.Desktop"
    ;;

  *)
    echo "Usage: $0 [directory|single-file|native-aot]"
    exit 1
    ;;
esac
