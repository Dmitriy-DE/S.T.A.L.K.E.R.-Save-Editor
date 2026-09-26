#!/usr/bin/env python3
"""Copy the Python editor's generated, metadata-only X-Ray catalogs into Core."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ASSET_NAMES = ("catalogs.json", "catalog_names.json")


def import_assets(python_repo: Path, output_dir: Path) -> None:
    source_root = python_repo.expanduser().resolve() / "web"
    output_dir.mkdir(parents=True, exist_ok=True)
    for name in ASSET_NAMES:
        source = source_root / name
        if not source.is_file():
            raise SystemExit(f"Python oracle is missing web/{name}")
        payload = source.read_bytes()
        document = json.loads(payload)
        if not isinstance(document, dict):
            raise SystemExit(f"Python oracle web/{name} is not a JSON object")
        if name == "catalogs.json" and document.get("schema_version") != 1:
            raise SystemExit("Unsupported Python catalog bundle schema")
        (output_dir / name).write_bytes(payload)
    print(f"Imported {len(ASSET_NAMES)} generated catalog assets into {output_dir}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", type=Path, required=True)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=ROOT / "src" / "StalkerSaveEditor.Core" / "Catalogs" / "Data",
    )
    args = parser.parse_args()
    import_assets(args.python_repo, args.output_dir)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
