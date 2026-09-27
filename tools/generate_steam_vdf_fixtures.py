#!/usr/bin/env python3
"""Generate Steam VDF parser fixtures from the read-only Python oracle."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import sys


VDF_TEXT = (
    '"libraryfolders" { "0" "C:\\\\Steam" '
    '"1" { "path" "D:\\\\Games\\\\SteamLibrary" "apps" { "41700" "1" } } }'
)
MALFORMED_TEXT = '"libraryfolders" { "0" "unterminated }'


def generate(python_repo: Path, fixture_dir: Path, golden_path: Path) -> None:
    python_repo = python_repo.expanduser().resolve()
    sys.path.insert(0, str(python_repo))

    from editor.steam_vdf import library_paths, parse_vdf

    parsed = parse_vdf(VDF_TEXT)
    libraries = library_paths(parsed.get("libraryfolders"))
    expected = {
        "oracle_revision": subprocess.run(
            ["git", "-C", str(python_repo), "rev-parse", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip(),
        "parsed": parsed,
        "library_paths": list(libraries),
    }
    try:
        parse_vdf(MALFORMED_TEXT)
    except ValueError as error:
        expected["malformed_error"] = str(error)
    else:
        raise RuntimeError("Python oracle accepted the malformed VDF fixture")

    fixture_dir.mkdir(parents=True, exist_ok=True)
    golden_path.parent.mkdir(parents=True, exist_ok=True)
    (fixture_dir / "libraryfolders.vdf").write_text(VDF_TEXT + "\n", encoding="utf-8")
    (fixture_dir / "unterminated.vdf").write_text(MALFORMED_TEXT + "\n", encoding="utf-8")
    golden_path.write_text(
        json.dumps(expected, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(f"Generated Steam VDF fixtures from {expected['oracle_revision']}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", type=Path, required=True)
    parser.add_argument(
        "--fixture-dir",
        type=Path,
        default=Path(__file__).parents[1] / "tests" / "Fixtures" / "steam-vdf",
    )
    parser.add_argument(
        "--golden-path",
        type=Path,
        default=Path(__file__).parents[1] / "tests" / "golden" / "steam-vdf" / "libraryfolders.json",
    )
    args = parser.parse_args()
    generate(args.python_repo, args.fixture_dir, args.golden_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
