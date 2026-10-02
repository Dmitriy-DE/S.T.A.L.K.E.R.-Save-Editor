# S.T.A.L.K.E.R. 2: the inventory layout of game 1.0.x (December 2024)

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
