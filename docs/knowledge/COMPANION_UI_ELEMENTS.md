# Game UI elements used by the companion menu

## Каталог родных элементов интерфейса: Тень Чернобыля (SHoC / ТЧ)

### 1. Архитектура UI в Тени Чернобыля

В S.T.A.L.K.E.R.: Shadow of Chernobyl (движок X-Ray 1.0 / OGSR) графический интерфейс описывается через XML-файлы конфигураций и связывается с Lua через класс `CScriptXmlInit` (`gamedata_soc/scripts/lua_help.script:5644`).

Ключевые особенности и отличия от ЧН и ЗП:
1. **Файлы описания текстур**: В ТЧ большинство описаний текстур расположено непосредственно в корневой папке `configs/ui/` в файлах `ui_common.xml`, `ui_old_textures.xml`, `ui_iconstotal.xml` и др. под тегом `<ui_texture>` (в ЧН и ЗП они перенесены в поддиректорию `textures_descr/`).
2. **Списки**: Для списков используется класс `CUIListWnd` (`lua_help.script:6477`) и метод `xml:InitList(path, parent)` (`lua_help.script:5650`). Современный класс `CUIListBox` из ЗП в ТЧ **отсутствует** (0 совпадений в `lua_help.script`).
3. **Шкалы прогресса**: Метод `InitProgressBar` в `CScriptXmlInit` для Lua в ванильном ТЧ **отсутствует** (`lua_help.script:5644-5670`). Хотя класс `CUIProgressBar` существует в C++ движка (`xrServerEntities` / `xrUICore`) и представлен в `lua_help.script:6901`, скрипты ТЧ не могли вызывать `xml:InitProgressBar(...)`. Шкалы инициализировались либо внутренним кодом C++ (инвентарь, экран обыска), либо собирались скриптами вручную через `CUIStatic`.
4. **Управление окнами**: Открытие и закрытие окон диалогов осуществляется через `self:GetHolder():start_stop_menu(self, true)` (`lua_help.script:5701`), в отличие от ЗП, где используются `ShowDialog` / `HideDialog`.
5. **Кнопки**: В ТЧ поддерживаются как простые кнопки `InitButton` (`lua_help.script:5668`), так и кнопки с тремя состояниями `Init3tButton` (`lua_help.script:5664`).

---

### 2. Стандартные элементы управления и их XML-структура

#### 2.1. Кнопка с тремя состояниями (`Init3tButton`)
- **Класс C++**: `CUI3tButton` (`lua_help.script:6276`).
- **Метод инициализации**: `xml:Init3tButton(path, parent)` (`lua_help.script:5664`).
- **Принцип работы состояний**:
  Движок (`OpenXRay: src/xrUICore/XML/UIXmlInitBase.cpp:992-1060`) считывает тег `<texture>texture_id</texture>`. Если суффикс не указан, движок автоматически ищет текстуры с четырьмя суффиксами состояний:
  - `_e` — **Enabled** (обычное состояние, доступна для нажатия)
  - `_h` — **Highlighted** (курсор наведён на кнопку)
  - `_t` — **Touched / Pressed** (кнопка зажата)
  - `_d` — **Disabled** (кнопка отключена / неактивна)
  Альтернативно можно явно указать раздельные теги `<texture_e>`, `<texture_h>`, `<texture_t>`, `<texture_d>`.
  Если у кнопки установлен атрибут `frame_mode="1"`, кнопка масштабируется как frameline-элемент (3-секционная растяжка).
- **Цвета текста по состояниям**: Внутри тега `<text_color>` можно задавать цвета под каждый статус: `<e r="216" g="186" b="140"/>`, `<d .../>`, `<t .../>`, `<h .../>`.
- **Звуки**: Теги `<sound_h>` (наведение) и `<sound_t>` (нажатие) задают аудиоэффекты (`UIXmlInitBase.cpp:417-422`).

**Ванильный пример**: `gamedata_soc/configs/ui/message_box.xml:30-33`:
```xml
<button_yes x="140" y="154" width="117" height="29" check_mode="0">
    <text x="0" y="0" font="letterica16" r="216" g="186" b="140">ui_st_btn_yes</text>
    <texture>ui_button_ordinary</texture>
</button_yes>
```
Текстура `ui_button_ordinary` ссылается на текстуры из `configs/ui/ui_common.xml:9-12`:
- `ui_button_ordinary_e` (117x29, x=0, y=115, `ui\ui_common.dds`)
- `ui_button_ordinary_h` (117x29, x=0, y=144, `ui\ui_common.dds`)
- `ui_button_ordinary_t` (117x29, x=0, y=173, `ui\ui_common.dds`)
- `ui_button_ordinary_d` (117x29, x=0, y=86, `ui\ui_common.dds`)

---

#### 2.2. Галочка / Чекбокс (`InitCheck`)
- **Класс C++**: `CUICheckButton` (`lua_help.script:6440`).
- **Метод инициализации**: `xml:InitCheck(path, parent)` (`lua_help.script:5663`).
- **Принцип работы**:
  Движок (`OpenXRay: src/xrUICore/XML/UIXmlInitBase.cpp:210-220`) считывает тег `<texture>texture_id</texture>`. Если тег `<texture>` отсутствует, по умолчанию подставляется `"ui_checker"`.
  Класс `CUICheckButton` наследуется от кнопки и меняет текстуру при переключении состояния чекбокса (включено/выключено).

**Ванильный пример**: `gamedata_soc/configs/ui/ui_mm_opt.xml:302-305`:
```xml
<check_tips x="20" y="180" width="243" height="21">
    <options_item entry="hud_info" group="mm_opt_gameplay"/>
    <text font="letterica16" r="215" g="195" b="170">ui_mm_tips</text>
</check_tips>
```
Инициализация в скрипте: `gamedata_soc/scripts/ui_mm_opt_gameplay.script:19`:
```lua
xml:InitCheck("tab_gameplay:check_tips", self)
```
По умолчанию используется текстура `ui_checker` из `configs/ui/ui_common.xml:138-141` (`ui_checker_d`, `ui_checker_e`, `ui_checker_h`, `ui_checker_t`).

---

#### 2.3. Вкладки (`InitTab`)
- **Класс C++**: `CUITabControl` (`lua_help.script:7476`).
- **Метод инициализации**: `xml:InitTab(path, parent)` (`lua_help.script:5648`).
- **Принцип работы**:
  Движок (`OpenXRay: src/xrUICore/XML/UIXmlInitBase.cpp:783-818`) ищет дочерние теги `<button>` внутри узла таб-контрола. Для каждой кнопки считывается обязательный атрибут `id="..."`. Если указан атрибут `radio="1"`, создаются `CUIRadioButton`, иначе `CUITabButton`. Каждая вкладка инициализируется как 3-позиционная кнопка (`Init3tButton`).

**Ванильный пример**: `gamedata_soc/configs/ui/ui_mm_opt.xml:44-59`:
```xml
<tab x="55" y="22" width="461" height="52">
    <button id="video" x="0" y="0" width="107" height="31">
        <texture>ui_tab_button_01</texture>
        <text font="letterica18">ui_mm_video</text>
    </button>
    <button id="sound" x="110" y="0" width="107" height="31">
        <texture>ui_tab_button_02</texture>
        <text font="letterica18">ui_mm_sound</text>
    </button>
    <button id="gameplay" x="220" y="0" width="107" height="31">
        <texture>ui_tab_button_03</texture>
        <text font="letterica18">ui_mm_gameplay</text>
    </button>
    <button id="controls" x="330" y="0" width="107" height="31">
        <texture>ui_tab_button_04</texture>
        <text font="letterica18">ui_mm_controls</text>
    </button>
</tab>
```
Инициализация в скрипте: `gamedata_soc/scripts/ui_mm_opt_main.script:34`:
```lua
self.tab = xml:InitTab("tab", self)
```

---

#### 2.4. Полоса прогресса (`CUIProgressBar` / XML `<progress>`)
- **Класс C++**: `CUIProgressBar` (`lua_help.script:6901`).
- **Метод в скриптах**: В ТЧ метод `xml:InitProgressBar` **не выведен в Lua API** (`lua_help.script:5644-5670`). В ванильной игре прогресс-бары создавались движком из C++ (`CUIXmlInit::InitProgressBar`).
- **Принцип работы в XML движка**:
  Движок (`OpenXRay: src/xrUICore/XML/UIXmlInitBase.cpp:479-560`) считывает:
  - Атрибуты: `x`, `y`, `width`, `height`, ориентацию: `horz="1"` (или `mode="horz"|"vert"`)
  - Диапазон: `min`, `max`, текущую позицию `pos`
  - Дочерний элемент `<progress>`: текстура заполняемой части
  - Дочерний элемент `<background>`: подложка шкалы

**Ванильный пример**: `gamedata_soc/configs/ui/inventory_item.xml:48-52`:
```xml
<progress_accuracy x="0" y="24" width="121" height="8" horz="1" min="0" max="100" pos="0">
    <progress>
        <texture>ui_talk_bar</texture>
    </progress>
</progress_accuracy>
```

---

#### 2.5. Поле ввода (`InitEditBox`)
- **Класс C++**: `CUIEditBox` (`lua_help.script:6451`).
- **Метод инициализации**: `xml:InitEditBox(path, parent)` (`lua_help.script:5662`).
- **Принцип работы**:
  Движок (`OpenXRay: src/xrUICore/XML/UIXmlInitBase.cpp:860-910`) инициализирует поле ввода текста.
  Поддерживаемые XML-атрибуты:
  - `max_symb_count`: максимальная длина строки
  - `num_only="1"`: разрешить только цифры
  - `read_only="1"`: только чтение
  - `file_name_mode="1"`: проверка допустимости символов в имени файла (запрет спецсимволов)
  - `password="1"`: режим скрытия пароля звёздочками

**Ванильный пример**: `gamedata_soc/configs/ui/ui_numpad_wnd.xml:7-10`:
```xml
<edit_box x="41" y="28" width="245" height="31">
    <texture>ui_numpad_edit</texture>
    <text font="graffiti32" r="238" g="153" b="26"/>
</edit_box>
```
Инициализация в скрипте: `gamedata_soc/scripts/ui_numpad.script:21`:
```lua
self.edit_box = xml:InitEditBox("edit_box", self)
```

---

### 3. Шрифты в Тень Чернобыля

#### 3.1. Функции `GetFont*` из `lua_help.script`
Экспортированы в глобальное пространство имён Lua (`gamedata_soc/scripts/lua_help.script`):
1. `GetFont()` — строка 73 (`CGameFont* GetFont();`)
2. `GetFontDI()` — строка 69 (`CGameFont* GetFontDI();`)
3. `GetFontMedium()` — строка 70 (`CGameFont* GetFontMedium();`)
4. `GetFontSmall()` — строка 74 (`CGameFont* GetFontSmall();`)
5. `GetFontLetterica16Russian()` — строка 71 (`CGameFont* GetFontLetterica16Russian();`)
6. `GetFontLetterica18Russian()` — строка 75 (`CGameFont* GetFontLetterica18Russian();`)
7. `GetFontLetterica25()` — строка 77 (`CGameFont* GetFontLetterica25();`)
8. `GetFontGraffiti19Russian()` — строка 72 (`CGameFont* GetFontGraffiti19Russian();`)
9. `GetFontGraffiti22Russian()` — строка 78 (`CGameFont* GetFontGraffiti22Russian();`)
10. `GetFontGraffiti32Russian()` — строка 79 (`CGameFont* GetFontGraffiti32Russian();`)
11. `GetFontGraffiti50Russian()` — строка 76 (`CGameFont* GetFontGraffiti50Russian();`)

#### 3.2. Имена шрифтов в ванильных XML (`font="..."`)
В ванильных XML-файлах ТЧ встречаются следующие идентификаторы шрифтов:
- `arial_14` (`configs/ui/ui_mm_mp_tabserver.xml:13`)
- `graffiti18` (`configs/ui/pda_character_new.xml:19`)
- `graffiti19` (`configs/ui/talk.xml:21`)
- `graffiti22` (`configs/ui/inventory.xml:12`)
- `graffiti32` (`configs/ui/ui_numpad_wnd.xml:9`)
- `graffiti50` (`configs/ui/ui_mm_main.xml:14`)
- `letterica16` (`configs/ui/ui_mm_opt.xml:299`)
- `letterica18` (`configs/ui/message_box.xml:47`)
- `letterica25` (`configs/ui/ui_credits.xml:34`)
- `medium` (`configs/ui/talk.xml:35`)
- `small` (`configs/ui/maingame.xml:51`)
- `header` (`configs/ui/pda.xml:14`)
- `normal` (`configs/ui/job_item.xml:12`)

Все эти имена сопоставляются движком с физическими файлами шрифтов и размерами в файле конфигурации `configs/fonts.ltx:1-105`.

---

### 4. Каталог текстур родного интерфейса (ТЧ)

