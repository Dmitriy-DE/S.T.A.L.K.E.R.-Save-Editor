# Конфигурация, динамические циклы и движковый API погоды в S.T.A.L.K.E.R.

## 1. Введение и архитектура погодной системы X-Ray

Погодная система в движке X-Ray управляет освещением, параметрами неба, облачностью, туманом, дальностью прорисовки, дождём, грозовыми разрядами и амбиентными звуками в зависимости от внутриигрового времени.

### 1.1. Движковые классы (C++)
- `CEnvironment` (`src/xrEngine/Environment.h`, `Environment.cpp`): синглтон окружения (`g_pGamePersistent->Environment()`), хранящий список всех погодных циклов `m_weathers`, текущий цикл, текущие интерполяторы и эффект погоды `m_pDescriptorFX`.
- `CEnvDescriptor`: описание состояния окружения на конкретный момент времени (небо, облака, цвета фога, солнца, интенсивность дождя `rain_density`, параметры грома `thunderbolt_collection`).
- `CEnvDescriptorMixer`: выполняет покадровую линейную интерполяцию параметров между двумя соседними часовыми секциями `CEnvDescriptor` с учётом текущего игрового времени (`level.get_time_hours()`, `level.get_time_minutes()`, `level.get_time_seconds()`).
- `CEnvAmbient`: менеджер фоновых спецэффектов (звуковые каналы `sound_channels`, летящие частицы, порывы ветра).

### 1.2. Экспорт функций движка в Lua (таблица `level`)
Движок экспортирует в скриптовое окружение глобальную таблицу `level`:

1. **`level.get_weather()`**
   - ТЧ: `gamedata_soc/scripts/lua_help.script:1477` (`function get_weather();`)
   - ЧН: `gamedata_cs/scripts/lua_help.script:2113` (`function get_weather();`)
   - ЗП: `gamedata/scripts/lua_help.script:2097` (`function get_weather();`)
   - Возвращает строковый идентификатор текущего погодного цикла/секции (например, `"default_clear"`, `"rain"`, `"indoor"`).

2. **`level.set_weather(string weather_name, boolean force_now)`**
   - ТЧ: `gamedata_soc/scripts/lua_help.script:1497` (`function set_weather(string, boolean);`)
   - ЧН: `gamedata_cs/scripts/lua_help.script:2145` (`function set_weather(string, boolean);`)
   - ЗП: `gamedata/scripts/lua_help.script:2129` (`function set_weather(string, boolean);`)
   - Переключает погодный цикл. Если `force_now = true`, сброс и применение параметров нового цикла происходят немедленно (без медленного смешивания интерполятора).

3. **`level.set_weather_fx(string fx_name)`**
   - ТЧ: `gamedata_soc/scripts/lua_help.script:1498` (`function set_weather_fx(string);`)
   - ЧН: `gamedata_cs/scripts/lua_help.script:2146` (`function set_weather_fx(string);`)
   - ЗП: `gamedata/scripts/lua_help.script:2130` (`function set_weather_fx(string);`)
   - Запускает поверх текущей погоды кратковременный погодный спецэффект (например, секцию `surge_day` при выбросе).

4. **`level.is_wfx_playing()`**
   - ЧН: `gamedata_cs/scripts/lua_help.script:2121` (`function is_wfx_playing();`)
   - ЗП: `gamedata/scripts/lua_help.script:2105` (`function is_wfx_playing();`)
   - Возвращает `true`, если в данный момент проигрывается погодный FX.

5. **`level.get_wfx_time()`**
   - ЧН: `gamedata_cs/scripts/lua_help.script:2114` (`function get_wfx_time();`)
   - ЗП: `gamedata/scripts/lua_help.script:2098` (`function get_wfx_time();`)
   - Возвращает текущее время (прогресс) проигрывания эффекта.

6. **`level.stop_weather_fx()`**
   - ЗП: `gamedata/scripts/lua_help.script:2133` (`function stop_weather_fx();`)
   - Принудительно останавливает текущий погодный эффект.

7. **`level.start_weather_fx_from_time(string fx_name, number time)`**
   - ЗП: `gamedata/scripts/lua_help.script:2131` (`function start_weather_fx_from_time(string, number);`)
   - Запуск эффекта с заданной временной точки (используется `WeatherManager:load()` при восстановлении сохранённой игры).

---

## 2. Тень Чернобыля (SoC / ТЧ)

