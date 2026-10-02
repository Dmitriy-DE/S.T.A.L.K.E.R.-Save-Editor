# Handoff — state on 2026-10-02 (v1.2.0 released; main is ahead of it)

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

`Core` (formats, writers, backups, companion installer, patching, doctors) · `Steam` (libsteam_api worker, cloud) · `Updater` · `Desktop` (Avalonia UI library, shared with web) · `Host` (Steam and updater services behind the UI's interfaces) · `App` (desktop host) · `Browser` (WASM host, not in the .sln) · `Cli` (NativeAOT). Mod: `mods/companion/{soc,cs,cop,s2}`.

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
   The tool signs latest.json → latest.json.sig with `~/.config/stalker-save-editor/update-signing-key.pem` (ECDSA P-256, never in git; the public key is embedded in `UpdateSignature`). From 1.2.1 the app rejects an unsigned or wrongly signed manifest. Losing the key means shipping a new public key in a release signed by the old one.
4. Web: `DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH dotnet publish src/StalkerSaveEditor.Browser -c Release -o artifacts/web` then `npx wrangler@4 pages deploy artifacts/web/wwwroot --project-name=stalker-save-editor --branch=main`.

## Done and not done

What the program does and how far it is verified: `docs/roadmap/STATE.md`. What is left, split into "needs the owner"
and "can be done without the owner": `docs/roadmap/README.md`. Game fixes: `FIX-PACKS.md` (sources, what was not
taken and why) and `FIX-AUDIT-2026-10-02.md` (a record per fix, the regression pass over the whole catalogue).

## Game fixes: checks before a PR that touches the catalogue

```
python3 tools/fix_regress.py ~/Projects/fix-sources ~/.cache/claude-pytest/fix-regress   # every patch against the dumps of six games, checkers, Lua harness
bash tools/fix_realcheck.sh                                                              # install and remove every fix on copies of the six real installs
```
The dumps are made with `fixes extract TARGET GAME_DIR OUT --archives-only scripts/ configs/ shaders/`. Every new or
changed fix gets a record in the audit document: the game function, who uses it, what changes for the player, how far
the logic was checked. A remedy of our own design is run in `tools/lua_harness` before it is catalogued: reading it was
not enough once (`cs.crash.smart-terrain-no-free-job` 1.0.0).

## Merge queue

`~/Projects/fix-sources/tools/run-queue.sh` merges the PRs listed in `full-queue.txt` one at a time (cherry-pick of the
PR's own commits onto `main`, build, tests, CI, squash). A stacked PR must point at `main` before it is merged: #240
was squash-merged into the branch below it and never reached `main` (repaired by #253; the queue now retargets).

## Lessons (do not repeat)

- "No evidence" was accepted twice when the data or the sources existed (quest flags, SRP crash list). Search the code and the web before declaring something impossible.
- Screens without content count as not done.
- Committed build output (`build/`) broke the release scripts once it was removed — packaging now creates its folders.