| ID текстуры | Файл .dds | Размер (WxH) | Где определена | Пример использования в XML | Тег в XML | Роль |
|-------------|-----------|--------------|----------------|----------------------------|-----------|------|
| `ui_bm_back` | `ui\ui_buy_menu` | 1024x768 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_16.xml:26` | `<texture>` | рамка окна |
| `ui_frame_mainDes` | `ui\ui_map_description` | 749x543 | `configs/ui/ui_map_description.xml` | `configs/ui/skin_selector.xml:7` | `<texture>` | рамка окна |
| `ui_inGame2_back_01w10_l` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_mm_opt_16.xml:13` | `<texture>` | рамка окна |
| `ui_inGame2_back_01w10_r` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_mm_opt_16.xml:17` | `<texture>` | рамка окна |
| `ui_inGame2_back_02w10_l` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_mm_save_dlg_16.xml:12` | `<texture>` | рамка окна |
| `ui_inGame2_back_02w10_r` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_mm_save_dlg_16.xml:15` | `<texture>` | рамка окна |
| `ui_inGame2_back_03w10_l` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_mm_mp_16.xml:13` | `<texture>` | рамка окна |
| `ui_inGame2_back_03w10_r` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_mm_mp_16.xml:16` | `<texture>` | рамка окна |
| `ui_menu_backgraund` | `ui\ui_mainMenu` | 1024x768 | `configs/ui/ui_mainmenu.xml` | `configs/ui/ui_mm_main.xml:4` | `<texture>` | рамка окна |
| `ui_numpad_lockframe` | `ui\ui_numpad` | 339x369 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:4` | `<texture>` | рамка окна |
| `ui_patch_back` | `ui\ui_hud` | 603x51 | `configs/ui/ui_hud.xml` | `configs/ui/ui_mm_opt_16.xml:736` | `<texture>` | рамка окна |
| `ui_statOne_back` | `ui\ui_statistics` | 440x163 | `configs/ui/ui_statistics.xml` | `configs/ui/stats.xml:21` | `<texture>` | рамка окна |
| `ui_statTwo_back` | `ui\ui_statistics` | 832x162 | `configs/ui/ui_statistics.xml` | `configs/ui/stats.xml:9` | `<texture>` | рамка окна |
| `blue_team_logo_small` | `ui\ui_team_logo_small` | 32x32 | `configs/ui/ui_team_logo_small.xml` | `configs/ui/ui_game_tdm.xml:14` | `<texture>` | кнопка |
| `green_team_logo_small` | `ui\ui_team_logo_small` | 32x32 | `configs/ui/ui_team_logo_small.xml` | `configs/ui/ui_game_tdm.xml:7` | `<texture>` | кнопка |
| `ui_TV_Skin` | `ui\ui_map_description` | 666x273 | `configs/ui/ui_map_description.xml` | `configs/ui/skin_selector.xml:13` | `<texture>` | кнопка |
| `ui_TV_Skin_button_l_d` | `ui\ui_map_description` | 33x218 | `configs/ui/ui_map_description.xml` | `configs/ui/skin_selector.xml:38` | `<texture[auto-_d]>` | кнопка |
| `ui_TV_Skin_button_l_e` | `ui\ui_map_description` | 33x218 | `configs/ui/ui_map_description.xml` | `configs/ui/skin_selector.xml:38` | `<texture[auto-_e]>` | кнопка |
| `ui_TV_Skin_button_l_h` | `ui\ui_map_description` | 33x218 | `configs/ui/ui_map_description.xml` | `configs/ui/skin_selector.xml:38` | `<texture[auto-_h]>` | кнопка |
| `ui_TV_Skin_button_l_t` | `ui\ui_map_description` | 33x218 | `configs/ui/ui_map_description.xml` | `configs/ui/skin_selector.xml:38` | `<texture[auto-_t]>` | кнопка |
| `ui_TV_Skin_button_r_d` | `ui\ui_map_description` | 33x218 | `configs/ui/ui_map_description.xml` | `configs/ui/skin_selector.xml:41` | `<texture[auto-_d]>` | кнопка |
| `ui_TV_Skin_button_r_e` | `ui\ui_map_description` | 33x218 | `configs/ui/ui_map_description.xml` | `configs/ui/skin_selector.xml:41` | `<texture[auto-_e]>` | кнопка |
| `ui_TV_Skin_button_r_h` | `ui\ui_map_description` | 33x218 | `configs/ui/ui_map_description.xml` | `configs/ui/skin_selector.xml:41` | `<texture[auto-_h]>` | кнопка |
| `ui_TV_Skin_button_r_t` | `ui\ui_map_description` | 33x218 | `configs/ui/ui_map_description.xml` | `configs/ui/skin_selector.xml:41` | `<texture[auto-_t]>` | кнопка |
| `ui_TV_descr_b` | `ui\ui_map_description` | 220x277 | `configs/ui/ui_map_description.xml` | `configs/ui/map_desc.xml:11` | `<texture>` | кнопка |
| `ui_TV_descr_back` | `ui\ui_map_description` | 224x277 | `configs/ui/ui_map_description.xml` | `configs/ui/map_desc.xml:14` | `<texture>` | кнопка |
| `ui_TV_descr_e` | `ui\ui_map_description` | 220x277 | `configs/ui/ui_map_description.xml` | `configs/ui/map_desc.xml:17` | `<texture>` | кнопка |
| `ui_TV_team_bottom` | `ui\ui_map_description` | 664x208 | `configs/ui/ui_map_description.xml` | `configs/ui/spawn_16.xml:17` | `<texture>` | кнопка |
| `ui_TV_team_tl` | `ui\ui_map_description` | 332x57 | `configs/ui/ui_map_description.xml` | `configs/ui/spawn_16.xml:11` | `<texture>` | кнопка |
| `ui_TV_team_tr` | `ui\ui_map_description` | 332x57 | `configs/ui/ui_map_description.xml` | `configs/ui/spawn_16.xml:14` | `<texture>` | кнопка |
| `ui_beltbut_aim_d` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:23` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_aim_e` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:23` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_aim_h` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:23` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_aim_t` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:23` | `<texture[auto-_t]>` | кнопка |
| `ui_beltbut_granadeBig_d` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:31` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_granadeBig_e` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:31` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_granadeBig_h` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:31` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_granadeBig_t` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:31` | `<texture[auto-_t]>` | кнопка |
| `ui_beltbut_granade_d` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:255` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_granade_e` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:255` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_granade_h` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:255` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_granade_t` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:255` | `<texture[auto-_t]>` | кнопка |
| `ui_beltbut_patrons_d` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:19` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_patrons_e` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:19` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_patrons_h` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:19` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_patrons_t` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:19` | `<texture[auto-_t]>` | кнопка |
| `ui_beltbut_silencer_d` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:15` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_silencer_e` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:15` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_silencer_h` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:15` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_silencer_t` | `ui\ui_common` | 23x23 | `configs/ui/ui_common.xml` | `configs/ui/inventorymp.xml:15` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_back_to_buy_d` | `ui\ui_buy_menu` | 47x41 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:128` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_back_to_buy_e` | `ui\ui_buy_menu` | 47x41 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:128` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_back_to_buy_h` | `ui\ui_buy_menu` | 47x41 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:128` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_back_to_buy_t` | `ui\ui_buy_menu` | 47x41 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:128` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_equp_01_d` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_equp_01_e` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_equp_01_h` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_equp_01_t` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_equp_02_d` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_equp_02_e` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_equp_02_h` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_equp_02_t` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_equp_03_d` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_equp_03_e` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_equp_03_h` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_equp_03_t` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_equp_04_d` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_equp_04_e` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_equp_04_h` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_equp_04_t` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_equp_05_d` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_equp_05_e` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_equp_05_h` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_equp_05_t` | `ui\ui_buy_menu` | 68x106 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_save_preset_d` | `ui\ui_buy_menu` | 37x25 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:189` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_save_preset_e` | `ui\ui_buy_menu` | 37x25 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:189` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_save_preset_h` | `ui\ui_buy_menu` | 37x25 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:189` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_save_preset_t` | `ui\ui_buy_menu` | 37x25 | `configs/ui/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:189` | `<texture[auto-_t]>` | кнопка |
| `ui_button_arrow_down_d` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:79` | `<texture[auto-_d]>` | кнопка |
| `ui_button_arrow_down_e` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:79` | `<texture[auto-_e]>` | кнопка |
| `ui_button_arrow_down_h` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:79` | `<texture[auto-_h]>` | кнопка |
| `ui_button_arrow_down_t` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:79` | `<texture[auto-_t]>` | кнопка |
| `ui_button_arrow_left_d` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:67` | `<texture[auto-_d]>` | кнопка |
| `ui_button_arrow_left_e` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:67` | `<texture[auto-_e]>` | кнопка |
| `ui_button_arrow_left_h` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:67` | `<texture[auto-_h]>` | кнопка |
| `ui_button_arrow_left_t` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:67` | `<texture[auto-_t]>` | кнопка |
| `ui_button_arrow_right_d` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:71` | `<texture[auto-_d]>` | кнопка |
| `ui_button_arrow_right_e` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:71` | `<texture[auto-_e]>` | кнопка |
| `ui_button_arrow_right_h` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:71` | `<texture[auto-_h]>` | кнопка |
| `ui_button_arrow_right_t` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:71` | `<texture[auto-_t]>` | кнопка |
| `ui_button_arrow_up_d` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:75` | `<texture[auto-_d]>` | кнопка |
| `ui_button_arrow_up_e` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:75` | `<texture[auto-_e]>` | кнопка |
| `ui_button_arrow_up_h` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:75` | `<texture[auto-_h]>` | кнопка |
| `ui_button_arrow_up_t` | `ui\ui_common` | 44x29 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:75` | `<texture[auto-_t]>` | кнопка |
| `ui_button_main01_d` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/skin_selector.xml:58` | `<texture[auto-_d]>` | кнопка |
| `ui_button_main01_e` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/skin_selector.xml:58` | `<texture[auto-_e]>` | кнопка |
| `ui_button_main01_h` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/skin_selector.xml:58` | `<texture[auto-_h]>` | кнопка |
| `ui_button_main01_t` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/skin_selector.xml:58` | `<texture[auto-_t]>` | кнопка |
| `ui_button_main02_d` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_d]>` | кнопка |
| `ui_button_main02_e` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_e]>` | кнопка |
| `ui_button_main02_h` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_h]>` | кнопка |
| `ui_button_main02_t` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_t]>` | кнопка |
| `ui_button_main03_d` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/skin_selector.xml:78` | `<texture[auto-_d]>` | кнопка |
| `ui_button_main03_e` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/skin_selector.xml:78` | `<texture[auto-_e]>` | кнопка |
| `ui_button_main03_h` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/skin_selector.xml:78` | `<texture[auto-_h]>` | кнопка |
| `ui_button_main03_t` | `ui\ui_common` | 157x48 | `configs/ui/ui_common.xml` | `configs/ui/skin_selector.xml:78` | `<texture[auto-_t]>` | кнопка |
| `ui_button_ordinary_d` | `ui\ui_common` | 117x29 | `configs/ui/ui_common.xml` | `configs/ui/message_box_16.xml:17` | `<texture[auto-_d]>` | кнопка |
| `ui_button_ordinary_e` | `ui\ui_common` | 117x29 | `configs/ui/ui_common.xml` | `configs/ui/message_box_16.xml:17` | `<texture[auto-_e]>` | кнопка |
| `ui_button_ordinary_h` | `ui\ui_common` | 117x29 | `configs/ui/ui_common.xml` | `configs/ui/message_box_16.xml:17` | `<texture[auto-_h]>` | кнопка |
| `ui_button_ordinary_t` | `ui\ui_common` | 117x29 | `configs/ui/ui_common.xml` | `configs/ui/message_box_16.xml:17` | `<texture[auto-_t]>` | кнопка |
| `ui_cur_task` | `ui\ui_common` | 19x19 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:41` | `<texture>` | кнопка |
| `ui_frame_01_t` | `ui\ui_old_textures` | 32x32 | `configs/ui/ui_old_textures.xml` | `configs/ui/ui_game_dm.xml:3` | `<texture[auto-_t]>` | кнопка |
| `ui_frame_03_t` | `ui\ui_old_textures` | 128x64 | `configs/ui/ui_old_textures.xml` | `configs/ui/trade.xml:85` | `<texture[auto-_t]>` | кнопка |
| `ui_frame_error` | `ui\ui_common` | 510x206 | `configs/ui/ui_common.xml` | `configs/ui/message_box_16.xml:4` | `<texture>` | кнопка |
| `ui_frame_error_sign_alarm` | `ui\ui_common` | 77x77 | `configs/ui/ui_common.xml` | `configs/ui/message_box_16.xml:7` | `<texture>` | кнопка |
| `ui_frame_error_sign_info` | `ui\ui_common` | 77x77 | `configs/ui/ui_common.xml` | `configs/ui/message_box_16.xml:130` | `<texture>` | кнопка |
| `ui_frame_error_sign_red` | `ui\ui_common` | 77x77 | `configs/ui/ui_common.xml` | `configs/ui/message_box_16.xml:48` | `<texture>` | кнопка |
| `ui_frame_t` | `ui\ui_old_textures` | 128x128 | `configs/ui/ui_old_textures.xml` | `configs/ui/stats.xml:93` | `<texture[auto-_t]>` | кнопка |
| `ui_highlight` | `ui\ui_common` | 128x128 | `configs/ui/ui_common.xml` | `configs/ui/game_tutorials.xml:44` | `<texture>` | кнопка |
| `ui_hud_button_voting_01_d` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:38` | `<texture[auto-_d]>` | кнопка |
| `ui_hud_button_voting_01_e` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:38` | `<texture[auto-_e]>` | кнопка |
| `ui_hud_button_voting_01_h` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:38` | `<texture[auto-_h]>` | кнопка |
| `ui_hud_button_voting_01_t` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:38` | `<texture[auto-_t]>` | кнопка |
| `ui_hud_button_voting_02_d` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:48` | `<texture[auto-_d]>` | кнопка |
| `ui_hud_button_voting_02_e` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:48` | `<texture[auto-_e]>` | кнопка |
| `ui_hud_button_voting_02_h` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:48` | `<texture[auto-_h]>` | кнопка |
| `ui_hud_button_voting_02_t` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:48` | `<texture[auto-_t]>` | кнопка |
| `ui_hud_button_voting_03_d` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:58` | `<texture[auto-_d]>` | кнопка |
| `ui_hud_button_voting_03_e` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:58` | `<texture[auto-_e]>` | кнопка |
| `ui_hud_button_voting_03_h` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:58` | `<texture[auto-_h]>` | кнопка |
| `ui_hud_button_voting_03_t` | `ui\ui_hud` | 37x25 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:58` | `<texture[auto-_t]>` | кнопка |
| `ui_hud_fragBig` | `ui\ui_hud` | 236x40 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:73` | `<texture>` | кнопка |
| `ui_hud_frame_buyM` | `ui\ui_hud` | 574x232 | `configs/ui/ui_hud.xml` | `configs/ui/trade.xml:89` | `<texture>` | кнопка |
| `ui_hud_frame_clock` | `ui\ui_hud` | 157x67 | `configs/ui/ui_hud.xml` | `configs/ui/ui_custom_msgs.xml:6` | `<texture>` | кнопка |
| `ui_hud_frame_money` | `ui\ui_hud` | 100x65 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:20` | `<texture>` | кнопка |
| `ui_hud_frame_moneyNumber` | `ui\ui_hud` | 119x46 | `configs/ui/ui_hud.xml` | `configs/ui/trade.xml:38` | `<texture>` | кнопка |
| `ui_hud_frame_patron` | `ui\ui_hud` | 157x65 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:35` | `<texture>` | кнопка |
| `ui_hud_frame_rank` | `ui\ui_hud` | 60x65 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:36` | `<texture>` | кнопка |
| `ui_hud_frame_voting` | `ui\ui_hud` | 600x371 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:7` | `<texture>` | кнопка |
| `ui_hud_frame_voting_dop2` | `ui\ui_hud` | 573x39 | `configs/ui/ui_hud.xml` | `configs/ui/voting_category_16.xml:296` | `<texture>` | кнопка |
| `ui_hud_grenadetarget_d` | `ui\ui_hud` | 91x92 | `configs/ui/ui_hud.xml` | `configs/ui/grenade.xml:9` | `<texture>` | кнопка |
| `ui_hud_grenadetarget_e` | `ui\ui_hud` | 91x92 | `configs/ui/ui_hud.xml` | `configs/ui/grenade.xml:6` | `<texture>` | кнопка |
| `ui_hud_icon_PDA` | `ui\ui_hud` | 48x29 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:94` | `<texture>` | кнопка |
| `ui_hud_icon_armour` | `ui\ui_hud` | 19x18 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:23` | `<texture>` | кнопка |
| `ui_hud_icon_artefact` | `ui\ui_hud` | 64x64 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:88` | `<texture>` | кнопка |
| `ui_hud_icon_car` | `ui\ui_hud` | 19x18 | `configs/ui/ui_hud.xml` | `configs/ui/car_panel.xml:7` | `<texture>` | кнопка |
| `ui_hud_icon_drop` | `ui\ui_hud` | 64x64 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:68` | `<texture>` | кнопка |
| `ui_hud_icon_eat` | `ui\ui_hud` | 64x64 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:72` | `<texture>` | кнопка |
| `ui_hud_icon_goodmode` | `ui\ui_hud` | 64x64 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:80` | `<texture>` | кнопка |
| `ui_hud_icon_health` | `ui\ui_hud` | 19x18 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:10` | `<texture>` | кнопка |
| `ui_hud_icon_psycho` | `ui\ui_hud` | 64x64 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:76` | `<texture>` | кнопка |
| `ui_hud_icon_radiation` | `ui\ui_hud` | 64x64 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:64` | `<texture>` | кнопка |
| `ui_hud_icon_sleep` | `ui\ui_hud` | 64x64 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:84` | `<texture>` | кнопка |
| `ui_hud_icon_weapon` | `ui\ui_hud` | 64x64 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:60` | `<texture>` | кнопка |
| `ui_hud_map` | `ui\ui_hud` | 185x192 | `configs/ui/ui_hud.xml` | `configs/ui/zone_map.xml:8` | `<texture>` | кнопка |
| `ui_hud_map_counter` | `ui\ui_hud` | 35x28 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:47` | `<texture>` | кнопка |
| `ui_hud_shk_armour` | `ui\ui_hud` | 110x10 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:29` | `<texture>` | кнопка |
| `ui_hud_shk_car` | `ui\ui_hud` | 110x10 | `configs/ui/ui_hud.xml` | `configs/ui/car_panel.xml:14` | `<texture>` | кнопка |
| `ui_hud_shk_health` | `ui\ui_hud` | 110x10 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:16` | `<texture>` | кнопка |
| `ui_hud_shk_light` | `ui\ui_hud` | 10x71 | `configs/ui/ui_hud.xml` | `configs/ui/motion_icon.xml:41` | `<texture>` | кнопка |
| `ui_hud_shk_noise` | `ui\ui_hud` | 10x71 | `configs/ui/ui_hud.xml` | `configs/ui/motion_icon.xml:47` | `<texture>` | кнопка |
| `ui_hud_shk_stamina` | `ui\ui_hud` | 57x7 | `configs/ui/ui_hud.xml` | `configs/ui/motion_icon.xml:35` | `<texture>` | кнопка |
| `ui_hud_shkala_armor` | `ui\ui_hud` | 164x38 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:21` | `<texture>` | кнопка |
| `ui_hud_shkala_car` | `ui\ui_hud` | 155x34 | `configs/ui/ui_hud.xml` | `configs/ui/car_panel.xml:5` | `<texture>` | кнопка |
| `ui_hud_shkala_health` | `ui\ui_hud` | 164x34 | `configs/ui/ui_hud.xml` | `configs/ui/maingame_16.xml:8` | `<texture>` | кнопка |
| `ui_hud_soldier_climb` | `ui\ui_hud` | 65x75 | `configs/ui/ui_hud.xml` | `configs/ui/motion_icon.xml:22` | `<texture>` | кнопка |
| `ui_hud_soldier_creep` | `ui\ui_hud` | 65x75 | `configs/ui/ui_hud.xml` | `configs/ui/motion_icon.xml:18` | `<texture>` | кнопка |
| `ui_hud_soldier_crouch` | `ui\ui_hud` | 65x75 | `configs/ui/ui_hud.xml` | `configs/ui/motion_icon.xml:14` | `<texture>` | кнопка |
| `ui_hud_soldier_normal` | `ui\ui_hud` | 65x75 | `configs/ui/ui_hud.xml` | `configs/ui/motion_icon.xml:9` | `<texture>` | кнопка |
| `ui_hud_soldier_run` | `ui\ui_hud` | 65x75 | `configs/ui/ui_hud.xml` | `configs/ui/motion_icon.xml:26` | `<texture>` | кнопка |
| `ui_hud_soldier_sprint` | `ui\ui_hud` | 65x75 | `configs/ui/ui_hud.xml` | `configs/ui/motion_icon.xml:30` | `<texture>` | кнопка |
| `ui_hud_stamina_full` | `ui\ui_hud` | 125x125 | `configs/ui/ui_hud.xml` | `configs/ui/motion_icon.xml:5` | `<texture>` | кнопка |
| `ui_hud_status_blue_01` | `ui\ui_hud` | 46x47 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:55` | `<texture>` | кнопка |
| `ui_hud_status_blue_02` | `ui\ui_hud` | 46x47 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:58` | `<texture>` | кнопка |
| `ui_hud_status_blue_03` | `ui\ui_hud` | 46x47 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:61` | `<texture>` | кнопка |
| `ui_hud_status_blue_04` | `ui\ui_hud` | 46x47 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:64` | `<texture>` | кнопка |
| `ui_hud_status_blue_05` | `ui\ui_hud` | 46x47 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:67` | `<texture>` | кнопка |
| `ui_hud_status_green_01` | `ui\ui_hud` | 46x47 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:40` | `<texture>` | кнопка |
| `ui_hud_status_green_02` | `ui\ui_hud` | 46x47 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:43` | `<texture>` | кнопка |
| `ui_hud_status_green_03` | `ui\ui_hud` | 46x47 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:46` | `<texture>` | кнопка |
| `ui_hud_status_green_04` | `ui\ui_hud` | 46x47 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:49` | `<texture>` | кнопка |
| `ui_hud_status_green_05` | `ui\ui_hud` | 46x47 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_dm.xml:52` | `<texture>` | кнопка |
| `ui_hud_teamF_counter` | `ui\ui_hud` | 36x70 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:14` | `<texture>` | кнопка |
| `ui_hud_teamF_counterC` | `ui\ui_hud` | 33x33 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:10` | `<texture>` | кнопка |
| `ui_hud_teamF_leftS` | `ui\ui_hud` | 39x43 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_tdm.xml:5` | `<texture>` | кнопка |
| `ui_hud_teamF_rightS` | `ui\ui_hud` | 39x43 | `configs/ui/ui_hud.xml` | `configs/ui/ui_game_tdm.xml:12` | `<texture>` | кнопка |
| `ui_icons_PDA_dialog_frame_t` | `ui\ui_common` | 4x4 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:94` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_PDA_dialog_string_e` | `ui\ui_common` | 16x22 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:89` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_PDA_dialog_t` | `ui\ui_common` | 32x32 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:86` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_PDA_string_tooltips_e` | `ui\ui_common` | 32x30 | `configs/ui/ui_common.xml` | `configs/ui/hint_item.xml:16` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_mapPDA_mark_t` | `ui\ui_common` | 32x32 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:75` | `<texture>` | кнопка |
| `ui_icons_mapPDA_persBig_e` | `ui\ui_common` | 32x32 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:49` | `<texture>` | кнопка |
| `ui_icons_mapPDA_persBig_h` | `ui\ui_common` | 32x32 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:62` | `<texture>` | кнопка |
| `ui_icons_newPDA_CrclMiddle_h` | `ui\ui_common` | 62x62 | `configs/ui/ui_common.xml` | `configs/ui/game_tutorials.xml:142` | `<texture>` | кнопка |
| `ui_icons_newPDA_ZoomMin_d` | `ui\ui_common` | 18x14 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:38` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_ZoomMin_e` | `ui\ui_common` | 18x14 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:38` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_ZoomMin_h` | `ui\ui_common` | 18x14 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:38` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_ZoomMin_t` | `ui\ui_common` | 21x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:38` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_compas_d` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:20` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_compas_e` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:20` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_compas_h` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:20` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_compas_t` | `ui\ui_common` | 21x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:20` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_editUserSpot_d` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:79` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_editUserSpot_e` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:79` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_editUserSpot_h` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:79` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_editUserSpot_t` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:79` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_gomark_d` | `ui\ui_common` | 21x19 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:49` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_gomark_e` | `ui\ui_common` | 21x19 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:49` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_gomark_h` | `ui\ui_common` | 21x19 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:49` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_gomark_t` | `ui\ui_common` | 21x19 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:49` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_mapMarDel_d` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:75` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_mapMarDel_e` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:75` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_mapMarDel_h` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:75` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_mapMarDel_t` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:75` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_mark_d` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:131` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_mark_e` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:131` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_mark_h` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:131` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_mark_t` | `ui\ui_common` | 21x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:131` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_perssign_d` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:28` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_perssign_e` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:28` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_perssign_h` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:28` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_perssign_t` | `ui\ui_common` | 21x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:28` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_perssigndel_d` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:152` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_perssigndel_e` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:152` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_perssigndel_h` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:152` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_perssigndel_t` | `ui\ui_common` | 21x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:152` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_setSelect_d` | `ui\ui_common` | 18x18 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:45` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_setSelect_e` | `ui\ui_common` | 18x18 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:45` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_setSelect_h` | `ui\ui_common` | 18x18 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:45` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_setSelect_t` | `ui\ui_common` | 18x18 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:45` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_showMarkonmap_d` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:23` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_showMarkonmap_e` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:23` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_showMarkonmap_h` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:23` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_showMarkonmap_t` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:23` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_showpers_d` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:24` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_showpers_e` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/af_params_16.xml:53` | `<texture>` | кнопка |
| `ui_icons_newPDA_showpers_h` | `ui\ui_common` | 15x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:24` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_showpers_t` | `ui\ui_common` | 21x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:24` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_showtext_d` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:19` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_showtext_e` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:19` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_showtext_h` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:19` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_showtext_t` | `ui\ui_common` | 20x20 | `configs/ui/ui_common.xml` | `configs/ui/job_item.xml:19` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_newPDA_zoom_d` | `ui\ui_common` | 18x14 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:33` | `<texture[auto-_d]>` | кнопка |
| `ui_icons_newPDA_zoom_e` | `ui\ui_common` | 18x14 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:33` | `<texture[auto-_e]>` | кнопка |
| `ui_icons_newPDA_zoom_h` | `ui\ui_common` | 18x14 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:33` | `<texture[auto-_h]>` | кнопка |
| `ui_icons_newPDA_zoom_t` | `ui\ui_common` | 21x15 | `configs/ui/ui_common.xml` | `configs/ui/pda_map.xml:33` | `<texture[auto-_t]>` | кнопка |
| `ui_inv_icon_explosion_immunity` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/inventory_new.xml:110` | `<texture>` | кнопка |
| `ui_inv_icon_health_restore_speed` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/af_params.xml:4` | `<texture>` | кнопка |
| `ui_inv_icon_telepatic_immunity` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/inventory_new.xml:100` | `<texture>` | кнопка |
| `ui_mapQuest_camp_defend` | `ui\ui_common` | 29x29 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:102` | `<texture>` | кнопка |
| `ui_mapQuest_camp_destroy` | `ui\ui_common` | 29x29 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:89` | `<texture>` | кнопка |
| `ui_mapQuest_stalker_destroy` | `ui\ui_common` | 29x29 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:128` | `<texture>` | кнопка |
| `ui_menu_options_dlg` | `ui\ui_common` | 566x461 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_save_dlg_16.xml:31` | `<texture>` | кнопка |
| `ui_mp_Voting_tv` | `ui\ui_mp_main` | 300x261 | `configs/ui/ui_mp_main.xml` | `configs/ui/voting_category_16.xml:174` | `<texture>` | кнопка |
| `ui_pda_frame2_t` | `ui\ui_old_textures` | 32x32 | `configs/ui/ui_old_textures.xml` | `configs/ui/actor_statistic.xml:11` | `<texture[auto-_t]>` | кнопка |
| `ui_pda_frame_sub_t` | `ui\ui_old_textures` | 32x32 | `configs/ui/ui_old_textures.xml` | `configs/ui/pda_new.xml:15` | `<texture[auto-_t]>` | кнопка |
| `ui_scale_three` | `ui\ui_inventory2` | 59x179 | `configs/ui/ui_inventory2.xml` | `configs/ui/inventory_new.xml:129` | `<texture>` | кнопка |
| `ui_sm_mapQuest_camp_defend` | `ui\ui_common` | 13x13 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:105` | `<texture>` | кнопка |
| `ui_sm_mapQuest_camp_destroy` | `ui\ui_common` | 13x13 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:92` | `<texture>` | кнопка |
| `ui_sm_mapQuest_stalker_destroy` | `ui\ui_common` | 13x13 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:131` | `<texture>` | кнопка |
| `ui_statOne_t` | `ui\ui_statistics` | 446x95 | `configs/ui/ui_statistics.xml` | `configs/ui/stats.xml:18` | `<texture>` | кнопка |
| `ui_statTwo_t` | `ui\ui_statistics` | 845x98 | `configs/ui/ui_statistics.xml` | `configs/ui/stats.xml:6` | `<texture>` | кнопка |
| `ui_string_01_e` | `ui\ui_old_textures` | 32x32 | `configs/ui/ui_old_textures.xml` | `configs/ui/pda.xml:18` | `<texture[auto-_e]>` | кнопка |
| `ui_string_02_e` | `ui\ui_old_textures` | 32x32 | `configs/ui/ui_old_textures.xml` | `configs/ui/actor_statistic.xml:41` | `<texture[auto-_e]>` | кнопка |
| `ui_string_03_e` | `ui\ui_old_textures` | 32x32 | `configs/ui/ui_old_textures.xml` | `configs/ui/map.xml:14` | `<texture[auto-_e]>` | кнопка |
| `ui_talk_dialogue2_e` | `ui\ui_numpad` | 600x40 | `configs/ui/ui_numpad.xml` | `configs/ui/talk_16.xml:35` | `<texture[auto-_e]>` | кнопка |
| `ui_talk_dialogue_e` | `ui\ui_numpad` | 600x40 | `configs/ui/ui_numpad.xml` | `configs/ui/talk_16.xml:28` | `<texture[auto-_e]>` | кнопка |
| `ui_teambase` | `ui\ui_common` | 29x29 | `configs/ui/ui_common.xml` | `configs/ui/map_spots_mp.xml:49` | `<texture>` | кнопка |
| `ui_PDA_checker_d` | `ui\ui_common` | 17x17 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:20` | `<texture[auto-_d]>` | галочка |
| `ui_PDA_checker_e` | `ui\ui_common` | 17x17 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:20` | `<texture[auto-_e]>` | галочка |
| `ui_PDA_checker_h` | `ui\ui_common` | 17x17 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:20` | `<texture[auto-_h]>` | галочка |
| `ui_PDA_checker_t` | `ui\ui_common` | 17x17 | `configs/ui/ui_common.xml` | `configs/ui/pda_events.xml:20` | `<texture[auto-_t]>` | галочка |
| `ui_cb_listbox_t` | `ui\ui_common` | 10x10 | `configs/ui/ui_common.xml` | `configs/ui/inventory_new.xml:4` | `<texture[auto-_t]>` | фон списка |
| `ui_scroll_PDA_back` | `ui\ui_common` | 17x30 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:63` | `<texture>` | фон списка |
| `ui_scroll_PDA_back_hor` | `ui\ui_common` | 30x17 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:60` | `<texture>` | фон списка |
| `ui_scroll_PDA_box_e` | `ui\ui_common` | 17x28 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:70` | `<texture>` | фон списка |
| `ui_scroll_PDA_btn_down_e` | `ui\ui_common` | 17x30 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:43` | `<texture_e>` | фон списка |
| `ui_scroll_PDA_btn_down_h` | `ui\ui_common` | 17x30 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:44` | `<texture_h>` | фон списка |
| `ui_scroll_PDA_btn_down_t` | `ui\ui_common` | 17x30 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:45` | `<texture_t>` | фон списка |
| `ui_scroll_PDA_btn_left_e` | `ui\ui_common` | 21x17 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:49` | `<texture_e>` | фон списка |
| `ui_scroll_PDA_btn_left_h` | `ui\ui_common` | 21x17 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:50` | `<texture_h>` | фон списка |
| `ui_scroll_PDA_btn_left_t` | `ui\ui_common` | 21x17 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:51` | `<texture_t>` | фон списка |
| `ui_scroll_PDA_btn_right_e` | `ui\ui_common` | 21x17 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:54` | `<texture_e>` | фон списка |
| `ui_scroll_PDA_btn_right_h` | `ui\ui_common` | 21x17 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:55` | `<texture_h>` | фон списка |
| `ui_scroll_PDA_btn_right_t` | `ui\ui_common` | 21x17 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:56` | `<texture_t>` | фон списка |
| `ui_scroll_PDA_btn_up_e` | `ui\ui_common` | 17x21 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:38` | `<texture_e>` | фон списка |
| `ui_scroll_PDA_btn_up_h` | `ui\ui_common` | 17x21 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:39` | `<texture_h>` | фон списка |
| `ui_scroll_PDA_btn_up_t` | `ui\ui_common` | 17x21 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:40` | `<texture_t>` | фон списка |
| `ui_scroll_back` | `ui\ui_common` | 15x16 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:29` | `<texture>` | фон списка |
| `ui_scroll_box` | `ui\ui_common` | 15x16 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:33` | `<texture>` | фон списка |
| `ui_scroll_btn_down` | `ui\ui_common` | 15x16 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:25` | `<texture_e>` | фон списка |
| `ui_scroll_btn_up` | `ui\ui_common` | 15x16 | `configs/ui/ui_common.xml` | `configs/ui/scroll_bar.xml:21` | `<texture_e>` | фон списка |
| `ui_button_tablist_d` | `ui\ui_common` | 114x44 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:52` | `<texture[auto-_d]>` | вкладка |
| `ui_button_tablist_e` | `ui\ui_common` | 114x44 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:52` | `<texture[auto-_e]>` | вкладка |
| `ui_button_tablist_h` | `ui\ui_common` | 114x44 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:52` | `<texture[auto-_h]>` | вкладка |
| `ui_button_tablist_t` | `ui\ui_common` | 114x44 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:52` | `<texture[auto-_t]>` | вкладка |
| `ui_statTwo_tabdiv_l` | `ui\ui_statistics` | 402x25 | `configs/ui/ui_statistics.xml` | `configs/ui/stats.xml:60` | `<texture>` | вкладка |
| `ui_table_button_e_e` | `ui\ui_common` | 5x19 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:44` | `<texture[auto-_e]>` | вкладка |
| `ui_table_button_o_e` | `ui\ui_common` | 4x19 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:26` | `<texture[auto-_e]>` | вкладка |
| `ui_table_divider_e` | `ui\ui_common` | 1x10 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:30` | `<texture[auto-_e]>` | вкладка |
| `ui_tablist_divider_e` | `ui\ui_common` | 36x31 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:75` | `<texture[auto-_e]>` | вкладка |
| `ui_tablist_textbox_t` | `ui\ui_common` | 32x32 | `configs/ui/ui_common.xml` | `configs/ui/voting_category_16.xml:161` | `<texture[auto-_t]>` | вкладка |
| `ui_buymenu_progBar` | `ui\ui_common` | 151x7 | `configs/ui/ui_common.xml` | `configs/ui/inventory_item.xml:50` | `<texture>` | полоса прогресса |
| `ui_mg_progress_efficiency_empty` | `ui\ui_hud` | 120x17 | `configs/ui/ui_hud.xml` | `configs/ui/carbody_item_16.xml:47` | `<texture>` | полоса прогресса |
| `ui_mg_progress_efficiency_full` | `ui\ui_hud` | 120x17 | `configs/ui/ui_hud.xml` | `configs/ui/inventory_item.xml:30` | `<texture>` | полоса прогресса |
| `ui_patch_progress` | `ui\ui_hud` | 403x10 | `configs/ui/ui_hud.xml` | `configs/ui/ui_mm_opt_16.xml:745` | `<texture>` | полоса прогресса |
| `ui_brokenline_e` | `ui\ui_common` | 9x21 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:79` | `<texture[auto-_e]>` | разделитель |
| `ui_linetextSmall_e_e` | `ui\ui_common` | 9x21 | `configs/ui/ui_common.xml` | `configs/ui/message_box_16.xml:166` | `<texture[auto-_e]>` | разделитель |
| `ui_linetext_e_e` | `ui\ui_common` | 9x24 | `configs/ui/ui_common.xml` | `configs/ui/ui_mm_save_dlg_16.xml:38` | `<texture[auto-_e]>` | разделитель |
| `rust_bot_client` | `ui\ui_mp_main` | 201x134 | `configs/ui/ui_mp_main.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:6` | `<texture>` | прочее |
| `rusty_01` | `ui\ui_mp_main` | 310x99 | `configs/ui/ui_mp_main.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:3` | `<texture>` | прочее |
| `ui_PDA` | `ui\ui_PDA` | 1020x702 | `configs/ui/ui_pda.xml` | `configs/ui/pda.xml:12` | `<texture>` | прочее |
| `ui_asus_01` | `ui\ui_asus_intro` | 512x512 | `configs/ui/ui_asus_intro.xml` | `configs/ui/ui_movies.xml:42` | `<texture>` | прочее |
| `ui_asus_02` | `ui\ui_asus_intro` | 512x512 | `configs/ui/ui_asus_intro.xml` | `configs/ui/ui_movies.xml:46` | `<texture>` | прочее |
| `ui_charinfo_left` | `ui\ui_inventory2` | 210x218 | `configs/ui/ui_inventory2.xml` | `configs/ui/carbody_new_16.xml:8` | `<texture>` | прочее |
| `ui_charinfo_right` | `ui\ui_inventory2` | 211x218 | `configs/ui/ui_inventory2.xml` | `configs/ui/carbody_new_16.xml:14` | `<texture>` | прочее |
| `ui_icons_newPDA_SmallBlue` | `ui\ui_common` | 11x11 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:52` | `<texture>` | прочее |
| `ui_icons_newPDA_SmallGreen` | `ui\ui_common` | 11x11 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:65` | `<texture>` | прочее |
| `ui_icons_newPDA_SmallRed` | `ui\ui_common` | 11x11 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:78` | `<texture>` | прочее |
| `ui_inv_icon_bleeding_restore_speed` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/af_params.xml:24` | `<texture>` | прочее |
| `ui_inv_icon_burn_immunity` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/inventory_new.xml:75` | `<texture>` | прочее |
| `ui_inv_icon_chemical_burn_immunity` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/inventory_new.xml:105` | `<texture>` | прочее |
| `ui_inv_icon_fire_wound_immunity` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/inventory_new.xml:115` | `<texture>` | прочее |
| `ui_inv_icon_power_restore_speed` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/af_params.xml:19` | `<texture>` | прочее |
| `ui_inv_icon_radiation_immunity` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/inventory_new.xml:95` | `<texture>` | прочее |
| `ui_inv_icon_radiation_restore_speed` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/af_params.xml:9` | `<texture>` | прочее |
| `ui_inv_icon_satiety_restore_speed` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/af_params.xml:14` | `<texture>` | прочее |
| `ui_inv_icon_shock_immunity` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/inventory_new.xml:85` | `<texture>` | прочее |
| `ui_inv_icon_strike_immunity` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/inventory_new.xml:80` | `<texture>` | прочее |
| `ui_inv_icon_wound_immunity` | `ui\ui_hud` | 18x18 | `configs/ui/ui_hud.xml` | `configs/ui/inventory_new.xml:90` | `<texture>` | прочее |
| `ui_inventory_main` | `ui\ui_inventory` | 1005x483 | `configs/ui/ui_inventory.xml` | `configs/ui/inventory_new.xml:18` | `<texture>` | прочее |
| `ui_inventory_mp_buy` | `ui\ui_inventory` | 1014x477 | `configs/ui/ui_inventory.xml` | `configs/ui/inventorymp.xml:11` | `<texture>` | прочее |
| `ui_inventory_rank` | `ui\ui_inventory` | 75x65 | `configs/ui/ui_inventory.xml` | `configs/ui/inventory_new.xml:124` | `<texture>` | прочее |
| `ui_mapQuest_artefact` | `ui\ui_common` | 29x29 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:115` | `<texture>` | прочее |
| `ui_mapQuest_gold` | `ui\ui_common` | 29x29 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:243` | `<texture>` | прочее |
| `ui_mapQuest_item` | `ui\ui_common` | 29x29 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:154` | `<texture>` | прочее |
| `ui_mapQuest_monster_find` | `ui\ui_common` | 29x29 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:141` | `<texture>` | прочее |
| `ui_menu_bigroll_l` | `ui\ui_mainMenu` | 86x86 | `configs/ui/ui_mainmenu.xml` | `configs/ui/ui_mm_main.xml:26` | `<texture>` | прочее |
| `ui_menu_bigroll_r` | `ui\ui_mainMenu` | 86x86 | `configs/ui/ui_mainmenu.xml` | `configs/ui/ui_mm_main.xml:29` | `<texture>` | прочее |
| `ui_menu_grating_l` | `ui\ui_mainMenu` | 46x70 | `configs/ui/ui_mainmenu.xml` | `configs/ui/ui_mm_main_16.xml:49` | `<texture>` | прочее |
| `ui_menu_grating_r` | `ui\ui_mainMenu` | 55x67 | `configs/ui/ui_mainmenu.xml` | `configs/ui/ui_mm_main_16.xml:52` | `<texture>` | прочее |
| `ui_menu_move` | `ui\ui_mainMenu` | 392x73 | `configs/ui/ui_mainmenu.xml` | `configs/ui/ui_mm_main_16.xml:30` | `<texture>` | прочее |
| `ui_menu_smallroll_l` | `ui\ui_mainMenu` | 55x55 | `configs/ui/ui_mainmenu.xml` | `configs/ui/ui_mm_main_16.xml:37` | `<texture>` | прочее |
| `ui_menu_smallroll_r` | `ui\ui_mainMenu` | 55x55 | `configs/ui/ui_mainmenu.xml` | `configs/ui/ui_mm_main_16.xml:40` | `<texture>` | прочее |
| `ui_mini_af_spot` | `ui\ui_common` | 13x15 | `configs/ui/ui_common.xml` | `configs/ui/map_spots_mp.xml:69` | `<texture>` | прочее |
| `ui_mini_af_spot_above` | `ui\ui_common` | 13x15 | `configs/ui/ui_common.xml` | `configs/ui/map_spots_mp.xml:100` | `<texture_above>` | прочее |
| `ui_mini_af_spot_below` | `ui\ui_common` | 13x15 | `configs/ui/ui_common.xml` | `configs/ui/map_spots_mp.xml:99` | `<texture_below>` | прочее |
| `ui_mini_sn_spot_above` | `ui\ui_common` | 11x11 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:54` | `<texture_above>` | прочее |
| `ui_mini_sn_spot_below` | `ui\ui_common` | 11x11 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:53` | `<texture_below>` | прочее |
| `ui_minimap_point` | `ui\ui_common` | 12x12 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:306` | `<texture>` | прочее |
| `ui_numpad_key00` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:13` | `<texture_t>` | прочее |
| `ui_numpad_key01` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:17` | `<texture_t>` | прочее |
| `ui_numpad_key02` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:21` | `<texture_t>` | прочее |
| `ui_numpad_key03` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:25` | `<texture_t>` | прочее |
| `ui_numpad_key04` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:29` | `<texture_t>` | прочее |
| `ui_numpad_key05` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:33` | `<texture_t>` | прочее |
| `ui_numpad_key06` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:37` | `<texture_t>` | прочее |
| `ui_numpad_key07` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:41` | `<texture_t>` | прочее |
| `ui_numpad_key08` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:45` | `<texture_t>` | прочее |
| `ui_numpad_key09` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:49` | `<texture_t>` | прочее |
| `ui_numpad_key0b` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:58` | `<texture_t>` | прочее |
| `ui_numpad_key0c` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:54` | `<texture_t>` | прочее |
| `ui_numpad_keyCancel` | `ui\ui_numpad` | 111x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:66` | `<texture_t>` | прочее |
| `ui_numpad_keyEnter` | `ui\ui_numpad` | 111x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:62` | `<texture_t>` | прочее |
| `ui_numpad_void` | `ui\ui_numpad` | 72x45 | `configs/ui/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:12` | `<texture_e>` | прочее |
| `ui_pda_m_noupdate` | `ui\ui_common` | 9x11 | `configs/ui/ui_common.xml` | `configs/ui/events_new.xml:80` | `<texture>` | прочее |
| `ui_pda_m_update` | `ui\ui_common` | 9x11 | `configs/ui/ui_common.xml` | `configs/ui/events_new.xml:84` | `<texture>` | прочее |
| `ui_scale_blue_1` | `ui\ui_inventory2` | 9x128 | `configs/ui/ui_inventory2.xml` | `configs/ui/inventory_new.xml:144` | `<texture>` | прочее |
| `ui_scale_green` | `ui\ui_inventory2` | 9x128 | `configs/ui/ui_inventory2.xml` | `configs/ui/inventory_new.xml:156` | `<texture>` | прочее |
| `ui_scale_one` | `ui\ui_inventory2` | 40x179 | `configs/ui/ui_inventory2.xml` | `configs/ui/inventory_new.xml:133` | `<texture>` | прочее |
| `ui_scale_red_1` | `ui\ui_inventory2` | 9x128 | `configs/ui/ui_inventory2.xml` | `configs/ui/inventory_new.xml:138` | `<texture>` | прочее |
| `ui_scale_yellow_1` | `ui\ui_inventory2` | 9x128 | `configs/ui/ui_inventory2.xml` | `configs/ui/inventory_new.xml:150` | `<texture>` | прочее |
| `ui_slots_belt` | `ui\ui_inventory2` | 1024x171 | `configs/ui/ui_inventory2.xml` | `configs/ui/inventory_new.xml:14` | `<texture>` | прочее |
| `ui_sm_mapQuest_artefact` | `ui\ui_common` | 13x13 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:118` | `<texture>` | прочее |
| `ui_sm_mapQuest_gold` | `ui\ui_common` | 13x13 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:246` | `<texture>` | прочее |
| `ui_sm_mapQuest_item` | `ui\ui_common` | 13x13 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:157` | `<texture>` | прочее |
| `ui_sm_mapQuest_monster_find` | `ui\ui_common` | 13x13 | `configs/ui/ui_common.xml` | `configs/ui/map_spots.xml:144` | `<texture>` | прочее |
| `ui_statOne_b` | `ui\ui_statistics` | 447x58 | `configs/ui/ui_statistics.xml` | `configs/ui/stats.xml:24` | `<texture>` | прочее |
| `ui_statTwo_b` | `ui\ui_statistics` | 839x58 | `configs/ui/ui_statistics.xml` | `configs/ui/stats.xml:12` | `<texture>` | прочее |

