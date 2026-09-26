# Формат пакетов `se_level_changer` и телепортация между уровнями

## 1. Введение и постановка задачи

В моде-компаньоне для перемещения игрока между локациями используется механизм создания временного объекта `level_changer` непосредственно под координатами актора. При соприкосновении с формой перехода (шейпом) движок осуществляет перенос игрока на целевую локацию.

Однако сериализация состояния `level_changer` через сетевые пакеты (`net_packet`: `STATE_Write` / `STATE_Read`) различается между играми трилогии (ТЧ, ЧН, ЗП).

---

## 2. Порядок полей в движке и скриптах

### 2.1. Исходники движка (C++)

Класс переходника: `CSE_ALifeLevelChanger`, наследующий `CSE_ALifeSpaceRestrictor` -> `CSE_Shape` -> `CSE_ALifeObject` -> `CSE_Abstract`.

#### Исходники:
1. **Зов Припяти (CoP / X-Ray 1.6)**:
   - `OpenXRay/xray-16`: `src/xrServerEntities/xrServer_Objects_ALife.cpp:766-805`
   - Заголовочный файл: `src/xrServerEntities/xrServer_Objects_ALife.h:400-415`
   - Номер ревизии формата: `xrServer_Objects.h:153` (`// 117 - CSE_ALifeLevelChanger appended with property m_bSilentMode`).
2. **Чистое Небо (CS / X-Ray 1.5)**:
   - `OpenXRay/xray-15`: `cs/engine/xrServerEntities/xrServer_Objects_ALife.cpp:1376-1413`
   - Заголовочный файл: `cs/engine/xrServerEntities/xrServer_Objects.h:151` (`// 117 - CSE_ALifeLevelChanger appended with property m_bSilentMode`).
3. **Тень Чернобыля (SHoC / X-Ray 1.0 / OGSR)**:
   - `OGSR/OGSR-Engine`: `ogsr_engine/COMMON_AI/xrServer_Objects_ALife.cpp:454-491`
   - Заголовочный файл: `ogsr_engine/COMMON_AI/xrServer_Objects.h:151,154` (`#define SPAWN_VERSION u16(118)`).

#### Сериализация C++ `CSE_ALifeLevelChanger::STATE_Write`:
```cpp
void CSE_ALifeLevelChanger::STATE_Write(NET_Packet& tNetPacket)
{
    inherited::STATE_Write(tNetPacket);
    tNetPacket.w_u16(m_tNextGraphID);           // u16: game_vertex_id назначения
    tNetPacket.w_u32(m_dwNextNodeID);           // u32 / s32: level_vertex_id назначения
    tNetPacket.w_float(m_tNextPosition.x);      // float: координаты назначения X
    tNetPacket.w_float(m_tNextPosition.y);      // float: координаты назначения Y
    tNetPacket.w_float(m_tNextPosition.z);      // float: координаты назначения Z
    tNetPacket.w_vec3(m_tAngles);               // vec3: направление взгляда (pitch, yaw, roll)
    tNetPacket.w_stringZ(m_caLevelToChange);    // stringZ: имя целевого уровня (напр. "zaton")
    tNetPacket.w_stringZ(m_caLevelPointToChange);// stringZ: имя граунд-поинта (напр. "start_actor_02")
    tNetPacket.w_u8(m_bSilentMode ? 1 : 0);     // u8: флаг тихого перехода (без всплывающего вопроса "Перейти?")
}
```

---

### 2.2. Скриптовая обёртка `se_level_changer`

#### Зов Припяти (CoP) и Чистое Небо (CS):
В ЧН и ЗП серверный объект `level_changer` зарегистрирован через фабрику на скриптовый класс `se_level_changer.se_level_changer`:
- `gamedata_cs/scripts/class_registrator.script:20`
- `gamedata/scripts/class_registrator.script:22`
- Скрипт: `gamedata_cs/scripts/se_level_changer.script:8-15` и `gamedata/scripts/se_level_changer.script:19-26`

Метод `se_level_changer:STATE_Write(packet)` дописывает в пакет:
1. `set_save_marker(packet, "save", false, "se_level_changer")` — маркер начала сохранения.
2. `packet:w_bool(self.enabled)` — флаг активности перехода (`u8`).
3. `packet:w_stringZ(self.hint)` — подсказка при наведении (строка `stringZ`, обычно `"level_changer_invitation"`).
4. `set_save_marker(packet, "save", true, "se_level_changer")` — контрольный размер блока данных (`u16`).

