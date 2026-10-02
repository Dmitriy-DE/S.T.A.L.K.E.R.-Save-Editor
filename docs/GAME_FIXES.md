# Game Fixes

Game Fixes are installed-game mutations, separate from save editing. Each definition carries a stable ID, distinct game target, supported Steam builds, category and maturity, problem/description, implementation type, exact affected paths, dependencies/conflicts, save compatibility, new-game requirement, detection method, verification state, provenance and references.

## Current catalogue

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

Where the fixes come from, what was not taken and why: [roadmap/FIX-PACKS.md](roadmap/FIX-PACKS.md). A record per
fix (game function, who uses it, what changes for the player, how far it was checked):
[roadmap/FIX-AUDIT-2026-10-02.md](roadmap/FIX-AUDIT-2026-10-02.md). Earlier research notes:
[GAME_FIX_RESEARCH.md](GAME_FIX_RESEARCH.md).


## Reliability evidence

- **L1:** Core tests cover build and source gates, archive-to-loose overlays, manifests, rollback, drift refusal, dependency/conflict checks, preset safety snapshots, managed settings, profiles, and install-audit ownership.
- **L2:** CLI and desktop view-model tests cover explicit actions, status reporting, preset counts and discovery using synthetic installations. The CLI all-installations path skips an unsupported build without writing.
- **L3:** every fix is installed into and removed from pristine copies of the six real installs with the CLI (`tools/fix_realcheck.sh`); every patch is re-checked against the game dumps without the editor's code (`tools/fix_regress.py`).
- **L4–L5:** no live game workflow, issue reproduction, or in-game acceptance has been performed.