---

## Каталог родных элементов интерфейса: Чистое Небо (CS / ЧН)

### 1. Архитектура UI в Чистом Небе

В S.T.A.L.K.E.R.: Clear Sky (движок X-Ray 1.5) графический интерфейс существенно доработан по сравнению с ТЧ.

Ключевые особенности и отличия:
1. **Файлы описания текстур**: Все XML-файлы с описанием текстур `<ui_texture>` перенесены в поддиректорию `configs/ui/textures_descr/*.xml`.
2. **Скриптовая инициализация шкал**: В класс `CScriptXmlInit` добавлен метод `InitProgressBar(path, parent)` (`gamedata_cs/scripts/lua_help.script:5783`), что устранило ограничение ТЧ и позволило создавать шкалы прогресса прямо из Lua.
3. **Списки**: Для списков по-прежнему используется `CUIListWnd` (`lua_help.script:6562`) и `InitList` (`lua_help.script:5779`).
4. **Управление окнами**: Открытие и закрытие окон диалогов, как и в ТЧ, выполняется через `start_stop_menu` (`lua_help.script:5810`).
5. **Поддержка широкоформатных экранов**: Появились отдельные XML-файлы для пропорций 16:9 / 16:10 с суффиксом `_16.xml`.

---

### 2. Стандартные элементы управления и их XML-структура

#### 2.1. Кнопка с тремя состояниями (`Init3tButton`)
- **Класс C++**: `CUI3tButton` (`lua_help.script:6377`).
- **Метод инициализации**: `xml:Init3tButton(path, parent)` (`lua_help.script:5798`).
- **Принцип работы состояний**:
  Движок считывает тег `<texture>texture_id</texture>` и ищет текстуры состояний `_e` (Enabled), `_t` (Touched), `_h` (Highlighted), `_d` (Disabled).
  Поддерживается `frame_mode="1"` для масштабируемых 3-секционных кнопок (`UIXmlInitBase.cpp:342`).

**Ванильный пример**: `gamedata_cs/configs/ui/ui_mm_main_16.xml:60-70`:
```xml
<btn_quit x="0" y="270" width="168" height="25">
    <text font="letterica18" align="l">ui_mm_quit_game</text>
    <texture>ui_in_game_menu_back</texture>
    <text_color>
        <e r="216" g="186" b="140"/>
        <h r="255" g="255" b="255"/>
        <t r="150" g="150" b="150"/>
        <d r="100" g="100" b="100"/>
    </text_color>
</btn_quit>
```

---

#### 2.2. Галочка / Чекбокс (`InitCheck`)
- **Класс C++**: `CUICheckButton` (`lua_help.script:6528`).
- **Метод инициализации**: `xml:InitCheck(path, parent)` (`lua_help.script:5793`).

**Ванильный пример**: `gamedata_cs/configs/ui/ui_mm_opt_16.xml:312-315`:
```xml
<check_tips x="20" y="180" width="243" height="21">
    <options_item entry="hud_info" group="mm_opt_gameplay"/>
    <text font="letterica16" r="215" g="195" b="170">ui_mm_tips</text>
</check_tips>
```
Инициализация в скрипте: `gamedata_cs/scripts/ui_mm_opt_gameplay.script:19`:
```lua
xml:InitCheck("tab_gameplay:check_tips", self)
```

---

#### 2.3. Вкладки (`InitTab`)
- **Класс C++**: `CUITabControl` (`lua_help.script:7554`).
- **Метод инициализации**: `xml:InitTab(path, parent)` (`lua_help.script:5777`).

**Ванильный пример**: `gamedata_cs/configs/ui/ui_mm_opt_16.xml:54-70`:
```xml
<tab x="46" y="22" width="384" height="52">
    <button id="video" x="0" y="0" width="89" height="31">
        <texture>ui_tab_button_01</texture>
        <text font="letterica18">ui_mm_video</text>
    </button>
    <button id="sound" x="92" y="0" width="89" height="31">
        <texture>ui_tab_button_02</texture>
        <text font="letterica18">ui_mm_sound</text>
    </button>
    <button id="gameplay" x="184" y="0" width="89" height="31">
        <texture>ui_tab_button_03</texture>
        <text font="letterica18">ui_mm_gameplay</text>
    </button>
    <button id="controls" x="276" y="0" width="89" height="31">
        <texture>ui_tab_button_04</texture>
        <text font="letterica18">ui_mm_controls</text>
    </button>
</tab>
```

---

#### 2.4. Полоса прогресса (`InitProgressBar`)
- **Класс C++**: `CUIProgressBar` (`lua_help.script:6989`).
- **Метод инициализации**: `xml:InitProgressBar(path, parent)` (`lua_help.script:5783`).

**Ванильный пример**: `gamedata_cs/configs/ui/inventory_new.xml:136-140`:
```xml
<progress_bar_health x="8" y="22" width="10" height="124" horz="0" min="0" max="100" pos="0">
    <progress>
        <texture>ui_inGame2_horizontal_progress_bar</texture>
    </progress>
</progress_bar_health>
```

---

#### 2.5. Поле ввода (`InitEditBox`)
- **Класс C++**: `CUIEditBox` (`lua_help.script:6539`).
- **Метод инициализации**: `xml:InitEditBox(path, parent)` (`lua_help.script:5792`).

**Ванильный пример**: `gamedata_cs/configs/ui/ui_mm_save_dlg_16.xml:39-44`:
```xml
<edit x="54" y="55" width="204" height="23" file_name_mode="1">
    <text font="letterica18"/>
    <texture>ui_inGame2_edit_box</texture>
</edit>
```
Инициализация в скрипте: `gamedata_cs/scripts/ui_save_dialog.script:45`:
```lua
self.edit_filename = xml:InitEditBox("edit", self)
```

---

### 3. Шрифты в Чистом Небе

#### 3.1. Функции `GetFont*` из `lua_help.script`
В `gamedata_cs/scripts/lua_help.script` экспортированы 11 функций получения шрифтов:
1. `GetFont()` — строка 73
2. `GetFontDI()` — строка 69
3. `GetFontMedium()` — строка 70
4. `GetFontSmall()` — строка 74
5. `GetFontLetterica16Russian()` — строка 71
6. `GetFontLetterica18Russian()` — строка 75
7. `GetFontLetterica25()` — строка 77
8. `GetFontGraffiti19Russian()` — строка 72
9. `GetFontGraffiti22Russian()` — строка 78
10. `GetFontGraffiti32Russian()` — строка 79
11. `GetFontGraffiti50Russian()` — строка 76

#### 3.2. Имена шрифтов в ванильных XML (`font="..."`)
В XML ЧН используются:
`arial_14`, `graffiti19`, `graffiti22`, `graffiti32`, `graffiti50`, `letterica16`, `letterica18`, `letterica25`, `medium`.
Конфигурация начертаний и размеров описана в `configs/fonts.ltx:1-105`.

---

### 4. Каталог текстур родного интерфейса (ЧН)

