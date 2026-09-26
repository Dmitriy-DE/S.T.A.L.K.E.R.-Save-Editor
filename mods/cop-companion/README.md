# S.T.A.L.K.E.R.: Call of Pripyat - Companion Mod Prototype

## Overview

The Companion Mod provides a non-invasive, out-of-process IPC (Inter-Process Communication) bridge between the **S.T.A.L.K.E.R. Save Editor** (C# desktop client) and an active, running game instance of **S.T.A.L.K.E.R.: Call of Pripyat** (X-Ray 1.6 / CoP Enhanced Edition).

Communication is handled via atomic file exchange inside the engine's `$app_data_root$` directory:
- Editor writes command to: `$app_data_root$/save_editor_cmd.txt`
- Mod reads and removes command, executes it safely within the engine update loop, and writes: `$app_data_root$/save_editor_out.txt`

---

## Installation

### 1. Vanilla Call of Pripyat (1.6.02 / Steam / GOG)

1. Ensure modding is enabled in `fsgame.ltx`:
   ```ini
   $game_data$ = true| true| $fs_root$| gamedata\
   ```
2. Copy `gamedata/scripts/save_editor_companion.script` into your game's `<GameRoot>/gamedata/scripts/` folder.
3. Open or unpack `<GameRoot>/gamedata/scripts/bind_stalker.script`.
4. Locate the `actor_binder:update(delta)` method (usually around line 300) and insert **exactly one line**:
   ```lua
   function actor_binder:update(delta)
       object_binder.update(self, delta)

       -- SAVE EDITOR COMPANION HOOK (1 line)
       if save_editor_companion then save_editor_companion.update() end

       -- ... remaining vanilla update code ...
   ```
5. Save the file.

### 2. Call of Pripyat Enhanced Edition (2024 Remaster / Steam appid 41700)

1. CoP EE supports mod loading through the `-moddir` launch parameter or the standard `gamedata/` folder located in the root or Steam common directory.
2. Place `gamedata/scripts/save_editor_companion.script` into `<CoP_EE_Root>/gamedata/scripts/`.
3. Apply the same 1-line hook into `bind_stalker.script` inside `gamedata/scripts/bind_stalker.script`.
4. Launch the game normally.

---

## Why Vanilla CoP Requires the `bind_stalker` Hook

1. **Lack of Global Script Event Listeners:**
   In vanilla X-Ray 1.6, the script engine does not maintain a native pub-sub event dispatcher (such as `RegisterScriptCallback` found in later community forks like OpenXRay, CoC, or Anomaly).
2. **Actor Lifecycle:**
   Game objects, inventory, and simulation (`alife()`, `db.actor`) are tied to the actor's existence. The engine invokes `actor_binder:update(delta)` once per frame specifically when the player is in-game. Hooking here ensures that commands are executed only when the game environment is fully initialized and operational.
3. **Alternative Evaluated: `level.add_call`:**
   `level.add_call(condition_func, action_func)` allows registering persistent frame callbacks into the engine's action queue. However, `level.add_call` still requires an initial execution trigger during game startup (such as from `bind_stalker.script:net_spawn` or a custom quest/info-portion). Placing a single direct call `save_editor_companion.update()` in `actor_binder:update` is strictly simpler, has zero memory overhead, and allows clean throttle control via `time_global()`.

---

## Supported Commands

Commands are written as plain single-line text into `save_editor_cmd.txt`:

| Command | Arguments | Example | Description |
| :--- | :--- | :--- | :--- |
| `ping` | *none* | `ping` | Verifies IPC responsiveness; returns `pong`. |
| `give` | `<section> [count]` | `give medkit 3` | Spawns items directly into player inventory via `alife():create`. |
| `money` | `<delta>` | `money 5000` | Adjusts player money (positive to add, negative to subtract). |
| `teleport` | `<x> <y> <z>` | `teleport 124.5 -8.2 310.0` | Instantly sets player position in current level. |

---

## Response Format

Responses are formatted as single-line JSON written to `save_editor_out.txt`:
```json
{"status":"ok","message":"pong","timestamp":1727384920}
```
Or on error:
```json
{"status":"error","message":"Item section 'invalid_wpn' does not exist in game configs","timestamp":1727384920}
```

---

## Limitations and Safety Guards

- **Actor Liveness:** Mod ignores commands if `db.actor` is nil or dead (`not db.actor:alive()`).
- **Level Boundaries:** `teleport` sets exact 3D coordinates. Teleporting outside valid AI level bounds or into geometry may cause physics clipping or fall-through; the C# editor should use verified coordinates from `TELEPORT-points.json`.
- **Game Paused:** When the game is paused (`Device.dwTimeGlobal` does not advance), the 2-second timer will not fire. The C# client must observe timeouts accordingly.
- **Fail-Safe Isolation:** All script operations are wrapped in `pcall` blocks; an invalid command or missing config section will return an error status instead of crashing the game engine.
