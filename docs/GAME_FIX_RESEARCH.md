# Game-fix research ledger

This ledger records the upstream pack, exact supported retail source, disposition, and the shipped catalogue IDs. `RetailFilesVerified` means each operation was checked against an exact archived source SHA-256 and unique anchor; it does not mean the game was launched or the defect was reproduced in-game.

## Implemented

| Target and source | Retail evidence | Implemented result |
|---|---|---|
| Clear Sky 1.5.10, [SRP v1.1.5](https://github.com/Decane/SRP/blob/dbf6a4bf954ef2de1861ec46eb0eb5aed3b3a5cc/SRP%20v1.1.5%20-%20Version%20History.txt), commit `dbf6a4bf954ef2de1861ec46eb0eb5aed3b3a5cc` | Local Steam build `11450472`; effective loose/archive files were extracted read-only. Every patch has an exact source hash and a unique text anchor. | 25 definitions. 2 Essential, 21 Recommended, 2 Community. 23 fixes are in Recommended. The two Escape PDA dialogue fixes remain Community because they modify optional level-changer dialogue. |
| Shadow of Chernobyl 1.0006, [ZRP 1.07 R5RC](https://www.metacognix.com/stlkrsoc/), 8 April 2015 | Local Steam build `11567845`; 15 distinct fixes were checked against the effective retail files and the ZRP archive. | 15 definitions: 3 Essential and 12 Recommended; all 15 are in Recommended. |
| Call of Pripyat 1.6.02, [stalker-cop-patch](https://github.com/victor-homyakov/stalker-cop-patch/tree/3835ee1b7829fe7c7712af1a7ff97e85e46a906c), commit `3835ee1b7829fe7c7712af1a7ff97e85e46a906c`, and PRP v1.2 from the [project page](https://www.moddb.com/mods/pripyat-reclamation-patch) and its [author-linked public folder](https://www.mediafire.com/folder/lo9xufb1io6av/PRP) | Local Steam build `11450453`; 43 exact operations across 24 definitions were checked against effective retail file hashes and upstream payloads. The PRP v1.2 archive SHA-256 is `d4c5b8c3836b7d51b0b175aeb1d5e18896f99dc27f5a7e79955f72638358d598`. The selected PRP payloads were byte-compared across v1.1 and v1.2. Russian text overlays use code page 1251. | 10 Recommended and 14 Community. Five PRP fixes are narrowly ported to Recommended: crow counter, X8 burer health, Jupiter scanner task gate, altered-insulator door gate, and R2/R3 sky transform. Knife hit distance/radius is available as an individual Community fix because its gameplay-balance impact lacks an independent defect fixture. The blast-value edits also remain Community because they change balance. |

### Clear Sky IDs

**Essential:** `cs.quest.dead-wild-napr`, `cs.quest.wolf-offline-cancellation`.

**Recommended:** `cs.quest.flood-underground-duty-goodwill`, `cs.ai.snork-aggression-key`, `cs.ai.agroprom-bloodsucker-aggression`, `cs.ai.agroprom-dogs-aggression`, `cs.ai.agroprom-scientist-bloodsucker-aggression`, `cs.ai.agroprom-snork-wave-aggression`, `cs.ai.escape-rescue-dog-aggression`, `cs.ai.military-dog-aggression`, `cs.ai.red-forest-bloodsucker-aggression`, `cs.ai.limansk-sniper-look-path`, `cs.quest.skip-destroyed-limansk-minigun-task`, `cs.quest.unique-limansk-commander-task`, `cs.quest.agroprom-task-repeat-6-4`, `cs.quest.agroprom-task-repeat-2-3`, `cs.quest.agroprom-task-repeat-4-2`, `cs.quest.cancel-strelok-teleport-tasks`, `cs.quest.hospital-sniper-objective-reversal`, `cs.ai.limansk-sniper-heal-once`, `cs.ai.hospital-minigunner-danger-keys`, `cs.logic.yantar-zombie-28-section`, `cs.quest.verified-hospital-sniper-danger-keys`.

**Community:** `cs.dialog.escape-2-level-changers`, `cs.dialog.escape-4-level-changer`.

### Shadow of Chernobyl IDs

**Essential:** `soc.dialog.wounded-enemy-crash`, `soc.quest.kruglov-rescue-dialog-recovery`, `soc.quest.skull-lukash-task-after-attack`.

**Recommended:** `soc.dialog.yurik-options-out-of-order`, `soc.quest.yantar-secret-tunnel-marker`, `soc.quest.dark-valley-sacrifice-guard-release`, `soc.quest.petruha-report-once`, `soc.logic.freedom-trader-armory-meet-schemes`, `soc.logic.freedom-blockpost-meet-scheme`, `soc.logic.freedom-max-attack-meet-scheme`, `soc.logic.red-forest-combat-and-death-schemes`, `soc.config.red-forest-stash-item-assignment`, `soc.config.bar-ecologist-guard-class`, `soc.quest.freedom-reward-relation-syntax`, `soc.logic.bar-danger-hit-distance-key`.

### Call of Pripyat IDs

**Recommended:** `cop.weapon.spas12-sight-alignment`, `cop.weapon.val-sight-alignment`, `cop.dialog.correct-anomaly-name`, `cop.quest.memory-module-unlock-attribution`, `cop.dialog.gonta-after-soroka-recovered`, `cop.prp.crow-counter-guard`, `cop.prp.x8-burer-health-guard`, `cop.prp.jupiter-scanner-task-guard`, `cop.prp.altered-insulator-door-gate`, `cop.prp.sky-stretching-fix`.

**Community:** `cop.weapon.f1-blast-radius`, `cop.weapon.rgd5-blast-radius`, `cop.localization.dialog-text-corrections`, `cop.localization.jupiter-dialog-spelling`, `cop.localization.pripyat-dialog-spelling`, `cop.localization.jupiter-quest-text`, `cop.localization.pripyat-quest-text`, `cop.localization.zaton-quest-text`, `cop.localization.weapon-description-correction`, `cop.localization.upgrade-description-typo`, `cop.localization.sleep-warning-capitalization`, `cop.localization.inventory-label-punctuation`, `cop.localization.achievement-pronoun-case`, `cop.prp.knife-hit-reach`.

## Deferred or rejected candidates

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

### SoC Enhanced Edition dispositions (build `24067120`)

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

### Clear Sky Enhanced Edition dispositions (build `24067129`)

The Steam manifest identifies build `24067129`. The Core X-Ray archive reader parsed all 13 archives; all 43 target source paths were present. Fixes were classified by exact file hash or a unique expected text anchor; already-present replacements were marked not needed. This is a read-only compatibility audit: none of these fixes is enabled for the EE target or authorized for installation.

**Applicable (23):** `cs.quest.dead-wild-napr`, `cs.quest.flood-underground-duty-goodwill`, `cs.quest.wolf-offline-cancellation`, `cs.ai.agroprom-bloodsucker-aggression`, `cs.ai.agroprom-dogs-aggression`, `cs.ai.agroprom-scientist-bloodsucker-aggression`, `cs.ai.agroprom-snork-wave-aggression`, `cs.ai.escape-rescue-dog-aggression`, `cs.ai.military-dog-aggression`, `cs.ai.red-forest-bloodsucker-aggression`, `cs.ai.limansk-sniper-look-path`, `cs.quest.skip-destroyed-limansk-minigun-task`, `cs.quest.agroprom-task-repeat-6-4`, `cs.quest.agroprom-task-repeat-2-3`, `cs.quest.agroprom-task-repeat-4-2`, `cs.quest.cancel-strelok-teleport-tasks`, `cs.quest.hospital-sniper-objective-reversal`, `cs.ai.limansk-sniper-heal-once`, `cs.ai.hospital-minigunner-danger-keys`, `cs.logic.yantar-zombie-28-section`, `cs.dialog.escape-2-level-changers`, `cs.dialog.escape-4-level-changer`, `cs.quest.verified-hospital-sniper-danger-keys`.

**Not needed (2; replacement already present):** `cs.ai.snork-aggression-key`, `cs.quest.unique-limansk-commander-task`.

### Call of Pripyat Enhanced Edition dispositions (build `24067133`)

The local Steam manifest identifies build `24067133`. The Core X-Ray archive reader parsed all 13 archives; 23 of the 25 distinct target paths were found. The absent paths and anchors are left as conflicts; no original-trilogy payload is enabled for this target.

**Applicable (20):** `cop.weapon.f1-blast-radius`, `cop.weapon.rgd5-blast-radius`, `cop.weapon.spas12-sight-alignment`, `cop.weapon.val-sight-alignment`, `cop.dialog.correct-anomaly-name`, `cop.quest.memory-module-unlock-attribution`, `cop.dialog.gonta-after-soroka-recovered`, `cop.prp.x8-burer-health-guard`, `cop.prp.altered-insulator-door-gate`, `cop.localization.dialog-text-corrections`, `cop.localization.jupiter-dialog-spelling`, `cop.localization.pripyat-dialog-spelling`, `cop.localization.jupiter-quest-text`, `cop.localization.pripyat-quest-text`, `cop.localization.zaton-quest-text`, `cop.localization.weapon-description-correction`, `cop.localization.upgrade-description-typo`, `cop.localization.sleep-warning-capitalization`, `cop.localization.inventory-label-punctuation`, `cop.localization.achievement-pronoun-case`.

**Not needed (1; replacement already present):** `cop.prp.knife-hit-reach`.

**Conflicting (3):** `cop.prp.crow-counter-guard` and `cop.prp.jupiter-scanner-task-guard` have absent expected anchors; `cop.prp.sky-stretching-fix` targets two files absent from the EE archives. These require separate EE-specific review before any support decision.

## Catalogue, verification, and limits

The shipped counts are 25 Clear Sky fixes (23 Recommended), 15 SoC fixes (15 Recommended), and 24 CoP fixes (10 Recommended). Community-only fixes are available as explicit individual selections and are excluded from Essential, Recommended, and All Safe presets. Pure balance changes remain Community-only. Catalogue `2026.09.2` compares against `2026.09.1`; the CoP Recommended count increases from 5 to 10.

The CoP retail check validated 43 operations across the 24 definitions against the effective Steam 1.6.02 archive files and upstream text. PRP payloads used by these definitions were compared read-only with retail and across v1.1/v1.2; user loose overrides were excluded. No local install path or save data was copied into the repository. The SoC check validated 43 operations across 15 definitions against retail 1.0006 and the ZRP archive. Clear Sky patches were ported from SRP and guarded by the exact Steam 1.5.10 file hashes. Separate read-only passes checked all 15 SoC definitions against build `24067120`, all 25 Clear Sky definitions against build `24067129`, and all 24 CoP definitions against build `24067133`; their dispositions are listed above. EE archive matches are research only and do not add builds or write capability to the catalogue.

The test suite proves exact transformation mechanics, idempotent reinstall, uninstall byte restoration, conflict detection, manifest ownership, build gates, and preset membership. The retail-source scripts additionally checked each supported file hash and unique anchor. These are L1/L2 evidence. No live game was launched, no affected save was used, and no L3 reproduction, L4 in-game acceptance, or L5 release-candidate gameplay session is claimed.
