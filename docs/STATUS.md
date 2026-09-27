# Project Status & Verification Levels (L1–L5)

This document tracks the verification status and implementation readiness of the modern C# S.T.A.L.K.E.R. Save Editor (`StalkerSaveEditor`), aligned with the [RL-1 reliability roadmap](roadmap/RL-reliability.md).

## Verification Level Definitions

- **L1 IMPLEMENTED**: Code exists, builds without errors or warnings.
- **L2 TESTED**: Unit tests, regression tests, positive and negative checks pass in CI.
- **L3 PACKAGED**: Feature verified inside built release packages (.deb, AppImage, single-file .exe, .dmg).
- **L4 RUNTIME**: Verified in real runtime environments (desktop GUI, cross-platform audio engines, filesystem I/O).
- **L5 GAME**: Verified against running game engine instances (save accepted, round-tripped, companion protocol active).

---

## Component Status Summary

| Area | Component | Verification Level | Scope & Notes |
|---|---|---|---|
| **Core** | X-Ray Reader (SoC / CS / CoP / EE) | **L2** | Full header, actor registry, inventory, ammo stacks, stashes parsing. Tested against golden fixtures. |
| **Core** | X-Ray Writer (SoC / CS / CoP / EE) | **L2** | Money modification, confirmed ammo stack counts, safe item additions/deletions with CRC32 preservation. |
| **Core** | S.T.A.L.K.E.R. 2 Reader | **L2** | Kraken/CRC container decompression, inventory layout, name tables, stash trees. **Strictly read-only (safety rule)**. |
| **Core** | X-Ray Archives (.db / .xdb / .xrp) | **L2** | FAT CRC checks, recursive archive extraction, asset stream resolution. |
| **Steam** | Steam RemoteStorage Worker | **L2** | Isolated out-of-process worker, 15-second timeout, read/list operations. Tested via mock IPC. |
| **Desktop** | Screen Parity & Theme (B1) | **L4** | Full parity with reference UI: Overview, Inventory, Stashes, Factions, Level Transitions, Backups, Settings. Industrial dark theme. |
| **Desktop** | Draft Store & Rollback (B1, B3) | **L4** | Non-destructive edits, Undo/Redo stack, draft journal, auto-backup before write. |
| **Audio** | Interface Sound Service (B2) | **L4** | Cross-platform audio (winmm on Windows, pw-play/paplay/aplay on Linux, afplay on macOS). 6 UI sound events, volume & mute controls. |
| **Localization** | 15-Language Engine (B7) | **L4** | 15 locales (1,327 entries each), Russian string gettext key, `target -> ru -> en` fallback chain, 0 placeholder mismatches. |
| **Companion** | In-Game Mod (G6) | **L2** | Universal Lua script for SoC, CS, CoP & EE. Protocol v1 + extended commands (`mark`, `jump_last`, `quicksave`, `hotkeys on`, `POLL_MS = 250`). |
| **Companion** | Desktop UI & Hotkeys (B4-UI) | **L4** | Live status monitoring, ping, install/uninstall hooks manager, hotkey configuration. |
| **Packaging** | Linux Packages (D1) | **L3** | Debian package (.deb) and portable AppImage. Tested and verified locally on Ubuntu 24.04. |
| **Packaging** | Windows Packages (D2) | **L3** | Portable single-file .exe (`PublishSingleFile`) and Inno Setup installer script (`packaging/windows/installer.iss`). |
| **Packaging** | macOS Packages (D3) | **L3** | Application bundle (.app) and Apple Disk Image (.dmg) generation script (`packaging/macos/build_macos.sh`). |
| **CI / CD** | Automated Packaging & Checks (D5, E7) | **L2** | Tag-triggered release workflow (`release-packages.yml`) and companion Lua syntax check workflow (`companion-check.yml`). |

---

## Active Issues & Pending Core APIs

- [Issue #55](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save_Editor/issues/55): Level changers mutation API in `XRayTrilogySave` (Desktop UI currently stubs mutation while displaying read-only data).
- [Issue #59](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save_Editor/issues/59): `ICompanionService` in `StalkerSaveEditor.Core` (Desktop UI operates through `MockCompanionService` until Core companion service is integrated).
