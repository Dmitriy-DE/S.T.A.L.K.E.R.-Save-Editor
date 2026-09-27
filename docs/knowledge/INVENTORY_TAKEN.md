# Сюжетные сценарии изъятия инвентаря и денег в S.T.A.L.K.E.R.

## 1. Введение

В серии игр S.T.A.L.K.E.R. есть сюжетные и геймплейные эпизоды, в которых у игрока принудительно изымается инвентарь (полностью или частично) либо списываются все наличные деньги.

Для редактора сохранений такие моменты представляют особую опасность: если пользователь откроет сейв, сделанный в промежуточном состоянии (вещи уже изъяты, но ещё не возвращены), наивное редактирование инвентаря или баланса денег приведёт к дублированию предметов, безвозвратной утере модификаций, поломке скриптовой логики квеста или повторному списанию ресурсов движком.

В данном документе детально разобрана механика каждого такого момента во всех трёх играх трилогии (ТЧ, ЧН, ЗП), приведены точные файлы, секции логики, инфопорции, идентификаторы тайников, а также сформулированы правила и рекомендации для валидатора редактора.

---

## 2. Чистое Небо (CS / ЧН)

### 2.1. Ограбление в подвале Барахолки (Свалка)

Самый известный и радикальный эпизод отъёма ресурсов в трилогии. Шрам спускается в подвал за КПК Клыка, наступает на растяжку, теряет сознание, а бандиты забирают все его вещи и деньги.

#### Конфигурация и логика рестриктора
- Файл логики подвала: `configs/scripts/garbage/gar_space_restrictor_fang_pda_cellar.ltx`
- Рестриктор входа/двери: `configs/scripts/garbage/gar_space_restrictor_ambush_door.ltx`
- Сценарий ограбления:
  1. Игрок заходит в подвал при активной задаче доставки КПК (`+val_deliver_pda_complete`), выдаётся инфопорция `gar_story_came_to_ambush_door` (`gar_space_restrictor_ambush_door.ltx:5`).
  2. В рестрикторе подвала активируется секция `sr_idle@ko` (`gar_space_restrictor_fang_pda_cellar.ltx:4-6`):
     ```ini
     [sr_idle@ko]
     on_actor_inside = {+gar_story_came_to_ambush_door} sr_idle@unconscious_start_time_1 %=disable_ui =run_postprocess(gar_ambush_hit)%
     ```
  3. Блокируется UI, проигрывается камера взрыва `scenario_cam\garbage\cam_garbage_basement`, спавнятся бандиты-грабители (`+gar_story_spawn_ambush_bandits`).
  4. После диалога грабителей (`gar_story_ambush_talk_4_played`) срабатывает ключевая секция `sr_idle@unconscious_talk_4_time` (строка 14-16):
     ```ini
     [sr_idle@unconscious_talk_4_time]
     on_timer = 3000 | sr_idle@unconscious_stop_1 %=take_money(all) =relocate_actor_inventory_to_box(727) +gar_story_ambush_remove =destroy_object(700) =teleport_actor(gar_bandit_ambush_teleport_walk:gar_bandit_ambush_teleport_look)%
     ```

#### Скриптовые функции отъёма
1. **Перемещение вещей в ящик**:
   - Функция: `xr_effects.script:1678-1686`:
     ```lua
     function relocate_actor_inventory_to_box(actor, npc, p)
         local function transfer_object_item(item)
             if item:section() ~= "wpn_binoc" and item:section() ~= "wpn_knife" and item:section() ~= "device_torch" then
                 db.actor:transfer_item(item, inv_box_1)
             end
         end
         inv_box_1 = level_object_by_sid (p[1])
         actor:inventory_for_each(transfer_object_item)
     end
     ```
   - Аргумент `p[1] = 727`.
   - Исключения (НЕ изымаются): бинокль (`wpn_binoc`), нож (`wpn_knife`), налобный фонарь (`device_torch`).
   - Все остальные предметы перемещаются в объект с Story ID `727`.
2. **Списание денег**:
   - Функция: `xr_effects.script:1827-1836`:
     ```lua
     function take_money(actor, npc, p)
         local num = p[1]
         if num == "all" or db.actor:money() < num then
             num = db.actor:money()
         end
         db.actor:give_money(-num)
         game_stats.money_quest_update(-num)
         xr_statistic.inc_spent_money_counter(num)
         news_manager.relocate_money(db.actor, "out", num)
     end
     ```
   - `num = "all"` списывает **100% денег** актора (`db.actor:give_money(-num)`).
   - **Важнейший факт**: Деньги **НЕ** помещаются ни в какой ящик и нигде в игре не сохраняются! Они просто уничтожаются вызовом `give_money(-num)`.

