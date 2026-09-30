# Fix packs and third-party fixes — plan and tracker

Owner decision (2026-09-30): port **all** useful third-party fixes into the editor so players do not need
external mods. Users install and remove each fix or pack themselves; originals are backed up and restored.
Authors are credited in every entry (open-source project, attribution, no sale).

## Principles

- Every change is checked against the owner's installed retail/EE files: a fix installs only onto the exact
  original it was made for (SHA-256), otherwise nothing is written.
- Two levels:
  1. **Individual fixes** — exact text replacements, reviewed line by line, linked to crash signatures
     (Game Doctor) and quest rules (Save Doctor). Critical ones first.
  2. **Whole packs** — SRP / ZRP / … installed as whole-file overlays (one button), optional parts as separate
     packs off by default. A pack and an individual fix never manage the same file (engine refuses).
- Categories: **Critical** (crash, save corruption, quest can never finish) → **Desirable** (wrong quest
  behaviour, rewards, NPC logic) → **Optional, off by default** (balance, sound, "(Optional)" features) →
  **Not ported** (cosmetic renames, intro removal, taste changes).
- Enhanced Editions: a pack file applies to EE only where the EE original equals the retail original the pack
  was made for; the rest is ported by hand.

## Sources

| Game | Source | Where | Size | Status |
|---|---|---|---|---|
| CS | SRP 1.1.5 (Decane) | github.com/Decane/SRP | 506 changelog entries (319 fixes); 611 changed + 66 new files | diffed against retail; 25 individual fixes ported |
| SoC | ZRP 1.07 R5RC (NatVac) | metacognix.com/files/stlkrsoc | hundreds of fixes | downloaded, not diffed yet; 15 ported |
| CoP | stalker-cop-patch (victor-homyakov) | github | 13 items, 24 files | 18 ported |
| CoP | Pripyat Reclamation Patch | ModDB (files deleted) | — | 6 ported earlier; source mirror needed |
| CoP EE | UCoPEEP | Steam Workshop 3487808500 | 1 gameplay change | ported (#184) |
| SoC EE | Workshop bloodsucker village fix | Steam Workshop 3487970687 | 1 script | ported, EE only (#184) |

## Engine work (not planned now)

Retail: OpenXRay-based binary patches are possible per exe version. EE: closed 64-bit DX12 engine, patched by
GSC; binary patches break on updates and Workshop forbids .dll/.exe. Only when a concrete bug cannot be fixed
in scripts.

## Tasks

| # | Task | Status | PR |
|---|---|---|---|
| FP-1 | Engine: whole-file overlay operation + content store | done | #185 |
| FP-2 | Pack builder: diff a pack against vanilla installs → overlay definitions (retail + EE applicability) | todo | |
| FP-3 | Pack hosting on R2 + in-app download with SHA check (button, not bundled) | todo | |
| FP-4 | UI: packs on the Game Fixes screen (install/remove whole pack, size, credits) | todo | |
| FP-5 | SRP 1.1.5 full pack (retail CS) | todo | |
| FP-6 | SRP optional features as separate packs (off by default) | todo | |
| FP-7 | ZRP 1.07 full pack (retail SoC) | todo | |
| FP-8 | Critical SRP fixes as individual fixes (45: crashes, save corruption, stuck quests) + crash signatures | todo | |
| FP-9 | Critical ZRP fixes as individual fixes + signatures | todo | |
| FP-10 | EE applicability: per pack, port what matches EE originals | todo | |
| FP-11 | PRP source mirror; remaining CoP fixes | todo | |
| FP-12 | Steam Workshop package of the companion (.pack writer + upload after owner OK) | todo | |

## Change log

- 2026-09-30 — plan written; UCoPEEP and Workshop bloodsucker fix ported (#184); overlay engine (#185).
