# Companion protocol v1

The in-game companion (`mods/companion`) lets the editor act on a running
game, including inventory actions, healing, repair, teleport and saved-point
navigation. One script serves Shadow of Chernobyl, Clear Sky, Call of Pripyat
and their Enhanced Editions.

## Files

All files live in the game's `$app_data_root$` (the user-data folder that also
holds `savedgames/`).

| File | Written by | Meaning |
|---|---|---|
| `save_editor_cmd.tmp` → `save_editor_cmd.txt` | editor | one command; write `.tmp`, then rename |
| `save_editor_out.tmp` → `save_editor_out.txt` | mod | one reply; same rename pattern |

The mod polls every 2 s while the actor exists (not in the main menu or
during loading), deletes the command file after reading it, and replies once.
After `hotkeys on`, it polls every 250 ms; `hotkeys off` restores the 2 s
interval.

## Lines

```
command: v1 <id> <command> [args...]
reply:   v1 <id> <status> <text>
```

- `id` — any token without spaces; the editor matches the reply by it.
- `status` — `ok`, `error` (bad arguments or engine failure) or
  `unsupported` (unknown command; an older mod).

| Command | Args | ok text |
|---|---|---|
| `ping` | — | `pong <build>` (`MOD_BUILD` of the loaded mod; old builds reply plain `pong`) |
| `info` | — | `level=<name> x=<f> y=<f> z=<f> money=<n>` |
| `give` | `<section> [1-100]` | `gave <n> <section>` |
| `money` | `<integer delta>` | `money=<balance>` (refuses to go below 0) |
| `heal` | — | `healed` (health, stamina, psy, radiation) |
| `repair_equipped` | — | `repaired=<n>` (condition 1.0 on slot items) |
| `teleport` | `<x> <y> <z>` | `at <x> <y> <z>` — current level only |
| `list_inventory` | — | `<section>:<id>,...` |
| `weather` | `[<section>] [now]` | `weather=<name>` (gets or sets active weather cycle) |
| `mark` | — | name of the saved point |
| `jump_last` | — | `at <name>` or `jumping to <name>@<level>` |
| `quicksave` | `[<name>]` | save name; defaults to `se_quick` |
| `hotkeys` | `on` or `off` | `hotkeys=on` or `hotkeys=off`; selects 250 ms or 2 s polling |
| `god` (S2 only) | `on` or `off` | `god mode on/off` — runs the game's `XSetGodMode` |
| `noclip` (S2 only) | `on` or `off` | `free flight on/off` — `XSetNoClipGSC` |
| `timespeed` (S2 only) | `0..100` (0 = normal) | `time speed <n>` — `XSetTimeSpeed` |

S2 commands call the game's built-in GSC debug commands through `KismetSystemLibrary.ExecuteConsoleCommand`
(the same names UETools and the MIT tool Rianvy/Stalker2Control type into the console). Trilogy mods reply
`unsupported`. In S2 `weather <preset>` runs `XForceWeather`.

Teleport points are the player's own: `info` returns the current position,
the editor stores it as a named bookmark and replays it with `teleport`.

## Editor side

- Refuse to send while a previous `save_editor_cmd.txt` still exists.
- Wait up to 10 s for the reply with the same `id`, then report
  "game not running or companion not installed" and delete the command file.
- Read replies with sharing that allows the mod to replace `save_editor_out.txt`
  during polling; retry transient I/O errors while `.tmp` is rotated into place.
- Never send a command that is not in this table.

Examples: `tests/golden/companion/*.json`.
