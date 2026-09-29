# Save Doctor

Save Doctor is a read-only structural inspection surface for one selected save. The desktop screen can open a save file directly or inherit the selected library save. The CLI supports:

    stalker-save-editor-cli doctor save "/path/to/save.sav"
    stalker-save-editor-cli doctor save "/path/to/save.sav" --json

The service uses the same supported format readers as Save Editor. A successful result means that the known container, framing, checksum/decompression, and parsed record checks accepted the file. It reports the detected format and parsed inventory record count.

Semantic quest reachability and missing-object references remain unknown outside the Quest Doctor rules below.

Quest Doctor (Clear Sky only) checks a small list of known breaks:

    stalker-save-editor-cli doctor quest "/path/to/save.sav" [--json]
    stalker-save-editor-cli doctor quest-repair "/path/to/save.sav" [--output PATH] [--backup-dir DIR]

Each rule pairs a creature in the save's ALife registry with an actor info portion that the NPC's `[death]` logic sets only when he dies online. A rule is **broken** when the NPC's registry object has health of zero or less and the info portion is absent. A missing NPC object or an unreadable actor info list is reported as unknown, never as broken.

| Rule | NPC section | Info portion | What reads it |
|---|---|---|---|
| `cs.wild-napr-dead` | `gar_digger_quester` | `gar_flea_market_stop_quest_line` | vanilla `dialogs_garbadge.xml` stops offering his tasks |
| `cs.wolf-dead` | `esc_wolf` | `esc_wolf_dead` | only the Game Fix `cs.quest.wolf-offline-cancellation` (vanilla never reads the flag); the report says so |
| `cs.hog-dead` | `mil_hog` | `mil_hog_death` | vanilla `mil_quest_line.ltx` after the talk with Forester; once `forester_talked_2` is set the line has branched, so the rule reports **unknown / too late** and offers no repair |

All three come from the SRP v1.1.5 history and were checked against the retail Steam `configs.db` + patches.

**Repair.** The desktop Save Doctor shows the rules and, when at least one is broken, a *Fix quests* button. It adds only the missing info portions through `XRayInfoPortionWriter.AddActorInfo` (SoC/CS store the save's game time with each flag) and replaces the save through the normal journaled backup + source-SHA check + read-back (`QuestDoctor.VerifyRepair` re-runs the rules on the written bytes). The CLI `doctor quest-repair` writes a new file next to the save (never over it) with a backup. Other formats and games report no states.

## Reliability boundary

- **L1:** checked-in X-Ray and S2 fixtures verify supported-format success, invalid input and unknown semantic status; Quest Doctor rules are tested on injected NPC data, the stalker STATE parser on a 200-byte window copied from a real Clear Sky save, and the repair write (backup, flag read-back) on the CS fixture.
- **L4 (read-only):** 62 real CS saves parsed; Wolf and Wild Napr alive in all, Hog present and alive in 9 — no real broken save has been seen yet.
- **L2:** desktop view-model, Quest Doctor CLI JSON, and Save Doctor CLI paths are covered by tests.
- **L5:** no in-game load of a repaired save and no reproduced broken quest yet.
