# Save Doctor

Save Doctor is a read-only structural inspection surface for one selected save. The desktop screen can open a save file directly or inherit the selected library save. The CLI supports:

    stalker-save-editor-cli doctor save "/path/to/save.sav"
    stalker-save-editor-cli doctor save "/path/to/save.sav" --json

The service uses the same supported format readers as Save Editor. A successful result means that the known container, framing, checksum/decompression, and parsed record checks accepted the file. It reports the detected format and parsed inventory record count.

Semantic quest reachability, missing-object references, and game-specific broken states remain unknown. The Save Doctor labels quest/task state as unavailable because the supported readers do not expose validated task-state fields. The CLI command `doctor quest SAVE_FILE [--json]` returns this format-specific coverage result and an empty task list; it does not infer a state or link a fix without reader evidence. The app has no validated issue signatures for those conditions. No save repair is offered, and analysis never writes or backs up the selected file.

The Quest Doctor currently reports that task states are unavailable for every reader it can parse. Save repair remains disabled because there is no real affected-save fixture paired with game-code evidence for a repair. Existing save-edit writes continue through EditService, explicit preparation, SHA checks, and the existing backup/recovery workflow. No new writer or save capability was added.

## Reliability boundary

- **L1:** checked-in X-Ray and S2 fixtures verify supported-format success, invalid input, unknown semantic status, and unavailable quest-state reporting.
- **L2:** desktop view-model, Quest Doctor CLI JSON, and Save Doctor CLI paths are covered by tests.
- **L3–L5:** no packaged-app audit, retail save corpus audit, quest repro, or in-game repair validation has been performed.