| ID текстуры | Файл .dds | Размер (WxH) | Где определена | Пример использования в XML | Тег в XML | Роль |
|-------------|-----------|--------------|----------------|----------------------------|-----------|------|
| `mp_background` | `` | 1024x768 | `configs/ui/textures_descr/ui_mp_main.xml` | `configs/ui/ui_mm_mp.xml:18` | `<texture>` | рамка окна |
| `ui_actor_back` | `` | 224x197 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/talk_16.xml:9` | `<texture>` | рамка окна |
| `ui_bm_back` | `` | 1024x768 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_16.xml:26` | `<texture>` | рамка окна |
| `ui_bt_upgrade_border` | `` | 105x56 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:28` | `<border>` | рамка окна |
| `ui_frame_mainDes` | `` | 749x543 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector.xml:5` | `<texture>` | рамка окна |
| `ui_inGame2_back_01` | `` | 1024x768 | `configs/ui/textures_descr/ui_ingame2_back_01.xml` | `configs/ui/ui_mm_opt_16.xml:9` | `<texture>` | рамка окна |
| `ui_inGame2_back_01w10_l` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_mm_opt_16.xml:13` | `<texture>` | рамка окна |
| `ui_inGame2_back_01w10_r` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_mm_opt_16.xml:17` | `<texture>` | рамка окна |
| `ui_inGame2_back_02` | `` | 1024x768 | `configs/ui/textures_descr/ui_ingame2_back_02.xml` | `configs/ui/ui_mm_save_dlg_16.xml:11` | `<texture>` | рамка окна |
| `ui_inGame2_back_02w10_l` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_mm_save_dlg_16.xml:14` | `<texture>` | рамка окна |
| `ui_inGame2_back_02w10_r` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_mm_save_dlg_16.xml:17` | `<texture>` | рамка окна |
| `ui_inGame2_back_03` | `` | 1024x768 | `configs/ui/textures_descr/ui_ingame2_back_03.xml` | `configs/ui/ui_credits_16.xml:9` | `<texture>` | рамка окна |
| `ui_inGame2_back_03w10_l` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_credits_16.xml:13` | `<texture>` | рамка окна |
| `ui_inGame2_back_03w10_r` | `` | 101x768 | `configs/ui/textures_descr/ui_ingame2_back_add_w.xml` | `configs/ui/ui_credits_16.xml:16` | `<texture>` | рамка окна |
| `ui_inGame2_back_invw10_l` | `` | 102x768 | `configs/ui/textures_descr/ui_ingame2_back_add3_w.xml` | `configs/ui/inventory_upgrade_16.xml:7` | `<texture>` | рамка окна |
| `ui_inGame2_back_invw10_r` | `` | 102x768 | `configs/ui/textures_descr/ui_ingame2_back_add3_w.xml` | `configs/ui/actor_menu_16.xml:8` | `<texture>` | рамка окна |
| `ui_inGame2_back_mmw10_l` | `` | 102x768 | `configs/ui/textures_descr/ui_ingame2_back_add2_w.xml` | `configs/ui/ui_mm_main_16.xml:13` | `<texture>` | рамка окна |
| `ui_inGame2_back_mmw10_r` | `` | 102x768 | `configs/ui/textures_descr/ui_ingame2_back_add2_w.xml` | `configs/ui/ui_mm_main_16.xml:16` | `<texture>` | рамка окна |
| `ui_inGame2_back_pdaw10_l` | `` | 102x768 | `configs/ui/textures_descr/ui_ingame2_back_add3_w.xml` | `configs/ui/pda_16.xml:5` | `<texture>` | рамка окна |
| `ui_inGame2_back_pdaw10_r` | `` | 102x768 | `configs/ui/textures_descr/ui_ingame2_back_add3_w.xml` | `configs/ui/pda_16.xml:8` | `<texture>` | рамка окна |
| `ui_inGame2_character_border` | `` | 165x110 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:20` | `<texture>` | рамка окна |
| `ui_inGame2_inventory_back` | `` | 725x768 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:4` | `<texture>` | рамка окна |
| `ui_inv_mechanic_back` | `` | 331x627 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/inventory_upgrade_16.xml:14` | `<texture>` | рамка окна |
| `ui_item_count_back` | `` | 29x16 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu_item.xml:217` | `<texture>` | рамка окна |
| `ui_menu2_backgraund` | `` | 1024x768 | `configs/ui/textures_descr/ui_mainmenu2.xml` | `configs/ui/ui_mm_main_16.xml:9` | `<texture>` | рамка окна |
| `ui_mm_loading_screen` | `` | 1024x768 | `configs/ui/textures_descr/ui_mm_loading_screen.xml` | `configs/ui/ui_mm_loading_screen.xml:4` | `<texture>` | рамка окна |
| `ui_numpad_lockframe` | `` | 339x369 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:5` | `<texture>` | рамка окна |
| `ui_patch_back` | `` | 603x51 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_mm_opt_16.xml:941` | `<texture>` | рамка окна |
| `ui_statOne_back` | `` | 440x163 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:21` | `<texture>` | рамка окна |
| `ui_statTwo_back` | `` | 845x162 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:9` | `<texture>` | рамка окна |
| `ui_string_statTwo_back` | `` | 32x15 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/ui_team_panels_dm.xml:91` | `<texture>` | рамка окна |
| `ui_watch_back` | `` | 108x47 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:180` | `<texture>` | рамка окна |
| `mp_stats_icon_c_deaths` | `` | 25x21 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_team_panels_dm.xml:48` | `<texture>` | кнопка |
| `ui_TV_Skin` | `` | 666x273 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector.xml:11` | `<texture>` | кнопка |
| `ui_TV_Skin_button_l_d` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector.xml:36` | `<texture[auto-_d]>` | кнопка |
| `ui_TV_Skin_button_l_e` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector.xml:36` | `<texture[auto-_e]>` | кнопка |
| `ui_TV_Skin_button_l_h` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector.xml:36` | `<texture[auto-_h]>` | кнопка |
| `ui_TV_Skin_button_l_t` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector.xml:36` | `<texture[auto-_t]>` | кнопка |
| `ui_TV_Skin_button_r_d` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector.xml:39` | `<texture[auto-_d]>` | кнопка |
| `ui_TV_Skin_button_r_e` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector.xml:39` | `<texture[auto-_e]>` | кнопка |
| `ui_TV_Skin_button_r_h` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector.xml:39` | `<texture[auto-_h]>` | кнопка |
| `ui_TV_Skin_button_r_t` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector.xml:39` | `<texture[auto-_t]>` | кнопка |
| `ui_TV_descr_b` | `` | 220x277 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/map_desc.xml:10` | `<texture>` | кнопка |
| `ui_TV_descr_back` | `` | 224x277 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/map_desc.xml:13` | `<texture>` | кнопка |
| `ui_TV_descr_e` | `` | 220x277 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/map_desc.xml:16` | `<texture>` | кнопка |
| `ui_TV_team_bottom` | `` | 664x208 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/spawn_16.xml:16` | `<texture>` | кнопка |
| `ui_TV_team_tl` | `` | 332x57 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/spawn_16.xml:10` | `<texture>` | кнопка |
| `ui_TV_team_tr` | `` | 332x57 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/spawn_16.xml:13` | `<texture>` | кнопка |
| `ui_am_prop_electro` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/booster_params.xml:108` | `<texture>` | кнопка |
| `ui_am_prop_thermo` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/booster_params.xml:98` | `<texture>` | кнопка |
| `ui_am_prop_time_period` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/booster_params.xml:158` | `<texture>` | кнопка |
| `ui_beltbut_aim_d` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:240` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_aim_e` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:240` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_aim_h` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:240` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_aim_t` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:240` | `<texture[auto-_t]>` | кнопка |
| `ui_beltbut_granadeBig_d` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:230` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_granadeBig_e` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:230` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_granadeBig_h` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:230` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_granadeBig_t` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:230` | `<texture[auto-_t]>` | кнопка |
| `ui_beltbut_granade_d` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:245` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_granade_e` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:245` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_granade_h` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:245` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_granade_t` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:245` | `<texture[auto-_t]>` | кнопка |
| `ui_beltbut_patrons_d` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:215` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_patrons_e` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:215` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_patrons_h` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:215` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_patrons_t` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:215` | `<texture[auto-_t]>` | кнопка |
| `ui_beltbut_silencer_d` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:220` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_silencer_e` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:220` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_silencer_h` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:220` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_silencer_t` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:220` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_back_to_buy_d` | `` | 47x41 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:119` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_back_to_buy_e` | `` | 47x41 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:119` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_back_to_buy_h` | `` | 47x41 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:119` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_back_to_buy_t` | `` | 47x41 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:119` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_equp_01_d` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_equp_01_e` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_equp_01_h` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_equp_01_t` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_equp_02_d` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_equp_02_e` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_equp_02_h` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_equp_02_t` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_equp_03_d` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_equp_03_e` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_equp_03_h` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_equp_03_t` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_equp_04_d` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_equp_04_e` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_equp_04_h` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_equp_04_t` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_equp_05_d` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_equp_05_e` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_equp_05_h` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_equp_05_t` | `` | 68x106 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_t]>` | кнопка |
| `ui_bm_save_preset_d` | `` | 37x25 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:180` | `<texture[auto-_d]>` | кнопка |
| `ui_bm_save_preset_e` | `` | 37x25 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:180` | `<texture[auto-_e]>` | кнопка |
| `ui_bm_save_preset_h` | `` | 37x25 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:180` | `<texture[auto-_h]>` | кнопка |
| `ui_bm_save_preset_t` | `` | 37x25 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:180` | `<texture[auto-_t]>` | кнопка |
| `ui_bt_repair_d` | `` | 56x57 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:23` | `<texture[auto-_d]>` | кнопка |
| `ui_bt_repair_e` | `` | 56x57 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:23` | `<texture[auto-_e]>` | кнопка |
| `ui_bt_repair_h` | `` | 56x57 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:23` | `<texture[auto-_h]>` | кнопка |
| `ui_bt_repair_t` | `` | 56x57 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:23` | `<texture[auto-_t]>` | кнопка |
| `ui_bt_upgrade_dissabled` | `` | 70x40 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:64` | `<back_texture>` | кнопка |
| `ui_bt_upgrade_taken` | `` | 70x40 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:49` | `<back_texture>` | кнопка |
| `ui_btn_assault_d` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:51` | `<texture[auto-_d]>` | кнопка |
| `ui_btn_assault_e` | `` | 256x100 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/mp_buy_menu_buttons.xml:51` | `<texture[auto-_e]>` | кнопка |
| `ui_btn_assault_h` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:51` | `<texture[auto-_h]>` | кнопка |
| `ui_btn_assault_t` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:51` | `<texture[auto-_t]>` | кнопка |
| `ui_btn_heavy_d` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:97` | `<texture[auto-_d]>` | кнопка |
| `ui_btn_heavy_e` | `` | 256x100 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/mp_buy_menu_buttons.xml:97` | `<texture[auto-_e]>` | кнопка |
| `ui_btn_heavy_h` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:97` | `<texture[auto-_h]>` | кнопка |
| `ui_btn_heavy_t` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:97` | `<texture[auto-_t]>` | кнопка |
| `ui_btn_shotgun_d` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:28` | `<texture[auto-_d]>` | кнопка |
| `ui_btn_shotgun_e` | `` | 256x100 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/mp_buy_menu_buttons.xml:28` | `<texture[auto-_e]>` | кнопка |
| `ui_btn_shotgun_h` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:28` | `<texture[auto-_h]>` | кнопка |
| `ui_btn_shotgun_t` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:28` | `<texture[auto-_t]>` | кнопка |
| `ui_btn_sniper_d` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:74` | `<texture[auto-_d]>` | кнопка |
| `ui_btn_sniper_e` | `` | 256x100 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/mp_buy_menu_buttons.xml:74` | `<texture[auto-_e]>` | кнопка |
| `ui_btn_sniper_h` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:74` | `<texture[auto-_h]>` | кнопка |
| `ui_btn_sniper_t` | `` | 256x100 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_buttons.xml:74` | `<texture[auto-_t]>` | кнопка |
| `ui_button_arrow_down_d` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:98` | `<texture[auto-_d]>` | кнопка |
| `ui_button_arrow_down_e` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:98` | `<texture[auto-_e]>` | кнопка |
| `ui_button_arrow_down_h` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:98` | `<texture[auto-_h]>` | кнопка |
| `ui_button_arrow_down_t` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:98` | `<texture[auto-_t]>` | кнопка |
| `ui_button_arrow_left_d` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:86` | `<texture[auto-_d]>` | кнопка |
| `ui_button_arrow_left_e` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:86` | `<texture[auto-_e]>` | кнопка |
| `ui_button_arrow_left_h` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:86` | `<texture[auto-_h]>` | кнопка |
| `ui_button_arrow_left_t` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:86` | `<texture[auto-_t]>` | кнопка |
| `ui_button_arrow_right_d` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:90` | `<texture[auto-_d]>` | кнопка |
| `ui_button_arrow_right_e` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:90` | `<texture[auto-_e]>` | кнопка |
| `ui_button_arrow_right_h` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:90` | `<texture[auto-_h]>` | кнопка |
| `ui_button_arrow_right_t` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:90` | `<texture[auto-_t]>` | кнопка |
| `ui_button_arrow_up_d` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:94` | `<texture[auto-_d]>` | кнопка |
| `ui_button_arrow_up_e` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:94` | `<texture[auto-_e]>` | кнопка |
| `ui_button_arrow_up_h` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:94` | `<texture[auto-_h]>` | кнопка |
| `ui_button_arrow_up_t` | `` | 44x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:94` | `<texture[auto-_t]>` | кнопка |
| `ui_button_main01_d` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector.xml:56` | `<texture[auto-_d]>` | кнопка |
| `ui_button_main01_e` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector.xml:56` | `<texture[auto-_e]>` | кнопка |
| `ui_button_main01_h` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector.xml:56` | `<texture[auto-_h]>` | кнопка |
| `ui_button_main01_t` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector.xml:56` | `<texture[auto-_t]>` | кнопка |
| `ui_button_main02_d` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_d]>` | кнопка |
| `ui_button_main02_e` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_e]>` | кнопка |
| `ui_button_main02_h` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_h]>` | кнопка |
| `ui_button_main02_t` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_t]>` | кнопка |
| `ui_button_main03_d` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector.xml:76` | `<texture[auto-_d]>` | кнопка |
| `ui_button_main03_e` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector.xml:76` | `<texture[auto-_e]>` | кнопка |
| `ui_button_main03_h` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector.xml:76` | `<texture[auto-_h]>` | кнопка |
| `ui_button_main03_t` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector.xml:76` | `<texture[auto-_t]>` | кнопка |
| `ui_button_ordinary_d` | `` | 117x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/message_box_16.xml:17` | `<texture[auto-_d]>` | кнопка |
| `ui_button_ordinary_e` | `` | 117x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/message_box_16.xml:17` | `<texture[auto-_e]>` | кнопка |
| `ui_button_ordinary_h` | `` | 117x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/message_box_16.xml:17` | `<texture[auto-_h]>` | кнопка |
| `ui_button_ordinary_t` | `` | 117x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/message_box_16.xml:17` | `<texture[auto-_t]>` | кнопка |
| `ui_car_panel_engine` | `` | 22x22 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/car_panel.xml:37` | `<texture>` | кнопка |
| `ui_char_health` | `` | 209x9 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:309` | `<texture>` | кнопка |
| `ui_cur_task` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_tasks.xml:275` | `<texture>` | кнопка |
| `ui_date_back_tile` | `` | 23x23 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_logs_16.xml:112` | `<texture>` | кнопка |
| `ui_date_bt_left_d` | `` | 25x23 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_logs_16.xml:118` | `<texture[auto-_d]>` | кнопка |
| `ui_date_bt_left_e` | `` | 25x23 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_logs_16.xml:118` | `<texture[auto-_e]>` | кнопка |
| `ui_date_bt_left_h` | `` | 25x23 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_logs_16.xml:118` | `<texture[auto-_h]>` | кнопка |
| `ui_date_bt_left_t` | `` | 25x23 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_logs_16.xml:118` | `<texture[auto-_t]>` | кнопка |
| `ui_date_bt_right_d` | `` | 25x23 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_logs_16.xml:123` | `<texture[auto-_d]>` | кнопка |
| `ui_date_bt_right_e` | `` | 25x23 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_logs_16.xml:123` | `<texture[auto-_e]>` | кнопка |
| `ui_date_bt_right_h` | `` | 25x23 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_logs_16.xml:123` | `<texture[auto-_h]>` | кнопка |
| `ui_date_bt_right_t` | `` | 25x23 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_logs_16.xml:123` | `<texture[auto-_t]>` | кнопка |
| `ui_dlg_answer` | `` | 580x193 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/talk_16.xml:53` | `<texture>` | кнопка |
| `ui_dlg_bottom` | `` | 1024x32 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/talk_16.xml:4` | `<texture>` | кнопка |
| `ui_dlg_left_app` | `` | 33x220 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/talk_16.xml:47` | `<texture>` | кнопка |
| `ui_dlg_top_middle` | `` | 680x524 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/talk_16.xml:44` | `<texture>` | кнопка |
| `ui_flag_russia_cs_d` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:128` | `<texture[auto-_d]>` | кнопка |
| `ui_flag_russia_cs_e` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:128` | `<texture[auto-_e]>` | кнопка |
| `ui_flag_russia_cs_h` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:128` | `<texture[auto-_h]>` | кнопка |
| `ui_flag_russia_cs_t` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:128` | `<texture[auto-_t]>` | кнопка |
| `ui_flag_usa_cs_d` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:124` | `<texture[auto-_d]>` | кнопка |
| `ui_flag_usa_cs_e` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:124` | `<texture[auto-_e]>` | кнопка |
| `ui_flag_usa_cs_h` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:124` | `<texture[auto-_h]>` | кнопка |
| `ui_flag_usa_cs_t` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:124` | `<texture[auto-_t]>` | кнопка |
| `ui_frame_01_t` | `` | 32x32 | `configs/ui/textures_descr/ui_old_textures.xml` | `configs/ui/ui_game_dm.xml:3` | `<texture[auto-_t]>` | кнопка |
| `ui_frame_03_t` | `` | 128x64 | `configs/ui/textures_descr/ui_old_textures.xml` | `configs/ui/trade.xml:85` | `<texture[auto-_t]>` | кнопка |
| `ui_frame_error` | `` | 510x206 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/message_box_16.xml:4` | `<texture>` | кнопка |
| `ui_frame_error_sign_alarm` | `` | 77x77 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/message_box_16.xml:7` | `<texture>` | кнопка |
| `ui_frame_error_sign_info` | `` | 77x77 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/message_box_16.xml:130` | `<texture>` | кнопка |
| `ui_frame_error_sign_red` | `` | 77x77 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/message_box_16.xml:48` | `<texture>` | кнопка |
| `ui_frame_t` | `` | 128x128 | `configs/ui/textures_descr/ui_old_textures.xml` | `configs/ui/stats.xml:93` | `<texture[auto-_t]>` | кнопка |
| `ui_highlight` | `` | 128x128 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/game_tutorial_pda_16.xml:13` | `<texture>` | кнопка |
| `ui_hud2_rad01_arrow` | `` | 9x97 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:101` | `<texture>` | кнопка |
| `ui_hud2_rad01_arrow_shadow` | `` | 9x97 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:104` | `<texture>` | кнопка |
| `ui_hud2_rad01_back` | `` | 266x121 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:87` | `<texture>` | кнопка |
| `ui_hud2_rad01_back2v` | `` | 23x219 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:91` | `<texture>` | кнопка |
| `ui_hud2_rad01_over` | `` | 66x51 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:94` | `<texture>` | кнопка |
| `ui_hud2_res_acid` | `` | 22x22 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:140` | `<texture>` | кнопка |
| `ui_hud2_res_back` | `` | 28x28 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:118` | `<texture>` | кнопка |
| `ui_hud2_res_eat` | `` | 22x22 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:146` | `<texture>` | кнопка |
| `ui_hud2_res_fire` | `` | 22x22 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:137` | `<texture>` | кнопка |
| `ui_hud2_res_psi` | `` | 22x22 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:143` | `<texture>` | кнопка |
| `ui_hud2_res_rad` | `` | 22x22 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:134` | `<texture>` | кнопка |
| `ui_hud2_stamina_bk` | `` | 103x37 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:96` | `<texture>` | кнопка |
| `ui_hud_button_voting_01_d` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:30` | `<texture[auto-_d]>` | кнопка |
| `ui_hud_button_voting_01_e` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:30` | `<texture[auto-_e]>` | кнопка |
| `ui_hud_button_voting_01_h` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:30` | `<texture[auto-_h]>` | кнопка |
| `ui_hud_button_voting_01_t` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:30` | `<texture[auto-_t]>` | кнопка |
| `ui_hud_button_voting_02_d` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:40` | `<texture[auto-_d]>` | кнопка |
| `ui_hud_button_voting_02_e` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:40` | `<texture[auto-_e]>` | кнопка |
| `ui_hud_button_voting_02_h` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:40` | `<texture[auto-_h]>` | кнопка |
| `ui_hud_button_voting_02_t` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:40` | `<texture[auto-_t]>` | кнопка |
| `ui_hud_button_voting_03_d` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:50` | `<texture[auto-_d]>` | кнопка |
| `ui_hud_button_voting_03_e` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:50` | `<texture[auto-_e]>` | кнопка |
| `ui_hud_button_voting_03_h` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:50` | `<texture[auto-_h]>` | кнопка |
| `ui_hud_button_voting_03_t` | `` | 37x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:50` | `<texture[auto-_t]>` | кнопка |
| `ui_hud_compas` | `` | 31x31 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/zone_map.xml:14` | `<texture>` | кнопка |
| `ui_hud_fragBig` | `` | 280x45 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:106` | `<texture>` | кнопка |
| `ui_hud_frame_money` | `` | 99x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:49` | `<texture>` | кнопка |
| `ui_hud_frame_rank` | `` | 60x65 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:65` | `<texture>` | кнопка |
| `ui_hud_frame_voting` | `` | 600x371 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:5` | `<texture>` | кнопка |
| `ui_hud_frame_voting_dop2` | `` | 573x39 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/voting_category_16.xml:296` | `<texture>` | кнопка |
| `ui_hud_grenadetarget_d` | `` | 91x92 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/grenade.xml:9` | `<texture>` | кнопка |
| `ui_hud_grenadetarget_e` | `` | 91x92 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/grenade.xml:6` | `<texture>` | кнопка |
| `ui_hud_icon_PDA` | `` | 48x29 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:76` | `<texture>` | кнопка |
| `ui_hud_icon_armour` | `` | 19x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:21` | `<texture>` | кнопка |
| `ui_hud_icon_artefact` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:67` | `<texture>` | кнопка |
| `ui_hud_icon_drop` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:47` | `<texture>` | кнопка |
| `ui_hud_icon_eat` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:51` | `<texture>` | кнопка |
| `ui_hud_icon_goodmode` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:59` | `<texture>` | кнопка |
| `ui_hud_icon_psycho` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:55` | `<texture>` | кнопка |
| `ui_hud_icon_radiation` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:43` | `<texture>` | кнопка |
| `ui_hud_icon_sleep` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:63` | `<texture>` | кнопка |
| `ui_hud_icon_weapon` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:39` | `<texture>` | кнопка |
| `ui_hud_map` | `` | 197x191 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/zone_map.xml:6` | `<texture>` | кнопка |
| `ui_hud_map_arrow` | `` | 11x24 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/map_spots.xml:9` | `<texture>` | кнопка |
| `ui_hud_map_counter` | `` | 35x29 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/zone_map.xml:22` | `<texture>` | кнопка |
| `ui_hud_points_count` | `` | 224x45 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_custom_msgs.xml:174` | `<texture>` | кнопка |
| `ui_hud_shk_armour` | `` | 114x9 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:27` | `<texture>` | кнопка |
| `ui_hud_shk_health` | `` | 114x9 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:13` | `<texture>` | кнопка |
| `ui_hud_shk_light` | `` | 10x71 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/motion_icon.xml:41` | `<texture>` | кнопка |
| `ui_hud_shk_noise` | `` | 10x71 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/motion_icon.xml:47` | `<texture>` | кнопка |
| `ui_hud_shk_stamina` | `` | 57x7 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/motion_icon.xml:35` | `<texture>` | кнопка |
| `ui_hud_shk_stamina_new` | `` | 68x14 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:171` | `<texture>` | кнопка |
| `ui_hud_shkala_armor` | `` | 165x28 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:19` | `<texture>` | кнопка |
| `ui_hud_soldier_climb` | `` | 65x75 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/motion_icon.xml:22` | `<texture>` | кнопка |
| `ui_hud_soldier_creep` | `` | 65x75 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/motion_icon.xml:18` | `<texture>` | кнопка |
| `ui_hud_soldier_crouch` | `` | 65x75 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/motion_icon.xml:14` | `<texture>` | кнопка |
| `ui_hud_soldier_normal` | `` | 65x75 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/motion_icon.xml:9` | `<texture>` | кнопка |
| `ui_hud_soldier_run` | `` | 65x75 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/motion_icon.xml:26` | `<texture>` | кнопка |
| `ui_hud_soldier_sprint` | `` | 65x75 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/motion_icon.xml:30` | `<texture>` | кнопка |
| `ui_hud_stamina_full` | `` | 125x125 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/motion_icon.xml:5` | `<texture>` | кнопка |
| `ui_hud_status_blue_01` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:84` | `<texture>` | кнопка |
| `ui_hud_status_blue_02` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:87` | `<texture>` | кнопка |
| `ui_hud_status_blue_03` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:90` | `<texture>` | кнопка |
| `ui_hud_status_blue_04` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:93` | `<texture>` | кнопка |
| `ui_hud_status_blue_05` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:96` | `<texture>` | кнопка |
| `ui_hud_status_green_01` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:69` | `<texture>` | кнопка |
| `ui_hud_status_green_02` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:72` | `<texture>` | кнопка |
| `ui_hud_status_green_03` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:75` | `<texture>` | кнопка |
| `ui_hud_status_green_04` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:78` | `<texture>` | кнопка |
| `ui_hud_status_green_05` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:81` | `<texture>` | кнопка |
| `ui_hud_teamF_counter` | `` | 36x70 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:9` | `<texture>` | кнопка |
| `ui_hud_teamF_counterC` | `` | 33x33 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:12` | `<texture>` | кнопка |
| `ui_hud_teamF_leftS` | `` | 38x43 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:18` | `<texture>` | кнопка |
| `ui_hud_teamF_rightS` | `` | 38x43 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:25` | `<texture>` | кнопка |
| `ui_hud_timer_games` | `` | 155x66 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_custom_msgs.xml:5` | `<texture>` | кнопка |
| `ui_icons_PDA_dialog_t` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_tasks.xml:206` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_PDA_tooltips_t` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_16.xml:35` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_mapPDA_mark_t` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:74` | `<texture>` | кнопка |
| `ui_icons_mapPDA_persBig_e` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:48` | `<texture>` | кнопка |
| `ui_icons_mapPDA_persBig_h` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:61` | `<texture>` | кнопка |
| `ui_icons_newPDA_CrclMiddle_h` | `` | 62x62 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/game_tutorials.xml:46` | `<texture>` | кнопка |
| `ui_icons_newPDA_Crclbig_h` | `` | 115x115 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/maingame_16.xml:4` | `<texture>` | кнопка |
| `ui_icons_newPDA_perssign_h` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/actor_menu_item.xml:222` | `<texture>` | кнопка |
| `ui_inGame2_armor_highlighter` | `` | 96x161 | `configs/ui/textures_descr/ui_actor_menu_cop.xml` | `configs/ui/actor_menu.xml:93` | `<texture>` | кнопка |
| `ui_inGame2_artefakt_highlighter` | `` | 52x52 | `configs/ui/textures_descr/ui_actor_menu_cop.xml` | `configs/ui/actor_menu.xml:121` | `<texture>` | кнопка |
| `ui_inGame2_detector_highlighter` | `` | 96x48 | `configs/ui/textures_descr/ui_actor_menu_cop.xml` | `configs/ui/actor_menu.xml:69` | `<texture>` | кнопка |
| `ui_inGame2_hint_wnd_Properties` | `` | 242x7 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu_item.xml:133` | `<texture>` | кнопка |
| `ui_inGame2_left_top` | `` | 331x142 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:4` | `<texture>` | кнопка |
| `ui_inGame2_weapon_highlighter` | `` | 82x353 | `configs/ui/textures_descr/ui_actor_menu_cop.xml` | `configs/ui/actor_menu.xml:78` | `<texture>` | кнопка |
| `ui_inv_icon_explosion_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:110` | `<texture>` | кнопка |
| `ui_inv_icon_health_restore_speed` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/af_params.xml:14` | `<texture>` | кнопка |
| `ui_inv_icon_telepatic_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:100` | `<texture>` | кнопка |
| `ui_menu2_highlight_one` | `` | 206x24 | `configs/ui/textures_descr/ui_mainmenu2.xml` | `configs/ui/ui_mm_main_16.xml:56` | `<texture>` | кнопка |
| `ui_menu2_move_e` | `` | 278x40 | `configs/ui/textures_descr/ui_mainmenu2.xml` | `configs/ui/ui_mm_main_16.xml:50` | `<texture>` | кнопка |
| `ui_menu_options_dlg` | `` | 563x459 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_save_dlg_16.xml:33` | `<texture>` | кнопка |
| `ui_mmap_secondary_task` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:585` | `<texture>` | кнопка |
| `ui_mnav_bt_center_d` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:174` | `<texture[auto-_d]>` | кнопка |
| `ui_mnav_bt_center_e` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:174` | `<texture[auto-_e]>` | кнопка |
| `ui_mnav_bt_center_h` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:174` | `<texture[auto-_h]>` | кнопка |
| `ui_mnav_bt_center_t` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:174` | `<texture[auto-_t]>` | кнопка |
| `ui_mnav_bt_down_d` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:190` | `<texture[auto-_d]>` | кнопка |
| `ui_mnav_bt_down_e` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:190` | `<texture[auto-_e]>` | кнопка |
| `ui_mnav_bt_down_h` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:190` | `<texture[auto-_h]>` | кнопка |
| `ui_mnav_bt_down_t` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:190` | `<texture[auto-_t]>` | кнопка |
| `ui_mnav_bt_left_d` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:169` | `<texture[auto-_d]>` | кнопка |
| `ui_mnav_bt_left_e` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:169` | `<texture[auto-_e]>` | кнопка |
| `ui_mnav_bt_left_h` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:169` | `<texture[auto-_h]>` | кнопка |
| `ui_mnav_bt_left_t` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:169` | `<texture[auto-_t]>` | кнопка |
| `ui_mnav_bt_legend_d` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:153` | `<texture[auto-_d]>` | кнопка |
| `ui_mnav_bt_legend_e` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:153` | `<texture[auto-_e]>` | кнопка |
| `ui_mnav_bt_legend_h` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:153` | `<texture[auto-_h]>` | кнопка |
| `ui_mnav_bt_legend_t` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:153` | `<texture[auto-_t]>` | кнопка |
| `ui_mnav_bt_right_d` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:179` | `<texture[auto-_d]>` | кнопка |
| `ui_mnav_bt_right_e` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:179` | `<texture[auto-_e]>` | кнопка |
| `ui_mnav_bt_right_h` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:179` | `<texture[auto-_h]>` | кнопка |
| `ui_mnav_bt_right_t` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:179` | `<texture[auto-_t]>` | кнопка |
| `ui_mnav_bt_up_d` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:158` | `<texture[auto-_d]>` | кнопка |
| `ui_mnav_bt_up_e` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:158` | `<texture[auto-_e]>` | кнопка |
| `ui_mnav_bt_up_h` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:158` | `<texture[auto-_h]>` | кнопка |
| `ui_mnav_bt_up_t` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:158` | `<texture[auto-_t]>` | кнопка |
| `ui_mnav_bt_zmreset_d` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:195` | `<texture[auto-_d]>` | кнопка |
| `ui_mnav_bt_zmreset_e` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:195` | `<texture[auto-_e]>` | кнопка |
| `ui_mnav_bt_zmreset_h` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:195` | `<texture[auto-_h]>` | кнопка |
| `ui_mnav_bt_zmreset_t` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:195` | `<texture[auto-_t]>` | кнопка |
| `ui_mnav_bt_zoom_in_d` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:163` | `<texture[auto-_d]>` | кнопка |
| `ui_mnav_bt_zoom_in_e` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:163` | `<texture[auto-_e]>` | кнопка |
| `ui_mnav_bt_zoom_in_h` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:163` | `<texture[auto-_h]>` | кнопка |
| `ui_mnav_bt_zoom_in_t` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:163` | `<texture[auto-_t]>` | кнопка |
| `ui_mnav_bt_zoom_out_d` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:185` | `<texture[auto-_d]>` | кнопка |
| `ui_mnav_bt_zoom_out_e` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:185` | `<texture[auto-_e]>` | кнопка |
| `ui_mnav_bt_zoom_out_h` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:185` | `<texture[auto-_h]>` | кнопка |
| `ui_mnav_bt_zoom_out_t` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:185` | `<texture[auto-_t]>` | кнопка |
| `ui_mp_Voting_tv` | `` | 300x261 | `configs/ui/textures_descr/ui_mp_main.xml` | `configs/ui/voting_category_16.xml:160` | `<texture>` | кнопка |
| `ui_nav_bt_d_e` | `` | 7x27 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_spot.xml:16` | `<texture_e[auto-_e]>` | кнопка |
| `ui_nav_bt_e_e` | `` | 7x27 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_16.xml:47` | `<texture_e[auto-_e]>` | кнопка |
| `ui_nav_bt_h_e` | `` | 7x27 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_spot.xml:17` | `<texture_h[auto-_e]>` | кнопка |
| `ui_nav_bt_t_e` | `` | 7x27 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_spot.xml:15` | `<texture_t[auto-_e]>` | кнопка |
| `ui_pda2_defend_base` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:122` | `<texture>` | кнопка |
| `ui_pda2_defend_base2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:98` | `<texture>` | кнопка |
| `ui_pda2_destroy_enemy2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:195` | `<texture>` | кнопка |
| `ui_pda2_exit_point` | `` | 19x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:115` | `<texture>` | кнопка |
| `ui_pda2_fr2_e` | `` | 955x9 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:7` | `<texture[auto-_e]>` | кнопка |
| `ui_pda2_fr_e` | `` | 955x21 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_fraction_war.xml:5` | `<texture[auto-_e]>` | кнопка |
| `ui_pda2_hl_quest_base` | `` | 37x37 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:84` | `<texture>` | кнопка |
| `ui_pda2_hl_seq_quest2` | `` | 29x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:654` | `<texture>` | кнопка |
| `ui_pda2_mapframe_t` | `` | 28x28 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_tasks.xml:34` | `<texture[auto-_t]>` | кнопка |
| `ui_pda2_place_down` | `` | 9x5 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_ranking.xml:185` | `<texture>` | кнопка |
| `ui_pda2_pt_territory` | `` | 27x27 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:330` | `<texture>` | кнопка |
| `ui_pda2_secondary_task` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:582` | `<texture>` | кнопка |
| `ui_pda2_secondary_task2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:10` | `<texture>` | кнопка |
| `ui_pda2_split_e` | `` | 48x7 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_tasks.xml:27` | `<texture[auto-_e]>` | кнопка |
| `ui_pda2_trader` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:760` | `<texture>` | кнопка |
| `ui_pda_frame2_t` | `` | 32x32 | `configs/ui/textures_descr/ui_old_textures.xml` | `configs/ui/actor_statistic.xml:12` | `<texture[auto-_t]>` | кнопка |
| `ui_pda_frame_sub_t` | `` | 32x32 | `configs/ui/textures_descr/ui_old_textures.xml` | `configs/ui/actor_statistic.xml:25` | `<texture[auto-_t]>` | кнопка |
| `ui_sega_healph` | `` | 325x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_custom_msgs.xml:146` | `<texture>` | кнопка |
| `ui_statGreen_e` | `` | 424x31 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/ui_team_panels_tdm.xml:307` | `<texture[auto-_e]>` | кнопка |
| `ui_statOne_t` | `` | 446x95 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:18` | `<texture>` | кнопка |
| `ui_statSingle_e` | `` | 447x58 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/ui_team_panels_dm.xml:6` | `<texture[auto-_e]>` | кнопка |
| `ui_statTwo2_e` | `` | 845x58 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/ui_team_panels_tdm.xml:10` | `<texture[auto-_e]>` | кнопка |
| `ui_statTwo_e` | `` | 845x25 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/ui_team_panels_tdm.xml:6` | `<texture[auto-_e]>` | кнопка |
| `ui_statTwo_t` | `` | 845x98 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:6` | `<texture>` | кнопка |
| `ui_string_01_e` | `` | 32x32 | `configs/ui/textures_descr/ui_old_textures.xml` | `configs/ui/pda_spot.xml:10` | `<texture[auto-_e]>` | кнопка |
| `ui_string_02_e` | `` | 32x32 | `configs/ui/textures_descr/ui_old_textures.xml` | `configs/ui/actor_statistic.xml:43` | `<texture[auto-_e]>` | кнопка |
| `ui_string_03_e` | `` | 32x32 | `configs/ui/textures_descr/ui_old_textures.xml` | `configs/ui/pda_tasks.xml:48` | `<texture[auto-_e]>` | кнопка |
| `ui_stroketextbox_t` | `` | 10x10 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_spot.xml:4` | `<texture[auto-_t]>` | кнопка |
| `ui_task_bt_center_d` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:139` | `<texture[auto-_d]>` | кнопка |
| `ui_task_bt_center_e` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:139` | `<texture[auto-_e]>` | кнопка |
| `ui_task_bt_center_h` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:139` | `<texture[auto-_h]>` | кнопка |
| `ui_task_bt_center_t` | `` | 21x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:139` | `<texture[auto-_t]>` | кнопка |
| `ui_task_bt_close_d` | `` | 15x15 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:223` | `<texture[auto-_d]>` | кнопка |
| `ui_task_bt_close_e` | `` | 15x15 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:223` | `<texture[auto-_e]>` | кнопка |
| `ui_task_bt_close_h` | `` | 15x15 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:223` | `<texture[auto-_h]>` | кнопка |
| `ui_task_bt_close_t` | `` | 15x15 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:223` | `<texture[auto-_t]>` | кнопка |
| `ui_task_bt_left_d` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:81` | `<texture[auto-_d]>` | кнопка |
| `ui_task_bt_left_e` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:81` | `<texture[auto-_e]>` | кнопка |
| `ui_task_bt_left_h` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:81` | `<texture[auto-_h]>` | кнопка |
| `ui_task_bt_left_t` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:81` | `<texture[auto-_t]>` | кнопка |
| `ui_task_bt_right_d` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:85` | `<texture[auto-_d]>` | кнопка |
| `ui_task_bt_right_e` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:85` | `<texture[auto-_e]>` | кнопка |
| `ui_task_bt_right_h` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:85` | `<texture[auto-_h]>` | кнопка |
| `ui_task_bt_right_t` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:85` | `<texture[auto-_t]>` | кнопка |
| `ui_task_bt_tslist_d` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:90` | `<texture[auto-_d]>` | кнопка |
| `ui_task_bt_tslist_e` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:90` | `<texture[auto-_e]>` | кнопка |
| `ui_task_bt_tslist_h` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:90` | `<texture[auto-_h]>` | кнопка |
| `ui_task_bt_tslist_t` | `` | 40x33 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:90` | `<texture[auto-_t]>` | кнопка |
| `ui_task_bt_visible_d` | `` | 24x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:239` | `<texture[auto-_d]>` | кнопка |
| `ui_task_bt_visible_e` | `` | 24x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:239` | `<texture[auto-_e]>` | кнопка |
| `ui_task_bt_visible_h` | `` | 24x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:239` | `<texture[auto-_h]>` | кнопка |
| `ui_task_bt_visible_t` | `` | 24x21 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:239` | `<texture[auto-_t]>` | кнопка |
| `ui_teambase` | `` | 29x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_mp.xml:59` | `<texture>` | кнопка |
| `ui_temp_ad3_artefact` | `` | 5x5 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/ui_detector_artefact.xml:10` | `<texture>` | кнопка |
| `ui_temp_ad3_radar_glow` | `` | 89x44 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/ui_detector_artefact.xml:5` | `<texture>` | кнопка |
| `ui_temp_frame_t` | `` | 50x50 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:270` | `<texture[auto-_t]>` | кнопка |
| `ui_wp_prop_damage` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/actor_menu_item.xml:52` | `<texture>` | кнопка |
| `ui_wp_prop_distantion` | `` | 19x19 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/actor_menu_item.xml:142` | `<texture>` | кнопка |
| `ui_wp_prop_ergonomics` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/actor_menu_item.xml:49` | `<texture>` | кнопка |
| `ui_wp_prop_tochnost` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/actor_menu_item.xml:46` | `<texture>` | кнопка |
| `ui_PDA_checker_d` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_logs_16.xml:89` | `<texture[auto-_d]>` | галочка |
| `ui_PDA_checker_e` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_logs_16.xml:89` | `<texture[auto-_e]>` | галочка |
| `ui_PDA_checker_h` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_logs_16.xml:89` | `<texture[auto-_h]>` | галочка |
| `ui_PDA_checker_t` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_logs_16.xml:89` | `<texture[auto-_t]>` | галочка |
| `ui_cb_listbox_t` | `` | 10x10 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/inventory_new.xml:4` | `<texture[auto-_t]>` | фон списка |
| `ui_scroll_PDA_back` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:41` | `<texture>` | фон списка |
| `ui_scroll_PDA_back_16` | `` | 14x14 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar_16.xml:44` | `<texture>` | фон списка |
| `ui_scroll_PDA_btn_down_e` | `` | 17x30 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:24` | `<texture_e>` | фон списка |
| `ui_scroll_PDA_btn_down_h` | `` | 17x30 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:25` | `<texture_h>` | фон списка |
| `ui_scroll_PDA_btn_down_t` | `` | 17x30 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:26` | `<texture_t>` | фон списка |
| `ui_scroll_PDA_btn_left_e` | `` | 21x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:30` | `<texture_e>` | фон списка |
| `ui_scroll_PDA_btn_left_h` | `` | 21x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:31` | `<texture_h>` | фон списка |
| `ui_scroll_PDA_btn_left_t` | `` | 21x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:32` | `<texture_t>` | фон списка |
| `ui_scroll_PDA_btn_right_e` | `` | 21x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:35` | `<texture_e>` | фон списка |
| `ui_scroll_PDA_btn_right_h` | `` | 21x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:36` | `<texture_h>` | фон списка |
| `ui_scroll_PDA_btn_right_t` | `` | 21x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:37` | `<texture_t>` | фон списка |
| `ui_scroll_PDA_btn_up_e` | `` | 17x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:19` | `<texture_e>` | фон списка |
| `ui_scroll_PDA_btn_up_h` | `` | 17x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:20` | `<texture_h>` | фон списка |
| `ui_scroll_PDA_btn_up_t` | `` | 17x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:21` | `<texture_t>` | фон списка |
| `ui_scroll_PDA_move` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:48` | `<texture>` | фон списка |
| `ui_scroll_PDA_move_16` | `` | 14x14 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar_16.xml:51` | `<texture>` | фон списка |
| `ui_scroll_back` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:10` | `<texture>` | фон списка |
| `ui_scroll_back_16` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar_16.xml:10` | `<texture>` | фон списка |
| `ui_scroll_box` | `` | 15x14 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:13` | `<texture>` | фон списка |
| `ui_scroll_box_16` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar_16.xml:13` | `<texture>` | фон списка |
| `ui_scroll_btn_down` | `` | 15x16 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:7` | `<texture_e>` | фон списка |
| `ui_scroll_btn_up` | `` | 15x16 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:4` | `<texture_e>` | фон списка |
| `ui_button_tablist_d` | `` | 114x44 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:56` | `<texture[auto-_d]>` | вкладка |
| `ui_button_tablist_e` | `` | 114x44 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:56` | `<texture[auto-_e]>` | вкладка |
| `ui_button_tablist_h` | `` | 114x44 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:56` | `<texture[auto-_h]>` | вкладка |
| `ui_button_tablist_t` | `` | 114x44 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:56` | `<texture[auto-_t]>` | вкладка |
| `ui_statOne_tabdiv` | `` | 411x25 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/ui_team_panels_dm.xml:112` | `<texture>` | вкладка |
| `ui_statTwo_tabdiv_l` | `` | 402x25 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:60` | `<texture>` | вкладка |
| `ui_table_button_e_e` | `` | 5x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:46` | `<texture[auto-_e]>` | вкладка |
| `ui_table_button_o_e` | `` | 4x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:28` | `<texture[auto-_e]>` | вкладка |
| `ui_table_divider_e` | `` | 1x10 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:32` | `<texture[auto-_e]>` | вкладка |
| `ui_tablist_divider_e` | `` | 36x30 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:77` | `<texture[auto-_e]>` | вкладка |
| `ui_tablist_textbox_t` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/voting_category_16.xml:147` | `<texture[auto-_t]>` | вкладка |
| `ui_buymenu_progBar` | `` | 151x7 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/buy_menu_item.xml:39` | `<texture>` | полоса прогресса |
| `ui_hud2_rad01_progress` | `` | 96x96 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:113` | `<texture>` | полоса прогресса |
| `ui_mm_loading_progress_bar` | `` | 268x37 | `configs/ui/textures_descr/ui_mm_loading_screen.xml` | `configs/ui/ui_mm_loading_screen.xml:9` | `<texture>` | полоса прогресса |
| `ui_patch_progress` | `` | 403x10 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_mm_opt_16.xml:950` | `<texture>` | полоса прогресса |
| `ui_pda2_big_progress` | `` | 217x57 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/pda_fraction_war.xml:69` | `<texture>` | полоса прогресса |
| `ui_pda2_big_progress2` | `` | 217x57 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/pda_fraction_war.xml:72` | `<texture>` | полоса прогресса |
| `ui_pda2_small_progress` | `` | 432x38 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/pda_fraction_war.xml:86` | `<texture>` | полоса прогресса |
| `ui_pda2_small_progress2` | `` | 432x38 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/pda_fraction_war.xml:89` | `<texture>` | полоса прогресса |
| `ui_sega_healph_progress` | `` | 254x10 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/strelok_progress.xml:5` | `<texture>` | полоса прогресса |
| `ui_brokenline_e` | `` | 9x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:81` | `<texture[auto-_e]>` | разделитель |
| `ui_hud_teamF_line` | `` | 404x4 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:4` | `<texture>` | разделитель |
| `ui_inGame2_delimiter_02` | `` | 336x47 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:146` | `<texture>` | разделитель |
| `ui_inv_delimiter_e` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:142` | `<texture[auto-_e]>` | разделитель |
| `ui_linetextSmall_e_e` | `` | 9x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/message_box_16.xml:166` | `<texture[auto-_e]>` | разделитель |
| `ui_linetext_e_e` | `` | 9x24 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_save_dlg_16.xml:40` | `<texture[auto-_e]>` | разделитель |
| `ui_pda2_delimiter_01` | `` | 10x5 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_ranking.xml:169` | `<texture>` | разделитель |
| `ui_pda2_delimiter_02` | `` | 42x50 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_ranking.xml:166` | `<texture>` | разделитель |
| `ui_pda2_delimiter_03` | `` | 955x31 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:19` | `<texture>` | разделитель |
| `ui_pda2_delimiter_03_bottom` | `` | 21x14 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_tasks.xml:23` | `<texture>` | разделитель |
| `ui_pda2_fr_delimiter_fraction` | `` | 955x103 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_fraction_war.xml:9` | `<texture>` | разделитель |
| `ui_pda2_fr_delimiter_ranking` | `` | 955x67 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_ranking.xml:46` | `<texture>` | разделитель |
| `ui_pda2_line_d_e` | `` | 9x4 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_fraction_war.xml:135` | `<texture[auto-_e]>` | разделитель |
| `ui_pda2_line_h_e` | `` | 9x3 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_fraction_war.xml:132` | `<texture[auto-_e]>` | разделитель |
| `ui_pda2_line_v_e` | `` | 3x11 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_fraction_war.xml:138` | `<texture[auto-_e]>` | разделитель |
| `ui_pda2_line_vl_e` | `` | 4x11 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_ranking.xml:12` | `<texture[auto-_e]>` | разделитель |
| `ui_pda2_line_vr_e` | `` | 4x11 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_ranking.xml:16` | `<texture[auto-_e]>` | разделитель |
| `ui_v_line_bold` | `` | 2x16 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/ui_team_panels_tdm.xml:255` | `<texture>` | разделитель |
| `ui_v_line_normal` | `` | 1x16 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/ui_team_panels_dm.xml:36` | `<texture>` | разделитель |
| `actor_big` | `` | 1x1 | `configs/ui/textures_descr/ui_logos.xml` | `configs/ui/pda_ranking.xml:111` | `<texture>` | прочее |
| `freedom_big` | `` | 249x194 | `configs/ui/textures_descr/ui_logos.xml` | `configs/ui/spawn_16.xml:19` | `<texture>` | прочее |
| `freedom_icon` | `` | 56x54 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/ui_game_ahunt.xml:20` | `<texture>` | прочее |
| `merc_big` | `` | 249x194 | `configs/ui/textures_descr/ui_logos.xml` | `configs/ui/spawn_16.xml:23` | `<texture>` | прочее |
| `merc_icon` | `` | 56x54 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/ui_game_ahunt.xml:27` | `<texture>` | прочее |
| `mp_stats_icon_c_art` | `` | 25x21 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_team_panels_ahunt.xml:65` | `<texture>` | прочее |
| `mp_stats_icon_c_assist` | `` | 25x21 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_team_panels_tdm.xml:60` | `<texture>` | прочее |
| `mp_stats_icon_c_frags` | `` | 25x21 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_team_panels_dm.xml:32` | `<texture>` | прочее |
| `mp_stats_icon_c_ping` | `` | 25x21 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_team_panels_dm.xml:64` | `<texture>` | прочее |
| `mp_stats_icon_c_rank` | `` | 25x21 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_team_panels_dm.xml:56` | `<texture>` | прочее |
| `rust_bot_client` | `` | 201x134 | `configs/ui/textures_descr/ui_mp_main.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:8` | `<texture>` | прочее |
| `rusty_01` | `` | 310x99 | `configs/ui/textures_descr/ui_mp_main.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:5` | `<texture>` | прочее |
| `ui_actor_overlay` | `` | 165x108 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/talk_character_16.xml:4` | `<texture>` | прочее |
| `ui_alife_combat` | `` | 32x32 | `configs/ui/textures_descr/ui_alife.xml` | `configs/ui/map_spots.xml:218` | `<texture>` | прочее |
| `ui_am_condition` | `` | 19x19 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/af_params.xml:4` | `<texture>` | прочее |
| `ui_am_prop_Vibros` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/booster_params.xml:38` | `<texture>` | прочее |
| `ui_am_prop_artefact` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/af_params.xml:116` | `<texture>` | прочее |
| `ui_am_prop_chem` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/booster_params.xml:88` | `<texture>` | прочее |
| `ui_am_prop_radio_restore` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/booster_params.xml:20` | `<texture_minus>` | прочее |
| `ui_am_prop_restore_bleeding` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/booster_params.xml:58` | `<texture>` | прочее |
| `ui_am_prop_satiety_restore_speed` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/booster_params.xml:28` | `<texture>` | прочее |
| `ui_am_propery_05` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/booster_params.xml:7` | `<texture>` | прочее |
| `ui_am_propery_07` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/booster_params.xml:48` | `<texture>` | прочее |
| `ui_am_propery_08` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/booster_params.xml:148` | `<texture>` | прочее |
| `ui_am_propery_09` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/booster_params.xml:17` | `<texture>` | прочее |
| `ui_am_propery_11` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/booster_params.xml:78` | `<texture>` | прочее |
| `ui_asus_01` | `` | 512x512 | `configs/ui/textures_descr/ui_asus_intro.xml` | `configs/ui/ui_movies.xml:64` | `<texture>` | прочее |
| `ui_asus_02` | `` | 512x512 | `configs/ui/textures_descr/ui_asus_intro.xml` | `configs/ui/ui_movies.xml:68` | `<texture>` | прочее |
| `ui_bt_upgrade_chain` | `` | 105x56 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:29` | `<inking>` | прочее |
| `ui_bt_upgrade_not_allowed` | `` | 70x40 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:59` | `<back_texture>` | прочее |
| `ui_bt_upgrade_unknown` | `` | 70x40 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_16.xml:54` | `<back_texture>` | прочее |
| `ui_car_panel_light` | `` | 22x22 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/car_panel.xml:46` | `<texture>` | прочее |
| `ui_char_armor` | `` | 209x9 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:322` | `<texture>` | прочее |
| `ui_char_arrow` | `` | 9x39 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:353` | `<texture>` | прочее |
| `ui_char_arrow_shadow` | `` | 9x39 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:356` | `<texture>` | прочее |
| `ui_char_bleeding` | `` | 22x22 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/maingame_16.xml:108` | `<texture>` | прочее |
| `ui_char_resist_acid` | `` | 31x27 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:396` | `<texture>` | прочее |
| `ui_char_resist_fire` | `` | 31x27 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:360` | `<texture>` | прочее |
| `ui_char_resist_psi` | `` | 31x27 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:414` | `<texture>` | прочее |
| `ui_char_resist_rad` | `` | 31x27 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:378` | `<texture>` | прочее |
| `ui_char_stamina` | `` | 209x9 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:296` | `<texture>` | прочее |
| `ui_closed_af_slot` | `` | 58x58 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:185` | `<texture>` | прочее |
| `ui_fm_base_bonuse` | `` | 1x1 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_fraction_war.xml:125` | `<texture>` | прочее |
| `ui_fm_base_fraction` | `` | 127x56 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_ranking.xml:71` | `<texture>` | прочее |
| `ui_fm_list_foverlay` | `` | 127x56 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_ranking.xml:114` | `<texture>` | прочее |
| `ui_fm_over_chars` | `` | 165x108 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_ranking.xml:64` | `<texture>` | прочее |
| `ui_fm_over_fraction_logo_l` | `` | 251x196 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_fraction_war.xml:33` | `<texture>` | прочее |
| `ui_fm_over_fraction_logo_r` | `` | 251x196 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_fraction_war.xml:36` | `<texture>` | прочее |
| `ui_fraction_overlay` | `` | 48x48 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/talk_character_16.xml:14` | `<texture>` | прочее |
| `ui_icons_newPDA_SmallBlue` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:51` | `<texture>` | прочее |
| `ui_icons_newPDA_SmallGreen` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:64` | `<texture>` | прочее |
| `ui_icons_newPDA_SmallRed` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:77` | `<texture>` | прочее |
| `ui_icons_newPDA_man` | `` | 25x25 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:100` | `<texture>` | прочее |
| `ui_icons_newPDA_manArrow` | `` | 49x49 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:107` | `<texture>` | прочее |
| `ui_inGame2_inventory_item_status_bar_cop` | `` | 38x5 | `configs/ui/textures_descr/ui_actor_menu_cop.xml` | `configs/ui/actor_menu_item.xml:239` | `<texture>` | прочее |
| `ui_inGame2_inventory_item_status_bar_cop_16` | `` | 30x5 | `configs/ui/textures_descr/ui_actor_menu_cop.xml` | `configs/ui/actor_menu_item_16.xml:239` | `<texture>` | прочее |
| `ui_inGame2_left_side` | `` | 331x768 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:176` | `<texture>` | прочее |
| `ui_inGame2_load_info` | `` | 501x180 | `configs/ui/textures_descr/ui_ingame2_back_02.xml` | `configs/ui/ui_mm_load_dlg_16.xml:48` | `<texture>` | прочее |
| `ui_inv_flogo_over` | `` | 108x48 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu.xml:27` | `<texture>` | прочее |
| `ui_inv_icon_additional_weight` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/af_params.xml:126` | `<texture>` | прочее |
| `ui_inv_icon_bleeding_restore_speed` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/af_params.xml:55` | `<texture>` | прочее |
| `ui_inv_icon_burn_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:75` | `<texture>` | прочее |
| `ui_inv_icon_chemical_burn_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:105` | `<texture>` | прочее |
| `ui_inv_icon_fire_wound_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:115` | `<texture>` | прочее |
| `ui_inv_icon_power_restore_speed` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/af_params.xml:45` | `<texture>` | прочее |
| `ui_inv_icon_radiation_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:95` | `<texture>` | прочее |
| `ui_inv_icon_radiation_restore_speed` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/af_params.xml:27` | `<texture_minus>` | прочее |
| `ui_inv_icon_satiety_restore_speed` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/af_params.xml:35` | `<texture>` | прочее |
| `ui_inv_icon_shock_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:85` | `<texture>` | прочее |
| `ui_inv_icon_strike_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:80` | `<texture>` | прочее |
| `ui_inv_icon_wound_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:90` | `<texture>` | прочее |
| `ui_inventory_main` | `` | 1005x483 | `configs/ui/textures_descr/ui_inventory.xml` | `configs/ui/inventory_new.xml:18` | `<texture>` | прочее |
| `ui_inventory_rank` | `` | 75x65 | `configs/ui/textures_descr/ui_inventory.xml` | `configs/ui/inventory_new.xml:124` | `<texture>` | прочее |
| `ui_magnifier2` | `` | 207x24 | `configs/ui/textures_descr/ui_magnifier2.xml` | `configs/ui/ui_mm_main_16.xml:52` | `<texture>` | прочее |
| `ui_mapQuest_gold` | `` | 29x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:173` | `<texture>` | прочее |
| `ui_menu2_fog_01` | `` | 279x148 | `configs/ui/textures_descr/ui_mainmenu2.xml` | `configs/ui/ui_mm_main_16.xml:34` | `<texture>` | прочее |
| `ui_menu2_fog_02` | `` | 268x76 | `configs/ui/textures_descr/ui_mainmenu2.xml` | `configs/ui/ui_mm_main_16.xml:41` | `<texture>` | прочее |
| `ui_menu2_ray_01` | `` | 249x156 | `configs/ui/textures_descr/ui_mainmenu2.xml` | `configs/ui/ui_mm_main_16.xml:25` | `<texture>` | прочее |
| `ui_menu2_ray_02` | `` | 219x163 | `configs/ui/textures_descr/ui_mainmenu2.xml` | `configs/ui/ui_mm_main_16.xml:28` | `<texture>` | прочее |
| `ui_mini_af_spot` | `` | 13x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_mp.xml:79` | `<texture>` | прочее |
| `ui_mini_af_spot_above` | `` | 13x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_mp.xml:114` | `<texture_above>` | прочее |
| `ui_mini_af_spot_below` | `` | 13x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_mp.xml:113` | `<texture_below>` | прочее |
| `ui_mini_sn_spot_above` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:53` | `<texture_above>` | прочее |
| `ui_mini_sn_spot_below` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:52` | `<texture_below>` | прочее |
| `ui_minimap_point` | `` | 12x12 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:227` | `<texture>` | прочее |
| `ui_mm_loading_left_widepanel` | `` | 170x768 | `configs/ui/textures_descr/ui_mm_loading_screen.xml` | `configs/ui/ui_mm_loading_screen_16.xml:7` | `<texture>` | прочее |
| `ui_mm_loading_right_widepanel` | `` | 170x768 | `configs/ui/textures_descr/ui_mm_loading_screen.xml` | `configs/ui/ui_mm_loading_screen_16.xml:10` | `<texture>` | прочее |
| `ui_mmap_base` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:355` | `<texture>` | прочее |
| `ui_mmap_common_actor` | `` | 7x7 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_relations.xml:18` | `<texture>` | прочее |
| `ui_mmap_quest` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:616` | `<texture>` | прочее |
| `ui_mmap_secondary_alert` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:40` | `<texture>` | прочее |
| `ui_mmap_squad_leader` | `` | 9x9 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:495` | `<texture>` | прочее |
| `ui_mmap_stask_last_02` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:587` | `<texture>` | прочее |
| `ui_mp_map_info_fore` | `` | 256x180 | `configs/ui/textures_descr/ui_mp_main.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:49` | `<texture>` | прочее |
| `ui_numpad_key00` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:14` | `<texture_t>` | прочее |
| `ui_numpad_key01` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:18` | `<texture_t>` | прочее |
| `ui_numpad_key02` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:22` | `<texture_t>` | прочее |
| `ui_numpad_key03` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:26` | `<texture_t>` | прочее |
| `ui_numpad_key04` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:30` | `<texture_t>` | прочее |
| `ui_numpad_key05` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:34` | `<texture_t>` | прочее |
| `ui_numpad_key06` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:38` | `<texture_t>` | прочее |
| `ui_numpad_key07` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:42` | `<texture_t>` | прочее |
| `ui_numpad_key08` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:46` | `<texture_t>` | прочее |
| `ui_numpad_key09` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:50` | `<texture_t>` | прочее |
| `ui_numpad_key0b` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:59` | `<texture_t>` | прочее |
| `ui_numpad_key0c` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:55` | `<texture_t>` | прочее |
| `ui_numpad_keyCancel` | `` | 111x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:67` | `<texture_t>` | прочее |
| `ui_numpad_keyEnter` | `` | 111x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:63` | `<texture_t>` | прочее |
| `ui_numpad_void` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:13` | `<texture_e>` | прочее |
| `ui_pda2` | `` | 1024x768 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_16.xml:13` | `<texture>` | прочее |
| `ui_pda2_attack_base2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:55` | `<texture>` | прочее |
| `ui_pda2_base` | `` | 21x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:352` | `<texture>` | прочее |
| `ui_pda2_bring_item2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:138` | `<texture>` | прочее |
| `ui_pda2_caption` | `` | 955x48 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_16.xml:22` | `<texture>` | прочее |
| `ui_pda2_capture_base2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:79` | `<texture>` | прочее |
| `ui_pda2_guide` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:771` | `<texture>` | прочее |
| `ui_pda2_important1` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_tasks.xml:329` | `<texture>` | прочее |
| `ui_pda2_important2` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:782` | `<texture>` | прочее |
| `ui_pda2_info2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:214` | `<texture>` | прочее |
| `ui_pda2_mechanic` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:749` | `<texture>` | прочее |
| `ui_pda2_mtask_overlay` | `` | 42x37 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:103` | `<texture>` | прочее |
| `ui_pda2_noice` | `` | 973x683 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_16.xml:18` | `<texture>` | прочее |
| `ui_pda2_place_up` | `` | 9x5 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_ranking.xml:182` | `<texture>` | прочее |
| `ui_pda2_pt_resource` | `` | 27x27 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:368` | `<texture>` | прочее |
| `ui_pda2_pt_science` | `` | 27x27 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:362` | `<texture>` | прочее |
| `ui_pda2_quest` | `` | 21x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:613` | `<texture>` | прочее |
| `ui_pda2_secondary_alert2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:29` | `<texture>` | прочее |
| `ui_pda2_sq_sos` | `` | 27x27 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:639` | `<texture>` | прочее |
| `ui_pda2_squad_leader` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:492` | `<texture>` | прочее |
| `ui_pda2_stask_last_01a` | `` | 25x25 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:798` | `<texture>` | прочее |
| `ui_pda2_stask_last_02` | `` | 21x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:66` | `<texture>` | прочее |
| `ui_pda2_stask_last_02a` | `` | 25x25 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:17` | `<texture>` | прочее |
| `ui_pda2_upgrades2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_find_upgrade.xml:96` | `<texture>` | прочее |
| `ui_sm_mapQuest_gold` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:176` | `<texture>` | прочее |
| `ui_statOne_b` | `` | 447x58 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:24` | `<texture>` | прочее |
| `ui_statTwo_b` | `` | 845x98 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:12` | `<texture>` | прочее |
| `ui_upgrade_arrow2` | `` | 16x16 | `configs/ui/textures_descr/ui_icon_equipment.xml` | `configs/ui/actor_menu_item.xml:226` | `<texture>` | прочее |
| `ui_wp_prop_skorostrelnost` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/actor_menu_item.xml:55` | `<texture>` | прочее |
| `ui_wp_propery_07` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu_item.xml:58` | `<texture>` | прочее |
| `wpn_crosshair` | `` | 1024x1024 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes.xml:4` | `<texture>` | прочее |
| `wpn_crosshair_add_l` | `` | 102x768 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes_16.xml:7` | `<texture>` | прочее |
| `wpn_crosshair_add_r` | `` | 102x768 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes_16.xml:10` | `<texture>` | прочее |
| `wpn_crosshair_bino` | `` | 1024x1024 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes.xml:28` | `<texture>` | прочее |
| `wpn_crosshair_g36` | `` | 1024x1024 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes.xml:16` | `<texture>` | прочее |
| `wpn_crosshair_l85` | `` | 1024x1024 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes.xml:10` | `<texture>` | прочее |
| `wpn_crosshair_rpg` | `` | 1024x1024 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes.xml:22` | `<texture>` | прочее |

---

## Каталог родных элементов интерфейса: Зов Припяти (CoP / ЗП)

### 1. Архитектура UI в Зове Припяти

В S.T.A.L.K.E.R.: Call of Pripyat (движок X-Ray 1.6) UI был полностью переработан под широкоформатные экраны и новую концепцию списков и окон.

Ключевые особенности и отличия:
1. **Переход на `CUIListBox`**: Устаревший класс `CUIListWnd` полностью удалён. Вместо него внедрён `CUIListBox` (`gamedata/scripts/lua_help.script:6477`), наследующийся от `CUIScrollView`. Инициализация выполняется методом `xml:InitListBox(path, parent)` (`lua_help.script:5744`). Элементы создаются через `CUIListBoxItem`.
2. **Отказ от `InitButton`**: Метод `InitButton` удалён из `CScriptXmlInit`. Единственным типом кнопок стал `Init3tButton` (`lua_help.script:5757`), возвращающий `CUI3tButton`.
3. **Управление диалогами**: Устаревший метод `start_stop_menu` заменён на стандартные `self:ShowDialog(true)` и `self:HideDialog()` (`lua_help.script:6464-6465`).
4. **Текстовые элементы**: Появился `InitTextWnd` (`lua_help.script:5737`), возвращающий легковесный `CUITextWnd`.

---

### 2. Стандартные элементы управления и их XML-структура

#### 2.1. Кнопка с тремя состояниями (`Init3tButton`)
- **Класс C++**: `CUI3tButton` (`lua_help.script:6377`).
- **Метод инициализации**: `xml:Init3tButton(path, parent)` (`lua_help.script:5757`).
- **Принцип работы состояний**:
  Движок автоматически раскладывает базовый идентификатор текстуры на суффиксы:
  - `_e` — обычная активная кнопка
  - `_t` — зажатая кнопка
  - `_h` — наведение мыши
  - `_d` — отключенная кнопка
  Для длинных растягиваемых кнопок используется `frame_mode="1"` (`UIXmlInitBase.cpp:342`).

**Ванильный пример**: `gamedata/configs/ui/ui_mm_main_16.xml:30-40`:
```xml
<btn_quit x="0" y="270" width="168" height="25">
    <text font="letterica18" align="l">ui_mm_quit_game</text>
    <texture>ui_in_game_menu_back</texture>
    <text_color>
        <e r="216" g="186" b="140"/>
        <h r="255" g="255" b="255"/>
        <t r="150" g="150" b="150"/>
        <d r="100" g="100" b="100"/>
    </text_color>
