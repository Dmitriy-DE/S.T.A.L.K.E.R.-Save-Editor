"""Build the companion's NPC spawn list from an unpacked game config tree.

Usage: python3 tools/generate_companion_squads.py <configs-dir> <game>
  <configs-dir>  the game's configs/ folder unpacked from configs.db
  <game>         cop | cs | soc

Writes mods/companion/<game>/gamedata/scripts/save_editor_squads.script with ids
and string keys only; names are translated by the game at runtime.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HUMAN = {"stalker", "bandit", "dolg", "freedom", "army", "monolith", "killer", "csky", "ecolog", "zombied", "renegade"}
COMM_MAP = {"military": "army"}

SOC_MUTANTS = [
    "bloodsucker_weak", "bloodsucker_normal", "bloodsucker_strong",
    "boar_weak", "boar_normal", "boar_strong",
    "flesh_weak", "flesh_normal", "flesh_strong",
    "dog_weak", "dog_normal", "dog_strong",
    "pseudodog_weak", "pseudodog_normal", "pseudodog_strong",
    "psy_dog",
    "snork_weak", "snork_normal", "snork_strong",
    "gigant_normal", "gigant_strong",
    "m_controller_normal", "m_controller_old",
    "m_poltergeist_normal_tele", "m_poltergeist_normal_flame",
    "burer_weak", "m_burer_normal",
    "m_chimera_e",
    "tushkano_normal",
    "zombie_weak", "zombie_normal", "zombie_strong",
    "m_fracture_e",
    "m_cat_e",
]


def read(path: Path) -> str:
    return path.read_bytes().decode("cp1251", errors="replace").replace("\r", "")


def ltx_sections(text: str) -> dict[str, dict[str, str]]:
    sections: dict[str, dict[str, str]] = {}
    current = None
    for raw in text.splitlines():
        line = raw.split(";", 1)[0].strip()
        header = re.match(r"^\[([^\]]+)\]", line)
        if header:
            current = sections.setdefault(header.group(1).split(":")[0].strip(), {})
            continue
        if current is not None and "=" in line:
            key, value = line.split("=", 1)
            current[key.strip()] = value.strip()
    return sections


def names(value: str) -> list[str]:
    return [part.strip() for part in value.split(",") if part.strip()]


def generate_cop(configs: Path) -> tuple[list[str], list[str], list[str]]:
    squads: dict[str, dict[str, str]] = {}
    for path in sorted((configs / "misc").glob("squad_descr*.ltx")):
        squads.update(ltx_sections(read(path)))
    profiles: dict[str, str] = {}
    for path in sorted((configs / "creatures").glob("spawn_sections*.ltx")):
        for section, values in ltx_sections(read(path)).items():
            if "character_profile" in values:
                profiles[section] = values["character_profile"]
    classes = dict(re.findall(r'<character id="([^"]+)">\s*<class>([^<]+)</class>', read(configs / "gameplay/npc_profile.xml")))
    specific: dict[str, tuple[str, str]] = {}
    for path in sorted((configs / "gameplay").glob("character_desc_*.xml")):
        for block in re.findall(r"<specific_character\b.*?</specific_character>", read(path), re.S):
            cls = re.search(r"<class>([^<]+)</class>", block)
            name = re.search(r"<name>([^<]+)</name>", block)
            icon = re.search(r"<icon>([^<]+)</icon>", block)
            if cls and name:
                specific[cls.group(1).strip()] = (name.group(1).strip(), icon.group(1).strip() if icon else "")

    stalkers, mutants, characters = [], [], []
    mutant_by_npc: dict[str, str] = {}
    for sid, values in sorted(squads.items()):
        faction = values.get("faction", "").split()[0] if values.get("faction") else ""
        npc = names(values.get("npc", ""))
        pool = npc or names(values.get("npc_random", ""))
        if not faction or not pool:
            continue
        if faction.startswith("monster"):
            if re.match(r"^(jup|lx8|pas|pri|zat)_", pool[0]):
                continue  # quest-bound variant of a generic creature
            # one group per creature section; prefer simulation squads
            current = mutant_by_npc.get(pool[0])
            if current is None or ("sim" in sid and "sim" not in current):
                mutant_by_npc[pool[0]] = sid
        elif faction in HUMAN and "story_id" in values and len(npc) == 1:
            profile = profiles.get(npc[0], npc[0])
            name, icon = specific.get(classes.get(profile, profile), ("", ""))
            if name:
                characters.append(f'{{ id = "{sid}", name = "{name}", icon = "{icon}", faction = "{faction}" }}')
        elif faction in HUMAN and "story_id" not in values:
            stalkers.append(f'{{ id = "{sid}", faction = "{faction}", size = {len(npc) or 0} }}')

    for npc_section, sid in sorted(mutant_by_npc.items()):
        mutants.append(f'{{ id = "{sid}", npc = "{npc_section}" }}')

    return stalkers, mutants, characters


def generate_cs(configs: Path) -> tuple[list[str], list[str], list[str]]:
    squads: dict[str, dict[str, str]] = {}
    for path in sorted((configs / "misc").glob("squad_descr*.ltx")):
        squads.update(ltx_sections(read(path)))

    profiles: dict[str, tuple[str, str]] = {}
    for path in sorted((configs / "creatures").glob("spawn_sections*.ltx")):
        for section, values in ltx_sections(read(path)).items():
            if "character_profile" in values:
                profiles[section] = (values["character_profile"], values.get("community", ""))

    npc_prof = configs / "gameplay/npc_profile.xml"
    classes = dict(re.findall(r'<character id="([^"]+)">\s*<class>([^<]+)</class>', read(npc_prof))) if npc_prof.exists() else {}

    specific: dict[str, tuple[str, str, str]] = {}
    for path in sorted((configs / "gameplay").glob("character_desc_*.xml")):
        for block in re.findall(r"<specific_character\b.*?</specific_character>", read(path), re.S):
            m_id = re.search(r'id="([^"]+)"', block)
            cls = re.search(r"<class>([^<]+)</class>", block)
            name = re.search(r"<name>([^<]+)</name>", block)
            icon = re.search(r"<icon>([^<]+)</icon>", block)
            comm = re.search(r"<community>([^<]+)</community>", block)
            if name:
                n = name.group(1).strip()
                ic = icon.group(1).strip() if icon else ""
                c = comm.group(1).strip() if comm else ""
                if cls:
                    specific[cls.group(1).strip()] = (n, ic, c)
                if m_id:
                    specific[m_id.group(1).strip()] = (n, ic, c)

    stalkers, mutants, characters = [], [], []
    mutant_by_npc: dict[str, str] = {}
    for sid, values in sorted(squads.items()):
        faction = values.get("faction", "").split()[0] if values.get("faction") else ""
        npc = names(values.get("npc", ""))
        pool = npc or names(values.get("npc_random", ""))
        if not faction or not pool:
            continue
        if faction.startswith("monster"):
            current = mutant_by_npc.get(pool[0])
            if current is None or ("sim" in sid and "sim" not in current) or ("lair" in sid and "lair" not in current):
                mutant_by_npc[pool[0]] = sid
        elif faction in HUMAN and "story_id" not in values:
            stalkers.append(f'{{ id = "{sid}", faction = "{faction}", size = {len(npc) or 0} }}')

    for npc_section, sid in sorted(mutant_by_npc.items()):
        mutants.append(f'{{ id = "{sid}", npc = "{npc_section}" }}')

    seen_chars: set[str] = set()
    for sec, (prof, comm) in sorted(profiles.items()):
        if "sim_default" in prof or "arena_" in sec:
            continue
        info = specific.get(classes.get(prof, prof)) or specific.get(prof)
        if info and info[0] and not info[0].startswith("GENERATE_NAME"):
            name, icon, spec_comm = info
            faction = COMM_MAP.get(comm or spec_comm or "stalker", comm or spec_comm or "stalker")
            if sec not in seen_chars:
                seen_chars.add(sec)
                characters.append(f'{{ id = "{sec}", name = "{name}", icon = "{icon}", faction = "{faction}" }}')

    return stalkers, mutants, characters


def infer_soc_faction(sec: str, comm: str) -> str:
    if comm:
        return COMM_MAP.get(comm, comm)
    for f in ["dolg", "freedom", "bandit", "killer", "monolith", "zombied", "ecolog", "soldier", "specnaz", "stalker"]:
        if f in sec:
            if f in ("soldier", "specnaz"):
                return "army"
            return f
    return "stalker"


def generate_soc(configs: Path) -> tuple[list[str], list[str], list[str]]:
    spawn_secs = ltx_sections(read(configs / "creatures/spawn_sections.ltx"))
    stalkers = []
    for sec, vals in sorted(spawn_secs.items()):
        comm = infer_soc_faction(sec, vals.get("community", ""))
        stalkers.append(f'{{ id = "{sec}", faction = "{comm}", size = 1 }}')

    mutants = [f'{{ id = "{m}", npc = "{m}" }}' for m in SOC_MUTANTS]

    seen_chars: set[str] = set()
    characters = []
    for p in sorted((configs / "gameplay").glob("character_desc_*.xml")):
        for b in re.findall(r"<specific_character\b.*?</specific_character>", read(p), re.S):
            m_id = re.search(r'id="([^"]+)"', b)
            m_name = re.search(r"<name>([^<]+)</name>", b)
            m_icon = re.search(r"<icon>([^<]+)</icon>", b)
            m_comm = re.search(r"<community>([^<]+)</community>", b)
            if m_id and m_name and m_icon:
                cid = m_id.group(1).strip()
                name = m_name.group(1).strip()
                icon = m_icon.group(1).strip()
                comm = m_comm.group(1).strip() if m_comm else "stalker"
                comm = COMM_MAP.get(comm, comm)
                if not name.startswith("GENERATE_NAME") and icon and cid not in seen_chars:
                    seen_chars.add(cid)
                    characters.append(f'{{ id = "{cid}", name = "{name}", icon = "{icon}", faction = "{comm}" }}')

    return stalkers, mutants, characters


def main() -> None:
    configs, game = Path(sys.argv[1]), sys.argv[2]
    if game == "cop":
        stalkers, mutants, characters = generate_cop(configs)
    elif game == "cs":
        stalkers, mutants, characters = generate_cs(configs)
    elif game == "soc":
        stalkers, mutants, characters = generate_soc(configs)
    else:
        raise ValueError(f"Unknown game: {game} (expected cop, cs, soc)")

    target = ROOT / "mods/companion" / game / "gamedata/scripts/save_editor_squads.script"
    target.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "-- Generated by tools/generate_companion_squads.py from the game's squad descriptions.",
        "-- Do not edit by hand. Names are string keys the game translates.",
        "stalkers = {", *(f"\t{row}," for row in stalkers), "}",
        "mutants = {", *(f"\t{row}," for row in mutants), "}",
        "characters = {", *(f"\t{row}," for row in characters), "}",
    ]
    target.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{target.relative_to(ROOT)}: {len(stalkers)} squads, {len(mutants)} mutant groups, {len(characters)} characters")


if __name__ == "__main__":
    main()