#### Тайник и квест возврата вещей
- Story ID тайника: `727 = "gar_redemption_box"` (`configs/game_story_ids.ltx:187`).
- Имя игрового объекта ящика: `gar_smart_terrain_5_6_box` (лагерь бандитов на Свалке).
- Квест: `[gar_quest_redemption]` («Вернуть свои вещи», `configs/misc/tm_garbadge.ltx:172-184`):
  ```ini
  [gar_quest_redemption]
  type = storyline
  task_type = additional
  target_cond = true
  name = gar_quest_redemption_name
  text = gar_quest_redemption_text
  condlist_0 = {+gar_quest_redemption_done} complete
  target_story_ids = 727
  ```
- Старт квеста: выдаётся инфопорция `gar_quest_redemption_started` в `gar_space_restrictor_ambush_door.ltx:11` при выходе из подвала.
- Завершение квеста: в `scripts/bind_physic_object.script:186-188`:
  ```lua
  if obj:clsid() == clsid.inventory_box then
      local box_name = obj:name()
      if box_name == "gar_smart_terrain_5_6_box" and db.actor:has_info("gar_quest_redemption_started") then
          db.actor:give_info_portion("gar_quest_redemption_done")
      end
      treasure_manager.use_box(obj, who)
  end
  ```
- При открытии ящика `gar_smart_terrain_5_6_box` игрок забирает свои предметы обратно, и квест закрывается.
- **Деньги в ванильной игре вернуть невозможно**: грабители не имеют при себе всей суммы, ящик деньги не выдаёт.

---

### 2.2. Блокпосты бандитов на входах на Свалку (ЧН)
- Файлы логики: `configs/scripts/garbage/gar_robbery_bandit_blockpost_*.ltx`
- Скрипт: `scripts/sr_robbery.script:525-544`
- Механика: бандиты требуют сложить оружие и заплатить дань за проход.
  - Функция `actor_give_money(first_speaker, second_speaker)`:
    ```lua
    db.actor:give_money(-money)
    news_manager.relocate_money(db.actor, "out", money)
    ```
  - Вещи бандиты не забирают (закомментировано в `sr_robbery.script:544`), списываются только деньги. Деньги не сохраняются в тайниках.

---

## 3. Тень Чернобыля (SoC / ТЧ)

### 3.1. Арена (Арни, Бар «100 Рентген»)

Перед каждым поединком на Арене у Меченого полностью изымается всё снаряжение и выдаётся строго фиксированный боекомплект на бой.

#### Конфигурация и логика рестрикторов
- Рестриктор шлюза/входа на Арену: `configs/scripts/bar_arena_sr.ltx:1-35`
- Триггер боя: `configs/scripts/bar_arena_combat_triger.ltx:1-85`
- Диалоги с Арни: `configs/gameplay/dialogs_bar.xml:1902-1944, 3630-3776`
- Скрипт выдачи наград: `scripts/bar_dialogs.script:230-255`

#### Скриптовый отъём инвентаря
Файл: `scripts/xr_effects.script:984-1065`:
```lua
local function transfer_object_item(item)
    out_object:transfer_item(item, in_object)
end

function bar_arena_teleport ( actor, npc)
    inv_box_1 = level_object_by_sid (573)

    out_object = actor
    in_object  = inv_box_1
    actor:inventory_for_each(transfer_object_item) 

    local spawn_items = {}	
    if has_alife_info("bar_arena_fight_1") then 
        table.insert(spawn_items, "wpn_pm")
        table.insert(spawn_items, "ammo_9x18_pmm")
        ...
    -- спавн выданного оружия и патронов в инвентарь актора
    for k,v in pairs(spawn_items) do
        alife():create(v, db.actor:position(), db.actor:level_vertex_id(), db.actor:game_vertex_id(), db.actor:id())
    end
end
```
- Идентификатор ящика: Story ID `573 = "bar_arena_inventory_box"` (`configs/game_story_ids.ltx:179`).
  (Для спецбоёв также используется Story ID `574 = "bar_arena_inventory_box_2"` через `bar_arena_teleport_2`, строка 1072).
