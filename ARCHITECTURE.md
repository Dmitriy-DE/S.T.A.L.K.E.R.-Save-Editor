# S.T.A.L.K.E.R. Save Editor — C# architecture

The Python editor (`Dmitriy-DE/S.T.A.L.K.E.R.-Save_Editor`) is the **oracle**.
Every reader and writer here must reproduce its results byte for byte on the
golden vectors (`tests/golden/fixture-vectors.json` there, CS-1) before it is
used. The Python repo is frozen for new features once this port reaches parity
(decision D5); until then it takes fixes only.

## Stack

- .NET 10 (LTS), C# latest, nullable enabled, warnings as errors.
- Avalonia 11.x, MVVM (CommunityToolkit.Mvvm).
- xUnit for tests.
- CI on Windows, Linux (Ubuntu 24.04) and macOS (arm64) from the first commit.

## Projects

```text
src/
  StalkerSaveEditor.Core/          no UI or Steam; narrow filesystem boundaries for saves and game files
    Formats/XRay/                  container (LZO), ALIFE chunks, actor, inventory
    Formats/Enhanced/              EE = XRay container + own ALIFE versions
    Formats/Stalker2/              GVAS/Kraken container, name tables, inventory, stash
    Codecs/                        Kraken (P/Invoke to native ooz), LZO1X (managed)
    Editing/                       immutable EditPlan, PreparedEdit, drafts, validation
    Catalogs/                      item names, icons, official names (same JSON as Python)
    Capabilities/                  CapabilityMaturity, FeatureCapability (+ evidence)
    Backups/                       backup + recovery artifacts, fresh-SHA checks
    Localization/                  tr(), 15 languages, same locale JSON as Python
    Patching/                      atomic installed-game writes and Game Fix transactions
  StalkerSaveEditor.Steam/         thin P/Invoke to steam_api: RemoteStorage,
                                   UserStats (achievements), Auto-Cloud game session
  StalkerSaveEditor.Updater/       state machine: Checking → Downloading →
                                   Verifying → WaitingForPermission → Installing →
                                   Restarting → Completed | Failed (real exit code)
  StalkerSaveEditor.Desktop/       Avalonia app
  StalkerSaveEditor.Cli/           inspect / prepare / verify, JSON output
tests/
  StalkerSaveEditor.Core.Tests/    parity with golden vectors, negative cases
  StalkerSaveEditor.Steam.Tests/   fakes only; never a live Steam session in CI
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

## Rules carried over from the Python repo

- Unknown or ambiguous fields stay read-only; no write from a guessed offset.
- Every write: fresh SHA of the source, backup, round-trip, CRC/framing check.
- Steam Cloud writes are explicit and never retried automatically. S2 writes go
  through the local Auto-Cloud folder plus a game session (SC-1).
- Capabilities per release are explicit: `verified`, `experimental`,
  `research`, `unsupported`. A capability becomes writable only with game
  evidence (L5).

## Python modules → C#

| Python | C# | Plan |
|---|---|---|
| `xray_container.py` | `Formats/XRay/XRayContainer` | KEEP (port) |
| `xray_save.py` (+ `xray_item_state`, `xray_relations`, `xray_factions`, `xray_slots`, `xray_delete`, `xray_level_changer`) | `Formats/XRay/*` | KEEP; split the 2 000-line module by concern |
| EE specs in `xray_save.py` | `Formats/Enhanced` | KEEP |
| `save_format.py`, `s2_*`, `kraken_blocks.py`, `codec.py` | `Formats/Stalker2/*`, `Codecs/*` | KEEP |
| `models.py`, `prepare.py`, `transactions.py`, `compare.py`, `drafts.py` | `Editing/*` | KEEP |
| `catalog*.py`, `item_names.py`, `official_names.py`, `icon_donor.py`, `s2_items.py` | `Catalogs/*` | KEEP; data JSON shared |
| `capabilities.py`, `capability_types.py`, `cloud_capabilities.py`, `equipment*.py`, `releases.py` | `Capabilities/*` | REDESIGN: one registry type |
| `i18n.py` | `Localization` | KEEP; same locale files |
| `steam_native.py`, `steam_autocloud.py`, `steam_achievements.py`, `steam_profiles.py`, `steam_vdf.py` | `StalkerSaveEditor.Steam` | KEEP; subprocess isolation → worker process |
| `steam_cdp.py` (Steam web via CEF) | `Steam/CloudWeb` | KEEP |
| `steam_backend.py` + SteamCloudFileManager helper | — | DROP (SC-2) |
| `updater.py`, `update_manifest.py`, `release_artifacts.py` | `StalkerSaveEditor.Updater` | REDESIGN (state machine) |
| `platforms.py`, `storage.py`, `settings.py`, `preferences.py` | Desktop/Infrastructure | KEEP |
| `diagnostics.py` | Desktop/Diagnostics | KEEP |
| `service.py` | `Core/EditorService` | REDESIGN: thin facade over Formats + Editing |
| `ui/*` (PySide6) | `StalkerSaveEditor.Desktop` | REDESIGN in Avalonia |
| `web/*` (Pyodide) | — | later: Blazor WASM over Core, decision after parity |

## Order (roadmap CS-2 … CS-8)

1. CS-2 skeleton: solution, projects, CI on 3 OS, empty tests pass.
2. CS-3 codecs: LZO managed, Kraken native; byte parity with Python.
3. CS-4 readers module by module, golden-vector parity per PR.
4. CS-5 writers one capability at a time, Python writer as oracle.
5. CS-6 Avalonia UI, CS-7 Steam/updater/packages, CS-8 switch-over.
