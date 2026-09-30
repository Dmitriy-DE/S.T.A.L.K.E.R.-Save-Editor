# Handoff — state on 2026-09-30 (v1.2.0)

Read this first, then `AGENTS.md`, `ARCHITECTURE.md`, `README.md`, `docs/roadmap/STATE.md`.

## Where things are

| What | Where |
|---|---|
| Code (only live repo) | `github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor` (renamed from `-Next`). Python repo `S.T.A.L.K.E.R.-Save_Editor` is archived, ignore it. |
| Released | v1.2.0 (GitHub Release + R2 update server `save-editor-downloads.save-editor.workers.dev/latest.json`) |
| Web edition | `stalker-save-editor.pages.dev` (C# WASM, deployed by hand) |
| Local worktrees | `~/Projects/save-editor-next-claude` (Claude merge queue), `~/Projects/save-editor-review-claude` (Claude edits — never edit where a queue script runs: it resets hard), `~/Projects/save-editor-next` (Codex) |
| Owner's games (Linux, Proton) | `~/.local/share/Steam/steamapps/common/` — SoC, CS (+OGSM mod), CoP, three EE; S2 via GFN only (no mods there) |
| Owner's saves | `…/<game>/_appdata_/savedgames/`, EE and S2 in `compatdata/<appid>/pfx/…` — 342 files. Read or copy; never write them. |

## Projects

`Core` (formats, writers, backups, companion installer, patching, doctors) · `Steam` (libsteam_api worker, cloud) · `Updater` · `Desktop` (Avalonia UI library, shared with web) · `App` (desktop host) · `Browser` (WASM host, not in the .sln) · `Cli` (NativeAOT). Mod: `mods/companion/{soc,cs,cop,s2}`.

## Checks before any PR

```
export TMPDIR=~/.cache/claude-pytest
dotnet build StalkerSaveEditor.sln -c Release -warnaserror
dotnet test StalkerSaveEditor.sln -c Release --no-build
./tools/check_companion.sh
dotnet run --project src/StalkerSaveEditor.App -c Release --no-build -- --test-i18n   (and --test-audio)
git diff --check
```
CI: ubuntu/windows/macOS + web + companion; branch protection = green + up to date; merge one PR at a time. The perf benchmark (ubuntu) flaps at ~1.3× — rerun the failed job. `gh pr edit` fails silently: use `gh api -X PATCH`.

New UI text: `L.T("русский текст")` + `python3 tools/add_translations.py file.tsv` (ru + 14 languages); `TranslationTests` fails on any missing string. Tests must hold on Windows/macOS: compare paths through `SaveSlotDiscovery.ResolveLinks`, parse JSON, generous timeouts.

## Release and publish (secrets for CI publishing are NOT set)

1. Bump `<Version>` in `Directory.Build.props` and `ApplicationVersionTests`, PR, merge.
2. `git tag vX.Y.Z origin/main && git push origin vX.Y.Z` → `release-packages.yml` builds all OSes and creates the GitHub Release.
3. R2: `gh release download vX.Y.Z` into a folder, `python3 tools/release/publish_release.py --prepared --output <folder> --publish-r2 --verify-r2` (local wrangler login).
4. Web: `DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH dotnet publish src/StalkerSaveEditor.Browser -c Release -o artifacts/web` then `npx wrangler@4 pages deploy artifacts/web/wwwroot --project-name=stalker-save-editor --branch=main`.

## Done

- Save editing for SoC/CS/CoP (+EE): money, stacks, condition, placement, upgrades, relations, add/remove items, stashes (take, put, create in stash); S2: money, stacks, condition. Backup + journal + atomic swap + read-back for every write.
- Read-only save data: PDA tasks with state and times (incl. CoP EE), kill statistics (SoC/CS), weather (CS 62/62, CoP), actor location, installed game build; task changes in Compare.
- 1.2.0 review fixes: async commands no longer async void, X11 re-grab crash, background task failures logged, resumable companion uninstall, audio races, library refresh cancel; Game Fix catalogue is JSON data; logs record writes/fixes/companion commands (redacted).
- Quest Doctor (SoC 4 rules, CS 3 rules): detect a dead NPC whose death flag is missing, repair the save, open the preventing Game Fix. Crash signatures (18 CS from SRP, 6 SoC from ZRP) in Game Doctor.
- Game Fixes: 64 retail + 54 Enhanced Edition variants. Experimental actor relocation to level-changer destinations (TP).
- Steam: cloud read/download/upload with confirmation and no retries, achievements.
- UI: 15 languages, game sounds/music, icons from the installed game, previews, compare, drafts with undo, diagnostics and daily redacted reports. Saves open off the UI thread; ReadyToRun builds (window start ~0.35 s).
- Companion mod: full in-game menu for SoC/CS/CoP (L4 in CoP only), rebindable hotkeys; S2 UE4SS mod experimental.
- CI: 3 OS + web; runtime gate on the packaged app; benchmark gate tolerant of slow runners.

## Not done / next

1. **Owner in game (L5):** a CS/CoP save written by the editor (grows ~4×), Quest Doctor repair, relocation (TP), stash moves, fixes, companion in SoC/CS/EE, S2 companion god/noclip/timespeed (needs S2 on PC with UE4SS).
2. **Refactors deferred** (no user-visible change): split SaveLibraryViewModel (loader / edit session / VM), list recycling (#90), analyzers for Desktop/App, headless tooling out of the App exe, one file-transaction primitive for fixes/companion/snapshots/backups (after L5).
3. **Needs owner material:** S2 save pairs for add items/upgrades/relations; S2 game files for content packs (CP-4); UCoPEEP/Workshop files (subscribe) to port external fixes; update signing key; CI publish secrets; Workshop upload.
4. Translation review by native speakers (TR-1).

Frontend v2: Codex rebuilt shell/themes + 8 screens (#160, #163–#169); Claude polished the other 12 (#170) and took over the frontend on 2026-09-30.

## Lessons (do not repeat)

- "No evidence" was accepted twice when the data or the sources existed (quest flags, SRP crash list). Search the code and the web before declaring something impossible.
- Screens without content count as not done.
- Committed build output (`build/`) broke the release scripts once it was removed — packaging now creates its folders.