### 2.1. Конфигурационные файлы
- Корневой файл: `configs/weathers/environment.ltx:1-36`.
  Содержит директивы `#include`:
  - `weather_default.ltx:1-626`
  - `weather_rain.ltx:1-674`
  - `weather_yantar.ltx:1-59`
  - `weather_radar.ltx:1-79`
  - `weather_prypyat.ltx:1-79`
  - `weather_stancia.ltx:1-119`
  - `weather_sarkofag.ltx:1-59`
  - `weather_indoor.ltx:1-119`
  - `weather_surge.ltx:1-125`
  - `weather_test.ltx:1-25`
  - `weather_map.ltx:1-25`

### 2.2. Секция `[weathers]`
Файл: `configs/weathers/environment.ltx:18-32`:
```ini
[weathers]
map             = sect_map
test            = sect_test
default         = sect_default_weather
indoor          = sect_indoor
indoor_x18      = sect_indoor_x18
pripyat         = sect_prypyat
radar           = sect_radar
rain            = sect_rain
sarkofag        = sect_sarkofag
stancia         = sect_stancia
stancia2        = sect_stancia2
yantar          = sect_yantar
yantar_indoor   = sect_yantar_indoor
```
Каждый ключ в `[weathers]` сопоставляется с суточной секцией (например, `[sect_default_weather]` в `weather_default.ltx`), в которой расписаны почасовые метки:
```ini
[sect_default_weather]
01:00:00 = default_weather_01
02:00:00 = default_weather_02
...
00:00:00 = default_weather_00
```

### 2.3. Погодные спецэффекты `[weather_effects]`
Файл: `configs/weathers/environment.ltx:34-36`:
```ini
[weather_effects]
surge_day   = sect_surge_day
p_surge_day = sect_p_surge_day
```

### 2.4. Привязка погоды к уровням
Файл: `configs/game_maps_single.ltx`:
- Кордон (`[l01_escape]`, строка 37): `weathers = default`
- Свалка (`[L02_garbage]`, строка 45): `weathers = default`
- Агропром (`[l03_agroprom]`, строка 53): `weathers = default`
- Подземелья Агропрома (`[l03u_agr_underground]`, строка 62): `weathers = indoor`
- Тёмная Долина (`[l04_darkvalley]`, строка 70): `weathers = pripyat`
- Лаборатория X-18 (`[l04u_LabX18]`, строка 78): `weathers = indoor_x18`
- Бар (`[L05_bar]`, строка 86): `weathers = default`
- Росток / Дикая территория (`[L06_rostok]`, строка 94): `weathers = default`
- Армейские склады (`[l07_military]`, строка 102): `weathers = default`
- Янтарь (`[l08_yantar]`, строка 110): `weathers = yantar`
- Лаборатория X-16 (`[l08u_brainlab]`, строка 118): `weathers = indoor`
- Радар (`[l10_radar]`, строка 126): `weathers = radar`
- Бункер Радара (`[l10u_bunker]`, строка 132): `weathers = indoor`
- Припять (`[l11_pripyat]`, строка 140): `weathers = pripyat`
- ЧАЭС-1 (`[l12_stancia]`, строка 148): `weathers = stancia`
- Саркофаг (`[l12u_sarcofag]`, строка 154): `weathers = sarkofag`
- Управление Монолитом (`[l12u_control_monolith]`, строка 160): `weathers = indoor`
- ЧАЭС-2 (`[l12_stancia_2]`, строка 168): `weathers = stancia2`

### 2.5. Скриптовая логика ТЧ (`level_weathers.script:1-63`)
- `WeatherManager:reset()` (строки 9-30) считывает имя погоды уровня из `game.ltx` (секции уровней берутся из `game_maps_single.ltx`).
- `WeatherManager:update()` (строки 50-63) проверяет смену игровых суток:
  ```lua
  if self.weather_change_day ~= level.get_time_days() then
      self:select_weather(false)
  end
  ```
- `WeatherManager:select_weather(now)` (строки 32-38) вызывает:
  ```lua
  level.set_weather(weather, now)
  ```
В ванильном ТЧ нет системы динамической погоды (вероятностных графов переходов). Погода на уровне фиксирована конфигурацией `game_maps_single.ltx` и меняется только скриптами или сюжетом.

---

## 3. Чистое Небо (CS / ЧН)

### 3.1. Конфигурационные файлы
- Корневой файл параметров: `configs/environment/environment.ltx:1-11`.
- Каталог погодных файлов: `configs/environment/weathers/`:
  - `default_clear.ltx` (ясно)
  - `default_cloudy.ltx` (облачно)
  - `default_rain.ltx` (дождь)
  - `default_thunder.ltx` (гроза)
  - `night.ltx` (ночь, используется в вступительной сцене Болот)
  - `indoor.ltx` (подземелье)
  - `stancia2.ltx` (штормовой финал ЧАЭС-2)
  - `sun_shafts.ltx` (солнечные лучи)
  - `[default].ltx` (базовый цикл)
  - `map.ltx` (погода карты PDA)
  - `old_version_weather.ltx`

