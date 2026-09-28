# Cross-Platform Packaging & Distribution

This document details the packaging and distribution pipeline for the S.T.A.L.K.E.R. Save Editor desktop application across Linux, Windows, and macOS.

---

## 1. Distribution Formats

| Operating System | Package Type | Target File | Build Tool |
|---|---|---|---|
| **Linux (Debian / Ubuntu)** | Debian Package | `stalker-save-editor_<version>_amd64.deb` | `dpkg-deb` / `packaging/linux/build_deb.sh` |
| **Linux (Universal)** | Portable AppImage | `StalkerSaveEditor-<version>-x86_64.AppImage` | `appimagetool` / `packaging/linux/build_appimage.sh` |
| **Windows** | Portable ZIP | `StalkerSaveEditor-v<version>-windows-x64.zip` | `packaging/windows/build_windows.ps1` |
| **Windows** | Standard Installer | `StalkerSaveEditor-Setup-<version>-x64.exe` | Inno Setup 6 (`packaging/windows/installer.iss`) |
| **macOS** | Application Bundle | `StalkerSaveEditor.app` | `packaging/macos/build_macos.sh` |
| **macOS** | Disk Image | `StalkerSaveEditor-<version>.dmg` | `hdiutil` / `genisoimage` |

---

## 2. Directory Structure

```
packaging/
├── publish_app.sh                    # Shared app + CLI + native library + assets + Companion publisher
├── linux/
│   ├── AppRun                         # AppImage startup launcher
│   ├── build_appimage.sh              # Universal AppImage generator
│   ├── build_deb.sh                   # Debian package builder
│   ├── stalker-save-editor.desktop    # XDG Desktop Entry
│   └── stalker-save-editor.png        # Application 256x256 icon
├── windows/
│   ├── build_windows.ps1              # Publishes app folder, portable ZIP and installer
│   └── installer.iss                  # Inno Setup 6 compilation script
└── macos/
    ├── build_macos.sh                 # macOS bundle and DMG generator
    └── Info.plist                     # Apple application bundle metadata
```

---

## 3. Building Locally

`packaging/publish_app.sh <rid> <output-directory> <version>` publishes the desktop app, NativeAOT
CLI, Kraken library, assets, Companion files and `BUILD_MANIFEST.json`. Supported runtime IDs are
`win-x64`, `linux-x64`, `osx-arm64` and `osx-x64`. It replaces the chosen output directory and may
build the vendored Kraken native library under `artifacts/native/` if that library is absent.

### Linux (.deb and AppImage)

Prerequisites: `.NET 10 SDK`, `dpkg-deb`, `appimagetool` (for AppImage).

```bash
# Build Debian package
./packaging/linux/build_deb.sh 1.4.0

# Build universal AppImage
./packaging/linux/build_appimage.sh 1.4.0
```

Packages are written under `dist/`. The AppImage build also creates the portable Linux tarball used
by the updater.

### Windows (.exe and Inno Setup Installer)

Prerequisites: `.NET 10 SDK`, Bash (Git for Windows), Python 3, the native Kraken build toolchain if
the binary is absent, and Inno Setup 6 (`ISCC.exe`).

On Windows (PowerShell):
```powershell
.\packaging\windows\build_windows.ps1 -Version "1.4.0"
```

The publisher can also be called directly for a Windows application folder; the build script below
adds the ZIP and Inno Setup installer.

```bash
./packaging/publish_app.sh win-x64 /tmp/stalker-save-editor-win 1.4.0
```

> [!NOTE]
> The shared publisher suppresses warning `IL3000` for the desktop single-file publish because the Steam worker runner (`SteamWorkerProcessRunner.cs`) uses `Assembly.Location`. NativeAOT warnings from the CLI remain errors.

The installer components are the app (required), CLI and Companion mod. Game Fixes are not an
installer component; fixes are installed, updated or removed explicitly through the app or CLI after
the user selects a game folder.

### macOS (.app and .dmg)

Prerequisites: `.NET 10 SDK`, macOS with `hdiutil` (or Linux with `genisoimage`).

```bash
./packaging/macos/build_macos.sh 1.4.0
```

---

## 4. Automated CI/CD Release Pipeline

The GitHub Actions workflow at [`.github/workflows/release-packages.yml`](../.github/workflows/release-packages.yml) builds the distribution artifacts:

- **Triggers**:
  - Push on version tags: `git tag v1.4.0 && git push origin v1.4.0`
  - Manual execution via `workflow_dispatch`
- **Runners**:
  - `ubuntu-latest`: Builds `.deb` and `.AppImage`
  - `windows-latest`: Builds the portable ZIP and Inno Setup installer
  - `macos-latest`: Builds arm64 and x64 macOS archives/disk images
- **Output**: Tag builds create a GitHub Release after packaging. Manual runs upload build artifacts; they publish a release only when run against a version tag and `dry_run` is false.

## Release publication (tag `vX.Y.Z`)

`.github/workflows/release-packages.yml` builds all packages, then the `release` job:

1. `tools/release/publish_release.py` copies the build outputs to stable names, writes `latest.json`
   (the manifest `StalkerSaveEditor.Updater` reads) and `SHA256SUMS`;
2. `tools/release/build_apt_repo.py` builds and signs the APT repository with the **existing** key
   (`APT_SIGNING_KEY`, `APT_SIGNING_KEY_ID`) — skipped when the secrets are absent;
3. uploads everything to R2 (`CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`) with `latest.json` last,
   then reads every object back and compares bytes — skipped with a notice when the secrets are absent;
4. creates the GitHub Release with the same files.

Secrets are set by the owner. Local dry run:

```bash
python3 tools/release/publish_release.py --artifacts dist --version 1.0.0 --commit "$(git rev-parse HEAD)" --output release-output
```
