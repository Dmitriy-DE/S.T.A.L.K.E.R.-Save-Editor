# X-Ray saves: format and evidence

## SAVE_FORMAT.md — confirmed facts vs hypotheses

### Confirmed

Container:

```text
u32 LE unpacked_size
Kraken/Oodle stream
u32 LE CRC32(file_without_final_crc)
```

Unique campaign/player anchor used by this build:

```python
MONEY_ANCHOR = bytes.fromhex(
  "0038010000000110cacfa848c8952149b51b9444000000000600000000060000"
)
```

Immediately after anchor:

```text
u32 money
u32 owned_flag
u16 owned_handle_count
u32 owned_handles[owned_handle_count]
u16 grid_cell_count
GridCell[grid_cell_count]
```

GridCell = `<IHH>` = handle,x,y.

Object records confirmed for inventory objects:

```text
+0  u32 handle
+11 u16 x
+13 u16 y
+18 u8 0x38
+19 u32 count
+24 f32 total_weight
+31 u8 kind
```

### Useful observed invariant

For the same object handle across nearby saves, the bytes at `+8..+10` are stable. v0.3 exposes them as `type-key`, but does NOT claim they are the public SID/hash.

### Structural array editing implemented

Because owned/grid arrays are count-prefixed and contiguous, v0.3 can rebuild those arrays with changed lengths while leaving the rest of raw payload intact. This is how experimental detach/attach is implemented.

### Inspection coverage and read-only rules

`inspect_save()` также возвращает `grid_handle_count`, `unresolved_handles` и
`warnings`. Некорректная grid-ссылка, duplicate position, отсутствующий object
record или неполный footprint остаются видимыми как диагностика, но не
считаются полностью разобранным объектом. Неизвестный `kind` показывается с
его числовым кодом и имеет `editable_count=False`; подтверждённые stackable
коды ограничены `4, 5, 7, 8`. Это coverage отчёт для текущей структуры, а не
доказательство универсальности всех кампаний и версий игры.

### Not confirmed

- root object registry count / full object record boundaries;
- handle allocator state;
- prototype SID reference/hash;
- durability;
- attachment lists;
- upgrade state;
- deletion semantics / garbage collection;
- whether all `owned_handles` are inventory ownership or include equipment/system references.

### Reverse-engineering strategy

Use controlled pairs and `cli.py diff-record`:

```bash
python3 cli.py diff-record A.sav B.sav 0xHANDLE --limit 4096
```

Best experiments:

- same weapon before/after exactly one durability change;
- same item moved one grid cell;
- same stack after +1 item;
- item equipped vs unequipped;
- attach/detach via normal game mechanics if possible;
- add a known SID through console on local Windows install, then diff the resulting save.

---

## X-Ray container — M06 evidence

Исследование и локальная проверка выполнены 2026-09-15. Эта запись разделяет
публично известную схему контейнера и то, что реально принято для этого
проекта. Байты личных сейвов в репозиторий не добавлялись.

### Публичное исследование

