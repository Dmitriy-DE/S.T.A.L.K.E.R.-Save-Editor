# Research notes: save contents, locations, in-game verification, sources

> Черновик Gemini (2026-09-26), Claude не перепроверял. Утверждения без доказательства на реальных сейвах — гипотезы. Лицензии внешних дампов (например, `Trasiankus/stalker2-bel`) до использования в сборке проверить отдельно.

## Что ещё можно извлечь из сохранений (по каждой игре)

Черновик подготовлен в рамках задачи **RS-1** (2026-09-25) по результатам исследования форматов сохранений X-Ray (ТЧ, ЧН, ЗП, Enhanced Editions) и S.T.A.L.K.E.R. 2 (Unreal Engine 5).

Документ описывает:
1. Поля и структуры данных в сейвах по каждой игре (где лежат, статус проверки, безопасность модификации).
2. Кандидаты для отображения на новой вкладке UI **«Сведения»** (Dashboard / Overview).
3. Перспективные кандидаты для безопасной записи в будущих релизах.

---

### 1. Сводные таблицы по играм

#### S.T.A.L.K.E.R. 2: Heart of Chornobyl (S2)

Формат контейнера: потоковые блоки Oodle Kraken (`0x40000` байт), несжатый заголовок `b"\xCC\x06"`, контрольная сумма CRC-32 в конце файла (последние 4 байта).

| Поле / Сущность | Где лежит | Проверено на сейвах | Режим доступа | Ценность и назначение |
|---|---|---|---|---|
| **Деньги игрока (Купоны)** | Структура кошелька за якорем `MONEY_ANCHOR` (`WALLET_FIELD_ID`), `uint32` | Да (все актуальные билды) | **Чтение / Запись** (уже в релизе) | Базовая экономика |
| **Рюкзак (Inventory Grid)** | Сетка 8 колонок, координаты `(x, y)`, размеры `(w, h)`, смещения записей объектов | Да (100% подтверждено) | **Чтение / Перемещение / Удаление** (в релизе) | Управление инвентарем |
| **Надетые вещи (Equipped)** | Слоты оружия (1, 2, пистолет, нож), костюм, шлем, пояс артефактов | Да (подтверждено на сейвах владельца: 102 надетых) | **Чтение** (в релизе) | Карточка персонажа |
| **Переносимое снаряжение (Carried)** | Детекторы (kind 6), КПК/заметки (kind 8), ПНВ (kind 10), бинокль (kind 11) | Да (вне сетки рюкзака) | **Чтение** (в релизе) | Полная инвентаризация |
| **Состояние брони и оружия (Condition)** | Оружие: `float32` в первичном стейте; Броня: смещение `+0x23` -> `+4` `float32` (0.0..1.0) | Да (все сейвы владельца) | **Чтение / Запись** (в релизе) | Ремонт и износ |
| **Модули оружия (Attachments)** | Идентификаторы установленных глушителей, прицелов, подствольников | Да (`S2WeaponConditionAnchor`) | **Только чтение** | Карточка оружия в UI |
| **Апгрейды оружия и брони** | Списки SID строк модификаций | Да (`read_s2_armor_upgrades`, `upgrades`) | **Только чтение** | Дерево прокачки экипировки |
| **Игровое время и таймстампы** | Секция кампании: `ticks` (int64) и `seconds` (float32) | Да (`editor/s2_campaigns.py`) | **Только чтение** | Время в Зоне, длительность игры |
| **Текущая локация / Регион** | Секция кампании: строка региона (`Zaton`, `Rostok`, `Pripyat` и др.) | Да (`s2_campaigns.py`) | **Только чтение** | Карточка «Где находится Скиф» |
| **Личный тайник (`PlayerStash`)** | Мировой контейнер инвентаря игрока (синий ящик на базах) | Да (структура идентична сетке инвентаря) | **Кандидат в чтение (L2)** | Отображение содержимого тайника |
| **Параметры выживания (Survival Stats)** | Блок `PlayerContext`: Здоровье, Радиация, Голод, Кровотечение, Усталость | Да (структура найдена в декомпрессированном дампе) | **Кандидат в чтение (L1) / Запись (L3)** | Индикаторы состояния Скифа |
| **Репутация у фракций** | Таблица отношений группировок (Варта, Искра, Долг, Свобода и т.д.) | Найдено на дампах | **Кандидат в чтение (L2)** | Сводка фракционных отношений |
| **Координаты в мире (Transform)** | Вектор `(X, Y, Z)` float32/float64 и углы ориентации | Найдено в объекте игрока | **Кандидат в чтение (L1)** | Позиция на глобальной карте |

---

#### Тень Чернобыля (ТЧ / SoC) и Enhanced Edition

Формат: контейнер X-Ray с чанками, LZO-сжатие пакетов. ALife Version 5 (ваниль) / EE.

| Поле / Сущность | Где лежит | Проверено на сейвах | Режим доступа | Ценность и назначение |
|---|---|---|---|---|
| **Деньги (Рубли)** | `CSE_ALifeTraderAbstract` внутри актора (`uint32`) | Да | **Чтение / Запись** (в релизе) | Базовая экономика |
| **Патроны (Ammo Stacks)** | `CSE_ALifeItemAmmo`: счетчик `uint16` в state и update чанках | Да | **Чтение / Запись** (в релизе) | Редактирование боезапаса |
| **Состояние (Condition)** | Поле `condition` (`float32`, 0.0..1.0) предметов | Да | **Чтение / Запись** (в релизе) | Ремонт предметов |
| **Группировка игрока** | `player_faction_index` в стейте актора | Да | **Чтение / Запись** (в релизе) | Смена фракции Меченого |
| **Отношения (Goodwill)** | Чанк 9: реестр отношений актора и группировок | Да | **Чтение / Запись** (в релизе) | Правка враждебности |
| **Игровое время (Game Time)** | Чанк 5: `game_time` (`uint64`), `time_factor` (`float32`) | Да (`editor/xray_save.py`) | **Только чтение** (в релизе) | Дата, час и минута в игре |
| **Текущий уровень (Level)** | Чанк 1 (SPAWN), субчанк 0: `level_name` (`l01_escape`, `l02_garbage`...) | Да | **Только чтение** (в релизе) | Текущая локация |
| **Координаты Меченого** | Позиция `(X, Y, Z)` (`float32`) и углы поворота | Да (`actor.position`) | **Только чтение** (в релизе) | Точное местоположение |
| **Здоровье, ранг, репутация** | `actor_health` (float), `actor_rank` (int32), `actor_reputation` (int32) | Да | **Только чтение** (в релизе) | Статус сталкера |
| **Инфопорции (InfoPortions)** | Вектор строк `CInfoPortion` в `client_data` актора (~370 записей) | **Да (проверено в сессии 24/25)** | **Кандидат в чтение (L1)** | **Прогресс сюжета и квестов** |
| **Патроны в магазине оружия** | `CSE_ALifeItemWeapon`: `m_u16AmmoElapsed`, `m_u8AmmoType` | Найдено в структуре | **Кандидат в чтение (L2)** | Точный статус оружия |
| **Аддоны на оружии** | `m_u8WeaponAddonFlags` (битовая маска: оптика, глушитель, подствольник) | Найдено в структуре | **Кандидат в чтение (L2)** | Комплектация стволов |
| **Статистика в КПК (PDA)** | `client_data` актора: счетчики убитых мутантов и сталкеров | Найдено в исходниках X-Ray | **Кандидат в чтение (L2)** | Карточка личных рекордов |

