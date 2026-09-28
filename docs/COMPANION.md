# Companion mod

An in-game menu for Shadow of Chernobyl, Clear Sky and Call of Pripyat (original and Enhanced
Editions). It also lets the editor send commands to the running game through a file protocol
([MOD_COMPANION_PROTOCOL.md](MOD_COMPANION_PROTOCOL.md)).

## Opening the menu

- **Esc → F1** in the pause menu.
- **Companion PDA** (`se_companion_pda`): use it from the inventory to open the menu over the running game.
  The mod gives it to the player on the first start and brings it back if it is lost.

## What it does (all three games)

| Tab | Contents |
|---|---|
| Modes | Immortality, endless stamina, auto-repair of worn gear, endless ammo, one spare life. CoP also: no bleeding. |
| Player | ±10 000 RU, heal, companion amulet (+500 kg, no hunger), quick-action items (heal, repair, mark, return to mark, save), quick save / load. |
| Inventory | Repair, duplicate or delete any item; filter by category. |
| Spawn | Every item of the installed game **including mods**, search, favourites, count 1–100, repeat the last spawn; squads, mutants and story characters. |
| NPC | Faction goodwill ±500 per click; make the nearest stalker (squad in CoP) friendly, remove enemies or corpses nearby (story NPCs untouched). CoP also: call the nearest stalker. |
| Teleport | Smart terrains of every level and your own saved points; travel between levels. |
| World | Time of day, time speed, weather. CoP also: start / stop an emission. |
| Map | Mark all stashes, artefacts on the level; remove the marks. |

Features marked "CoP" rely on engine calls that SoC and CS do not have (checked against each
game's `lua_help`: `set_npc_position`, writable `bleeding`, `surge_manager`).

The spawn list is written by the installer from the game's own configs, so items added by mods
(OGSM, …) appear and sections the engine cannot spawn as inventory items stay out.

## Installing

Use any of these; each writes a manifest so the mod can be updated and removed cleanly:

- the editor: **Companion → All games → Install / update in all checked**;
- the Windows installer: component **Companion mod** (installs into every game found);
- the command line: `stalker-save-editor-cli companion install all` (or `soc`, `cs`, `cop`;
  `--game-dir DIR` for a folder the editor cannot find).

The installer converts the files to windows-1251, patches `bind_stalker.script` and the main menu
(and `quest_items`) of the game or of an installed mod, and refuses to overwrite files it did not
write. `companion uninstall` restores the patched files. After updating the editor, reinstall and
restart the game: the Companion screen warns when the game still runs an older mod build.

Copying the files by hand does not work: the hooks must be patched into the game's own scripts.

## Verification

Each game's calls are checked against its `lua_help` dump and `tools/check_companion.sh`
(Lua syntax, XML controls, hook patches) runs in CI. Call of Pripyat was tested in the game by the
owner; Shadow of Chernobyl and Clear Sky menus are installed on the owner's machine and wait for an
in-game check.

## S.T.A.L.K.E.R. 2 (experimental)

`mods/companion/s2` is a UE4SS Lua mod for S.T.A.L.K.E.R. 2 (money, give item, position, teleport
through the protocol). It is **experimental and not verified in the game**. It needs
[UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) installed by the player; the editor (Companion → All
games, unchecked by default) or `stalker-save-editor-cli companion install s2` copies it into
`Stalker2/Binaries/Win64/ue4ss/Mods/SaveEditorCompanion`. It writes every command and every error to
`%LOCALAPPDATA%\Stalker2\Saved\save_editor_companion.log`; the editor adds that log to its reports.
Cloud gaming services (GeForce NOW) do not allow mods.
