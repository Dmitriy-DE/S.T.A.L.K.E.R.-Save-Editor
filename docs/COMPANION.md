# In-Game Companion Mod & Protocol Guide

The **S.T.A.L.K.E.R. Save Editor Companion** provides a live bridge between the desktop editor and running X-Ray engine games without requiring the player to exit or reload their save.

---

## 1. Architecture & Communication Protocol

The companion uses an atomic, non-blocking file exchange inside the game's `$app_data_root$` directory (the same folder where `savedgames/` and `user.ltx` reside).

```
Desktop Editor                           Game (X-Ray Engine)
      |                                           |
      |-- 1. Writes save_editor_cmd.tmp --------->|
      |-- 2. Atomically renames to .txt --------->|
      |                                           | (Polled every 250 ms when hotkeys active, otherwise 2000 ms)
      |                                           |-- 3. Reads & deletes command file
      |                                           |-- 4. Executes Lua engine call
      |                                           |-- 5. Writes save_editor_out.tmp
      |                                           |-- 6. Atomically renames to .txt
      |<-- 7. Editor reads reply -----------------|
```

### Protocol Format (v1)

- **Command format**: `v1 <id> <command> [args...]`
- **Reply format**: `v1 <id> <status> <text>`
  - `status`: `ok`, `error` (bad arguments or engine failure), or `unsupported` (command not supported in this game version).

### Protocol Rules & Safety
- **No Concurrent Commands**: The editor refuses to dispatch a new command while `save_editor_cmd.txt` is present on disk.
- **Timeout & Cleanup**: The editor waits up to 10 seconds for a matching `id` response. If no response arrives, it reports that the game or mod is not running and clears the command file.
- **Strict Protocol Compliance**: The editor never issues commands outside the declared specification.

---

## 2. Supported Commands & Protocol Matrix

| Command | Arguments | Meaning | Expected Success Response |
|---|---|---|---|
| `ping` | — | Heartbeat / liveness check | `pong` |
| `info` | — | Current actor level, 3D coordinates, and balance | `level=<name> x=<f> y=<f> z=<f> money=<n>` |
| `give` | `<section> [1-100]` | Spawns item section into actor inventory | `gave <n> <section>` |
| `money` | `<integer delta>` | Adjusts money (refuses negative balances) | `money=<balance>` |
| `heal` | — | Restores full health, stamina, psy-health; clears radiation | `healed` |
| `repair_equipped` | — | Sets condition 1.0 on all equipped armor and weapons | `repaired=<n>` |
| `teleport` | `<x> <y> <z>` | Teleports actor within current level | `at <x> <y> <z>` |
| `list_inventory` | — | Enumerate inventory items with runtime engine IDs | `<section>:<id>,...` |
| `weather` | `[<section>] [now]` | Queries or sets active weather cycle | `weather=<name>` |
| `mark` | `[<name>]` | Bookmarks current position with name | `<name>` |
| `jump_last` | — | Teleports actor to last marked position | `at <name>` or `jumping to <name>@<level>` |
| `quicksave` | `[<name>]` | Triggers immediate engine save (default: `se_quick`) | `saved` or `<name>` |
| `hotkeys` | `on` / `off` | Sets polling frequency: `on` = 250 ms, `off` = 2000 ms | `hotkeys=on` or `hotkeys=off` |

---

## 3. Game Engine Support & In-Game Verification Status

Per repository reliability rules ([docs/roadmap/RL-reliability.md](roadmap/RL-reliability.md)), synthetic round-trips and static syntax checks do not count as proof of live game execution.

### Verification Status Table

| Command | SoC (1.0006) | CS (1.5.10) | CoP (1.6.02) | S2 (UE5) | Verified in Live Game? | Current Verification Level |
|---|---|---|---|---|---|---|
| `ping` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `info` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `give` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `money` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `heal` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `repair_equipped` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `teleport` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `list_inventory` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `weather` | Supported | Supported | Supported | Unsupported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `mark` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `jump_last` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `quicksave` | Supported | Supported | Supported | Unsupported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |
| `hotkeys` | Supported | Supported | Supported | Supported | Нет (только статика) | **L2** (Lua 5.1 syntax + `check_companion.sh`) |

> **Notice**: Live in-game verification (**L4 / L5**) is conducted separately by the repository maintainer on dedicated test installations. No feature is marked L4/L5 until validated against a running retail game process.

---

## 4. Manual Installation Guide (SoC / CS / CoP)

While the Desktop Save Editor provides automated one-click hook installation via the Companion screen, manual installation can be performed following the steps below.

### Step 0: Prerequisites & `fsgame.ltx`
Open `fsgame.ltx` in the game root folder and ensure that `$game_data$` allows loading loose files:
```ini
$game_data$   = true|  true|  $fs_root$|  gamedata\
```

---

### Step 1: File Deployment

#### Call of Pripyat (ЗП - 1.6.02)
1. Copy common scripts:
   - `mods/companion/gamedata/scripts/save_editor_companion.script` → `<CoP_Root>/gamedata/scripts/`
   - `mods/companion/gamedata/scripts/save_editor_level_changer.script` → `<CoP_Root>/gamedata/scripts/`
