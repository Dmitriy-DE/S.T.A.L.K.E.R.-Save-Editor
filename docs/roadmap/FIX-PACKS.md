# Our own fixes (derived from third-party fix packs) — plan and tracker

Owner decision (2026-09-30): port **all** useful third-party fixes into the editor so players do not need
external mods. Users install and remove each fix or pack themselves; originals are backed up and restored.
Authors are credited in every entry (open-source project, attribution, no sale).

## Principles (owner correction, 2026-09-30)

- **The fixes are ours.** SRP, ZRP, stalker-cop-patch, PRP, Workshop mods are research sources only: we find what
  they changed and why, then write our own fix entries (one problem per entry, our text, our tests), crediting the
  source. We never ship or install a third-party pack as such.
- Every fix is an exact, verified change: installs only onto the exact original file (SHA-256), user installs and
  removes it, the original is backed up and restored.
- Critical first (crash, save corruption, quest can never finish) → desirable (wrong quest behaviour, rewards, NPC
  logic) → optional, off by default (balance, sound, extras) → not ported (renames, intro removal, taste).
- Text changes are exact text replacements; new or binary files (e.g. all.spawn) use the whole-file overlay
  operation with the content shipped by us.
- Enhanced Editions: a fix gets an EE variant only when checked against the EE file.

## Sources