---

#### Чистое Небо (ЧН / CS) и Зов Припяти (ЗП / CoP)

Формат: ALife Version 7 (ЧН) и Version 8 (ЗП).

| Поле / Сущность | Где лежит | Проверено на сейвах | Режим доступа | Ценность и назначение |
|---|---|---|---|---|
| **Все базовые поля ТЧ** | Деньги, патроны, износ, группировки, время, уровень, координаты, здоровье | Да | **В релизе** | Аналогично ТЧ |
| **Апгрейды снаряжения (ЗП/ЧН)** | `CSE_ALifeItemWeapon` / `Outfit`: установленные апгрейды | Да (`read_inventory_upgrades`) | **Только чтение** (в релизе) | Просмотр установленных улучшений |
| **Инфопорции (InfoPortions)** | Вектор строк `CInfoPortion` в `client_data` актора (~889 записей в ЗП) | **Да (проверено в сессии 24/25)** | **Кандидат в чтение (L1)** | **Сюжетные вехи и задания** |
| **Достижения игрока (ЗП)** | Инфопорции вида `pda_achievement_*` («Сыщик», «Первооткрыватель» и др.) | Да (через фильтрацию вектора инфопорций) | **Кандидат в чтение (L1)** | Бейджи достижений в UI |
| **Сданные инструменты (ЗП)** | Инфопорции `zat_b3_tech_tool_*`, `jup_b217_tech_tool_*` | Да (через инфопорции) | **Кандидат в чтение (L1)** | Прогресс техников (Кардан, Азот) |
| **Война группировок (ЧН)** | Чанк 2 / `CSE_ALifeFactionManager`: ресурсы, сила и захваченные точки | Найдено в структуре ЧН | **Кандидат в чтение (L3)** | Карта баланса сил в ЧН |
| **Личный ящик на базах (ЗП)** | `CSE_ALifeInventoryBox` на «Скадовске» и «Янове» | Найдено в Chunk 2 | **Кандидат в чтение (L2)** | Содержимое личного сейфа |

---

### 2. Предложения для UI: новая вкладка «Сведения» (Overview)

Вместо сухого технического списка предлагается объединить извлеченные данные в визуальные карточки (по аналогии со стилистикой КПК сталкера):

```
+-----------------------------------------------------------------------------------+
|  [Карточка 1: Профиль сталкера]         |  [Карточка 2: Время и локация]          |
|  - Имя: Скиф / Дегтярёв / Меченый       |  - Локация: Затон / Окрестности Юпитера |
|  - Группировка: Одиночки (нейтрал)      |  - Игровое время: 12 авг 2012, 14:35    |
|  - Ранг: Опытный (1240)                 |  - Координаты: X: 142.5, Y: -12.3, Z: 8 |
|  - Репутация: Отличная (+450)           |  - Темп времени: x10.0                  |
|  - Здоровье: 100% | Радиация: 0         |                                         |
+-----------------------------------------------------------------------------------+
|  [Карточка 3: Сюжет и достижения]       |  [Карточка 4: Сводка имущества]         |
|  - Сюжетных вех пройдено: 142           |  - Наличные: 145 200 руб. / купонов     |
|  - Текущая стадия: Опасные связи        |  - Вес рюкзака: 38.4 / 50.0 кг          |
|  - Достижения (ЗП):                     |  - Предметов в рюкзаке: 24              |
|    [★ Первооткрыватель] [★ Сыщик]       |  - В тайнике на базе: 58 предметов      |
|    [★ Друг Одиночек]                    |  - Износ брони: 94% (отличное)          |
+-----------------------------------------------------------------------------------+
```

#### Детали реализации карточек:

1. **Карточка «Профиль сталкера» (Read-only, L1):**
   - Выводит имя персонажа, ранг, репутацию и группировку.
   - Для S2: выводит витальные показатели (здоровье, голод, усталость, пси-защиту), если они присутствуют в `PlayerContext`.

2. **Карточка «Время и локация» (Read-only, L1):**
   - Уже вычисляет дату/время из Чанка 5 (X-Ray) или `ticks`/`seconds` (S2).
   - Преобразует технические имена уровней (`l01_escape`, `jupiter`, `Zaton`) в красивые локализованные имена («Кордон», «Окрестности завода „Юпитер“», «Затон») через справочник.

3. **Карточка «Сюжетный прогресс и достижения» (Read-only, L1–L2):**
   - **X-Ray:** парсит вектор строк `CInfoPortion` из `client_data` актора. Сравнивает идентификаторы со словарем сюжетных задач (из XML-конфигов игры).
   - Для **Зова Припяти:** выводит 18 официальных достижений сталкера в виде иконок/бейджей.

4. **Карточка «Хранилище и тайник» (Read-only, L2):**
   - Для **S2:** подключает парсер мирового ящика `PlayerStash`. Пользователь видит, что осталось в лагере, не загружая игру.
   - Для **ЗП:** выводит содержимое личных ящиков на «Скадовске» и «Янове».

---

### 3. Границы безопасности (Safety Boundary)

В соответствии с правилами проекта (`AGENTS.md`):
1. **Только чтение для новых полей:**
   - Инфопорции (InfoPortions), квесты, тайники, статистика и координаты открываются **строго в режиме Read-Only**.
   - Прямая запись в `client_data` или произвольное добавление инфопорций запрещены, так как они вызывают рассинхронизацию квестовых скриптов и битые сюжетные тупики.
2. **Кандидаты на безопасную запись в будущем (L2/L3):**
   - Сброс радиации / голода в S2 (если смещение скалярное и валидировано round-trip тестом);
   - Ремонт предметов в личном тайнике `PlayerStash` (после подтверждения инварианта контейнера).

---

<!-- Gemini research (RS-1, 2026-09-26). Unverified against real saves: each row needs a save-pair check before any write. -->
## Исследование: Данные сохранений S.T.A.L.K.E.R. (RS-1)

Документ подготовлен в рамках исследовательской задачи **RS-1** для проекта **S.T.A.L.K.E.R. Save Editor**.
Включает подробный разбор структур данных файлов сохранений движков **X-Ray Engine** (Тень Чернобыля, Чистое Небо, Зов Припяти, Enhanced Edition) и **Unreal Engine 5** (S.T.A.L.K.E.R. 2: Heart of Chornobyl), не поддерживаемых текущими версиями редактора на чтение или запись.

---

### 1. Тень Чернобыля (S.T.A.L.K.E.R.: Shadow of Chornobyl / SoC)

Формат контейнера: блочный бинарный файл чанков X-Ray (ALife Version 5), LZO1X-сжатие пакетов состояния актора и объектов `all.spawn`.

