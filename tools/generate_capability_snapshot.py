#!/usr/bin/env python3
"""Generate the C# capability registry data from the read-only Python oracle."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys


SNAPSHOT_PROGRAM = r"""
import json
import subprocess
from editor.formats import formats

payload = {
    "oracle_revision": subprocess.check_output(
        ["git", "rev-parse", "HEAD"], text=True
    ).strip(),
    "formats": [
        {
            "id": format_.id,
            "release_id": format_.release_id,
            "edition": format_.edition,
            "capabilities": format_.capabilities.as_dict(),
        }
        for format_ in formats()
    ],
}
print(json.dumps(payload, ensure_ascii=False, sort_keys=True, indent=2))
"""


def generate_snapshot(python_repo: Path) -> str:
    if not (python_repo / "editor" / "formats.py").is_file():
        raise ValueError(f"Python oracle does not contain editor/formats.py: {python_repo}")

    environment = os.environ.copy()
    environment["PYTHONPATH"] = str(python_repo)
    environment["PYTHONDONTWRITEBYTECODE"] = "1"
    result = subprocess.run(
        [sys.executable, "-B", "-c", SNAPSHOT_PROGRAM],
        cwd=python_repo,
        env=environment,
        check=True,
        capture_output=True,
        text=True,
    )
    payload = json.loads(result.stdout)
    validate_snapshot(payload)
    return json.dumps(payload, ensure_ascii=False, sort_keys=True, indent=2) + "\n"


def validate_snapshot(payload: object) -> None:
    if not isinstance(payload, dict):
        raise ValueError("Python format registry snapshot must be an object")
    revision = payload.get("oracle_revision")
    if not isinstance(revision, str) or len(revision) != 40:
        raise ValueError("Python format registry snapshot has no full oracle revision")
    formats = payload.get("formats")
    if not isinstance(formats, list) or not formats:
        raise ValueError("Python format registry must return at least one format")

    format_ids: set[str] = set()
    required_mutations = {
        "edit_money",
        "edit_stacks",
        "move_items",
        "add_items",
        "remove_items",
        "edit_durability",
        "edit_upgrades",
        "edit_relations",
        "edit_player_faction",
        "edit_placement",
    }
    for item in formats:
        if not isinstance(item, dict):
            raise ValueError("Python format registry contains a non-object entry")
        format_id = item.get("id")
        if not isinstance(format_id, str) or not format_id:
            raise ValueError("Python format registry contains an empty format id")
        if format_id in format_ids:
            raise ValueError(f"Python format registry repeats {format_id!r}")
        format_ids.add(format_id)

        capabilities = item.get("capabilities")
        if not isinstance(capabilities, dict):
            raise ValueError(f"{format_id!r} has no capability object")
        mutation_support = capabilities.get("mutation_support")
        if not isinstance(mutation_support, dict):
            raise ValueError(f"{format_id!r} has no mutation support map")
        if set(mutation_support) != required_mutations:
            raise ValueError(f"{format_id!r} has an unexpected mutation capability set")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", required=True, type=Path)
    parser.add_argument(
        "--source-output",
        type=Path,
        default=Path("src/StalkerSaveEditor.Core/Capabilities/Data/capability-snapshot.json"),
    )
    parser.add_argument(
        "--golden-output",
        type=Path,
        default=Path("tests/golden/capabilities/capability-registry.json"),
    )
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parents[1]
    snapshot = generate_snapshot(args.python_repo.resolve())
    outputs = [args.source_output, args.golden_output]
    for output in outputs:
        destination = output if output.is_absolute() else repo_root / output
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(snapshot, encoding="utf-8", newline="\n")
        print(f"Wrote {destination}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
