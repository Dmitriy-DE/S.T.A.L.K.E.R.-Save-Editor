# Каталог родных элементов интерфейса: Чистое Небо (CS / ЧН)

## 1. Архитектура UI в Чистом Небе

В S.T.A.L.K.E.R.: Clear Sky (движок X-Ray 1.5) графический интерфейс существенно доработан по сравнению с ТЧ.

Ключевые особенности и отличия:
1. **Файлы описания текстур**: Все XML-файлы с описанием текстур `<ui_texture>` перенесены в поддиректорию `configs/ui/textures_descr/*.xml`.
2. **Скриптовая инициализация шкал**: В класс `CScriptXmlInit` добавлен метод `InitProgressBar(path, parent)` (`gamedata_cs/scripts/lua_help.script:5783`), что устранило ограничение ТЧ и позволило создавать шкалы прогресса прямо из Lua.
3. **Списки**: Для списков по-прежнему используется `CUIListWnd` (`lua_help.script:6562`) и `InitList` (`lua_help.script:5779`).
4. **Управление окнами**: Открытие и закрытие окон диалогов, как и в ТЧ, выполняется через `start_stop_menu` (`lua_help.script:5810`).
5. **Поддержка широкоформатных экранов**: Появились отдельные XML-файлы для пропорций 16:9 / 16:10 с суффиксом `_16.xml`.

---

## 2. Стандартные элементы управления и их XML-структура

### 2.1. Кнопка с тремя состояниями (`Init3tButton`)
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

### 2.2. Галочка / Чекбокс (`InitCheck`)
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

### 2.3. Вкладки (`InitTab`)
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

### 2.4. Полоса прогресса (`InitProgressBar`)
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

### 2.5. Поле ввода (`InitEditBox`)
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

## 3. Шрифты в Чистом Небе

### 3.1. Функции `GetFont*` из `lua_help.script`
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

### 3.2. Имена шрифтов в ванильных XML (`font="..."`)
В XML ЧН используются:
`arial_14`, `graffiti19`, `graffiti22`, `graffiti32`, `graffiti50`, `letterica16`, `letterica18`, `letterica25`, `medium`.
Конфигурация начертаний и размеров описана в `configs/fonts.ltx:1-105`.

---

## 4. Каталог текстур родного интерфейса (ЧН)

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
