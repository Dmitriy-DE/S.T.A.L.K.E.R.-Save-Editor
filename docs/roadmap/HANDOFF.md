# Handoff — state on 2026-09-29

Read this first, then `AGENTS.md`, `ARCHITECTURE.md`, `README.md`, `docs/roadmap/STATE.md`.

## Where things are

| What | Where |
|---|---|
| Code (only live repo) | `github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor` (renamed from `-Next`). Python repo `S.T.A.L.K.E.R.-Save_Editor` is archived, ignore it. |
| Released | v1.1.0 (GitHub Release + R2 update server `save-editor-downloads.save-editor.workers.dev/latest.json`) |
| Web edition | `stalker-save-editor.pages.dev` (C# WASM, deployed by hand) |
| Local worktrees | `~/Projects/save-editor-next-claude` (Claude), `~/Projects/save-editor-next` (Codex), `-gemini`, `-toolkit` — use your own |
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

- Save editing for SoC/CS/CoP (+EE): money, stacks, condition, placement, upgrades, relations, add/remove items, stashes; S2: money, stacks, condition. Backup + journal + atomic swap + read-back for every write.
- Steam: cloud read/download/upload with confirmation and no retries, achievements.
- UI: 15 languages, game sounds/music, icons from the installed game, previews, compare, drafts with undo, diagnostics and daily redacted reports (worker `/diagnostics`).
- Companion mod: full in-game menu for SoC/CS/CoP (L4 in CoP only); S2 UE4SS mod experimental. Hotkeys held only while the game window is focused.
- Toolkit (Codex, #125–#130): Game Doctor, Game Fix engine shared with the Companion layer, 64 fixes (CS 25, SoC 15, CoP 24; owner's installs: 23/15/10 applicable), presets, Windows installer component, snapshots, profiles, `user.ltx` settings, install audit, crash-log discovery, save timeline, encyclopedia, Live Inspector.
- PR #132 (open): `XRayTrilogySave.ActorKnownInfo` (actor quest flags from registry chunk 9) and `XRayInfoPortionWriter.AddActorInfo` — verified on real saves in memory.

## Not done / next

1. **Quest Doctor + save repair** — rules "known broken quest → missing info portion → add it" from SRP/ZRP/PRP changelogs and the game's task configs; screen with "Repair save" and "Install prevention fix". Start with Clear Sky (Wolf, Wild Napr, Hog …).
2. **Crash signatures** — SRP history quotes ~40 crash messages (e.g. `wrong target for storyline quest: logic@work5,gar_smart_terrain_6_3` → `cs.quest.dead-wild-napr`). The catalogue is currently empty.
3. **EE / S2 fixes** — none shipped. External fixes to recommend and detect (do not redistribute): UCoPEEP for CoP EE (Nexus mods/2, Workshop 3487808500), S2 Nexus fixes (e.g. mods/1739). EE payload adaptation of our 64 fixes not done.
4. **Not verified in any game:** all save writes (L5), fixes, SoC/CS companion menus, 1.1.0 installers on Windows/macOS.
5. **Known risk:** after any write CS/CoP saves grow ~4× (our LZO compressor vs the game's). Nobody has loaded such a save in the game yet — check first.
6. S2: adding items, upgrades, relations need before/after save pairs from the owner.
7. R2/APT secrets for CI publishing (owner). Python repo can be deleted by the owner (`gh auth refresh -s delete_repo`).

## Lessons (do not repeat)

- "No evidence" was accepted twice when the data or the sources existed (quest flags, SRP crash list). Search the code and the web before declaring something impossible.
- Screens without content count as not done.
- Committed build output (`build/`) broke the release scripts once it was removed — packaging now creates its folders.
