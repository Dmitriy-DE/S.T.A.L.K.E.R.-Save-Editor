# Каталог родных элементов интерфейса: Тень Чернобыля (SHoC / ТЧ)

## 1. Архитектура UI в Тени Чернобыля

В S.T.A.L.K.E.R.: Shadow of Chernobyl (движок X-Ray 1.0 / OGSR) графический интерфейс описывается через XML-файлы конфигураций и связывается с Lua через класс `CScriptXmlInit` (`gamedata_soc/scripts/lua_help.script:5644`).

Ключевые особенности и отличия от ЧН и ЗП:
1. **Файлы описания текстур**: В ТЧ большинство описаний текстур расположено непосредственно в корневой папке `configs/ui/` в файлах `ui_common.xml`, `ui_old_textures.xml`, `ui_iconstotal.xml` и др. под тегом `<ui_texture>` (в ЧН и ЗП они перенесены в поддиректорию `textures_descr/`).
2. **Списки**: Для списков используется класс `CUIListWnd` (`lua_help.script:6477`) и метод `xml:InitList(path, parent)` (`lua_help.script:5650`). Современный класс `CUIListBox` из ЗП в ТЧ **отсутствует** (0 совпадений в `lua_help.script`).
3. **Шкалы прогресса**: Метод `InitProgressBar` в `CScriptXmlInit` для Lua в ванильном ТЧ **отсутствует** (`lua_help.script:5644-5670`). Хотя класс `CUIProgressBar` существует в C++ движка (`xrServerEntities` / `xrUICore`) и представлен в `lua_help.script:6901`, скрипты ТЧ не могли вызывать `xml:InitProgressBar(...)`. Шкалы инициализировались либо внутренним кодом C++ (инвентарь, экран обыска), либо собирались скриптами вручную через `CUIStatic`.
4. **Управление окнами**: Открытие и закрытие окон диалогов осуществляется через `self:GetHolder():start_stop_menu(self, true)` (`lua_help.script:5701`), в отличие от ЗП, где используются `ShowDialog` / `HideDialog`.
5. **Кнопки**: В ТЧ поддерживаются как простые кнопки `InitButton` (`lua_help.script:5668`), так и кнопки с тремя состояниями `Init3tButton` (`lua_help.script:5664`).

---

## 2. Стандартные элементы управления и их XML-структура

### 2.1. Кнопка с тремя состояниями (`Init3tButton`)
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

### 2.2. Галочка / Чекбокс (`InitCheck`)
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

### 2.3. Вкладки (`InitTab`)
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

### 2.4. Полоса прогресса (`CUIProgressBar` / XML `<progress>`)
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

### 2.5. Поле ввода (`InitEditBox`)
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

## 3. Шрифты в Тень Чернобыля

### 3.1. Функции `GetFont*` из `lua_help.script`
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

### 3.2. Имена шрифтов в ванильных XML (`font="..."`)
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

## 4. Каталог текстур родного интерфейса (ТЧ)

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
