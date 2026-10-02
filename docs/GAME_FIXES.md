# Game fixes

## Game Fixes

Game Fixes are installed-game mutations, separate from save editing. Each definition carries a stable ID, distinct game target, supported Steam builds, category and maturity, problem/description, implementation type, exact affected paths, dependencies/conflicts, save compatibility, new-game requirement, detection method, verification state, provenance and references.

### Current catalogue

Catalogue version `2026.10.1`:

| Game (Steam build) | Fixes | Essential | Recommended | Community | In the Recommended preset | Enhanced Edition variants (build) |
|---|---|---|---|---|---|---|
| Shadow of Chernobyl 1.0006 (`11567845`) | 35 | 12 | 23 | 0 | 35 | 20 (`24067120`) |
| Clear Sky 1.5.10 (`11450472`) | 75 | 28 | 45 | 2 | 73 | 51 (`24067129`) |
| Call of Pripyat 1.6.02 (`11450453`) | 36 | 8 | 14 | 14 | 22 | 25 (`24067133`) |

Each definition is bound to the SHA-256 of the file the game actually uses (a patch archive overrides the base
archives) and uses exact text anchors; one Clear Sky entry edits `all.spawn` structurally. An Enhanced Edition variant
exists only where the EE file still has the fault; it is the same change with the EE file's hash. Community entries
(balance, sights, Russian proofreading) are never part of a preset. SoC 1.0004 and S.T.A.L.K.E.R. 2 have no fixes.

Where the fixes come from, what was not taken and why: the section "Fix packs" below. A record per
fix (game function, who uses it, what changes for the player, how far it was checked):
[roadmap/FIX-AUDIT-2026-10-02.md](roadmap/FIX-AUDIT-2026-10-02.md). Earlier research notes:
[GAME_FIX_RESEARCH.md](GAME_FIXES.md).


### Reliability evidence

- **L1:** Core tests cover build and source gates, archive-to-loose overlays, manifests, rollback, drift refusal, dependency/conflict checks, preset safety snapshots, managed settings, profiles, and install-audit ownership.
- **L2:** CLI and desktop view-model tests cover explicit actions, status reporting, preset counts and discovery using synthetic installations. The CLI all-installations path skips an unsupported build without writing.
- **L3:** every fix is installed into and removed from pristine copies of the six real installs with the CLI (`tools/fix_realcheck.sh`); every patch is re-checked against the game dumps without the editor's code (`tools/fix_regress.py`).
- **L4–L5:** no live game workflow, issue reproduction, or in-game acceptance has been performed.

---

## Installed-game patching architecture

Installed-game file mutations live in `StalkerSaveEditor.Core.Patching`, separate from save-file serialization and editing. The desktop app and CLI call the same Core providers; Avalonia is not referenced by the patching code.

```text
Desktop / CLI
      ↓
Core patch providers
  ├── Companion installer
  └── Game Fix engine
      ↓
IGameFileSystem + AtomicGameFileWriter
      ↓
Physical filesystem
```

`IGameFileSystem` is the low-level I/O seam shared inside Core. `PhysicalGameFileSystem` adapts the host filesystem, while tests can supply controlled implementations. `AtomicGameFileWriter` writes a complete sibling temporary file and renames it into place. It is intentionally distinct from save writers, whose framing, checksums, backups and capability gates follow game-specific save contracts.

### Provider responsibilities

The common layer does not decide which game files are safe to change. Each provider owns its target model, exact plan, applicability checks, manifest and recovery rules:

- Companion resolves its packaged files, validates its game release and managed manifest, then copies or restores its own file set.
- Game Fixes select a catalogue definition, require the target marker and exact Steam build, check the effective source hash and unique patch anchor, save original bytes and record before/after hashes.
- Both providers reject path traversal, linked targets and overlap with the other provider's managed files. A Companion marker without a readable manifest blocks an ambiguous shared path.
- Game Fix uninstall refuses to overwrite post-install drift. It restores a previous loose file or removes a new loose overlay after hash validation. For an archive-only X-Ray source, the archive is read through the shared archive/file-tree reader and left unchanged.

