# S.T.A.L.K.E.R. Save Editor — architecture

The archived Python editor (`Dmitriy-DE/S.T.A.L.K.E.R.-Save_Editor`) was the oracle for the port: readers and writers
reproduce its results on the vectors in `tests/Fixtures` and `tests/golden` (175 scenarios, `docs/PARITY.csv`). The
generators under `tools/generate_*_fixtures.py` record where each vector came from; they need that repository to run.

## Stack

- .NET 10 (LTS), C# latest, nullable enabled, warnings as errors.
- Avalonia 11.x, MVVM with the project's own small base (`ObservableViewModel`, `RelayCommand`); no MVVM toolkit package.
  Screens are built in C# (`Views/*View.cs`), screen ids are `AppTabs`.
- xUnit for tests.
- CI on Windows, Linux (Ubuntu 24.04) and macOS (arm64) from the first commit.

## Projects

```text
src/
  StalkerSaveEditor.Core/          no UI and no Steam; narrow filesystem boundaries for saves and game files
    Formats/XRay/                  container (LZO), ALIFE chunks, actor, inventory, writers, game archives
    Formats/Enhanced/              Enhanced Editions: the X-Ray container with their own ALIFE versions
    Formats/Stalker2/              Kraken container, name tables, inventory, stash, writers
    Codecs/                        Kraken (P/Invoke to the bundled native ooz), LZO1X (managed)
    Editing/                       EditPlan, PreparedEdit, EditService, drafts
    Catalogs/                      item names, official names, S2 items
    Capabilities/                  what can be read and written per release, with its maturity
    Backups/                       backup journal, local save replacement, recovery
    Storage/                       save folders, Steam libraries, atomic file writes
    Inspection/                    save summary and preview
    Content/                       files of an installed game: archive tree, LTX, string tables, DDS icons
    Companion/                     installer of the in-game mod, hook patcher, file protocol client
    Hotkeys/                       global hotkeys while the game window is in front (Windows; X11 helper process)
    Patching/                      Game Fix catalogue and engine, all.spawn editor, snapshots, profiles, user.ltx
    Diagnostics/                   Game Doctor, Save Doctor, Quest Doctor, crash logs, app log
  StalkerSaveEditor.Steam/         worker process around steam_api: RemoteStorage, UserStats, Auto-Cloud
  StalkerSaveEditor.Updater/       update check, signed manifest, download, install per platform
  StalkerSaveEditor.Desktop/       Avalonia UI library (views, view models, 15 languages), shared with the web
                                   edition; knows Steam and the updater only as interfaces (HostPlatform factories)
  StalkerSaveEditor.Host/          desktop-only services behind those interfaces: Steam cloud, achievements,
                                   self-update; referenced by the App, never by the Browser host
  StalkerSaveEditor.App/           desktop executable; also the Steam worker and the X11 hotkey helper entry points
  StalkerSaveEditor.Browser/       WebAssembly host of the same UI (not in the .sln)
  StalkerSaveEditor.Cli/           NativeAOT command line: saves, doctor, fixes, companion, crash logs
mods/companion/                    the in-game mod: soc, cs, cop (Lua), s2 (UE4SS)
tests/
  StalkerSaveEditor.Core.Tests/    formats, editing, patching, diagnostics, view models
  StalkerSaveEditor.Steam.Tests/   fakes only; never a live Steam session in CI
  StalkerSaveEditor.Cli.Tests/
tools/                             fixture generators, companion generators and checks, Game Fix research tools
                                   (static checkers, fix_regress.py, fix_realcheck.sh, lua_harness/), release scripts
```

Core has no reference to Steam or the UI: local files go through
`Backups/LocalSaveStorage` and `LocalSaveReplacement`, Steam Cloud through the
separate `StalkerSaveEditor.Steam` project (worker process). Detection is by
content, never by path (AGENTS rule).

`Editing/DraftStore` keeps undoable, unapplied `EditPlan` snapshots under the
application data directory, keyed by the source save's lowercase SHA256. Its
schema 2 stores only the plan and hash, never a save path. It can import the
Python draft schema 1. Legacy edits that the current C# plan cannot represent
are preserved as opaque JSON and disable applying or extending that snapshot
until the caller explicitly discards them.
Draft files are replaced atomically, limited to 2 MiB, and removed when the
current plan is empty.

Installed-game changes use Core/Patching, separate from save mutation. Companion and Game Fixes share
the same filesystem boundary and sibling-temp atomic writer while retaining their own plan validators
and manifests. Each checks the other provider's managed paths before writing. Exact overlapping paths
are rejected because stacked transformations have no shared provenance or rollback chain yet.
Game Doctor and Save Doctor stay read-only except for the explicit S2 custom-mod folder move exposed
as a separate troubleshooting action. Save Doctor delegates to the supported format readers and
does not create a new writer or capability.

## Rules

- Unknown or ambiguous fields stay read-only; no write from a guessed offset.
- Every write: fresh SHA of the source, backup, round-trip, CRC/framing check.
- Steam Cloud writes are explicit and never retried automatically. The S2 path (local Auto-Cloud folder plus a game
  session, `SteamAutoCloudWriter`) exists but is switched off in the UI until it is verified in the game.
- Capabilities per release are explicit: `verified`, `experimental`,
  `research`, `unsupported`. A capability becomes writable only with game
  evidence (L5).

## Game Fixes

`Patching/Data/game-fixes.json` is data: exact text replacements bound to the SHA-256 of the original file, structured
all.spawn edits, and per-fix Enhanced Edition file hashes from which the EE variants are derived. `GameFixEngine`
installs only onto the exact original, keeps it as a backup, journals the operation and restores on failure or
removal. A writer asks `GameDoctor.Identify` (folder marker, Steam build) before touching a game; the full
`GameDoctor.Analyze` is for display.
