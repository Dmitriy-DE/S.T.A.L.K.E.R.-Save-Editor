# Game Doctor

Game Doctor is a read-only audit of one installation selected by the user. It accepts an explicit game target because a directory marker alone cannot reliably distinguish the three original X-Ray games from their Enhanced Editions.

## Current checks

- Confirms that the selected directory exists and has the expected X-Ray `fsgame.ltx` marker or S.T.A.L.K.E.R. 2 `Stalker2/Content/Paks` directory.
- Reads a matching Steam `appmanifest_*.acf` build ID when the selected path is exactly the manifest's install directory.
- Lists up to 2,000 loose files under X-Ray `gamedata/` or S2 `Stalker2/Content/Paks/~mods/`.
- Verifies the existing Companion manifest and installed file hashes for the original trilogy when Companion files or a manifest are present.
- Lists toolkit Game Fix manifests and checks every managed file against its installed SHA-256. A malformed or changed state is reported for review.
- For S2, reports whether a custom `~mods` folder is present. The user can explicitly disable it by moving the directory to `Stalker2/Content/~mods.disabled`, outside `Paks`, then restore it later. This is a same-installation directory move; files are neither deleted nor copied. Steam Workshop and mod.io subscriptions are not changed.

Loose files are reported as **unclassified**. The application does not ship retail file baselines, so it cannot call a loose file vanilla, modified, safe, or conflicting based on its presence alone. The scan skips reparse points and does not modify or delete anything. Enhanced Editions have separate target IDs and do not reuse the original Companion status or fixes.

The Game Fix check reports catalogued definitions, safe preset recommendations and active manifest/hash state separately. The single Clear Sky definition is experimental, so the check reports that no safe recommendation is available instead of calling the installation clean or recommending it implicitly. Targets without a definition remain Unknown for fix recommendations. Game Doctor does not discover all installs, run a game integrity repair, inspect quest state, or find recent crash logs automatically. It only modifies S2 custom-mod folder placement after the user chooses the explicit action.

The S2 custom-mod recommendation follows the [official Update 2.0 mod FAQ](https://www.stalker2.com/news/mods-cost-of-hope-update-2-0-faq), which recommends removing mods before the update and identifies `Stalker2/Content/Paks/~mods` as the custom-mod folder. The app's reversible move is a user-triggered troubleshooting action; it does not diagnose whether any individual mod is stale or incompatible.

## CLI

```text
stalker-save-editor-cli doctor game cs "/path/to/Clear Sky"
stalker-save-editor-cli doctor game soc-ee "/path/to/Shadow of Chornobyl Enhanced Edition" --json
```

Targets are `soc`, `cs`, `cop`, `soc-ee`, `cs-ee`, `cop-ee`, and `s2`. The caller chooses the target; automatic discovery and non-Steam version identification are not claimed. A Steam build ID is metadata and is not treated as a retail game version.

S2 custom mods can also be temporarily moved out and restored with `mods s2-disable GAME_DIR` and `mods s2-restore GAME_DIR`.

## Reliability

- **L1:** deterministic unit tests cover structural checks, loose-file reporting, target separation, missing directories, Steam build ID parsing, and Game Fix hash drift.
- **L2:** CLI JSON serialization is covered by a test using a synthetic install directory.
- **L3–L5:** no packaged installer or retail game validation has been performed for this feature.
