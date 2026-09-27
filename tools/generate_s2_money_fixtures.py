#!/usr/bin/env python3
"""Generate the S2 money writer oracle vectors from the Python editor."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import subprocess
import sys
from pathlib import Path

MONEY = 876_543


def generate(python_repo: Path, output_dir: Path) -> int:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    sys.path.insert(0, str(python_repo))

    from editor.codec import load_encoder
    from editor.formats import by_id
    from editor.models import EditPlan, SourceRef
    from editor.prepare import prepare_edit
    import save_format as sf

    try:
        load_encoder()
    except Exception as exc:
        raise SystemExit(
            "The Python Kraken encoder is required for byte-for-byte S2 vectors. "
            "Build it with tools/build_ooz_encoder.py and add its output directory "
            "to PYTHONPATH before running this generator."
        ) from exc

    fixture_namespace = runpy.run_path(str(python_repo / "tests" / "conftest.py"))
    source = fixture_namespace["synthetic_save"].__wrapped__()
    source_sha256 = hashlib.sha256(source).hexdigest()
    plan = EditPlan(
        source=SourceRef(
            kind="local",
            locator="synthetic-fixture",
            sha256=source_sha256,
        ),
        money=MONEY,
    )
    prepared = prepare_edit(source, plan)
    expected = bytes(prepared.data)
    original_raw = sf.decompress_save(source)
    expected_raw = sf.decompress_save(expected)
    money_offset, _ = sf.locate_money(original_raw)
    changed = {
        index
        for index, (before, after) in enumerate(zip(original_raw, expected_raw, strict=True))
        if before != after
    }
    if not changed or not changed <= set(range(money_offset, money_offset + 4)):
        raise SystemExit("Python oracle changed bytes outside the S2 wallet field")
    if sf.inspect_save(expected).money != MONEY:
        raise SystemExit("Python oracle failed the S2 money round-trip")

    output_dir.mkdir(parents=True, exist_ok=True)
    source_name = "s2-money-source.sav"
    expected_name = "s2-money-expected.sav"
    raw_name = "s2-money-expected.raw"
    (output_dir / source_name).write_bytes(source)
    (output_dir / expected_name).write_bytes(expected)
    (output_dir / raw_name).write_bytes(expected_raw)

    capability = by_id("stalker2").capabilities.support("edit_money").maturity
    manifest = {
        "oracleRevision": _revision(python_repo),
        "source": source_name,
        "sourceSha256": source_sha256,
        "expected": expected_name,
        "expectedSha256": hashlib.sha256(expected).hexdigest(),
        "expectedRaw": raw_name,
        "expectedRawSha256": hashlib.sha256(expected_raw).hexdigest(),
        "money": MONEY,
        "moneyOffset": money_offset,
        "capability": capability,
    }
    (output_dir / "s2-money-vectors.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"Generated Python S2 money vector from {_revision(python_repo)}")
    return 0


def _revision(python_repo: Path) -> str:
    return subprocess.run(
        ["git", "-C", str(python_repo), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", required=True, type=Path)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parents[1]
        / "tests"
        / "Fixtures"
        / "writer-s2-money",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
