#!/usr/bin/env python3
"""Generate synthetic X-Ray placement writer vectors from Python editor oracle."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import struct
import subprocess
import sys
from collections.abc import Callable
from pathlib import Path

HANDLE = 0x3456
TARGET_SLOT = 3
CAPABILITIES = ("edit_money", "edit_stacks", "edit_placement", "add_items", "remove_items")


def _revision(python_repo: Path) -> str:
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


def generate(python_repo: Path, output_dir: Path) -> int:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    sys.path.insert(0, str(python_repo))
    sys.path.insert(0, str(python_repo / "tests"))

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
        XRaySaveError,
        parse_xray,
        prepare_xray,
    )

    save_tests = runpy.run_path(str(python_repo / "tests" / "test_xray_save.py"))
    spawn = save_tests["_spawn"]
    object_record = save_tests["_object_record"]
    base_item_state = save_tests["_base_item_state"]
    lzo1x_compress = save_tests["lzo1x_compress"]

    dur_tests = runpy.run_path(str(python_repo / "tests" / "test_xray_durability.py"))
    condition_fixture = dur_tests["_condition_fixture"]

    # Initial client_place: 3 (ruck) | slot 2 | base_slot 3
    initial_place = 3 | (2 << 4) | (3 << 10)

    cases = (
        ("stalker-soc", SOC_FORMAT, {"version": 118, "outer": 3}),
        ("stalker-cs", CS_FORMAT, {"version": 124, "outer": 5}),
        ("stalker-cop", COP_FORMAT, {"version": 128, "outer": 6}),
        (
            "stalker-soc-ee",
            SOC_EE_FORMAT,
            {"version": 118, "outer": 3, "alife": 51},
        ),
        (
            "stalker-cs-ee",
            CS_EE_FORMAT,
            {"version": 128, "outer": 6, "alife": 54},
        ),
        (
            "stalker-cop-ee",
            COP_EE_FORMAT,
            {"version": 128, "outer": 6, "alife": 54},
        ),
    )

    output_dir.mkdir(parents=True, exist_ok=True)
    vectors: list[dict[str, object]] = []

    for release_id, spec, opts in cases:
        source = condition_fixture(
            version=opts["version"],
            outer=opts["outer"],
            name="wpn_test",
            condition=0.75,
            client_place=initial_place,
        )
        if opts.get("alife") is not None:
            c = XRayContainer.from_bytes(source)
            chunks = []
            for ch in c.chunks:
                payload = ch.data
                if ch.type == 0:
                    payload = struct.pack("<I", opts["alife"])
                chunks.append(ch.type.to_bytes(4, "little") + len(payload).to_bytes(4, "little") + payload)
            raw = b"".join(chunks)
            source = struct.pack("<III", 0xFFFFFFFF, opts["outer"], len(raw)) + lzo1x_compress(raw)

        if release_id.endswith("-ee") and release_id != "stalker-soc-ee":
            marker = "marsh" if release_id == "stalker-cs-ee" else "zaton"
            source = _add_registry_marker(
                source,
                marker=marker,
                version=opts["version"],
                spawn=spawn,
                object_record=object_record,
                base_item_state=base_item_state,
            )

        sha256 = hashlib.sha256(source).hexdigest()
        plan = EditPlan(
            source=SourceRef(kind="local", locator="synthetic-placement", sha256=sha256),
            placements=((HANDLE, "slot", TARGET_SLOT),),
        )
        prepared = prepare_xray(source, plan, spec)
        expected = bytes(prepared.data)
        source_raw = XRayContainer.from_bytes(source).raw
        expected_raw = XRayContainer.from_bytes(expected).raw

        parsed = parse_xray(expected, spec)
        item = parsed.inventory[0]
        if item.placement_type != "slot" or item.placement_slot != TARGET_SLOT:
            raise SystemExit(f"Python oracle failed placement round-trip for {release_id}")

        changed_offsets = [i for i, (b1, b2) in enumerate(zip(source_raw, expected_raw)) if b1 != b2]

        slug = release_id.removeprefix("stalker-")
        names = {
            "source": f"xray-placement-{slug}-source.sav",
            "expected": f"xray-placement-{slug}-expected.sav",
            "expectedRaw": f"xray-placement-{slug}-expected.raw",
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
                "sourcePlacement": {"type": "ruck", "slot": 2, "baseSlot": 3, "storage": "inventory"},
                "targetPlacement": {"type": "slot", "slot": TARGET_SLOT, "baseSlot": 3, "storage": "equipped"},
                "changedRawOffsets": changed_offsets,
                "capabilities": {
                    name: python_format.capabilities.support(name).maturity
                    for name in CAPABILITIES
                },
            }
        )

    # Negative case 1: weapon into helmet slot (slot 12)
    cop_source = vectors[2]["source"]
    cop_bytes = (output_dir / cop_source).read_bytes()
    cop_sha = hashlib.sha256(cop_bytes).hexdigest()
    invalid_slot_plan = EditPlan(
        source=SourceRef(kind="local", locator="synthetic", sha256=cop_sha),
        placements=((HANDLE, "slot", 12),),
    )
    try:
        prepare_xray(cop_bytes, invalid_slot_plan, COP_FORMAT)
        raise SystemExit("Python oracle unexpectedly accepted inappropriate slot 12 for weapon")
    except XRaySaveError:
        pass

    # Negative case 2: weapon moved to belt (belt is restricted to artifacts)
    invalid_belt_plan = EditPlan(
        source=SourceRef(kind="local", locator="synthetic", sha256=cop_sha),
        placements=((HANDLE, "belt", None),),
    )
    try:
        prepare_xray(cop_bytes, invalid_belt_plan, COP_FORMAT)
        raise SystemExit("Python oracle unexpectedly allowed placing weapon on belt")
    except XRaySaveError:
        pass

    # Negative case 3: slot out of range (slot 99)
    try:
        EditPlan(
            source=SourceRef(kind="local", locator="synthetic", sha256=cop_sha),
            placements=((HANDLE, "slot", 99),),
        )
        raise SystemExit("EditPlan unexpectedly allowed slot 99")
    except ValueError:
        pass

    # Negative case 4: unknown item handle
    missing_handle_plan = EditPlan(
        source=SourceRef(kind="local", locator="synthetic", sha256=cop_sha),
        placements=((0x9999, "slot", TARGET_SLOT),),
    )
    try:
        prepare_xray(cop_bytes, missing_handle_plan, COP_FORMAT)
        raise SystemExit("Python oracle unexpectedly allowed moving unknown item handle")
    except XRaySaveError:
        pass

    manifest = {
        "oracleRevision": _revision(python_repo),
        "vectors": vectors,
        "negativeCases": {
            "inappropriateSlot": {"handle": HANDLE, "destination": "slot", "invalidSlot": 12, "expectedError": "игра не кладёт этот предмет в слот 12"},
            "inappropriateBelt": {"handle": HANDLE, "destination": "belt", "expectedError": "игра не кладёт этот предмет на пояс"},
            "slotOutOfRange": {"handle": HANDLE, "invalidSlot": 99, "allowedRange": [1, 13]},
            "unknownItemHandle": {"invalidHandle": 0x9999, "expectedError": "unknown item"},
            "staleSourceSha": {"mismatchedSha": "0000000000000000000000000000000000000000000000000000000000000000"},
        },
    }
    (output_dir / "xray-placement-vectors.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(f"Generated {len(vectors)} synthetic X-Ray placement vectors from {_revision(python_repo)}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", required=True, type=Path)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "tests" / "Fixtures" / "writer-placement",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
