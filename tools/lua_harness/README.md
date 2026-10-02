# Lua harness for Game Fixes

Runs pieces of the games' own Lua outside the game: the engine is replaced by stubs, the script logic runs for real
under stock Lua 5.1. It shows what a patched script does in situations nobody has watched in the game. It does not
replace a play test: nothing here proves how the engine itself behaves.

`tools/fix_regress.py` runs every harness on the trees it builds (`OUT/<game>/original` and `/patched`).

| Script | Checks |
|---|---|
| `smart_jobs.lua PATCHED` | `cs.crash.smart-terrain-no-free-job`: six camp situations (overload, the extra NPC leaves or dies, the owner dies, a job frees up, a job swap); a job's owner and the camp's job-to-NPC record must stay right. On the retail script the first situation stops with the game's own abort. |
| `smart_jobs_diff.lua RETAIL PATCHED [ROUNDS]` | Random camp life with never more NPCs than jobs: the patched script must do exactly what the retail one does, step by step. |
| `smart_jobs_overload.lua PATCHED [ROUNDS]` | Random camp life with more NPCs than jobs: no error, every NPC has a job, every job's owner is a present NPC doing that job. |

Object ids in the harness are large and sparse, as in the game. With small consecutive ids the game's own
`table.sort(self.npc_info, …)` reorders the table and the retail script itself fails; in the game that sort does nothing.
