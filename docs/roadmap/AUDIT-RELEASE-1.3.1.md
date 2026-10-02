# Audit before 1.3.1 (2026-10-02)

1.3.1 answers two lists: what the owner saw in the released 1.3.0, and the code review of `main` (Codex, same day).
Each finding was reproduced first (renders of the real build on the owner's saves in an empty HOME, a read-only
harness over all real saves, `dotnet-stack` samples), then fixed. "Not checked" is at the end.

## Owner's findings in 1.3.0

| Finding | Cause | Fix |
|---|---|---|
| Achievements → Refresh opens a second window | the desktop executable handled only `--steam-native-worker`; the achievements child (`--steam-native-op …`) started the whole interface | both executables route worker arguments through `SteamWorkerCommandLine`; a malformed worker line exits with a usage error and never reaches the interface |
| "The previous run ended with an error" after a normal close | the D-Bus connection posts to an interface thread that is already gone; the cancellation was recorded as a crash | cancellations raised while closing are logged, not reported |
| Interface sounds while the editor is closed | the windowless service modes (`--measure-ui`, `--screenshot`) played tab sounds through the speakers; they were run during the 1.3.0 audit | service modes are silent |
| Everything is too small on Full HD; icons alone say nothing | fixed 1260 × 820 window, 100% scale, menu folded below 1600 px, buttons as wide as their text | scale "fit the screen" by default (125% on Full HD, up to 200%), the window opens sized to the display, labelled full-width menu with a toggle whose state is kept |
| Stashes named `ZAT_A2_ACTOR_TREASURE (ЗАТОН)` | object names shown as they are; level names hard-coded in Russian | names from the games' string tables in 13 languages: levels of all three games, 301 Clear Sky stashes; personal boxes and unnamed boxes described by kind; the object name stays as a small second line |
| Transitions named `zaton_level_changer_0000` | same | "Zaton → Jupiter" from the level changer's own destination |
| Two rows "RGD-5 grenade" | every object is a row | identical single objects are one row with a count, as in the game |
| Not every save has a picture | measured on 333 saves: all 29 without one are autosaves or cloud saves for which the game wrote no picture | the empty frame says so |
| Game Doctor and Companion layout | lists as wide as their text, labels of different width, page limited to 1000 px | one label column, full-width lists, page uses the window |
| Encyclopedia in Russian in an English interface | names came from the installed game's own files | the games' names in the interface language first; same for upgrades and the player's faction |
| "2243 items" in a stash | checked: a real count in a save of a mod | none needed |
| Left edge clipped | not reproduced in renders at 940, 1260 and 1920 px | — |

## Found while reproducing

| Finding | Fix |
|---|---|
| **Wrong destination of a level changer.** The block was taken at the first offset that happened to parse. Shadow of Chernobyl spells levels with a capital letter (`L02_Garbage`), the reader accepted only lower case, so a later offset was taken (`arbage`); in Call of Pripyat an offset one byte early was taken when the preceding byte was a digit (`5pripyat`). The numbers read there were offered for the experimental move of the character | the block is accepted only where the restrictor shape list ends, with identifier names, a finite position and the known trailer, and only when exactly one place fits. On 148 real saves every destination is now a real level name; 7 270 moves were prepared in memory and read back |
| The window froze for 3–5 s at every start | `CompanionServiceAdapter` did the installer's work on the interface thread behind `Task.Yield`: the status check opens the archives of every installed game. Now on a worker. Found with `--measure-ui` stage timings and `dotnet-stack` |
| Settings page full of switches that are permanently disabled | removed |

## Code review of main (16 points)

| # | Point | State |
|---|---|---|
| 1 | Toolkit wrote the `user.ltx` of the previously selected installation | fixed, test A → B |
| 2 | "To backpack" checkbox undid its own click | fixed |
| 3 | Save button outside a 940 px window at 125% | fixed: the title yields, the commands stay; inventory details stay beside the list |
| 4 | Save Doctor did not analyse the save selected during an analysis | fixed, test |
| 5 | Encyclopedia decoded every icon on the interface thread before the screen was opened | virtualized list, icon on row build |
| 6 | Heavy work on the interface thread | saving, comparing and listing installed fixes run in the background in the application. Restoring a backup and the Game Environment screen are still synchronous |
| 7 | Draft history unbounded in memory | untouched state + latest 100 steps; the session continues with the journal that was written |
| 8 | A money edit rebuilt the plan of the whole inventory several times | one plan per notification burst; no set allocation per item |
| 9 | A foreign file was copied in full before its signature was checked | header first |
| 10 | Parse session kept every intermediate image | three latest |
| 11 | Limit on large saves looked at the packed size | uses the declared unpacked size; applies to a single open |
| 12 | Library cache holds the full model of every save | not done |
| 13 | Save history rebuilt per batch | built once per load |
| 14 | Benchmarks do not gate the new hot paths | not done |
| 15 | Two tests scanned the machine's real save library | fixed |
| 16 | Shared Lua source for the SoC and CS menu; all screens built at start | screens are built on first show; the Lua source is not merged (a change of the mod needs a run in both games) |

## What was run

| Check | On what | Result |
|---|---|---|
| Build, warnings as errors; all tests | branch `feat/after-1.3` | Core 992, Steam 76, CLI 40 pass |
| Companion scripts, translations (14 languages, 2 297 strings), sounds | `tools/check_companion.sh`, `--test-i18n`, `--test-audio` | pass |
| Names in an English interface | every real save read through the application's loader | no Russian text left in item, upgrade, faction, stash or level names; remaining raw keys are items of mods and Clear Sky upgrade groups |
| Level-changer destinations and the move | 148 real X-Ray saves, 7 270 destination × save pairs, in memory | all read back at the destination, numbers within a level's range |
| Money, stack, condition edits; S2 stacks; damaged input | all real saves, in memory | see the numbers below |
| Interface timing | `tools/ui_probe measure` on a 4.6 MB mod save with the six installed games visible | longest gap 0.2–0.4 s (was 3–5 s) |
| Renders | 940 px at 125%, 1260 px, 1920 px | Save visible, no overlap |
| Real saves and game folders | never opened for writing | — |

## Not checked

- Nothing was run in a game.
- The window on a real display: the size and scale chosen from the screen, the menu toggle by mouse, closing without
  the error banner, Achievements → Refresh with a live Steam session. The code paths are covered by tests and
  windowless renders only.
- Windows and macOS packages: built by CI, not started on hardware.
- The background save in the application is covered by a test that calls it directly; the disabled screens during
  the write were not exercised by hand.
