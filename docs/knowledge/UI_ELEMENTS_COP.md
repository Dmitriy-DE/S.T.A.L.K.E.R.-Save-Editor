# Каталог родных элементов интерфейса: Зов Припяти (CoP / ЗП)

## 1. Архитектура UI в Зове Припяти

В S.T.A.L.K.E.R.: Call of Pripyat (движок X-Ray 1.6) UI был полностью переработан под широкоформатные экраны и новую концепцию списков и окон.

Ключевые особенности и отличия:
1. **Переход на `CUIListBox`**: Устаревший класс `CUIListWnd` полностью удалён. Вместо него внедрён `CUIListBox` (`gamedata/scripts/lua_help.script:6477`), наследующийся от `CUIScrollView`. Инициализация выполняется методом `xml:InitListBox(path, parent)` (`lua_help.script:5744`). Элементы создаются через `CUIListBoxItem`.
2. **Отказ от `InitButton`**: Метод `InitButton` удалён из `CScriptXmlInit`. Единственным типом кнопок стал `Init3tButton` (`lua_help.script:5757`), возвращающий `CUI3tButton`.
3. **Управление диалогами**: Устаревший метод `start_stop_menu` заменён на стандартные `self:ShowDialog(true)` и `self:HideDialog()` (`lua_help.script:6464-6465`).
4. **Текстовые элементы**: Появился `InitTextWnd` (`lua_help.script:5737`), возвращающий легковесный `CUITextWnd`.

---

## 2. Стандартные элементы управления и их XML-структура

### 2.1. Кнопка с тремя состояниями (`Init3tButton`)
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

### 2.2. Галочка / Чекбокс (`InitCheck`)
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

### 2.3. Вкладки (`InitTab`)
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

### 2.4. Полоса прогресса (`InitProgressBar`)
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

### 2.5. Поле ввода (`InitEditBox`)
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

## 3. Шрифты в Зове Припяти

### 3.1. Функции `GetFont*` из `lua_help.script`
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

### 3.2. Имена шрифтов в ванильных XML (`font="..."`)
В XML ЗП используются:
`arial_14`, `font_graffiti`, `graffiti19`, `graffiti22`, `graffiti32`, `graffiti50`, `letterica16`, `letterica18`, `letterica25`, `medium`.
Конфигурация начертаний и размеров описана в `configs/fonts.ltx:1-105`.

---

## 4. Каталог текстур родного интерфейса (ЗП)

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
