# Game-fix research ledger

This ledger separates upstream reports from changes that are safe to ship as an installable fix. A changelog entry alone does not establish the supported retail build, exact bytes, mod-layer interaction, save compatibility, or in-game acceptance.

| Target | Candidate reviewed | Source and result | Status |
|---|---|---|---|
| Clear Sky 1.5.10 | Wild Napr can remain a target for later Flea Market tasks after dying offline | [SRP v1.1.5 README](https://github.com/Decane/SRP/blob/master/SRP%20v1.1.5%20-%20Readme.txt) and [version history](https://github.com/Decane/SRP/blob/master/SRP%20v1.1.5%20-%20Version%20History.txt) describe the issue and the `on_death` info-portion change. The installed Steam build 11450472 source was inspected read-only: the effective archive entry is `configs/creatures/spawn_sections_garbage.ltx`, SHA-256 `029ddb9331d44c8c0d6aaf3a5791bbb53f7e738c8efba7fc121a30a90a0a5078`; the CRLF anchor is unique. | Implemented as `cs.quest.dead-wild-napr`, category Essential, maturity Experimental, retail-file verified only. Writes a loose overlay and leaves archives untouched. Not reproduced or accepted in-game; existing saves with a prior NPC death are unverified. |
| Clear Sky 1.5.10 | Wolf reward task may remain active after Wolf dies offline | Same SRP sources. The exact target transformation and supported file variant have not been independently isolated. | Deferred; no payload shipped |
| Clear Sky 1.5.10 | Wolf escort missions may cancel or fail immediately | Same SRP sources; behavior interacts with task state and NPC logic. | Deferred; no payload shipped |
| Clear Sky 1.5.10 | Vasyan logic may require killing him to progress | Same SRP sources; the fix changes story progression and needs a verified transformation plus save/reproduction evidence. | Deferred; no payload shipped |
| Clear Sky 1.5.10 | Faction-war stalls and task-state issues | SRP documents many changes across shared logic. The pack is not vendored and no individual transformation was isolated. | Deferred; no payload shipped |
| Shadow of Chernobyl | ZRP project and its supported retail versions reviewed | [ZRP project](https://metacognix.com/files/stlkrsoc/index.html) is a broad modular patch set. No narrow change was verified against an exact clean retail source file in this work. | Research only; no payload |
| Call of Pripyat 1.6.02 | Twelve entries from `stalker-cop-patch` enumerated: detector visuals/markers, dialog label and lines, grenade fragment radius, weapon sights/names, intro removal, memory-module attribution, typos, sorted lists and repair pricing | [Repository README](https://github.com/victor-homyakov/stalker-cop-patch) includes a mix of defect claims, visual changes and preference/economy changes. No entry was isolated against an exact clean retail file and version here; several are intentionally not categorized as bugs without stronger evidence. | Deferred; no payload |
| Call of Pripyat | PRP project packaging issue reviewed | [PRP project](https://www.moddb.com/mods/pripyat-reclamation-patch) discussion identified a missing `postprocess.ltx` entry in one PRP package revision. This is a package issue, not a demonstrated vanilla-game defect. | Rejected as a game-fix candidate |
| Enhanced Editions | Separate Steam app targets identified and installed retail files audited | All three editions have distinct `GameTarget` values and build gates. No edition-specific fix candidate was verified; original-game fixes are not inherited. | Audit only; no payload |
| S.T.A.L.K.E.R. 2 | Remove stale custom mods after Update 2.0 | [Official Update 2.0 mod FAQ](https://www.stalker2.com/news/mods-cost-of-hope-update-2-0-faq) recommends starting without stale mods and identifies `Stalker2/Content/Paks/~mods`. | Implemented as a user-triggered reversible folder move, not as a game-file fix |

## Research scope and counts

Counts below describe candidates individually recorded or enumerated in this ledger, not every bug fixed by an upstream project. The upstream packs are much broader than this bounded payload review.

| Target | Candidates reviewed | Implemented | Rejected or deferred |
|---|---:|---:|---:|
| Clear Sky 1.5.10 | 5 individually recorded issue clusters | 1 | 4 deferred |
| Shadow of Chernobyl 1.0004/1.0005/1.0006 | 1 project-level ZRP audit; 0 isolated payloads | 0 | 1 project audit deferred from payload implementation |
| Call of Pripyat 1.6.02 | 12 entries enumerated from `stalker-cop-patch`, plus 1 PRP packaging issue | 0 | 12 patch entries deferred; 1 packaging issue rejected as a vanilla bug candidate |
| Enhanced Editions | 3 distinct edition targets checked for source/build separation; 0 payload candidates verified | 0 | No edition-specific payload selected |
| S.T.A.L.K.E.R. 2 | 1 official stale-mod troubleshooting action | 0 game fixes | 1 troubleshooting action implemented outside the fix catalogue |

The SRP history documents a much larger Clear Sky surface than the five clusters broken out above, including faction-war stalls, quest progression and crash cases. Those entries are not counted as independently researched payloads here because their fixes span multiple scripts, logic files and in some cases `all.spawn`, and some require a new game. The [ZRP project overview](https://www.metacognix.com/stlkrsoc/) describes compatibility with 1.0004–1.0006 and points to change logs shipped inside its archive; that detailed archive list was not audited file by file. The [CoP patch repository](https://github.com/victor-homyakov/stalker-cop-patch) enumerates 12 mixed gameplay, text, visual and quality-of-life entries for 1.6.02; none was isolated against a clean retail source and version in this task. The [Enhanced Edition official news listing](https://steamcommunity.com/app/2427410/allnews/) is edition-specific; original-game community patches were not ported based on similar filenames or engine ancestry.

## Catalogue outcome

Five Clear Sky issue clusters were individually recorded: one has an installable exact-build payload and four remain deferred. A wider SRP history scan confirms more candidate issues, but does not promote them to isolated payloads. The SoC review was one project-level ZRP audit rather than a counted set of individual fixes. For CoP, twelve repository entries were enumerated and deferred from implementation, and one PRP package omission was rejected as a vanilla-game candidate. The Enhanced Editions were audited as three distinct targets and have no edition-specific payloads. The S2 action is troubleshooting, not a vanilla-game fix.

The Clear Sky entry is deliberately marked **Experimental** despite having a verified retail source file. Retail bytes and target selection are proven; runtime behavior, compatibility with arbitrary mod overlays, and save-state effects are not. Experimental entries are excluded from all automatic presets. The entry can be applied only after the user explicitly selects it and the exact Steam build passes the engine gate.

## Verification boundary

Tests cover archive reading, exact transformation, overlay creation/removal, build gates, manifests, rollback and drift refusal. A temporary copy of the installed retail archive was used for a CLI install/remove round trip and compared unchanged after removal. No live installation was modified. This verifies target and file handling, not game acceptance. The evidence is L1/L2; no L3 issue reproduction, L4 in-game acceptance, or L5 release-candidate game session is claimed. Before promoting maturity, reproduce the fault with an affected save, validate the result in-game, and test interactions with a populated user mod stack.
