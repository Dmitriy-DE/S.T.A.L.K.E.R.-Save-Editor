# S.T.A.L.K.E.R. 2 saves: format and evidence

## S2 compact key mapping evidence — 2026-09-16

### Вывод

Сейчас нет принятого конструктора `prototype SID → новый compact type_key` для
S.T.A.L.K.E.R. 2. Поэтому read-only catalog из M21 не открывает Add/clone/
upgrade writer.

При этом для уже существующих inventory objects подтверждено отдельное
read-only соответствие: последние два байта `type_key` — индекс в embedded
save-local таблице имён, которая начинается с `GunAK74_ST`. Это не означает,
что таблица является универсальным SID-реестром или что по ней можно безопасно
создать новый object.

Таким образом, `type_key` больше не показывается пользователю как безымянный
для тех строк, которые однозначно разрешаются внутри самого сейва. Его
семантика для записи новых объектов и применимость к произвольному build всё
ещё не доказаны.

### Локальный corpus

Команда читала четыре личных файла вне Git через общий parser и печатала только
агрегаты и SHA-256:

```text
python tools/analyze_s2_mapping.py <sample-1.sav> <sample-2.sav> <sample-3.sav> <sample-4.sav>
```

| Sample | SHA-256 | Inventory objects | Owned handles | Grid cells |
| --- | --- | ---: | ---: | ---: |
| sample-1 | `800aad0f6ae3517b9b6fcf05dd0bf758e4cd9c33d76dfb25be96b6dd02e80f0d` | 25 | 39 | 36 |
| sample-2 | `56cb09bb639aa8af80cd705961fa14613c8c8851d936c3ded00344504401dc78` | 36 | 53 | 81 |
| sample-3 | `bc7435ca9e22bde4646b2fa0f60fcf96040ab0f4e55a3bff1791c6fb7d8c0a3e` | 34 | 53 | 65 |
| sample-4 | `fd9664ac0a1084bd386ea7ee935c53315c9f74e1504913a363338d24327ea3af` | 34 | 53 | 65 |

После чтения embedded name table все parsed inventory rows в этих четырёх
файлах получили имя: `25/25`, `36/36`, `34/34`, `34/34`. Повторяющиеся
контрольные примеры:

```text
050000 → table[0]   → GunAK74_ST
041600 → table[22]  → Bandage
041700 → table[23]  → Medkit
043200 → table[50]  → A762NATOS
043900 → table[57]  → A556D
047a00 → table[122] → AntiRad
```

Индекс является save-local: в разных сохранениях таблица может иметь разный
размер и дополнительные строки, поэтому приложение не переносит этот индекс
в другой файл без повторного чтения его таблицы.

Между четырьмя образцами есть 34 handle, встречающихся минимум дважды. У 13
из них наблюдаемый `type_key` различается, например:

```text
handle 0x30002091: 046701 → 046501
handle 0x30002649: 048501 → 048201
handle 0x3000283C: 048200 → 047A00
```

Отдельно ключ `052000` был замечен у двух разных handle (`0x30006D62` и
`0x300075FB`) в совокупности образцов. Это competing negative evidence против
простого утверждения «один ключ — один object/SID»; оно не доказывает
конкретную альтернативную схему.

### Внешние источники и граница

