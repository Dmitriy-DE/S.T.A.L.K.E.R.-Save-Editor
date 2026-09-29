# Game Fixes

Game Fixes are installed-game mutations, separate from save editing. Each definition carries a stable ID, distinct game target, supported Steam builds, category and maturity, problem/description, implementation type, exact affected paths, dependencies/conflicts, save compatibility, new-game requirement, detection method, verification state, provenance and references.

## Current catalogue

Catalogue version `2026.09.2` contains 25 Clear Sky 1.5.10 fixes, 15 Shadow of Chernobyl 1.0006 fixes, and 24 Call of Pripyat 1.6.02 fixes. Their build IDs are respectively `11450472`, `11567845`, and `11450453`. Each definition is hash-guarded against its inspected retail source file and uses exact text anchors. There are 2/3/0 Essential, 21/12/10 Recommended, and 2/0/14 Community definitions. The Recommended preset selects 23 Clear Sky fixes, all 15 SoC fixes, and 10 CoP fixes.

The CoP Recommended set includes the existing two sight corrections, three quest/dialogue text fixes, and five narrow PRP fixes: crow-counter cleanup, X8 burer healing, Jupiter scanner task gating, the altered-insulator door gate, and sky stretching in the R2/R3 shaders. The knife hit distance/radius adjustment is available as an individual Community fix; it is excluded from the preset because its gameplay-balance impact lacks an independent defect fixture. The grenade blast-value changes and Russian proofreading edits are also Community-only. SoC 1.0004 is not supported because no exact 1.0004 retail baseline was verified; the 1.0006 payloads are not applied to it. Other PRP candidates that require binary spawn data, new optional files, broad weapon rewrites, or unverified quest-state changes remain deferred. Enhanced Edition applicability remains unverified. Full source dispositions and every catalogue ID are listed in [GAME_FIX_RESEARCH.md](GAME_FIX_RESEARCH.md).

SoC and CoP definitions are independently gated to their verified original-game builds. No fix is inferred for the three Enhanced Editions or S2; they have separate targets and require their own retail evidence. Research decisions and source evidence are recorded in [GAME_FIX_RESEARCH.md](GAME_FIX_RESEARCH.md).

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
    stalker-save-editor-cli fixes apply-preset recommended all [--steam-root "/path/to/Steam"] [--json]
    stalker-save-editor-cli fixes install <id> "/path/to/game"
    stalker-save-editor-cli fixes update <id> "/path/to/game"
    stalker-save-editor-cli fixes remove <id> "/path/to/game"

`list` reports catalogued definitions only. `status` distinguishes available catalogue entries, safe preset recommendations and installed manifests. `apply-preset` supports Essential only, Recommended, and All safe fixes; Custom remains an explicit per-fix selection. All Safe currently contains the same Essential+Recommended definitions and excludes Community changes. Recommended is the default recommendation, but nothing is applied unless the user invokes an action. Preset application requires a structurally valid selected install and, for non-empty selections, a Steam build supported by every selected definition. It preflights managed-file drift and rolls back only fixes newly installed by that batch if a later install fails. Targets without a fix for the detected build report no compatible recommendation.

The `all` form uses Game Doctor's existing installation discovery and applies the selected preset only when every fix in it supports the detected build. Desktop, explicit CLI, and installer preset actions create a Toolkit Environment safety snapshot before changing a compatible installation; the CLI reports the snapshot ID. Unsupported builds are reported as skipped and left untouched. JSON results list game IDs, build IDs, selected counts, fix IDs, safety snapshot IDs, and per-installation errors without serializing installation paths.

The Windows installer has an optional Game Fixes component. If selected, it offers Recommended (default), Essential only, All safe, and Later. Setup delegates to `stalker-save-editor-cli fixes apply-preset ... all`; Inno contains no patch operations. The desktop screen displays a preset delta when the catalogue adds fixes. For this release, CoP Recommended changes from 5 to 10 between `2026.09.1` and `2026.09.2`; Clear Sky and SoC counts are unchanged.

## Boundaries

- The installer integration is Windows-only. The Inno Setup compiler is not available in this Linux environment, so the `.iss` script has not been compiled here.
- No “fix safe issues” action runs without an explicit user command. Preset application is explicit and requires a selected compatible game installation.
- Profile activation and managed `user.ltx` changes use the Toolkit snapshot coordinator and their existing providers. Unmanaged settings are not captured or rewritten.
- The retail archive copy round trip verifies file targeting and rollback; it does not prove a game accepts the overlay or that an affected save is repaired.

## Reliability evidence

- **L1:** Core tests cover build and source gates, archive-to-loose overlays, manifests, rollback, drift refusal, dependency/conflict checks, preset safety snapshots, managed settings, profiles, and install-audit ownership.
- **L2:** CLI and desktop view-model tests cover explicit actions, status reporting, preset counts and discovery using synthetic installations. The CLI all-installations path skips an unsupported build without writing.
- **L3:** an earlier packaged NativeAOT CLI smoke test covered an empty preset; it did not verify these new retail catalogue payloads.
- **L4–L5:** no live game workflow, issue reproduction, or in-game acceptance has been performed.