Структура каждого `.ltx` файла в `configs/environment/weathers/`:
В отличие от ТЧ, секции названы временами суток `[00:00:00]`, `[01:00:00]`, ..., `[23:00:00]`. Движок загружает файл напрямую по имени: при вызове `level.set_weather("default_clear", true)` движок X-Ray 1.5 открывает `configs/environment/weathers/default_clear.ltx`.

### 3.2. Динамический погодный граф
Файл: `configs/environment/dynamic_weather_graphs.ltx:1-9`:
```ini
[dynamic_default]
clear   = 0.7
cloudy  = 0.2
rain    = 0.05
thunder = 0.05
```
Определяет вероятности переходов между четырьмя состояниями:
1. `clear` (ясно, вес 0.7)
2. `cloudy` (облачно, вес 0.2)
3. `rain` (дождь, вес 0.05)
4. `thunder` (гроза, вес 0.05)

### 3.3. Привязка погоды к уровням ЧН
Файл: `configs/game_maps_single.ltx`:
- Болота (`[marsh]`, строка 67):
  ```ini
  weathers = {-mar_intro_scene_1_end} night, {+mar_intro_scene_1_end -mar_tutorial_return_to_base_reversed} default_clear, dynamic_default
  ```
- Все открытые уровни (Кордон, Свалка, Тёмная Долина, Агропром, Янтарь, Армейские склады, Рыжий лес, Лиманск, Госпиталь): `weathers = dynamic_default` (строки 10, 19, 28, 33, 44, 62, 72, 77, 87).
- Катакомбы Агропрома: `weathers = indoor` (строка 14).
- ЧАЭС: `weathers = stancia2` (строка 82).

### 3.4. Скриптовая динамическая погода ЧН (`level_weathers.script:1-241`)
- Класс `WeatherManager:__init()` (строки 5-18) загружает `dynamic_weather_graphs.ltx`.
- `WeatherManager:update()` (строки 47-64) вызывается в цикле обновления актора. Проверяет смену игрового часа:
  ```lua
  if self.last_hour ~= level.get_time_hours() then
      self.last_hour = level.get_time_hours()
      for lvl, st in pairs(self.state) do
          st.current_state = st.next_state
          st.next_state = get_next_state(st.graph, st.current_state)
      end
      self:select_weather(false)
  end
  ```
- `WeatherManager:select_weather(now)` (строки 66-100):
  - Для статической погоды (например, `indoor`, `stancia2`):
    `weather_section_name = weather` (строка 74).
  - Для динамической погоды (`dynamic_default`):
    `weather_section_name = "default_" .. st.current_state` (строка 86).
    В результате формируются секции: `default_clear`, `default_cloudy`, `default_rain`, `default_thunder`.
  - Устанавливает погоду в движке:
    `level.set_weather(weather_section_name, now)` (строка 96).
- Глобальная функция синглтона: `level_weathers.get_weather_manager()` (строки 235-240).

---

## 4. Зов Припяти (CoP / ЗП)

### 4.1. Конфигурационные файлы
- Каталог: `configs/environment/weathers/`:
  - `default_clear.ltx`
  - `default_cloudy.ltx`
  - `default_rain.ltx`
  - `default_thunder.ltx`
  - `night.ltx`
  - `indoor.ltx`
  - `indoor_ambient.ltx` (специальный тёмный эмбиент для Путепровода «Припять-1»)
  - `stancia2.ltx`
  - `sun_shafts.ltx`
  - `[default].ltx`
  - `map.ltx`

### 4.2. Динамический погодный граф CoP
Файл: `configs/environment/dynamic_weather_graphs.ltx:1-9`:
```ini
[dynamic_default]
clear   = 0.4
cloudy  = 0.4
rain    = 0.1
thunder = 0.1
```
В ЗП соотношение вероятностей более пасмурное, чем в ЧН:
- `clear`: 0.4 (в ЧН 0.7)
- `cloudy`: 0.4 (в ЧН 0.2)
- `rain`: 0.1 (в ЧН 0.05)
- `thunder`: 0.1 (в ЧН 0.05)

### 4.3. Привязка к уровням CoP
Файл: `configs/game_maps_single.ltx`:
- Затон (`[zat_b38]`, строка 50): `weathers = dynamic_default`
- Окрестности Юпитера (`[pri_a16]`, строка 55): `weathers = dynamic_default`
- Путепровод Припять-1 (`[jupiter_underground]`, строка 60): `weathers = indoor_ambient`
- Припять (`[pripyat]`, строка 65): `weathers = dynamic_default`
- Лаборатория X-8 (`[labx8]`, строка 70): `weathers = indoor`

