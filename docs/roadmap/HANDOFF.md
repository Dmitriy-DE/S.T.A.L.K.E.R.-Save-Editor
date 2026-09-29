# Handoff — state on 2026-09-29 (evening)

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

- Save editing for SoC/CS/CoP (+EE): money, stacks, condition, placement, upgrades, relations, add/remove items, stashes (take, put, create in stash); S2: money, stacks, condition. Backup + journal + atomic swap + read-back for every write.
- Read-only save data: PDA tasks with state and times, kill statistics (SoC/CS), weather (CS/CoP), actor location.
- Quest Doctor (SoC 4 rules, CS 3 rules): detect a dead NPC whose death flag is missing, repair the save, open the preventing Game Fix. Crash signatures (18 CS from SRP, 6 SoC from ZRP) in Game Doctor.
- Game Fixes: 64 retail + 54 Enhanced Edition variants. Experimental actor relocation to level-changer destinations (TP).
- Steam: cloud read/download/upload with confirmation and no retries, achievements.
- UI: 15 languages, game sounds/music, icons from the installed game, previews, compare, drafts with undo, diagnostics and daily redacted reports. Saves open off the UI thread; ReadyToRun builds (window start ~0.35 s).
- Companion mod: full in-game menu for SoC/CS/CoP (L4 in CoP only), rebindable hotkeys; S2 UE4SS mod experimental.
- CI: 3 OS + web; runtime gate on the packaged app; benchmark gate tolerant of slow runners.

## Not done / next

1. **Frontend v2 (Codex)** — handoff in `~/Projects/STALKER_FRONTEND_HANDOFF_V1/00_CSHARP_OVERRIDE.md` (C# override of a ChatGPT design pack): left sidebar, new screens, themes/accent/scale. Claude merges and fixes.
2. **Owner in game (L5):** a CS/CoP save written by the editor (it grows ~4×), Quest Doctor repair, relocation, fixes, SoC/CS companion.
3. CoP EE task registry (0/12 saves read); CS weather where the string is absent.
4. S2 companion inspector/god mode/free camera — needs S2 with mods (owner plays via GFN) → later version.
5. EE/S2 external fixes (UCoPEEP, Nexus) detection; S2 adding items, upgrades, relations need save pairs.
6. Content packs (CP-3/4), S2 armour upgrade names (KB-7), game build fingerprint (RL-6), companion Workshop/EE/S2 (MOD-2…5), translation review by native speakers (TR-1).
7. Owner only: Cloudflare/APT secrets for CI publishing; deleting the archived Python repo.

## Lessons (do not repeat)

- "No evidence" was accepted twice when the data or the sources existed (quest flags, SRP crash list). Search the code and the web before declaring something impossible.
- Screens without content count as not done.
- Committed build output (`build/`) broke the release scripts once it was removed — packaging now creates its folders.
