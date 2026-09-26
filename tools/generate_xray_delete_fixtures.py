#!/usr/bin/env python3
"""Generate synthetic X-Ray delete writer vectors from the Python oracle."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import sys
from collections.abc import Callable
from pathlib import Path

HANDLE = 0x1234
CAPABILITIES = ("edit_money", "edit_stacks", "add_items", "remove_items")


def generate(python_repo: Path, output_dir: Path) -> int:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    sys.path.insert(0, str(python_repo))
    sys.path.insert(0, str(python_repo / "tests"))

    from editor.formats import by_id
    from editor.models import EditPlan, SourceRef
    from editor.xray_container import XRayContainer
    from editor.xray_delete import analyze_xray_delete
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

    save_tests = runpy.run_path(str(python_repo / "tests" / "test_xray_save.py"))
    fixture = save_tests["_fixture"]
    spawn = save_tests["_spawn"]
    object_record = save_tests["_object_record"]
    base_item_state = save_tests["_base_item_state"]
    durability_tests = runpy.run_path(str(python_repo / "tests" / "test_xray_durability.py"))
    condition_fixture = durability_tests["_condition_fixture"]
    delete_tests = runpy.run_path(str(python_repo / "tests" / "test_xray_delete.py"))
    dependent_fixture = delete_tests["_fixture_with_dependent_object"]

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
        if release_id.endswith("-ee") and release_id != "stalker-soc-ee":
            marker = "marsh" if release_id == "stalker-cs-ee" else "zaton"
            source = _add_registry_marker(
                source,
                marker=marker,
                version=int(options["version"]),
                spawn=spawn,
                object_record=object_record,
                base_item_state=base_item_state,
            )
        sha256 = hashlib.sha256(source).hexdigest()
        plan = EditPlan(
            source=SourceRef(kind="local", locator="synthetic-fixture", sha256=sha256),
            detach=((HANDLE, True),),
        )
        prepared = prepare_xray(source, plan, spec)
        expected = bytes(prepared.data)
        expected_raw = XRayContainer.from_bytes(expected).raw
        parsed = parse_xray(expected, spec)
        if any(item.handle == HANDLE for item in parsed.inventory):
            raise SystemExit(f"Python oracle failed the deletion round-trip for {release_id}")

        slug = release_id.removeprefix("stalker-")
        names = {
            "source": f"xray-delete-{slug}-source.sav",
            "expected": f"xray-delete-{slug}-expected.sav",
            "expectedRaw": f"xray-delete-{slug}-expected.raw",
        }
        (output_dir / names["source"]).write_bytes(source)
        (output_dir / names["expected"]).write_bytes(expected)
        (output_dir / names["expectedRaw"]).write_bytes(expected_raw)
        python_format = by_id(release_id)
        vectors.append(
            {
                "releaseId": release_id,
                **names,
                "sourceSha256": sha256,
                "expectedSha256": hashlib.sha256(expected).hexdigest(),
                "expectedRawSha256": hashlib.sha256(expected_raw).hexdigest(),
                "handle": HANDLE,
                "capabilities": {
                    name: python_format.capabilities.support(name).maturity
                    for name in CAPABILITIES
                },
            }
        )

    equipped = condition_fixture(
        version=128,
        outer=6,
        name="wpn_delete_equipped",
        client_place=1 | (1 << 4) | (1 << 10),
    )
    equipped_parsed = parse_xray(equipped, COP_FORMAT)
    if analyze_xray_delete(equipped_parsed, 0x3456).allowed:
        raise SystemExit("Python oracle unexpectedly allowed deleting an equipped item")
    (output_dir / "xray-delete-equipped-source.sav").write_bytes(equipped)

    dependent = dependent_fixture()
    dependent_parsed = parse_xray(dependent, COP_FORMAT)
    if analyze_xray_delete(dependent_parsed, 0x3456).allowed:
        raise SystemExit("Python oracle unexpectedly allowed deleting an item with a child")
    (output_dir / "xray-delete-dependent-source.sav").write_bytes(dependent)

    cap = by_id("stalker2")
    manifest = {
        "oracleRevision": _revision(python_repo),
        "vectors": vectors,
        "negativeFixtures": {
            "equipped": "xray-delete-equipped-source.sav",
            "dependent": "xray-delete-dependent-source.sav",
        },
        "capabilities": {
            "stalker2": {
                name: cap.capabilities.support(name).maturity for name in CAPABILITIES
            }
        },
    }
    (output_dir / "xray-delete-vectors.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(f"Generated {len(vectors)} synthetic X-Ray delete vectors from {_revision(python_repo)}")
    return 0


def _revision(python_repo: Path) -> str:
    import subprocess

    return subprocess.run(
        ["git", "-C", str(python_repo), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _add_registry_marker(
    source: bytes,
    *,
    marker: str,
    version: int,
    spawn: Callable[..., bytes],
    object_record: Callable[[bytes, bytes], bytes],
    base_item_state: Callable[[int], bytes],
) -> bytes:
    from editor.xray_container import XRayContainer

    container = XRayContainer.from_bytes(source)
    chunks: list[bytes] = []
    for chunk in container.chunks:
        payload = chunk.data
        if chunk.type == 2:
            count = int.from_bytes(payload[:4], "little")
            update = b"\x00\x00\x00"
            guard = spawn(
                f"{marker}_writer_guard",
                0x7777,
                0,
                version,
                base_item_state(version),
                update,
            )
            payload = (count + 1).to_bytes(4, "little") + payload[4:] + object_record(guard, update)
        chunks.append(chunk.type.to_bytes(4, "little") + len(payload).to_bytes(4, "little") + payload)
    return container.build(b"".join(chunks))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", required=True, type=Path)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "tests" / "Fixtures" / "writer-delete",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
