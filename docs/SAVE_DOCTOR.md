# Save Doctor

Save Doctor is a read-only structural inspection surface for one selected save. The desktop screen can open a save file directly or inherit the selected library save. The CLI supports:

    stalker-save-editor-cli doctor save "/path/to/save.sav"
    stalker-save-editor-cli doctor save "/path/to/save.sav" --json

The service uses the same supported format readers as Save Editor. A successful result means that the known container, framing, checksum/decompression, and parsed record checks accepted the file. It reports the detected format and parsed inventory record count.

Semantic quest reachability, missing-object references, and game-specific broken states remain unknown. The app has no validated issue signatures for those conditions. No save repair is offered, and analysis never writes or backs up the selected file.

This screen is a structural Save Health check, not a Quest Doctor or a repair workflow. Existing save-edit writes continue through EditService, explicit preparation, SHA checks, and the existing backup/recovery workflow. No new writer or save capability was added.

## Reliability boundary

- **L1:** synthetic and checked-in save fixtures verify supported-format success, invalid input, and unknown semantic status.
- **L2:** desktop view-model and CLI JSON paths are covered by tests.
- **L3–L5:** no packaged-app audit, retail save corpus audit, quest repro, or in-game repair validation has been performed.
