# Состояние проекта — 28.09 (финальная пачка)

Актуальный статус проекта S.T.A.L.K.E.R. Save Editor — Next (C# / .NET 10).
Завершена реализация всех задач в зоне Desktop, Packaging, Companion и Documentation.

---

## 1. Решения и архитектурные договорённости

- **D13 & D14.** Вся активная разработка ведётся в репозитории C# (`S.T.A.L.K.E.R.-Save-Editor-Next`). Python-репозиторий заморожен как эталон.
- **Архитектурный рубеж Core vs UI.** Пользовательский интерфейс не содержит логики мутации файлов сейвов, парсинга байтов или диспетчеризации писателей. Все операции редактирования, добавления, удаления и сохранения выполняются через централизованный сервис `EditService` в ядре Core (`StalkerSaveEditor.Core`).
- **Безопасность записи.**
  - Любое изменение сейва создаёт pre-save бэкап с контролем SHA-256 вне рабочей папки.
  - Неизвестные, неоднозначные или экспериментальные поля (а также сохранение сейвов S.T.A.L.K.E.R. 2 до появления верифицированного писателя) остаются строго read-only.
  - Запись в Steam Cloud требует явного подтверждения; статусы `Uncertain` отображаются с диагностическим объяснением и не перезаписываются автоматически.
- **Никаких выдуманных данных в UI.** Переходы строятся по реальным `LevelChangers` из сейва; здоровье не заменяется фейковыми 100%; статус компаньона отображает реальное состояние хуков и сетевого взаимодействия.

---

## 2. Итоговый реестр задач и статус PR (Зона Gemini)

| № | Задача | Ветка / PR | Статус | Уровень верификации | Как проверено |
|---|---|---|:---:|:---:|---|
| **1** | Все правки Core в UI (апгрейды, прочность, размещение, фракции, тайники, add/remove, undo/redo) | `gemini/b1-core-edits` / [PR #86](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/pull/86) | **PR Opened** (Запрос в Core: [Issue #81](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/issues/81)) | **L2** | 10 тестов ViewModel (`InventoryViewModelTests`, `DraftStoreTests`), биндинг к `EditService.PrepareEdit`, блокировка записи S2 по `CapabilityService`. |
| **2** | Экран «Облако» (Steam RemoteStorage & S2 Auto-Cloud) | `gemini/b5-cloud-screen` / [PR #88](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/pull/88) | **PR Opened** | **L2** | `CloudViewModelTests` (6 тестов), явное подтверждение записи, красные бейджи для статуса `Uncertain` без автоповтора, изолированный Steam worker. |
| **3** | Экран «Достижения Steam» | `gemini/b6-steam-achievements` / [PR #89](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/pull/89) | **PR Opened** | **L2** | `SteamAchievementsViewModelTests` (5 тестов), защита от случайной блокировки, таймстампы разблокировки, мок и боевой адаптер. |
| **4** | Экран «Обновления» (OTA Updates) | `gemini/b8-updates-screen` / [PR #91](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/pull/91) | **PR Opened** | **L2** | `UpdatesViewModelTests` (5 тестов), верхний баннер уведомления в главном окне, сравнение семантических версий через GitHub Releases API. |
| **5** | Экран «Переходы» (Level Transitions) | `gemini/b1-transitions-real` / [PR #92](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/pull/92) | **PR Opened** | **L2** | `TransitionsViewModelTests` (4 теста), отображение реальных `XRayTrilogySave.LevelChangers`, нулевой процент выдуманных данных, безопасный read-only режим. |
| **6** | Настройки и первый запуск (First-Launch Wizard & Settings) | `gemini/d6-settings-wizard` / [PR #94](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/pull/94) | **PR Opened** | **L2** | `SettingsViewModelTests` (8 тестов), автопоиск папок сейвов ТЧ/ЧН/ЗП/S2 (включая Linux Steam Proton prefixes), визард первого запуска, 14 локалей, выбор темы и громкости звуков. |
| **7** | Игровой стиль окна (Authentic Trilogy Theme & Column Fix) | `gemini/d7-game-theme` / [PR #95](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/pull/95) | **PR Opened** | **L2** | Исправлена обрезка колонки S2 на экране «Возможности», аутентичные милитари-рамки (`CornerRadius = 1`), состояния кнопок `_e/_h/_t`, скриншоты до/после на реальных фикстурах. |
| **8** | Пакеты: включение `mods/companion` рядом с бинарником + `dry_run` input | `gemini/d8-packaging-companion` / [PR #78](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/pull/78) | **MERGED** | **L3** | Сборка и запуск `.AppImage` (43 МБ) и `.deb` (42 МБ), проверка функции «Установить хуки» в запущенном бандле, headless скриншот. |
| **9** | Мод-компаньон: документация и сверка API | `gemini/d9-companion-docs` / [PR #96](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/pull/96) | **PR Opened** | **L2** | Пошаговая ручная установка для ТЧ/ЧН/ЗП, честная таблица «проверено в игре / нет», сверка всех 145 функций и методов с `lua_help` дампами трёх игр (`lh_gamedata_*.txt`). |
| **10** | Финал: README и STATE.md | `gemini/d10-docs-state` | **PR Opened** | **L2** | Полное описание возможностей, руководства по установке и сборке, ссылки на скриншоты реального UI, синхронизация статуса проекта. |

---

## 3. Статус тестов и проверок

Все 5 обязательных гейтов проекта выполняются со 100% успехом:
1. `dotnet build -warnaserror`: **PASS** (0 warnings, 0 errors across all 7 projects).
2. `dotnet test`: **PASS** (516 unit/integration tests passed across Core, Steam, and Desktop).
3. `tools/check_companion.sh`: **PASS** (18 companion Lua scripts compiled via `luac5.1 -p`, synthetic `bind_stalker` patch verified, UI contract checks passed).
4. `dotnet run --project src/StalkerSaveEditor.Desktop -- --test-i18n`: **PASS** (14 locales verified: 1327/1327 messages translated with 0 placeholder discrepancies).
5. `dotnet run --project src/StalkerSaveEditor.Desktop -- --test-audio`: **PASS** (6 UI sound triggers verified).

---

## 4. Оставшиеся задачи на стороне Codex и Maintainer

- **Codex:**
  - Реализация поддержки Add/Delete предметов в `XRayEditWriter` по зарегистрированному [Issue #81](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/issues/81).
  - Спецификация и верификация писателя сейвов S.T.A.L.K.E.R. 2 (до этого сохранение S2 в UI заблокировано).
- **Maintainer (Владелец):**
  - Живая проверка в играх (**L4/L5**) для ТЧ (1.0006) и ЧН (1.5.10) по образцу ЗП (Esc → F1, хоткеи, спавн предметов/мутантов).
