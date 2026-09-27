#!/usr/bin/env python3
"""Generate legacy draft-storage fixtures through the Python oracle."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path


def generate(python_repo: Path, output_directory: Path) -> Path:
    python_repo = python_repo.expanduser().resolve()
    output_directory = output_directory.expanduser().resolve()
    sys.path.insert(0, str(python_repo))

    from editor.drafts import DraftStore
    from editor.models import EditPlan, SourceRef

    source_bytes = b"synthetic Python draft legacy vector"
    source_sha256 = hashlib.sha256(source_bytes).hexdigest()
    source = SourceRef(
        kind="local",
        locator="synthetic-old-slot.sav",
        sha256=source_sha256,
    )
    plans = (
        EditPlan(source=source),
        EditPlan(source=source, money=450_000),
        EditPlan(
            source=source,
            money=900_000,
            stacks=((0x1234, 44),),
            detach=((0x2345, False),),
            adds=(("wpn_test", 2, "inventory"),),
        ),
    )

    output_directory.mkdir(parents=True, exist_ok=True)
    store = DraftStore(output_directory)
    store.save(source_sha256, plans, 2)
    generated_path = store.path_for(source_sha256)
    fixture_path = output_directory / "python-v1-draft.json"
    os.replace(generated_path, fixture_path)
    revision = subprocess.run(
        ["git", "-C", str(python_repo), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    (output_directory / "draft-vectors.json").write_text(
        json.dumps(
            {
                "fixture": fixture_path.name,
                "oracleRevision": revision,
                "sourceSha256": source_sha256,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    print(f"Python oracle revision: {revision}")
    print(f"source SHA256: {source_sha256}")
    print(f"wrote: {fixture_path}")
    return fixture_path


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", required=True, type=Path)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("tests/Fixtures/drafts"),
    )
    args = parser.parse_args()
    generate(args.python_repo, args.output_dir)


if __name__ == "__main__":
    main()
