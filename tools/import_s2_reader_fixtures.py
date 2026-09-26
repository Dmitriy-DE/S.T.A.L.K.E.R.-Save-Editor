#!/usr/bin/env python3
"""Export the Python oracle's private-data-free S2 stash reader fixture."""

from __future__ import annotations

import argparse
import importlib.util
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def export_stash_fixture(python_repo: Path, output_dir: Path) -> Path:
    oracle = python_repo.expanduser().resolve()
    tests = oracle / "tests"
    sys.path.insert(0, str(oracle))
    sys.path.insert(0, str(tests))

    source = tests / "test_s2_stash.py"
    spec = importlib.util.spec_from_file_location("python_oracle_s2_stash", source)
    if spec is None or spec.loader is None:
        raise SystemExit(f"Cannot load Python oracle fixture source: {source.name}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    payload = module._save_with_stash()
    if not payload.startswith(b"SYNTHETIC-FIXTURE\x00"):
        raise SystemExit("Refusing to export non-synthetic S2 stash bytes")

    output_dir.mkdir(parents=True, exist_ok=True)
    target = output_dir / "synthetic-s2-stash.raw"
    target.write_bytes(payload)
    return target


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", type=Path, required=True)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=ROOT / "tests" / "Fixtures",
    )
    args = parser.parse_args()
    target = export_stash_fixture(args.python_repo, args.output_dir)
    print(f"Exported synthetic S2 stash fixture: {target.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
