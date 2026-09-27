"""Check that every xml node a companion UI script asks for exists.

The game aborts with "XML node not found" when a script calls
xml:Init*("form:name") for a node the xml lacks, so catch it before install.
Usage: python3 tools/check_companion_ui.py
"""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "mods/companion"
PAIRS = [("cop/gamedata/scripts/save_editor_companion_ui.script", "cop/gamedata/configs/ui/ui_save_editor_companion.xml")]


def has(root: ET.Element, path: str) -> bool:
    node = root
    for part in path.split(":"):
        node = node.find(part)
        if node is None:
            return False
    return True


def names_in_loops(script: str) -> dict[str, list[str]]:
    """Resolve `"form:prefix" .. var` against the literal lists the loops use."""

    lists: dict[str, list[str]] = {}
    for name, body in re.findall(r"local (\w+) = \{([^{}]*)\}", script):
        lists[name] = re.findall(r'"([a-z_]+)"', body)
    return lists


def main() -> int:
    failures = 0
    for script_rel, xml_rel in PAIRS:
        script = (ROOT / script_rel).read_text(encoding="utf-8")
        text = (ROOT / xml_rel).read_text(encoding="utf-8")
        root = ET.fromstring(text.split("?>", 1)[1] if text.startswith("<?xml") else text)
        paths = set(re.findall(r'Init\w+\("([a-z_:]+)"(?! \.\.)', script))
        lists = names_in_loops(script)
        for prefix, var in re.findall(r'Init\w+\("([a-z_:]+)" \.\. (\w+)', script):
            loop = re.search(r"for _, " + var + r" in ipairs\((\w+)\)", script)
            inline = re.search(r"for _, " + var + r' in ipairs\(\{([^}]*)\}\)', script)
            values = lists.get(loop.group(1), []) if loop else re.findall(r'"([a-z_]+)"', inline.group(1)) if inline else []
            if not values:
                print(f"UNRESOLVED {prefix} .. {var} in {script_rel}")
                failures += 1
            paths.update(prefix + v for v in values)
        for path in sorted(paths):
            if not has(root, path):
                print(f"MISSING {path} ({xml_rel})")
                failures += 1
    print("ok" if failures == 0 else f"{failures} problem(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
