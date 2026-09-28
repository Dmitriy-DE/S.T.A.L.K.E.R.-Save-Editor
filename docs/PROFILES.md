# Profiles, snapshots and rollback

## Current behavior

The application does not yet provide named game profiles or a general game-install snapshot/restore screen. No `profiles` or `snapshot` CLI command is available.

There are narrower, existing recovery paths:

- Save edits produce verified, hash-journaled backups. The Save Library can compare a save with another save or its backups and restore a backup to a new copy or, after an explicit action, in place with a safety backup.
- Game Fix manifests keep original bytes per managed file. Removal restores those bytes only while the installed file and stored backup still match their recorded hashes.
- Companion keeps its own install manifest and per-file recovery data through the shared atomic game-file layer.
- S.T.A.L.K.E.R. 2 custom mods can be moved between `Stalker2/Content/Paks/~mods` and the recovery folder `Stalker2/Content/~mods.disabled`; both folders present at once are treated as a conflict and block the automatic toggle.

These component-specific records are not a single point-in-time snapshot and cannot restore unrelated third-party changes. Game Doctor inventories manifest-owned files and warns about missing or drifted files; other loose files remain unclassified because retail baselines are not bundled.

## Safety requirements before adding profiles

A future profile must describe only explicit, toolkit-owned settings and fix IDs. Activation needs a complete preflight, a collision-free ownership plan across Companion/Game Fix/config providers, verified before/after hashes, same-directory atomic writes, a durable recovery journal, and rollback that refuses external drift. Profile activation must not infer settings from arbitrary mods or change Steam Cloud state. A profile must never claim that unrelated files were snapshotted when they were only enumerated.
