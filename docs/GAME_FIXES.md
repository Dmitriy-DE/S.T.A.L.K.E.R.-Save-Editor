# Game Fixes

Game Fixes are installed-game mutations, separate from save editing. Each definition carries a stable ID, distinct game target, supported Steam builds, category and maturity, problem/description, implementation type, exact affected paths, dependencies/conflicts, save compatibility, new-game requirement, detection method, verification state, provenance and references.

## Current catalogue

The catalogue contains one Clear Sky 1.5.10 entry: `cs.quest.dead-wild-napr`. Its patch adds an NPC death info-portion so later Flea Market tasks stop targeting Wild Napr after his death. It is categorized Essential, but marked Experimental because retail-file verification does not prove runtime behavior or save compatibility. The fix is explicitly selectable in the desktop screen and CLI; it is excluded from Essential, Recommended and All Safe presets because presets select only Validated entries. As a result, every shipped safe preset currently selects zero fixes for every target.

No fix is inferred for SoC, CoP, the three Enhanced Editions, or S2. Original and Enhanced Edition targets have independent catalogue entries and Steam build gates. Research decisions and source evidence are recorded in [GAME_FIX_RESEARCH.md](GAME_FIX_RESEARCH.md).

The engine currently implements exact single-byte text transformations. Structured, binary and generic overlay definitions are rejected until there is a provider for them. Production installation rejects definitions whose verification state is only Research or SyntheticTests. Retail-file verification is distinct from in-game verification.

## Shared game-file mutation layer

Companion installation and Game Fixes share `Patching/IGameFileSystem`, `PhysicalGameFileSystem` and `AtomicGameFileWriter`. Each provider keeps domain-specific planning and manifests. Game Fixes store per-file before/after SHA-256, original-byte backups, source/build metadata and state in:

    <game>/.save-editor-game-fixes/<fix-id>/manifest.json
    <game>/.save-editor-game-fixes/<fix-id>/backups/

Writes use a sibling temporary file followed by atomic replacement. Install validates the selected game marker, exact matching Steam build ID, source SHA when specified and a unique exact text anchor. When the target exists only in an X-Ray archive, the engine reads the effective archived source and writes a loose `gamedata` overlay; it never rewrites that archive. Uninstall deletes an overlay created by the fix or restores the prior loose file only after hash checks.

Uninstall restores only if the current file still matches the installed hash and the backup matches its original hash. It refuses to overwrite drift, removes dependencies only after dependents, and rolls back already-written files if a later operation fails. Empty state created by a failed new install is cleaned up.

Companion and Game Fixes inspect one another's manifests before mutation. If either provider claims the same file, the second operation fails before writing; stacked transformations are unsupported. A Companion marker without a valid manifest also blocks a Game Fix on that path. This prevents silent overwrites in either installation order.

An explicit update action is available when the installed manifest version differs from the catalogue. It requires a strictly increasing numeric version, matching target build, unchanged managed-file set, intact installed hashes and valid original backups. The engine removes the previous layer, validates the new patch against the recovered source, installs it, and restores the previous active patch/manifest if the new install fails. Updates that change managed paths or encounter drift are refused. Catalogue rollback/downgrade is blocked.

## Desktop and CLI

The desktop screen lists definitions, categories, maturity, source/build details and current managed-file state. Install and remove require an explicitly selected target folder and fix; installation also requires a fresh compatibility check for the selected build. An update action appears when an installed manifest is older than the catalogue and uses the same compatibility gate. If a managed file changed after install, update/removal is refused rather than overwriting it.

    stalker-save-editor-cli fixes list [--game cs] [--json]
    stalker-save-editor-cli fixes status cs "/path/to/Clear Sky" [--json]
    stalker-save-editor-cli fixes apply-preset recommended cs "/path/to/Clear Sky" [--json]
    stalker-save-editor-cli fixes install <id> "/path/to/game"
    stalker-save-editor-cli fixes update <id> "/path/to/game"
    stalker-save-editor-cli fixes remove <id> "/path/to/game"

`list` reports catalogued definitions only. `status` distinguishes available catalogue entries, safe preset recommendations and installed manifests. `apply-preset` supports Essential only, Recommended, and All safe fixes; Custom remains an explicit per-fix selection. Recommended is the default recommendation, but nothing is applied unless the user invokes an action. Preset application requires a structurally valid selected install and, for non-empty selections, a Steam build supported by every selected definition. It preflights managed-file drift and rolls back only fixes newly installed by that batch if a later install fails. An empty safe preset reports zero selected fixes and creates no installation state.

## Boundaries

- The current app installer was not changed to expose Game Fixes as a separate installable package component; there is no validated safe payload to apply from setup.
- No automatic “fix safe issues” action is available. The only payload is experimental and requires explicit per-fix selection.
- Configuration mutations, profile activation and their transactions are future users of the shared low-level filesystem layer, not implemented providers.
- The retail archive copy round trip verifies file targeting and rollback; it does not prove a game accepts the overlay or that an affected save is repaired.

## Reliability evidence

- **L1:** Core tests cover build and source gates, archive-to-loose overlays, manifests, rollback, drift refusal, dependency/conflict checks, and empty safe presets.
- **L2:** CLI and desktop view-model tests cover explicit actions, status reporting and preset behavior using synthetic installations.
- **L3:** the packaged NativeAOT CLI was smoke-tested for an empty Recommended preset only; no packaged Experimental fix install/remove was performed.
- **L4–L5:** no live game workflow, issue reproduction, or in-game acceptance has been performed.