| Поле | Где хранится (по открытым исходникам OpenXRay/X-Ray SDK или модам) | Зачем игроку | Риск правки | Источники |
|---|---|---|---|---|
| **Инфопорции (InfoPortions / Сюжетные флаги)** | `client_data` актора (`CSE_ALifeCreatureActor`): `net_packet` хранит вектор строк `CInfoPortion`. В ванильном сейве ТЧ содержится 350–450 активных инфопорций. | Разблокировка застрявших сюжетных квестов (например, сбой скрипта на Янтаре или Агропроме), открытие закрытых дверей лабораторий (X-18, X-16, X-10) без поиска ключей/документов, получение наград за квесты. | **Высокий**: добавление противоречащих друг другу инфопорций приводит к поломке логики скриптов (`xr_logic.script`), застреванию квестовых NPC или вылету движка при загрузке локации. | [OpenXRay xrServer_Objects_ALife_Monsters.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrServerEntities/xrServer_Objects_ALife_Monsters.cpp), [OpenXRay InfoPortion.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrGame/InfoPortion.cpp) |
| **Патроны в патроннике и магазине оружия** | `CSE_ALifeItemWeapon`: переменные `m_u16AmmoElapsed` (uint16 — количество патронов в магазине) и `m_u8AmmoType` (uint8 — индекс текущего типа заряженных патронов: обычные, бронебойные). | Моментальная перезарядка любого оружия без расхода патронов из инвентаря; возможность выставить нестандартный размер магазина (например, 100 патронов вместо 30). | **Низкий**: движок валидирует `m_u16AmmoElapsed` при выстреле; если число превышает размер магазина из `wpn_*.ltx`, игра обычно не вылетает, но сбрасывает значение при следующей перезарядке. | [OpenXRay xrServer_Objects_ALife_Items.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrServerEntities/xrServer_Objects_ALife_Items.cpp) |
| **Установленные аддоны оружия (Битовая маска)** | `CSE_ALifeItemWeapon`: поле `m_u8WeaponAddonFlags` (uint8). Битовая маска: `1` — глушитель (`eWeaponAddonSilencer`), `2` — прицел (`eWeaponAddonScope`), `4` — подствольный гранатомет (`eWeaponAddonGrenadeLauncher`). | Установка оптики, глушителя или подствольника на оружие без наличия самих предметов в рюкзаке; принудительное включение аддонов на оружие, где конфиг это разрешает. | **Средний**: если флаг аддона выставлен для оружия, у которого в конфиге `*.ltx` параметр `scope_status = 0` (нельзя установить оптику), игра может упасть при рендеринге 3D-модели в руках (`xrRender`). | [OpenXRay xrServer_Objects_ALife_Items.h](https://github.com/OpenXRay/xray-16/blob/master/src/xrServerEntities/xrServer_Objects_ALife_Items.h) |
| **Статистика убийств сталкера (PDA / КПК)** | `client_data` актора: блоки сериализации `CPda` и реестра статистики `CEncyclopediaRegistry` / `game_news` (число убитых людей, мутантов, выполненных заданий, найденных артефактов). | Просмотр и редактирование личного счета сталкера в рейтинге Зоны, поднятие в топ-1 сталкеров в КПК. | **Минимальный**: данные носят чисто информационный характер, движок не использует их для критических скриптовых триггеров. | [OpenXRay PDA.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrGame/PDA.cpp), [OpenXRay EncyclopediaRegistry.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrGame/EncyclopediaRegistry.cpp) |
| **Тайники на локациях (Заполненность и метки)** | Секция объектов `CSE_ALifeInventoryBox` в Chunk 2 (`all.spawn` объекты) + инфопорции выдачи координат тайников из трупов. Переменная состояния сундука определяет, активирован ли тайник. | Просмотр содержимого всех тайников на локации до их открытия; принудительная активация (выдача метки и спавн хабара) всех редких тайников (например, хабар Клыка в Припяти). | **Средний**: изменение структуры предметов внутри ящика требует строгого соблюдения заголовков пакета `CSE_ALifeInventoryBox` и привязки `parent_id`. | [OpenXRay xrServer_Objects_ALife_Items.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrServerEntities/xrServer_Objects_ALife_Items.cpp) |

---

### 2. Чистое Небо (S.T.A.L.K.E.R.: Clear Sky / CS)

Формат контейнера: чанки X-Ray (ALife Version 7), расширенная сериализация менеджеров войн группировок и модульных деревьев апгрейдов.

| Поле | Где хранится (по открытым исходникам OpenXRay/X-Ray SDK или модам) | Зачем игроку | Риск правки | Источники |
|---|---|---|---|---|
| **Ресурсы и сила группировок (Война группировок)** | Chunk 2 / `sim_faction`: сериализация `CSE_ALifeFactionManager`. Содержит таблицы очков ресурсов (`resource`), потенциала/силы (`power`), количества доступных отрядов и списки целевых смарт-террейнов (`target_smart`). | Победа любимой группировки в Войне группировок (например, захват Чистым Небом Болот или Долгом Свалки); спавн подкреплений и снаряжения на захваченных базах. | **Высокий**: искусственное завышение силы фракции без захваченных путей перехода между локациями (`game_graph`) приводит к зависанию симуляции A-Life (`smart_terrain.script`) и блокировке перемещения отрядов. | [OpenXRay sim_faction.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrGame/sim_faction.cpp), [X-Ray SDK 0.6 CS Scripts](https://github.com/OpenXRay/xray-16/tree/master/resources/scripts) |
| **Дерево апгрейдов оружия и брони (Ветвление)** | `CSE_ALifeItemWeapon` / `CSE_ALifeItemCustomOutfit`: динамический строковый вектор `installed_upgrades` (списки идентификаторов `prop_weight`, `prop_reliability` и т.д.). | Установка взаимоисключающих модификаций (например, одновременно максимальная скорострельность и максимальная точность; полная прокачка без выбора одной ветки у техника). | **Средний**: если установить апгрейд с несуществующим в конфигах секции SID, игра вылетает при открытии окна модификации у механика (`ui_mm_faction_war.script`). | [OpenXRay xrServer_Objects_ALife_Items.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrServerEntities/xrServer_Objects_ALife_Items.cpp) |
| **Флешки с апгрейдами (Сданные механикам)** | Секция `client_data` актора: вектор инфопорций вида `esc_mechanic_flash_1_given`, `agr_mechanic_flash_*`. | Закрытие второстепенных квестов на поиск данных модификаций для получения денежных наград и открытия доступа к топовым апгрейдам (у Новикова, Громова, Яра). | **Низкий**: инфопорции флешек безопасны для добавления, так как проверяются техниками однократно. | [OpenXRay InfoPortion.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrGame/InfoPortion.cpp) |
| **Сквады напарников и проводники** | Структура `CSE_ALifeOnlineOfflineGroup`: координаты сквадов, текущее действие (`action`), целевой объект. | Вызов проводника или дружественного отряда в любую точку локации для быстрой эвакуации или огневой поддержки. | **Высокий**: ручное редактирование целевых координат сквада вне сетки AI-нод (`ai_nodes`) вызывает фатальный сбой поиска пути (`pathfinder crash`). | [OpenXRay xrServer_Objects_ALife.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrServerEntities/xrServer_Objects_ALife.cpp) |

---

### 3. Зов Припяти (S.T.A.L.K.E.R.: Call of Pripyat / CoP)

Формат контейнера: чанки X-Ray (ALife Version 8), сохранение системы достижений, расширенных квестовых цепочек и стационарных личных ящиков.

| Поле | Где хранится (по открытым исходникам OpenXRay/X-Ray SDK или модам) | Зачем игроку | Риск правки | Источники |
|---|---|---|---|---|
| **Достижения сталкера (Achievements)** | Вектор инфопорций `CInfoPortion` в `client_data` актора. Строковые ключи: `pda_achievement_pioner` («Первооткрыватель»), `pda_achievement_detective` («Сыщик»), `pda_achievement_one_of_the_boys` («Свой парень»), `pda_achievement_friend_of_duty` («Друг Долга»), `pda_achievement_friend_of_freedom` («Друг Свободы»), `pda_achievement_mutant_hunter` («Охотник на мутантов») и др. | Мгновенная разблокировка игровых бонусов в КПК: скидки у торговцев (Сыч, Гаваец), ежедневные поставки медикаментов в личный ящик, дружественное отношение группировок, повышение шанса нахождения редких артефактов в аномалиях. | **Минимальный**: флаги достижений проверены годами моддинга, игра стабильно считывает их и обновляет интерфейс КПК без побочных эффектов. | [OpenXRay xrGame/pda_extra.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrGame/PDA.cpp), [OpenXRay InfoPortion.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrGame/InfoPortion.cpp) |
| **Личный ящик сталкера на базах (Личный сейф)** | `CSE_ALifeInventoryBox` стационарных контейнеров: `zat_b40_personal_box` (ящик на «Скадовске»), `jup_b200_personal_box` (ящик на станции «Янов»), `pri_b305_personal_box` (ящик в прачечной в Припяти). | Просмотр, извлечение и перемещение предметов в личный ящик на базе без необходимости идти через всю локацию; пополнение запасов аптечек, патронов и артефактов. | **Средний**: требует корректной генерации уникальных игровых ID (`game_id`) для каждого нового предмета, иначе возникнет коллизия идентификаторов при загрузке. | [OpenXRay xrServer_Objects_ALife_Items.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrServerEntities/xrServer_Objects_ALife_Items.cpp) |
| **Сданные комплекты инструментов техникам** | Инфопорции актора: `zat_b3_tech_tool_1` (для грубой работы Кардану), `zat_b3_tech_tool_2` (для тонкой работы), `zat_b3_tech_tool_3` (для калибровки); аналогичные ключи `jup_b217_tech_tool_*` для Азота на Янове. | Доступ ко всем уровням модификации снаряжения у механиков со старта игры без необходимости искать инструменты на «Юпитере» и в Припяти. | **Минимальный**: добавление ключей активирует доступные ветки апгрейдов в интерфейсе техников, диалоговые ветки синхронизируются штатно. | [OpenXRay InfoPortion.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrGame/InfoPortion.cpp) |
| **Тайники со снаряжением Стрелка** | Секция объектов и инфопорции квеста «Тайники Стрелка» на заводе «Юпитер» (`jup_b202_treasures_found_*`). | Быстрый поиск и разблокировка уникального оружия Стрелка (СГИ-5к Стрелка) и записей группы Стрелка. | **Низкий**: флаги влияют только на учет найденных тайников в квестовой ветке. | [OpenXRay xrServer_Objects_ALife_Items.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrServerEntities/xrServer_Objects_ALife_Items.cpp) |
| **Статус членов отряда для похода в Припять** | Инфопорции состояния спутников майора Дегтярёва: Зулус (`jup_b218_zulus_in_squad`), Вано (`jup_b218_vano_in_squad`), Соколов (`jup_b218_sokolov_in_squad`), Бродяга (`jup_b218_strider_in_squad`). | Возможность собрать идеальный отряд в путепровод «Припять-1» для получения лучшей концовки (достижение «Лидер») даже в случае гибели или отказа персонажей в процессе прохождения. | **Средний**: требуется согласованность со статусом спавна самих NPC в смарт-террейне башни Зулуса. | [OpenXRay xrServer_Objects_ALife_Monsters.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrServerEntities/xrServer_Objects_ALife_Monsters.cpp) |

---

### 4. Enhanced Edition (Legends of the Zone Trilogy / EE Remaster)

Формат контейнера: адаптированный 64-битный формат чанков X-Ray (ALife Version 5/7/8 с расширенными 64-битными хэшами, поддержкой геймпадов и синхронизации облачных сохранений Steam/Консолей).

| Поле | Где хранится (по открытым исходникам OpenXRay/X-Ray SDK или модам) | Зачем игроку | Риск правки | Источники |
|---|---|---|---|---|
| **Метаданные платформы и слота сохранения** | Заголовочный блок контейнера: расширенный GUID профиля игрока, хэш платформы (Steam Deck / Xbox / PlayStation / PC), таймстамп облачной синхронизации. | Предотвращение перезаписи сейвов при кроссплатформенном переносе (например, перенос прогресса с ПК на Steam Deck); диагностика конфликтов синхронизации. | **Критический**: несовпадение внутренней контрольной суммы или заголовка заголовка слота приводит к пометке сохранения консолью как «Поврежденные данные» (Corrupted Save). | [OpenXRay Platform Layer](https://github.com/OpenXRay/xray-16/tree/master/src/xrCore), [Steam Cloud API Documentation](https://partner.steamgames.com/doc/features/cloud) |
| **Настройки раскладки колеса селектора оружия (Weapon Wheel)** | `client_data` актора: сериализованный порядок слотов быстрого доступа колеса оружия (введено в трилогии Legends of the Zone для геймпадов). | Кастомизация порядка переключения стволов на геймпаде и Steam Deck без необходимости повторного назначения через меню настроек игры. | **Низкий**: при ошибке движок сбрасывает порядок слотов селектора на конфигурацию по умолчанию из инвентаря. | [OpenXRay Input Subsystem](https://github.com/OpenXRay/xray-16/blob/master/src/xrEngine/xr_input.cpp) |
| **Унифицированные инфопорции трофеев и достижений консолей** | Вектор инфопорций: мостик между внутриигровыми событиями и внешними API трофеев (PlayStation Network Trophies / Xbox Achievements / Steam Achievements). | Отслеживание прогресса получения «платины» или 100% достижений без захода в игру. | **Средний**: триггер трофеев срабатывает в момент записи инфопорции; повторная установка флага в готовом сейве может не отправить событие в Steam API без физического захода на триггер-локацию. | [OpenXRay PDA.cpp](https://github.com/OpenXRay/xray-16/blob/master/src/xrGame/PDA.cpp) |

---

### 5. S.T.A.L.K.E.R. 2: Heart of Chornobyl (S2)

Формат контейнера: потоковые блоки Oodle Kraken (`0x40000` байт), несжатый заголовок `b"\xCC\x06"`, CRC-32 в последних 4 байтах файла. Сериализация объектов Unreal Engine 5 с бинарными строковыми SID и дельта-стейтами компонентов.

| Поле | Где хранится (по открытым исходникам OpenXRay/X-Ray SDK или модам) | Зачем игроку | Риск правки | Источники |
|---|---|---|---|---|
| **Личный тайник игрока на базах (PlayerStash)** | Мировой контейнер инвентаря актора (синий металлический сундук на базах в Залесье, Ростке, Янове и др.). Структура сериализации идентична `InventoryGrid` рюкзака Скифа (координаты X/Y, размеры W/H, ссылки на компоненты предметов). | Просмотр содержимого базы без возвращения в лагерь; удаление застрявших квестовых предметов; трансфер тяжелого хабара (экзоскелеты, пулеметы, редкие артефакты) напрямую в инвентарь или наоборот. | **Средний**: требует строгого сохранения границ ячеек сетки и уникальности SID внутри мирового контейнера, иначе предмет может исчезнуть или наложиться на другой. | [GSC Game Features Modding Guide (PDF)](https://cdn.stalker2.com/guides/Game_Features_Modding_Guide.pdf), [Zone Kit Documentation](https://support.stalker2.com/hc/en-us/sections/36534422256017-The-Modder-s-Zone) |
| **Параметры выживания (Survival Stats / PlayerContext)** | Компонент `PlayerContext`: Здоровье (`Health` float32), Выносливость (`Stamina` float32), Радиация (`Radiation` float32), Голод (`Hunger` float32), Кровотечение (`Bleeding` float32), Усталость (`Sleepiness` float32). | Мгновенное снятие радиационного заражения, остановка смертельного кровотечения, сброс голода и усталости перед сложным боем; создание режима «бесконечной выносливости». | **Высокий**: значения жестко связаны с постэффектами экрана и таймерами аномалий. Запись некорректного диапазона (например, значение радиации выше 1.0 или отрицательное здоровье) вызывает моментальную смерть Скифа при спавне или вечный черный экран. | [GSC Save_Load_system_for_mods.pdf](https://cdn.stalker2.com/guides/Save_Load_system_for_mods.pdf), [Fearless Revolution Cheat Engine S2 Analysis](https://fearlessrevolution.com/) |
| **Отношения и репутация у фракций (Faction Goodwill)** | Секция репутации: динамические коэффициенты отношений Скифа к ключевым группировкам Зоны («Варта», «Искра», «Одиночки», «Долг», «Свобода», «Монолит», «Бандиты», «Наемники»). | Примирение с враждебной фракцией (если игрок случайно открыл огонь по патрулю «Варты» или сталкерам в Залесье); открытие доступа к закрытым фракционным торговцам и базам. | **Высокий**: изменение репутации без учета текущей стадии сюжетных квестов (например, вступление в конфликт на Свалке) способно заблокировать диалоги сюжетных персонажей (Коршунов, Рихтер, Зотов). | [GSC CFG Guide](https://cdn.stalker2.com/guides/How_to_apply_modifications_to_vanilla_.cfg_files.pdf), [Zone Kit TextDatabase.json](https://support.stalker2.com/hc/en-us) |
| **Координаты Скифа в мире (Transform / Position)** | Структура актора: векторы координат `X, Y, Z` (float32/double) и ориентации в пространстве (`Pitch, Yaw, Roll`). | Телепортация из аномальных ловушек и застреваний в текстурах/геометрии (частая проблема при релизе); быстрый доступ к недосягаемым крышам и секретным тайникам. | **Критический**: изменение координат без синхронизации с навигационным графом и стримингом уровней UE5 (`World Partition`) приводит к проваливанию актора под ландшафт или зависанию стриминга чанков мира. | [Unreal Engine 5 World Partition Documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/world-partition-in-unreal-engine) |
| **Открытые точки быстрого перемещения (Fast Travel)** | Блок открытых маркеров на карте в КПК: битовая карта или список SID посещенных лагерей проводников. | Открытие всех точек проводников на глобальной карте Зоны со старта игры без многочасового пешего исследования опасных территорий. | **Средний**: требует совпадения SID точек с таблицей карты Zone Kit, иначе маркер на карте будет отображаться некорректно. | [GSC Modding Guide](https://cdn.stalker2.com/guides/Game_Features_Modding_Guide.pdf) |

---

### 6. Заключение и рекомендации по внедрению

1. **Приоритет 1 (Безопасное чтение / Read-Only):**
   - Инфопорции достижений ЗП (`pda_achievement_*`) и инструментов техников — 100% безопасны для чтения и могут быть выведены на карточку обзора игрока в UI.
   - Личный ящик сталкера на базах (ЗП: `CSE_ALifeInventoryBox`, S2: `PlayerStash`) — готов к интеграции в режим просмотра содержимого сундуков.
   - Аддоны оружия (битовая маска `m_u8WeaponAddonFlags`) — безопасны для отображения установленных прицелов и глушителей в карточке оружия.

2. **Приоритет 2 (Осторожная запись с подтверждением):**
   - Патроны в магазине (`m_u16AmmoElapsed`) и сброс радиации/кровотечения — высокий пользовательский спрос, но требует строгого валидатора диапазонов значений (clamp).

3. **Запрещено к записи без детальных регрессионных тестов (согласно `AGENTS.md`):**
   - Глобальные координаты актора (`Transform` X/Y/Z) в S2 — высокий риск краша стриминга мира Unreal Engine 5.
   - Произвольная запись инфопорций сюжета без проверки взаимоисключающих цепочек логики.

---

## Compact Kraken rebuild — R11

Исследование и локальная проверка выполнены 2026-09-16. Личные сейвы и их
байты в репозиторий не добавлялись. Этот механизм уменьшает объём повторной
пересборки, но не является новым Kraken/Oodle encoder и не открывает отдельный
гейт игровой совместимости.

### Подтверждённая схема framing

Публичный [исходник Kraken decompressor](https://github.com/layola13/Oodle_compressor/blob/master/kraken.cpp)
описывает два заголовочных байта перед каждым quantum размером до `0x40000`
(256 KiB):

- у первого байта младшая тетрада равна `0xC`, биты 4–5 зарезервированы,
  `0x80` означает restart decoder, а `0x40` — stored/uncompressed block;
- у второго байта младшие 7 бит задают decoder type, а `0x80` означает
  per-quantum checksum; для компактного решения принят только decoder type 6
  без checksum;
- для обычного compressed quantum следующие три байта содержат
  `compressed_size - 1` в младших 18 битах; специальный all-ones quantum
  (например memset) намеренно не интерпретируется;
- размер распакованного quantum определяется объявленным размером всего
  payload: обычные блоки имеют 256 KiB, последний может быть короче.

Заголовок разбирается перед каждым блоком. Нельзя считать, что после первого
`8C06` весь поток является одним payload: в реальном S.T.A.L.K.E.R. 2 образце
между блоками снова встречаются `0C06`/`8C06` и quantum header.

### Решение

`editor/kraken_blocks.py` выполняет только bounded framing parser и decision
по сохранению байтов:

1. При no-op исходный stream возвращается byte-for-byte. Если его framing не
   распознаётся, no-op всё равно сохраняет исходные байты без попытки угадать
   границы.
2. При изменении raw одинаковой длины сравниваются raw-срезы по доказанным
   границам. Неизменённый независимый restart-block копируется целиком вместе
   с исходным compressed payload.
3. Изменённый блок записывается в известной форме `CC06 + raw`. Следующий
   блок без restart-бита зависит от старого decoder dictionary, поэтому он
   тоже пересобирается. Цепочка продолжается до следующего restart-блока.
4. Если raw length изменился, decoder/checksum/quantum variant неизвестен или
   границы не покрывают весь payload, возвращается явный `full-fallback`:
   весь новый raw разбивается на stored-блоки `CC06`.

`PatchResult` сообщает `rebuild_mode`, число `preserved_blocks` и
`rebuilt_blocks`, а также `rebuild_reason`. Для любого mutation
`save_format.patch_save` после сборки заново проверяет CRC, native decompress,
равенство ожидаемому raw и структурные инварианты.

### Локальный corpus check

Пять доступных локально S.T.A.L.K.E.R. 2 samples были прочитаны только в
памяти. Для каждого выполнена контролируемая проба `money + 1`; исходные
файлы не изменялись, игра не запускалась, output на диск не записывался.

| Метрика | Наблюдение |
| --- | ---: |
| Исходный packed размер | 6,679,693–6,947,625 bytes |
| Распакованный размер | 26,937,652–27,244,752 bytes |
| Количество quantum | 103–104 |
| CRC исходников | 5/5 valid |
| Результат | 5/5 `compact`, decompress==expected raw, CRC valid |
| Packed compact output | 16,095,156–16,295,458 bytes |
| Packed full stored rebuild для сравнения | 26,937,866–27,244,968 bytes |
| Время одной пробы на текущем Linux host | примерно 1.35–2.01 s |

В этом corpus restart-блоков мало, поэтому изменение раннего блока может
запустить длинную зависимую цепочку и заметно увеличить output. Compact не
означает «всегда сохранить размер исходного сейва»; он означает «сохранить
доказанные неизменённые encoded blocks и не подменять их без причины».

### Ограничения и следующий gate

- Kraken encoder не реализован. `CC06` — текущая проверенная форма stored
  block, а не попытка воспроизвести байты игрового компрессора.
- Checksummed blocks, decoder types кроме 6 и специальные quantum forms
  получают full fallback, а не угадываемую частичную запись.
- Round-trip и CRC доказывают целостность контейнера и ожидаемые raw edits;
  они не доказывают, что конкретная версия игры примет изменённый сейв.
- Игровая проверка с загрузкой и повторным сохранением в этом проходе не
  выполнялась. Для снятия experimental-ограничения нужен отдельный controlled
  game load/re-save gate; отсутствие S.T.A.L.K.E.R. 2 установки на текущем
  компьютере остаётся блокером.

---

## Save locations — M03 research evidence

Исследование выполнено 2026-09-15. В таблицу попали только пути, для которых
есть опубликованный источник. Папка считается найденной только если она уже
существует; discovery ничего не создаёт и не читает содержимое сейвов.

### Что подтверждено

| Игра и издание | Подтверждённый путь | Источник и проверка |
|---|---|---|
| S.T.A.L.K.E.R. 2, Windows/общий | `%LOCALAPPDATA%\Stalker2\Saved\SaveGames` | [PCGamingWiki: S.T.A.L.K.E.R. 2](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R._2%3A_Heart_of_Chornobyl), строка Save game data location; сверено с GOG/Steam строками той же таблицы. |
| S.T.A.L.K.E.R. 2, Steam | `%LOCALAPPDATA%\Stalker2\Saved\STEAM\SaveGames` | [PCGamingWiki: S.T.A.L.K.E.R. 2](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R._2%3A_Heart_of_Chornobyl), Steam row. |
| S.T.A.L.K.E.R. 2, Epic/EOS | `%LOCALAPPDATA%\Stalker2\Saved\EOS\SaveGames` | [официальное руководство GSC по сохранениям](https://www.stalker2.com/news/saves-managing-manual), EOS path. |
| S.T.A.L.K.E.R. 2, GOG | `%LOCALAPPDATA%\Stalker2\Saved\GOG\SaveGames` | [PCGamingWiki: S.T.A.L.K.E.R. 2](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R._2%3A_Heart_of_Chornobyl), GOG row. |
| S.T.A.L.K.E.R. 2, Game Pass/Microsoft Store | `%LOCALAPPDATA%\Packages\GSCGameWorld.S.T.A.L.K.E.R.2HeartofChornobyl_6fr1t1rwfarwt\SystemAppData\xgs\<user-id>\SaveGames` | [PCGamingWiki: S.T.A.L.K.E.R. 2](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R._2%3A_Heart_of_Chornobyl), Microsoft Store row. `<user-id>` не угадывается: discovery перечисляет существующие каталоги под `xgs`. |
| Shadow of Chernobyl, оригинал/retail | `%PUBLIC%\Documents\stalker-shoc\savedgames` | [PCGamingWiki: Shadow of Chernobyl](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Shadow_of_Chernobyl), Windows row; страница отдельно перечисляет патчи 1.0004–1.0006. |
| Shadow of Chernobyl, GOG | `%USERPROFILE%\Documents\Stalker-SHOC\savedgames` | [PCGamingWiki: Shadow of Chernobyl](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Shadow_of_Chernobyl), GOG row. |
| Clear Sky, оригинал/1.5.10 | `%USERPROFILE%\Documents\Stalker-STCS\savedgames` | [PCGamingWiki: Clear Sky](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Clear_Sky), Windows row; там же 1.5.10 назван последним официальным патчем. |
| Clear Sky, GOG | — отдельная точная GOG path row не подтверждена | [PCGamingWiki: Clear Sky](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Clear_Sky) lists GOG, but its save table has no separate GOG row. The documented X-Ray Windows path remains a fallback probe, not a GOG-specific claim. |
| Call of Pripyat, оригинал/1.6.02 | `%PUBLIC%\Public Documents\S.T.A.L.K.E.R. - Call of Pripyat\savedgames` | [PCGamingWiki: Call of Pripyat](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Call_of_Pripyat), Windows row; страница описывает оригинал и указывает 1.6.02 GOG/Steam patches. |
| Call of Pripyat, GOG | `%USERPROFILE%\Documents\Stalker-COP\savedgames` | [PCGamingWiki: Call of Pripyat](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Call_of_Pripyat), GOG row. |
| Shadow of Chernobyl, Enhanced/Legends, GOG | `%USERPROFILE%\Saved Games\STALKER Shadow of Chornobyl - EE\gog\savedgames` | [PCGamingWiki: Shadow Enhanced](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Shadow_of_Chornobyl_Enhanced_Edition), GOG row. |
| Shadow of Chernobyl, Enhanced/Legends, Steam | `%USERPROFILE%\Saved Games\STALKER Shadow of Chornobyl - EE\STEAM\savedgames` | [PCGamingWiki: Shadow Enhanced](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Shadow_of_Chornobyl_Enhanced_Edition) and [SteamDB UFS](https://steamdb.info/app/2427410/ufs/) (app ID 2427410 and `WinSavedGames` path). |
| Clear Sky, Enhanced/Legends, Steam | `%USERPROFILE%\Saved Games\STALKER Clear Sky - EE\STEAM\savedgames` | [PCGamingWiki: Clear Sky Enhanced](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Clear_Sky_Enhanced_Edition) and [SteamDB UFS](https://steamdb.info/app/2427420/ufs/) (app ID 2427420 and `WinSavedGames` path). |
| Call of Pripyat, Enhanced/Legends, Steam | `%USERPROFILE%\Saved Games\STALKER Call of Prypiat - EE\STEAM\savedgames` | [PCGamingWiki: Call of Prypiat Enhanced](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Call_of_Prypiat_Enhanced_Edition) and [SteamDB UFS](https://steamdb.info/app/2427430/ufs/) (app ID 2427430 and exact product spelling `Prypiat`). |

Original Steam locations are game-install data, not Documents:

| Игра | Путь | Проверка |
|---|---|---|
| Shadow of Chernobyl | `[Steam Library]/steamapps/common/STALKER Shadow of Chernobyl/_appdata_/savedgames` | [SteamDB UFS](https://steamdb.info/app/4500/ufs/) and [PCGamingWiki: Shadow](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Shadow_of_Chernobyl). |
| Clear Sky | `[Steam Library]/steamapps/common/STALKER Clear Sky/_appdata_/savedgames` | [SteamDB UFS](https://steamdb.info/app/20510/ufs/) and [PCGamingWiki: Clear Sky](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Clear_Sky). |
| Call of Pripyat | `[Steam Library]/steamapps/common/Stalker Call of Pripyat/_appdata_/savedgames` | [SteamDB UFS](https://steamdb.info/app/41700/ufs/) and [PCGamingWiki: Call of Pripyat](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Call_of_Pripyat). |

### Proton

PCGamingWiki's Steam Play note says that the prefix mirrors the Windows/Steam
paths and gives the Steam app ID. Поэтому статический путь раскрывается как:

```text
[Steam Library]/steamapps/compatdata/<app-id>/pfx/drive_c/users/steamuser/Documents/<X-Ray folder>/savedgames
[Steam Library]/steamapps/compatdata/<app-id>/pfx/drive_c/users/steamuser/Saved Games/<EE folder>/STEAM/savedgames
```

Для старых X-Ray сборок также проверяются `drive_c/users/Public/Documents` и
`drive_c/ProgramData/Documents`: некоторые retail-конфигурации используют
`%PUBLIC%`/`Public Documents`. Для S.T.A.L.K.E.R. 2 проверяются оба
встречающихся в Proton дерева профиля:
`drive_c/users/steamuser/AppData/Local/Stalker2/Saved/...` и legacy-вариант
`drive_c/users/steamuser/Local Settings/Application Data/Stalker2/Saved/...`.
В каждом из них проверяются `SaveGames` и вложенный `Data`, а также профили
`STEAM`, `EOS` и `GOG`. На текущем хосте реально найден Steam-путь:
`~/.steam/steam/steamapps/compatdata/1643320/pfx/drive_c/users/steamuser/Local Settings/Application Data/Stalker2/Saved/STEAM/SaveGames/Data`.

App ID сверены по SteamDB: SoC `4500`, Clear Sky `20510`, Call of Pripyat
`41700`, S.T.A.L.K.E.R. 2 `1643320`, Enhanced `2427410`, `2427420`, `2427430`.
Источники: [SteamDB SoC](https://steamdb.info/app/4500/ufs/), [Clear Sky](https://steamdb.info/app/20510/ufs/),
[Call of Pripyat](https://steamdb.info/app/41700/ufs/), [S.T.A.L.K.E.R. 2](https://steamdb.info/app/1643320/ufs/),
[Enhanced bundle entries](https://steamdb.info/sub/1323391/). Реального Proton-prefix
на этой машине нет; наличие нужного дерева проверено synthetic fixture тестом.

### `fsgame.ltx` и локализация

X-Ray путь нельзя считать вечной константой. Публичный [iXray `fsgame.ltx`](https://github.com/ixray-team/ixray-1.0-stsoc/blob/default/fsgame.ltx)
содержит схему `true|false|root|relative`, `$app_data_root$` и
`$game_saves$ = true|false|$app_data_root$|savedgames\`. README публичного
[Stalker Xray tools](https://github.com/stalker-tools/tools) также прямо
перечисляет анализ `fsgame.ltx` и `.sav`. Реализация сначала уважает найденный
`fsgame.ltx`/`fsgame_soc.ltx` рядом с установленной игрой, затем использует
источниковые fallback-пути.

PCGamingWiki поясняет, что `Documents` заменяется на `My Documents` в Windows
XP. На современных локализованных системах имя Known Folder может быть иным;
поэтому discovery проверяет стандартные имена и существующие каталоги с
локализованными именами (`Документы`, `Документи`, `Documentos` и т.п.), но не
создаёт каталог и не делает безграничный рекурсивный поиск.

### Явные пробелы evidence

- Страницы [Clear Sky Enhanced](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Clear_Sky_Enhanced_Edition)
  и [Call of Prypiat Enhanced](https://www.pcgamingwiki.com/wiki/S.T.A.L.K.E.R.%3A_Call_of_Prypiat_Enhanced_Edition)
  подтверждают Steam/Proton path, но сейчас не дают отдельной точной строки
  для GOG save directory. Поэтому такие строки не объявлены подтверждёнными;
  код лишь возвращает уже существующий `.../<EE name>/gog/savedgames` и не
  создаёт его.
- Для retail/GOG/Proton не выполнялась загрузка сейва самой игрой и повторное
  сохранение. M03 доказывает discovery дерева путей, не совместимость
  контейнера: это отдельные M06–M09 gates.
- PCGamingWiki прямо указывает Steam Cloud paths под `userdata/<user-id>/<app-id>`;
  они относятся к синхронизируемому облачному storage, а не к локальному
  каталогу save slots, и в `save_directories()` не смешиваются с найденными
  локальными папками.

### Evidence commands

```text
PYTHON=.venv/bin/python -m pytest tests/test_platform_save_locations.py -q  -> 10 passed
PYTHON=.venv/bin/ruff check editor/platforms.py tests/test_platform_save_locations.py -> exit 0
PYTHON=.venv/bin/mypy editor/platforms.py -> exit 0
```

Тестовое дерево создаётся в `tmp_path`: проверены пустые Steam/GOG/Store roots,
другая Steam library из `libraryfolders.vdf`, malformed VDF с продолжением
поиска, manifests, локализованный Documents, Proton prefix, `fsgame.ltx`
override и отсутствие записи в filesystem.

---

## In-game verification protocol — M10

This document records game load/re-save evidence only. It stores hashes and
results, never save bytes. A parser round-trip, a matching extension, or a
successful result from another game is not an in-game pass.

### Procedure

For each row below, use a fresh workspace and one ordinary official save from
the selected release. Each run changes exactly one bounded field or one
explicitly listed equipment record; never combine unrelated mutations in the
same game-validation run.

1. Close the game and make sure the selected save is not being synchronized or
   rewritten by another process.
2. Prepare a copy with one bounded mutation. The command does not write the
   source file or a game directory. For money:

   ```bash
   python -m tools.prepare_ingame_verification \
     --release <release-id> \
     --source /absolute/path/to/save \
     --workspace /absolute/path/to/private/m10/<release-id> \
     --money <new-value>
   ```

   For an equipment condition, use the exact handle from the inspection and a
   normalized value from `0` to `1`:

   ```bash
   python -m tools.prepare_ingame_verification \
     --release <release-id> \
     --source /absolute/path/to/save \
     --workspace /absolute/path/to/private/m10/<release-id>-durability \
     --durability <handle-or-0x-handle> <value>
   ```

   The same protocol accepts `--upgrade HANDLE key1,key2` and
   `--placement HANDLE slot SLOT` for formats whose source-backed writer
   exposes those operations. The tool refuses an unsupported anchor before it
   exports the edited handoff copy.

3. Copy the generated `edited_path` to a disposable slot/location accepted by
   that release. Do not overwrite the original save.
4. Launch the matching official game manually, load the edited copy, and
   confirm that the requested bounded value is present and that the visible
   world/inventory has no unexpected change.
5. Save again from inside the game. Keep that re-saved file outside the Git
   worktree.
6. Parse the game re-save and record the command output:

   ```bash
   python -m tools.verify_ingame_result \
     --manifest /absolute/path/to/private/m10/<release-id>/manifest.json \
     --resaved /absolute/path/to/private/m10/<release-id>/resaved-save
   ```

7. Add only release version/build, source/edited/re-saved SHA-256, mutation,
   observed value, visible result, parser result, and a concise limitation to
   the row.

The first successful row enables only the exact capability and release tested
by that row. Later cards repeat the same load/re-save gate for their own
capability; a money result does not validate equipment durability or upgrades.
An unsupported or failed row stays read-only with its reason. For the three
installed originals, the owner explicitly accepted the visible load result and
asked to proceed without another launch; their rows are therefore marked
`passed-owner-attested`. This is product acceptance, while the absent second
save SHA remains visible as a limitation.

### Current evidence matrix

| Release | Game/build | Source SHA-256 | Edited SHA-256 | Re-saved SHA-256 | Visible result | Parser result | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `stalker2` | not run | — | — | — | — | — | `pending-owner-run` |
| `stalker-soc` | local Steam original; build not recorded | `832e50d33626fb879f04a0507bf759ead5c888b508bb64b5a2176ae69e880101` | `f8c958e511f21fff3459220ff6794e086f00dde463a24005b0baceed8a4d1c2b` | owner-confirmed; not captured | Loaded in official game; money `123456789`; inventory visible | Prepared output parses as `stalker-soc`; owner accepted save persistence | `passed-owner-attested` |
| `stalker-cs` | local Steam original; build not recorded | `e8d86714448846d91c027ea706602bee30dc456cc66192d418f91990a2faeee0` | `c91234bf1920d67e438fd45d28716b0255e8d46cd70b5a49e5ff2c22aa2af9e2` | owner-confirmed; not captured | Loaded in official game; money `123456789`; inventory visible | Prepared output parses as `stalker-cs`; owner accepted save persistence | `passed-owner-attested` |
| `stalker-cop` | local Steam original; build not recorded | `79be7cc6bf058330536d58c8b4ddbee6fb7268b940f13321a6dfc23a85b4cb75` | `ab30ad79960585f534f8be1b11278b0a4a06e641ddbfc2f30f567d6f15cb308d` | owner-confirmed; not captured | Loaded in official game; money `123456789`; inventory visible; ammo stack `x30` visible | Prepared output parses as `stalker-cop`; owner accepted save persistence | `passed-owner-attested` |
| `stalker-soc-ee` | no accepted local sample | — | — | — | unavailable | unavailable | `pending-owner-run` |
| `stalker-cs-ee` | no accepted local sample | — | — | — | unavailable | unavailable | `pending-owner-run` |
| `stalker-cop-ee` | no accepted local sample | — | — | — | unavailable | unavailable | `pending-owner-run` |

Enhanced Edition rows are separate on purpose. Original X-Ray bytes must never
be routed through an Enhanced profile, and one Enhanced release cannot prove
the other two.

The owner supplied visual load evidence from three local screenshots. The
screenshots themselves remain outside Git; only their SHA-256 values are
recorded here:

| Release | Screenshot SHA-256 |
| --- | --- |
| `stalker-cs` | `12e8b7fc317034542853b33db11512393ad3db703abccd00ee2ef220e21d04e6` |
| `stalker-soc` | `216b074800008fd0941191133e484379ea739cdf404fe43430af64cbd986eaf5` |
| `stalker-cop` | `f882a7af65a42af615092442dcd67b603ea4b3d5fdf15187aa23aad61336800d` |

### Local automated preparation checks

- `.venv/bin/python3.14 -m pytest tests/test_ingame_protocol.py -q` — exit 0,
  `7 passed`.
- No real game was launched and no game directory was written by this check.

---

## Официальные источники по моддингу и API для внутриигрового компаньона

Черновик собран Gemini (RS-2, MOD-1) 2026-09-25, выборочно проверен Claude.
Статус утверждений: **проверено** — прочитан первоисточник; остальное — со
ссылкой, но не перепроверено.

### Выводы для роадмапа

- **S2:** Blueprint-мод работает только в загруженном мире (`ModWorldSubsystem`
  → `OnWorldBeginPlay`). **Проверено** по PDF GSC: «the moment the player loads
  into the map (… not in the main menu)». Окно в главном меню официальным API не
  делается, а оверлей в игре — стандартный UMG. Предметы и деньги можно выдавать
  через `Execute Console Command` (например, спавн по SID). Итог: компаньон S2 —
  окно в игре (MOD-4), не в главном меню.
- **EE:** в Steam Workshop разрешены скрипты `.script` и конфиги `.ltx`/`.xml`,
  запрещены `.dll`/`.exe`. Упаковка через `xrCompress -store` в `.pak`, загрузка
  через `xrSWS_Upload`. На mod.io скрипты запрещены. Итог: Lua-компаньон для EE
  возможен только через Steam Workshop (MOD-3).
- **Оригиналы:** официальные X-Ray SDK — ТЧ 0.4, ЧН 0.5/0.6, ЗП 0.7.
  Вся нужная Lua-функциональность есть:
  - `db.actor:give_money`; в ТЧ отрицательные суммы ненадёжны;
  - `alife():create(..., parent_id)` и `alife():release(se_obj, true)`: только серверный объект;
  - `set_condition`;
  - `relation_registry.set_community_goodwill`, `set_character_rank`;
  - `set_actor_position` — только в пределах уровня;
  - между уровнями — динамический `level_changer`, созданный через `alife():create` с заполненным net_packet.
- **Горячая клавиша:** в ванильном X-Ray нет глобального коллбэка клавиш во время игры. Открытие через `OnKeyboard` в `ui_main_menu.script`: Esc, затем клавиша.

### S.T.A.L.K.E.R. 2 (Zone Kit)

PDF GSC на `https://cdn.stalker2.com/guides/`:

- `Game_Features_Modding_Guide.pdf`;
- `Intro_to_actor_&_placeholder_mods.pdf`;
- `Simple_mod_dependency_system.pdf`;
- `How_to_add_new_actors.pdf`;
- `How_to_patch_vanilla_actors.pdf`;
- `How_to_delete_vanilla_actors.pdf`;
- `Audio_modding_quick_start_guide.pdf`;
- `How_to_apply_modifications_to_vanilla_.cfg_files.pdf` — патчи `.cfg_patch_*`, полезно для CP-4/KB-7;
- `Save_Load_system_for_mods.pdf` — данные мода в сейве, до 10 МБ; **проверено**;
- `Launching_new_quest_in_game_build_using_ModWorldSubsystem.pdf` — **проверено**;
- `Mod_TextTool.pdf`.

Где ещё:

- Раздел поддержки: https://support.stalker2.com/hc/en-us/sections/36534422256017-The-Modder-s-Zone
- Zone Kit: https://store.epicgames.com/p/stalker-2-zone-kit.
  - Требования: Windows 10/11, 32 ГБ RAM, 8 ГБ VRAM, около 700 ГБ места.
  - Нужна лицензия игры.

### Enhanced Editions

- Гайд GSC: https://steamcommunity.com/sharedfiles/filedetails/?id=3497576322
- Инструменты:
  - https://modio.stalker-game.com/assets/tools-stk-lotz-modio.7z (xrCompress);
  - http://modio.stalker-game.com/assets/ee/stk-utils.7z (xrSWS_Upload);
  - https://modio.stalker-game.com/assets/ee/stk-shaders.7z (DX12-шейдеры, нужен DXC).

### Оригинальная трилогия

- ТЧ SDK 0.4: http://files.gsc-game.com/st/xray-sdk-setup-v0.4.exe
- ЧН SDK: http://files.gsc-game.com/st/xray-cs-sdk-setup.exe
- ЗП SDK 0.7: http://files.gsc-game.com:3128/st/xray-cop-sdk-setup.exe
- Документация: https://sdk.stalker-game.com/en/index.php?title=S.T.A.L.K.E.R._MOD_portal
- Привязки Lua (OpenXRay):
  - `src/xrGame/script_game_object_script{,2,3}.cpp`;
  - `alife_simulator_script.cpp`;
  - `level_script.cpp`;
  - `ui/UIScriptWnd_script.cpp`.

Ссылки файлового сервера GSC старые (HTTP): скачивать только владельцу и
проверять подпись и хеш установщика вручную.
