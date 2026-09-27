# Упаковка модов под S.T.A.L.K.E.R. Enhanced Edition (Legends of the Zone Trilogy)

Документ описывает структуру модов, формат метаданных `desc.json`, архитектуру контейнера `.xrp` / `.pack` и процедуру упаковки/загрузки мода-компаньона для версий Enhanced Edition (Shadow of Chornobyl, Clear Sky, Call of Prypiat).

---

## 1. Официальные инструменты и источники

GSC Game World предоставляет официальный инструментарий для моддинга трилогии EE:
- **Официальный гайд в Steam:** [Steam Community Guide #3497576322](https://steamcommunity.com/sharedfiles/filedetails/?id=3497576322).
- **Набор утилит `stk-utils.7z`:** `http://modio.stalker-game.com/assets/ee/stk-utils.7z`:
  - `stk-utils/workshop/xrCompress.exe` (версия для Steam Workshop — поддерживает `.script` и `.ltx`).
  - `stk-utils/modio/xrCompress.exe` (версия для Mod.io / консолей — намеренно отфильтровывает скрипты и конфиги).
  - `stk-utils/workshop/xrSWS_Upload.exe` (консольная утилита публикации в Steam Workshop).

---

## 2. Различия платформ: Steam Workshop vs Mod.io

| Возможность | Steam Workshop (PC EE) | Mod.io (Консоли) |
|---|---|---|
| Скрипты (`.script`) | **Разрешены** | **Запрещены** (модерация отклоняет) |
| Конфиги (`.ltx`, `.xml`) | **Разрешены** | **Запрещены** |
| Ресурсы (текстуры, звуки, модели) | Разрешены | Разрешены (до 1 ГБ распакованного размера) |
| Бинарники (`.dll`, `.exe`) | **Запрещены** | **Запрещены** |

> **Вывод:** Внутриигровой мод-компаньон редактора сейвов, использующий Lua-скрипты, для Enhanced Edition может распространяться **только через Steam Workshop** (или устанавливаться вручную).

---

## 3. Формат контейнера `.xrp` / `.pack` / `.xdb0`

Контейнеры `.xrp` в Enhanced Edition — это **классический формат архивов X-Ray DB** (`.db*`, `.xdb*`):

### Бинарная структура файла:
1. **Header Chunk (`0x0000029a` = 666):**
   - Размер: 156 байт (`0x0000009c`).
   - Содержимое: INI-секция заголовка:
     ```ini
     [header]
     auto_load = true
     level_name = single
     level_ver = 1.0
     entry_point = $fs_root$\gamedata\
     creator = gsc game world
     link = www.stalker-game.com
     ```
2. **Data Chunk (`0x00000000`):**
   - Блок данных: последовательно склеенные тела упакованных файлов. При использовании флага `-store` файлы хранятся несжатыми, что обеспечивает быструю загрузку в игре.
3. **FAT Chunk (`0x80000001`):**
   - Бит `0x80000000` означает сжатие таблицы файлов алгоритмом **LZO1X**.
   - Чанк хранит сжатый размер, исходный размер и распакованную таблицу аллокации (FAT): пути файлов (null-terminated stringZ), смещение данных, размер, сжатый размер и CRC32.

---

## 4. Формат манифеста `desc.json`

Файл `desc.json` размещается в корне каталога мода и содержит метаданные для локального менеджера модов и инструментов публикации:

```json
{
  "title": "S.T.A.L.K.E.R. Save Editor Companion",
  "game": "cop",
  "version": "1.0.0",
  "author": "Dmitriy-DE",
  "description": "In-game companion mod for Call of Pripyat Enhanced Edition",
  "preview": "preview.jpg",
  "entry_point": "gamedata/scripts/save_editor_companion.script",
  "package_file": "save_editor_companion_cop.xrp",
  "steam_workshop": {
    "game_code": "COP",
    "published_file_id": 0,
    "visibility": "public"
  }
}
```

### Поля:
- `title` (*обязательное*): Человекочитаемое название мода.
- `game` (*обязательное*): Код целевой игры (`soc`, `cs`, `cop`).
- `version` (*обязательное*): Семантическая версия мода.
- `author` (*обязательное*): Имя автора или команды разработки.
- `description` (*обязательное*): Текстовое описание возможностей мода.
- `preview` (*опциональное*): Имя файла обложки (`preview.jpg` или `preview.png`).
- `entry_point` (*опциональное*): Относительный путь к точке входа (`gamedata/scripts/...`).
- `package_file` (*опциональное*): Имя скомпилированного архива мода (`.xrp` / `.pack`).
- `steam_workshop` (*опциональное*): Метаданные интеграции с мастерской Steam:
  - `game_code`: `"SOC"`, `"CS"`, или `"COP"`.
  - `published_file_id`: `0` для нового мода; заполняется ID после первой публикации (`--mode=update`).
  - `visibility`: `"public"`, `"friends"`, `"unlisted"`, `"private"`.

---

## 5. Структура папок мода

### Вариант 1: Стейджинг для загрузки в Steam Workshop
Папка, передаваемая параметром `--path` утилите `xrSWS_Upload.exe`:
```
dist/ee_cop_companion/
├── desc.json
├── preview.jpg
└── save_editor_companion_cop.xrp   (или .pack)
```
*(Если в моде есть видеоролики `.mp4`, они не пакуются в `.xrp`, а лежат открыто в подпапке `textures/ui/video/`)*.

### Вариант 2: Локальная установка (распакованная)
Для тестирования без загрузки в Workshop:
```
<GameRoot>/mods/SaveEditorCompanion/
├── desc.json
└── gamedata/
    └── scripts/
        ├── save_editor_companion.script
        └── bind_stalker.script (хук вызова)
```

---

## 6. Пошаговая инструкция упаковки

### Автоматическая упаковка (рекомендуемый способ)
Используйте скрипт `tools/pack_companion_ee.py`:

```bash
# Упаковка для Call of Pripyat EE с вызовом xrCompress через Wine:
python3 tools/pack_companion_ee.py mods/companion/ \
    --game cop \
    --output dist/ee_cop_companion \
    --title "S.T.A.L.K.E.R. Save Editor Companion" \
    --version 1.0.0 \
    --author "Dmitriy-DE" \
    --desc "In-game companion mod for Call of Pripyat Enhanced Edition"

# Упаковка без сжатия (только структура папок и desc.json):
python3 tools/pack_companion_ee.py mods/companion/ \
    --game soc \
    --output dist/ee_soc_companion \
    --no-compress
```

### Ручной запуск `xrCompress` под Linux (Wine / Proton)
1. Скачать официальный `stk-utils.7z`.
2. Запустить упаковку через Wine:
   ```bash
   WINEDEBUG=-all wine stk-utils/workshop/xrCompress.exe /path/to/mod_gamedata_folder -store
   ```
3. В родительской папке появится файл `<folder_name>.xdb0`.
4. Переименовать `<folder_name>.xdb0` в `save_editor_companion.xrp` и переместить в стейджинг к `desc.json`.

### Публикация в Steam Workshop
Запуск `xrSWS_Upload.exe` (требует запущенного клиента Steam):
```cmd
xrSWS_Upload.exe --mode=create --game=COP --path="C:\mods\ee_cop_companion" --preview="C:\mods\ee_cop_companion\preview.jpg" --title="S.T.A.L.K.E.R. Save Editor Companion" --desc="In-game companion mod for Call of Pripyat Enhanced Edition"
```
После успешного создания Steam присваивает моду `PublishedFileId`, который можно указать в `desc.json` для последующих обновлений через `--mode=update`.