2. Copy CoP-specific files:
   - `mods/companion/cop/gamedata/scripts/*` → `<CoP_Root>/gamedata/scripts/`
   - `mods/companion/cop/gamedata/configs/*` → `<CoP_Root>/gamedata/configs/`
   - `mods/companion/cop/gamedata/textures/*` → `<CoP_Root>/gamedata/textures/`

#### Shadow of Chernobyl (ТЧ - 1.0004 / 1.0006)
1. Copy common scripts:
   - `mods/companion/gamedata/scripts/save_editor_companion.script` → `<SoC_Root>/gamedata/scripts/`
   - `mods/companion/gamedata/scripts/save_editor_level_changer.script` → `<SoC_Root>/gamedata/scripts/`
2. Copy SoC-specific files:
   - `mods/companion/soc/gamedata/scripts/*` → `<SoC_Root>/gamedata/scripts/`
   - `mods/companion/soc/gamedata/configs/*` → `<SoC_Root>/gamedata/config/` (or `configs/` depending on mod/version)
   - `mods/companion/soc/gamedata/textures/*` → `<SoC_Root>/gamedata/textures/`

#### Clear Sky (ЧН - 1.5.10)
1. Copy common scripts:
   - `mods/companion/gamedata/scripts/save_editor_companion.script` → `<CS_Root>/gamedata/scripts/`
   - `mods/companion/gamedata/scripts/save_editor_level_changer.script` → `<CS_Root>/gamedata/scripts/`
2. Copy CS-specific files:
   - `mods/companion/cs/gamedata/scripts/*` → `<CS_Root>/gamedata/scripts/`
   - `mods/companion/cs/gamedata/configs/*` → `<CS_Root>/gamedata/configs/`
   - `mods/companion/cs/gamedata/textures/*` → `<CS_Root>/gamedata/textures/`

---

### Step 2: Hook Injection in `bind_stalker.script`

If `gamedata/scripts/bind_stalker.script` does not exist in loose files, extract it from the game archives:
- **SoC**: Extract from `gamedata.db*` using `converter.exe` or the desktop editor.
- **CS / CoP**: Extract from `resources.db*` / `patches.db*`.

Make a backup copy: `bind_stalker.script.bak`.

#### Hook 1: Actor Update Loop
In `bind_stalker.script`, locate `function actor_binder:update(delta)`. Right after the call to `object_binder.update(self, delta)`:
- **SoC** (~line 215 in vanilla 1.0006):
- **CS** (~line 253 in vanilla 1.5.10):
- **CoP** (~line 246 in vanilla 1.6.02):

Insert the following line:
```lua
if save_editor_companion then save_editor_companion.update() end
```

#### Hook 2: Item Usage Trigger
In `bind_stalker.script`, locate `function actor_binder:use_inventory_item(obj)` and insert:
```lua
if save_editor_companion and save_editor_companion.on_item_use then
    save_editor_companion.on_item_use(obj)
end
```

---

### Step 3: Uninstallation / Rollback
1. Delete or restore `bind_stalker.script` from `bind_stalker.script.bak`.
2. Remove `gamedata/scripts/save_editor_companion.script` and `save_editor_level_changer.script`.
3. Remove added files in `gamedata/scripts/save_editor_*.script`, `gamedata/configs/misc/save_editor_companion.ltx`, `gamedata/configs/ui/ui_save_editor_companion.xml`, and `gamedata/textures/ui/se_companion/`.

---

## 5. Cross-Game Engine API Verification (`lua_help` Audit)

Every engine function and method called by `mods/companion` has been audited against the official Lua export symbol dumps from retail X-Ray builds:
- **SoC**: `~/.cache/claude-pytest/lh_gamedata_soc.txt` (X-Ray 1.0006)
- **CS**: `~/.cache/claude-pytest/lh_gamedata_cs.txt` (X-Ray 1.5.10 / CS)
- **CoP**: `~/.cache/claude-pytest/lh_gamedata.txt` (X-Ray 1.6.02 / CoP)

### Verified Symbols Matrix

