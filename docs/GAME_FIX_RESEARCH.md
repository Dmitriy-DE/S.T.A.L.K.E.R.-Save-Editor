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
| Call of Pripyat | PRP project packaging issue reviewed | [PRP project](https://www.moddb.com/mods/pripyat-reclamation-patch) discussion identified a missing `postprocess.ltx` entry in one PRP package revision. This is a package issue, not a demonstrated vanilla-game defect. | Rejected as a game-fix candidate |
| Enhanced Editions | Separate Steam app targets identified and installed retail files audited | All three editions have distinct `GameTarget` values and build gates. No edition-specific fix candidate was verified; original-game fixes are not inherited. | Audit only; no payload |
| S.T.A.L.K.E.R. 2 | Remove stale custom mods after Update 2.0 | [Official Update 2.0 mod FAQ](https://www.stalker2.com/news/mods-cost-of-hope-update-2-0-faq) recommends starting without stale mods and identifies `Stalker2/Content/Paks/~mods`. | Implemented as a user-triggered reversible folder move, not as a game-file fix |

## Catalogue outcome

Five Clear Sky issue candidates were reviewed: one has an installable, exact-build payload; four remain deferred. The SoC review was a project-level ZRP audit rather than a counted set of isolated fixes. One CoP package omission was rejected as a game-fix candidate. The Enhanced Editions were audited as distinct targets and have no payloads. The S2 action is troubleshooting, not a vanilla-game fix.

The Clear Sky entry is deliberately marked **Experimental** despite having a verified retail source file. Retail bytes and target selection are proven; runtime behavior, compatibility with arbitrary mod overlays, and save-state effects are not. Experimental entries are excluded from all automatic presets. The entry can be applied only after the user explicitly selects it and the exact Steam build passes the engine gate.

## Verification boundary

Tests cover archive reading, exact transformation, overlay creation/removal, build gates, manifests, rollback and drift refusal. A temporary copy of the installed retail archive was used for a CLI install/remove round trip and compared unchanged after removal. No live installation was modified. This verifies target and file handling, not game acceptance. The evidence is L1/L2; no L3 issue reproduction, L4 in-game acceptance, or L5 release-candidate game session is claimed. Before promoting maturity, reproduce the fault with an affected save, validate the result in-game, and test interactions with a populated user mod stack.