| Game | Source | Where | Size | Status |
|---|---|---|---|---|
| CS | SRP 1.1.5 (Decane) | github.com/Decane/SRP | 506 changelog entries (319 fixes); 611 changed + 66 new files | diffed against retail; 25 individual fixes ported |
| SoC | ZRP 1.07 R5RC (NatVac) | metacognix.com/files/stlkrsoc | hundreds of fixes | change logs used as the list of known faults; 26 fixes (15 config, 8 crash, 3 logic) |
| SoC / CS / CoP | GSC's own Enhanced Edition scripts | the installed EE games | retail vs EE: `tools/ee_diff.py` | faults GSC fixed itself; the retail fix is written by us (SoC 7, CS 5, CoP 5) |
| CoP | stalker-cop-patch (victor-homyakov) | github | 13 items, 24 files | 18 ported |
| CoP | Pripyat Reclamation Patch | ModDB (files deleted) | — | 6 ported earlier; source mirror needed |
| CoP EE | UCoPEEP | Steam Workshop 3487808500 | 1 gameplay change | ported (#184) |
| SoC EE | Workshop bloodsucker village fix | Steam Workshop 3487970687 | 1 script | ported, EE only (#184) |

## Engine work (not planned now)

Retail: OpenXRay-based binary patches are possible per exe version. EE: closed 64-bit DX12 engine, patched by
GSC; binary patches break on updates and Workshop forbids .dll/.exe. Only when a concrete bug cannot be fixed
in scripts.

## Tasks

| # | Task | Status | PR |
|---|---|---|---|
| FP-1 | Engine: whole-file overlay operation + content store | done | #185 |
| FP-2 | Research tools: pack diff (`fixes build-pack`), missing logic-section checker (`tools/check_logic_refs.py`) | done | #187, #192 |
| FP-3 | Hunk extractor: split each source diff into minimal anchored text changes, grouped per file, mapped to changelog entries | todo | |
| FP-4 | SRP critical fixes as our own entries (45: crashes, save corruption, stuck quests) + crash signatures | in progress (SRP crash list: 26 of 33 done) | #189–#193 |
| FP-5 | CS desirable fixes (quests, rewards, NPC logic) | in progress: 5 EE-confirmed (mutant without squad, helicopter save, patrol index, trade, flare timer) | #227 |
| FP-6 | SRP optional fixes (balance, sound, extras), off by default | todo | |
| FP-7 | SoC critical + desirable fixes as our own entries | in progress: 8 crash fixes, 3 logic fixes | #226, #231 |
| FP-8 | ZRP optional fixes | todo | |
| FP-9 | CoP: EE-confirmed fixes; remaining stalker-cop-patch items; PRP source mirror | in progress: 5 EE-confirmed | #228 |
| FP-10 | EE variants for every ported fix where the EE file allows | in progress (full EE dump + `tools/ee_variants.py`; CS EE 36) | #200 |
| FP-11 | Steam Workshop package of the companion (.pack writer + upload after owner OK) | todo | |
| FP-12 | Structural all.spawn editor (`AllSpawnEditor`, `Structured` fixes) + SRP's all.spawn errors | done: 43 edits; SRP clean-ups/gameplay not ported | #201 |

## FP-4 progress (SRP 1.1.5 crash list)

| SRP crash | Our entry | State |
|---|---|---|
| Stringov's stash given twice | `cs.crash.treasure-given-twice` | done #191 |
| Robbers leave during a hold-up | `cs.crash.robbery-squad-left` | done #190 |
| Robber leader is a mutant / offline | `cs.crash.robbery-leader-offline` | done #190 |
| Attitude of a missing squad | `cs.crash.relation-to-missing-squad` | done #190 |
| 'Help' task evaluation | `cs.crash.sim-combat`, `cs.crash.squad-action-finished-twice` (2nd patch) | done #189, #193 |
| Missing logic sections | `cs.crash.limansk-missing-logic` (+4 refs proven unreachable) | done #192 |
| Capture task for a missing squad | `cs.crash.capture-task-missing-squad` | done #189 |
| Load/delete an already deleted save | `cs.crash.save-menu-deleted-last` | done #191 |
| Squad captures a smart terrain | `cs.crash.squad-action-finished-twice` | done #189 |
| Campfire with nobody / no animation | `cs.crash.kamp-no-animation` | done #189 |
| Anomaly zone without an artefact | `cs.crash.anomaly-zone-missing-artefact` | done #189 |
| Orest strays (Agroprom) | `cs.crash.agroprom-orest-path` (published log: path inaccessible) | done #197 |
| Red Forest mine trader strays | `cs.crash.red-forest-mine-trader-path` | done #197 |
| Wild Napr task (`wrong target for storyline quest`) | `cs.crash.capture-task-missing-squad` (2 more patches) | done #200 |
| NPC offline during dialogue/trade | `cs.crash.npc-offline-during-dialog` (own `pda.dialog_open` flag) | done #195 |
| Smart terrain overloading: Army Warehouses camp 2_1 | `cs.crash.all-spawn-errors` (capacity, new game) | done #201 |
| Smart terrain overloading: Dark Valley wagon 9_6, scripted target + joining squad | — | todo: SRP reworks the simulation (sim_board / sim_squad_generic); needs its own analysis |
| Marsh creature scene (Agroprom, Swamps) | `cs.crash.marsh-creature-no-squad` (log: `npc_squad` nil) | done #196 |
| Buggy dialog trees | — (`tools/check_dialogs.py`: none reachable in retail) | closed #195/#196 |
| Waypoint: Cordon bonfire | `cs.crash.all-spawn-errors` | done #198 → #201 |
| Army Warehouses path when mutants attack | `cs.crash.all-spawn-errors` (missing target link, new game) | done #201 |
| Malformed conditions (found by our checker) | `cs.crash.condlist-syntax` | done #199 |
| Two rare crashes on reload (`sim_combat` 419/968) | `cs.crash.sim-combat` | done #189 |
| Missing mesh (`item_rukzak`) | `cs.crash.missing-backpack-model` (points at the game's own `dev_rukzak`) | done #202 |
| Save corruption: corpse cleanup deletes a reused ID | `cs.save.corpse-cleanup-wrong-object` | done #203 |
| Red Forest ambush squad missing (`There is no squad … in sim_board`) | `cs.crash.relation-to-missing-squad` | done #190 |
| Marsh creature entity deleted right after creation (race) | — | todo: no log; SRP rewrote sr_bloodsucker |
| 'You are saving too much' | — | research: a guard against the engine's packet buffer; removing it is unsafe without engine RE |
| Logic loaded before the player exists (save corruption) | — | todo: cause not pinned down |
| Flea Market basement mugging corrupts the game | — | todo: SRP replaces a story-id deletion with effectors |

## Method: faults confirmed by GSC (Enhanced Edition)

`fixes extract TARGET GAME_DIR OUT --archives-only scripts/ configs/` dumps a game as shipped; `tools/ee_diff.py RETAIL EE`
prints what differs between the retail and the Enhanced Edition scripts with comments, whitespace and encoding
ignored. A guard or a corrected condition that GSC added in the EE proves the retail fault. The fix is then written
for the retail file by us (the EE engine has functions retail lacks, so EE code is not copied) and gets an EE variant
only when the EE file still has the fault.

Candidates seen in the diffs and not ported yet:

| Game | File | What GSC changed |
|---|---|---|
| SoC | `xr_patrol.script` | patrol manager stops the game on a missing commander → returns nothing (callers must cope) |
| SoC | `xr_logic.script` | logic initialised before the player exists; `selective` as an alias of `active` |
| SoC | `gulag_dark_valley.script` | the "sos" bloodsucker job is bound to one creature instead of any bloodsucker |
| SoC | `dialogs_military.script` | reward item of a Freedom dialog |
| SoC | `agroprom_tasks.script` | "find Strelok's stash" completes when leaving the underground with the stash found |
| CS | `sr_bloodsucker.script` | reaction switched off for squad members who are not in the level (same file as `cs.crash.marsh-creature-no-squad`, whose EE variant must stay valid) |
| CS | `guiders.script` | guide availability condition (`or` → `and`) |
| CS | `state_mgr_animation.script` | sub-animation loop never ran (`for key = n, 0 do`) |
| CS | `task_manager.script` | upgrade-search tasks stored under the wrong key |
| CS | `dialogs_escape.script`, `minigame_dialogs.script` | who pays 500 RU for support; bet equal to the player's money |
| CS | `treasure_manager.script` | stash states saved in table order, which is not stable |
| CS | `sound_manager.script`, `state_mgr.script`, `xr_kamp.script` | nil guards; deliberate stops turned into log lines |
| CoP | `dialogs.script`, `dialogs_jupiter.script` | Jupiter task conditions (`jup_a10`, `jup_a12`) |
| CoP | `outro_cond.script` | Noah's ending slide ignores the death of his dog |

## Change log

- 2026-10-02 — SoC, CS, CoP batches (#226–#228, #231): 8 SoC crash fixes and 3 logic fixes, 5 CS and 5 CoP fixes for
  faults GSC fixed in the Enhanced Editions. New research tools: `fixes extract`, `tools/ee_diff.py`. Each preset was
  installed with the CLI into a hard-linked pristine copy of the real install and removed again without leftovers
  (SoC 23, CS 47, CoP 16 fixes); every patched script passes `luac5.1 -p`. Nothing was run in the games.

- 2026-09-30 — batches 11–13 (#200–#203) and FP-12 (#201): Wild Napr story task; structural all.spawn editor with 43 fixes
  (25 waypoints on existing saves, 18 object settings for new games) verified by re-parsing the real retail file; missing
  backpack model via the game's own model; corpse cleanup save corruption. EE: full dump, 8 more EE variants. Source of
  exact crash texts: SRP 1.1.5 version history. Tools: `spawn_diff.py`, `ee_variants.py`.

- 2026-09-30 — FP-4 batches 6–10 (#195–#199): NPC offline in dialogue, marsh creature, Orest, mine trader, Cordon
  waypoint, malformed conditions. Engine: same-length binary patches (`binary: true`) for all.spawn records.
  Research tools: `check_dialogs.py`, `check_condlists.py`, `spawn_points.py`. Real crash logs found online were used to
  prove causes (STCS Redux notes, GameFAQs). Remaining CS crashes: Wild Napr, smart overloading, Army Warehouses
  waypoint, missing backpack mesh.

- 2026-09-30 — FP-4 batches 1–5 (#189–#193): 12 Clear Sky crash entries, 6 Game Doctor signatures. Four suspected
  missing logic sections checked against all.spawn and script flow: unreachable, no fix. Stacked branches rebuilt
  on main after the queue broke; a leftover pack downloader removed from #188.

- 2026-09-30 — owner: no third-party packs in the product; the fixes must be ours. Pack-install direction dropped
  (the zips uploaded to R2 `fixpacks/` are unused). Overlay engine (#185) and diff tool (#187) kept.

- 2026-09-30 — pack builder (`fixes build-pack`, archives only, programs/docs skipped). Results on the owner's
  installs: SRP 677 files (66 new), ZRP 510 (208 new), stalker-cop-patch 24. EE: only part of each pack matches
  the EE originals (SRP 345/677, ZRP 301/510, cop-patch 11/24) → **no whole packs on EE**, individual fixes only.
- 2026-09-30 — plan written; UCoPEEP and Workshop bloodsucker fix ported (#184); overlay engine (#185).
