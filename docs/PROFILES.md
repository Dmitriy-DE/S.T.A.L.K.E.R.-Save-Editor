# Toolkit environment, profiles and snapshots

## Managed snapshots

The Toolkit Environment screen can create, inspect, restore and delete snapshots for an explicitly selected X-Ray installation. It records only the Game Fix and Companion provider files/manifests, plus the ownership manifest and explicit overrides for an existing `user.ltx` selected through the settings provider. File contents are stored once under SHA-256 object names in the application's data directory; snapshot metadata lists the exact provider and relative path for each object.

Restore first creates a safety snapshot, checks provider hashes for drift, then reconciles through `GameFixEngine`, `CompanionInstaller` and `ManagedUserLtxSettings`. Companion manifests and backups are reinstated through its existing installer provider after the bundled payload has been replayed. Snapshot objects are integrity checked and are never copied directly into a game folder. Delete removes the snapshot metadata and prunes only objects no remaining snapshot references.

A snapshot is bound to the resolved installation path. Restore can fail when the current Steam build, fix catalogue, Companion payload, or original file anchors differ. In that case the operation reports the conflict and retains the safety snapshot. It does not capture unrelated files, saves, Steam Cloud data, arbitrary mods, or the complete `user.ltx`; unmanaged lines in that file remain outside the snapshot.

## Profiles

Profiles are named local records of a selected game's installed Game Fix IDs, Companion on/off state and current toolkit-owned `user.ltx` overrides. The profile screen saves the current state, shows the recorded IDs/settings, applies a selected profile, and deletes profiles. Apply creates an automatic recovery snapshot first and uses the same three managed providers as snapshot restore. When a profile carries a `user.ltx` path, that resolved path is used unless the user explicitly selects another existing `user.ltx`.

Profiles do not infer or capture settings changed outside the Toolkit. A profile cannot apply a fix absent from the shipped catalogue or for a mismatched target/build. Enhanced Editions and S.T.A.L.K.E.R. 2 do not inherit original-trilogy fix or Companion state.

## Config editor

The editor resolves the default `user.ltx` location from the selected game's existing `fsgame.ltx` app-data alias, then accepts an explicit file selection. It currently supports:

- `g_fov` from 30 through 150;
- `hud_fov` from 0.2 through 1.0;
- `mouse_sens` from 0.01 through 1.0;
- the known `on`/`off` toggles `hud_crosshair`, `hud_crosshair_dist`, `hud_info`, `hud_weapon`, and `cl_dynamiccrosshair`.

The screen shows current value, the first-seen original value, and whether the Toolkit owns the current line. If no original command existed, “game default” means the engine supplied the value; the editor does not invent a numeric default. It edits one known line and preserves the other bytes and line endings. A changed owned line becomes a conflict and blocks automatic replacement or default restore.

## Install audit

The audit reports files covered by valid Game Fix/Companion manifests as toolkit-managed. A loose file is called vanilla only when its SHA-256 matches a known retail source hash for the exact detected Steam build and path; the catalogue is not a complete game baseline, so every other loose file remains unknown. Manifestless contents under Toolkit state directories are orphaned state needing review and are retained.

An active Game Fix manifest whose ID is no longer in the shipped catalogue is reported as an orphaned toolkit-owned fix. Cleanup is offered only if the expected game matches, the fix has no active dependents, every installed-file hash and recorded backup hash matches, and the Game Fix provider is the sole audited owner of each affected path. The action rechecks those conditions and uninstalls through `GameFixEngine`, restoring the recorded prior bytes. Any mismatch remains a conflict and disables cleanup; unknown and manifestless files are never removed.
