# Crash Analyzer

Crash Analyzer extracts structured facts from a selected log and can discover recent trilogy logs automatically. Discovery checks each structurally validated SoC/Clear Sky/CoP installation's `logs`, `_appdata_/logs`, and `_appdata_/log` directories, then checks sibling `logs` directories for existing save locations found by `SaveDirectoryLocator`. On Linux this reuses its Proton-prefix candidates. Results are ordered newest-first and canonicalized through `SaveSlotDiscovery.ResolveLinks`.

```text
stalker-save-editor-cli crash analyse "/path/to/xray_*.log" --game "Clear Sky"
stalker-save-editor-cli crash analyse "/path/to/xray_*.log" --json
stalker-save-editor-cli crash discover [--steam-root "/path/to/Steam"] [--json]
```

The output includes the log file's last-write time in UTC, which is file metadata rather than a timestamp asserted by the game log. The parser does not upload the log, and it does not claim a known fix based on generic text similarity. `knownIssueId` remains null until this repository has a documented, game/build-specific signature with a validated fix. Unknown logs remain `Unknown` and receive no fix recommendation. Discovery only lists candidate logs; `crash analyse LOG` remains an explicit local read of the chosen file.

The current signature table has no entries: none of the shipped fixes has a reproduced crash with an exact game build, script file, line, and message that can be safely matched. These parser results are diagnostic evidence only; they do not prove a cause or resolution.

## Reliability

- **L1:** synthetic tests cover X-Ray fatal fields, Lua stack frames, unknown logs, install-local log discovery, Proton save-profile log discovery, and refusal to claim a known fix.
- **L2:** CLI output and Steam-root-scoped discovery are covered with temporary fixture paths.
- **L3–L5:** no packaged app or real-game crash reproduction has been validated for this feature.

## Minidumps

The game overwrites its log on the next start; the `xray_*.mdmp` files next to it stay. `crash analyse FILE` and the
Game Doctor read a minidump as well as a log (told apart by content): the engine's own error text (Expression,
Function, File, Line, Description, Arguments) is recovered from the dumped memory, and the exception record gives the
failing module and offset (`EXCEPTION_ACCESS_VIOLATION in xrRender_R1.dll+0x879a4`). Nothing is symbolised. When the
newest log holds no crash, the newest dump is analysed instead. `crash discover` lists dumps with the logs.
Verification: L2 (synthetic dumps) and a run over eleven real dumps of Clear Sky and Call of Pripyat.
