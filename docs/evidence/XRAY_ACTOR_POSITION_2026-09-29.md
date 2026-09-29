# X-Ray actor position and level (TP-1) — 2026-09-29

Read-only research on the owner's retail saves (62 Clear Sky, 6 Shadow of Chernobyl, Call of Pripyat).

## Where the actor's location lives

| Field | Place in the actor record | Notes |
|---|---|---|
| `o_Position` (3 × f32) | spawn header after `section\0 name\0 u8 game_id u8 rp` | same in SoC, CS, CoP |
| `o_Angle` (3 × f32) | right after the position | |
| `m_tGraphID` game vertex (u16) | STATE + 0 | selects the level through `game.graph` |
| `m_tNodeID` level vertex (u32) | STATE + 10 (after distance f32 and direct_control u32) | |
| position copy (3 × f32) | UPDATE + 11 (after size u16, health f32, timestamp u32, flags u8) | equals the spawn position in every save checked |

The game vertex decides the level: Clear Sky's graph (in `all.spawn`, chunk 4, `CVertex` 42 bytes, level id in the low
8 bits of the packed u32) maps marsh 0–244, garbage 245–471, escape 472–742, darkvalley 743–960, red_forest 961–1056,
agroprom 1057–1211, yantar 1212–1312, military 1313–1510, agroprom_underground 1511–1523, limansk 1524–1564,
stancia_2 1565–1572, hospital 1573–1619. Every save's game vertex fell in the level its title names.

## Proof that level-changer destinations are safe targets

`tss - Кордон.sav` (saved right after entering Cordon from the Swamps) has the actor at position
(-271.1, -21.7, -276.6), game vertex 473, level vertex 3366 — byte for byte the destination stored in the Swamps
level changer `mar_level_changer_to_escape_1`. The game itself places the actor there.

## Level-changer destination block

`gv u16, lv u32, position 3 × f32, direction 3 × f32, level\0, point\0`, then SoC: nothing; CS/CoP: silent u8,
flag u8, hint string (and sometimes a logic name), trailing u16. Located by parsing
(`XRayLevelChangerReader.ParseStateSuffix`) at the first offset whose names are identifiers and whose tail matches.

## Implementation

`XRayRelocation` (TP-3/TP-4, experimental): offers only these destinations, writes spawn position + angle,
STATE game/level vertex and UPDATE position in place (record length unchanged), and reads the result back.
In-memory check: SoC 37 destinations / 19 levels, CS 95 / 13, CoP 7 / 7 — each relocation read back at the
destination with inventory and money unchanged. **Not loaded in the game yet (L5).**
