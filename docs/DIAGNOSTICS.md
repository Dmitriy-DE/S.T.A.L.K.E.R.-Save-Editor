# Game Doctor, Save Doctor, crash analyser, profiles

## Game Doctor

Game Doctor is a read-only audit of one explicitly selected installation. Its discovery flow finds Steam-manifest installs for all seven targets and uses the existing GOG/Heroic/retail locator for the original trilogy. A directory marker alone cannot reliably distinguish original X-Ray games from their Enhanced Editions, so discovery uses each Steam app ID and requires a matching structural marker: `fsgame.ltx` or the target-specific `fsgame_soc.ltx`, `fsgame_cs.ltx`, or `fsgame_cop.ltx`. Other storefronts and custom layouts can be entered manually.

### Current checks

- Confirms that the selected directory exists and has the matching X-Ray game marker or S.T.A.L.K.E.R. 2 `Stalker2/Content/Paks` directory.
- Reads a matching Steam `appmanifest_*.acf` build ID when the selected path is exactly the manifest's install directory.
- Lists up to 2,000 loose files under X-Ray `gamedata/` or S2 `Stalker2/Content/Paks/~mods/`.
- Verifies the existing Companion manifest and installed file hashes for the original trilogy when Companion files or a manifest are present.
- Lists toolkit Game Fix manifests and checks every managed file against its installed SHA-256. A malformed or changed state is reported for review.
- Audits each valid manifest-owned Companion and Game Fix file by path, checks whether it exists and still matches its recorded hash, and warns if multiple toolkit manifests claim the same path. Loose files that are not present in those manifests are listed as `Unclassified` with `Unknown` status; the audit does not label them conflicts based on presence alone.
- For S2, reports whether a custom `~mods` folder is present. The user can explicitly disable it by moving the directory to `Stalker2/Content/~mods.disabled`, outside `Paks`, then restore it later. This is a same-installation directory move; files are neither deleted nor copied. Steam Workshop and mod.io subscriptions are not changed.

Game Doctor reports loose files as **unclassified** and remains read-only. The Toolkit Environment audit can additionally mark a file vanilla when its SHA-256 matches a known retail source hash for the exact detected Steam build and path; this is a partial path-level baseline, not a complete game baseline. Other loose files remain unknown. It can offer cleanup for a stale Game Fix manifest only when the existing Game Fix provider verifies exclusive ownership, current-file hashes, recovery-backup hashes, target and dependency state; cleanup restores through that provider. Files with unknown ownership, changed hashes, or manifestless state are retained. The scan skips reparse points. Enhanced Editions have separate target IDs and do not reuse the original Companion status or fixes.

The Game Fix check reports catalogued definitions, safe preset recommendations and active manifest/hash state separately. The current catalogue has hash-guarded fixes for Clear Sky 1.5.10, SoC 1.0006 and CoP 1.6.02; Community changes stay out of presets. SoC 1.0004, unsupported builds, Enhanced Editions and S2 remain without a fix recommendation. Game Doctor does not run a game integrity repair or inspect quest state. Crash Analyzer can find recent X-Ray logs from detected installations and save-profile folders through `crash discover`; no crash signature is currently validated. Game Doctor only modifies S2 custom-mod folder placement after the user chooses the explicit action.