</btn_quit>
```

---

#### 2.2. Галочка / Чекбокс (`InitCheck`)
- **Класс C++**: `CUICheckButton` (`lua_help.script:6528`).
- **Метод инициализации**: `xml:InitCheck(path, parent)` (`lua_help.script:5748`).

**Ванильный пример**: `gamedata/configs/ui/ui_mm_opt_16.xml:280-285`:
```xml
<check_crosshair x="20" y="80" width="243" height="21">
    <options_item entry="hud_crosshair" group="mm_opt_gameplay"/>
    <text font="letterica16" r="215" g="195" b="170">ui_mm_show_crosshair</text>
</check_crosshair>
```
Инициализация в скрипте: `gamedata/scripts/ui_mm_opt_gameplay.script:20`:
```lua
xml:InitCheck("tab_gameplay:check_crosshair", self)
```

---

#### 2.3. Вкладки (`InitTab`)
- **Класс C++**: `CUITabControl` (`lua_help.script:7476`).
- **Метод инициализации**: `xml:InitTab(path, parent)` (`lua_help.script:5734`).

**Ванильный пример**: `gamedata/configs/ui/ui_mm_opt_16.xml:45-60`:
```xml
<tab x="11" y="27" width="368" height="38">
    <button id="video" x="0" y="0" width="89" height="31">
        <texture>ui_tab_button_01</texture>
        <text font="letterica18">ui_mm_video</text>
    </button>
    <button id="sound" x="92" y="0" width="89" height="31">
        <texture>ui_tab_button_02</texture>
        <text font="letterica18">ui_mm_sound</text>
    </button>
    <button id="gameplay" x="184" y="0" width="89" height="31">
        <texture>ui_tab_button_03</texture>
        <text font="letterica18">ui_mm_gameplay</text>
    </button>
    <button id="controls" x="276" y="0" width="89" height="31">
        <texture>ui_tab_button_04</texture>
        <text font="letterica18">ui_mm_controls</text>
    </button>
