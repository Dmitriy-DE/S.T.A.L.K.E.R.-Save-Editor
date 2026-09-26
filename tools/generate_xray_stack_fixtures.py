#!/usr/bin/env python3
"""Generate synthetic X-Ray stack writer vectors from the Python editor oracle."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import sys
from pathlib import Path

STACK_HANDLE = 0x1234
STACK_COUNT = 44
CAPABILITIES = ("edit_money", "edit_stacks", "add_items", "remove_items")


def generate(python_repo: Path, output_dir: Path) -> int:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    sys.path.insert(0, str(python_repo))

    xray_tests = runpy.run_path(str(python_repo / "tests" / "test_xray_save.py"))
    fixture = xray_tests["_fixture"]
    base_item_fixture = xray_tests["_fixture_with_base_item"]
    from editor.formats import by_id
    from editor.models import EditPlan, SourceRef
    from editor.xray_container import XRayContainer
    from editor.xray_save import (
        COP_EE_FORMAT,
        COP_FORMAT,
        CS_EE_FORMAT,
        CS_FORMAT,
        SOC_EE_FORMAT,
        SOC_FORMAT,
        parse_xray,
        prepare_xray,
    )

    cases = (
        ("stalker-soc", SOC_FORMAT, {"version": 118, "outer": 3, "registry": b"writer-soc"}),
        ("stalker-cs", CS_FORMAT, {"version": 124, "outer": 5, "registry": b"writer-cs"}),
        ("stalker-cop", COP_FORMAT, {"version": 128, "outer": 6, "registry": b"writer-cop"}),
        (
            "stalker-soc-ee",
            SOC_EE_FORMAT,
            {"version": 118, "outer": 3, "alife": 51, "registry": b"writer-soc-ee"},
        ),
        (
            "stalker-cs-ee",
            CS_EE_FORMAT,
            {
                "version": 128,
                "outer": 6,
                "alife": 54,
                "section": "ammo_marsh_writer",
                "registry": b"writer-cs-ee",
            },
        ),
        (
            "stalker-cop-ee",
            COP_EE_FORMAT,
            {
                "version": 128,
                "outer": 6,
                "alife": 54,
                "section": "ammo_zaton_writer",
                "registry": b"writer-cop-ee",
            },
        ),
    )

    output_dir.mkdir(parents=True, exist_ok=True)
    vectors: list[dict[str, object]] = []
    for release_id, spec, options in cases:
        source = fixture(**options)
        source_sha256 = hashlib.sha256(source).hexdigest()
        plan = EditPlan(
            source=SourceRef(kind="local", locator="synthetic-fixture", sha256=source_sha256),
            stacks=((STACK_HANDLE, STACK_COUNT),),
        )
        prepared = prepare_xray(source, plan, spec)
        expected = bytes(prepared.data)
        expected_raw = XRayContainer.from_bytes(expected).raw
        parsed = parse_xray(expected, spec)
        stack = parsed.object_by_id(STACK_HANDLE)
        if stack.count != STACK_COUNT or stack.update_count != STACK_COUNT:
            raise SystemExit(f"Python oracle failed the stack round-trip for {release_id}")

        slug = release_id.removeprefix("stalker-")
        source_name = f"xray-stack-{slug}-source.sav"
        expected_name = f"xray-stack-{slug}-expected.sav"
        raw_name = f"xray-stack-{slug}-expected.raw"
        (output_dir / source_name).write_bytes(source)
        (output_dir / expected_name).write_bytes(expected)
        (output_dir / raw_name).write_bytes(expected_raw)

        python_format = by_id(release_id)
        vectors.append(
            {
                "releaseId": release_id,
                "source": source_name,
                "sourceSha256": source_sha256,
                "expected": expected_name,
                "expectedSha256": hashlib.sha256(expected).hexdigest(),
                "expectedRaw": raw_name,
                "expectedRawSha256": hashlib.sha256(expected_raw).hexdigest(),
                "handle": STACK_HANDLE,
                "count": STACK_COUNT,
                "capabilities": {
                    name: python_format.capabilities.support(name).maturity
                    for name in CAPABILITIES
                },
            }
        )

    unknown_source = base_item_fixture()
    unknown_name = "xray-stack-unknown-kind-source.sav"
    (output_dir / unknown_name).write_bytes(unknown_source)

    s2 = by_id("stalker2")
    manifest = {
        "oracleRevision": _revision(python_repo),
        "vectors": vectors,
        "unknownKind": {
            "source": unknown_name,
            "sourceSha256": hashlib.sha256(unknown_source).hexdigest(),
        },
        "capabilities": {
            "stalker2": {
                name: s2.capabilities.support(name).maturity for name in CAPABILITIES
            }
        },
    }
    (output_dir / "xray-stack-vectors.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(f"Generated {len(vectors)} synthetic X-Ray stack vectors from {_revision(python_repo)}")
    return 0


def _revision(python_repo: Path) -> str:
    import subprocess

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
        default=Path(__file__).resolve().parents[1] / "tests" / "Fixtures" / "writer-stacks",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