- В отличие от ЧН, передаются **абсолютно все предметы**, включая бинокль, нож и болты/фонарик (в ТЧ фонарик не инвентарный).
- Актор телепортируется на арену в секцию `sr_cutscene@fight_*` (`bar_arena_combat_triger.ltx:23-77`).

#### Завершение боя и зачистка Арены
1. При смерти противников взводится флаг `bar_arena_fight_*_done` и открываются двери.
2. Игрок подходит к Арни и завершает диалог: вызывается `bar_dialogs.arena_give_reward(actor, npc)` (`scripts/bar_dialogs.script:250-251`):
   ```lua
   db.actor:give_info_portion("bar_arena_reset")
   xr_zones.purge_arena_items("bar_arena")
   ```
3. Функция `xr_zones.purge_arena_items("bar_arena")` (`scripts/xr_zones.script:96-100`):
   Зона арены `arena_zone_binder` хранит все объекты внутри периметра:
   ```lua
   for k, v in pairs(self.saved_obj) do
       local obj = alife():object(k)
       if obj then
           alife:release(obj, true)
       end
   end
   ```
   **Внимание!** Все предметы, брошенные игроком на землю Арены, оружие убитых врагов и неиспользованные выданные патроны **принудительно уничтожаются** через `alife:release`!
4. Игрок подходит к металлическому синему ящику (`bar_arena_inventory_box`, SID 573) в предбаннике у Арни и **вручную забирает свои вещи**. Скрипт их автоматически обратно в карманы не перекладывает.

---

### 3.2. Вырезанный контент: КПЗ наёмников в Мёртвом Городе (ТЧ)
- Файл зоны: `configs/scripts/cit/cit_jail_scene_zone.ltx:2`
- Функция: `xr_effects.script:125-131`:
  ```lua
  function drop_actor_inventory(actor, npc, p)
      if p[1] then
          drop_point  = patrol(p[1]):point(0)
          drop_object = actor
          actor:inventory_for_each(drop_object_item)
      end
  end
  ```
- В оригинальном релизе 1.0006 локация `cit` (Dead City) отключена в `game_levels.ltx`, но логика присутствует в скриптах. Все вещи вываливались на землю в точке патрульного пути.

---

## 4. Зов Припяти (CoP / ЗП)

### 4.1. Кража личного ящика Корягой на станции «Янов»
В Зове Припяти сценариев отъёма непосредственно носимого инвентаря игрока нет (функция `relocate_actor_inventory_to_box` в `gamedata/scripts/xr_effects.script:1892-1904` закомментирована разработчиками GSC).

Однако реализована сюжетная кража содержимого **личного синего ящика игрока**:
- Исходный ящик: `jup_b202_actor_treasure` (личный ящик Дегтярёва на «Янове»).
- Вор: сталкер Коряга (`jup_b202_stalker_snag`).
- Целевой ящик (тайник Коряги): `jup_b202_snag_treasure` (люк под полустанком возле «Янова»).
- Скрипт перемещения: `scripts/xr_effects.script:2368-2379`:
  ```lua
  function jup_b202_inventory_box_relocate(actor, npc)
      local inv_box_out = get_story_object("jup_b202_actor_treasure")
      local inv_box_in = get_story_object("jup_b202_snag_treasure")
      local items_to_relocate = {}
      local function relocate(inv_box_out, item)
          table.insert(items_to_relocate, item)
      end
      inv_box_out:iterate_inventory_box(relocate, inv_box_out)
      for k,v in pairs(items_to_relocate) do
          inv_box_out:transfer_item(v, inv_box_in)
      end
  end
  ```
- Состояние отслеживается инфопорциями:
  - `jup_b52_actor_items_can_be_stolen`: кража возможна;
  - `jup_b202_actor_items_returned`: игрок нашёл тайник под полустанком и забрал имущество.
  - Проверка в скриптах: `xr_conditions.script:1568-1571` (`jup_b202_actor_treasure_not_in_steal`).

---

## 5. Опасности для редактора сохранений (RCA & Bug Scenarios)

### Сценарий 1: Редактирование инвентаря во время ограбления в ЧН
**Состояние сейва**: игрок сохранился после подвала на Барахолке, квест `gar_quest_redemption` активен (`has_info("gar_quest_redemption_started") and not has_info("gar_quest_redemption_done")`).
1. **Что видит редактор в сейве**: в `cse_alife_creature_actor` практически пустой инвентарь (только нож, бинокль и фонарь).
2. **Что сделает пользователь**: подумает, что вещи «пропали из-за бага» или решит нагенерировать себе броню, оружие и патроны взамен отобранных.
3. **Последствия в игре**:
   - Когда игрок доберётся до тайника `gar_smart_terrain_5_6_box` (SID 727) и откроет его, там будут лежать **все его старые вещи**. Произойдёт непреднамеренное дублирование уникальных предметов (например, артефактов, прокачанных стволов).
   - Если пользователь изменил состояние брони/оружия, он отредактировал временные дубликаты, а оригинальные прокачанные предметы остались в ящике с прежним состоянием.

