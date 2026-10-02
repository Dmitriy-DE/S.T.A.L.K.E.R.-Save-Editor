# Состояние проекта

Актуально на 2026-10-02 (после v1.2.0, C# / .NET 10, Avalonia). Сверка обещаний с кодом —
[AUDIT_2026-10-02.md](AUDIT_2026-10-02.md). Python-редактор заархивирован.

## Что есть

| Область | Состояние |
|---|---|
| Чтение сейвов | ТЧ, ЧН, ЗП, три EE, S.T.A.L.K.E.R. 2 |
| Правка X-Ray | деньги, стаки, прочность, размещение, апгрейды, отношения, добавление и удаление предметов, тайники, перенос игрока (experimental); бэкап, журнал, атомарная замена, обратное чтение |
| Правка S2 | деньги, стаки, прочность, предмет из тайника в рюкзак; запись в облако S2 выключена до проверки в игре |
| Steam | облако трилогии (чтение, скачивание, запись с подтверждением, без повторов), достижения |
| Исправления игр | наши фиксы для розницы: ЧН 55, ТЧ 29, ЗП 31, плюс варианты для EE; установка и удаление с журналом транзакции; пресеты; Game Doctor, Quest Doctor, сигнатуры вылетов |
| Интерфейс | 15 языков, музыка и звуки, иконки из установленной игры, сравнение, черновики с отменой, диагностика и отчёты |
| Мод-компаньон | ТЧ, ЧН, ЗП — полное меню, переназначаемые клавиши (сохраняются); S2 — экспериментальный (UE4SS) |
| Сборки | Windows (установщик, zip), Linux (.deb, AppImage, tar.gz), macOS (arm64, x64), CLI (NativeAOT), веб (WASM, `stalker-save-editor.pages.dev`) |

## Проверено

- L1–L2: ~1030 тестов (Core 915, CLI 40, Steam 72), CI на трёх ОС, веб запускается в headless-браузере.
- L3 по файлам: пресеты фиксов ставились CLI в чистую копию реальных установок ТЧ, ЧН и ЗП и снимались без остатка.
- L4: компаньон ЗП — владелец в игре. ТЧ и ЧН ждут проверки.
- L5 (игра загрузила изменённый сейв) для C#-редактора не записан.

## Открыто

См. [AUDIT_2026-10-02.md](AUDIT_2026-10-02.md): проверки владельца в игре, материалы по S2, решения по APT и Workshop,
секреты Cloudflare для автопубликации. Трекеры: [FIX-PACKS.md](FIX-PACKS.md), [REVIEW-2026-10-01.md](REVIEW-2026-10-01.md).

## История (как было на момент записи)

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
  excluded from safe presets. Essential-only, Recommended and All-safe preset application now uses a
  batch transaction that checks the detected build and rolls back only fixes newly installed by a
  failed batch. Every current safe preset is an explicit no-op because no Validated fix is catalogued.
  No installer component was added while there is no validated safe fix to apply from setup.
- **Game Doctor:** discovery by Steam app ID covers all seven targets; the original trilogy also uses
  existing GOG/Heroic/retail detection. It deduplicates symlinked Steam roots per target, rejects manifest paths
  outside `steamapps/common`, checks the selected target marker/build, inventories up to 2,000 loose
  files, audits Companion/Game Fix-owned file hashes and leaves other loose files unclassified. S2
  custom-mod folder state has a user-triggered reversible directory move.
- **Save Doctor:** read-only format-reader check and parsed inventory count. Semantic quest/object
  health stays unknown; no repair is registered.
- **Crash Analyzer:** user-selected log parser for fatal fields, Lua markers/stack frames, and common
  engine exception markers. No known signatures or automatic log discovery are registered.
- **Desktop / CLI:** Game Doctor has a detected-install picker and per-file ownership/integrity list;
  Save Doctor remains read-only; Game Fixes supports explicit install/update/remove and the three
  safe-preset actions. `doctor discover [--steam-root PATH]... [--json]` and
  `fixes apply-preset <essential|recommended|all-safe> TARGET GAME_DIR [--json]` use Core services.
- **Still not implemented here:** Quest Doctor, evidence-backed save repair, game-wide snapshots,
  profiles/config activation, an expanded save timeline beyond existing Save Library backup compare
  and restore, installed-game encyclopaedia expansion, Live Inspector additions, and installer
  integration. Component-specific rollback exists for Game Fix, Companion, S2 mod-folder moves and
  existing save backups; it is not a general snapshot service. No game was launched and no live
  Companion/Steam operation was performed.
- **Packaging:** Linux app plus NativeAOT CLI package built; packaged CLI discovery, Game Doctor JSON,
  and empty Recommended preset passed against a temporary synthetic install. Windows installer and
  in-game/runtime acceptance were not tested.

Verification levels are recorded per feature in docs/GAME_DOCTOR.md, docs/SAVE_DOCTOR.md,
docs/CRASH_ANALYZER.md, docs/GAME_FIXES.md, docs/GAME_FIX_RESEARCH.md, and
docs/PATCHING_ARCHITECTURE.md. Synthetic tests do not establish retail or in-game correctness.

## Toolkit follow-up (2026-09-29)

This checkout continues from `988279b8a79e883774a3bbbe09c33f58db89768e` on
`codex/toolkit-part-2`; the preceding Game Doctor section records the earlier baseline.

- **Game Fixes:** catalogue `2026.09.2` includes 25 Clear Sky 1.5.10, 15 SoC 1.0006, and 24
  CoP 1.6.02 definitions, including six narrow fixes ported from PRP v1.2. Safe presets use only
  Essential and Recommended entries. CoP Recommended grows from 5 to 10 fixes. SoC 1.0004,
  Clear Sky EE, CoP EE, and S2 remain without fix recommendations because matching retail baselines
  or applicability evidence are unavailable. SoC EE build `24067120` was checked separately: 12
  SoC fixes are applicable (one partly pre-existing), and three conflict. The Windows installer has
  an optional preset component and delegates application to the CLI. The desktop reports the
  catalogue version delta.
- **Crash and quest diagnostics:** recent trilogy logs can be discovered from validated installs
  and existing save-location candidates, including Proton candidates. Quest Doctor reports task
  state as unavailable because supported readers do not expose validated quest fields. There are
  no known crash signatures or automatic repairs.
- **Toolkit Environment:** content-addressed snapshots cover only Game Fix, Companion, and
  explicitly managed `user.ltx` state. Profiles replay through the existing providers and create a
  recovery snapshot first. The `user.ltx` editor is allow-listed and preserves unrelated bytes.
  Install audit marks provider-backed files as managed, exact known retail hashes for the detected
  Steam build as vanilla, and other loose files as unknown. It can clean a stale Game Fix only when
  the provider verifies the current and backup hashes, target, dependencies, and exclusive ownership;
  manifestless state remains for review.
- **Save library:** the timeline orders discovered files by real modification times and enables
  adjacent comparisons only when timestamps establish an unambiguous order. The encyclopedia reads
  item names, icons, weight, cost, and section from installed-game data and uses existing save and
  Companion actions. The live inspector uses the existing `info` and `list_inventory` protocol.
- **Verification on 2026-09-29:** Release build with warnings as errors, all 803 local tests,
  Companion Lua checks, audio validation, i18n validation for all 14 locales, and `git diff --check`
  pass. This is local L1–L2 evidence. The Inno Setup script was not compiled here; no package was
  exercised and no game or live Steam session was used. L3–L5 are not claimed.