</tab>
```

---

#### 2.4. Полоса прогресса (`InitProgressBar`)
- **Класс C++**: `CUIProgressBar` (`lua_help.script:6989`).
- **Метод инициализации**: `xml:InitProgressBar(path, parent)` (`lua_help.script:5739`).

**Ванильный пример**: `gamedata/configs/ui/ui_actor_sleep_screen.xml:15-20`:
```xml
<time_bar x="30" y="80" width="300" height="15" horz="1" min="0" max="24" pos="0">
    <progress>
        <texture>ui_inGame2_horizontal_progress_bar</texture>
    </progress>
</time_bar>
```

---

#### 2.5. Поле ввода (`InitEditBox`)
- **Класс C++**: `CUIEditBox` (`lua_help.script:6451`).
- **Метод инициализации**: `xml:InitEditBox(path, parent)` (`lua_help.script:5747`).

**Ванильный пример**: `gamedata/configs/ui/ui_mm_save_dlg_16.xml:32-37`:
```xml
<edit x="24" y="75" width="204" height="23" file_name_mode="1">
    <text font="letterica18"/>
    <texture>ui_inGame2_edit_box</texture>
</edit>
```
Инициализация в скрипте: `gamedata/scripts/ui_save_dialog.script:45`:
```lua
self.edit_filename = xml:InitEditBox("edit", self)
```

---

### 3. Шрифты в Зове Припяти

#### 3.1. Функции `GetFont*` из `lua_help.script`
В `gamedata/scripts/lua_help.script` экспортированы 11 функций:
1. `GetFont()` — строка 73
2. `GetFontDI()` — строка 69
3. `GetFontMedium()` — строка 70
4. `GetFontSmall()` — строка 74
5. `GetFontLetterica16Russian()` — строка 71
6. `GetFontLetterica18Russian()` — строка 75
7. `GetFontLetterica25()` — строка 77
8. `GetFontGraffiti19Russian()` — строка 72
9. `GetFontGraffiti22Russian()` — строка 78
10. `GetFontGraffiti32Russian()` — строка 79
11. `GetFontGraffiti50Russian()` — строка 76

#### 3.2. Имена шрифтов в ванильных XML (`font="..."`)
В XML ЗП используются:
`arial_14`, `font_graffiti`, `graffiti19`, `graffiti22`, `graffiti32`, `graffiti50`, `letterica16`, `letterica18`, `letterica25`, `medium`.
Конфигурация начертаний и размеров описана в `configs/fonts.ltx:1-105`.

---

### 4. Каталог текстур родного интерфейса (ЗП)

| ID текстуры | Файл .dds | Размер (WxH) | Где определена | Пример использования в XML | Тег в XML | Роль |
|-------------|-----------|--------------|----------------|----------------------------|-----------|------|
| `ui_bm_back` | `` | 1024x768 | `configs/ui/textures_descr/ui_buy_menu.xml` | `configs/ui/mp_buy_menu_16.xml:185` | `<texture>` | рамка окна |
| `ui_car_panel_back` | `` | 220x120 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/car_panel.xml:5` | `<texture>` | рамка окна |
| `ui_credits_background` | `` | 1024x768 | `configs/ui/textures_descr/ui_mainmenu.xml` | `configs/ui/ui_credits_16.xml:10` | `<texture>` | рамка окна |
| `ui_frame_mainDes` | `` | 749x543 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector_16.xml:103` | `<texture>` | рамка окна |
| `ui_inGame2_Mp_buyscreen_main_window` | `` | 1024x768 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_16.xml:26` | `<texture>` | рамка окна |
| `ui_inGame2_Mp_buyscreen_priceinfo_screen` | `` | 86x51 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_16.xml:34` | `<texture>` | рамка окна |
| `ui_inGame2_Mp_screen_main_window` | `` | 724x526 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/skin_selector.xml:4` | `<texture>` | рамка окна |
| `ui_inGame2_Mp_screen_map_window` | `` | 324x234 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/voting_category_16.xml:99` | `<texture>` | рамка окна |
| `ui_inGame2_Mp_screen_mapinfo_window` | `` | 324x234 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/map_desc.xml:13` | `<texture>` | рамка окна |
| `ui_inGame2_Mp_screen_skin_window` | `` | 111x232 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/skin_selector.xml:11` | `<texture>` | рамка окна |
| `ui_inGame2_Mp_screen_sponsor_window` | `` | 658x234 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/server_info.xml:14` | `<texture>` | рамка окна |
| `ui_inGame2_Radar_main_window` | `` | 226x226 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/zone_map.xml:6` | `<texture>` | рамка окна |
| `ui_inGame2_awards_background` | `` | 121x121 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mp_awards.xml:4` | `<texture>` | рамка окна |
| `ui_inGame2_background` | `` | 1024x768 | `configs/ui/textures_descr/ui_actor_main_menu.xml` | `configs/ui/ui_mm_main_16.xml:10` | `<texture>` | рамка окна |
| `ui_inGame2_inventory_back` | `` | 683x768 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:12` | `<texture>` | рамка окна |
| `ui_inGame2_main_window` | `` | 698x498 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_offline_16.xml:15` | `<texture>` | рамка окна |
| `ui_inGame2_main_window_small` | `` | 493x363 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_gamespy_16.xml:16` | `<texture>` | рамка окна |
| `ui_inGame2_mp_background` | `` | 1024x768 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_gamespy_16.xml:4` | `<texture>` | рамка окна |
| `ui_inGame2_mp_screen_selection` | `` | 370x16 | `configs/ui/textures_descr/ui_actor_mp_ingame_menu.xml` | `configs/ui/ui_team_panels_tdm_16.xml:70` | `<texture>` | рамка окна |
| `ui_inGame2_opt_background` | `` | 1024x768 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:4` | `<texture>` | рамка окна |
| `ui_inGame2_opt_main_window` | `` | 487x462 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:23` | `<texture>` | рамка окна |
| `ui_inGame2_panorama_window` | `` | 601x122 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/ui_sleep_dialog.xml:11` | `<texture>` | рамка окна |
| `ui_inGame2_pda_favorite_weapon_background` | `` | 393x130 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_ranking.xml:95` | `<texture>` | рамка окна |
| `ui_inGame2_picture_window` | `` | 208x135 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabdemo.xml:27` | `<texture>` | рамка окна |
| `ui_inGame2_screen_1` | `` | 845x669 | `configs/ui/textures_descr/ui_actor_mp_ingame_menu.xml` | `configs/ui/ui_team_panels_tdm_16.xml:4` | `<texture>` | рамка окна |
| `ui_inGame2_screen_2` | `` | 624x411 | `configs/ui/textures_descr/ui_actor_mp_ingame_menu.xml` | `configs/ui/voting_category_16.xml:87` | `<texture>` | рамка окна |
| `ui_inGame2_screen_3` | `` | 444x679 | `configs/ui/textures_descr/ui_actor_mp_ingame_menu.xml` | `configs/ui/ui_team_panels_dm.xml:4` | `<texture>` | рамка окна |
| `ui_inGame2_screen_4` | `` | 624x411 | `configs/ui/textures_descr/ui_actor_mp_ingame_menu.xml` | `configs/ui/voting_category_16.xml:4` | `<texture>` | рамка окна |
| `ui_inGame2_widescreen_panel_left` | `` | 128x768 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_gamespy_16.xml:10` | `<texture>` | рамка окна |
| `ui_inGame2_widescreen_panel_right` | `` | 128x768 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_gamespy_16.xml:13` | `<texture>` | рамка окна |
| `ui_inGame2_widescreen_sidepanels_left` | `` | 128x768 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/pda_16.xml:15` | `<texture>` | рамка окна |
| `ui_inGame2_widescreen_sidepanels_right` | `` | 128x768 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/pda_16.xml:12` | `<texture>` | рамка окна |
| `ui_item_count_back` | `` | 29x16 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/maingame_16.xml:123` | `<texture>` | рамка окна |
| `ui_mm_loading_screen` | `` | 1024x768 | `configs/ui/textures_descr/ui_mm_loading_screen.xml` | `configs/ui/ui_mm_loading_screen.xml:4` | `<texture>` | рамка окна |
| `ui_numpad_lockframe` | `` | 339x369 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:5` | `<texture>` | рамка окна |
| `ui_patch_back` | `` | 603x51 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_mm_opt_16.xml:813` | `<texture>` | рамка окна |
| `ui_save_load_back` | `` | 288x428 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/ui_mm_save_dlg_16.xml:19` | `<texture>` | рамка окна |
| `ui_statOne_back` | `` | 440x163 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:21` | `<texture>` | рамка окна |
| `ui_statTwo_back` | `` | 845x162 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:9` | `<texture>` | рамка окна |
| `mp_award_achilles_heel` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:150` | `<texture>` | кнопка |
| `mp_award_deadly_accuracy` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:430` | `<texture>` | кнопка |
| `mp_award_dignity` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:510` | `<texture>` | кнопка |
| `mp_award_double_shot_double_kill` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:230` | `<texture>` | кнопка |
| `mp_award_dry_victory` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:90` | `<texture>` | кнопка |
| `mp_award_fater_than_bullets` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:170` | `<texture>` | кнопка |
| `mp_award_harvest_time` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:190` | `<texture>` | кнопка |
| `mp_award_silent_death` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:590` | `<texture>` | кнопка |
| `mp_award_toughy` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:290` | `<texture>` | кнопка |
| `secondary_task_spot_above` | `` | 19x16 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/map_spots.xml:615` | `<texture_above>` | кнопка |
| `secondary_task_spot_below` | `` | 19x16 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/map_spots.xml:614` | `<texture_below>` | кнопка |
| `ui_TV_Skin` | `` | 666x273 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector_16.xml:109` | `<texture>` | кнопка |
| `ui_TV_Skin_button_l_d` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector_16.xml:134` | `<texture[auto-_d]>` | кнопка |
| `ui_TV_Skin_button_l_e` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector_16.xml:134` | `<texture[auto-_e]>` | кнопка |
| `ui_TV_Skin_button_l_h` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector_16.xml:134` | `<texture[auto-_h]>` | кнопка |
| `ui_TV_Skin_button_l_t` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector_16.xml:134` | `<texture[auto-_t]>` | кнопка |
| `ui_TV_Skin_button_r_d` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector_16.xml:137` | `<texture[auto-_d]>` | кнопка |
| `ui_TV_Skin_button_r_e` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector_16.xml:137` | `<texture[auto-_e]>` | кнопка |
| `ui_TV_Skin_button_r_h` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector_16.xml:137` | `<texture[auto-_h]>` | кнопка |
| `ui_TV_Skin_button_r_t` | `` | 33x218 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/skin_selector_16.xml:137` | `<texture[auto-_t]>` | кнопка |
| `ui_TV_descr_e` | `` | 220x277 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/map_desc.xml:16` | `<texture>` | кнопка |
| `ui_TV_team_bottom` | `` | 664x208 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/spawn_16.xml:81` | `<texture>` | кнопка |
| `ui_TV_team_tl` | `` | 332x57 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/spawn_16.xml:75` | `<texture>` | кнопка |
| `ui_TV_team_tr` | `` | 332x57 | `configs/ui/textures_descr/ui_map_description.xml` | `configs/ui/spawn_16.xml:78` | `<texture>` | кнопка |
| `ui_am_prop_damage` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/actor_menu_item.xml:362` | `<texture>` | кнопка |
| `ui_am_prop_electro` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/af_params.xml:78` | `<texture>` | кнопка |
| `ui_am_prop_thermo` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/af_params.xml:68` | `<texture>` | кнопка |
| `ui_am_prop_time_period` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/booster_params.xml:159` | `<texture>` | кнопка |
| `ui_beltbut_granadeBig_d` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:178` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_granadeBig_e` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:178` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_granadeBig_h` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:178` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_granadeBig_t` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:178` | `<texture[auto-_t]>` | кнопка |
| `ui_beltbut_patrons_d` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:169` | `<texture[auto-_d]>` | кнопка |
| `ui_beltbut_patrons_e` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:169` | `<texture[auto-_e]>` | кнопка |
| `ui_beltbut_patrons_h` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:169` | `<texture[auto-_h]>` | кнопка |
| `ui_beltbut_patrons_t` | `` | 23x23 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/mp_buy_menu_buttons.xml:169` | `<texture[auto-_t]>` | кнопка |
| `ui_button_main01_d` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector_16.xml:154` | `<texture[auto-_d]>` | кнопка |
| `ui_button_main01_e` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector_16.xml:154` | `<texture[auto-_e]>` | кнопка |
| `ui_button_main01_h` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector_16.xml:154` | `<texture[auto-_h]>` | кнопка |
| `ui_button_main01_t` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector_16.xml:154` | `<texture[auto-_t]>` | кнопка |
| `ui_button_main02_d` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_d]>` | кнопка |
| `ui_button_main02_e` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_e]>` | кнопка |
| `ui_button_main02_h` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_h]>` | кнопка |
| `ui_button_main02_t` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/stats.xml:123` | `<texture[auto-_t]>` | кнопка |
| `ui_button_main03_d` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector_16.xml:174` | `<texture[auto-_d]>` | кнопка |
| `ui_button_main03_e` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector_16.xml:174` | `<texture[auto-_e]>` | кнопка |
| `ui_button_main03_h` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector_16.xml:174` | `<texture[auto-_h]>` | кнопка |
| `ui_button_main03_t` | `` | 157x48 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/skin_selector_16.xml:174` | `<texture[auto-_t]>` | кнопка |
| `ui_button_ordinary_d` | `` | 117x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:831` | `<texture[auto-_d]>` | кнопка |
| `ui_button_ordinary_e` | `` | 117x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:831` | `<texture[auto-_e]>` | кнопка |
| `ui_button_ordinary_h` | `` | 117x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:831` | `<texture[auto-_h]>` | кнопка |
| `ui_button_ordinary_t` | `` | 117x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_opt_16.xml:831` | `<texture[auto-_t]>` | кнопка |
| `ui_car_panel_engine` | `` | 22x22 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/car_panel.xml:20` | `<texture>` | кнопка |
| `ui_flag_russia_cop_d` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:115` | `<texture[auto-_d]>` | кнопка |
| `ui_flag_russia_cop_e` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:115` | `<texture[auto-_e]>` | кнопка |
| `ui_flag_russia_cop_h` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:115` | `<texture[auto-_h]>` | кнопка |
| `ui_flag_russia_cop_t` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:115` | `<texture[auto-_t]>` | кнопка |
| `ui_flag_usa_cop_d` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:111` | `<texture[auto-_d]>` | кнопка |
| `ui_flag_usa_cop_e` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:111` | `<texture[auto-_e]>` | кнопка |
| `ui_flag_usa_cop_h` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:111` | `<texture[auto-_h]>` | кнопка |
| `ui_flag_usa_cop_t` | `` | 128x128 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/ui_mm_main_16.xml:111` | `<texture[auto-_t]>` | кнопка |
| `ui_frame_01_t` | `` | 32x32 | `configs/ui/textures_descr/ui_old_textures.xml` | `configs/ui/ui_game_dm.xml:3` | `<texture[auto-_t]>` | кнопка |
| `ui_frame_t` | `` | 128x128 | `configs/ui/textures_descr/ui_old_textures.xml` | `configs/ui/stats.xml:93` | `<texture[auto-_t]>` | кнопка |
| `ui_hud_frame_money` | `` | 99x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/mp_buy_menu_16.xml:189` | `<texture>` | кнопка |
| `ui_hud_frame_rank` | `` | 60x65 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/mp_buy_menu_16.xml:193` | `<texture>` | кнопка |
| `ui_hud_grenadetarget_e` | `` | 91x92 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/grenade.xml:3` | `<texture>` | кнопка |
| `ui_hud_icon_PDA` | `` | 48x29 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:112` | `<texture>` | кнопка |
| `ui_hud_icon_artefact` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:103` | `<texture>` | кнопка |
| `ui_hud_icon_drop` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:74` | `<texture>` | кнопка |
| `ui_hud_icon_eat` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:78` | `<texture>` | кнопка |
| `ui_hud_icon_goodmode` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:99` | `<texture>` | кнопка |
| `ui_hud_icon_psycho` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:82` | `<texture>` | кнопка |
| `ui_hud_icon_radiation` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:70` | `<texture>` | кнопка |
| `ui_hud_icon_sleep` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:86` | `<texture>` | кнопка |
| `ui_hud_icon_weapon` | `` | 64x64 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/maingame_16.xml:18` | `<texture>` | кнопка |
| `ui_hud_map_arrow` | `` | 11x24 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/map_spots.xml:8` | `<texture>` | кнопка |
| `ui_hud_points_count` | `` | 224x45 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_custom_msgs.xml:179` | `<texture>` | кнопка |
| `ui_hud_status_blue_01` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:51` | `<texture>` | кнопка |
| `ui_hud_status_blue_02` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:54` | `<texture>` | кнопка |
| `ui_hud_status_blue_03` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:57` | `<texture>` | кнопка |
| `ui_hud_status_blue_04` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:60` | `<texture>` | кнопка |
| `ui_hud_status_blue_05` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:63` | `<texture>` | кнопка |
| `ui_hud_status_green_01` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:36` | `<texture>` | кнопка |
| `ui_hud_status_green_02` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:39` | `<texture>` | кнопка |
| `ui_hud_status_green_03` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:42` | `<texture>` | кнопка |
| `ui_hud_status_green_04` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:45` | `<texture>` | кнопка |
| `ui_hud_status_green_05` | `` | 46x47 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_game_ahunt.xml:48` | `<texture>` | кнопка |
| `ui_hud_teamF_counter` | `` | 36x70 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/messages_window.xml:10` | `<texture>` | кнопка |
| `ui_hud_teamF_counterC` | `` | 33x33 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/messages_window.xml:7` | `<texture>` | кнопка |
| `ui_hud_timer_games` | `` | 155x66 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_custom_msgs.xml:5` | `<texture>` | кнопка |
| `ui_icons_PDA_dialog_t` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_tasks.xml:181` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_PDA_tooltips_t` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_16.xml:39` | `<texture[auto-_t]>` | кнопка |
| `ui_icons_mapPDA_mark_t` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:73` | `<texture>` | кнопка |
| `ui_icons_mapPDA_persBig_e` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:47` | `<texture>` | кнопка |
| `ui_icons_mapPDA_persBig_h` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:60` | `<texture>` | кнопка |
| `ui_icons_newPDA_Crclbig_h` | `` | 115x115 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/maingame_16.xml:4` | `<texture>` | кнопка |
| `ui_icons_newPDA_perssign_h` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/actor_menu_item.xml:238` | `<texture>` | кнопка |
| `ui_inGame2_Detector_icon_acid_big` | `` | 34x34 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/ui_detector_artefact.xml:26` | `<texture>` | кнопка |
| `ui_inGame2_Detector_icon_artefact` | `` | 8x8 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/ui_detector_artefact.xml:335` | `<texture>` | кнопка |
| `ui_inGame2_Detector_icon_electro_big` | `` | 34x34 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/ui_detector_artefact.xml:60` | `<texture>` | кнопка |
| `ui_inGame2_Detector_icon_fire_big` | `` | 34x34 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/ui_detector_artefact.xml:108` | `<texture>` | кнопка |
| `ui_inGame2_Detector_icon_gravity_big` | `` | 34x34 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/ui_detector_artefact.xml:90` | `<texture>` | кнопка |
| `ui_inGame2_Mp_HUD_DM` | `` | 114x41 | `configs/ui/textures_descr/ui_actor_mp_hud.xml` | `configs/ui/ui_game_dm.xml:70` | `<texture>` | кнопка |
| `ui_inGame2_Mp_HUD_art_hunt` | `` | 54x63 | `configs/ui/textures_descr/ui_actor_mp_hud.xml` | `configs/ui/ui_game_ahunt.xml:77` | `<texture>` | кнопка |
| `ui_inGame2_Mp_HUD_left_panel` | `` | 175x32 | `configs/ui/textures_descr/ui_actor_mp_hud.xml` | `configs/ui/ui_game_ahunt.xml:3` | `<texture>` | кнопка |
| `ui_inGame2_Mp_HUD_money_panel` | `` | 146x42 | `configs/ui/textures_descr/ui_actor_mp_hud.xml` | `configs/ui/ui_game_ahunt.xml:20` | `<texture>` | кнопка |
| `ui_inGame2_Mp_HUD_right_panel` | `` | 175x32 | `configs/ui/textures_descr/ui_actor_mp_hud.xml` | `configs/ui/ui_game_ahunt.xml:7` | `<texture>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_d` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/skin_selector.xml:68` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_e` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/skin_selector.xml:68` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_h` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/skin_selector.xml:68` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_nodowncorner_d` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:104` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_nodowncorner_e` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:104` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_nodowncorner_h` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:104` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_nodowncorner_t` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:104` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_noupcorner_d` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:111` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_noupcorner_e` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:111` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_noupcorner_h` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:111` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_noupcorner_t` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:111` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_bigbuttone_t` | `` | 127x28 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/skin_selector.xml:68` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_back_button_d` | `` | 36x35 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:75` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_back_button_e` | `` | 36x35 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:75` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_back_button_h` | `` | 36x35 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:75` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_back_button_t` | `` | 36x35 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:75` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buygrenadeluncher_buttone_d` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:198` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buygrenadeluncher_buttone_e` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:198` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buygrenadeluncher_buttone_h` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:198` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buygrenadeluncher_buttone_t` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:198` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buyscope_buttone_d` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:193` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buyscope_buttone_e` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:193` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buyscope_buttone_h` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:193` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buyscope_buttone_t` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:193` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buysilencer_buttone_d` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:183` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buysilencer_buttone_e` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:183` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buysilencer_buttone_h` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:183` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_buysilencer_buttone_t` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:183` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_depositinfo_screen` | `` | 86x52 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_16.xml:30` | `<texture>` | кнопка |
| `ui_inGame2_Mp_buyscreen_save_buttone_background_panel` | `` | 40x53 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_16.xml:50` | `<texture>` | кнопка |
| `ui_inGame2_Mp_buyscreen_save_buttone_background_panel_nocorner` | `` | 40x53 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_16.xml:47` | `<texture>` | кнопка |
| `ui_inGame2_Mp_buyscreen_saveselection_buttone_d` | `` | 26x25 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:141` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_saveselection_buttone_e` | `` | 26x25 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:141` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_saveselection_buttone_h` | `` | 26x25 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:141` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_saveselection_buttone_t` | `` | 26x25 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:141` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_HeavyWeapons_d` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:63` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_HeavyWeapons_e` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:63` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_HeavyWeapons_h` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:63` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_HeavyWeapons_t` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:63` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_Sniperrifles_d` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:51` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_Sniperrifles_e` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:51` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_Sniperrifles_h` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:51` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_Sniperrifles_t` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:51` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_machineguns_d` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:39` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_machineguns_e` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:39` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_machineguns_h` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:39` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_machineguns_t` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:39` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_shotguns_d` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:27` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_shotguns_e` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:27` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_shotguns_h` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:27` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_second_weapons_shotguns_t` | `` | 209x118 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:27` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_1_d` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_1_e` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_1_h` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_1_t` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:2` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_2_d` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_2_e` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_2_h` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_2_t` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:7` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_3_d` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_3_e` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_3_h` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_3_t` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:11` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_4_d` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_4_e` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_4_h` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_4_t` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:15` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_5_d` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_5_e` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_5_h` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_Mp_buyscreen_small_weapons_screen_5_t` | `` | 85x103 | `configs/ui/textures_descr/ui_actor_mp_buyscreen.xml` | `configs/ui/mp_buy_menu_buttons.xml:20` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_Mp_screen_skin_window_H` | `` | 95x216 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/skin_selector.xml:33` | `<texture>` | кнопка |
| `ui_inGame2_PDA_icon_Place_to_rest` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:772` | `<texture>` | кнопка |
| `ui_inGame2_PDA_icon_Place_to_rest_small` | `` | 15x15 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:775` | `<texture>` | кнопка |
| `ui_inGame2_PDA_icon_Stalker_Trader` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:717` | `<texture>` | кнопка |
| `ui_inGame2_PDA_icon_Stalker_Trader_small` | `` | 15x14 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:720` | `<texture>` | кнопка |
| `ui_inGame2_Patroni_HUD_active_items_icon_e` | `` | 12x23 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:127` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_Patroni_HUD_blue_bar` | `` | 138x10 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:214` | `<texture>` | кнопка |
| `ui_inGame2_Patroni_HUD_main_window` | `` | 222x126 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:201` | `<texture>` | кнопка |
| `ui_inGame2_Patroni_HUD_red_bar` | `` | 138x16 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:207` | `<texture>` | кнопка |
| `ui_inGame2_armor_highlighter` | `` | 96x161 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:151` | `<texture>` | кнопка |
| `ui_inGame2_arrow_button_d` | `` | 36x20 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:88` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_arrow_button_e` | `` | 36x20 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:88` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_arrow_button_h` | `` | 36x20 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:88` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_arrow_button_t` | `` | 36x20 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:88` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_arrow_down` | `` | 9x10 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:110` | `<texture>` | кнопка |
| `ui_inGame2_artefakt_highlighter` | `` | 52x52 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:192` | `<texture>` | кнопка |
| `ui_inGame2_big_inventory_button_d` | `` | 243x24 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:322` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_big_inventory_button_e` | `` | 243x24 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:322` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_big_inventory_button_h` | `` | 243x24 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:322` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_big_inventory_button_t` | `` | 243x24 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:322` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_button_d` | `` | 108x24 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabprofile.xml:14` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_button_e` | `` | 108x24 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabprofile.xml:14` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_button_h` | `` | 108x24 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabprofile.xml:14` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_button_t` | `` | 108x24 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabprofile.xml:14` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_demo_player_button_1_d` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:8` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_demo_player_button_1_e` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:8` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_demo_player_button_1_h` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:8` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_demo_player_button_1_t` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:8` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_demo_player_button_2_d` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:16` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_demo_player_button_2_e` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:16` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_demo_player_button_2_h` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:16` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_demo_player_button_2_t` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:16` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_demo_player_button_3_d` | `` | 49x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:48` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_demo_player_button_3_e` | `` | 49x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:48` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_demo_player_button_3_h` | `` | 49x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:48` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_demo_player_button_3_t` | `` | 49x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:48` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_demo_player_button_4_d` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:40` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_demo_player_button_4_e` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:40` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_demo_player_button_4_h` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:40` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_demo_player_button_4_t` | `` | 77x27 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:40` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_demo_player_info_window_t` | `` | 36x11 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/actor_menu.xml:3` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_demo_player_main_window` | `` | 452x87 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:4` | `<texture>` | кнопка |
| `ui_inGame2_demo_player_stats` | `` | 437x148 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabdemo.xml:71` | `<texture>` | кнопка |
| `ui_inGame2_detector_highlighter` | `` | 96x48 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:162` | `<texture>` | кнопка |
| `ui_inGame2_dialog_main_window` | `` | 634x760 | `configs/ui/textures_descr/ui_actor_dialog_screen.xml` | `configs/ui/talk_16.xml:4` | `<texture>` | кнопка |
| `ui_inGame2_edit_box_1_e` | `` | 3x25 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabprofile.xml:9` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_edit_box_2_e` | `` | 3x29 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/pda_spot.xml:10` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_edit_box_e` | `` | 10x29 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:30` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_empty_frame_e` | `` | 207x3 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabdemo.xml:31` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_helmet_blocker` | `` | 109x114 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:257` | `<texture>` | кнопка |
| `ui_inGame2_helmet_highlighter` | `` | 96x98 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:171` | `<texture>` | кнопка |
| `ui_inGame2_hint_wnd_Information` | `` | 242x7 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/buy_menu_item.xml:27` | `<texture>` | кнопка |
| `ui_inGame2_hint_wnd_Properties` | `` | 242x7 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/buy_menu_item.xml:49` | `<texture>` | кнопка |
| `ui_inGame2_hint_wnd_bar` | `` | 128x9 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/buy_menu_item.xml:81` | `<texture>` | кнопка |
| `ui_inGame2_hint_wnd_bar_16` | `` | 101x9 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu_item.xml:166` | `<texture>` | кнопка |
| `ui_inGame2_hint_wnd_main_window_t` | `` | 25x150 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu.xml:371` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_hint_wnd_upgrades` | `` | 242x7 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu_item.xml:223` | `<texture>` | кнопка |
| `ui_inGame2_inventory_button_d` | `` | 133x24 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:298` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_inventory_button_e` | `` | 133x24 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:298` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_inventory_button_h` | `` | 133x24 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:298` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_inventory_button_t` | `` | 133x24 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:298` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_inventory_health_bar` | `` | 214x16 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:392` | `<texture>` | кнопка |
| `ui_inGame2_opt_button_1_d` | `` | 131x25 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:47` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_opt_button_1_e` | `` | 9x9 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:47` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_opt_button_1_h` | `` | 131x25 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:47` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_opt_button_1_t` | `` | 131x25 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:47` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_opt_button_2_d` | `` | 129x25 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:71` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_opt_button_2_e` | `` | 9x9 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:71` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_opt_button_2_h` | `` | 129x25 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:71` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_opt_button_2_t` | `` | 129x25 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:71` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_opt_buttons_frame` | `` | 461x38 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:25` | `<texture>` | кнопка |
| `ui_inGame2_pda_Filtr_arrow_left_d` | `` | 30x30 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_logs_16.xml:76` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_Filtr_arrow_left_e` | `` | 30x30 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_logs_16.xml:76` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_Filtr_arrow_left_h` | `` | 30x30 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_logs_16.xml:76` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_Filtr_arrow_left_t` | `` | 30x30 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_logs_16.xml:76` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_Filtr_arrow_right_d` | `` | 30x30 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_logs_16.xml:81` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_Filtr_arrow_right_e` | `` | 30x30 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_logs_16.xml:81` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_Filtr_arrow_right_h` | `` | 30x30 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_logs_16.xml:81` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_Filtr_arrow_right_t` | `` | 30x30 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_logs_16.xml:81` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_button_d` | `` | 172x27 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_16.xml:49` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_button_e` | `` | 172x27 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_16.xml:49` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_button_h` | `` | 172x27 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_16.xml:49` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_button_t` | `` | 172x27 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_16.xml:49` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_buttons_background_t` | `` | 56x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:7` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_buttons_leftside_e` | `` | 28x27 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_16.xml:6` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_buttons_rightside_e` | `` | 8x27 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_16.xml:9` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_center_on_mission_button_d` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:137` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_center_on_mission_button_e` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:137` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_center_on_mission_button_h` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:137` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_center_on_mission_button_t` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:137` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_control_map_button_0_d` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:143` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_control_map_button_0_e` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:143` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_control_map_button_0_h` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:143` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_control_map_button_0_t` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:143` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_control_map_button_1_d` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:147` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_control_map_button_1_e` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:147` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_control_map_button_1_h` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:147` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_control_map_button_1_t` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:147` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_control_map_button_2_d` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:151` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_control_map_button_2_e` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:151` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_control_map_button_2_h` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:151` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_control_map_button_2_t` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:151` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_control_map_button_3_d` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:155` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_control_map_button_3_e` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:155` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_control_map_button_3_h` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:155` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_control_map_button_3_t` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:155` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_control_map_button_4_d` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:159` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_control_map_button_4_e` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:159` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_control_map_button_4_h` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:159` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_control_map_button_4_t` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:159` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_control_map_button_5_d` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:163` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_control_map_button_5_e` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:163` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_control_map_button_5_h` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:163` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_control_map_button_5_t` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:163` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_control_map_button_6_d` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:167` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_control_map_button_6_e` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:167` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_control_map_button_6_h` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:167` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_control_map_button_6_t` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:167` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_control_map_button_7_d` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:171` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_control_map_button_7_e` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:171` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_control_map_button_7_h` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:171` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_control_map_button_7_t` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:171` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_control_map_button_8_d` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:175` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_control_map_button_8_e` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:175` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_control_map_button_8_h` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:175` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_control_map_button_8_t` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:175` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_map_background_e` | `` | 955x16 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:51` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_map_frame_e` | `` | 923x12 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:57` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_missionlist_button_e_e` | `` | 15x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:36` | `<texture_e[auto-_e]>` | кнопка |
| `ui_inGame2_pda_missionlist_button_ht_e` | `` | 15x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:37` | `<texture_t[auto-_e]>` | кнопка |
| `ui_inGame2_pda_missionlist_button_leftside` | `` | 26x29 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:24` | `<texture>` | кнопка |
| `ui_inGame2_pda_missionlist_button_rightside_e` | `` | 6x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:27` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_ranking_center_caption_e` | `` | 8x27 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_ranking.xml:69` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_ranking_icon_over_t` | `` | 300x2 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_ranking.xml:24` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_smallbutton_d` | `` | 179x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:83` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_pda_smallbutton_e` | `` | 179x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:83` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_pda_smallbutton_h` | `` | 179x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:83` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_pda_smallbutton_t` | `` | 179x19 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:83` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_pda_smallbuttons_rightside` | `` | 41x27 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:30` | `<texture>` | кнопка |
| `ui_inGame2_pda_texture` | `` | 1024x768 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_16.xml:4` | `<texture>` | кнопка |
| `ui_inGame2_quick_item_highlighter` | `` | 66x59 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:189` | `<texture>` | кнопка |
| `ui_inGame2_repair_button_d` | `` | 85x29 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/inventory_upgrade_16.xml:15` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_repair_button_e` | `` | 85x29 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/inventory_upgrade_16.xml:15` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_repair_button_h` | `` | 85x29 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/inventory_upgrade_16.xml:15` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_repair_button_t` | `` | 85x29 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/inventory_upgrade_16.xml:15` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_servers_list_button_e` | `` | 3x15 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabprofile.xml:30` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_servers_list_frame_t` | `` | 533x1 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabprofile.xml:36` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_shield_health` | `` | 29x36 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:57` | `<texture>` | кнопка |
| `ui_inGame2_text_leyer` | `` | 535x22 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:75` | `<texture>` | кнопка |
| `ui_inGame2_text_leyer_1` | `` | 210x15 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:68` | `<texture>` | кнопка |
| `ui_inGame2_text_leyer_2` | `` | 102x22 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:82` | `<texture>` | кнопка |
| `ui_inGame2_upgrade_on_icon_lamp_red_disabled` | `` | 5x38 | `configs/ui/textures_descr/ui_actor_upgrades.xml` | `configs/ui/inventory_upgrade_16.xml:41` | `<back_texture>` | кнопка |
| `ui_inGame2_upgrade_on_icon_lamp_yellow_highlighted` | `` | 5x38 | `configs/ui/textures_descr/ui_actor_upgrades.xml` | `configs/ui/inventory_upgrade_16.xml:26` | `<back_texture>` | кнопка |
| `ui_inGame2_upgrade_on_weapon_lamp_empty_highlighted` | `` | 14x14 | `configs/ui/textures_descr/ui_actor_upgrades.xml` | `configs/ui/inventory_upgrade_16.xml:22` | `<point_texture>` | кнопка |
| `ui_inGame2_upgrade_on_weapon_lamp_red_disabled` | `` | 12x12 | `configs/ui/textures_descr/ui_actor_upgrades.xml` | `configs/ui/inventory_upgrade_16.xml:42` | `<point_texture>` | кнопка |
| `ui_inGame2_upgrade_on_weapon_lamp_yellow_highlighted` | `` | 14x14 | `configs/ui/textures_descr/ui_actor_upgrades.xml` | `configs/ui/inventory_upgrade_16.xml:27` | `<point_texture>` | кнопка |
| `ui_inGame2_vote_button_d` | `` | 207x28 | `configs/ui/textures_descr/ui_actor_mp_ingame_menu.xml` | `configs/ui/voting_category_16.xml:17` | `<texture[auto-_d]>` | кнопка |
| `ui_inGame2_vote_button_e` | `` | 207x28 | `configs/ui/textures_descr/ui_actor_mp_ingame_menu.xml` | `configs/ui/voting_category_16.xml:17` | `<texture[auto-_e]>` | кнопка |
| `ui_inGame2_vote_button_h` | `` | 207x28 | `configs/ui/textures_descr/ui_actor_mp_ingame_menu.xml` | `configs/ui/voting_category_16.xml:17` | `<texture[auto-_h]>` | кнопка |
| `ui_inGame2_vote_button_t` | `` | 207x28 | `configs/ui/textures_descr/ui_actor_mp_ingame_menu.xml` | `configs/ui/voting_category_16.xml:17` | `<texture[auto-_t]>` | кнопка |
| `ui_inGame2_weapon_highlighter` | `` | 82x353 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:99` | `<texture>` | кнопка |
| `ui_inv_icon_explosion_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:111` | `<texture>` | кнопка |
| `ui_inv_icon_telepatic_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:101` | `<texture>` | кнопка |
| `ui_menu_options_dlg` | `` | 563x459 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_scenes_dlg.xml:4` | `<texture>` | кнопка |
| `ui_pda2_defend_base` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:127` | `<texture>` | кнопка |
| `ui_pda2_defend_base2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:103` | `<texture>` | кнопка |
| `ui_pda2_destroy_enemy2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:200` | `<texture>` | кнопка |
| `ui_pda2_exit_point` | `` | 19x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:114` | `<texture>` | кнопка |
| `ui_pda2_fr_e` | `` | 955x21 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_fraction_war.xml:5` | `<texture[auto-_e]>` | кнопка |
| `ui_pda2_hl_quest_base` | `` | 37x37 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:83` | `<texture>` | кнопка |
| `ui_pda2_hl_seq_quest2` | `` | 29x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:696` | `<texture>` | кнопка |
| `ui_pda2_pt_territory` | `` | 27x27 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:307` | `<texture>` | кнопка |
| `ui_pda2_secondary_task2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:10` | `<texture>` | кнопка |
| `ui_sega_healph` | `` | 325x25 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_custom_msgs.xml:151` | `<texture>` | кнопка |
| `ui_statOne_t` | `` | 446x95 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:18` | `<texture>` | кнопка |
| `ui_statTwo_t` | `` | 845x98 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:6` | `<texture>` | кнопка |
| `ui_task_bt_close_d` | `` | 15x15 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:194` | `<texture[auto-_d]>` | кнопка |
| `ui_task_bt_close_e` | `` | 15x15 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:194` | `<texture[auto-_e]>` | кнопка |
| `ui_task_bt_close_h` | `` | 15x15 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:194` | `<texture[auto-_h]>` | кнопка |
| `ui_task_bt_close_t` | `` | 15x15 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_tasks.xml:194` | `<texture[auto-_t]>` | кнопка |
| `ui_teambase` | `` | 29x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_mp.xml:59` | `<texture>` | кнопка |
| `ui_temp_ad3_artefact` | `` | 5x5 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/ui_detector_artefact.xml:10` | `<texture>` | кнопка |
| `ui_temp_ad3_radar_glow` | `` | 89x44 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/ui_detector_artefact.xml:5` | `<texture>` | кнопка |
| `ui_temp_ad4_mine_acidic` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/ui_detector_artefact.xml:139` | `<texture>` | кнопка |
| `ui_temp_ad4_mine_electric` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/ui_detector_artefact.xml:173` | `<texture>` | кнопка |
| `ui_temp_ad4_mine_gravitational` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/ui_detector_artefact.xml:203` | `<texture>` | кнопка |
| `ui_temp_ad4_mine_thermal` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/ui_detector_artefact.xml:221` | `<texture>` | кнопка |
| `ui_temp_frame_t` | `` | 50x50 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/inventory_upgrade_info_16.xml:6` | `<texture[auto-_t]>` | кнопка |
| `ui_wp_prop_damage` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/buy_menu_item.xml:58` | `<texture>` | кнопка |
| `ui_wp_prop_distantion` | `` | 19x19 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/actor_menu_item.xml:149` | `<texture>` | кнопка |
| `ui_wp_prop_ergonomics` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/buy_menu_item.xml:55` | `<texture>` | кнопка |
| `ui_wp_prop_tochnost` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/buy_menu_item.xml:52` | `<texture>` | кнопка |
| `ui_PDA_checker_d` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_logs_16.xml:48` | `<texture[auto-_d]>` | галочка |
| `ui_PDA_checker_e` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_logs_16.xml:48` | `<texture[auto-_e]>` | галочка |
| `ui_PDA_checker_h` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_logs_16.xml:48` | `<texture[auto-_h]>` | галочка |
| `ui_PDA_checker_t` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/pda_logs_16.xml:48` | `<texture[auto-_t]>` | галочка |
| `ui_inGame2_checkbox_d` | `` | 44x29 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_gamespy_16.xml:80` | `<texture[auto-_d]>` | галочка |
| `ui_inGame2_checkbox_e` | `` | 44x29 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_gamespy_16.xml:80` | `<texture[auto-_e]>` | галочка |
| `ui_inGame2_checkbox_h` | `` | 44x29 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_gamespy_16.xml:80` | `<texture[auto-_h]>` | галочка |
| `ui_inGame2_checkbox_t` | `` | 44x29 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_gamespy_16.xml:80` | `<texture[auto-_t]>` | галочка |
| `ui_cb_listbox_t` | `` | 10x10 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/inventory_new.xml:5` | `<texture[auto-_t]>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_horisontal_bckgrnd_e` | `` | 2x17 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:40` | `<texture[auto-_e]>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_horisontal_box_E_e` | `` | 2x13 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:43` | `<texture_e[auto-_e]>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_horisontal_box_H_T_e` | `` | 2x13 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:44` | `<texture_t[auto-_e]>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_horisontal_left_arrow_E` | `` | 18x17 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:19` | `<texture_e>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_horisontal_left_arrow_H_T` | `` | 18x17 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:20` | `<texture_h>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_horisontal_right_arrow_E` | `` | 18x17 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:24` | `<texture_e>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_horisontal_right_arrow_H_T` | `` | 18x17 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:25` | `<texture_h>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_vertical_bckgrnd_e` | `` | 17x2 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:48` | `<texture[auto-_e]>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_vertical_bottom_arrow_E` | `` | 17x26 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:34` | `<texture_e>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_vertical_bottom_arrow_H_T` | `` | 17x26 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:35` | `<texture_h>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_vertical_box_E_e` | `` | 13x2 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:51` | `<texture_e[auto-_e]>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_vertical_box_H_T_e` | `` | 13x2 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:52` | `<texture_t[auto-_e]>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_vertical_top_arrow_E` | `` | 17x18 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:29` | `<texture_e>` | фон списка |
| `ui_inGame2_pda_map_scrollbar_vertical_top_arrow_H_T` | `` | 17x18 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/scroll_bar.xml:30` | `<texture_h>` | фон списка |
| `ui_scroll_back_e` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:10` | `<texture[auto-_e]>` | фон списка |
| `ui_scroll_box_e` | `` | 15x8 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:13` | `<texture[auto-_e]>` | фон списка |
| `ui_scroll_btn_down` | `` | 15x16 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:7` | `<texture_e>` | фон списка |
| `ui_scroll_btn_up` | `` | 15x16 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/scroll_bar.xml:4` | `<texture_e>` | фон списка |
| `ui_statTwo_tabdiv_l` | `` | 402x25 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:60` | `<texture>` | вкладка |
| `ui_tablist_textbox_t` | `` | 32x32 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/ui_mm_mp_offline_16.xml:68` | `<texture[auto-_t]>` | вкладка |
| `ui_buymenu_progBar` | `` | 151x7 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/buy_menu_item.xml:37` | `<texture>` | полоса прогресса |
| `ui_inGame2_demo_player_progress_bar` | `` | 402x4 | `configs/ui/textures_descr/ui_actor_mp_screen.xml` | `configs/ui/demo_play_control.xml:60` | `<texture>` | полоса прогресса |
| `ui_inGame2_inventory_progress_bar` | `` | 122x16 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:428` | `<texture>` | полоса прогресса |
| `ui_mm_loading_progress_bar` | `` | 506x4 | `configs/ui/textures_descr/ui_mm_loading_screen.xml` | `configs/ui/ui_mm_loading_screen.xml:12` | `<texture>` | полоса прогресса |
| `ui_patch_progress` | `` | 403x10 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/ui_mm_opt_16.xml:822` | `<texture>` | полоса прогресса |
| `ui_pda2_big_progress` | `` | 217x57 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/pda_fraction_war.xml:69` | `<texture>` | полоса прогресса |
| `ui_pda2_big_progress2` | `` | 217x57 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/pda_fraction_war.xml:72` | `<texture>` | полоса прогресса |
| `ui_pda2_small_progress` | `` | 432x38 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/pda_fraction_war.xml:86` | `<texture>` | полоса прогресса |
| `ui_pda2_small_progress2` | `` | 432x38 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/pda_fraction_war.xml:89` | `<texture>` | полоса прогресса |
| `ui_sega_healph_progress` | `` | 254x10 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/strelok_progress.xml:5` | `<texture>` | полоса прогресса |
| `storyline_task_spot_above` | `` | 19x16 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/map_spots.xml:594` | `<texture_above>` | разделитель |
| `storyline_task_spot_below` | `` | 19x16 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/map_spots.xml:593` | `<texture_below>` | разделитель |
| `ui_inGame2_center_trade_devider` | `` | 341x163 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:227` | `<texture>` | разделитель |
| `ui_inGame2_empty_frameline_15_e` | `` | 1x15 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:782` | `<texture[auto-_e]>` | разделитель |
| `ui_inGame2_hint_wnd_bar_alfa_line` | `` | 128x9 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/buy_menu_item.xml:78` | `<texture>` | разделитель |
| `ui_inGame2_hint_wnd_bar_alfa_line_16` | `` | 101x9 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu_item.xml:163` | `<texture>` | разделитель |
| `ui_inGame2_pda_line_horizontal_e` | `` | 1x1 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/ui_mm_mp_tabprofile.xml:112` | `<texture[auto-_e]>` | разделитель |
| `ui_inGame2_pda_line_vertical_e` | `` | 1x1 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/ui_mm_mp_tabprofile.xml:109` | `<texture[auto-_e]>` | разделитель |
| `ui_inGame2_pda_map_devider_e` | `` | 1x3 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:12` | `<texture[auto-_e]>` | разделитель |
| `ui_inGame2_pda_map_devider_line` | `` | 1x3 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_tasks.xml:15` | `<texture>` | разделитель |
| `ui_inGame2_pda_offline_e` | `` | 33x34 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_16.xml:29` | `<texture_e>` | разделитель |
| `ui_inGame2_pda_offline_h` | `` | 33x34 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_16.xml:30` | `<texture_h>` | разделитель |
| `ui_inGame2_pda_offline_t` | `` | 33x34 | `configs/ui/textures_descr/ui_actor_pda.xml` | `configs/ui/pda_16.xml:28` | `<texture_t>` | разделитель |
| `ui_inGame2_servers_list_button_devider_e` | `` | 3x1 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabclient_16.xml:22` | `<texture[auto-_e]>` | разделитель |
| `ui_pda2_fr_delimiter_fraction` | `` | 955x103 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_fraction_war.xml:9` | `<texture>` | разделитель |
| `ui_pda2_line_d_e` | `` | 9x4 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_fraction_war.xml:135` | `<texture[auto-_e]>` | разделитель |
| `ui_pda2_line_h_e` | `` | 9x3 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_fraction_war.xml:132` | `<texture[auto-_e]>` | разделитель |
| `ui_pda2_line_v_e` | `` | 3x11 | `configs/ui/textures_descr/ui_pda2.xml` | `configs/ui/pda_fraction_war.xml:138` | `<texture[auto-_e]>` | разделитель |
| `freedom_big` | `` | 249x194 | `configs/ui/textures_descr/ui_logos.xml` | `configs/ui/spawn_16.xml:18` | `<texture>` | прочее |
| `merc_big` | `` | 249x194 | `configs/ui/textures_descr/ui_logos.xml` | `configs/ui/spawn_16.xml:21` | `<texture>` | прочее |
| `mp_award_avenger` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:470` | `<texture>` | прочее |
| `mp_award_black_list` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:570` | `<texture>` | прочее |
| `mp_award_blitzkrieg` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:70` | `<texture>` | прочее |
| `mp_award_cherub` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:490` | `<texture>` | прочее |
| `mp_award_climber` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:250` | `<texture>` | прочее |
| `mp_award_invincible_fury` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:310` | `<texture>` | прочее |
| `mp_award_lightning_reflexes` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:350` | `<texture>` | прочее |
| `mp_award_lucky` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:550` | `<texture>` | прочее |
| `mp_award_mad` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:130` | `<texture>` | прочее |
| `mp_award_marksman` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:390` | `<texture>` | прочее |
| `mp_award_massacre` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:7` | `<texture>` | прочее |
| `mp_award_multichampion` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:110` | `<texture>` | прочее |
| `mp_award_oculist` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:330` | `<texture>` | прочее |
| `mp_award_opener` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:270` | `<texture>` | прочее |
| `mp_award_overwhelming_superiority` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:49` | `<texture>` | прочее |
| `mp_award_paranoia` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:28` | `<texture>` | прочее |
| `mp_award_peace_ambassador` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:410` | `<texture>` | прочее |
| `mp_award_remembrance` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:450` | `<texture>` | прочее |
| `mp_award_skewer` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:210` | `<texture>` | прочее |
| `mp_award_sprinter_stopper` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:370` | `<texture>` | прочее |
| `mp_award_stalker_flair` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:530` | `<texture>` | прочее |
| `mp_award_unknown` | `` | 121x121 | `configs/ui/textures_descr/ui_mp_achivements.xml` | `configs/ui/ui_mp_awards.xml:18` | `<texture>` | прочее |
| `ui_actor_overlay` | `` | 165x108 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/talk_character_16.xml:4` | `<texture>` | прочее |
| `ui_alife_combat` | `` | 32x32 | `configs/ui/textures_descr/ui_alife.xml` | `configs/ui/map_spots.xml:217` | `<texture>` | прочее |
| `ui_am_condition` | `` | 19x19 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/af_params.xml:7` | `<texture>` | прочее |
| `ui_am_prop_Vibros` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/booster_params.xml:39` | `<texture>` | прочее |
| `ui_am_prop_artefact` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/af_params.xml:118` | `<texture>` | прочее |
| `ui_am_prop_chem` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/af_params.xml:108` | `<texture>` | прочее |
| `ui_am_prop_radio_restore` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/af_params.xml:29` | `<texture_minus>` | прочее |
| `ui_am_prop_restore_bleeding` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/af_params.xml:57` | `<texture>` | прочее |
| `ui_am_prop_satiety_restore_speed` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/af_params.xml:37` | `<texture>` | прочее |
| `ui_am_propery_01` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu_item.xml:380` | `<texture>` | прочее |
| `ui_am_propery_05` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/af_params.xml:16` | `<texture>` | прочее |
| `ui_am_propery_07` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/af_params.xml:47` | `<texture>` | прочее |
| `ui_am_propery_08` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/af_params.xml:128` | `<texture>` | прочее |
| `ui_am_propery_09` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/af_params.xml:26` | `<texture>` | прочее |
| `ui_am_propery_11` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/af_params.xml:98` | `<texture>` | прочее |
| `ui_car_panel_light` | `` | 22x22 | `configs/ui/textures_descr/ui_ixray_ex.xml` | `configs/ui/car_panel.xml:29` | `<texture>` | прочее |
| `ui_credits_left_widepanel` | `` | 128x768 | `configs/ui/textures_descr/ui_actor_main_menu.xml` | `configs/ui/ui_credits_16.xml:19` | `<texture>` | прочее |
| `ui_credits_right_widepanel` | `` | 128x768 | `configs/ui/textures_descr/ui_actor_main_menu.xml` | `configs/ui/ui_credits_16.xml:22` | `<texture>` | прочее |
| `ui_fm_base_bonuse` | `` | 1x1 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_fraction_war.xml:125` | `<texture>` | прочее |
| `ui_fm_over_chars` | `` | 165x108 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_logs_16.xml:22` | `<texture>` | прочее |
| `ui_fm_over_fraction_logo_l` | `` | 251x196 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_fraction_war.xml:33` | `<texture>` | прочее |
| `ui_fm_over_fraction_logo_r` | `` | 251x196 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_fraction_war.xml:36` | `<texture>` | прочее |
| `ui_fraction_overlay` | `` | 48x48 | `configs/ui/textures_descr/ui_inventory2.xml` | `configs/ui/talk_character_16.xml:14` | `<texture>` | прочее |
| `ui_icons_newPDA_SmallBlue` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:50` | `<texture>` | прочее |
| `ui_icons_newPDA_SmallGreen` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:63` | `<texture>` | прочее |
| `ui_icons_newPDA_SmallRed` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:76` | `<texture>` | прочее |
| `ui_icons_newPDA_man` | `` | 26x26 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:99` | `<texture>` | прочее |
| `ui_icons_newPDA_manArrow` | `` | 49x49 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:106` | `<texture>` | прочее |
| `ui_inGame2_GameSpy_logo` | `` | 130x30 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/ui_mm_mp_gamespy_16.xml:19` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Actor_Box` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:761` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Actor_Box_small` | `` | 15x14 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:764` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Primary_mission` | `` | 23x23 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:582` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Secondary_mission` | `` | 23x23 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:603` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Stalker_Medic` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:750` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Stalker_Medic_small` | `` | 15x14 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:753` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Stalker_VIP` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:739` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Stalker_guide` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:728` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Stalker_guide_small` | `` | 15x14 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:731` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Stalker_machanik` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:706` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_Stalker_machanik_small` | `` | 15x14 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:709` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_location` | `` | 205x205 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:783` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_location_legend` | `` | 41x41 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/pda_tasks.xml:254` | `<texture>` | прочее |
| `ui_inGame2_PDA_icon_secret` | `` | 25x25 | `configs/ui/textures_descr/ui_actor_pda_icons.xml` | `configs/ui/map_spots.xml:172` | `<texture>` | прочее |
| `ui_inGame2_Radar_blue_bar` | `` | 206x206 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/motion_icon.xml:8` | `<texture>` | прочее |
| `ui_inGame2_Radar_compass` | `` | 9x30 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/zone_map.xml:14` | `<texture>` | прочее |
| `ui_inGame2_Radar_green_bar` | `` | 206x206 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/motion_icon.xml:5` | `<texture>` | прочее |
| `ui_inGame2_arrow_left` | `` | 10x9 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:101` | `<texture>` | прочее |
| `ui_inGame2_arrow_right` | `` | 10x9 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:104` | `<texture>` | прочее |
| `ui_inGame2_arrow_up` | `` | 9x10 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_tabserver_16.xml:107` | `<texture>` | прочее |
| `ui_inGame2_artefact_blocker` | `` | 59x59 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:253` | `<texture>` | прочее |
| `ui_inGame2_bleeding_inv_green` | `` | 45x45 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu.xml:400` | `<texture>` | прочее |
| `ui_inGame2_bleeding_inv_red` | `` | 45x45 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu.xml:406` | `<texture>` | прочее |
| `ui_inGame2_bleeding_inv_yellow` | `` | 45x45 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu.xml:403` | `<texture>` | прочее |
| `ui_inGame2_blood_icon_part_1` | `` | 13x20 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:176` | `<texture>` | прочее |
| `ui_inGame2_blood_icon_part_2` | `` | 17x26 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:179` | `<texture>` | прочее |
| `ui_inGame2_blood_icon_part_3` | `` | 23x33 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:182` | `<texture>` | прочее |
| `ui_inGame2_inventory_item_status_bar` | `` | 38x5 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu_item.xml:255` | `<texture>` | прочее |
| `ui_inGame2_inventory_item_status_bar_16` | `` | 30x5 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu_item_16.xml:241` | `<texture>` | прочее |
| `ui_inGame2_inventory_status_bar` | `` | 58x5 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:107` | `<texture>` | прочее |
| `ui_inGame2_lamp_OFF` | `` | 37x25 | `configs/ui/textures_descr/ui_actor_multiplayer_menu_screen.xml` | `configs/ui/ui_mm_mp_gamespy_16.xml:104` | `<texture>` | прочее |
| `ui_inGame2_left_side` | `` | 341x768 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/actor_menu.xml:17` | `<texture>` | прочее |
| `ui_inGame2_left_widepanel` | `` | 128x768 | `configs/ui/textures_descr/ui_actor_main_menu.xml` | `configs/ui/ui_mm_main_16.xml:13` | `<texture>` | прочее |
| `ui_inGame2_load_info` | `` | 501x180 | `configs/ui/textures_descr/ui_ingame2_back_02.xml` | `configs/ui/ui_scenes_dlg.xml:48` | `<texture>` | прочее |
| `ui_inGame2_marker` | `` | 30x118 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/ui_sleep_dialog.xml:15` | `<texture>` | прочее |
| `ui_inGame2_message_box` | `` | 677x267 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/pda_spot.xml:4` | `<texture>` | прочее |
| `ui_inGame2_opt_left_widepanel` | `` | 128x768 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:10` | `<texture>` | прочее |
| `ui_inGame2_opt_right_widepanel` | `` | 128x768 | `configs/ui/textures_descr/ui_actor_main_menu_options.xml` | `configs/ui/ui_mm_opt_16.xml:13` | `<texture>` | прочее |
| `ui_inGame2_radiation_icon_part_1` | `` | 21x21 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:186` | `<texture>` | прочее |
| `ui_inGame2_radiation_icon_part_2` | `` | 25x25 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:189` | `<texture>` | прочее |
| `ui_inGame2_radiation_icon_part_3` | `` | 31x31 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:192` | `<texture>` | прочее |
| `ui_inGame2_radiation_inv_green` | `` | 45x45 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu.xml:413` | `<texture>` | прочее |
| `ui_inGame2_radiation_inv_red` | `` | 45x45 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu.xml:419` | `<texture>` | прочее |
| `ui_inGame2_radiation_inv_yellow` | `` | 45x45 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/actor_menu.xml:416` | `<texture>` | прочее |
| `ui_inGame2_repair_panel` | `` | 341x768 | `configs/ui/textures_descr/ui_actor_menu.xml` | `configs/ui/inventory_upgrade_16.xml:4` | `<texture>` | прочее |
| `ui_inGame2_right_widepanel` | `` | 128x768 | `configs/ui/textures_descr/ui_actor_main_menu.xml` | `configs/ui/ui_mm_main_16.xml:16` | `<texture>` | прочее |
| `ui_inGame2_shield_Psy` | `` | 31x41 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:37` | `<texture>` | прочее |
| `ui_inGame2_shield_Radiation` | `` | 31x41 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:41` | `<texture>` | прочее |
| `ui_inGame2_shield_biological` | `` | 31x41 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:45` | `<texture>` | прочее |
| `ui_inGame2_shield_blood` | `` | 29x36 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:49` | `<texture>` | прочее |
| `ui_inGame2_shield_force` | `` | 29x36 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:53` | `<texture>` | прочее |
| `ui_inGame2_shield_radiation_cleanup` | `` | 29x36 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:65` | `<texture>` | прочее |
| `ui_inGame2_shield_stamina` | `` | 29x36 | `configs/ui/textures_descr/ui_actor_hint_wnd.xml` | `configs/ui/maingame_16.xml:61` | `<texture>` | прочее |
| `ui_inGame2_sky_panorama` | `` | 591x118 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/ui_sleep_dialog.xml:7` | `<texture>` | прочее |
| `ui_inGame2_slider` | `` | 274x61 | `configs/ui/textures_descr/ui_actor_main_menu.xml` | `configs/ui/ui_mm_main_16.xml:23` | `<texture>` | прочее |
| `ui_inGame2_small_plane_1` | `` | 223x212 | `configs/ui/textures_descr/ui_actor_mp_ingame_menu.xml` | `configs/ui/voting_category_16.xml:6` | `<texture>` | прочее |
| `ui_inGame2_upgrade_on_icon_lamp_green_upgraded` | `` | 5x38 | `configs/ui/textures_descr/ui_actor_upgrades.xml` | `configs/ui/inventory_upgrade_16.xml:36` | `<back_texture>` | прочее |
| `ui_inGame2_upgrade_on_weapon_lamp_green_upgraded` | `` | 12x12 | `configs/ui/textures_descr/ui_actor_upgrades.xml` | `configs/ui/inventory_upgrade_16.xml:37` | `<point_texture>` | прочее |
| `ui_inv_icon_burn_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:76` | `<texture>` | прочее |
| `ui_inv_icon_chemical_burn_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:106` | `<texture>` | прочее |
| `ui_inv_icon_fire_wound_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:116` | `<texture>` | прочее |
| `ui_inv_icon_radiation_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:96` | `<texture>` | прочее |
| `ui_inv_icon_shock_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:86` | `<texture>` | прочее |
| `ui_inv_icon_strike_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:81` | `<texture>` | прочее |
| `ui_inv_icon_wound_immunity` | `` | 18x18 | `configs/ui/textures_descr/ui_hud.xml` | `configs/ui/inventory_new.xml:91` | `<texture>` | прочее |
| `ui_inventory_main` | `` | 1005x483 | `configs/ui/textures_descr/ui_inventory.xml` | `configs/ui/inventory_new.xml:19` | `<texture>` | прочее |
| `ui_inventory_rank` | `` | 75x65 | `configs/ui/textures_descr/ui_inventory.xml` | `configs/ui/inventory_new.xml:125` | `<texture>` | прочее |
| `ui_magnifier2` | `` | 207x24 | `configs/ui/textures_descr/ui_magnifier2.xml` | `configs/ui/ui_mm_main_16.xml:25` | `<texture>` | прочее |
| `ui_magnifier3` | `` | 207x32 | `configs/ui/textures_descr/ui_magnifier2.xml` | `configs/ui/ui_mm_main.xml:19` | `<texture>` | прочее |
| `ui_mapQuest_gold` | `` | 29x29 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:184` | `<texture>` | прочее |
| `ui_microphone_icon` | `` | 64x64 | `configs/ui/textures_descr/ui_microphone.xml` | `configs/ui/maingame_16.xml:22` | `<texture>` | прочее |
| `ui_mini_af_spot` | `` | 13x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_mp.xml:79` | `<texture>` | прочее |
| `ui_mini_af_spot_above` | `` | 13x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_mp.xml:114` | `<texture_above>` | прочее |
| `ui_mini_af_spot_below` | `` | 13x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_mp.xml:113` | `<texture_below>` | прочее |
| `ui_mini_sn_spot_above` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:52` | `<texture_above>` | прочее |
| `ui_mini_sn_spot_below` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:51` | `<texture_below>` | прочее |
| `ui_minimap_point` | `` | 12x12 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:226` | `<texture>` | прочее |
| `ui_minimap_squad_leader` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:472` | `<texture>` | прочее |
| `ui_mm_loading_left_widepanel` | `` | 128x768 | `configs/ui/textures_descr/ui_mm_loading_screen.xml` | `configs/ui/ui_mm_loading_screen_16.xml:7` | `<texture>` | прочее |
| `ui_mm_loading_right_widepanel` | `` | 128x768 | `configs/ui/textures_descr/ui_mm_loading_screen.xml` | `configs/ui/ui_mm_loading_screen_16.xml:10` | `<texture>` | прочее |
| `ui_mmap_base` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:332` | `<texture>` | прочее |
| `ui_mmap_common_actor` | `` | 7x7 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_relations.xml:18` | `<texture>` | прочее |
| `ui_mmap_quest` | `` | 15x15 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:658` | `<texture>` | прочее |
| `ui_mmap_secondary_alert` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:45` | `<texture>` | прочее |
| `ui_mmap_squad_leader` | `` | 9x9 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:506` | `<texture>` | прочее |
| `ui_mmap_stask_last_02` | `` | 17x17 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:591` | `<texture>` | прочее |
| `ui_numpad_key00` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:14` | `<texture_t>` | прочее |
| `ui_numpad_key01` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:18` | `<texture_t>` | прочее |
| `ui_numpad_key02` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:22` | `<texture_t>` | прочее |
| `ui_numpad_key03` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:26` | `<texture_t>` | прочее |
| `ui_numpad_key04` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:30` | `<texture_t>` | прочее |
| `ui_numpad_key05` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:34` | `<texture_t>` | прочее |
| `ui_numpad_key06` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:38` | `<texture_t>` | прочее |
| `ui_numpad_key07` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:42` | `<texture_t>` | прочее |
| `ui_numpad_key08` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:46` | `<texture_t>` | прочее |
| `ui_numpad_key09` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:50` | `<texture_t>` | прочее |
| `ui_numpad_key0b` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:59` | `<texture_t>` | прочее |
| `ui_numpad_key0c` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:55` | `<texture_t>` | прочее |
| `ui_numpad_keyCancel` | `` | 111x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:67` | `<texture_t>` | прочее |
| `ui_numpad_keyEnter` | `` | 111x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:63` | `<texture_t>` | прочее |
| `ui_numpad_void` | `` | 72x45 | `configs/ui/textures_descr/ui_numpad.xml` | `configs/ui/ui_numpad_wnd.xml:13` | `<texture_e>` | прочее |
| `ui_pda2_attack_base2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:60` | `<texture>` | прочее |
| `ui_pda2_base` | `` | 21x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:329` | `<texture>` | прочее |
| `ui_pda2_bring_item2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:143` | `<texture>` | прочее |
| `ui_pda2_capture_base2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:84` | `<texture>` | прочее |
| `ui_pda2_info2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:219` | `<texture>` | прочее |
| `ui_pda2_noice` | `` | 973x683 | `configs/ui/textures_descr/ui_pda2_noice.xml` | `configs/ui/pda_16.xml:20` | `<texture>` | прочее |
| `ui_pda2_pt_resource` | `` | 27x27 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:345` | `<texture>` | прочее |
| `ui_pda2_pt_science` | `` | 27x27 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:339` | `<texture>` | прочее |
| `ui_pda2_quest` | `` | 21x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:655` | `<texture>` | прочее |
| `ui_pda2_secondary_alert2` | `` | 19x19 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:34` | `<texture>` | прочее |
| `ui_pda2_sq_sos` | `` | 27x27 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:681` | `<texture>` | прочее |
| `ui_pda2_squad_leader` | `` | 11x11 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:469` | `<texture>` | прочее |
| `ui_pda2_stask_last_01` | `` | 21x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:794` | `<texture>` | прочее |
| `ui_pda2_stask_last_01a` | `` | 25x25 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:623` | `<texture>` | прочее |
| `ui_pda2_stask_last_02` | `` | 21x21 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:584` | `<texture>` | прочее |
| `ui_pda2_stask_last_02a` | `` | 25x25 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots_complex.xml:17` | `<texture>` | прочее |
| `ui_sm_mapQuest_gold` | `` | 13x13 | `configs/ui/textures_descr/ui_common.xml` | `configs/ui/map_spots.xml:187` | `<texture>` | прочее |
| `ui_statOne_b` | `` | 447x58 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:24` | `<texture>` | прочее |
| `ui_statTwo_b` | `` | 845x98 | `configs/ui/textures_descr/ui_statistics.xml` | `configs/ui/stats.xml:12` | `<texture>` | прочее |
| `ui_upgrade_arrow2` | `` | 16x16 | `configs/ui/textures_descr/ui_icon_equipment.xml` | `configs/ui/actor_menu_item.xml:242` | `<texture>` | прочее |
| `ui_wp_prop_skorostrelnost` | `` | 19x19 | `configs/ui/textures_descr/ui_actor_sleep_screen.xml` | `configs/ui/buy_menu_item.xml:61` | `<texture>` | прочее |
| `ui_wp_propery_07` | `` | 19x19 | `configs/ui/textures_descr/ui_ingame2_common.xml` | `configs/ui/actor_menu_item.xml:64` | `<texture>` | прочее |
| `wpn_crosshair` | `` | 1024x1024 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes.xml:4` | `<texture>` | прочее |
| `wpn_crosshair_add_l` | `` | 102x768 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes_16.xml:7` | `<texture>` | прочее |
| `wpn_crosshair_add_r` | `` | 102x768 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes_16.xml:10` | `<texture>` | прочее |
| `wpn_crosshair_bino` | `` | 1024x1024 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes.xml:28` | `<texture>` | прочее |
| `wpn_crosshair_g36` | `` | 1024x1024 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes.xml:16` | `<texture>` | прочее |
| `wpn_crosshair_l85` | `` | 1024x1024 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes.xml:10` | `<texture>` | прочее |
| `wpn_crosshair_rpg` | `` | 1024x1024 | `configs/ui/textures_descr/ui_ingame.xml` | `configs/ui/scopes.xml:22` | `<texture>` | прочее |