| Symbol / Call | Call Type | SoC | CS | CoP | Notes & Runtime Safety |
|---|---|:---:|:---:|:---:|---|
| `alife().create` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `alife().level_name` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `alife().object` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `alife().release` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `alife().story_object` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `game.function` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `game.translate_string` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `get_console().execute` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.change_game_time` | Engine Namespace | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `level.function` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.get_time_factor` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.get_time_hours` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.get_time_minutes` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.get_weather` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.map_add_object_spot` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.map_has_object_spot` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.map_remove_object_spot` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.name` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.object_by_id` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.present` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.set_time_factor` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.set_weather` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `level.start_stop_menu` | Engine Namespace | ✓ | ✓ | ✗ | SoC and CS (UI deprecated in CoP) |
| `level.stop_weather_fx` | Engine Namespace | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `level.temporary` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `relation_registry.change_community_goodwill` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `relation_registry.community_goodwill` | Engine Namespace | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:00()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:Action()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:AddCallback()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:AddExistingItem()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:AddItem()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:AddTextField()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:AttachChild()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:ClearList()` | Object Method | ✗ | ✓ | ✓ | CS and CoP |
| `:CurrentID()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:Fill()` | Object Method | ✓ | ✓ | ✗ | SoC and CS (UI deprecated in CoP) |
| `:GetHeight()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:GetHolder()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:GetSelectedIndex()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:GetSelectedItem()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:GetText()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:GetTextItem()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:GetWidth()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:HideDialog()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:Init3tButton()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:InitCheck()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:InitComboBox()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:InitEditBox()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:InitList()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:InitListBox()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:InitStatic()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:InitTextWnd()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:InitTexture()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:InitWindow()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:OnKeyboard()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:ParseFile()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:Register()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:RemoveAll()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:STATE_Read()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:STATE_Write()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:Selected()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:SetAutoDelete()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:SetCheck()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:SetEllipsis()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:SetFilters()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:SetFont()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:SetOriginalRect()` | Object Method | ✓ | ✓ | ✗ | SoC and CS (UI deprecated in CoP) |
| `:SetText()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:SetTextColor()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:SetTextureRect()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:SetWndPos()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:SetWndRect()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:SetWndSize()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:Show()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:ShowDialog()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:ShowSelectedItem()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:TextControl()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:active_item()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:alive()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:character_name()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:close()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:condition()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:create()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:direction()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:distance_to()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:execute()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:game_vertex_id()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:get_ammo_in_magazine()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:get_id()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:give_money()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:id()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:inventory_for_each()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:item_in_slot()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:kill()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:level_id()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:level_name()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:level_vertex_id()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:line_exist()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:mad()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:money()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:name()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:object()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:position()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_eof()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_float()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_s32()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_seek()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_string()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_stringZ()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_tell()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_u16()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_u32()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_u8()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:r_vec3()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:rank()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:read()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:register_npc()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:relation()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:release()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:section()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:section_exist()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:section_name()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:set()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:set_actor_position()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:set_ammo_elapsed()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:set_condition()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:set_npc_position()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:set_relation()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:squad_members()` | Object Method | ✗ | ✗ | ✓ | CoP-only (guarded at runtime or in cop/ UI) |
| `:start_stop_menu()` | Object Method | ✓ | ✓ | ✗ | SoC and CS (UI deprecated in CoP) |
| `:story_id()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:story_object()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:update()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:update_path()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:vertex()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:w_begin()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:w_float()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:w_s32()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:w_stringZ()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:w_tell()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:w_u16()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:w_u32()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:w_u8()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |
| `:w_vec3()` | Object Method | ✓ | ✓ | ✓ | Universal X-Ray API |

### Key Cross-Game Engine Differences & Mitigations
1. **Time of Day Manipulation**:
   - `level.change_game_time` is native to CoP (1.6.02). In SoC (1.0006) and CS (1.5.10) it does not exist in engine exports.
   - *Mitigation in `save_editor_companion.script`*: The mod checks `if level.change_game_time ~= nil then` and falls back to dynamic `time_factor` fast-forwarding (`level.set_time_factor`) until the desired hour is reached.
2. **Weather FX**:
   - `level.stop_weather_fx` is native to CoP.
   - *Mitigation in `save_editor_companion.script`*: Guarded with `if level.stop_weather_fx ~= nil then level.stop_weather_fx() end`.
3. **UI Engine Evolution**:
   - SoC/CS use `level.start_stop_menu(wnd, true)` and legacy `CUIWindow:SetOriginalRect(x,y,w,h)`.
   - CoP replaced this with `wnd:ShowDialog(true)`, `wnd:HideDialog()`, `CUIListBox:InitListBox`, and `CUIStatic:TextControl()`.
   - *Mitigation*: Separate game-tailored UI scripts exist in `mods/companion/soc/`, `mods/companion/cs/`, and `mods/companion/cop/`.

---

## 6. Desktop Hotkeys & In-Game Keybindings

The companion hotkeys are specifically chosen to avoid collision with standard X-Ray engine F-keys (F5 Quicksave, F6/F7 Quickload, F8/F9 etc.):
- `Ctrl+H` — Restore health, stamina, psy-health, clear radiation (`heal`).
- `Ctrl+R` — Repair equipped armor and weapons to 100% condition (`repair_equipped`).
- `Ctrl+M` — Add currency (`money +5000`).
- `Ctrl+J` — Teleport to last saved marker (`jump_last`).
- `Ctrl+S` — Trigger immediate engine save (`quicksave`).

---

## 7. Automated Testing & Verification Tooling

Run the test suite from the repository root:
```bash
./tools/check_companion.sh
```

This performs:
1. Syntax compilation on all 18 companion script files (`luac5.1 -p`).
2. Exact hook validation for `actor_binder:update` and `actor_binder:use_inventory_item`.
3. Syntax compilation of synthetic patched `actor_binder` class.
4. UI contract validation across SoC, CS, and CoP XML and Lua layouts (`tools/check_companion_ui.py`).
