#!/usr/bin/env bash
# Publishes the complete application folder for one runtime:
#   <out>/StalkerSaveEditor[.exe]           desktop app (self-contained single file, Kraken library inside)
#   <out>/stalker-save-editor-cli[.exe]     CLI (NativeAOT) — also installs the companion mod for Setup
#   <out>/stalker_ooz / libstalker_ooz.*    Kraken library for the CLI
#   <out>/Assets/                           icons, sounds, fonts' licences, PROVENANCE.json
#   <out>/mods/companion/                   the companion mod
#   <out>/BUILD_MANIFEST.json               what the updater reads (target, architecture, version)
# usage: packaging/publish_app.sh <rid: win-x64|linux-x64|osx-arm64|osx-x64> <out> <version>
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RID="${1:?rid}"
OUT="${2:?output directory}"
VERSION="${3:?version}"

case "$RID" in
  win-*) EXE=".exe"; TARGET="windows"; NATIVE="stalker_ooz.dll" ;;
  osx-*) EXE=""; TARGET="macos"; NATIVE="libstalker_ooz.dylib" ;;
  *) EXE=""; TARGET="linux"; NATIVE="libstalker_ooz.so" ;;
esac
case "$RID" in
  *-arm64) ARCH="arm64" ;;
  *) ARCH="x86_64" ;;
esac

# Kraken (S.T.A.L.K.E.R. 2 saves) from the vendored ooz sources; universal on macOS.
if [[ ! -f "$ROOT/artifacts/native/$NATIVE" ]]; then
  python3 "$ROOT/tools/build_ooz_native.py" --output-dir "$ROOT/artifacts/native"
fi

rm -rf "$OUT"
mkdir -p "$OUT"
dotnet publish "$ROOT/src/StalkerSaveEditor.App/StalkerSaveEditor.App.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:NoWarn=IL3000 \
  -p:Version="$VERSION" -o "$OUT"

cli_out="$(mktemp -d "${TMPDIR:-$ROOT/build}/cli.XXXXXX")"
dotnet publish "$ROOT/src/StalkerSaveEditor.Cli/StalkerSaveEditor.Cli.csproj" \
  -c Release -r "$RID" -p:PublishAot=true -p:Version="$VERSION" -o "$cli_out"
cp "$cli_out/StalkerSaveEditor.Cli$EXE" "$OUT/stalker-save-editor-cli$EXE"
cp "$ROOT/artifacts/native/$NATIVE" "$OUT/"
rm -rf "$cli_out"

rm -f "$OUT"/*.pdb "$OUT"/*.dbg
mkdir -p "$OUT/mods"
cp -r "$ROOT/mods/companion" "$OUT/mods/companion"
printf '{\n  "target": "%s",\n  "architecture": "%s",\n  "version": "%s"\n}\n' "$TARGET" "$ARCH" "$VERSION" > "$OUT/BUILD_MANIFEST.json"
echo "$OUT"
