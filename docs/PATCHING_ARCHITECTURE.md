# Installed-game patching architecture

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

## Provider responsibilities

The common layer does not decide which game files are safe to change. Each provider owns its target model, exact plan, applicability checks, manifest and recovery rules:

- Companion resolves its packaged files, validates its game release and managed manifest, then copies or restores its own file set.
- Game Fixes select a catalogue definition, require the target marker and exact Steam build, check the effective source hash and unique patch anchor, save original bytes and record before/after hashes.
- Both providers reject path traversal, linked targets and overlap with the other provider's managed files. A Companion marker without a readable manifest blocks an ambiguous shared path.
- Game Fix uninstall refuses to overwrite post-install drift. It restores a previous loose file or removes a new loose overlay after hash validation. For an archive-only X-Ray source, the archive is read through the shared archive/file-tree reader and left unchanged.

The current filesystem seam and atomic writer are internal to Core. Providers are currently synchronous because their operations are bounded local file I/O; the desktop view-model may run them off the UI thread. The CLI uses them directly. There is no second UI-side mutation implementation.

## Extension boundary

Configuration editors, profiles and future patch types should add Core providers on this seam rather than writing game files from UI code. They still need their own parsers, target/build gates, manifest ownership, exact conflict behavior, backup format and whole-operation rollback. The existence of a shared writer does not grant a capability to mutate an unverified format.

The shared layer does not currently provide cross-provider transactions, package-installer integration, or stacked transformations. Game Fix version updates are supported only as a per-fix transaction with an increasing numeric version and the same managed file set; bulk presets remain unavailable until every selected fix can be applied and rolled back as one transaction.

## Verification

Tests use isolated temporary game directories and synthetic archive fixtures. One targeted CLI round trip used a temporary copy of a local retail Clear Sky archive; it confirmed the exact source fingerprint, loose overlay output, archive preservation and overlay removal. No live user installation or Steam Cloud state was mutated. This demonstrates file mechanics, not in-game acceptance.