### 4.4. Скриптовая реализация (`level_weathers.script:1-241`)
Полностью совпадает по архитектуре со скриптом ЧН:
- Ежечасно переключает состояния `clear`, `cloudy`, `rain`, `thunder`.
- Вызывает `level.set_weather("default_" .. st.current_state, now)`.
- Синглтон: `level_weathers.get_weather_manager()`.

---

## 5. Сводная таблица погодных циклов по играм

| Идентификатор для `level.set_weather` | SoC (ТЧ) | CS (ЧН) | CoP (ЗП) | Описание |
|---|---|---|---|---|
| `default` | Да (`sect_default_weather`) | Нет | Нет | Базовый ясный цикл ТЧ |
| `default_clear` | Нет | Да | Да | Ясная безоблачная погода |
| `default_cloudy` | Нет | Да | Да | Пасмурная погода с рассеянным светом |
| `default_rain` | Нет | Да | Да | Затяжной дождь |
| `default_thunder` | Нет | Да | Да | Гроза с штормовым ветром и молниями |
| `rain` | Да (`sect_rain`) | Нет | Нет | Дождливая погода ТЧ |
| `night` | Нет | Да | Да | Тёмная глухая ночь |
| `yantar` | Да (`sect_yantar`) | Нет | Нет | Желтовато-зелёная мгла над болотом Янтаря |
| `radar` | Да (`sect_radar`) | Нет | Нет | Тяжёлый туман и серость «Выжигателя» |
| `pripyat` | Да (`sect_prypyat`) | Нет | Нет | Холодное серое небо Припяти (ТЧ) |
| `stancia` | Да (`sect_stancia`) | Нет | Нет | Предгрозовое небо ЧАЭС-1 |
| `stancia2` | Да (`sect_stancia2`) | Да | Да | Бушующий штормовой выброс на ЧАЭС-2 |
| `sarkofag` | Да (`sect_sarkofag`) | Нет | Нет | Освещение внутри разрушенного 4-го блока |
| `indoor` | Да (`sect_indoor`) | Да | Да | Освещение подземных катакомб и лабораторий |
| `indoor_x18` | Да (`sect_indoor_x18`) | Нет | Нет | Специфический свет лаборатории X-18 |
| `indoor_ambient` | Нет | Нет | Да | Подземный эмбиент Путепровода Припять-1 |
| `sun_shafts` | Нет | Да | Да | Выраженные лучи сквозь разрывы облаков |
| `[default]` | Нет | Да | Да | Базовый дефолтный профиль ЧН/ЗП |

---

## 6. Реализация в моде-компаньоне

### 6.1. Протокол (`docs/MOD_COMPANION_PROTOCOL.md`)
Добавлена команда:
```
command: v1 <id> weather [<section>] [now]
reply:   v1 <id> ok weather=<name>
```
Если аргумент не указан, возвращается имя текущей погоды (`level.get_weather()`).
Если аргумент указан, вызывается `level.set_weather(section, now)`.

### 6.2. Обработчик в `save_editor_companion.script`
Функция `handlers.weather(args)`:
1. Валидирует доступность функции движка `level.set_weather`.
2. Читает флаг немедленного применения `now` (по умолчанию `true`).
3. Вызывает `level.set_weather(weather, now)`.
4. В ЧН и ЗП при установке погоды из динамического графа (`clear`, `cloudy`, `rain`, `thunder` или префиксов `default_*`) обновляет текущее состояние `st.current_state` и `st.next_state` в `level_weathers.get_weather_manager().state`. Благодаря этому динамический погодный менеджер не сбрасывает выбранную погоду в следующее наступление игрового часа, а плавно продолжает цикл от выбранного состояния.

### 6.3. Интерфейс (UI) в companion
Вкладка «Мир» (`world`) в `save_editor_companion_ui.script`:
- Показывает список доступных погодных циклов из соответствующего для игры `save_editor_weather.script`.
- Карточка справа отображает: название погоды, имя секции движка, описание и текущую погоду.
- Кнопка **«Погода»** (`btn_a`) или **двойной щелчок** по строке списка мгновенно активирует выбранную погоду.
- Кнопка **«Выброс»** (`btn_b`) запускает сценарий выброса (`surge_manager.start_surge()`).
- Степпер **`-`** и **`+`** переключает ускорение времени (`x1`, `x2`, `x5`, `x10`, `x20`, `x50`, `x100`).
