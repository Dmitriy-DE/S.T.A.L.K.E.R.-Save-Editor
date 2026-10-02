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
| SoC / CS / CoP | GSC's own Enhanced Edition scripts | the installed EE games | retail vs EE: `tools/ee_diff.py` | faults GSC fixed itself; the retail fix is written by us (SoC 10, CS 11 + 3 retail-only additions, CoP 6) |
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
| FP-2 | Research tools: pack diff (`fixes build-pack`), missing logic-section checker (`tools/check_logic_refs.py`), undefined-global checker (`tools/lua_globals.py`, #243) | done | #187, #192 |
| FP-3 | Hunk extractor: split each source diff into minimal anchored text changes, grouped per file, mapped to changelog entries | dropped: the diffs are read by hand per fault (`tools/ee_diff.py`); a generic hunk extractor would not tell faults from rewrites | |
| FP-4 | SRP critical fixes as our own entries (45: crashes, save corruption, stuck quests) + crash signatures | closed: every SRP crash entry is done or has a reason in "What is left and why" | #189–#193 |
| FP-5 | CS desirable fixes (quests, rewards, NPC logic) | closed (sources exhausted, see "What is left and why"): 5 EE-confirmed crash/save fixes (#227), 4 logic fixes (sub-animations, guides, Cordon support payment, shooting range bet), 5 from the SRP fault list, remark crash, offline combat | #227, #233, #240, #243 |
| FP-6 | SRP optional fixes (balance, sound, extras), off by default | closed: not faults (see "What is left and why") | |
| FP-7 | SoC critical + desirable fixes as our own entries | closed (sources exhausted, see "What is left and why"): 12 crash fixes, 5 logic/config fixes (#237, #241, #243 added to the first 11) | #226, #231, #237, #241, #243 |
| FP-8 | ZRP optional fixes | closed: not faults (see "What is left and why") | |
| FP-9 | CoP: EE-confirmed fixes; remaining stalker-cop-patch items; PRP source mirror | closed (sources exhausted, see "What is left and why"): 5 EE-confirmed, Goldfish once, 4 found by our own checkers | #228, #237, #242 |
| FP-10 | EE variants for every ported fix where the EE file allows | closed: every new fix gets its EE variant when the EE file still has the fault (SoC 19, CS 46, CoP 25) | #200 |
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
| Smart terrain overloading: Dark Valley wagon 9_6, scripted target + joining squad | `cs.crash.smart-terrain-no-free-job` (job sharing instead of the abort) | done #246, not run in the game |
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
| Flea Market basement mugging corrupts the game | `cs.save.flea-market-basement-object` | done #245 |

## What is left and why (2026-10-02)

Every source was gone through to the end: the SRP 1.1.5 version history (all "Fixed" entries), the ZRP 1.07 R5 tree
(scripts and configs diffed against retail), stalker-cop-patch, the Enhanced Edition diffs of all three games, and our
own checkers (`check_condlists`, `check_logic_refs`, `check_dialogs`, `lua_globals`, `check_infos`, `check_condfuncs`,
`check_module_calls`, `check_trade_items`) over all three games. What is not in the catalogue falls into these groups:

| Group | Examples | Why not |
|---|---|---|
| Needs the simulation rewritten | SRP: capture/eliminate/defend task bookkeeping (player's help forgotten, tasks cancelled or completed in the wrong case, targets chosen wrongly), squads abandoning or preferring wrong targets, offline squads stuck in 'wait', faction power after load, supply chests on load. Done from this group in #248–#251: the crashes and hangs in capture/defend tasks, attacking squads taking post jobs, offline fights against 'idle' squads, counter-attacks hijacking scripted squads | SRP changes `sim_board`, `sim_faction*`, `sim_squad_generic`, `task_objects` as a whole (thousands of lines); the faults cannot be cut out as small text changes, and none of it can be checked without long play |
| Needs a save-format change | delayed 'defend' task forgets its attacking squad on load (GSC added the field in the EE); wounded start time as SRP stores it | we do not change what a retail save contains |
| Spawn order | SRP: marsh creature deleted right after creation; monsters spawning before the player | stalkers are done (`cs.save.npc-spawn-before-player`, #250, unobserved); the monster binder and the marsh creature race are not |
| Story rework | SRP: Cordon bus-stop skirmish; Limansk bridge squad; Kostyan's squad; Yantar defence flow; megaphone and greeting timing (Hog's death, the Compass order and the Red Forest bridge unlock are done in #250). ZRP: Dark Valley toll, Sarcophagus decoder, Bar territory | each is a redesign of a scene, not a repair of a broken line; several need a new game |
| Balance or taste | SRP: shotgun stabiliser −2 rounds, BTR at the Freedom base, trade lists and prices, weapon and upgrade values, mutant parameters; ZRP: corpse timers, mutant speeds, weapon tweaks | GSC kept them in the Enhanced Editions or they are choices, not faults (FP-6 and FP-8 stay closed: no optional packs) |
| Engine, shaders, UI art, sound assets | SRP: wet surfaces, sun shafts, detector dial, scroll bars, missing or wrong sounds, map spots, animations not settling | not reachable from scripts/configs, or needs new assets; engine work is out of scope (see "Engine work") |
| New game / all.spawn only | SRP: remaining waypoint and inventory-box corrections, smart terrain capacities | the ones that crash are done (`cs.crash.all-spawn-errors`); the rest are clean-ups |
| SRP's own regressions | every "Fixed an SRP vX regression" entry | never existed in retail |
| Found, traced, unreachable | old crow-killer/shooting dialogs, `val_join_freedom`, `set_actor_community`, unused trade lists, cut Dead City / arena logic | nothing in the shipped game reaches them |
| Found, left as shipped | CoP `zat_b7_duty_illicit_dealer_b5.ltx` malformed `combat_ignore_cond` | repairing changes a quest fight that works |
| Our own remedy, unobserved | `cs.crash.smart-terrain-no-free-job` (job sharing), `cs.save.npc-spawn-before-player` (deferred set-up) | in the catalogue, but their behaviour has not been seen in the game: first things to look at in a play test |

Scenes looked at one by one after the owner asked for them (2026-10-02), and why they stay out:

| Scene | What was found | Result |
|---|---|---|
| Limansk bridge squad firing at the house | retail takes the squad off these posts when the machine gun dies and a retail restrictor sends it to storm the house; SRP removed that and built its own cease-fire states, and its later "keeps firing" entries repair that redesign | not a retail fault that can be cut out; nothing ported |
| Cordon bus-stop skirmish starting early | the task and both squads are identical in retail and SRP; the change sits somewhere in SRP's reworked Cordon quest line and squad simulation | no local change to take |
| Kostyan's squad attacking early | SRP: all.spawn, new game only, tied to its reworked Army Warehouses line | not ported |
| Yantar factory defence flow | SRP rewrote the stalkers' logic files (150+ changed lines each); the retail typos in that scene are fixed (`cs.logic.yantar-factory-scene-typos`) | rewrite not ported |
| Wet surfaces shaders, DX9 `vert.ps` | re-tuning of the effect / the engine's own entry-point convention | not faults; the MSAA edge shader is fixed (`cs.render.msaa-edge-compare`, #252) |
| SRP sounds, textures, models | 26 replacements (25 weapon sounds for SRP's own weapon configs, the Limansk bridge model), 17 new files no retail config uses | only the bridge model repairs a retail fault; it needs a download source for binary content (open decision) |

New entries from here on need evidence the sources do not give: real crash logs (the Game Doctor collects them) or
a play test.

## Method: faults confirmed by GSC (Enhanced Edition)

`fixes extract TARGET GAME_DIR OUT --archives-only scripts/ configs/` dumps a game as shipped; `tools/ee_diff.py RETAIL EE`
prints what differs between the retail and the Enhanced Edition scripts with comments, whitespace and encoding
ignored. A guard or a corrected condition that GSC added in the EE proves the retail fault. The fix is then written
for the retail file by us (the EE engine has functions retail lacks, so EE code is not copied) and gets an EE variant
only when the EE file still has the fault.

Candidates seen in the diffs and deliberately not ported:

| Game | File | What GSC changed | Why not |
|---|---|---|---|
| SoC | `xr_logic.script` | logic initialised before the player exists; `selective` as an alias of `active` | the EE callers retry; retail callers do not, so the object would stay without logic |
| SoC | `dialogs_military.script` | reward item of a Freedom dialog | a reward change, not a fault |
| CS | `sound_manager.script` | nil guards around the EE's new sound API | the guarded calls do not exist in retail |
| CoP | `dialogs.script` (`jup_a10` autosave) | extra condition `jup_a10_vano_give_task` | no retail script or config gives that info portion |
| CoP | `outro_cond.script` | Noah's slide also needs `zat_b18_noah_dog_death` to be absent | same: the info portion does not exist in retail |
| CS | `w_spas12_up.ltx`, `w_wincheaster1300_up.ltx` (SRP) | accuracy upgrade no longer removes 2 rounds | GSC kept the −2 in the EE: balance, not a fault |
| CS | `xr_wounded.script` (SRP) | start time of the heavily wounded state saved differently | changes what is written into the save |
| CS | `val_sr_quest_night_bloodsucker.ltx` (SRP) | task also fails by day after the hunt is done | makes the task stricter; not a fault |
| SoC | `dialogs_darkvalley` (ZRP) | guard against paying the bandit toll twice | without the rest of ZRP's dialog rework the phrase can dead-end |
| SoC | `sar_decoding.ltx`, `m_flesh.ltx` (ZRP) | decoder timer guard; missing comma in a velocity line | not proven that the retail lines misbehave |
| CoP | `zat_b7_duty_illicit_dealer_b5.ltx` (own check) | five `combat_ignore_cond` lines lack a `}` | repairing them changes who fights whom in a quest fight that works as shipped |
| CoP / CS / SoC | unreachable code found by `tools/lua_globals.py` | `sim_board:set_actor_community` (CoP), negative ammo tooltip and second `unregister_squad` (CS), `barman_need_kill_veterans` (SoC) | nothing in the shipped game reaches them |

## Change log

- 2026-10-02 — closing batch (#248) and the disposition of what is left (section "What is left and why" above):
  CS `cs.crash.capture-task-missing-squad` 1.1.0 (capture counter-attack and defend call-time guards),
  `cs.logic.upgrade-task-id-leak` 1.2.0 (mechanic restored on load), `cs.logic.wounded-autoheal-after-load`,
  `cop.logic.wounded-autoheal-after-load`, `cs.logic.dark-valley-holdup-after-base` 1.1.0 (not before the Fang lead).
  Totals after #248: SoC 35 (EE 19), CS 68 catalogued / 66 recommended (EE 46), CoP 36 / 22 (EE 25).
- 2026-10-02 — two more CS batches after the owner asked for everything that can be done: #250 (stalker set-up waits for the
  player, Hog's death after the task, Compass order, Red Forest bridge unlock, delayed-defend guards) and #251 (attacking
  squads skip post jobs, offline fights reach idle squads, counter-attacks leave scripted squads). CS 73 / 71 (EE 50).
- 2026-10-02 — #252: `cs.render.msaa-edge-compare`; `FIX-AUDIT-2026-10-02.md` — per fix the game function, its users, the effect
  for the player and the depth of the check. CS 74 / 72. From now on every fix gets such a record in its PR.

- 2026-10-02 — five more batches (#240–#244), 18 new fixes and one extended:
  - CS from the SRP fault list (#240): mutant killed by nobody (`mob_death`), attack logic key `agressive`, Freedom exo
    bleeding sign, carry-weight tooltip of five suits, Dark Valley hold-up after reaching the base.
  - SoC from the ZRP diff (#241): `xr_remark` global `st`, guarded zone without attacker, X-18 danger key, Shell artefact name.
  - CoP by our own checkers (#242, nothing lists these): Kopachi zombie squad condlist, laundry second door section,
    Gauss squad `walker@base_1`, X-8 poltergeist line without `=`.
  - New tool `tools/lua_globals.py` (#243): globals read but never defined. SoC `mob_death` and `xr_hit` crashes, CS
    `xr_remark` crash; `cs.crash.sim-combat` 1.1.0 also repairs offline damage that was 0 for squads weaker than 3.
  - CS Yantar factory scene (#244): three misspelt info portions, found by `tools/check_infos.py`; also
    `tools/check_condfuncs.py`. Their other hits are cut content (traced one by one).
  - CS (#245): offered faction-war tasks no longer become active on load (`task_manager.script`, as SRP and the EE do);
    Flea Market basement scene no longer deletes story object 700 after it is gone (SRP's remedy, cause not proven).
    Totals after #245: CS 63 catalogued / 61 recommended (EE 41).
  - CS (#246): `cs.crash.smart-terrain-no-free-job` — an NPC with no free job shares a held one instead of the
    'Insufficient smart_terrain jobs' abort. Our own remedy, behaviour not observed in the game. CS 64 / 62 (EE 42).
  - CS (#247): anomaly fields save only their own artefacts (`cs.crash.anomaly-zone-missing-artefact` 1.1.0), bloodsucker
    PDA hand-in, Orest's task chain (`on_reversed` + `3_2`/`2_3` typo), Sakharov's `11.5x70` ammo names. Tools
    `check_module_calls.py`, `check_trade_items.py`. CS 67 / 65 (EE 45).
  - Totals after #244: CS 62 catalogued / 60 recommended (EE 40). Before it, after #243: SoC 35 (EE 19), CS 61 catalogued / 59 recommended (EE 39), CoP 35 catalogued / 21 recommended (EE 24).
    Every preset was installed into and removed from a pristine copy of the real install. Nothing was run in the games.

- 2026-10-02 — remaining candidates (#237): `retailOnly` patches (an EE variant skips them), CS stash flag order /
  upgrade-task ids / illegal state, SoC patrol without commander / Agroprom exit / Dark Valley scene, CoP Goldfish once.
  `tools/fix_realcheck.sh` repeats the real-install round trip for all three games (SoC 29, CS 53, CoP 17 fixes).

- 2026-10-02 — CS logic batch (#233): four more EE-confirmed faults; preset (51 fixes) installed into and removed from
  a pristine copy of the real install.

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
