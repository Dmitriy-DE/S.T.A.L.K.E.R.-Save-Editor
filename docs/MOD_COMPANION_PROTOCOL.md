# S.T.A.L.K.E.R. Save Editor: In-Game Companion IPC Protocol

## 1. Architectural Motivation

The S.T.A.L.K.E.R. Save Editor operates out-of-process from the running game. Direct dynamic memory injection (e.g. `WriteProcessMemory`, DLL hooking, code injection) carries severe anti-cheat/antivirus flagging risks, fragile offset dependencies across game versions, and potential memory corruption.

File-based Inter-Process Communication (IPC) via the game's designated user-data directory (`$app_data_root$`) provides a clean, decoupled, and crash-resilient communication protocol.

---

## 2. Channel Location and Discovery

Both the C# Editor client and the in-game Lua companion mod use the engine-standard user data path:
- **X-Ray Path Alias:** `$app_data_root$` (configured in `fsgame.ltx`).
- **Default Windows paths:**
  - Original CoP: `%PUBLIC%\Documents\stlk-cop\` or `<GameRoot>\_appdata_\`
  - CoP EE / Steam: `%LOCALAPPDATA%\GSC Game World\STALKER - Call of Pripyat\` or Steam Cloud user path.
- **File Names:**
  - Request Command File: `save_editor_cmd.txt`
  - Response Output File: `save_editor_out.txt`

---

## 3. Wire Format Specification

### 3.1 Command Format (Editor -> Game)
**Choice: Plain Text (Single-Line Command Strings)**

**Rationale:**
1. **Vanilla Lua Constraints:** Standard Lua 5.1 in X-Ray 1.6 lacks native JSON parsing support. Bundling a full third-party Lua JSON library introduces unnecessary parser attack surfaces, syntax quirks, and script dependencies.
2. **Atomic Simplicity:** Single-line space-separated commands (`<VERB> [ARG_1] [ARG_2] ... [ARG_N]`) are trivially parsed with standard Lua pattern matching (`string.gmatch(line, "%S+")`).
3. **Low Complexity:** Zero allocations and instant validation reduce game tick latency to < 1 ms.

**Syntax:**
```text
<COMMAND_VERB> [ARGUMENTS]\n
```

**Supported Verbs:**
- `ping`
- `give <section_name> [count]`
- `money <delta_integer>`
- `teleport <x_float> <y_float> <z_float>`

**Examples:**
```text
ping
give medkit 5
money 10000
teleport 114.250 -4.100 288.750
```

### 3.2 Response Format (Game -> Editor)
**Choice: Compact JSON Line**

**Rationale:**
1. **Easy Output from Lua:** Serializing structured data to JSON in Lua requires only `string.format` with basic escaping.
2. **Native C# Deserialization:** The C# editor consumes responses using high-performance, built-in `System.Text.Json` (`JsonSerializer` or `JsonDocument`).

**Schema:**
```json
{
  "status": "ok" | "error",
  "message": "Human readable result or error description",
  "timestamp": 1727384920
}
```

**Fields:**
- `status` (`string`, mandatory): `"ok"` if command executed successfully; `"error"` if parsing, validation, or engine execution failed.
- `message` (`string`, mandatory): Descriptive summary of execution results or exact error rationale.
- `timestamp` (`integer`, mandatory): Unix epoch timestamp in seconds from game host system (`os.time()`).

---

## 4. Lifecycle and State Flow

```
[ C# Editor Client ]                                      [ Game Engine / Lua Mod ]
        |                                                            |
        | 1. Verify xrEngine.exe process is running                  |
        | 2. Delete stale save_editor_out.txt (if any)               |
        | 3. Write temp file & atomically rename to                  |
        |    save_editor_cmd.txt                                     |
        |                                                            |
        |----------------------------------------------------------->|
        |                                                            | 4. Update tick (every 2s):
        |                                                            |    Check save_editor_cmd.txt
        |                                                            | 5. Read command & delete
        |                                                            |    save_editor_cmd.txt
        |                                                            | 6. Execute command under pcall
        |                                                            | 7. Write save_editor_out.txt
        |<-----------------------------------------------------------|
        |                                                            |
        | 8. Poll for save_editor_out.txt (100ms interval)          |
        | 9. Parse response JSON                                     |
        | 10. Delete save_editor_out.txt                             |
        v                                                            v
```

---

## 5. Timing, Polling & Timeouts

| Parameter | Recommended Value | Explanation |
| :--- | :--- | :--- |
| **Mod Update Period** | `2000 ms` | Prevents file I/O overhead on game frame rate. |
| **C# Client Poll Interval** | `100 ms` | Interval at which C# checks for `save_editor_out.txt`. |
| **Initial Ping Timeout** | `5000 ms` | Max wait time during handshake ping to confirm mod is installed and active. |
| **Standard Command Timeout** | `4500 ms` | Standard commands take up to 2000 ms (mod loop tick) + 100 ms write time. 4500 ms provides a generous 2.25x safety margin. |
| **Pause/Menu Detection** | `> 6000 ms` | If timeout expires without response, engine is likely paused in ESC menu, in sleep mode, in a cutscene, or the mod is not hooked. |

---

## 6. Safety and Security Rules

1. **Process Liveness Verification:**
   Before creating or modifying `save_editor_cmd.txt`, the C# Editor **MUST** assert that the target game process (`xrEngine.exe` or `xrEngine`) is currently running. If the game is closed, writing command files is prohibited.
2. **Atomic Write Pattern:**
   The C# editor writes to `save_editor_cmd.txt.tmp` first and immediately renames/moves it to `save_editor_cmd.txt`. This prevents the Lua mod from attempting to read a half-written file.
3. **Actor and Level Sanity Checks:**
   - Commands are rejected if `db.actor:alive()` is false.
   - Item spawns verify that `system_ini():section_exist(sec)` is true before passing to `alife():create`.
   - Teleport requests should be checked against known valid map boundaries to avoid physics voids.
4. **Exception Containment:**
   All Lua execution paths are wrapped inside protected calls (`pcall`), ensuring no malformed user input or runtime exception can crash the game engine.