Официальное [руководство по сохранениям S2](https://www.stalker2.com/news/saves-managing-manual)
описывает расположение, копирование и замену файлов, но не serializer
inventory objects. Официальный [Zone Kit guide по созданию оружия](https://zonekit-support.stalker2.com/hc/en-us/articles/38198715901329-Zone-Kit-Guide-Adding-new-Weapon)
подтверждает loose prototype CFG/SID и связанные upgrade prototypes. Публичный
[список console-команд](https://github.com/scalespeeder/stalker-2-pc-console-common-useful-commands-list)
создаёт предметы внутри запущенной игры; это не описание offline `.sav` object
registry.

Следующий достаточный вход — три контролируемые пары `Save A → один известный
SID → Save B` из разных категорий с сохранённым source/edited/resaved SHA и
последующим game load/re-save/read-back. В текущем окружении S2 не установлен,
такого входа нет; код намеренно оставляет статус `unconfirmed`.

### Safety

`save_format.locate_s2_item_name_table` и `editor/s2_mapping.py` не имеют
writer path. CLI не печатает имя/путь файла, не сохраняет отчёт автоматически
и не добавляет данные в browser bundle. Личные сейвы, скриншоты и raw payload
в репозиторий не добавлялись.

---

## S2 catalog evidence — 2026-09-16

### Source boundary

The public [S.T.A.L.K.E.R. 2 Zone Kit Phase 2 notes](https://www.stalker2.com/news/zone-kit-phase-2-new-features)
describe official item/inventory and upgrade-related modding data, but they do
not specify the offline `.sav` serializer. The implementation and current
Zone Kit/Workshop source boundary are recorded in
the S2 Zone Kit / Steam Workshop catalogue (its note was removed with the Python-era history).
The community
[stalker2cfg resource dump](https://github.com/chrisvblemos/stalker2cfg) and
[S2 Zona Configurator](https://github.com/dmcooller/S2ZonaConfigurator) document
the loose nested `struct.begin` configuration convention and prototype SIDs.
They are syntax references only; their data is not bundled and their mod
writers are not used by Save Editor.

The official [manual save-management page](https://www.stalker2.com/news/saves-managing-manual)
documents where the game keeps/copies saves. It does not establish a mapping
from an S2 prototype SID to the compact three-byte key observed in a save.

### Accepted implementation boundary

`editor/s2_catalog.py` reads loose files below the canonical official
`Content/GameLite/GameData/ItemPrototypes` directory. It rejects obvious mod
roots for canonical loading, ignores symlinks and malformed files, and never
scans PAK archives. An explicit `load_overlay` path can read a Workshop/Zone
Kit loose tree as provenance-labeled catalog research data; it does not merge
that overlay into canonical metadata or enable a save writer.
Each returned `ItemDefinition` has `serialization_family=None` and
`prototype=None`; this prevents metadata from being mistaken for a save
serializer or a safe clone template. Upgrade IDs are attached only to the
exact item SID named by the same official config record.

`S2CatalogProvider` returns an empty `FactionCatalog` in its combined view only
to keep the shared release contract; that is explicit missing evidence, not a
claim that S2 has no factions.

### Tests

The private-data-free fixture in `tests/test_s2_catalog.py` covers item fields,
nested `UpgradePrototypeSIDs`, source-path discovery and mod-overlay rejection.
`tests/test_catalog_bundle.py` covers browser metadata load/export without a
faction section. No personal save, S2 game archive or mod file is committed.

The host has no installed S2 resource root, so no real S2 catalog count is
reported and `web/catalogs.json` intentionally remains the original X-Ray
catalog only. The `.sav` parser still reports S2 inventory with unknown
compact keys; no SID mapping or mutation capability was enabled.

---

## S2 item addition — what the game does (2026-09-26)

Pair: two real saves of one play session (private). Save A: 511 109 coupons,
44 inventory items. Save B, after buying and looting: 459 086 coupons, 63 items.

### Findings

19 items are in B's inventory and not in A's. Comparing each handle's object
record in both saves:

| What happened | Items | How the handle was obtained |
|---|---|---|
| **Transfer** — same object moved to the player | 14 (quest PDAs, keys, scopes, an artifact) | the object already existed in A with the same type; only its ownership/grid place changed |
| **New record, recycled handle** | 4 (bought ammo boxes 5.56, 7.62 NATO, 9×39 ×2) | in A the handle belonged to a different, since-destroyed object |
| **New record, free handle** | 1 (Bloodstone from loot) | A has no record with that handle; it is a gap below the maximum |

- The highest handle is `0x3000874a` in both saves: the game fills gaps and
  reuses freed handles instead of growing the range.
- Stack growth (bandages 34 → 46, 5.45 122 → 235 …) is a count change inside the
  existing record, the path ED-3 already confirmed in the game.

### Consequences for ED-2

1. **Transfer (first stage):** give the player an object that already exists in
   the save (stash, world loot): add its handle to the owned list and a free
   grid cell. The game did this 14 times in the sample.
2. **Clone (second stage):** a copy of an existing record under an unused
   handle below the maximum, as the game did for loot and purchases. Needs the
   record insertion point and its own L5 check.

### Open questions

- Where the world/stash owner of a transferred object is recorded, and whether
  it must be cleared (compare a stash item before/after moving it to the
  backpack; S2-STASH).
- The S2 stash is shared by all territories (owner's note); in CS/CoP each
  location has its own stash.

---

## S.T.A.L.K.E.R. 2 equipment condition writer — 2026-09-19

Этот документ фиксирует границу экспериментальной поддержки состояния
экипировки. Личные сейвы, распакованные payload bytes и содержимое Steam Cloud
в репозиторий не добавляются. Для расположения локальных копий используется
уже принятый [S2 save-location evidence](RESEARCH.md).

### Что подтверждено

В одном actor-owned S2 object с `kind=1` одновременно выполняются все
условия безопасного anchor:

1. handle находится в верхней записи объекта;
2. тот же little-endian handle повторяется по адресу `record_offset + 0x23`;
3. по адресу `record_offset + 0x27` лежит little-endian `f32` в диапазоне
   `0…1`;
4. object входит в actor-owned handles и не является unresolved orphan.

В локальном differential corpus четыре независимых `kind=1` записи проходили
эту nested-shape проверку; в наблюдавшихся парах значение было `0.75`. В
отдельном actor-owned экипированном объекте Exoskeleton parser прочитал
`0.9745476246`, показал запись как `экипировано` и разрешил staged изменение.
Эти наблюдения подтверждают форму поля, но не утверждают, что каждая броня,
оружие или версия игры использует тот же layout.

Свежий read-only scan настроенного локального S2 `SaveGames/Data` root увидел
51 `.sav`: 33 файла разобрались текущим inventory parser-ом, 18 были честно
отклонены до inventory из-за отсутствующего уникального wallet anchor. В
разобранных файлах получено 1 153 inventory rows, включая 297 equipped rows и
33 `kind=1` armor rows; все 33 armor rows прошли exact anchor guard. До и после
сканирования SHA-256 и `mtime_ns` каждого файла совпали; scan ничего не писал.

Классификация не выводится из одного `kind=1`: embedded save-local name table
используется для exact suffix `_Armor`/`_Helmet`. Поэтому `GunBucket_*`, grid
rows и modifier names вроде `*_Armor_PSY_*` не получают armor writer только из-за
похожего kind или имени. Loose official CFG catalog дополнительно даёт
display-name/icon metadata, когда установленная игра предоставляет такие
ресурсы; при packed-only install UI показывает честный category glyph.

Реализация в `editor/s2_item_state.py` пишет только четыре байта этого
подтверждённого `f32`. Перед записью она проверяет kind, оба handle и диапазон;
после container rebuild `save_format.patch_save` повторно читает каждое
запрошенное значение и отклоняет transaction при расхождении. Preview не
меняет исходные bytes, а capability помечена как experimental.

### Текущее состояние после corpus-проверки 2026-09-22

В `editor/s2_item_state.py` добавлен отдельный weapon codec. Он принимает
только kind `0` с тем же handle, локальным record boundary, компактным
upgrade-vector из известных name-table ключей и подтверждённым direct-module
run; случайный `f32`, модуль, ПНВ или бинокль anchor-ом не становятся.
Condition оружия проходит тот же immutable `EditPlan`/repack/round-trip guard,
что и броня. Текущий статус — `experimental`: game load/re-save ещё не
проверен. Наблюдаемые Kharod/Lavina/Skif anchors и модульные границы описаны
в [S2 weapon/module corpus](S2_FORMAT.md).

### Что намеренно остаётся read-only

| Возможность | Граница | Что нужно до включения |
|---|---|---|
| Condition оружия | Source-backed read/write только для подтверждённой shape; game acceptance не подтверждён | Контролируемая пара «один weapon повреждён/починен» с однозначным handle, полем и game read-back для расширения охвата |
| Upgrades экипировки | Наблюдаемые upgrade-key arrays не разделены надёжно на installed/current и available/applicable | Пара с ровно одной установленной игрой upgrade и подтверждённым сериализатором всех affected references |
| Выдать предмет из каталога | Official CFG/SID описывает prototype metadata, но не является доказанным constructor для `.sav` object registry | Пара с ровно одним pickup, allocator/handle, owner/grid/stack/reference edges и game read-back |

Поэтому S2 capability открывает экспериментальную condition-правку
подтверждённых оружия/брони. `add_items`, S2 upgrades и неподтверждённые
device/module records остаются выключенными; неизвестные records не
превращаются в предметы только из-за похожего имени или соседнего `f32`.

### Controlled-pair protocol

Все шаги выполняются пользователем вручную на копиях вне живого Cloud slot.
До любого эксперимента сохранить исходный файл и SHA-256. Codex не запускает
игру и не пишет в игровую папку.

#### 1. Одна известная броня

1. В игре сделать отдельный ручной save `armor-before`: экипировать одну
   конкретную броню и записать её название/состояние.
2. Выполнить ровно одно действие, меняющее condition (например, получить
   повреждение), и сохранить `armor-after` в новый slot. Не менять оружие,
   инвентарь, задания или upgrades между двумя saves.
3. В редакторе сравнить распакованные копии: должен сохраниться тот же
   actor-owned handle, type/name и object-reference graph; должен измениться
   только подтверждённый condition anchor и необходимые CRC/Kraken metadata.
   Любой дополнительный diff сначала классифицируется, а не игнорируется.
4. Открыть `armor-before` в редакторе, застейджить новое значение condition,
   сделать Preview/Save в новую локальную копию и проверить fresh SHA,
   decompression/CRC round-trip и post-rebuild read-back.
5. Загрузить эту копию в игру, убедиться, что игра принимает save, затем
   снова сохранить его. Новый game save должен сохранить тот же object handle,
   показать ожидаемую прочность и снова пройти parser read-back.

Только после этой строки evidence можно считать armor condition game-accepted;
до неё статус остаётся experimental.

#### 2. Один известный upgrade

Сделать `upgrade-before` и `upgrade-after` с одной и той же бронёй, установив
ровно один известный upgrade и не меняя другие поля. В diff нужно доказать:

- однозначную запись установленного upgrade и её границы;
- какие arrays являются installed/current, а какие available/applicable;
- связь upgrade с owner/object handle и отсутствие скрытого удаления/добавления;
- валидный container round-trip и game load/re-save после записи редактором.

Пока эти условия не выполнены, writer не трогает ни одну из двух наблюдаемых
upgrade-key arrays.

#### 3. Один pickup из каталога

Сделать `item-before` и `item-after`, подняв в игре ровно один известный
предмет из официального каталога. Зафиксировать type/prototype key, количество
и место в инвентаре. В diff нужно найти и подтвердить одновременно:

- allocator нового handle и полную object record;
- prototype/type binding, owner reference и grid/stack placement;
- все обязательные dependent references, которые игра создаёт вместе с item;
- отсутствие случайного клонирования существующего объекта;
- game load/re-save для файла, созданного writer-ом.

Одного SID из metadata-каталога недостаточно: до такой пары `add_items` остаётся
запрещённым.

#### 4. Cloud только после локального acceptance

Сначала пройти локальный load/re-save и сохранить backup/read-back SHA. Только
после этого выбрать отдельный Cloud slot, скачать его через текущий Cloud flow,
применить ту же transaction и проверить persisted/read-back SHA. Live upload
не заменяет игровую проверку и не должен быть первым местом эксперимента.

### Current verdict

Экспериментальная правка condition подтверждённой S2 брони реализована с
точным anchor, backup/preview и post-rebuild guard. Игра ещё не выполнила
controlled load/re-save для этой правки в рамках этого evidence, поэтому
заявлять полную поддержку экипировки, upgrades, выдачи предметов или weapon
condition нельзя.

---

## S.T.A.L.K.E.R. 2 equipment corpus report — 2026-09-22

This evidence is aggregate-only. The repository contains no personal save
paths, raw payload bytes, or copied save files. The corpus command assigns
accepted inputs anonymous `sample-001`-style identifiers and records only
SHA-256, packed/raw sizes, parser release, category totals, ownership/grid/
equipped counts, and condition-anchor status.

### Reproducible read-only command

```bash
python3 tools/research_equipment.py \
  --input-root /explicit/copied/S2/SaveGames/Data \
  --report /tmp/s2-equipment-corpus.json \
  --release stalker2
```

Every candidate is snapshotted before parsing. After each input, the command
compares bytes, size, and `mtime_ns`; a changed input aborts the run instead
of producing evidence for a moving corpus. Files that the parser cannot
accept are counted only in `rejected_count`; their names and paths are not
written to the report.

### Observed boundary

The current read-only scan of the supplied local corpus accepted 6 S2 Data
files and rejected 9 files without a unique wallet/parser boundary. The
accepted files contain 228 actor-owned observations, 171 grid observations
and 54 equipped observations in aggregate; category totals are 14 weapons,
16 armor, 2 helmets, 17 module observations, 4 devices and 116 unresolved or
other rows. S2 armour perk rows such as `*_Armor_protection*` are classified
as modules even when their opaque serialized kind resembles a weapon. Accepted
files are approximately 6.65–6.73 MB packed and
26.07–26.69 MB raw. The command recorded each accepted input SHA-256 and
verified bytes, size and `mtime_ns` before/after parsing.

Separate equipment evidence records armor condition anchors, source-backed
weapon condition candidates, device rows and module observations. The current
codec enables only the confirmed weapon/armor condition shapes as
`experimental`; this corpus alone does not make them game-accepted.

`NVG_NPC_Gen3` is an actor/grid device observation. `Binoculars_*` and
`NVG_Gen2` appearing solely in the embedded name table are metadata, not owned
inventory rows. The synthetic regression test enforces that boundary.

### Gate

This report is research evidence only. It does not promote an experimental
writer to game-verified and never turns an unknown condition into zero.

---

## S.T.A.L.K.E.R. 2 weapon condition and module corpus — 2026-09-22

This evidence note records observations from the two newly supplied full S2
`SaveGames/Data` files and the earlier supplied Data corpus. Save bytes are
not stored in the repository. The hashes below identify the external samples
without making them fixtures.

### New samples

| Sample label | Packed bytes | Raw bytes | SHA-256 | CRC/parser |
|---|---:|---:|---|---|
| `610B5C7749C53C84312F7F841B50EE81 (1)` | 6,724,666 | 26,693,031 | `f107fb08ac972f3c20a9e59e84839d912c9ff1232963bec702802fa391124394` | CRC OK; S2 parser accepted |
| `217BB29D4FA4C87BD9F734AE755338CB` | 6,665,035 | 26,250,825 | `38f45e365440c2df6fd15032c2b06331809ae70ad5b0f72e23a89b2ea4f23796` | CRC OK; S2 parser accepted |

Both samples contain the same actor-owned Kharod handle `0x30002D01` and the
same grid Lavina handle `0x3000265C`. They are not a one-field binary pair:
the Kharod record has additional changes in state/module-related bytes, so
the condition conclusion below is a strong observed candidate, not yet a
game-accepted writer contract.

The wider local Data corpus also contains an actor-owned `Gun_SkifGun_HG`
record. Its primary weapon state is now read separately from a later embedded
Lavina snapshot that can appear inside the same broad record-end window. The
reader keeps ambiguity inside the primary state prefix read-only and accepts
only the single early candidate; this prevents the later `0.394` Lavina value
from being displayed as the pistol's condition.

### Condition observations

#### Kharod

The Kharod condition candidate is the little-endian `f32` immediately before
the observed Kharod upgrade-key vector:

| Sample | Handle | Relative offset | Raw f32 | UI-equivalent |
|---|---|---:|---:|---:|
| `610... (1)` | `0x30002D01` | `record + 0x190` | `0.8845216632` | `88.45%` |
| `217...` | `0x30002D01` | `record + 0x190` | `0.9251356721` | `92.51%` |

The `92%` value agrees with the supplied in-game screenshot after normal UI
rounding. The same relative field was also observed for the earlier supplied
Kharod saves at approximately `100%`, `97.85%` and `98.18%`. This makes
`record + 0x190` a strong read anchor for this current Kharod serialization
shape.

The adjacent Kharod field at `record + 0x0C4` changes from approximately
`1.1700317` to `0.22` in the new pair. It is not treated as durability: its
range and position do not match the UI condition, and it changes alongside
other state bytes.

#### Lavina

The Lavina record exposes a matching-looking condition position immediately
before its upgrade-key vector:

| Sample | Handle | Relative offset | Raw f32 | UI-equivalent |
|---|---|---:|---:|---:|
| `610... (1)` | `0x3000265C` | `record + 0x199` | `0.3941797018` | `39.42%` |
| `217...` | `0x3000265C` | `record + 0x199` | `0.3941797018` | `39.42%` |

This agrees with the supplied `39%` screenshot, but it is unchanged between
the two new files. It is therefore a corroborating read observation, not a
Lavina differential pair. A Lavina writer still needs a before/after pair or
game read-back using this shape.

The same primary-state shape is present for `Gun_SkifGun_HG`/`GunPM_HG` in the
wider corpus, including its two direct modules and three upgrade keys. It is
shown as experimental source-backed data, not as proof that every S2 weapon
serializer shares the same layout.

#### Saiga/D-12

The embedded 672-entry name table contains `GuardGunD12_SG`, `GunD12_SG`,
`GunD12_MagDefault`, `TopRailD12` and several D-12 upgrade names. The supplied
files do not expose a D-12/`Saiga` actor-owned inventory row in the current
parser result. Several D-12 object records occur in the raw payload, but their
handles are not in the actor-owned list and must not be shown as the user's
weapon. The `86%` screenshot therefore needs its corresponding full Data save
for a D-12 differential check.

### Observed weapon modules

The actual owned weapon records contain module references separate from the
upgrade-key vectors.

| Weapon | Direct state/module references observed |
|---|---|
| Skif pistol (`Gun_SkifGun_HG` / `GunPM_HG` state) | `GunPM_MagIncreased`, `RU_Silen_1` |
| Kharod | `GunKharod_MagDefault`, `HP_Laser_1`, `EN_Silen_3`, `EN_GoloScope_1`, `EN_GLaunch_1` |
| Lavina | `GunLavina_MagDefault`, `TopRailLavina`, `RU_Grip_1`, `RU_X2Scope_1`, `HP_Laser_2` |

The same records also contain duplicated weapon-specific upgrade-key vectors:
15 Kharod candidates and 16 Lavina candidates in the observed shapes. The
current bytes do not prove which vector entries are installed/current and
which are available/applicable. The product must therefore show the direct
module observations separately from upgrade availability and keep upgrade
mutation read-only until a one-upgrade controlled pair identifies all affected
references.

The module vocabulary is visibly weapon-specific: Kharod has suppressor,
optical sight, grenade-launcher and laser references in this sample, while
Lavina has its own rail, grip, X2 scope, laser and weapon-specific upgrade
families. The editor must not expose one global module list for every game or
weapon.

### Device and metadata boundary

`NVG_NPC_Gen3` appears as an actor/grid item in the supplied corpus with
`kind=4` and no condition anchor; it is classified as a read-only device.
`Binoculars_02`, `Binoculars_03` and
`NVG_NPC_Gen2` are present in save-local name metadata, but their presence in
the name table alone does not prove that the player owns or equips them. No
condition editor should be attached to these devices.

The changed-save round-trip was also run with the locally built native
`ooz_encoder` from the vendored pyooz source. Editing Kharod to `0.99` and
Lavina to `0.88` produced CRC-valid compact outputs of `6,331,286` bytes from
the `6,724,666`-byte source and `6,283,080` bytes from the `6,665,035`-byte
source. The raw values and modules survived decompression round-trip; the
original Downloads files were not modified. Without that encoder the editor
now refuses to create an inflated `CC06` fallback for a compressed source.

### Acceptance status

- S2 weapon condition: **experimental/source-backed generic writer** for every
  accepted actor-owned weapon row with the validated structural anchor; the
  current corpus exercises Kharod, Lavina and Skif, but the implementation has
  no weapon-name allow-list. It is not yet game-accepted.
- S2 Lavina condition: **research / corroborating read candidate** at
  `record + 0x199`; no differential pair in these two files.
- S2 D-12 condition: **not observed in an actor-owned row** in these files.
- S2 direct weapon modules: **read-only observed metadata**.
- S2 upgrade vectors: **read-only, installed-vs-available unresolved**.
- S2 PNV/binocular condition: **not applicable until a real owned/equipped
  device record proves otherwise**.

### Required next evidence

1. Edit a copy using only the Kharod candidate, load it in the game, save it
   again and compare the game-resaved condition.
2. Supply a Lavina before/after pair where only its condition changes, or a
   D-12 pair for the `86%` state.
3. Supply a pair with exactly one module/upgrade installation changed; record
   whether the game says the module is installed, available or blocked by a
   prerequisite upgrade.

Until game load/re-save evidence passes, code may parse and stage the
source-backed weapon condition mutation, but must not claim universal S2 weapon
repair or any S2 module/upgrade writing. Modules/upgrades remain read-only.

---

## Equipment support matrix — 2026-09-22

This is the release-scoped product matrix consumed by `editor.equipment` and
the Qt/web projections. It separates what can be displayed from what can be
written. `experimental` means the local codec/round-trip exists; it is not a
claim that the game accepted the edited save. `research` means observations
are displayed but the mutation is disabled. `unsupported` means no control is
offered.

| Release | Categories shown | Durability | Upgrades | Placement | Add/remove | Devices | Names/icons |
|---|---|---|---|---|---|---|---|
| S.T.A.L.K.E.R. 2 | weapon, armor, helmet, module, device, consumable, ammo, artifact, quest | experimental for source-backed weapon/armor anchors | research; installed vs available unresolved | unsupported | unsupported | NVG/binocular/detector are read-only; no fabricated condition | official loose CFG/localization; selected Zone Kit/Workshop is presentation-only |
| Shadow of Chernobyl | weapon, armor, module, device, consumable, ammo, artifact, quest | experimental | unsupported | experimental | experimental | no separate device writer | official X-Ray resources |
| Clear Sky | weapon, armor, module, device, consumable, ammo, artifact, quest | experimental | experimental | experimental | experimental | device/module semantics remain format-specific | official X-Ray resources |
| Call of Pripyat | weapon, armor, helmet, module, device, consumable, ammo, artifact, quest | experimental | experimental | experimental | experimental | device/module semantics remain format-specific | official X-Ray resources |
| Shadow of Chernobyl — Enhanced Edition | base X-Ray categories only, parser not accepted | unsupported | unsupported | unsupported | unsupported | unsupported | unavailable until an EE sample is accepted |
| Clear Sky — Enhanced Edition | base X-Ray categories only, parser not accepted | unsupported | unsupported | unsupported | unsupported | unsupported | unavailable until an EE sample is accepted |
| Call of Pripyat — Enhanced Edition | base X-Ray categories only, parser not accepted | unsupported | unsupported | unsupported | unsupported | unsupported | unavailable until an EE sample is accepted |

### Interpretation rules

- A catalog entry is metadata. It does not prove that the player owns the
  item. Owned rows come only from actor/grid/equipped observations.
- `module_states` and `upgrade_states` are `unknown` unless the save format
  proves installed/current/applicable state. S2 direct weapon references are
  shown separately from the duplicated upgrade vectors.
- NVG and binocular rows are devices. They have a placement/source field when
  the save provides one, but no durability editor. A name-table entry such as
  `Binoculars_*` or `NVG_Gen2` never creates an owned row.
- SoC's lack of confirmed upgrade editing is intentional and independent from
  Clear Sky/Call of Pripyat. Enhanced Edition profiles do not inherit an
  original-game parser or writer.
- S2 weapon condition uses one generic structural codec for every accepted
  actor-owned weapon row whose exact module/upgrade anchor is present; Kharod,
  Lavina and Skif are examples from the current corpus, not a hard-coded
  allow-list. It remains `experimental` until an edited copy is loaded and
  re-saved by the game. No module/upgrade mutation is enabled by this matrix.

The machine-readable source is `editor/equipment_matrix.py` (Python editor, archived);
the stable JSON projection is `EquipmentSupport.as_dict()` and
`EquipmentItem.as_dict()`.

---

## S.T.A.L.K.E.R. 2: the inventory layout of game 1.0.x (December 2024)

Fifteen of the owner's saves, written between 2 and 6 December 2024, unpack (Kraken, CRC) like current saves but were
refused: the wallet anchor of the current layout does not occur in them. What differs, found by comparing them with
current saves of the same playthrough:

| Part | Current layout | 1.0.x |
|---|---|---|
| Container id (12 bytes `CA CF A8 48 C8 95 21 49 B5 1B 94 44`) | once, after the list of sub-containers | once, before that list |
| Between the id and the wallet | `00 00 00 00`, `06 00 00 00 00 06 00 00` | u16 count and that many (handle `0x38……`, u32 1); `00 00 00 00`; one small number as u32 and again as u16 |
| Wallet, flag, owned handles | u32, u32, u16 count, handles | the same; emptied slots stay in the list as `FF FF FF FF` |
| Grid cell | handle, x u16, y u16 (8 bytes) | handle, x u8, y u8 (6 bytes) |
| Object record | handle, id, 3-byte type key, x u16, y u16, 3 bytes, `38`, count, … | handle, id, 2-byte name index, x u8, y u8, 3 bytes, `38`, count, … (every later field 3 bytes earlier) |
| Names | several tables, the key's first byte selects one | one table of ~35 000 names running to the last byte of the save; its first entry is `Player` |

Evidence that the reading is right:

- all 15 saves parse with no unresolved handle; every grid item gets a name; counts fit the items (`A939A` × 468,
  `ArmyMedkit` × 31, `Bandage` × 10);
- the x of every record equals the x of its grid cell;
- the last old save (6 December 2024: 85 433 RU; Varta dog tag, Skif's pistol, isolator keys, two electro-collars) and
  the first current-layout save of the same playthrough (9 September 2026) hold the same money and the same items;
- money follows the playthrough from save to save (343 846 → 360 727 → 369 727 → 280 718 → … → 85 433).

What is not known: where worn equipment, condition and upgrades sit in the old records. They are not shown.

Decision: such a save is **read only**. `Stalker2InventoryLayout.IsLegacy` marks it; every S2 writer refuses it and
the interface shows the reason. Nothing written in this layout was ever loaded by a game, and the current game
rewrites the save in the current layout when it saves again. Verification level: L2 (synthetic tests in
`Stalker2LegacyLayoutTests`) plus a read of the 15 real saves; not L5.

---

## X-Ray attachments и upgrades — 2026-09-16

Исследование ограничено официальными оригинальными релизами ТЧ, ЧН и ЗП.
Моды, личные файлы и байты сейвов в репозиторий не попадают. Результат этого
этапа — read-only анализ; mutating writer для attachments пока не открывается.

### Что подтверждено исходником движка

В зафиксированном [OpenXRay xray-16, `CSE_ALifeItemWeapon::STATE_Read`](https://raw.githubusercontent.com/OpenXRay/xray-16/c37860c09850d894b721ba115cd936bb3f11482c/src/xrServerEntities/xrServer_Objects_ALife_Items.cpp)
после базового `CSE_ALifeInventoryItem` состояния идут `a_current` (`u16`),
`a_elapsed` (`u16`) и `wpn_state` (`u8`). Поле `m_addon_flags.flags` читается
ровно одним байтом при `m_wVersion > 40`; затем при более новых версиях идут
`ammo_type` и упакованный счётчик гранат. Это даёт точный anchor только после
разбора всех предшествующих полей, а не по поиску похожего байта.

В [заголовке `CSE_ALifeItemWeapon`](https://raw.githubusercontent.com/OpenXRay/xray-16/c37860c09850d894b721ba115cd936bb3f11482c/src/xrServerEntities/xrServer_Objects_ALife_Items.h)
маски состояния такие:

| Слот | Mask | Значение в `m_addon_flags` |
|---|---:|---|
| scope | `0x01` | при установленном бите scope считается включённым |
| grenade launcher | `0x02` | при установленном бите подствольник считается включённым |
| silencer | `0x04` | при установленном бите глушитель считается включённым |

Остальные биты сохраняются как unknown. Редактор не должен очищать их при
будущей записи.

В [перечислении `EWeaponAddonStatus`](https://raw.githubusercontent.com/OpenXRay/xray-16/c37860c09850d894b721ba115cd936bb3f11482c/src/xrServerEntities/alife_space.h)
статусы конфигурации имеют другую семантику:

| Код | Статус | Правило |
|---:|---|---|
| `0` | disabled | слот отключён |
| `1` | permanent | аксессуар всегда установлен, бит не является переключателем |
| `2` | attachable | аксессуар можно подключать/отключать игровым действием |

`Weapon.cpp` читает `scope_status`, `silencer_status` и
`grenade_launcher_status`. Для attachable scope дополнительно используется
`scopes_sect`; для silencer и grenade launcher движок читает component section
из `silencer_name` и `grenade_launcher_name`. Поэтому effective state нельзя
получать из одного флага: permanent-слоты должны учитываться по конфигу, а
attachable — по state byte.

### Read-only реализация

`tools/analyze_attachments.py` (Python editor, archived) теперь:

- разбирает weapon STATE до точного addon-byte anchor с version boundaries;
- показывает `flag_attached_slots`, unknown bits и effective slots после
  применения release-конфига;
- читает официальные weapon LTX из loose `gamedata` либо проверенных
  `resources/*.db` архивов;
- связывает конкретный weapon key с addon statuses, component sections и
  `scopes_sect` options;
- сохраняет уже разобранный `m_upgrades` отдельно, не смешивая upgrades с
  attachments;
- ничего не пишет в сейв и не выводит личный путь в JSON-отчёт.

Пример локального запуска:

```text
python tools/analyze_attachments.py \
  --release stalker-cop \
  --game-root /path/to/Call\ of\ Pripyat \
  /path/to/save.scop
```

Если `gamedata` содержит очевидный community overlay, он пропускается, а
инструмент пытается взять только официальный packed config. На этой машине
так был исключён overlay ЧН; официальные config archives ЧН и ЗП проверялись
отдельно. Для ТЧ подходящего локального config archive нет, поэтому его
compatibility profile остаётся `Unknown`, а не заполняется догадками.

### Локальные наблюдения

Это агрегат read-only probe доступных личных сейвов, не универсальная
характеристика всех сохранений и не game acceptance:

| Релиз | Config profiles | Actor-owned weapons в проверочном сейве | Наблюдаемые флаги |
|---|---:|---:|---|
| ТЧ | 0 | 6 | все `0x00`, profile не найден |
| ЧН | 59 | 4 | все `0x00`; loose mod overlay исключён |
| ЗП | 54 | 6 | пять `0x00`, один `0x04` у `wpn_usp_nimble` |

В расширенном локальном corpus ЗП один и тот же `(handle, weapon key)` в
четырёх случаях встречается со значениями `0x00` и `0x01`. Это полезная
корроборация того, что byte меняется между состояниями, но файлы не являются
контролируемой серией attach/detach: нет зафиксированных трёх состояний,
известного игрового действия и независимого экземпляра с тем же
component/derived-stat diff. Поэтому это не открывает writer.

Отдельно проверено, что у actor-owned weapon records в выбранных сейвах нет
простого набора child objects, который можно было бы безопасно создавать или
удалять вместо state byte. В registry встречаются `wpn_addon_*` объекты с
другими parent edges, но это не доказывает их роль для каждой inventory
операции; orphan/parent совпадение не превращается в reference schema.

### Gate для следующей задачи

Кандидатная операция для будущей R10 должна быть typed и item-scoped:

```text
set_weapon_addons(
    handle,
    scope={enabled, component},
    silencer={enabled, component},
    grenade_launcher={enabled, component},
)
```

Перед записью понадобятся exact weapon profile, attachable status, component
compatibility, занятый slot и все derived/reference updates. Permanent и
disabled нельзя превращать в переключатель, unknown bits нужно сохранить.
Патч только видимого scalar без доказанного companion state запрещён.

Сейчас R09 даёт точный read-only codec и compatibility map, но controlled
attach/detach gate остаётся **BLOCKED**. До game load/re-save/read-back также не
доказано, что изменение конкретного addon в каждом оригинальном build
сохраняется после следующего игрового save. R10 поэтому остаётся закрытой.

Upgrades уже исследованы отдельно: [X-Ray upgrades evidence](XRAY_FORMAT.md).
Их `m_upgrades` vector нельзя смешивать с addon flags в одной неподтверждённой
операции.

---

> Черновик Gemini (2026-09-26), Claude не перепроверял. Утверждения без доказательства на реальных сейвах — гипотезы. Лицензии внешних дампов (например, `Trasiankus/stalker2-bel`) до использования в сборке проверить отдельно.

## Публичные источники официальных названий предметов S.T.A.L.K.E.R. 2 на других языках

Ресерч выполнен в рамках задачи **KB-6** (2026-09-25) для поддержки многоязычного каталога предметов S.T.A.L.K.E.R. 2: Heart of Chornobyl в редакторе сохранений.

Целевые языки игры: **uk, de, fr, pl, es, it, cs, ja, ko, zh (zh_CN, zh_TW), tr, pt-BR** (+ уже интегрированные **ru** и **en**).

---

### 1. Текущее состояние в проекте

На данный момент в web/s2_items.json (Python editor, archived) покрытие именами по языкам распределено неравномерно:
- **ru:** 1375 предметов (100% из русской таблицы локализации `Module:S2Localization` на Fandom);
- **en:** 349 предметов (ручная выверка + карточки инфобоксов en-вики);
- **uk:** 141 предмет (только интервики-ссылки `langlinks` со страниц en-вики);
- **pl:** 67 предметов (интервики-ссылки);
- **fr:** 25 предметов (интервики-ссылки);
- **de, es, it, cs, ja, ko, zh_CN, tr, pt_BR:** 0 предметов (на соответствующих языковых поддоменах Fandom отдельные статьи по большинству предметов S2 отсутствуют).

Интервики-ссылки MediaWiki не способны обеспечить полное покрытие для новых игр, так как фанатские сообщества на не-английских языках создают статьи медленно и фрагментарно. Необходимы прямые дампы оригинальных языковых таблиц игры.

---

### 2. Анализ источников

#### Источник A: GSC Zone Kit — `TextDatabase.json` (Эталонный официальный источник)

- **Ссылка / Путь:** `Stalker2/Content/TextToolBackup/TextDatabase.json` внутри официального дистрибутива Zone Kit.
- **Документация GSC:** [Mod TextTool Guide (PDF)](https://cdn.stalker2.com/guides/Mod_TextTool.pdf), стр. 1:
  > *«The Stalker2\\Content\\TextToolBackup\\TextDatabase.json file contains all of the vanilla localized texts and is read by Zone Kit at startup.»*
- **Поддерживаемые языки:** Все 15 официальных языков игры:
  - `en` (English)
  - `uk` (Ukrainian)
  - `ru` (Russian)
  - `de` (German)
  - `fr` (French)
  - `pl` (Polish)
  - `es` (Spanish - Spain)
  - `it` (Italian)
  - `cs` (Czech)
  - `ja` (Japanese)
  - `ko` (Korean)
  - `zh_CN` (Chinese Simplified)
  - `zh_TW` (Chinese Traditional)
  - `tr` (Turkish)
  - `pt_BR` (Portuguese - Brazil)
- **Покрытие:** 100% всех строк игры (включая оружие, броню, патроны, квестовые предметы, заметки, модификации).
- **Формат:** Чистый валидный UTF-8 JSON со структурой SID → язык → текст.
- **Лицензия и условия:** Zone Kit распространяется бесплатно разработчиками (GSC Game World) через Epic Games Store и Steam для моддинга S.T.A.L.K.E.R. 2. Использование названий предметов подпадает под добросовестное использование (presentation / catalog purposes) без нарушения проприетарного кода.
- **Как получить машинно:** Прямое чтение файла:
  ```python
  import json
  with open("TextDatabase.json", encoding="utf-8") as f:
      db = json.load(f)
  ```
- **Оценка:** Наилучший первоисточник для долгосрочной полной локализации.

---

#### Источник B: GitHub-репозиторий `Trasiankus/stalker2-bel` (`localization.json`)

- **Ссылка:** https://github.com/Trasiankus/stalker2-bel
- **Прямой URL данных:** https://raw.githubusercontent.com/Trasiankus/stalker2-bel/main/localization.json
- **Размер файла:** ~16.9 МБ.
- **Поддерживаемые языки:**
  - `ua` (`uk`) — 100% официальный украинский текст из ванильной игры;
  - `ru` — 100% официальный русский текст из ванильной игры;
  - `bel` — фанатский белорусский перевод.
- **Покрытие для украинского языка:** 100% (все ключи `sid_items_*_name`, `sid_questItemprototypes_*_name`, `sid_upgrades_*_name`, `sid_character_*`).
- **Формат:** JSON:
  ```json
  {
    "sid_items_GunTOZ_SG_name": {
      "ua": "ТОЗ-34",
      "ru": "ТОЗ-34",
      "bel": "ТОЗ-34"
    },
    "sid_items_Bread_name": {
      "ua": "Хліб",
      "ru": "Хлеб",
      "bel": "Хлеб"
    }
  }
  ```
- **Лицензия и условия:** Публичный открытый репозиторий на GitHub. Исходные тексты `ua` и `ru` взяты напрямую из игры и обновляются скриптом `make_mod.py`.
- **Как получить машинно:**
  ```python
  import urllib.request, json

  URL = "https://raw.githubusercontent.com/Trasiankus/stalker2-bel/main/localization.json"
  req = urllib.request.Request(URL, headers={"User-Agent": "Mozilla/5.0"})
  with urllib.request.urlopen(req, timeout=30) as resp:
      data = json.loads(resp.read().decode("utf-8"))
  # Ключи предметов: sid_items_<SID>_name -> data[key]["ua"]
  ```
- **Оценка:** Готовое немедленное решение для закрытия 100% украинской локализации (`uk`) без необходимости иметь установленный Zone Kit на машине сборки.

---

#### Источник C: Распаковка ванильного архива `LocalizationDB.ubulk` через FModel / S2HOC_LocEditor

- **Инструменты:**
  - **FModel:** https://github.com/4sval/FModel (просмотр и распаковка UE5-паков `.utoc`/`.ucas`);
  - **S2HOC Localization Editor:** доступен на AP-PRO и Nexus Mods;
  - **UnrealReZen:** https://github.com/rm-NoobInCoding/UnrealReZen.
- **Путь в архивах игры:** `Stalker2/Content/Localization/Game/LocalizationDB.ubulk` и `.uasset`.
- **Поддерживаемые языки:** Все 15 официальных языков игры.
- **Покрытие:** 100% ванильного текста текущей установленной версии игры.
- **Формат:** Конвертируется утилитой `S2HOC_LocEditor` в единый `LocalizationDB.json`.
- **Лицензия и условия:** Требуется наличие купленной установленной игры у разработчика для однократного экспорта.
- **Как получить машинно:** Экспорт через консольный вызов утилит распаковщика или использование готового сконвертированного JSON.
- **Оценка:** Полный аналог источника A, если Zone Kit не установлен, но установлена сама игра.

---

#### Источник D: MediaWiki API (Fandom S.T.A.L.K.E.R. Wikis)

- **Ссылки:**
  - Русская вики: `https://stalker.fandom.com/ru/api.php` (страницы `Module:S2Localization/data/ru/part1` .. `part5`);
  - Английская вики: `https://stalker.fandom.com/api.php` (`Infobox Item` с параметрами `hoc_code`, `code`);
  - Языковые поддомены: `/uk/`, `/pl/`, `/de/`, `/fr/`, `/es/`, `/cs/`, `/it/`.
- **Поддерживаемые языки:**
  - `ru`: 100% (уже интегрировано через парсинг модуля Lua);
  - `en`: ~25% через инфобоксы, остальное — дополняется ручной таблицей;
  - `uk`, `pl`, `fr`, `de`, `es`: крайне низкое (<5–10% предметов).
- **Лицензия:** Creative Commons Attribution-ShareAlike 3.0 (CC BY-SA 3.0).
- **Как получить машинно:** Запросы к MediaWiki API (`tools/build_s2_catalog.py`).
- **Оценка:** Для русского языка модуль Fandom идеален. Для остальных 13 языков этот источник не подходит из-за отсутствия статей по большинству предметов в региональных вики.

---

#### Источник E: Фанатские платформы моддинга (Nexus Mods / AP-PRO)

- **Проекты:**
  - *Merged Localization for S.T.A.L.K.E.R. 2* (Nexus Mods);
  - *S.T.A.L.K.E.R. 2 Pak Cfg Merge Tool* (GitHub / Nexus).
- **Специфика:** Моды локализации на Nexus в основном решают проблему конфликта модов (движок S2 читает только один активный файл локализации). Большинство модов представляют собой модифицированные дампы `LocalizationDB.json`.
- **Оценка:** Для редактора сохранений использовать фанатские модпаки не рекомендуется, так как они могут содержать изменённые или нестандартные названия. Нужны только чистые ванильные строки.

---

### 3. Сводная таблица источников

| Источник | Языки | Покрытие S2 | Формат | Машинный доступ | Лицензионная чистота |
|---|---|---|---|---|---|
| **Zone Kit (`TextDatabase.json`)** | Все 15 языков (uk, de, fr, pl, es, it, cs, ja, ko, zh_CN, zh_TW, tr, pt_BR) | **100%** | JSON | Прямой локальный `json.load()` | Официальный бесплатный SDK GSC |
| **`Trasiankus/stalker2-bel`** | `uk`, `ru` | **100%** | JSON | `curl` с GitHub без авторизации | Открытый репозиторий GitHub (дамп ваниллы) |
| **`LocalizationDB.ubulk` (FModel / LocEditor)** | Все 15 языков | **100%** | Двоичный `.ubulk` → JSON | Локальный скрипт распаковки | Ресурсы купленной игры |
| **Fandom MediaWiki (`Module:S2Localization`)** | `ru` | **100%** | Lua таблица | MediaWiki API | CC BY-SA 3.0 |
| **Fandom MediaWiki (`langlinks`)** | uk, pl, fr, de | **1–10%** (дыры) | Wikitext / JSON | MediaWiki API | CC BY-SA 3.0 |

---

### 4. Рекомендации для сборщика каталога (`tools/build_s2_catalog.py`)

1. **Немедленное улучшение для украинского языка (`uk`):**
   - Добавить в build_s2_catalog.py (Python editor, archived) подгрузку словаря из репозитория `Trasiankus/stalker2-bel/localization.json` (или сохранить выжимку `sid_*_name` в data/s2_sources.json (Python editor, archived)).
   - Это поднимет покрытие украинского языка с **141** до **1375+ предметов (100%)** без ручного труда.

2. **Стратегическое решение для всех 15 языков:**
   - Извлечь из Zone Kit (`Stalker2/Content/TextToolBackup/TextDatabase.json`) или распакованного `LocalizationDB.ubulk` только релевантные для инвентаря ключи (`sid_items_*_name`, `sid_questItemprototypes_*_name`, `sid_upgrades_*_name`);
   - Упаковать их в компактный файл `data/s2_item_names_multilang.json.gz` (~300–400 КБ);
   - Встроить обработку этого файла в build_s2_catalog.py (Python editor, archived). В результате интерфейс редактора на немецком, польском, французском, испанском, чешском, японском и других языках получит 100% аутентичные официальные имена GSC.
