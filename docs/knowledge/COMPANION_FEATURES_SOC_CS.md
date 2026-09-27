# Возможности мода-компаньона в ТЧ и ЧН: анализ движкового API и скриптов

Документ сопоставляет возможности компаньона ЗП (Call of Pripyat, X-Ray 1.6, `mods/companion` после PR #36) с движками и скриптами ТЧ (Shadow of Chernobyl, X-Ray 1.0) и ЧН (Clear Sky, X-Ray 1.5).

Каждый факт верифицирован по эталонным дампам `lua_help.script`, скриптам и конфигурационным файлам распакованных ресурсов оригинальных игр:
- **ТЧ (SoC)**: `gamedata_soc/scripts/lua_help.script` (8184 строки), `configs/`
- **ЧН (CS)**: `gamedata_cs/scripts/lua_help.script` (12288 строк), `configs/`
- **ЗП (CoP)**: `gamedata/scripts/lua_help.script` (12067 строк), `configs/`

---

## 1. Сводная таблица функций

| # | Функция / Механика | ЗП (CoP, X-Ray 1.6) | ТЧ (SoC, X-Ray 1.0) | ЧН (CS, X-Ray 1.5) |
|---|--------------------|---------------------|----------------------|--------------------|
| **1.1** | **Режим: бессмертие (`god`)** | `db.actor.health = 1`, `psy_health = 1`, `radiation = -1` (`lua_help:7322, 7325, 7327`). **Поддерживается.** | `db.actor.health = 1`, `psy_health = 1`, `radiation = -1` (`lua_help:5036, 5039, 5040`). **Поддерживается.** | `db.actor.health = 1`, `psy_health = 1`, `radiation = -1` (`lua_help:7175, 7178, 7179`). **Поддерживается.** |
| **1.2** | **Режим: выносливость (`stamina`)** | `db.actor.power = 1` (`lua_help:7326`). **Поддерживается.** | `db.actor.power = 1` (`lua_help:5038`). **Поддерживается.** | `db.actor.power = 1` (`lua_help:7177`). **Поддерживается.** |
| **1.3** | **Режим: без кровотечения (`bleeding`)** | `db.actor.bleeding = 1` (`lua_help:7324`). **Поддерживается.** | В движке ТЧ нет свойства `bleeding` для записи (только `get_bleeding() const` на строке 5331). Прямое присваивание `actor.bleeding` вызывает ошибку luabind. **Не поддерживается движком напрямую.** *Ванильная замена:* использование бинта (`actor:eat(bandage)`) либо артефакт на поясе с `bleeding_restore_speed = 100.0`. | В движке ЧН нет свойства `bleeding` для записи (только `get_bleeding() const` на строке 7450). **Не поддерживается движком напрямую.** *Ванильная замена:* использование бинта или артефакт с `bleeding_restore_speed = 100.0`. |
| **1.4** | **Режим: авторемонт (`repair`)** | `item_in_slot(n)` (`lua_help:7401`), `set_condition(1)` (`lua_help:7435`). **Поддерживается.** | `item_in_slot(n)` (`lua_help:5108`), `set_condition(1)` (`lua_help:5187`). **Поддерживается.** | `item_in_slot(n)` (`lua_help:7254`), `set_condition(1)` (`lua_help:7288`). **Поддерживается.** |
| **1.5** | **Режим: патроны (`ammo`)** | `active_item()` (`lua_help:7331`), `set_ammo_elapsed(n)` (`lua_help:7434`). **Поддерживается.** | `active_item()` (`lua_help:5045`), `set_ammo_elapsed(n)` (`lua_help:5272`). **Поддерживается.** | `active_item()` (`lua_help:7189`), `set_ammo_elapsed(n)` (`lua_help:7287`). **Поддерживается.** |
| **1.6** | **Режим: запасная жизнь (`life`)** | Таймер в `update()`, проверка `actor.health < 0.15` -> `actor.health = 1` (`lua_help:7322`). **Поддерживается.** | Таймер в `update()`, проверка `actor.health < 0.15` -> `actor.health = 1` (`lua_help:5036`). **Поддерживается.** | Таймер в `update()`, проверка `actor.health < 0.15` -> `actor.health = 1` (`lua_help:7175`). **Поддерживается.** |
| **2** | **Амулет компаньона (`+вес, без голода`)** | Артефакт на поясе (`af_medusa`): `additional_inventory_weight = 500`, `satiety_restore_speed = 0.1` (`misc/artefacts.ltx`). **Поддерживается.** | Класс `CArtefact` в X-Ray 1.0 (ТЧ) игнорирует `additional_inventory_weight` (параметр читается только у костюмов `CCustomOutfit` в `misc/outfit.ltx:4`). **Не поддерживается для веса через артефакт.** *Ванильная замена:* `power_restore_speed = 0.05` (бесконечный бег даже при перегрузе), `satiety_restore_speed = 0.1` (`misc/artefacts.ltx:113` — голод отсутствует), `bleeding_restore_speed = 100.0`. | Артефакты в ЧН штатно поддерживают `additional_inventory_weight = 500` (`misc/artefacts.ltx:185, 296, 405`) и `satiety_restore_speed = 0.1` (`misc/artefacts.ltx:118`). **Поддерживается полностью.** |
| **3** | **Быстрые действия в быстрых слотах** | 4 HUD-слота быстрого доступа F1-F4 (`[quick_slot_0..3]`), движковый `CUIInventoryWnd` и коллбек `callback.use_object` (`bind_stalker.script:123`). **Поддерживается.** | 4 быстрых слота на худе отсутствуют в движке X-Ray 1.0. В ванильном `bind_stalker.script` не зарегистрирован `callback.use_object` для актора. **Не поддерживается как быстрые слоты HUD.** *Ванильная замена:* прямой запуск действий из меню компаньона (Esc + F1 -> вкладка «Персонаж»). | 4 быстрых слота на худе отсутствуют в движке X-Ray 1.5. В ванильном `bind_stalker.script` не зарегистрирован `callback.use_object` для актора. **Не поддерживается как быстрые слоты HUD.** *Ванильная замена:* прямой запуск действий из меню компаньона. |
| **4** | **Быстрый сейв / лоад (`quick_save`)** | `get_console()` (`lua_help:2104`), `CConsole:execute(string)` (`lua_help:739`). Команды `save` / `load`. **Поддерживается.** | `get_console()` (`lua_help:1472`), `CConsole:execute(string)` (`lua_help:744`). Команды `save` / `load`. **Поддерживается.** | `get_console()` (`lua_help:2108`), `CConsole:execute(string)` (`lua_help:890`). Команды `save` / `load`. **Поддерживается.** |
| **5** | **Отношения группировок (`relation_registry`)** | `relation_registry.community_goodwill` (`lua_help:11987`), `change_community_goodwill` (`lua_help:11984`). **Поддерживается.** | `relation_registry.community_goodwill` (`lua_help:8166`), `change_community_goodwill` (`lua_help:8167`), `set_community_goodwill` (`lua_help:8168`). **Поддерживается.** | `relation_registry.community_goodwill` (`lua_help:12206`), `change_community_goodwill` (`lua_help:12203`), `set_community_goodwill` (`lua_help:12205`). **Поддерживается.** |
| **6.1** | **Позвать NPC (`call_nearest`)** | `npc:set_npc_position(vector)` (`lua_help:8091`). **Поддерживается.** | В движках X-Ray 1.0 и 1.5 метода `set_npc_position` в классе `game_object` нет (добавлен только в X-Ray 1.6). **Не поддерживается движком напрямую.** *Ванильная замена:* перемещение через логику поведения (`set_desired_position` на строке 5173 / `xr_logic`), либо переспавн NPC в координатах актора. | В классе `game_object` метода `set_npc_position` нет. **Не поддерживается движком напрямую.** *Ванильная замена:* логика поведения (`set_desired_position` на строке 7768) или переспавн. |
| **6.2** | **Союзный отряд (`befriend_nearest_squad`)** | `get_object_squad(npc):squad_members()`, `obj:set_relation(game_object.friend, db.actor)` (`lua_help:7687`). **Поддерживается.** | В ТЧ нет симуляционных сквадов (`squad`); каждый сталкер существует индивидуально. Подружиться с сталкером: `npc:set_relation(game_object.friend, db.actor)` (`lua_help:5230`). **Поддерживается для отдельных NPC.** *Ванильная замена для «отряда»:* подружить всех живых сталкеров в радиусе 20 м. | В ЧН есть отряды симуляции: `npc:set_relation(game_object.friend, db.actor)` (`lua_help:7688`), `sim_board.get_sim_board()` и `squad:squad_members()`. **Поддерживается.** |
| **6.3** | **Убрать врагов (`kill_enemies`)** | `obj:kill(actor)` (`lua_help:7408`), `obj:relation(actor) == game_object.enemy` (`lua_help:7432`), `get_object_story_id(id) == nil`. **Поддерживается.** | `obj:kill(actor)` (`lua_help:5115`), `obj:relation(actor) == game_object.enemy` (`lua_help:5178`, `const enemy = 2` на строке 4993). Проверка сюжетных: `obj:story_id() == 4294967295` (`lua_help:5175`). **Поддерживается.** | `obj:kill(actor)` (`lua_help:7260`), `obj:relation(actor) == game_object.enemy` (`lua_help:7284`). Проверка сюжетных: `obj:story_id() == 4294967295` (`lua_help:7483`). **Поддерживается.** |
| **6.4** | **Убрать трупы (`remove_corpses`)** | `alife():release(alife():object(id), true)` (`lua_help:2069`). Проверка сюжетных. **Поддерживается.** | `alife():release(alife():object(id), true)` (`lua_help:1440`). Проверка сюжетных: `se_obj.m_story_id == 4294967295` (`lua_help:3805`). **Поддерживается.** | `alife():release(alife():object(id), true)` (`lua_help:2073`). Проверка сюжетных: `se_obj.m_story_id == 4294967295` (`lua_help:3336`). **Поддерживается.** |
| **7** | **Выброс: начать и остановить (`start_surge`, `stop_surge`)** | Скриптовый менеджер `surge_manager.get_surge_manager()`: запуск `start_surge()`, остановка `end_surge(true)`. **Поддерживается.** | В ванильном ТЧ выбросов как периодической или динамической механики нет (система вырезана из релиза, остался только скриптовый эффект погоды на ЧАЭС `level.set_weather_fx("surge_day")`, `x1.script:3`). **Не поддерживается.** | Скриптовая схема `xr_surge_hide.script`: запуск `xr_surge_hide.activate_surge(time)` (`xr_surge_hide.script:326`), остановка через сброс фаз `xr_surge_hide.phase = 0; xr_surge_hide.start_surge = false; level.stop_weather_fx()`. **Поддерживается скриптово.** |
| **8** | **Время суток (`level.change_game_time` / `set_hour`)** | `level.change_game_time(days, hours, minutes)` (`lua_help:2124`). **Поддерживается напрямую в движке.** | В `namespace level` ТЧ нет `change_game_time` (вызов появился только в ЗП). **Не поддерживается движковым вызовом.** *Ванильная замена:* перемотка времени через кратковременный разгон фактора времени `level.set_time_factor(10000)` (`lua_help:8154`) на апдейте до достижения часа `level.get_time_hours()` (`lua_help:8134`), затем возврат `level.set_time_factor(10)`. | В `namespace level` ЧН нет `change_game_time`. **Не поддерживается движковым вызовом.** *Ванильная замена:* ускорение времени через `level.set_time_factor(10000)` (`lua_help:12281`) до нужного часа. |
| **9.1** | **Метки тайников (`treasure_manager`)** | `treasure_manager.get_treasure_manager().secret_restrs`, `level.map_add_object_spot(id, "treasure", "")` (`lua_help:2133`). **Поддерживается.** | `treasure_manager.get_treasure_manager().treasure_info`: объект тайника `alife():story_object(v.target)`. Установка метки: `level.map_add_object_spot(obj.id, "treasure", hint)` (`lua_help:8108`). **Поддерживается.** | `treasure_manager.treasures`: объект тайника `alife():object(v.target)`. Установка метки: `level.map_add_object_spot(obj.id, "treasure", hint)` (`lua_help:12278`). **Поддерживается.** |
| **9.2** | **Метки артефактов на карте** | Поиск `alife():object(id)` при `parent_id == 65535` и секции из каталога артефактов. Метка `primary_object_spot` (`lua_help:2133`). **Поддерживается.** | Поиск `alife():object(id)` при `parent_id == 65535`. Метка `artefact_location` (`map_spots.xml:45`) или `green_location` через `level.map_add_object_spot(id, spot, "")` (`lua_help:8108`). **Поддерживается.** | Поиск `alife():object(id)` при `parent_id == 65535`. Метка `green_location` (`map_spots.xml:13`) через `level.map_add_object_spot(id, "green_location", "")` (`lua_help:12278`). **Поддерживается.** |

---

## 2. Подробный технический анализ по категориям

### 1. Режимы игрока (Toggles)

- **Бессмертие (`god`)**:
  - В ЗП: `db.actor.health = 1`, `db.actor.psy_health = 1`, `db.actor.radiation = -1` (`lua_help:7322, 7325, 7327`).
  - В ТЧ: `db.actor.health = 1`, `db.actor.psy_health = 1`, `db.actor.radiation = -1` (`lua_help:5036, 5039, 5040`). Полностью эквивалентно.
  - В ЧН: `db.actor.health = 1`, `db.actor.psy_health = 1`, `db.actor.radiation = -1` (`lua_help:7175, 7178, 7179`). Полностью эквивалентно.

- **Выносливость (`stamina`)**:
  - Экспортировано свойство `power` в классе `game_object`:
    - ТЧ: строка 5038 (`property power;`).
    - ЧН: строка 7177 (`property power;`).
    - ЗП: строка 7326 (`property power;`).

- **Кровотечение (`bleeding`)**:
  - В ЗП свойство `bleeding` экспортировано в luabind для записи: `property bleeding;` (`lua_help:7324`).
  - В ТЧ и ЧН свойство `bleeding` в классе `game_object` **не экспортировано** (есть только константный геттер `function get_bleeding() const;` — строка 5331 в ТЧ, строка 7450 в ЧН). Попытка выполнить `db.actor.bleeding = 1` приводит к падению luabind (`attempt to index a userdata value`).
  - *Безопасное решение:* вызов через `pcall(function() db.actor.bleeding = 1 end)`, а для гарантированного снятия кровотечения — использование ванильного бинта (`bandage`: `wounds_heal_perc = 1.0` в `items.ltx`) или артефакта на поясе.

- **Авторемонт (`repair`)**:
  - Проход по слотам `0..12` (`item_in_slot(slot)`):
    - ТЧ: `item_in_slot` (`lua_help:5108`), `set_condition` (`lua_help:5187`).
    - ЧН: `item_in_slot` (`lua_help:7254`), `set_condition` (`lua_help:7288`).
    - ЗП: `item_in_slot` (`lua_help:7401`), `set_condition` (`lua_help:7435`).
    Работает одинаково во всех трех играх.

- **Патроны (`ammo`)**:
  - Заполнение магазина активного оружия (`item:set_ammo_elapsed(size)`):
    - ТЧ: `active_item()` (`lua_help:5045`), `set_ammo_elapsed()` (`lua_help:5272`).
    - ЧН: `active_item()` (`lua_help:7189`), `set_ammo_elapsed()` (`lua_help:7287`).
    - ЗП: `active_item()` (`lua_help:7331`), `set_ammo_elapsed()` (`lua_help:7434`).
    Работает одинаково во всех трех играх.

- **Запасная жизнь (`life`)**:
  - Скриптовый триггер в цикле `update()`: при `db.actor.health < 0.15` восстанавливает здоровье до 1 и отключает галочку. Основан исключительно на `actor.health` (поддерживается во всех играх).

---

### 2. Амулет компаньона (`se_companion_amulet`)

- **ЧН (Clear Sky)**:
  В X-Ray 1.5 класс `CArtefact` обрабатывает параметры переносимого веса на поясе:
  `additional_inventory_weight = 500` и `additional_inventory_weight2 = 500` (как у артефактов `af_gravi`, `af_gold_fish` в `configs/misc/artefacts.ltx:296, 405`).
  Параметр сытости `satiety_restore_speed = 0.1` также штатно поддерживается (`misc/artefacts.ltx:118`).
  Поэтому в ЧН амулет компаньона работает точно так же, как в ЗП.

- **ТЧ (Shadow of Chernobyl)**:
  В X-Ray 1.0 класс `CArtefact` **не содержит** логики для `additional_inventory_weight` (в ТЧ этот параметр есть только у костюмов `CCustomOutfit` в `misc/outfit.ltx:4`).
  *Ванильная замена:*
  - `power_restore_speed = 0.05` дает бесконечный бег даже при критическом перегрузе (сталкер не устает и может передвигаться).
  - `satiety_restore_speed = 0.1` (`misc/artefacts.ltx:113`) полностью блокирует голод.
  - `bleeding_restore_speed = 100.0` моментально заживляет любые раны.
  - `radiation_restore_speed = -0.01` выводит радиацию.

---

### 3. Быстрые действия в быстрых слотах

- **ЗП**: HUD имеет 4 слота F1-F4, зарегистрированных в инвентаре, и коллбек `callback.use_object` (`bind_stalker.script:123`), что позволяет назначать предметы `se_act_*` на F1-F4.
- **ТЧ и ЧН**:
  В движках X-Ray 1.0 и 1.5 4 HUD-слота быстрого доступа отсутствуют.
  В ванильном `bind_stalker.script` ТЧ и ЧН отсутствует регистрация `callback.use_object` для актора (коллбек использовался только для физических объектов и диалогов).
  *Ванильная замена:*
  Все быстрые действия (`Вылечить`, `Починить`, `Метка`, `К метке`, `Быстрый сейв`, `Загрузить сейв`) вынесены прямыми кнопками в меню компаньона (Esc + F1 -> вкладка «Персонаж»).

---

### 4. Быстрый сейв и загрузка (`quick_save` / `quick_load`)

- Вызовы консоли `get_console():execute("save " .. name)` и `"load " .. name`:
  - ТЧ: `get_console()` (`lua_help:1472`), `CConsole:execute` (`lua_help:744`).
  - ЧН: `get_console()` (`lua_help:2108`), `CConsole:execute` (`lua_help:890`).
  - ЗП: `get_console()` (`lua_help:2104`), `CConsole:execute` (`lua_help:739`).
  Полная 100% совместимость.

---

### 5. Отношения группировок (`relation_registry`)

- Изменение и чтение репутации:
  - ТЧ:
    - `relation_registry.community_goodwill(community, actor_id)`: строка 8166
    - `relation_registry.change_community_goodwill(community, actor_id, delta)`: строка 8167
    - `relation_registry.set_community_goodwill(community, actor_id, val)`: строка 8168
  - ЧН:
    - `relation_registry.change_community_goodwill`: строка 12203
    - `relation_registry.set_community_goodwill`: строка 12205
    - `relation_registry.community_goodwill`: строка 12206
  - ЗП:
    - `relation_registry.change_community_goodwill`: строка 11984
    - `relation_registry.community_goodwill`: строка 11987
  Полная 100% идентичность во всех трех играх.

---

### 6. Сталкеры, отряды, враги и трупы

- **Позвать ближайшего NPC (`call_nearest`)**:
  - В ЗП: `npc:set_npc_position(vector)` (`lua_help:8091`).
  - В ТЧ и ЧН: В классе `game_object` метод `set_npc_position` отсутствует. Движок управляет положением сталкера через AI-физику и схемы поведения (`xr_logic`). Мгновенная телепортация живого NPC в онлайне **не поддерживается движком**.
  - *Безопасное решение:* проверка `if npc.set_npc_position ~= nil`, предотвращающая вызов nil-функции.

- **Союзный отряд (`befriend_nearest_squad`)**:
  - `npc:set_relation(game_object.friend, db.actor)`:
    - ТЧ: `set_relation` (`lua_help:5230`), `game_object.friend = 0` (`lua_help:4994`). В ТЧ нет понятия сквада, поэтому дружба устанавливается для ближайшего сталкера (и окружающих сталкеров в радиусе).
    - ЧН: `set_relation` (`lua_help:7688`), симуляционные отряды через `sim_board` и `squad:squad_members()`.
    - ЗП: `set_relation` (`lua_help:7687`), отряды через `get_object_squad(npc)`.

- **Убрать врагов вокруг (`kill_enemies`)**:
  - Метод убийства: `obj:kill(db.actor)`:
    - ТЧ: строка 5115
    - ЧН: строка 7260
    - ЗП: строка 7408
  - Отношение: `obj:relation(db.actor) == game_object.enemy`:
    - ТЧ: строка 5178 (`const enemy = 2`, строка 4993)
    - ЧН: строка 7284 (`const enemy = 2`, строка 7133)
    - ЗП: строка 7432 (`const enemy = 2`, строка 7280)
  - Проверка сюжетных персонажей (чтобы не сломать сюжет):
    - В ЗП: `get_object_story_id(obj:id()) == nil`.
    - В ТЧ и ЧН глобальной функции `get_object_story_id` нет. Проверка выполняется через движковый метод `obj:story_id() == 4294967295` (`lua_help:5175` в ТЧ, `lua_help:7483` в ЧН) или `se_obj.m_story_id == 4294967295`.

- **Убрать трупы вокруг (`remove_corpses`)**:
  - Удаление через серверный объект: `alife():release(alife():object(id), true)`.
    - ТЧ: `lua_help:1440`
    - ЧН: `lua_help:2073`
    - ЗП: `lua_help:2069`
    С обязательной проверкой, что труп не является сюжетным (`m_story_id`).

---

### 7. Выброс (`start_surge`, `stop_surge`)

- **ЗП**: Скриптовый класс `surge_manager.get_surge_manager()`: методы `start_surge()` и `end_surge(true)`.
- **ЧН**: Выбросами управляет скрипт `xr_surge_hide.script`:
  - Запуск: `xr_surge_hide.activate_surge(time)` (`xr_surge_hide.script:326`).
  - Остановка: сброс фаз `xr_surge_hide.phase = 0; xr_surge_hide.start_surge = false; xr_surge_hide.surge_activated = false; xr_surge_hide.surge_finished = true; level.stop_weather_fx()`.
- **ТЧ**: Механика выбросов **не поддерживается**. В ванильном ТЧ периодических выбросов нет (система была вырезана из игры; сохранились лишь погодные спецэффекты на локации ЧАЭС через `level.set_weather_fx("surge_day")`).

---

### 8. Время суток (`set_hour`)

- **ЗП**: `level.change_game_time(days, hours, minutes)` (`lua_help:2124`).
- **ТЧ и ЧН**: Функция `level.change_game_time` отсутствует в `namespace level`.
  *Ванильная замена:*
  Перемотка игрового времени осуществляется через кратковременное ускорение фактора времени:
  - ТЧ: `level.set_time_factor(10000)` (`lua_help:8154`), текущий час `level.get_time_hours()` (`lua_help:8134`).
  - ЧН: `level.set_time_factor(10000)` (`lua_help:12281`), текущий час `level.get_time_hours()` (`lua_help:12259`).
  При достижении целевого часа фактор времени сбрасывается обратно на стандартное значение (10).

---

### 9. Метки тайников и артефактов на карте

- **Метки тайников (`mark_stashes`)**:
  - Движковый вызов: `level.map_add_object_spot(id, "treasure", hint)`:
    - ТЧ: строка 8108
    - ЧН: строка 12278
    - ЗП: строка 2133
  - Структура менеджера тайников:
    - В ЗП: `treasure_manager.get_treasure_manager().secret_restrs`.
    - В ТЧ: `treasure_manager.get_treasure_manager().treasure_info`: цель тайника хранится в поле `v.target` (story_id), объект извлекается через `alife():story_object(v.target)`.
    - В ЧН: `treasure_manager.treasures`: поле `v.target`, объект извлекается через `alife():object(v.target)`.

- **Метки артефактов (`mark_artefacts`)**:
  - Перебор серверных объектов `alife():object(id)` при `se.parent_id == 65535` (свободно лежит в мире).
  - Секция проверяется по каталогу артефактов `save_editor_catalog.items.artifact`.
  - Тип метки на карте:
    - В ЗП: `"primary_object_spot"`.
    - В ТЧ: `"artefact_location"` (`configs/ui/map_spots.xml:45`) или `"green_location"`.
    - В ЧН: `"green_location"` (`configs/ui/map_spots.xml:13`).
  - Снятие меток: `level.map_remove_object_spot(id, spot)` (`lua_help:8139` в ТЧ, `lua_help:12266` в ЧН, `lua_help:2128` в ЗП).