### Сценарий 2: Редактирование денег перед подвалом в ЧН
**Состояние сейва**: сохранение в подвале прямо перед срабатыванием триггера `gar_ambush_hit`.
1. **Действие пользователя**: пользователь прописывает 1 000 000 RU в редакторе.
2. **Последствия в игре**: через 3 секунды после загрузки скрипт выполняет `take_money(all)`. Все введённые деньги сгорают без следа.

### Сценарий 3: Редактирование инвентаря во время боя на Арене в ТЧ
**Состояние сейва**: сейв сделан прямо во время раунда на Арене (`has_info("bar_arena_fight") and not has_info("bar_arena_reset")`).
1. **Что в сейве**: реальный инвентарь игрока лежит в ящике SID `573` (`bar_arena_inventory_box`). На игроке надета выданная экипировка боя.
2. **Опасность 1**: Если игрок добавит в инвентарь ценные предметы, а во время боя уронит их на пол Арены — при завершении боя вызов `xr_zones.purge_arena_items("bar_arena")` удалит их навсегда через `alife:release`!
3. **Опасность 2**: Если игрок добавит себе топовую броню или оружие, то после победы он заберёт из синего ящика своё старое оружие, получив дубликаты и сломав баланс прогрессии.

### Сценарий 4: Редактирование личного ящика на Янове в ЗП
**Состояние сейва**: активна стадия квеста Коряги (`jup_b52_actor_items_can_be_stolen` выдана, `jup_b202_actor_items_returned` отсутствует).
1. Ящик `jup_b202_actor_treasure` уже пуст, все вещи лежат в `jup_b202_snag_treasure`.
2. Если редактор сейвов предлагает редактирование персонального ящика базы — правка ящика на Янове запишет предметы в пустой ящик, а в тайнике Коряги останется копия.

---

## 6. Рекомендации для валидатора и UI редактора

### 6.1. Детектирование опасных состояний
Редактор сейвов (через модуль `SaveValidator` или аналогичный аналитик инфопорций) должен проверять следующие комбинации:

| Игра | Опасное состояние | Условие проверки инфопорций / локации |
|---|---|---|
| **CS (ЧН)** | Ограбление на Свалке активно | `+gar_quest_redemption_started` И `-gar_quest_redemption_done` |
| **SoC (ТЧ)** | Игрок находится на Арене | `+bar_arena_fight` И `-bar_arena_reset` (или актор на `L05_bar` внутри шейпа `bar_arena_sr`) |
| **CoP (ЗП)** | Личный ящик похищен Корягой | `+jup_b52_actor_items_can_be_stolen` И `-jup_b202_actor_items_returned` |

### 6.2. Реакция интерфейса (UI)
1. **При обнаружении ограбления на Свалке (ЧН)**:
   - Отображать предупреждающий баннер:
     > ⚠️ **Внимание: Сюжетное ограбление (подвал Барахолки)**
     > Ваши вещи временно перемещены бандитами в тайник на Свалке (ящик SID 727, лагерь бандитов).
     > В инвентаре персонажа находятся только базовые предметы (нож, бинокль, фонарь).
     > Добавление предметов в инвентарь приведёт к дубликатам после возврата вещей по квесту.
   - Опционально: предоставить прямую возможность редактировать содержимое тайника SID 727 (`gar_redemption_box`).

2. **При обнаружении боя на Арене (ТЧ)**:
   - Отображать предупреждающий баннер:
     > ⚠️ **Внимание: Идёт бой на Арене**
     > Основное снаряжение Меченого сложено в ящик у Арни (SID 573). В инвентаре находится временное оружие, выданное на время поединка.
     > Любые предметы, выброшенные на арене, будут безвозвратно уничтожены скриптом очистки после боя.

3. **При обнаружении кражи Коряги (ЗП)**:
   - При открытии вкладки тайников станции «Янов» пояснять, что вещи сейчас находятся в тайнике под полустанком (`jup_b202_snag_treasure`), а не в синем ящике на вокзале.
