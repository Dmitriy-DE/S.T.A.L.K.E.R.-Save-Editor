# Состояние проекта

Актуально на выпуск 1.0.x (C# / .NET 10, Avalonia). Python-редактор заморожен и служит только эталоном.

## Что есть

| Область | Состояние |
|---|---|
| Чтение сейвов | ТЧ, ЧН, ЗП, три EE, S.T.A.L.K.E.R. 2 — паритет с Python по всем 175 сценариям `docs/PARITY.csv` |
| Правка X-Ray | деньги, стаки, прочность, размещение, апгрейды, отношения, добавление и удаление предметов, тайники; бэкап, журнал, атомарная замена, обратное чтение |
| Правка S2 | деньги, стаки, прочность; запись в облако S2 выключена до проверки в игре |
| Steam | облако трилогии (чтение, скачивание, запись с проверкой, без повторов), достижения |
| Интерфейс | 15 языков, музыка и звуки трёх игр, иконки, превью, сравнение, черновики с отменой, диагностика и отчёты |
| Мод-компаньон | ТЧ, ЧН, ЗП — полное меню; S2 — экспериментальный (UE4SS) |
| Сборки | Windows (установщик, zip), Linux (.deb, AppImage, tar.gz), macOS (arm64, x64), CLI (NativeAOT), веб (WASM, не опубликован) |

## Проверено

- L1–L2: 690+ тестов, CI на трёх ОС, `tools/check_companion.sh`.
- L4: компаньон ЗП — владелец в игре. ТЧ и ЧН — установлены, ждут проверки.
- Записи сейвов в игре (L5) не подтверждены.

## Открыто

- Публикация на R2 и APT: нужны секреты Cloudflare и ключ APT (владелец).
- Веб-версия: собирается в CI, не опубликована вместо Python-веба.
- S2: добавление предметов, апгрейды, отношения — нужны пары сейвов «до/после».
- Переназначение горячих клавиш компаньона не сохраняется.

## Game Doctor, Save Doctor, Game Fixes (2026-09-28)

This work is isolated on codex/stalker-toolkit-game-doctor, based on the fetched main at
57f1537b6dbb3b97c23a0323e5bdb2629e4ce06a. It does not change or merge main.

- **Installed-game patching:** Companion and Game Fixes use the shared Core Patching filesystem
  boundary and atomic replacement writer. They refuse managed-path overlap in either install order.
- **Game Fixes:** metadata model, explicit game/build/source gates, archive-to-loose-overlay handling,
  hash-backed recovery manifests, drift-protected uninstall, dependencies/conflicts, categories and
  preset filtering and a guarded per-fix version-update transaction exist. One Clear Sky fix
  (`cs.quest.dead-wild-napr`) is available as Experimental:
  its retail target bytes and archive round trip were verified, but it is not verified in-game and is
  excluded from safe presets. No installer component was added.
- **Game Doctor:** explicit target and path; structural marker; matching Steam build ID; capped,
  unclassified loose files; Companion state; Game Fix manifests/hash drift; S2 custom-mod folder state
  with a user-triggered reversible directory move.
- **Save Doctor:** read-only format-reader check and parsed inventory count. Semantic quest/object
  health stays unknown; no repair is registered.
- **Crash Analyzer:** user-selected log parser for fatal fields, Lua markers/stack frames, and common
  engine exception markers. No known signatures or automatic log discovery are registered.
- **Desktop / CLI:** explicit Game Doctor and Save Doctor screens, a Game Fix catalogue with guarded
  install/update/remove actions, and matching CLI commands use the same Core services. Fix status separates
  catalogue entries, safe recommendations, experimental entries and installed manifests.
- **Not implemented in this branch:** Quest Doctor, save repair, game snapshots, profiles, config
  editor, save timeline, installed-game encyclopaedia expansion, Live Inspector additions, and
  installer integration. No existing Companion or Steam behavior was live-tested.

Verification levels are recorded per feature in docs/GAME_DOCTOR.md, docs/SAVE_DOCTOR.md,
docs/CRASH_ANALYZER.md, docs/GAME_FIXES.md, docs/GAME_FIX_RESEARCH.md, and
docs/PATCHING_ARCHITECTURE.md. Synthetic tests do not establish retail or in-game correctness.
