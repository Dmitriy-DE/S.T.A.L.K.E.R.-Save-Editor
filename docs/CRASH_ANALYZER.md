# Crash Analyzer

Crash Analyzer accepts a user-selected log and extracts structured facts. It recognizes X-Ray fatal-error fields, Lua error markers and script stack-frame file/line references, and common engine exception markers. The desktop work in this branch adds no log discovery; the CLI can include an optional game label and emits either readable text or JSON.

```text
stalker-save-editor-cli crash analyse "/path/to/xray_*.log" --game "Clear Sky"
stalker-save-editor-cli crash analyse "/path/to/xray_*.log" --json
```

The output includes the log file's last-write time in UTC, which is file metadata rather than a timestamp asserted by the game log. The parser does not upload the log, and it does not claim a known fix based on generic text similarity. `knownIssueId` remains null until this repository has a documented, game/build-specific signature with a validated fix. Unknown logs remain `Unknown` and receive no fix recommendation. The analyzer is not yet wired to a selected Game Doctor installation or a diagnostic bundle.

The current catalogue has no validated crash signatures. These parser results are diagnostic evidence only; they do not prove a cause or resolution.

## Reliability

- **L1:** synthetic tests cover X-Ray fatal fields, Lua stack frames, unknown logs, and refusal to claim a known fix.
- **L2:** CLI output is covered with a local fixture log.
- **L3–L5:** no packaged app or real-game crash reproduction has been validated for this feature.
