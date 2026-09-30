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
| SoC | ZRP 1.07 R5RC (NatVac) | metacognix.com/files/stlkrsoc | hundreds of fixes | downloaded, not diffed yet; 15 ported |
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
| FP-4 | SRP critical fixes as our own entries (45: crashes, save corruption, stuck quests) + crash signatures | in progress (crashes ≈11/20) | #189–#193 |
| FP-5 | SRP desirable fixes (quests, rewards, NPC logic) | todo | |
| FP-6 | SRP optional fixes (balance, sound, extras), off by default | todo | |
| FP-7 | ZRP critical + desirable fixes as our own entries | todo | |
| FP-8 | ZRP optional fixes | todo | |
| FP-9 | CoP: remaining stalker-cop-patch items; PRP source mirror | todo | |
| FP-10 | EE variants for every ported fix where the EE file allows | todo | |
| FP-11 | Steam Workshop package of the companion (.pack writer + upload after owner OK) | todo | |

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
| Orest strays (Agroprom), mine trader strays (Red Forest) | — | todo: SRP diff mixes rework and balance; cause to prove |
| Wild Napr task | — | todo: multi-file (death handling moved to spawn sections + condition) |
| NPC offline during dialogue/trade | — | todo |
| Smart terrain overloading | — | todo |
| Marsh creature scene (Agroprom, Swamps) | — | todo |
| Buggy dialog trees | — | todo |
| Waypoints: Army Warehouses, Cordon bonfire | — | todo |
| Two rare crashes on reload | — | todo |
| Missing mesh (`item_rukzak`) | — | todo (needs a model resource, credited) |

## Change log

- 2026-09-30 — FP-4 batches 1–5 (#189–#193): 12 Clear Sky crash entries, 6 Game Doctor signatures. Four suspected
  missing logic sections checked against all.spawn and script flow: unreachable, no fix. Stacked branches rebuilt
  on main after the queue broke; a leftover pack downloader removed from #188.

- 2026-09-30 — owner: no third-party packs in the product; the fixes must be ours. Pack-install direction dropped
  (the zips uploaded to R2 `fixpacks/` are unused). Overlay engine (#185) and diff tool (#187) kept.

- 2026-09-30 — pack builder (`fixes build-pack`, archives only, programs/docs skipped). Results on the owner's
  installs: SRP 677 files (66 new), ZRP 510 (208 new), stalker-cop-patch 24. EE: only part of each pack matches
  the EE originals (SRP 345/677, ZRP 301/510, cop-patch 11/24) → **no whole packs on EE**, individual fixes only.
- 2026-09-30 — plan written; UCoPEEP and Workshop bloodsucker fix ported (#184); overlay engine (#185).