The current filesystem seam and atomic writer are internal to Core. Providers are currently synchronous because their operations are bounded local file I/O; the desktop view-model may run them off the UI thread. The CLI uses them directly. There is no second UI-side mutation implementation.

### Extension boundary

Configuration editors, profiles and future patch types should add Core providers on this seam rather than writing game files from UI code. They still need their own parsers, target/build gates, manifest ownership, exact conflict behavior, backup format and whole-operation rollback. The existence of a shared writer does not grant a capability to mutate an unverified format.

The shared layer does not provide cross-provider transactions or stacked transformations. Game Fix version updates are supported only as a per-fix transaction with an increasing numeric version and the same managed file set. Essential-only, Recommended and All-safe presets use a Game Fix batch transaction: all selected definitions are preflighted, newly installed fixes are rolled back if a later install fails, and fixes that were already installed are left in place. Windows setup delegates preset application to the CLI and contains no patch logic; the installer operation itself is not transactionally coupled to application-file installation. The catalogue contains retail-file-verified fixes for Clear Sky 1.5.10, SoC 1.0006 and CoP 1.6.02. The safe presets include only Essential and Recommended categories; Community fixes require individual selection. SoC 1.0004 and unsupported builds have no applicable payload.

Game Doctor reads the Companion and Game Fix manifests through their providers to show per-file ownership and hash state. It marks other loose files as unclassified because the toolkit does not include complete retail baselines. The audit is read-only and does not attempt to resolve third-party conflicts.

### Verification

Tests use isolated temporary game directories and synthetic archive fixtures. One targeted CLI round trip used a temporary copy of a local retail Clear Sky archive; it confirmed the exact source fingerprint, loose overlay output, archive preservation and overlay removal. No live user installation or Steam Cloud state was mutated. This demonstrates file mechanics, not in-game acceptance.

---

## Game-fix research ledger

This ledger records the upstream pack, exact supported retail source, disposition, and the shipped catalogue IDs. `RetailFilesVerified` means each operation was checked against an exact archived source SHA-256 and unique anchor; it does not mean the game was launched or the defect was reproduced in-game.

### Implemented