#### Тень Чернобыля (SHoC):
В ванильном ТЧ класс `se_level_changer` **отсутствует**:
- В `gamedata_soc/scripts/class_registrator.script` регистрация скриптового класса для `level_changer` отсутствует (объект обрабатывается напрямую движковым `cse_alife_level_changer`).
- В пакете **нет** полей `enabled`, `hint` и нет маркеров `set_save_marker`.
- Пакет ТЧ завершается сразу после записи `silent_mode` (`u8`).

---

## 3. Таблица структуры пакета по играм

| Базовый класс / Слой | Поле | Тип | ТЧ (SHoC) | ЧН (CS) | ЗП (CoP) |
|----------------------|------|-----|-----------|---------|----------|
| `CSE_ALifeObject` | `game_vertex_id` | `u16` | + | + | + |
| | `distance` | `float` | + | + | + |
| | `direct_control` | `s32` | + | + | + |
| | `level_vertex_id` | `s32` | + | + | + |
| | `object_flags` | `s32` | + | + | + |
| | `custom_data` | `stringZ` | + | + | + |
| | `story_id` | `s32` | + | + | + |
| | `spawn_story_id` | `s32` | + | + | + |
| `CSE_Shape` | `shape_count` | `u8` | + | + | + |
| | Данные шейпов (сфера/бокс) | N байт | + | + | + |
| `CSE_ALifeSpaceRestrictor` | `restrictor_type` | `u8` | + | + | + |
| `CSE_ALifeLevelChanger` | `dest_game_vertex_id` | `u16` | + | + | + |
| | `dest_level_vertex_id` | `s32` | + | + | + |
| | `dest_position` | `vec3` | + | + | + |
| | `dest_direction` | `vec3` | + | + | + |
| | `dest_level_name` | `stringZ` | + | + | + |
| | `dest_graph_point` | `stringZ` | + | + | + |
| | `silent_mode` | `u8` | + | + | + |
| `se_level_changer` (Lua) | `enabled` (activity flag) | `u8` (bool) | **НЕТ** | + | + |
| | `hint` (invitation text) | `stringZ` | **НЕТ** | + | + |
| | `save_marker` | `u16` | **НЕТ** | + | + |

---

## 4. Ссылки на моды-телепорты на GitHub

1. **Тень Чернобыля (SHoC)**:
   - Репозиторий: `naxac/Spatial-Subway`
     - Файл: `gamedata/scripts/teleportator.script`
     - URL: https://github.com/naxac/Spatial-Subway/blob/8a3a6b598b570f25ad46ec8116e08c397be7d998/gamedata/scripts/teleportator.script#L200-L264
     - Описание: функция `create_level_changer(pos, lv, gv, lname)` читает через `STATE_Write` и записывает обновлённый пакет без хвостовых полей `se_level_changer`.
   - Репозиторий: `predvestnikapocalipsisa-hue/STALKER-SOC-GUIDES-TUTORIALS`
     - Файл: `features/Slava_features/level_changer через скрипт/level_changer.script`
     - URL: https://github.com/predvestnikapocalipsisa-hue/STALKER-SOC-GUIDES-TUTORIALS/blob/17f5aa63d841a190cc8c18ed0b55e9f59fffea6d/features/Slava_features/level_changer%20%D1%87%D0%B5%D1%80%D0%B5%D0%B7%20%D1%81%D0%BA%D1%80%D0%B8%D0%BF%D1%82/level_changer.script
2. **Зов Припяти (CoP)**:
   - Репозиторий: `xray-storage/so-xray` («Повелитель Зоны» / трейнер)
     - Файл: `gamedata/scripts/god.script`
     - Функция: `god.spawn_lc` с чтением и записью `enabled`, `hint` и `marker`.

---

## 5. Надёжное определение игры из скрипта

В движках X-Ray глобальное окружение Lua различается набором экспортированных C++ классов:

1. **`_G.CUIListBox`**:
   - Присутствует только в Зове Припяти (`gamedata/scripts/lua_help.script:6477`).
   - Отсутствует в ТЧ и ЧН (там используется `CUIListWnd`).
2. **`_G.vector2`**:
   - Экспортирован в ЧН (`gamedata_cs/scripts/lua_help.script:7468`) и ЗП (`gamedata/scripts/lua_help.script:7390`).
   - Отсутствует в ТЧ (`gamedata_soc/scripts/lua_help.script` — в ТЧ есть только 3D `vector`).
3. **`_G.CUILabel`**:
   - Экспортирован в ТЧ (`gamedata_soc/scripts/lua_help.script:6469`).
   - Удалён в ЧН и ЗП.

Функция классификации:
```lua
function detect_game()
    if _G.CUIListBox ~= nil then
        return "cop"
    elseif _G.vector2 ~= nil then
        return "cs"
    else
        return "soc"
    end
end
```
