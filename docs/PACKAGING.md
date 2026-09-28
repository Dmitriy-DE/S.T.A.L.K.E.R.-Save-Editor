# Cross-Platform Packaging & Distribution

This document details the packaging and distribution pipeline for the S.T.A.L.K.E.R. Save Editor desktop application across Linux, Windows, and macOS.

---

## 1. Distribution Formats

| Operating System | Package Type | Target File | Build Tool |
|---|---|---|---|
| **Linux (Debian / Ubuntu)** | Debian Package | `stalker-save-editor_<version>_amd64.deb` | `dpkg-deb` / `packaging/linux/build_deb.sh` |
| **Linux (Universal)** | Portable AppImage | `StalkerSaveEditor-<version>-x86_64.AppImage` | `appimagetool` / `packaging/linux/build_appimage.sh` |
| **Windows** | Portable Executable | `StalkerSaveEditor.exe` | `dotnet publish` (`PublishSingleFile=true`) |
| **Windows** | Standard Installer | `StalkerSaveEditor-<version>-Setup.exe` | Inno Setup 6 (`packaging/windows/installer.iss`) |
| **macOS** | Application Bundle | `StalkerSaveEditor.app` | `packaging/macos/build_macos.sh` |
| **macOS** | Disk Image | `StalkerSaveEditor-<version>.dmg` | `hdiutil` / `genisoimage` |

---

## 2. Directory Structure

```
packaging/
├── linux/
│   ├── AppRun                         # AppImage startup launcher
│   ├── build_appimage.sh              # Universal AppImage generator
│   ├── build_deb.sh                   # Debian package builder
│   ├── stalker-save-editor.desktop    # XDG Desktop Entry
│   └── stalker-save-editor.png        # Application 256x256 icon
├── windows/
│   ├── build_windows.ps1              # Native PowerShell build script
│   ├── build_windows.sh               # Bash/WSL cross-publish script
│   └── installer.iss                  # Inno Setup 6 compilation script
└── macos/
    ├── build_macos.sh                 # macOS bundle and DMG generator
    └── Info.plist                     # Apple application bundle metadata
```

---

## 3. Building Locally

### Linux (.deb and AppImage)

Prerequisites: `.NET 10 SDK`, `dpkg-deb`, `appimagetool` (for AppImage).

```bash
# Build Debian package
./packaging/linux/build_deb.sh 1.4.0

# Build universal AppImage
./packaging/linux/build_appimage.sh 1.4.0
```

The resulting packages will be placed in `artifacts/packages/`.

### Windows (.exe and Inno Setup Installer)

Prerequisites: `.NET 10 SDK`, Inno Setup 6 (`ISCC.exe`).

On Windows (PowerShell):
```powershell
.\packaging\windows\build_windows.ps1 -Version "1.4.0"
```

On Linux (Cross-publish single-file .exe):
```bash
./packaging/windows/build_windows.sh 1.4.0
```

> [!NOTE]
> Single-file publishing uses `-p:TreatWarningsAsErrors=false` because the Steam worker runner (`SteamWorkerProcessRunner.cs`) uses `Assembly.Location` which raises warning `IL3000` during single-file bundling.

### macOS (.app and .dmg)

Prerequisites: `.NET 10 SDK`, macOS with `hdiutil` (or Linux with `genisoimage`).

```bash
./packaging/macos/build_macos.sh 1.4.0
```

---

## 4. Automated CI/CD Release Pipeline

The GitHub Actions workflow at [`.github/workflows/release-packages.yml`](../.github/workflows/release-packages.yml) automatically builds and publishes all distribution artifacts:

- **Triggers**:
  - Push on version tags: `git tag v1.4.0 && git push origin v1.4.0`
  - Manual execution via `workflow_dispatch`
- **Matrix**:
  - `ubuntu-24.04`: Builds `.deb` and `.AppImage`
  - `windows-latest`: Builds portable `StalkerSaveEditor.exe` and compiles Inno Setup installer
  - `macos-14`: Builds `StalkerSaveEditor.app` and `StalkerSaveEditor-<version>.dmg`
- **Output**: Automatically attaches compiled packages to the GitHub Release.

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