The S2 custom-mod recommendation follows the [official Update 2.0 mod FAQ](https://www.stalker2.com/news/mods-cost-of-hope-update-2-0-faq), which recommends removing mods before the update and identifies `Stalker2/Content/Paks/~mods` as the custom-mod folder. The app's reversible move is a user-triggered troubleshooting action; it does not diagnose whether any individual mod is stale or incompatible.

### CLI

```text
stalker-save-editor-cli doctor discover
stalker-save-editor-cli doctor discover --steam-root "/path/to/Steam" --json
stalker-save-editor-cli doctor game cs "/path/to/Clear Sky"
stalker-save-editor-cli doctor game soc-ee "/path/to/Shadow of Chornobyl Enhanced Edition" --json
```

Targets are `soc`, `cs`, `cop`, `soc-ee`, `cs-ee`, `cop-ee`, and `s2`. Discovery returns source, directory and build metadata; selecting a result fills the Game Doctor target and path. Non-Steam trilogy versions remain unidentified unless a matching Steam manifest is present. A Steam build ID is metadata and is not treated as a retail game version.

S2 custom mods can also be temporarily moved out and restored with `mods s2-disable GAME_DIR` and `mods s2-restore GAME_DIR`.

### Reliability

- **L1:** deterministic unit tests cover structural checks, loose-file reporting, target separation, missing directories, Steam build ID parsing and app-ID discovery, Companion and Game Fix ownership/hash drift, and unknown-file classification.
- **L2:** ViewModel and CLI JSON tests cover discovery and file audit using synthetic install directories.
- **L3:** the packaged NativeAOT CLI was smoke-tested against a temporary synthetic Steam install for discovery, Game Doctor JSON and an empty safe preset. This did not test a Windows installer or game files.
- **L4–L5:** no user workflow was run against a live install and no game was launched.

---

## Save Doctor

Save Doctor is a read-only structural inspection surface for one selected save. The desktop screen can open a save file directly or inherit the selected library save. The CLI supports:

    stalker-save-editor-cli doctor save "/path/to/save.sav"
    stalker-save-editor-cli doctor save "/path/to/save.sav" --json

The service uses the same supported format readers as Save Editor. A successful result means that the known container, framing, checksum/decompression, and parsed record checks accepted the file. It reports the detected format and parsed inventory record count.

Semantic quest reachability and missing-object references remain unknown outside the Quest Doctor rules below.

Quest Doctor (Shadow of Chernobyl and Clear Sky) checks a small list of known breaks:

    stalker-save-editor-cli doctor quest "/path/to/save.sav" [--json]
    stalker-save-editor-cli doctor quest-repair "/path/to/save.sav" [--output PATH] [--backup-dir DIR]

Each rule pairs a creature in the save's ALife registry with an actor info portion that the NPC's `[death]` logic sets only when he dies online. A rule is **broken** when the NPC's registry object has health of zero or less and the info portion is absent. A missing NPC object or an unreadable actor info list is reported as unknown, never as broken.

| Rule | NPC section | Info portion | What reads it |
|---|---|---|---|
| `cs.wild-napr-dead` | `gar_digger_quester` | `gar_flea_market_stop_quest_line` | vanilla `dialogs_garbadge.xml` stops offering his tasks |
| `cs.wolf-dead` | `esc_wolf` | `esc_wolf_dead` | only the Game Fix `cs.quest.wolf-offline-cancellation` (vanilla never reads the flag); the report says so |
| `cs.hog-dead` | `mil_hog` | `mil_hog_death` | vanilla `mil_quest_line.ltx` after the talk with Forester; once `forester_talked_2` is set the line has branched, so the rule reports **unknown / too late** and offers no repair |

| `soc.mole-dead` | `agr_krot` | `agr_krot_dead` | vanilla `tasks_agroprom.xml` fails "meet Mole's group" |
| `soc.prisoner-dead` | `val_prisoner_captive` | `val_prisoner_dead` | vanilla `tasks_darkvalley.xml` fails "help the prisoner" |
| `soc.courier-dead` | `mil_freedom_member0001` | `mil_courier_dead` | vanilla `tasks_military.xml` completes "kill the courier" |
| `soc.informer-dead` | `mil_ara` | `mil_ara_dead` | vanilla `tasks_military.xml` completes the informer step |

The Clear Sky rules come from the SRP v1.1.5 history and were checked against the retail Steam `configs.db` + patches. The Shadow of Chernobyl rules come from the retail `all.spawn` (each NPC's own `[death] on_info`) and the retail task XML; ZRP 1.09 has no save-flag fixes for these. Call of Pripyat has no rules: its story scripts check whether the NPC object is alive, and the few `[death]` flags that tasks read are set only when the actor is the killer, so an offline death does not leave a missing flag.

**Repair.** The desktop Save Doctor shows the rules and, when at least one is broken, a *Fix quests* button. It adds only the missing info portions through `XRayInfoPortionWriter.AddActorInfo` (SoC/CS store the save's game time with each flag) and replaces the save through the normal journaled backup + source-SHA check + read-back (`QuestDoctor.VerifyRepair` re-runs the rules on the written bytes). The CLI `doctor quest-repair` writes a new file next to the save (never over it) with a backup. Other formats and games report no states.

### Reliability boundary

- **L1:** checked-in X-Ray and S2 fixtures verify supported-format success, invalid input and unknown semantic status; Quest Doctor rules are tested on injected NPC data, the stalker STATE parser on a 200-byte window copied from a real Clear Sky save, and the repair write (backup, flag read-back) on the CS fixture.
- **L4 (read-only):** 62 real CS saves parsed; Wolf and Wild Napr alive in all, Hog present and alive in 9 — no real broken save has been seen yet.
- **L2:** desktop view-model, Quest Doctor CLI JSON, and Save Doctor CLI paths are covered by tests.
- **L5:** no in-game load of a repaired save and no reproduced broken quest yet.

### Crash signatures

`CrashSignatureCatalog` holds crash messages quoted verbatim by the SRP v1.1.5 history (18, Clear Sky) and ZRP 1.09 `CrashesStillInTheGame.txt` (6, Shadow of Chernobyl). `CrashLogAnalyzer` sets `KnownIssueId`/`KnownIssue` when a log contains one, with advice: repair the save (Quest Doctor rule), install a Game Fix, reload, an earlier save (damaged save), or a community patch when we ship no fix. Game Doctor shows the newest log in the game's `logs`/`_appdata_/logs` folder; the CLI prints the match for `crash analyse`. Call of Pripyat has no signatures yet. Verified on quoted messages (L1) and on the owner's three retail logs, none of which contain a crash (L4, read-only).

---

## Crash Analyzer

Crash Analyzer extracts structured facts from a selected log and can discover recent trilogy logs automatically. Discovery checks each structurally validated SoC/Clear Sky/CoP installation's `logs`, `_appdata_/logs`, and `_appdata_/log` directories, then checks sibling `logs` directories for existing save locations found by `SaveDirectoryLocator`. On Linux this reuses its Proton-prefix candidates. Results are ordered newest-first and canonicalized through `SaveSlotDiscovery.ResolveLinks`.

```text
stalker-save-editor-cli crash analyse "/path/to/xray_*.log" --game "Clear Sky"
stalker-save-editor-cli crash analyse "/path/to/xray_*.log" --json
stalker-save-editor-cli crash discover [--steam-root "/path/to/Steam"] [--json]
```

The output includes the log file's last-write time in UTC, which is file metadata rather than a timestamp asserted by the game log. The parser does not upload the log, and it does not claim a known fix based on generic text similarity. `knownIssueId` remains null until this repository has a documented, game/build-specific signature with a validated fix. Unknown logs remain `Unknown` and receive no fix recommendation. Discovery only lists candidate logs; `crash analyse LOG` remains an explicit local read of the chosen file.

The current signature table has no entries: none of the shipped fixes has a reproduced crash with an exact game build, script file, line, and message that can be safely matched. These parser results are diagnostic evidence only; they do not prove a cause or resolution.

### Reliability

- **L1:** synthetic tests cover X-Ray fatal fields, Lua stack frames, unknown logs, install-local log discovery, Proton save-profile log discovery, and refusal to claim a known fix.
- **L2:** CLI output and Steam-root-scoped discovery are covered with temporary fixture paths.
- **L3–L5:** no packaged app or real-game crash reproduction has been validated for this feature.

### Minidumps

The game overwrites its log on the next start; the `xray_*.mdmp` files next to it stay. `crash analyse FILE` and the
Game Doctor read a minidump as well as a log (told apart by content): the engine's own error text (Expression,
Function, File, Line, Description, Arguments) is recovered from the dumped memory, and the exception record gives the
failing module and offset (`EXCEPTION_ACCESS_VIOLATION in xrRender_R1.dll+0x879a4`). Nothing is symbolised. When the
newest log holds no crash, the newest dump is analysed instead. `crash discover` lists dumps with the logs.
Verification: L2 (synthetic dumps) and a run over eleven real dumps of Clear Sky and Call of Pripyat.

---

## Toolkit environment, profiles and snapshots

### Managed snapshots

The Toolkit Environment screen can create, inspect, restore and delete snapshots for an explicitly selected X-Ray installation. It records only the Game Fix and Companion provider files/manifests, plus the ownership manifest and explicit overrides for an existing `user.ltx` selected through the settings provider. File contents are stored once under SHA-256 object names in the application's data directory; snapshot metadata lists the exact provider and relative path for each object.

Restore first creates a safety snapshot, checks provider hashes for drift, then reconciles through `GameFixEngine`, `CompanionInstaller` and `ManagedUserLtxSettings`. Companion manifests and backups are reinstated through its existing installer provider after the bundled payload has been replayed. Snapshot objects are integrity checked and are never copied directly into a game folder. Delete removes the snapshot metadata and prunes only objects no remaining snapshot references.

A snapshot is bound to the resolved installation path. Restore can fail when the current Steam build, fix catalogue, Companion payload, or original file anchors differ. In that case the operation reports the conflict and retains the safety snapshot. It does not capture unrelated files, saves, Steam Cloud data, arbitrary mods, or the complete `user.ltx`; unmanaged lines in that file remain outside the snapshot.

### Profiles

Profiles are named local records of a selected game's installed Game Fix IDs, Companion on/off state and current toolkit-owned `user.ltx` overrides. The profile screen saves the current state, shows the recorded IDs/settings, applies a selected profile, and deletes profiles. Apply creates an automatic recovery snapshot first and uses the same three managed providers as snapshot restore. When a profile carries a `user.ltx` path, that resolved path is used unless the user explicitly selects another existing `user.ltx`.

Profiles do not infer or capture settings changed outside the Toolkit. A profile cannot apply a fix absent from the shipped catalogue or for a mismatched target/build. Enhanced Editions and S.T.A.L.K.E.R. 2 do not inherit original-trilogy fix or Companion state.

### Config editor

The editor resolves the default `user.ltx` location from the selected game's existing `fsgame.ltx` app-data alias, then accepts an explicit file selection. It currently supports:

- `g_fov` from 30 through 150;
- `hud_fov` from 0.2 through 1.0;
- `mouse_sens` from 0.01 through 1.0;
- the known `on`/`off` toggles `hud_crosshair`, `hud_crosshair_dist`, `hud_info`, `hud_weapon`, and `cl_dynamiccrosshair`.

The screen shows current value, the first-seen original value, and whether the Toolkit owns the current line. If no original command existed, “game default” means the engine supplied the value; the editor does not invent a numeric default. It edits one known line and preserves the other bytes and line endings. A changed owned line becomes a conflict and blocks automatic replacement or default restore.

### Install audit

The audit reports files covered by valid Game Fix/Companion manifests as toolkit-managed. A loose file is called vanilla only when its SHA-256 matches a known retail source hash for the exact detected Steam build and path; the catalogue is not a complete game baseline, so every other loose file remains unknown. Manifestless contents under Toolkit state directories are orphaned state needing review and are retained.

An active Game Fix manifest whose ID is no longer in the shipped catalogue is reported as an orphaned toolkit-owned fix. Cleanup is offered only if the expected game matches, the fix has no active dependents, every installed-file hash and recorded backup hash matches, and the Game Fix provider is the sole audited owner of each affected path. The action rechecks those conditions and uninstalls through `GameFixEngine`, restoring the recorded prior bytes. Any mismatch remains a conflict and disables cleanup; unknown and manifestless files are never removed.