| Target and source | Retail evidence | Implemented result |
|---|---|---|
| Clear Sky 1.5.10, [SRP v1.1.5](https://github.com/Decane/SRP/blob/dbf6a4bf954ef2de1861ec46eb0eb5aed3b3a5cc/SRP%20v1.1.5%20-%20Version%20History.txt), commit `dbf6a4bf954ef2de1861ec46eb0eb5aed3b3a5cc` | Local Steam build `11450472`; effective loose/archive files were extracted read-only. Every patch has an exact source hash and a unique text anchor. | 25 definitions. 2 Essential, 21 Recommended, 2 Community. 23 fixes are in Recommended. The two Escape PDA dialogue fixes remain Community because they modify optional level-changer dialogue. |
| Shadow of Chernobyl 1.0006, [ZRP 1.07 R5RC](https://www.metacognix.com/stlkrsoc/), 8 April 2015 | Local Steam build `11567845`; 15 distinct fixes were checked against the effective retail files and the ZRP archive. | 15 definitions: 3 Essential and 12 Recommended; all 15 are in Recommended. |
| Call of Pripyat 1.6.02, [stalker-cop-patch](https://github.com/victor-homyakov/stalker-cop-patch/tree/3835ee1b7829fe7c7712af1a7ff97e85e46a906c), commit `3835ee1b7829fe7c7712af1a7ff97e85e46a906c`, and PRP v1.2 from the [project page](https://www.moddb.com/mods/pripyat-reclamation-patch) and its [author-linked public folder](https://www.mediafire.com/folder/lo9xufb1io6av/PRP) | Local Steam build `11450453`; 43 exact operations across 24 definitions were checked against effective retail file hashes and upstream payloads. The PRP v1.2 archive SHA-256 is `d4c5b8c3836b7d51b0b175aeb1d5e18896f99dc27f5a7e79955f72638358d598`. The selected PRP payloads were byte-compared across v1.1 and v1.2. Russian text overlays use code page 1251. | 10 Recommended and 14 Community. Five PRP fixes are narrowly ported to Recommended: crow counter, X8 burer health, Jupiter scanner task gate, altered-insulator door gate, and R2/R3 sky transform. Knife hit distance/radius is available as an individual Community fix because its gameplay-balance impact lacks an independent defect fixture. The blast-value edits also remain Community because they change balance. |

#### Clear Sky IDs

**Essential:** `cs.quest.dead-wild-napr`, `cs.quest.wolf-offline-cancellation`.

**Recommended:** `cs.quest.flood-underground-duty-goodwill`, `cs.ai.snork-aggression-key`, `cs.ai.agroprom-bloodsucker-aggression`, `cs.ai.agroprom-dogs-aggression`, `cs.ai.agroprom-scientist-bloodsucker-aggression`, `cs.ai.agroprom-snork-wave-aggression`, `cs.ai.escape-rescue-dog-aggression`, `cs.ai.military-dog-aggression`, `cs.ai.red-forest-bloodsucker-aggression`, `cs.ai.limansk-sniper-look-path`, `cs.quest.skip-destroyed-limansk-minigun-task`, `cs.quest.unique-limansk-commander-task`, `cs.quest.agroprom-task-repeat-6-4`, `cs.quest.agroprom-task-repeat-2-3`, `cs.quest.agroprom-task-repeat-4-2`, `cs.quest.cancel-strelok-teleport-tasks`, `cs.quest.hospital-sniper-objective-reversal`, `cs.ai.limansk-sniper-heal-once`, `cs.ai.hospital-minigunner-danger-keys`, `cs.logic.yantar-zombie-28-section`, `cs.quest.verified-hospital-sniper-danger-keys`.

**Community:** `cs.dialog.escape-2-level-changers`, `cs.dialog.escape-4-level-changer`.

#### Shadow of Chernobyl IDs

**Essential:** `soc.dialog.wounded-enemy-crash`, `soc.quest.kruglov-rescue-dialog-recovery`, `soc.quest.skull-lukash-task-after-attack`.

**Recommended:** `soc.dialog.yurik-options-out-of-order`, `soc.quest.yantar-secret-tunnel-marker`, `soc.quest.dark-valley-sacrifice-guard-release`, `soc.quest.petruha-report-once`, `soc.logic.freedom-trader-armory-meet-schemes`, `soc.logic.freedom-blockpost-meet-scheme`, `soc.logic.freedom-max-attack-meet-scheme`, `soc.logic.red-forest-combat-and-death-schemes`, `soc.config.red-forest-stash-item-assignment`, `soc.config.bar-ecologist-guard-class`, `soc.quest.freedom-reward-relation-syntax`, `soc.logic.bar-danger-hit-distance-key`.

#### Call of Pripyat IDs

**Recommended:** `cop.weapon.spas12-sight-alignment`, `cop.weapon.val-sight-alignment`, `cop.dialog.correct-anomaly-name`, `cop.quest.memory-module-unlock-attribution`, `cop.dialog.gonta-after-soroka-recovered`, `cop.prp.crow-counter-guard`, `cop.prp.x8-burer-health-guard`, `cop.prp.jupiter-scanner-task-guard`, `cop.prp.altered-insulator-door-gate`, `cop.prp.sky-stretching-fix`.

**Community:** `cop.weapon.f1-blast-radius`, `cop.weapon.rgd5-blast-radius`, `cop.localization.dialog-text-corrections`, `cop.localization.jupiter-dialog-spelling`, `cop.localization.pripyat-dialog-spelling`, `cop.localization.jupiter-quest-text`, `cop.localization.pripyat-quest-text`, `cop.localization.zaton-quest-text`, `cop.localization.weapon-description-correction`, `cop.localization.upgrade-description-typo`, `cop.localization.sleep-warning-capitalization`, `cop.localization.inventory-label-punctuation`, `cop.localization.achievement-pronoun-case`, `cop.prp.knife-hit-reach`.

### Deferred or rejected candidates

| Target | Candidate | Disposition and reason |
|---|---|---|
| SoC 1.0004 | ZRP fixes | **Deferred.** The local retail archive evidence covers Steam 1.0006 only. The ZRP project supports 1.0004, but no exact local 1.0004 baseline/hash was available to authorize installation. The 1.0006 payloads are not reused against it. |
| SoC 1.0005 | ZRP fixes | **Deferred.** No exact retail baseline was available. |
| CoP PRP Base Fix: anomaly artifact spawning | Lua anomaly-binder recovery logic | **Deferred.** PRP adds counter-range recovery that toggles zone state and clears shared artefact-zone bookkeeping. It has exact source anchors, but no affected-save fixture or live reproduction was available to validate that state reset safely repairs an existing spawn state. |
| CoP PRP Base Fix: Berill suit upgrade | `configs/misc/outfit_upgrades/optional/o_cs_heavy_outfit_up.ltx` | **Deferred.** The PRP file is an added optional payload with no matching retail source file, so there is no exact vanilla hash to guard a whole-file addition against. |
| CoP PRP Base Fix: crow cleanup | `scripts/bind_crow.script` | **Implemented** as `cop.prp.crow-counter-guard`; protects both net-destroy and death-callback decrements with registration and zero-floor checks. The patched Lua parses with `luac5.1 -p`. |
| CoP PRP Base Fix: X8 burers | `configs/scripts/labx8/lx8_burers.ltx` | **Implemented** as `cop.prp.x8-burer-health-guard`; removes only the three hit callbacks that fully restore health when the actor is outside the restriction. |
| CoP PRP Base Fix: ironsight alignment | Weapon `.ltx` files | **Partly covered, remainder deferred.** The existing CoP patch's SPAS-12 and AS Val definitions port two isolated sight corrections. PRP rewrites many weapon files with additional aiming, fire-point and formatting edits; those broad file deltas were not imported or split into further fixes without independent review. |
| CoP PRP Base Fix: Jupiter scanners | `configs/scripts/jupiter/jup_b32_sr_scanners.ltx` | **Implemented** as `cop.prp.jupiter-scanner-task-guard`; adds the task-start condition only to scanner fields 4 and 5. |
| CoP PRP Base Fix: knife range | `configs/weapons/w_knife.ltx` | **Community fix** as `cop.prp.knife-hit-reach`; changes only the second hit distance and radius. It stays out of Recommended because the range change affects gameplay balance and no independent defect fixture establishes the intended behavior. |
| CoP PRP Base Fix: Kovalsky one-shot | `configs/scripts/pripyat/pri_a16_kovalski_start.ltx` | **Deferred.** The PRP file changes multiple dialogue and quest transitions. Without an affected quest-state fixture or cutscene reproduction, the relevant state-machine edit cannot be isolated confidently. |
| CoP PRP Base Fix: muzzle flash | Weapon `.ltx` files | **Deferred.** Weapon files contain interleaved aim, fire-point and other edits; no standalone muzzle-only transformation was established from the PRP delta. |
| CoP PRP Base Fix: Pripyat base | Multiple quest, AI and dynamic-door `.ltx` files | **Deferred.** This is a broad multi-file behavior pack. Only the independent Jupiter scanner and altered-insulator door changes are included; the remaining story-state rewrites lack an affected-state fixture or runtime verification. |
| CoP PRP Base Fix: sky stretching | `shaders/r2/sky2.vs`, `shaders/r3/sky2.vs` | **Implemented** as `cop.prp.sky-stretching-fix`; changes only the vertical component of the shared sky-position transform in both shader profiles. Shader compilation and visual acceptance were not available. |
| CoP PRP Base Fix: unreachable stashes | `spawns/all.spawn` | **Deferred.** The upstream target is binary and the Game Fix engine's supported exact-text operation cannot safely parse or patch this spawn container. |
| CoP PRP Other Fix: altered-insulator door | `configs/scripts/jupiter/jup_b1_door.ltx` | **Implemented** as `cop.prp.altered-insulator-door-gate`; requires the existing task-start info-portion and the half-artifact item before the door enters its open state. |
| Other PRP content | NPC behavior, trader/dialogue additions, audio, UI and optional gameplay tweaks | **Deferred or excluded.** The pack explicitly mixes fixes with tweaks and mods. Broad story/AI changes were not added to the safe preset without a narrow operation and affected-state evidence; optional additions are not installed as whole-file overlays. |
| SoC Enhanced Edition build `24067120` | 15 SoC 1.0006 definitions checked against the effective EE archive files; no targeted loose overrides were present. | **12 applicable** (including `soc.logic.freedom-trader-armory-meet-schemes`, where one operation was already present and one remains applicable); **3 conflicting**: `soc.quest.yantar-secret-tunnel-marker`, `soc.logic.red-forest-combat-and-death-schemes`, and `soc.quest.freedom-reward-relation-syntax` have absent or ambiguous anchors. No original-trilogy payload was enabled for the EE target. |
| Clear Sky Enhanced Edition build `24067129` | All 13 X-Ray archives parsed with no unreadable archives; the 43 targeted source paths were found and checked. | **23 applicable**, **2 not needed**, **0 conflicting**. “Applicable” means the archived file had an exact source hash or a unique expected anchor; EE remains unsupported by the catalogue and no original-trilogy payload is enabled for this target. |
| Call of Pripyat Enhanced Edition build `24067133` | All 13 X-Ray archives parsed with no unreadable archives; 23 of 25 targeted source paths were found and checked. | **20 applicable**, **1 not needed**, **3 conflicting**. EE remains unsupported by the catalogue and no original-trilogy payload is enabled for this target. |
| S.T.A.L.K.E.R. 2 | Plain-file fixes from the listed packs | **Not applicable.** None of the reviewed SRP, ZRP, CoP community patch or PRP files is an S2 upstream payload. The reversible custom-mod-folder action belongs to Game Doctor, not this fix catalogue. |
| CoP `stalkers_upgrade_info.ltx` | Repair discount and upgrade-unlock edits | **Rejected from presets.** These modify economy/progression balance and are not narrow defect corrections. |
| CoP detector/sleep-screen textures and icon assets | Visual pack changes | **Deferred.** The current Game Fix engine supports exact text transformations only; there is no binary asset provider with verified restoration semantics. |
| CoP intro movie/tutorial edits | Remove intro sequence and adjust tutorial/audio timing | **Deferred.** The changes alter startup behavior or media timing and are not required for the narrow Recommended set. No audio asset or runtime playback verification was performed. |
| CoP README memory attribution claim | Novikov attribution | **Implemented narrowly.** The exact quest item entry does say Novikov unlocks the module; the upstream patch changes this to a route-neutral “some specialist,” fixing the Azot route contradiction. |

#### SoC Enhanced Edition dispositions (build `24067120`)

Each SoC 1.0006 catalogue fix was checked against its exact effective EE file anchors. No targeted loose-file overrides were present. No SoC payload is enabled for the Enhanced Edition target.

| Fix ID | EE disposition |
|---|---|
| `soc.dialog.wounded-enemy-crash` | Applicable |
| `soc.dialog.yurik-options-out-of-order` | Applicable |
| `soc.quest.kruglov-rescue-dialog-recovery` | Applicable |
| `soc.quest.skull-lukash-task-after-attack` | Applicable |
| `soc.quest.yantar-secret-tunnel-marker` | Conflicting: expected anchor is absent |
| `soc.quest.dark-valley-sacrifice-guard-release` | Applicable |
| `soc.quest.petruha-report-once` | Applicable |
| `soc.logic.freedom-trader-armory-meet-schemes` | Applicable; one operation is already present and the other anchor applies |
| `soc.logic.freedom-blockpost-meet-scheme` | Applicable |
| `soc.logic.freedom-max-attack-meet-scheme` | Applicable |
| `soc.logic.red-forest-combat-and-death-schemes` | Conflicting: expected anchor is absent |
| `soc.config.red-forest-stash-item-assignment` | Applicable |
| `soc.config.bar-ecologist-guard-class` | Applicable |
| `soc.quest.freedom-reward-relation-syntax` | Conflicting: one anchor is ambiguous |
| `soc.logic.bar-danger-hit-distance-key` | Applicable |

#### Clear Sky Enhanced Edition dispositions (build `24067129`)

The Steam manifest identifies build `24067129`. The Core X-Ray archive reader parsed all 13 archives; all 43 target source paths were present. Fixes were classified by exact file hash or a unique expected text anchor; already-present replacements were marked not needed. This is a read-only compatibility audit: none of these fixes is enabled for the EE target or authorized for installation.

**Applicable (23):** `cs.quest.dead-wild-napr`, `cs.quest.flood-underground-duty-goodwill`, `cs.quest.wolf-offline-cancellation`, `cs.ai.agroprom-bloodsucker-aggression`, `cs.ai.agroprom-dogs-aggression`, `cs.ai.agroprom-scientist-bloodsucker-aggression`, `cs.ai.agroprom-snork-wave-aggression`, `cs.ai.escape-rescue-dog-aggression`, `cs.ai.military-dog-aggression`, `cs.ai.red-forest-bloodsucker-aggression`, `cs.ai.limansk-sniper-look-path`, `cs.quest.skip-destroyed-limansk-minigun-task`, `cs.quest.agroprom-task-repeat-6-4`, `cs.quest.agroprom-task-repeat-2-3`, `cs.quest.agroprom-task-repeat-4-2`, `cs.quest.cancel-strelok-teleport-tasks`, `cs.quest.hospital-sniper-objective-reversal`, `cs.ai.limansk-sniper-heal-once`, `cs.ai.hospital-minigunner-danger-keys`, `cs.logic.yantar-zombie-28-section`, `cs.dialog.escape-2-level-changers`, `cs.dialog.escape-4-level-changer`, `cs.quest.verified-hospital-sniper-danger-keys`.

**Not needed (2; replacement already present):** `cs.ai.snork-aggression-key`, `cs.quest.unique-limansk-commander-task`.

#### Call of Pripyat Enhanced Edition dispositions (build `24067133`)

The local Steam manifest identifies build `24067133`. The Core X-Ray archive reader parsed all 13 archives; 23 of the 25 distinct target paths were found. The absent paths and anchors are left as conflicts; no original-trilogy payload is enabled for this target.

**Applicable (20):** `cop.weapon.f1-blast-radius`, `cop.weapon.rgd5-blast-radius`, `cop.weapon.spas12-sight-alignment`, `cop.weapon.val-sight-alignment`, `cop.dialog.correct-anomaly-name`, `cop.quest.memory-module-unlock-attribution`, `cop.dialog.gonta-after-soroka-recovered`, `cop.prp.x8-burer-health-guard`, `cop.prp.altered-insulator-door-gate`, `cop.localization.dialog-text-corrections`, `cop.localization.jupiter-dialog-spelling`, `cop.localization.pripyat-dialog-spelling`, `cop.localization.jupiter-quest-text`, `cop.localization.pripyat-quest-text`, `cop.localization.zaton-quest-text`, `cop.localization.weapon-description-correction`, `cop.localization.upgrade-description-typo`, `cop.localization.sleep-warning-capitalization`, `cop.localization.inventory-label-punctuation`, `cop.localization.achievement-pronoun-case`.

**Not needed (1; replacement already present):** `cop.prp.knife-hit-reach`.

**Conflicting (3):** `cop.prp.crow-counter-guard` and `cop.prp.jupiter-scanner-task-guard` have absent expected anchors; `cop.prp.sky-stretching-fix` targets two files absent from the EE archives. These require separate EE-specific review before any support decision.

### Catalogue, verification, and limits

The shipped counts are 25 Clear Sky fixes (23 Recommended), 15 SoC fixes (15 Recommended), and 24 CoP fixes (10 Recommended). Community-only fixes are available as explicit individual selections and are excluded from Essential, Recommended, and All Safe presets. Pure balance changes remain Community-only. Catalogue `2026.09.2` compares against `2026.09.1`; the CoP Recommended count increases from 5 to 10.

The CoP retail check validated 43 operations across the 24 definitions against the effective Steam 1.6.02 archive files and upstream text. PRP payloads used by these definitions were compared read-only with retail and across v1.1/v1.2; user loose overrides were excluded. No local install path or save data was copied into the repository. The SoC check validated 43 operations across 15 definitions against retail 1.0006 and the ZRP archive. Clear Sky patches were ported from SRP and guarded by the exact Steam 1.5.10 file hashes. Separate read-only passes checked all 15 SoC definitions against build `24067120`, all 25 Clear Sky definitions against build `24067129`, and all 24 CoP definitions against build `24067133`; their dispositions are listed above. EE archive matches are research only and do not add builds or write capability to the catalogue.

The test suite proves exact transformation mechanics, idempotent reinstall, uninstall byte restoration, conflict detection, manifest ownership, build gates, and preset membership. The retail-source scripts additionally checked each supported file hash and unique anchor. These are L1/L2 evidence. No live game was launched, no affected save was used, and no L3 reproduction, L4 in-game acceptance, or L5 release-candidate gameplay session is claimed.

---

## Our own fixes (derived from third-party fix packs) — plan and tracker

Owner decision (2026-09-30): port **all** useful third-party fixes into the editor so players do not need
external mods. Users install and remove each fix or pack themselves; originals are backed up and restored.
Authors are credited in every entry (open-source project, attribution, no sale).

### Principles (owner correction, 2026-09-30)

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

### Sources

| Game | Source | Where | Size | Status |
|---|---|---|---|---|
| CS | SRP 1.1.5 (Decane) | github.com/Decane/SRP | 506 changelog entries (319 fixes); 611 changed + 66 new files | diffed against retail; 25 individual fixes ported |
| SoC | ZRP 1.07 R5RC (NatVac) | metacognix.com/files/stlkrsoc | hundreds of fixes | change logs used as the list of known faults; 26 fixes (15 config, 8 crash, 3 logic) |
| SoC / CS / CoP | GSC's own Enhanced Edition scripts | the installed EE games | retail vs EE: `tools/ee_diff.py` | faults GSC fixed itself; the retail fix is written by us (SoC 10, CS 11 + 3 retail-only additions, CoP 6) |
| CoP | stalker-cop-patch (victor-homyakov) | github | 13 items, 24 files | 18 ported |
| CoP | Pripyat Reclamation Patch | ModDB (files deleted) | — | 6 ported earlier; source mirror needed |
| CoP EE | UCoPEEP | Steam Workshop 3487808500 | 1 gameplay change | ported (#184) |
| SoC EE | Workshop bloodsucker village fix | Steam Workshop 3487970687 | 1 script | ported, EE only (#184) |

### Engine work (not planned now)

Retail: OpenXRay-based binary patches are possible per exe version. EE: closed 64-bit DX12 engine, patched by
GSC; binary patches break on updates and Workshop forbids .dll/.exe. Only when a concrete bug cannot be fixed
in scripts.

### Tasks

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

### FP-4 progress (SRP 1.1.5 crash list)

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

### What is left and why (2026-10-02)

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

### Method: faults confirmed by GSC (Enhanced Edition)

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

### Change log

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