- В открытом [stalker-tools `save_tool.py`](https://github.com/stalker-tools/tools/blob/main/save_tool.py)
  чтение начинается с трёх little-endian `u32`: `magic`, `version` и
  `unpacked_len`. Реализация принимает `magic == 0xffffffff`, отбрасывает
  версии ниже 2 и распаковывает оставшийся поток как raw LZO1X с ожидаемой
  длиной `unpacked_len`.
- В той же реализации после распаковки читаются little-endian chunk-записи
  `type,size`; среди известных типов есть ALIFE, SPAWN, OBJECT, GAME_TIME,
  REGISTRY и SCRIPT_VARS. Это полезное направление для дальнейшего чтения,
  но не доказательство расположения денег или инвентаря.
- В разборе [Valentin Pi формата SoC](https://github.com/valentinpi/valentinpi.github.io/blob/master/posts/soc/soc.md)
  показана запись внешнего заголовка `-1`, `ALIFE_VERSION`, исходный размер,
  затем raw LZO1X-1. Приведённый в статье образец имеет версию `0x0003`.
  Статья отдельно отмечает, что повторное сжатие может дать другие байты,
  хотя игра загрузила пересобранный файл. Поэтому «распаковать и снова сжать»
  не доказывает byte-for-byte round-trip.
- Публичный [xrWiki Save Unpacker](https://xray-engine.org/index.php?action=mpdf&title=S.T.A.L.K.E.R._save_unpacker)
  документирует unpack/repack-инструмент и историю поддержки SoC, CoP и CS.
  Это подтверждает наличие внешнего исследовательского материала, но не даёт
  SHA-доказательства для Enhanced Editions.
- Исходники [OpenXRay для SoC](https://github.com/ixray-team/ixray-1.0-stsoc),
  [CS](https://github.com/ixray-team/ixray-1.5-stcs) и
  [CoP](https://github.com/ixray-team/ixray-1.6-stcop) подтверждают сериализацию
  `GAME_TIME`, object registry, actor/trader money и ammo state. Таблица версий
  сверена с [Universal-ACDC](https://github.com/PSIget/Universal-ACDC): для
  современных релизов ожидаются spawn versions SoC 118, CS 122–124 и CoP 128.
- Для CoP быстрые сохранения используют отдельное расширение `.scop` согласно
  [документации OpenXRay](https://github.com/OpenXRay/xray-16/issues/1536).

### Что доступно локально

Репозиторный поиск по рабочему дереву по-прежнему не находит игровых файлов:

```text
rg --files -g '*.sav' -g '*.bak' -g '*.scop' .
```

Эти файлы намеренно остаются вне Git. Для локального read-only прогона найден
внешний установленный корпус оригинальных Steam-версий: 4 SoC `.sav`, 56 CS
`.sav` и 168 CoP `.scop`. Все 228 файлов распарсились с ожидаемой структурой
chunks `(ALIFE, GAME_TIME, SPAWN, OBJECT, REGISTRY)` и версиями контейнера
3/5/6; actor spawn versions — 118/124/128 соответственно. Это корпус
оригинальной трилогии на этой машине, а не доказательство всех модов,
патчей или Enhanced Editions.

### Решение

M06 разблокирована для подтверждённых оригинальных X-Ray контейнеров. Реализация
проверяет:

1. `magic=0xffffffff`, outer versions 3/5/6 и ожидаемый unpacked size;
2. raw LZO1X stream с bounds checks и строгой границей end marker;
3. little-endian chunks без обрезания, лишних байт и неизвестной внешней версии;
4. byte-for-byte no-op: `XRayContainer.build()` возвращает исходные bytes,
   если payload не менялся.

После изменения payload применяется безопасный literal-only LZO writer; его
compressed bytes не обещают совпадать с игрой. Поэтому no-op SHA является
отдельным доказательством, а игровой load/re-save остаётся внешним гейтом.

### Что ещё не принято

Для расширения заявления нужны отдельные данные:

- controlled pairs, где известны изменения денег и предметов;
- сохранения Enhanced Editions: [SoC EE discussion](https://steamcommunity.com/app/2427410/discussions/0/528723757459612258/)
  описывает отдельные `.sav/.dds/.info` в `Saved Games`, а [CS EE discussion](https://steamcommunity.com/app/2427420/discussions/0/603030907426702266/?l=ukrainian)
  сообщает об отличающемся `.scs`. Desktop discovery теперь показывает `.scs`
  как неизвестный кандидат вместо того, чтобы молча пропускать его; это не
  основание принимать его текущим X-Ray parser;
- загрузка отредактированной копии каждой оригинальной игры и повторное
  сохранение самой игрой.

До этих проверок не заявляются Enhanced Editions и полная совместимость с
модами. Неизвестные версии получают явный отказ.

### Локальный gate

Локальный gate после реализации:

```text
PYTHON=.venv/bin/python make check
PYTHON=.venv/bin/python make test
```

Зелёный gate не заменяет Windows runtime и проверку загрузкой игры.

---

## X-Ray inventory writer evidence — 2026-09-15

Эта запись содержит только агрегированные read-only результаты. Байты личных
сейвов, их имена, SHA и пути в репозиторий не добавлялись; writer не сохранял
результат ни в один игровой каталог.

### Что подтверждено

Для original Steam-корпуса на текущем Linux-хосте каждый уникальный файл был:

1. распакован и строго проверен через полный object registry;
2. проверен на byte-for-byte no-op через `XRayContainer.build()`;
3. проверен in-memory money round-trip на одном representative save каждого
   релиза;
4. проверен in-memory ammo count round-trip одновременно в STATE и UPDATE на
   одном representative save каждого релиза;
5. проверен in-memory catalog-backed add path и synthetic deep-remove
   regression без записи результата в игровой каталог.

| Release | Files | Unique bytes | Strict parse | No-op | Actor version | Object count | Inventory count | Money | Ammo |
| --- | ---: | ---: | ---: | ---: | --- | --- | --- | --- | --- |
| Original Shadow of Chernobyl | 4 | 4 | 4 | 4 | 118 (4) | 16,788–20,010 | 5–87 | 1/1 | 1/1 |
| Original Clear Sky | 56 | 56 | 56 | 56 | 124 (56) | 22,849–25,350 | 7–139 | 1/1 | 1/1 |
| Original Call of Pripyat | 168 | 168 | 168 | 168 | 128 (168) | 11,067–31,925 | 28–229 | 1/1 | 1/1 |

`Money` и `Ammo` показывают успешные representative in-memory проверки, а не
число файлов, изменённых на диске. Детальный verifier для всех профилей выдал:

```json
{
  "stalker2": {"candidates": 0, "parsed": 0, "sha_matches": 0},
  "stalker-soc": {"candidates": 4, "parsed": 4, "versions": {"118": 4}, "sha_matches": 4, "edit_roundtrips": 7},
  "stalker-cs": {"candidates": 56, "parsed": 56, "versions": {"124": 56}, "sha_matches": 56, "edit_roundtrips": 109},
  "stalker-cop": {"candidates": 168, "parsed": 168, "versions": {"128": 168}, "sha_matches": 168, "edit_roundtrips": 336},
  "stalker-soc-ee": {"candidates": 0, "parsed": 0},
  "stalker-cs-ee": {"candidates": 0, "parsed": 0},
  "stalker-cop-ee": {"candidates": 0, "parsed": 0}
}
```

Все 228 уникальных образцов имели пустой `failure_summaries`; verifier выводит
только агрегаты и не сохраняет пути, имена или байты.

### Structural slice

Сериализованный object теперь предоставляет проверенные окна `SPAWN`, `STATE`
и `UPDATE`, включая точные границы record. Writer умеет доказанный
synthetic/catalog-backed путь для следующих serializer families:

- `ammo` — clone существующего ammo object с новым `object_id`, serialized key,
  actor parent и count в обеих подтверждённых позициях;
- `base`, `detector`, `outfit`, `pda`, `torch`, `weapon`,
  `weapon_magazined`, `weapon_shotgun`, `weapon_wgl` — clone существующего
  registry template той же family с новым key/object id и actor parent;
- удалить любой выбранный actor-owned registry record только как `deep`
  operation;
- добавить/удалить object record с корректным registry count и новым framing;
- выполнить строгий reparse результата.

Установленные официальные каталоги дали 389 item definitions для SoC, 417 для
CS и 434 для CoP. В отдельном read-only прогоне на representative save каждого
релиза по одному item из всех десяти перечисленных families был добавлен в
память, результат распарсился обратно, а исходный SHA остался неизменным:

| Release | Catalog items | Families exercised | Added records |
| --- | ---: | ---: | ---: |
| Original Shadow of Chernobyl | 389 | 10 | 10 |
| Original Clear Sky | 417 | 10 | 10 |
| Original Call of Pripyat | 434 | 10 | 10 |

Это не копирование prototype bytes из game archive: каталог сообщает ключ,
категорию, stack limit и serializer family, а сериализованный STATE/UPDATE
template берётся из самого save. Каталог не используется для угадывания SID,
prototype или полей durability/upgrades. Inventory grid/position,
reference-safe deletion, attachments и game semantics остаются отдельными
ограничениями.

### Additional installed-save corpus probe — 2026-09-16

The current installed official original-trilogy save directories were scanned
read-only. Personal filenames, paths, bytes, and hashes are omitted. All
detected candidates parsed successfully:

| Release | Candidate saves | Parsed | Inventory count |
| --- | ---: | ---: | ---: |
| Original Shadow of Chernobyl | 6 | 6 | 5–88 |
| Original Clear Sky | 59 | 59 | 7–139 |
| Original Call of Pripyat | 171 | 171 | 28–229 |

This is a parser-coverage result only. It does not turn the structural writer
probe into proof of game load/re-save or persistence.

### Ограничения

- Результаты относятся к обнаруженному на этой машине original-корпусу и не
  являются доказательством Enhanced Editions, Windows runtime или загрузки
  результата самой игрой.
- Повторное сжатие после изменения может иметь другие packed bytes; no-op
  обязан быть byte-identical, а изменённый output подтверждается parse и SHA.
- Web-каталог содержит только metadata keys/families без игровых архивов,
  локальных путей и save bytes; desktop каталог читается из явно выбранных
  официальных ресурсов read-only.
- Игровой load/re-save и controlled real-save add/remove остаются отдельным
  внешним acceptance gate.

---

## X-Ray catalog evidence — 2026-09-15

This note records only aggregate read-only results. No save bytes, game
archives, private catalog dumps, or personal filesystem paths are stored in
the repository.

### Public format references

- The public [X-Ray tools DBReader](https://github.com/stalker-tools/tools/blob/main/DBReader.py)
  documents the chunked `.db/.xdb` archive layout, the 2947 regional header
  scrambler, LZ-Huffman header compression, and LZO-compressed file entries.
- The same project's [GameConfig](https://github.com/stalker-tools/tools/blob/main/GameConfig.py)
  documents the INI-like item sections, inheritance, inventory grid fields,
  and XML localization inputs.
- The implementation in `editor/xray_catalog.py` is a bounded independent
  reader. It does not import or copy the public project's code or game data.

### Local read-only probe

The installed official Steam manifests for the original trilogy were detected
on the Linux host. The probe passed the selected install root to
`XRayCatalogProvider` and did not write to the install or save directories.

| Release | Source accepted | Items | Categories | Result |
| --- | --- | ---: | --- | --- |
| Original Shadow of Chernobyl | official packed X-Ray archives | 389 | ammo 51; artifact 60; consumable 12; grenade 14; item 91; outfit 13; weapon 148 | accepted for read-only catalog extraction |
| Original Clear Sky | official packed X-Ray archives; unpacked `gamedata` overlay ignored because it contained an `OGSM` community-mod marker | 417 | ammo 50; artifact 53; consumable 9; grenade 10; item 111; outfit 11; weapon 173 | accepted for read-only catalog extraction |
| Original Call of Pripyat | official packed X-Ray archives | 434 | ammo 47; artifact 57; consumable 17; grenade 14; item 134; outfit 10; weapon 155 | accepted for read-only catalog extraction |

These counts prove the reader against the current host only. They do not prove
that every retail language, patch, GOG installation, or Enhanced Edition has
the same catalog. `prototype` remains `None`; catalog-backed add uses the
item key/family metadata together with a same-family template already present
in the selected save, rather than copying prototype bytes from an archive.

### Full-key in-memory writer probe — 2026-09-15

`.venv/bin/python tools/verify_xray_catalog.py` was run
read-only with one installed official save directory per original release.
The verifier clones every catalog key through the same serializer-family
writer helper, rebuilds one temporary container in memory, and parses it
again. It does not write an edited save or print personal paths.

| Release | Candidate saves | Catalog keys | Keys with a template | Keys visible after round-trip | Failures |
| --- | ---: | ---: | ---: | ---: | ---: |
| Original Shadow of Chernobyl | 4 | 389 | 389 | 389 | 0 |
| Original Clear Sky | 56 | 417 | 417 | 417 | 0 |
| Original Call of Pripyat | 168 | 434 | 434 | 434 | 0 |

This is the strongest current evidence that the original desktop/web catalog
can stage every key in the official resource catalog using an existing save
template. It is still structural evidence: it does not prove game load/re-save,
localized display names, correct weight/grid placement, durability/upgrades,
quest uniqueness, or reference-safe removal.

---

## X-Ray durability — 2026-09-15

Этот файл фиксирует, что удалось подтвердить для прочности предметов в
официальных оригинальных SoC, Clear Sky и Call of Pripyat. Личные сейвы и
игровые архивы в репозиторий не добавлялись.

### Схема из исходников

В публичных исходниках [iXRay 1.0 SoC](https://github.com/ixray-team/ixray-1.0-stsoc),
[iXRay 1.5 Clear Sky](https://github.com/ixray-team/ixray-1.5-stcs) и
[iXRay 1.6 Call of Pripyat](https://github.com/ixray-team/ixray-1.6-stcop)
`CSE_ALifeInventoryItem::STATE_Read/Write` читает и пишет `m_fCondition`
после dynamic-visual state как little-endian `float`. В исходниках это поле
добавлено после старых spawn versions; текущие поддержанные акторы имеют
версии 118 / 124 / 128.

Client-data предмета также содержит сохранённое состояние `CInventoryItem`:
исходник пишет `m_ItemCurrPlace.value` как `u16`, затем condition как `float`.
Перед этими полями у разных оригинальных релизов присутствует различный
physics-prefix, поэтому редактор не фиксирует один абсолютный offset. Он ищет
только тот `float`, который в точности совпадает с подтверждённым STATE
condition, проверяет соседний `u16` по исходному bit layout и принимает место
только при валидном типе и диапазоне slot/base-slot. Так распознаются
`equipped` и `inventory`; неоднозначные и повреждённые значения остаются
`unknown`.

Для weapon/outfit UPDATE-сериализаторы передают условие через `w_float_q8`.
Его исходная формула округляет нормализованное значение в байт `0…255`;
поэтому writer вычисляет mirror как `floor(condition * 255 + 0.5)`. Offset не
зашит по одному совпадению: parser проверяет только относительные позиции 3 и
4 внутри UPDATE и принимает позицию лишь когда `q8 / 255` совпадает с STATE
значением в пределах одного шага квантования. При неоднозначности UPDATE не
трогается, а STATE остаётся отдельным подтверждённым anchor.

### Локальный read-only probe

На одном выбранном официальном файле каждого установленного оригинального
релиза parser получил следующие агрегаты:

| Релиз | Actor-owned weapon/outfit items с condition | Найден q8 mirror | Без безопасного mirror | Диапазон condition |
|---|---:|---:|---:|---:|
| Shadow of Chornobyl | 8 | 7 | 1 | 0.984313…–1.000000 |
| Clear Sky | 6 | 6 | 0 | 0.925490…–1.000000 |
| Call of Pripyat | 7 | 7 | 0 | 1.000000 |

В SoC единственный объект без mirror — сохранённый helmet UPDATE длиной,
недостаточной для безопасного q8 anchor; его STATE condition всё равно
читается, но соседние UPDATE bytes не угадываются.

Точный слот экипировки или ячейка рюкзака в принятом actor registry не
извлекаются надёжно: writer пока не меняет `m_ItemCurrPlace` и не обещает
equip/unequip. UI различает только подтверждённые `equipped`/`inventory`; для
остальных actor-owned предметов показывает `слот не определён`. Это позволяет
редактировать condition обеих групп без догадок о placement.

### Реализация и граница приёмки

- `editor/xray_item_state.py` читает STATE `f32`, находит только совпадающий
  UPDATE `q8` и client-data `f32` mirror с placement, затем патчит все
  подтверждённые mirrors с bounds checks.
- `EditPlan.durability` принимает только уникальные handle и конечные значения
  `0…1`.
- Qt и web показывают состояние в процентах, staged changes не меняют исходные
  bytes до Preview; browser bridge передаёт тот же typed plan.
- Неизвестные families и объекты без подтверждённого anchor остаются
  read-only.

Локально пройдены synthetic tests для трёх версий, parser round-trip на
установленных saves и UI/bridge проверки. Отдельный controlled game
load/re-save именно с изменённой прочностью пока не собран. Поэтому для трёх
оригинальных релизов `FormatCapabilities.edit_durability` включён только как
`experimental_fields`: Qt/web показывают предупреждение, desktop требует
backup, browser не перезаписывает источник. Это разрешает владельцу проверить
подготовленную копию, но не является claim о принятии condition игрой.

---

## X-Ray inventory placement — 2026-09-16

Этот файл фиксирует только подтверждённую границу переноса предметов между
слотами, поясом и рюкзаком в официальных оригинальных SoC, CS и CoP. Enhanced
Editions, S.T.A.L.K.E.R. 2, моды и личные сейвы не входят в scope. Read-only
проба не записывает найденные файлы; загрузка изменённого сейва игрой и
повторное сохранение по M10 не выполнялись.

### Сериализация из исходников

В OpenXRay [inventory_item_object.cpp, pinned commit
`c37860c`](https://github.com/OpenXRay/xray-16/blob/c37860c09850d894b721ba115cd936bb3f11482c/src/xrGame/inventory_item_object.cpp#L102-L106)
обычный inventory object сначала сохраняет унаследованное физическое
состояние, а затем `CInventoryItem::save`. Унаследованный
[PhysicsShellHolder.cpp](https://github.com/OpenXRay/xray-16/blob/c37860c09850d894b721ba115cd936bb3f11482c/src/xrGame/PhysicsShellHolder.cpp#L350-L359)
записывает один `u8 enable_state`; следующий
[inventory_item.cpp](https://github.com/OpenXRay/xray-16/blob/c37860c09850d894b721ba115cd936bb3f11482c/src/xrGame/inventory_item.cpp#L359-L363)
записывает `m_ItemCurrPlace.value` как little-endian `u16`, затем condition.
Поэтому подтверждённая граница `SInvItemPlace` в client-data у трёх
поддержанных релизов — `client_data_offset + 1`.

`SInvItemPlace` — это source-defined bitfield: нижние 4 бита содержат тип,
следующие 6 — `slot_id`, ещё 6 — `base_slot_id`. Типы `Slot`, `Belt` и `Ruck`,
а также диапазон слотов 1…13 сверены с
[inventory_space.h](https://github.com/OpenXRay/xray-16/blob/c37860c09850d894b721ba115cd936bb3f11482c/src/xrServerEntities/inventory_space.h)
и с чтением/записью item place в
[inventory_item.cpp](https://github.com/OpenXRay/xray-16/blob/c37860c09850d894b721ba115cd936bb3f11482c/src/xrGame/inventory_item.cpp#L752-L760).
Игровые операции слота, пояса и рюкзака также видны в
[Inventory.cpp](https://github.com/OpenXRay/xray-16/blob/c37860c09850d894b721ba115cd936bb3f11482c/src/xrGame/Inventory.cpp#L414-L417).

### Граница редактора

Codec читает ровно два байта по release-specific offset и не сканирует
соседние данные в поисках подходящего значения. Изменение разрешается только
для actor-owned inventory object с подтверждённым client-data anchor:

- `slot` принимает номер 1…13 и меняет type/slot, сохраняя `base_slot_id` и
  прочие верхние биты;
- `belt` и `ruck` принимают `None` вместо номера слота и меняют только type,
  сохраняя остальные биты;
- отсутствующий, неизвестный или неподтверждённый place остаётся read-only;
- writer меняет только этот `u16`, после чего выполняются обычные CRC,
  decompression/rebuild, fresh SHA и повторный parse/read-back guards.

Qt и web используют один immutable `EditPlan.placements`, показывают before →
after и сначала только staging/preview. Для original SoC/CS/CoP capability
помечена experimental; для S2 и Enhanced она не включается.

### Read-only корпус

Проверены копии официальных оригинальных релизов локально, без записи в
исходные файлы. Числа ниже — агрегаты конкретного корпуса, а не утверждение
о каждом возможном сейве:

| Релиз | Файлы parsed/failed | Inventory objects | Editable place | Типы | Storage |
| --- | ---: | ---: | --- | --- | --- |
| Shadow of Chernobyl | 6 / 0 | 373 | 328 | belt 25, ruck 303 | inventory 328 |
| Clear Sky | 59 / 0 | 4486 | 4034 | belt 114, ruck 3920 | inventory 4034 |
| Call of Pripyat | 171 / 0 | 27373 | 27373 | belt 616, ruck 25276, slot 1481 | equipped 1481, inventory 25892 |

Проба подтвердила, что offset `1` даёт согласованное распределение и
отсутствие parse failures на этом корпусе. Это структурное и read-only
evidence; оно не доказывает, что каждая конкретная версия игры примет новый
place или корректно покажет его после следующего игрового сохранения.

### Проверки

- `tests/test_xray_placement.py`: release-specific offsets, slot/belt/ruck,
  сохранение metadata и отказ unsafe anchors;
- `tests/test_ui_placement.py`: Qt staging и неизменность snapshot;
- `tests/test_web_bridge.py`, `tests/test_web_inventory_icons.py`:
  cross-platform bridge и web controls;
- `make check`, `make test`, `node --check web/app.js`, `git diff --check`.

---

## X-Ray upgrades — 2026-09-16

Этот файл фиксирует только подтверждённую границу исследования. Моды,
личные сейвы, байты сейвов и персональные пути в Git не попадают. Запуск игры,
загрузка изменённого сейва и повторное сохранение по M10 не выполнялись.

### Схема из исходников

В публичном исходнике [OpenXRay xray-16, inventory item STATE
codec](https://github.com/OpenXRay/xray-16/blob/c37860c09850d894b721ba115cd936bb3f11482c/src/xrServerEntities/xrServer_Objects_ALife_Items.cpp#L79-L95)
`CSE_ALifeInventoryItem::STATE_Write` записывает `m_fCondition`, затем
`m_upgrades`; `STATE_Read` читает `m_upgrades` только при
`m_wVersion > 123`. `m_upgrades` имеет тип `xr_vector<shared_str>`, поэтому
общий сериализатор вектора даёт `u32 count` и последовательность
нуле-терминированных строк. Реализация редактора меняет только этот bounded
vector и сохраняет последующий STATE-хвост, UPDATE и соседние записи.

Отсутствие вектора до этой version boundary не трактуется как пустой список:
для SoC и ранних состояний CS поле остаётся `None` и read-only. Это различие
важно для ЧН, где в одном реальном корпусе встречаются actor-owned vectors,
а в другом состоянии ещё может не быть подтверждённой границы.

### Локальный read-only probe

Проверены копии официальных оригинальных релизов без записи в исходные файлы.
Ниже агрегаты конкретного корпуса, а не универсальные характеристики каждой
установки:

| Релиз | Catalog definitions | Actor-owned vectors | Positive vectors | Stored IDs matching catalog | Stored IDs outside catalog |
| --- | ---: | ---: | ---: | ---: | ---: |
| Shadow of Chernobyl | 0 | 0 | 0 | 0 | 0 |
| Clear Sky | 503 | 75 | 3 | 7 | 9 |
| Call of Pripyat | 654 | 132 | 3 | 38 | 0 |

Каталог для новых значений release-scoped и item-scoped: редактор не выводит
чужие ID и не превращает неизвестную строку из сейва в официальное описание.
Уже записанные неизвестные ID можно оставить в векторе или снять галочку;
добавить их заново нельзя.

### Реализация и граница

- `parse_xray` возвращает vector, абсолютные границы и признак
  `upgrades_editable` только для подтверждённого actor-owned объекта.
- `EditPlan.upgrades` принимает уникальные handle и уникальные строки;
  writer проверяет release, catalog и applicability перед записью.
- Qt и web показывают текущий vector, каталоговые варианты и staged before →
  after; исходные bytes не меняются до Preview.
- Desktop запись требует M16 backup/read-back; browser скачивает новую копию.
- Возможность помечена `experimental_fields`. Structural round-trip не
  доказывает, что конкретная версия игры примет новый upgrade, покажет его в
  меню или сохранит после следующего игрового save.

### Источник и тесты

Порядок полей взят из исходника X-Ray, а vector boundary и сохранение хвоста
проверяются синтетическим корпусом в `tests/test_xray_upgrades.py`. Web bridge
и UI staging проверяются в `tests/test_web_upgrades.py` и
`tests/test_ui_upgrades.py`; последний пропускается, если Qt test runtime не
установлен.

---

## X-Ray deletion safety — 2026-09-16

### Вывод

M23 не объявляет общий «удалить любой предмет» безопасным. Он усиливает
существующий structural `EditPlan.detach` проверками, которые действительно
видны в разобранном X-Ray object registry:

- target должен быть actor-owned;
- explicit `storage == equipped` блокируется;
- каждый parsed object с `parent_id == target.object_id` считается зависимым;
- отсутствующий или `unresolved` target блокируется до записи.

Если эти условия выполнены, writer удаляет только точный record window и
обновляет OBJECT count существующим container writer. Backup, source SHA,
rebuild и read-back guards остаются без изменений.

### Что считается известной ссылкой

В parsed `XRayObject` уже есть `object_id` и `parent_id`, считанные из строгого
object-record envelope. Поэтому непосредственный registry child можно назвать
по точному handle и не удалять родителя в обход него. Это не заменяет полного
reference graph: поля внутри STATE/UPDATE, quest ownership, equipment links,
prototype identity и неизвестные хвосты сохраняются opaque и не сканируются
эвристически.

`storage is None` означает, что placement anchor не был разобран. Для
совместимости с ранними read-only fixtures такой объект не объявляется
экипированным автоматически; отдельное подтверждение его game semantics всё
ещё требуется.

### Проверки

Synthetic regression покрывает четыре результата: actor-owned leaf проходит;
explicit equipped object отклоняется; parent с child отклоняется с точным
dependent handle; missing object возвращает blocked analysis. Writer tests
подтверждают отказ до registry rebuild для equipped и dependent cases.
Browser bundle manifest также проверяет, что новый общий модуль поставляется
вместе с `xray_save`, поэтому web import graph не расходится с desktop core.

Личные сейвы не изменялись и в evidence не включались. Game load/re-save не
выполнялся, поэтому M10 и полная R08 acceptance остаются открытыми.

---

## X-Ray faction catalog — 2026-09-15

Этот документ фиксирует только метаданные, извлечённые read-only из
официальных ресурсов. Сейвы, игровые архивы, личные пути и содержимое
локализаций в Git не попадают. Моды намеренно не используются.

### Источник и правило выбора

Для каждой оригинальной игры `XRayCatalogProvider` выбирает только ресурсный
корень этой игры. В распакованном `gamedata` overlay с очевидным маркером
community-мода отклоняется; затем проверяются официальные упакованные
архивы. Секция `[game_relations]` задаёт пары `community key → numeric id`, а
`[communities_relations]` задаёт матрицу значений. Отображаемое имя берётся
только из найденной локализации выбранного ресурса; отсутствующее имя
остаётся `None`.

Публичная исходная семантика X-Ray также видна в [OpenXRay
`script_game_object.h`](https://github.com/OpenXRay/xray-16/blob/dev/src/xrGame/script_game_object.h),
а release-specific исходники доступны в [iXRay 1.0 SoC](https://github.com/ixray-team/ixray-1.0-stsoc),
[iXRay 1.5 Clear Sky](https://github.com/ixray-team/ixray-1.5-stcs) и
[iXRay 1.6 Call of Pripyat](https://github.com/ixray-team/ixray-1.6-stcop).
Каталог не переносит из этих проектов списки ID: значения читаются из
выбранной установки.

### Локальный read-only probe

| Релиз | Предметы | Именованные предметы | Группировки | Адреса отношений | Локализация | Результат |
| --- | ---: | ---: | ---: | ---: | --- | --- |
| Shadow of Chornobyl | 389 | 0 | 0 | 0 | ресурсный образ не найден на хосте | item catalog only |
| Clear Sky | 417 | 271 | 20 | 400 | официальный `xrussian.db` | catalog accepted |
| Call of Pripyat | 434 | 144 | 11 | 121 | официальный `xenglish.db` | catalog accepted |

Для web bundle также проверен официальный resource-source snapshot SoC:
389 предметов, 15 группировок и 225 адресов, limits `-3000…1000`, thresholds
`-400/500`. Он собран из `gamedata/config` репозитория iXRay 1.0 SoC
(commit `a11547a2e4e6426b77ebe4ee19550ab1cab7fdef`) и не является заменой
отсутствующего локального SoC resource image; desktop теперь использует для
этого случая тот же компактный metadata snapshot, что и browser.

В Clear Sky в итоговом каталоге, например, присутствуют resource-derived
`actor`, `csky`, `renegade`, `dolg`, `freedom` и другие ключи; в Call of
Pripyat присутствуют `actor`, `bandit`, `dolg`, `freedom`, `zombied` и другие
ключи. Это наблюдаемый результат установки, а не зашитый список. У каждой
записи сохранены exact key, display name (если есть), numeric ID, release ID
и источник секции; запрос чужого ключа заканчивается `CatalogLookupError`.

### Хэши выбранных ресурсных индексов

Хэши нужны для воспроизводимости локального probe и не содержат игровых
данных в репозитории:

| Ресурс | SHA-256 |
| --- | --- |
| Clear Sky `configs.db` | `90741a73997d73cafbf692c9109811b89725e87c6192d76e6bd373125dec1858` |
| Clear Sky `xrussian.db` | `35adabeea3ecd27c530a33c8ce274660713829c342a61934c838f6307dd8dc6b` |
| Call of Pripyat `configs.db` | `14ec49daee8880106c8fab5b7e38f115319afb4f48a6407b3541fe044d4f0d3a` |
| Call of Pripyat `xenglish.db` | `1d5874c050b128a3464ef86a9eef6112ef14ad829c6c582fab9a4990ae6af981` |

### Front ends и границы

`tools/build_web_catalogs.py` сериализует те же item/faction/relation/upgrade
metadata в `web/catalogs.json`. В браузер не попадают архивы, локальные пути и
сейвы; `web/web_bridge.py` проверяет release ID и не принимает чужие записи.
Если официальный ресурсный корень отсутствует, desktop использует тот же
заранее собранный metadata bundle без прототипов и игровых архивов; локальные
иконки остаются недоступны без официального atlas из установки.

Этот результат подтверждает resource catalog и cross-release isolation. Схема
сериализации faction relations и player community отдельно разобрана в
[XRAY_FACTION_RELATIONS](XRAY_FORMAT.md), но игровой
load/re-save, Enhanced Edition и совместимость с модами по-прежнему не
подтверждены.

---

## X-Ray faction relations — 2026-09-15

Этот файл фиксирует read-only разбор отношений в официальных оригинальных
сейвах SoC, ЧН и ЗП. Моды, личные файлы и игровые эксперименты в Git не
попадают.

### Схема из исходников

В официальных исходниках [iXRay 1.0 SoC](https://github.com/ixray-team/ixray-1.0-stsoc),
[iXRay 1.5 Clear Sky](https://github.com/ixray-team/ixray-1.5-stcs) и
[iXRay 1.6 Call of Pripyat](https://github.com/ixray-team/ixray-1.6-stcop)
`RELATION_DATA::load/save` сериализует сначала personal map, затем map
отношений к communities. Для каждой записи используются `u32 count`, ключи
и signed `s32 goodwill`; ключом верхней map является object id персонажа.
Часть InfoPortions находится перед relation map в том же ALife registry chunk.
SoC/ЧН сохраняют у InfoPortion также `u64` timestamp, ЗП — только строку.

Редактор разбирает этот ограниченный префикс chunk 9, запоминает точные
границы и патчит только goodwill строки актёра. Если строки нет, она
добавляется в actor row в отсортированном порядке; остальные строки и хвост
registry сохраняются. Личные отношения не смешиваются с community goodwill.

Исходники задают пределы community goodwill:

| Игра | community goodwill | neutral threshold | friend threshold |
| --- | ---: | ---: | ---: |
| Shadow of Chornobyl | -3000…1000 | -400 | 500 |
| Clear Sky | -3000…1000 | -999 | 999 |
| Call of Pripyat | -3000…1000 | -999 | 999 |

При отсутствии community строки движок возвращает `NEUTRAL_GOODWILL` (0),
поэтому это значение показывается как `0 (default)`, а не как сохранённая
строка.

### Read-only probe на локальном корпусе

Использованы только копии установленных официальных сейвов; байты не
перезаписывались.

| Релиз | Actor community | Relation rows | Actor community offset |
| --- | ---: | ---: | ---: |
| Shadow of Chornobyl | 0 | 4 | 75731 |
| Clear Sky | 4 | 13 | 98779 |
| Call of Pripyat | 0 | 5 | 61831 |

Измеренные community rows: SoC `[(3, 0), (7, 0), (8, 200), (9, 50)]`, ЧН
содержит 13 строк с пределами от `-3000` до `1000`, ЗП — 5 строк, включая
`(1, -350)`, `(2, 1000)`, `(3, 1000)` и `(9, 1000)`. Эти значения являются
снимками конкретных сейвов, а не универсальным состоянием кампании.

### Реализация и граница записи

- `editor/xray_relations.py` имеет bounded parser/writer для InfoPortions,
  actor relation row, signed 32-bit bounds и сохранения хвоста.
- `SaveInfo` показывает только реально сохранённые community goodwill rows.
- Qt и web показывают faction catalog, текущие значения и конкретное
  предупреждение для ЧН/SoC/ЗП; staged UI не меняет исходный файл.
- `EditPlan.faction_relations` валидирует exact release-scoped keys; чужой или
  неизвестный numeric id отклоняется.
- Synthetic tests проверяют patch одной строки, добавление отсутствующей
  строки, сохранение personal/other rows и round-trip.

C#-порт в StalkerSaveEditor.Core использует тот же ограниченный префикс
chunk 9, изменяет только actor row и проверяет точные ключи фракций и пределы
goodwill. Синтетические SoC/CS/CoP фикстуры, созданные Python-оракулом,
проверяют совпадение выходных байтов. Значения CS/CoP Enhanced Edition
читаются, но запись остаётся unsupported согласно Python capability registry.

Запись в production UI для трёх оригинальных релизов включена как
`experimental_fields`: Qt/web показывают предупреждение, desktop backup
обязателен, browser скачивает новую копию без изменения источника. Controlled
game load/re-save по процедуре M10 (Python editor, archived) ещё не выполнялся.
Структурный round-trip доказывает сохранение контейнера и полей, но не
доказывает, что сюжет не перезапишет community или что внутриигровой AI
примет изменение.

---

## X-Ray player community — 2026-09-16

Этот файл фиксирует исследование принадлежности игрока к группировке в
официальной оригинальной трилогии. Моды, личные файлы и байты сейвов в Git не
попадают. Проверка игровым запуском и повторным сохранением не выполнялась.

### Схема из исходников

В публичном исходнике [OpenXRay xray-16](https://github.com/OpenXRay/xray-16)
`CSE_ALifeCreatureActor::STATE_Write` сериализует сначала состояние creature,
затем `CSE_ALifeTraderAbstract::STATE_Write`. В trader STATE после `m_dwMoney`
идут `specific_character`, trader flags, profile, затем три signed `s32`:
`m_community_index`, `m_rank`, `m_reputation`; после них сохраняются имя и
deadbody flags. Поэтому редактор не ищет похожее число по payload, а проходит
тот же version-gated prefix и принимает community только с подтверждённым
offset.

Каталог community не выводится из текущего числа в сейве. Для каждой игры
используется её отдельный resource-derived faction catalog с numeric id; ключ,
которого нет в каталоге или у которого нет numeric id, writer отклоняет.

### Read-only probe корпуса

Проверены копии официальных сейвов трёх установленных релизов без записи в
исходные файлы:

| Релиз | Actor community | community offset подтверждён |
| --- | ---: | --- |
| Shadow of Chernobyl | 0 | да |
| Clear Sky | 4 | да |
| Call of Pripyat | 0 | да |

Это значения конкретных проверенных слотов, а не универсальное состояние
кампании. S.T.A.L.K.E.R. 2 и Enhanced Editions остаются read-only: для них нет
подтверждённой offline-схемы actor community.

### Реализация и граница

- `parse_xray` возвращает текущий signed `s32`, его bounded offset и флаг
  редактируемости.
- `EditPlan.player_faction` принимает только release-scoped catalog key.
- Writer меняет только четыре байта actor community, затем пересобирает
  контейнер и проверяет round-trip; остальные actor поля сравниваются тестом.
- Qt и web показывают текущую группировку, staged выбор и конкретное
  предупреждение о возможной перезаписи сюжетными скриптами. Web скачивает
  копию и не меняет исходный файл.
- Поле отмечено `experimental_fields`; структурный round-trip не доказывает,
  что сюжет сохранит принадлежность после загрузки. Контролируемое
  load/re-save по M10 остаётся отдельным незавершённым gate.

---

## Сюжетные сценарии изъятия инвентаря и денег в S.T.A.L.K.E.R.

### 1. Введение

В серии игр S.T.A.L.K.E.R. есть сюжетные и геймплейные эпизоды, в которых у игрока принудительно изымается инвентарь (полностью или частично) либо списываются все наличные деньги.

Для редактора сохранений такие моменты представляют особую опасность: если пользователь откроет сейв, сделанный в промежуточном состоянии (вещи уже изъяты, но ещё не возвращены), наивное редактирование инвентаря или баланса денег приведёт к дублированию предметов, безвозвратной утере модификаций, поломке скриптовой логики квеста или повторному списанию ресурсов движком.

В данном документе детально разобрана механика каждого такого момента во всех трёх играх трилогии (ТЧ, ЧН, ЗП), приведены точные файлы, секции логики, инфопорции, идентификаторы тайников, а также сформулированы правила и рекомендации для валидатора редактора.

---

### 2. Чистое Небо (CS / ЧН)

#### 2.1. Ограбление в подвале Барахолки (Свалка)

Самый известный и радикальный эпизод отъёма ресурсов в трилогии. Шрам спускается в подвал за КПК Клыка, наступает на растяжку, теряет сознание, а бандиты забирают все его вещи и деньги.

##### Конфигурация и логика рестриктора
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

##### Скриптовые функции отъёма
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

##### Тайник и квест возврата вещей
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

#### 2.2. Блокпосты бандитов на входах на Свалку (ЧН)
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

### 3. Тень Чернобыля (SoC / ТЧ)

#### 3.1. Арена (Арни, Бар «100 Рентген»)

Перед каждым поединком на Арене у Меченого полностью изымается всё снаряжение и выдаётся строго фиксированный боекомплект на бой.

##### Конфигурация и логика рестрикторов
- Рестриктор шлюза/входа на Арену: `configs/scripts/bar_arena_sr.ltx:1-35`
- Триггер боя: `configs/scripts/bar_arena_combat_triger.ltx:1-85`
- Диалоги с Арни: `configs/gameplay/dialogs_bar.xml:1902-1944, 3630-3776`
- Скрипт выдачи наград: `scripts/bar_dialogs.script:230-255`

##### Скриптовый отъём инвентаря
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

##### Завершение боя и зачистка Арены
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

#### 3.2. Вырезанный контент: КПЗ наёмников в Мёртвом Городе (ТЧ)
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

### 4. Зов Припяти (CoP / ЗП)

#### 4.1. Кража личного ящика Корягой на станции «Янов»
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

### 5. Опасности для редактора сохранений (RCA & Bug Scenarios)

#### Сценарий 1: Редактирование инвентаря во время ограбления в ЧН
**Состояние сейва**: игрок сохранился после подвала на Барахолке, квест `gar_quest_redemption` активен (`has_info("gar_quest_redemption_started") and not has_info("gar_quest_redemption_done")`).
1. **Что видит редактор в сейве**: в `cse_alife_creature_actor` практически пустой инвентарь (только нож, бинокль и фонарь).
2. **Что сделает пользователь**: подумает, что вещи «пропали из-за бага» или решит нагенерировать себе броню, оружие и патроны взамен отобранных.
3. **Последствия в игре**:
   - Когда игрок доберётся до тайника `gar_smart_terrain_5_6_box` (SID 727) и откроет его, там будут лежать **все его старые вещи**. Произойдёт непреднамеренное дублирование уникальных предметов (например, артефактов, прокачанных стволов).
   - Если пользователь изменил состояние брони/оружия, он отредактировал временные дубликаты, а оригинальные прокачанные предметы остались в ящике с прежним состоянием.

#### Сценарий 2: Редактирование денег перед подвалом в ЧН
**Состояние сейва**: сохранение в подвале прямо перед срабатыванием триггера `gar_ambush_hit`.
1. **Действие пользователя**: пользователь прописывает 1 000 000 RU в редакторе.
2. **Последствия в игре**: через 3 секунды после загрузки скрипт выполняет `take_money(all)`. Все введённые деньги сгорают без следа.

#### Сценарий 3: Редактирование инвентаря во время боя на Арене в ТЧ
**Состояние сейва**: сейв сделан прямо во время раунда на Арене (`has_info("bar_arena_fight") and not has_info("bar_arena_reset")`).
1. **Что в сейве**: реальный инвентарь игрока лежит в ящике SID `573` (`bar_arena_inventory_box`). На игроке надета выданная экипировка боя.
2. **Опасность 1**: Если игрок добавит в инвентарь ценные предметы, а во время боя уронит их на пол Арены — при завершении боя вызов `xr_zones.purge_arena_items("bar_arena")` удалит их навсегда через `alife:release`!
3. **Опасность 2**: Если игрок добавит себе топовую броню или оружие, то после победы он заберёт из синего ящика своё старое оружие, получив дубликаты и сломав баланс прогрессии.

#### Сценарий 4: Редактирование личного ящика на Янове в ЗП
**Состояние сейва**: активна стадия квеста Коряги (`jup_b52_actor_items_can_be_stolen` выдана, `jup_b202_actor_items_returned` отсутствует).
1. Ящик `jup_b202_actor_treasure` уже пуст, все вещи лежат в `jup_b202_snag_treasure`.
2. Если редактор сейвов предлагает редактирование персонального ящика базы — правка ящика на Янове запишет предметы в пустой ящик, а в тайнике Коряги останется копия.

---

### 6. Рекомендации для валидатора и UI редактора

#### 6.1. Детектирование опасных состояний
Редактор сейвов (через модуль `SaveValidator` или аналогичный аналитик инфопорций) должен проверять следующие комбинации:

| Игра | Опасное состояние | Условие проверки инфопорций / локации |
|---|---|---|
| **CS (ЧН)** | Ограбление на Свалке активно | `+gar_quest_redemption_started` И `-gar_quest_redemption_done` |
| **SoC (ТЧ)** | Игрок находится на Арене | `+bar_arena_fight` И `-bar_arena_reset` (или актор на `L05_bar` внутри шейпа `bar_arena_sr`) |
| **CoP (ЗП)** | Личный ящик похищен Корягой | `+jup_b52_actor_items_can_be_stolen` И `-jup_b202_actor_items_returned` |

#### 6.2. Реакция интерфейса (UI)
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

---

## Формат пакетов `se_level_changer` и телепортация между уровнями

### 1. Введение и постановка задачи

В моде-компаньоне для перемещения игрока между локациями используется механизм создания временного объекта `level_changer` непосредственно под координатами актора. При соприкосновении с формой перехода (шейпом) движок осуществляет перенос игрока на целевую локацию.

Однако сериализация состояния `level_changer` через сетевые пакеты (`net_packet`: `STATE_Write` / `STATE_Read`) различается между играми трилогии (ТЧ, ЧН, ЗП).

---

### 2. Порядок полей в движке и скриптах

#### 2.1. Исходники движка (C++)

Класс переходника: `CSE_ALifeLevelChanger`, наследующий `CSE_ALifeSpaceRestrictor` -> `CSE_Shape` -> `CSE_ALifeObject` -> `CSE_Abstract`.

##### Исходники:
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

##### Сериализация C++ `CSE_ALifeLevelChanger::STATE_Write`:
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

#### 2.2. Скриптовая обёртка `se_level_changer`

##### Зов Припяти (CoP) и Чистое Небо (CS):
В ЧН и ЗП серверный объект `level_changer` зарегистрирован через фабрику на скриптовый класс `se_level_changer.se_level_changer`:
- `gamedata_cs/scripts/class_registrator.script:20`
- `gamedata/scripts/class_registrator.script:22`
- Скрипт: `gamedata_cs/scripts/se_level_changer.script:8-15` и `gamedata/scripts/se_level_changer.script:19-26`

Метод `se_level_changer:STATE_Write(packet)` дописывает в пакет:
1. `set_save_marker(packet, "save", false, "se_level_changer")` — маркер начала сохранения.
2. `packet:w_bool(self.enabled)` — флаг активности перехода (`u8`).
3. `packet:w_stringZ(self.hint)` — подсказка при наведении (строка `stringZ`, обычно `"level_changer_invitation"`).
4. `set_save_marker(packet, "save", true, "se_level_changer")` — контрольный размер блока данных (`u16`).

##### Тень Чернобыля (SHoC):
В ванильном ТЧ класс `se_level_changer` **отсутствует**:
- В `gamedata_soc/scripts/class_registrator.script` регистрация скриптового класса для `level_changer` отсутствует (объект обрабатывается напрямую движковым `cse_alife_level_changer`).
- В пакете **нет** полей `enabled`, `hint` и нет маркеров `set_save_marker`.
- Пакет ТЧ завершается сразу после записи `silent_mode` (`u8`).

---

### 3. Таблица структуры пакета по играм

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

### 4. Ссылки на моды-телепорты на GitHub

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

### 5. Надёжное определение игры из скрипта

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

---

## X-Ray level changer STATE packet research — 2026-09-26

### Scope

Repository review found no parser or test for `all.spawn` level-changer objects.
`editor.xray_catalog.read_xray_asset()` returns bytes from an installed asset
archive; it does not decode the spawn file or dispatch object state by class.

This research records and tests only the class-specific suffix read by
`CSE_ALifeLevelChanger::STATE_Read`. It does not establish the `all.spawn`
container/object framing or locate the end of the inherited
`CSE_ALifeSpaceRestrictor` state.

### Source and field order

The reference is the public OpenXRay `xray-16` source pinned at commit
[`eda9503`](https://github.com/OpenXRay/xray-16/blob/eda9503dd4056e52fa9cee58dfae53f530cd5b9a/src/xrServerEntities/xrServer_Objects_ALife.cpp#L650-L742).
This is an OpenXRay implementation reference, not proof that every original
GSC game build uses the same object version or packet layout.

`CSE_ALifeSpaceRestrictor::STATE_Read` first reads its inherited dynamic-object
state, then `cform_read`, and reads the restrictor type only when
`m_wVersion > 74`. `CSE_ALifeLevelChanger::STATE_Read` then reads the following
class-specific suffix. The version in these conditions is the entity's
`m_wVersion`, not the save-container or actor-spawn version.

| Entity version | Suffix fields read | Interpretation |
|---|---|---|
| `< 34` | two `u32`, then two NUL-terminated strings | The two integers are discarded by the source; the parser leaves destination ids and vectors unknown. |
| `>= 34` | `u16`, `u32`, three `f32`, then direction | `m_tNextGraphID`, `m_dwNextNodeID`, destination position. |
| `34…53` | one `f32` direction value | Source stores it as the y component of `m_tAngles`; the other components are zero. |
| `> 53` | three `f32` direction values | Full `m_tAngles` vector. |
| all versions | two NUL-terminated strings | `m_caLevelToChange` (destination level name), then `m_caLevelPointToChange` (destination point name). |
| `> 116` | one `u8` | `m_bSilentMode`, interpreted as false/true. |

The code in `editor.xray_level_changer.parse_level_changer_state_suffix()`
implements only that suffix. Its caller must locate the suffix boundary and
provide the entity version. It also returns the number of consumed bytes so a
future object parser can account for any remaining state bytes explicitly.

### Test evidence

`tests/test_xray_level_changer.py` builds synthetic packet suffixes and checks
versions 118, 53, and 33, plus rejection of a truncated version-118 suffix.
These tests verify this decoder against the cited field order; they do not
prove game acceptance or match a real installed `all.spawn` asset. The test
names are ASCII; non-ASCII string encoding is not established here.

### Remaining evidence gap

No personal game assets were used or committed. A future anchor-builder change
still needs to establish the full `all.spawn` record framing, object class and
version source, inherited-state boundary, and release-specific compatibility
before it can safely emit destination points. This PR intentionally does not
add `tools/build_relocation_anchors.py` or `web/relocation_anchors.json`.

---

## X-Ray actor position and level (TP-1) — 2026-09-29

Read-only research on the owner's retail saves (62 Clear Sky, 6 Shadow of Chernobyl, Call of Pripyat).

### Where the actor's location lives

| Field | Place in the actor record | Notes |
|---|---|---|
| `o_Position` (3 × f32) | spawn header after `section\0 name\0 u8 game_id u8 rp` | same in SoC, CS, CoP |
| `o_Angle` (3 × f32) | right after the position | |
| `m_tGraphID` game vertex (u16) | STATE + 0 | selects the level through `game.graph` |
| `m_tNodeID` level vertex (u32) | STATE + 10 (after distance f32 and direct_control u32) | |
| position copy (3 × f32) | UPDATE + 11 (after size u16, health f32, timestamp u32, flags u8) | equals the spawn position in every save checked |

The game vertex decides the level: Clear Sky's graph (in `all.spawn`, chunk 4, `CVertex` 42 bytes, level id in the low
8 bits of the packed u32) maps marsh 0–244, garbage 245–471, escape 472–742, darkvalley 743–960, red_forest 961–1056,
agroprom 1057–1211, yantar 1212–1312, military 1313–1510, agroprom_underground 1511–1523, limansk 1524–1564,
stancia_2 1565–1572, hospital 1573–1619. Every save's game vertex fell in the level its title names.

### Proof that level-changer destinations are safe targets

`tss - Кордон.sav` (saved right after entering Cordon from the Swamps) has the actor at position
(-271.1, -21.7, -276.6), game vertex 473, level vertex 3366 — byte for byte the destination stored in the Swamps
level changer `mar_level_changer_to_escape_1`. The game itself places the actor there.

### Level-changer destination block

`gv u16, lv u32, position 3 × f32, direction 3 × f32, level\0, point\0`, then SoC: nothing; CS/CoP: silent u8,
flag u8, hint string (and sometimes a logic name), trailing u16. Located by parsing
(`XRayLevelChangerReader.ParseStateSuffix`) at the first offset whose names are identifiers and whose tail matches.

### Implementation

`XRayRelocation` (TP-3/TP-4, experimental): offers only these destinations, writes spawn position + angle,
STATE game/level vertex and UPDATE position in place (record length unchanged), and reads the result back.
In-memory check: SoC 37 destinations / 19 levels, CS 95 / 13, CoP 7 / 7 — each relocation read back at the
destination with inventory and money unchanged. **Not loaded in the game yet (L5).**

---

## Конфигурация, динамические циклы и движковый API погоды в S.T.A.L.K.E.R.

### 1. Введение и архитектура погодной системы X-Ray

Погодная система в движке X-Ray управляет освещением, параметрами неба, облачностью, туманом, дальностью прорисовки, дождём, грозовыми разрядами и амбиентными звуками в зависимости от внутриигрового времени.

#### 1.1. Движковые классы (C++)
- `CEnvironment` (`src/xrEngine/Environment.h`, `Environment.cpp`): синглтон окружения (`g_pGamePersistent->Environment()`), хранящий список всех погодных циклов `m_weathers`, текущий цикл, текущие интерполяторы и эффект погоды `m_pDescriptorFX`.
- `CEnvDescriptor`: описание состояния окружения на конкретный момент времени (небо, облака, цвета фога, солнца, интенсивность дождя `rain_density`, параметры грома `thunderbolt_collection`).
- `CEnvDescriptorMixer`: выполняет покадровую линейную интерполяцию параметров между двумя соседними часовыми секциями `CEnvDescriptor` с учётом текущего игрового времени (`level.get_time_hours()`, `level.get_time_minutes()`, `level.get_time_seconds()`).
- `CEnvAmbient`: менеджер фоновых спецэффектов (звуковые каналы `sound_channels`, летящие частицы, порывы ветра).

#### 1.2. Экспорт функций движка в Lua (таблица `level`)
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

### 2. Тень Чернобыля (SoC / ТЧ)

#### 2.1. Конфигурационные файлы
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

#### 2.2. Секция `[weathers]`
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

#### 2.3. Погодные спецэффекты `[weather_effects]`
Файл: `configs/weathers/environment.ltx:34-36`:
```ini
[weather_effects]
surge_day   = sect_surge_day
p_surge_day = sect_p_surge_day
```

#### 2.4. Привязка погоды к уровням
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

#### 2.5. Скриптовая логика ТЧ (`level_weathers.script:1-63`)
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

### 3. Чистое Небо (CS / ЧН)

#### 3.1. Конфигурационные файлы
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

#### 3.2. Динамический погодный граф
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

#### 3.3. Привязка погоды к уровням ЧН
Файл: `configs/game_maps_single.ltx`:
- Болота (`[marsh]`, строка 67):
  ```ini
  weathers = {-mar_intro_scene_1_end} night, {+mar_intro_scene_1_end -mar_tutorial_return_to_base_reversed} default_clear, dynamic_default
  ```
- Все открытые уровни (Кордон, Свалка, Тёмная Долина, Агропром, Янтарь, Армейские склады, Рыжий лес, Лиманск, Госпиталь): `weathers = dynamic_default` (строки 10, 19, 28, 33, 44, 62, 72, 77, 87).
- Катакомбы Агропрома: `weathers = indoor` (строка 14).
- ЧАЭС: `weathers = stancia2` (строка 82).

#### 3.4. Скриптовая динамическая погода ЧН (`level_weathers.script:1-241`)
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

### 4. Зов Припяти (CoP / ЗП)

#### 4.1. Конфигурационные файлы
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

#### 4.2. Динамический погодный граф CoP
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

#### 4.3. Привязка к уровням CoP
Файл: `configs/game_maps_single.ltx`:
- Затон (`[zat_b38]`, строка 50): `weathers = dynamic_default`
- Окрестности Юпитера (`[pri_a16]`, строка 55): `weathers = dynamic_default`
- Путепровод Припять-1 (`[jupiter_underground]`, строка 60): `weathers = indoor_ambient`
- Припять (`[pripyat]`, строка 65): `weathers = dynamic_default`
- Лаборатория X-8 (`[labx8]`, строка 70): `weathers = indoor`

#### 4.4. Скриптовая реализация (`level_weathers.script:1-241`)
Полностью совпадает по архитектуре со скриптом ЧН:
- Ежечасно переключает состояния `clear`, `cloudy`, `rain`, `thunder`.
- Вызывает `level.set_weather("default_" .. st.current_state, now)`.
- Синглтон: `level_weathers.get_weather_manager()`.

---

### 5. Сводная таблица погодных циклов по играм

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

### 6. Реализация в моде-компаньоне

#### 6.1. Протокол (`docs/COMPANION.md`)
Добавлена команда:
```
command: v1 <id> weather [<section>] [now]
reply:   v1 <id> ok weather=<name>
```
Если аргумент не указан, возвращается имя текущей погоды (`level.get_weather()`).
Если аргумент указан, вызывается `level.set_weather(section, now)`.

#### 6.2. Обработчик в `save_editor_companion.script`
Функция `handlers.weather(args)`:
1. Валидирует доступность функции движка `level.set_weather`.
2. Читает флаг немедленного применения `now` (по умолчанию `true`).
3. Вызывает `level.set_weather(weather, now)`.
4. В ЧН и ЗП при установке погоды из динамического графа (`clear`, `cloudy`, `rain`, `thunder` или префиксов `default_*`) обновляет текущее состояние `st.current_state` и `st.next_state` в `level_weathers.get_weather_manager().state`. Благодаря этому динамический погодный менеджер не сбрасывает выбранную погоду в следующее наступление игрового часа, а плавно продолжает цикл от выбранного состояния.

#### 6.3. Интерфейс (UI) в companion
Вкладка «Мир» (`world`) в `save_editor_companion_ui.script`:
- Показывает список доступных погодных циклов из соответствующего для игры `save_editor_weather.script`.
- Карточка справа отображает: название погоды, имя секции движка, описание и текущую погоду.
- Кнопка **«Погода»** (`btn_a`) или **двойной щелчок** по строке списка мгновенно активирует выбранную погоду.
- Кнопка **«Выброс»** (`btn_b`) запускает сценарий выброса (`surge_manager.start_surge()`).
- Степпер **`-`** и **`+`** переключает ускорение времени (`x1`, `x2`, `x5`, `x10`, `x20`, `x50`, `x100`).

---

## Official Enhanced Edition format evidence — 2026-09-15

Enhanced Edition profiles remain visible as official release descriptors, but
they are not registered as readable formats until a real, legally usable save
sample proves the container and versioned object serialization. This prevents
the original X-Ray parser from being presented as compatible merely because a
file has a familiar extension.

### Local read-only inventory

The machine was checked through the release-aware Steam manifest and save-path
providers. The probe did not open Steam, write a save, or print personal paths.

| Official release | Steam release tree detected | Candidate save directories | Candidate files | Current parser profile |
| --- | ---: | ---: | ---: | --- |
| S.T.A.L.K.E.R. 2: Heart of Chornobyl | 0 | 0 | 0 | registered S2 profile; no local sample in automatic paths |
| Shadow of Chornobyl — Enhanced Edition | 0 | 0 | 0 | unavailable; no EE adapter registered |
| Clear Sky — Enhanced Edition | 0 | 0 | 0 | unavailable; no EE adapter registered |
| Call of Pripyat — Enhanced Edition | 0 | 0 | 0 | unavailable; no EE adapter registered |
| Original Shadow of Chornobyl | 1 | 1 | 4 `.sav` | registered original X-Ray profile |
| Original Clear Sky | 1 | 1 | 56 `.sav` | registered original X-Ray profile |
| Original Call of Pripyat | 1 | 1 | 168 `.scop` | registered original X-Ray profile |

The zero EE rows are a current-host observation, not a claim that the releases
do not create saves on Windows. Desktop discovery now covers the documented
`Saved Games/<release>- EE/{STEAM,gog}/savedgames` roots, both `Prypiat` and
`Pripyat` spellings, and the corresponding Proton prefixes. Candidate hints
include `.sav`, `.scop`, `.scs` and the `.dds`/`.info` sidecars; sidecars remain
non-editable and all EE candidates remain unsupported until a parser exists.

### Public leads checked

- The [official GSC FAQ](https://www.stalker-game.com/es/faq) says the Enhanced
  games are separate PC releases and that saves from the original PC versions
  cannot be transferred to Enhanced. This confirms that an original-save
  parser cannot be silently reused as an EE adapter.
- The [Shadow of Chornobyl EE Steam discussion](https://steamcommunity.com/app/2427410/discussions/0/528723757459612258/)
  describes EE save-side `.sav`, `.dds`, and `.info` files under a separate
  Saved Games location.
- The [Clear Sky EE Steam discussion](https://steamcommunity.com/app/2427420/discussions/0/603030907426702266/?l=ukrainian)
  contains a report involving `.scs`, which is a useful extension lead but not
  a serialization specification or a verified sample.
- SteamDB's official Steam Cloud manifests independently document the Windows
  `Saved Games` roots for [SoC EE](https://steamdb.info/app/2427410/ufs/),
  [CS EE](https://steamdb.info/app/2427420/ufs/) and
  [CoP EE](https://steamdb.info/app/2427430/ufs/); those entries use wildcard
  save patterns but provide no binary serialization details.
- Public original-X-Ray readers and OpenXRay sources were used for the
  original profiles only; they do not prove EE compatibility.

No public source located in this pass supplied a complete, license-clear EE
save fixture plus enough format detail to expose inventory mutations safely.
Therefore no Enhanced Edition format adapter, catalog, or writer is enabled.

### User-facing boundary

The Qt release selector and release-specific path settings include all three
official EE descriptors. Discovery can report an EE candidate and its exact
unsupported reason. Opening still requires a registered parser; until the
evidence row is accepted, the candidate remains read-only/unavailable rather
than being routed through an original-game parser.

Required next evidence for each EE release is a real sample (or a reproducible
public format description), exact header/compression/version identification,
wrong-game rejection, strict no-op round-trip, and then controlled game-load
validation. One EE sample must not be generalized to the other two releases.

---

## Enhanced Edition save format — 2026-09-26

Sample: 17 fresh saves made by the owner through Steam/Proton (SoC EE 4, CS EE 7,
CoP EE 6; new games, a few pickups and trades). Saves stay private.

### Result

The EE saves use **the original X-Ray container unchanged**: `0xFFFFFFFF`, outer
version, unpacked size, then one LZO1X stream. Chunks are the same set and order
(`0 ALIFE`, `5 GAME_TIME`, `1 SPAWN`, `0x10`, `2 OBJECTS`, `9 REGISTRY`). The object
serializer is the original one: actor spawn version 118 (SoC) and 128 (CS, CoP).

| Game | Outer version | ALIFE version | Actor | Original for comparison |
|---|---|---|---|---|
| SoC EE | 3 | **51** | 118 | 3 / 3 / 118 |
| CS EE | **6** | **54** | **128** | 5 / 5 / 122–124 |
| CoP EE | 6 | **54** | 128 | 6 / 6 / 128 |

The ALIFE header number is the only thing that separates an EE save from its
original. CS EE and CoP EE share every version number; the object chunk tells
them apart by level names (`marsh` only in CS, `zaton` only in CoP). A save with
neither marker is refused.

### Read results (L4 on real saves)

Money and inventory parse on all 17 saves, e.g. SoC EE late save: 150 roubles,
PM, 9×18 stacks, bandages, bread, energy drinks, vodka; CoP EE: 2271, AKS-74U,
«Zarya», medkits, RGD-5. CS EE relation registry does not parse with the original
layout (warning, relations stay read-only).

### Not yet

- Owner screenshots to confirm the numbers (L5 for reading).
- Writing (EE-4): money first, local L5 before Steam Cloud.

---

## Упаковка модов под S.T.A.L.K.E.R. Enhanced Edition (Legends of the Zone Trilogy)

Документ описывает структуру модов, формат метаданных `desc.json`, архитектуру контейнера `.xrp` / `.pack` и процедуру упаковки/загрузки мода-компаньона для версий Enhanced Edition (Shadow of Chornobyl, Clear Sky, Call of Prypiat).

---

### 1. Официальные инструменты и источники

GSC Game World предоставляет официальный инструментарий для моддинга трилогии EE:
- **Официальный гайд в Steam:** [Steam Community Guide #3497576322](https://steamcommunity.com/sharedfiles/filedetails/?id=3497576322).
- **Набор утилит `stk-utils.7z`:** `http://modio.stalker-game.com/assets/ee/stk-utils.7z`:
  - `stk-utils/workshop/xrCompress.exe` (версия для Steam Workshop — поддерживает `.script` и `.ltx`).
  - `stk-utils/modio/xrCompress.exe` (версия для Mod.io / консолей — намеренно отфильтровывает скрипты и конфиги).
  - `stk-utils/workshop/xrSWS_Upload.exe` (консольная утилита публикации в Steam Workshop).

---

### 2. Различия платформ: Steam Workshop vs Mod.io

| Возможность | Steam Workshop (PC EE) | Mod.io (Консоли) |
|---|---|---|
| Скрипты (`.script`) | **Разрешены** | **Запрещены** (модерация отклоняет) |
| Конфиги (`.ltx`, `.xml`) | **Разрешены** | **Запрещены** |
| Ресурсы (текстуры, звуки, модели) | Разрешены | Разрешены (до 1 ГБ распакованного размера) |
| Бинарники (`.dll`, `.exe`) | **Запрещены** | **Запрещены** |

> **Вывод:** Внутриигровой мод-компаньон редактора сейвов, использующий Lua-скрипты, для Enhanced Edition может распространяться **только через Steam Workshop** (или устанавливаться вручную).

---

### 3. Формат контейнера `.xrp` / `.pack` / `.xdb0`

Контейнеры `.xrp` в Enhanced Edition — это **классический формат архивов X-Ray DB** (`.db*`, `.xdb*`):

#### Бинарная структура файла:
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

### 4. Формат манифеста `desc.json`

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

#### Поля:
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

### 5. Структура папок мода

#### Вариант 1: Стейджинг для загрузки в Steam Workshop
Папка, передаваемая параметром `--path` утилите `xrSWS_Upload.exe`:
```
dist/ee_cop_companion/
├── desc.json
├── preview.jpg
└── save_editor_companion_cop.xrp   (или .pack)
```
*(Если в моде есть видеоролики `.mp4`, они не пакуются в `.xrp`, а лежат открыто в подпапке `textures/ui/video/`)*.

#### Вариант 2: Локальная установка (распакованная)
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

### 6. Пошаговая инструкция упаковки

#### Автоматическая упаковка (рекомендуемый способ)
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

#### Ручной запуск `xrCompress` под Linux (Wine / Proton)
1. Скачать официальный `stk-utils.7z`.
2. Запустить упаковку через Wine:
   ```bash
   WINEDEBUG=-all wine stk-utils/workshop/xrCompress.exe /path/to/mod_gamedata_folder -store
   ```
3. В родительской папке появится файл `<folder_name>.xdb0`.
4. Переименовать `<folder_name>.xdb0` в `save_editor_companion.xrp` и переместить в стейджинг к `desc.json`.

#### Публикация в Steam Workshop
Запуск `xrSWS_Upload.exe` (требует запущенного клиента Steam):
```cmd
xrSWS_Upload.exe --mode=create --game=COP --path="C:\mods\ee_cop_companion" --preview="C:\mods\ee_cop_companion\preview.jpg" --title="S.T.A.L.K.E.R. Save Editor Companion" --desc="In-game companion mod for Call of Pripyat Enhanced Edition"
```
После успешного создания Steam присваивает моду `PublishedFileId`, который можно указать в `desc.json` для последующих обновлений через `--mode=update`.
