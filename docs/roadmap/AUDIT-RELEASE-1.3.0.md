# Audit before 1.3.0 (2026-10-02)

Rule of this pass: nothing is taken on trust, including the checks of the previous passes. Each line says what was
run and on what; "not checked" is listed at the end.

## What was run

| Check | On what | Result |
|---|---|---|
| Build from a clean clone, warnings as errors | fresh `git clone`, no cached output | 0 warnings, 0 errors. The S2 tests need `tools/build_ooz_native.py` once per clone (now in HANDOFF) |
| All tests | clean clone, then the release branch | Core 952, Steam 65, CLI 40 pass |
| Queue result against the original branch stack | `git diff` of the merged `main` and the stack | nothing from the stack is missing on `main` |
| Money edit and undo, in memory | all 277 real saves that parse as current formats (SoC 6, SoC EE 6, CS 62, CoP 173, CoP EE 12, S2 18) | every save: the edit changes only money, the undo describes exactly the original save. 15 old-layout S2 saves are refused, as intended |
| Stack edit and condition edit, in memory | the same X-Ray saves | 253 stack edits and 259 condition edits: only the edited item changes |
| S2 stack edit on every editable item | one real save, 29 items | all written and read back |
| Readers under damage | 1 880 mutated saves (bit flips, truncation, overwritten runs, damaged headers) from all formats | rejected or parsed; no unexpected exception type, slowest 0.3 s |
| Fix catalogue against dumps extracted anew from the six installed games | 596 patches | no patch error; the checkers find nothing the patches add |
| Every fix installed and removed | copies of the six real installs | all install, nothing left after removal |
| Real game folders and saves | modification times | no file touched |
| Complete Linux package built from the clean clone | packaged app and NativeAOT CLI | start, i18n and audio self-checks, UI timing run, CLI on real saves (incl. an old S2 save) and on the real Clear Sky install |
| Web host | `StalkerSaveEditor.Browser` | builds |
| Unused code | analyzers and a cross-reference of declarations | nothing new |

## Found and fixed in this pass

| Finding | Fix |
|---|---|
| Reducing a stack of a carried quest item (kind 8) to one in S.T.A.L.K.E.R. 2 was refused only after the save had been packed, with a message about a failed round trip (all 18 real saves) | refused before anything is packed, with the reason; rule shared by reader and writer |
| `tools/fix_regress.py` assumed the old dump layout of Clear Sky and picked the wrong config folder for two Enhanced Editions (their checker comparison was empty) | both corrected; found only because the dumps were extracted anew |
| The roadmap documents on `main` and on the branch stack had diverged | merged by hand; the tracker carries the open rows of the earlier audit |
| A test that failed once in the clean clone (`SettingsAndAudioTests`) | already serialised on `main` (#236 line); not reproduced since |

## Not checked

- Nothing was run in a game: no L4 or L5 for any save write or any fix of this release.
- Windows and macOS packages: built by CI only, not started on hardware here.
- Steam Cloud and achievements against a live Steam session.
- The updater against the real update server (checked at publish time by `publish_release.py --verify-r2`).
