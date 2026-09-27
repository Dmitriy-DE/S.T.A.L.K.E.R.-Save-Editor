#!/usr/bin/env python3
"""Generate the S2 stack writer byte-parity fixture from the Python editor."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import subprocess
import sys
from pathlib import Path


PYTHON_ORACLE_REVISION = "6f3839cb870161290ae1d404c40e291485c29e37"
STACK_HANDLE = 0x30000001
STACK_COUNT = 7
UNKNOWN_KIND_HANDLE = 0x30000004
STACK_COUNT_OFFSET = 19
STACK_WEIGHT_OFFSET = 24


def generate(python_repo: Path, output_dir: Path) -> int:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    revision = _revision(python_repo)
    if revision != PYTHON_ORACLE_REVISION:
        raise SystemExit(
            "Python save-format oracle revision mismatch: "
            f"expected {PYTHON_ORACLE_REVISION}, found {revision}"
        )
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
        stacks=((STACK_HANDLE, STACK_COUNT),),
    )
    prepared = prepare_edit(source, plan)
    expected = bytes(prepared.data)
    source_raw = sf.decompress_save(source)
    expected_raw = sf.decompress_save(expected)
    if source_raw == expected_raw:
        raise SystemExit("Python oracle did not change the requested S2 stack")
    source_items = {item.handle: item for item in sf.locate_inventory(source_raw)}
    source_item = source_items.get(STACK_HANDLE)
    if source_item is None or not source_item.editable_count:
        raise SystemExit("Python fixture no longer contains the expected editable stack")
    expected_items = {item.handle: item for item in sf.locate_inventory(expected_raw)}
    expected_item = expected_items.get(STACK_HANDLE)
    if expected_item is None or expected_item.count != STACK_COUNT:
        raise SystemExit("Python oracle failed the S2 stack-count round-trip")
    changed = {
        index
        for index, (before, after) in enumerate(zip(source_raw, expected_raw, strict=True))
        if before != after
    }
    allowed = set(
        range(
            source_item.record_offset + STACK_COUNT_OFFSET,
            source_item.record_offset + STACK_COUNT_OFFSET + 4,
        )
    )
    allowed.update(
        range(
            source_item.record_offset + STACK_WEIGHT_OFFSET,
            source_item.record_offset + STACK_WEIGHT_OFFSET + 4,
        )
    )
    if not changed or not changed <= allowed:
        raise SystemExit("Python oracle changed bytes outside the S2 stack count and weight fields")
    if sf.inspect_save(expected).money != sf.inspect_save(source).money:
        raise SystemExit("Python stack oracle unexpectedly changed S2 money")

    output_dir.mkdir(parents=True, exist_ok=True)
    source_name = "s2-stacks-source.sav"
    expected_name = "s2-stacks-expected.sav"
    raw_name = "s2-stacks-expected.raw"
    (output_dir / source_name).write_bytes(source)
    (output_dir / expected_name).write_bytes(expected)
    (output_dir / raw_name).write_bytes(expected_raw)

    capability = by_id("stalker2").capabilities.support("edit_stacks").maturity
    manifest = {
        "capability": capability,
        "count": STACK_COUNT,
        "expected": expected_name,
        "expectedRaw": raw_name,
        "expectedRawSha256": hashlib.sha256(expected_raw).hexdigest(),
        "expectedSha256": hashlib.sha256(expected).hexdigest(),
        "handle": STACK_HANDLE,
        "oracleRevision": revision,
        "source": source_name,
        "sourceSha256": source_sha256,
        "unknownKindHandle": UNKNOWN_KIND_HANDLE,
    }
    (output_dir / "s2-stacks-vectors.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"Generated Python S2 stack vector from {revision}")
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
        / "writer-s2-stacks",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
