# Save Doctor

Save Doctor is a read-only structural inspection surface for one selected save. The desktop screen can open a save file directly or inherit the selected library save. The CLI supports:

    stalker-save-editor-cli doctor save "/path/to/save.sav"
    stalker-save-editor-cli doctor save "/path/to/save.sav" --json

The service uses the same supported format readers as Save Editor. A successful result means that the known container, framing, checksum/decompression, and parsed record checks accepted the file. It reports the detected format and parsed inventory record count.

Semantic quest reachability, missing-object references, and game-specific broken states remain unknown. The Save Doctor labels quest/task state as unavailable because the supported readers do not expose validated task-state fields. The CLI command `doctor quest SAVE_FILE [--json]` returns the rule results below for Clear Sky and an empty list for other formats. The app has no validated issue signatures for those conditions. No save repair is offered, and analysis never writes or backs up the selected file.

Quest Doctor (Clear Sky only) checks a small list of known breaks. Each rule pairs a creature in the save's ALife registry with an actor info portion: the NPC is dead (his registry object has health of zero or less) but the info portion that the game's own task lists (`tm_garbadge.ltx`, `tm_escape.ltx`: `{+flag} reversed`) use to cancel his tasks is absent. Current rules: Wild Napr (`gar_digger_quester`, `gar_flea_market_stop_quest_line`, prevented by `cs.quest.dead-wild-napr`) and Wolf (`esc_wolf`, `esc_wolf_dead`, prevented by `cs.quest.wolf-offline-cancellation`); both come from the SRP v1.1.5 history. A missing NPC object or an unreadable actor info list is reported as unknown, never as broken. `QuestDoctor.PrepareRepair` builds the fixed save bytes through `XRayInfoPortionWriter.AddActorInfo` (read-back checked) and returns nothing when no rule is proven. It is not yet wired to a UI or CLI write; applying it must use the normal backup/SHA workflow. Other formats and games report no states.

## Reliability boundary

- **L1:** checked-in X-Ray and S2 fixtures verify supported-format success, invalid input and unknown semantic status; Quest Doctor rules are tested on injected NPC data, and the stalker STATE parser on a 200-byte window copied from a real Clear Sky save (62 real CS saves parsed, all with living Wolf and Wild Napr).
- **L2:** desktop view-model, Quest Doctor CLI JSON, and Save Doctor CLI paths are covered by tests.
- **L3–L5:** no packaged-app audit, retail save corpus audit, quest repro, or in-game repair validation has been performed.
