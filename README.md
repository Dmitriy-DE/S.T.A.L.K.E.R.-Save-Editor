# S.T.A.L.K.E.R. Save Editor — Next (C#)

[![CI](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/actions/workflows/ci.yml/badge.svg)](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/actions/workflows/ci.yml)

High-performance, cross-platform **.NET 10** desktop save editor and live in-game companion for the entire **S.T.A.L.K.E.R.** series:
- **S.T.A.L.K.E.R.: Shadow of Chernobyl** (1.0004 / 1.0006)
- **S.T.A.L.K.E.R.: Clear Sky** (1.5.10)
- **S.T.A.L.K.E.R.: Call of Pripyat** (1.6.02)
- **Enhanced Editions** (SoC, CS, CoP)
- **S.T.A.L.K.E.R. 2: Heart of Chornobyl** (UE5)

![S.T.A.L.K.E.R. Save Editor](docs/images/inventory.png)

---

## What It Can Do

Every write goes through the same checked path: the change is prepared, the file is re-read and
verified, and a backup of the original is kept outside the save folder. Fields whose meaning is not
proven stay read-only; the **Capabilities** tab shows, per game, what can be written.

- **Library** — finds saves of all games (Steam, Proton, GOG, disc installs) and shows the game's own
  slot screenshot, level or S.T.A.L.K.E.R. 2 region and play time; «Open…» opens any file.
- **Overview** — money, actor name, health, rank, level, in-game date, item and stash counts; compare
  the save with another save of the same game or with one of its backups.
- **Inventory (X-Ray)** — money, stack counts, condition, slot/belt/backpack placement, weapon and armour
  upgrades, adding items from the catalogue and removing items. Item names and icons come from the
  installed game (mods such as OGSM included) or from the shipped pack.
- **Stashes, factions, level changers** — take loot out of stashes, change faction goodwill; level
  changers are read-only.
- **S.T.A.L.K.E.R. 2** — reading, official item names and icons; writing stays off in the interface
  until changes are confirmed in the game.
- **Drafts** — edits are kept as a draft with undo/redo (`Ctrl+Z`, `Ctrl+Y`) and written with `Ctrl+S`.
- **Steam Cloud and achievements** — compare local and cloud saves, download, upload only after an
  explicit confirmation (never retried automatically); view and toggle achievements.
- **Companion mod** — installs into every SoC/CS/CoP found and gives an in-game menu (Esc → F1) with
  live cheats, teleport marks, weather and more; the app talks to it through a file protocol.
- **Interface** — 14 languages, the games' own menu sounds and optional main-menu music, crash report
  and «Check environment» under Settings → Diagnostics.
- **Web edition** — the same interface in the browser (WebAssembly); files never leave the browser.
- **S.T.A.L.K.E.R. 2 companion (experimental)** — a UE4SS mod, installed only on request; it logs
  everything it does so problems can be fixed.

### Reports

To find bugs, the desktop editor sends its log to the developer once a day and right after a crash
(the S.T.A.L.K.E.R. 2 mod's log is included). Paths, user names and Steam IDs are removed; saves are
never sent. Nothing is sent until you have seen the notice on the first start, and it can be switched
off there or in Settings → Diagnostics.

## How to Install & Run

### Download Pre-Built Releases

Download the latest release package for your operating system from [Releases](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/releases):

| Platform | Package | How to install |
|---|---|---|
| **Windows** | `StalkerSaveEditor-Setup-<version>-x64.exe` | Run it. Components: the editor (always), the command-line tool, and the companion mod into every S.T.A.L.K.E.R. game found (Steam, GOG, disc). |
| **Windows** | `StalkerSaveEditor-v<version>-windows-x64.zip` | Portable: unpack anywhere and run `StalkerSaveEditor.exe`. |
| **Linux** | `stalker-save-editor_<version>_amd64.deb` | `sudo apt install ./stalker-save-editor_<version>_amd64.deb` — installs to `/usr/lib/stalker-save-editor`, commands `stalker-save-editor` and `stalker-save-editor-cli`. |
| **Linux** | `StalkerSaveEditor-<version>-x86_64.AppImage` | `chmod +x` and run. |
| **Linux** | `StalkerSaveEditor-v<version>-linux-x64.tar.gz` | Portable: unpack and run `./StalkerSaveEditor`. |
| **macOS** | `.dmg` | Open and drag `StalkerSaveEditor.app` to Applications. |

Every package contains the companion mod; the app (Companion → «Все игры»), the Windows installer
and `stalker-save-editor-cli companion install all` put it into the games. The mod also patches a few
game scripts, so it is not offered as a plain archive.

#### Windows SmartScreen

The Windows builds are not code-signed, so SmartScreen may say *"Windows protected your PC"* the
first time. Click **More info → Run anyway**. Check the file first if you like: its SHA-256 is listed
in `SHA256SUMS` next to the download (`Get-FileHash .\StalkerSaveEditor-Setup-*.exe`).

---

### Building from Source

#### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/)
- Python 3.10+ (for helper tools and codecs)
- Lua 5.1 (`luac5.1` or `luac`) for companion verification

#### Build Solution
```bash
# Clone repository
git clone https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next.git
cd S.T.A.L.K.E.R.-Save-Editor-Next

# Build release configuration
dotnet build --configuration Release -warnaserror
```

#### Run Tests & Checks
```bash
# Execute unit and ViewModel test suite
dotnet test

# Validate companion mod Lua scripts and binder patch syntax
./tools/check_companion.sh

# Verify i18n coverage across all 14 locales
dotnet run --project src/StalkerSaveEditor.App -- --test-i18n

# Verify audio sound asset integrity
dotnet run --project src/StalkerSaveEditor.App -- --test-audio
```

#### Launch the Desktop Editor
```bash
dotnet run --project src/StalkerSaveEditor.App
```

---

## Verification Levels & Reliability (L1–L5)

In accordance with our [Reliability Charter](docs/roadmap/RL-reliability.md), every feature is categorized by its verified confidence level:

- **L1 (Synthetic Round-Trip)**: Bit-level serializer/parser round-trips and checksum verification on synthetic fixtures.
- **L2 (Synthetic UI Headless & Test Suite)**: ViewModel unit tests, Avalonia headless UI rendering, Lua 5.1 syntax checks, and complete i18n audits.
- **L3 (Standalone Application & Packaging)**: Validated execution of compiled native bundles (`.AppImage`, `.deb`, `.exe`, `.dmg`) with bundled assets.
- **L4 (Game-Accepted Output)**: Save mutations loaded and saved without error in retail game engines.
- **L5 (Steam Cloud & Production Certified)**: End-to-end verified with retail Steam Cloud and retail games.

---

## Documentation Links

- [Project Roadmap & State](docs/roadmap/STATE.md)
- [Reliability & Verification Levels (L1–L5)](docs/roadmap/RL-reliability.md)
- [In-Game Companion Mod & Protocol Guide](docs/COMPANION.md)
- [Companion Protocol Specification (v1)](docs/MOD_COMPANION_PROTOCOL.md)
- [Cross-Platform Packaging & Distribution Guide](docs/PACKAGING.md)
- [Local Save Editing & Recovery Flow](docs/CS6_LOCAL_EDITING.md)
- [Architecture Guidelines](ARCHITECTURE.md)
- [Safety Rules & Agent Guidelines](AGENTS.md)
