#!/usr/bin/env python3
"""Generate synthetic X-Ray stash vectors from the read-only Python oracle."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import struct
import subprocess
import sys
from pathlib import Path

CASES = (
    ("stalker-soc", 118, 3, None, "esc_actor_stash"),
    ("stalker-cs", 124, 5, None, "mar_actor_stash"),
    ("stalker-cop", 128, 6, None, "zat_actor_stash"),
    ("stalker-soc-ee", 118, 3, 51, "esc_actor_stash"),
    ("stalker-cs-ee", 128, 6, 54, "mar_actor_stash"),
    ("stalker-cop-ee", 128, 6, 54, "zat_actor_stash"),
)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def generate(python_repo: Path, output_dir: Path) -> int:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    sys.path.insert(0, str(python_repo))
    sys.path.insert(0, str(python_repo / "tests"))

    from editor.xray_container import XRayContainer, lzo1x_compress
    from editor.xray_save import (
        COP_EE_FORMAT,
        COP_FORMAT,
        CS_EE_FORMAT,
        CS_FORMAT,
        SOC_EE_FORMAT,
        SOC_FORMAT,
        parse_xray,
        take_from_stash,
        xray_stashes,
    )

    specs = {
        "stalker-soc": SOC_FORMAT,
        "stalker-cs": CS_FORMAT,
        "stalker-cop": COP_FORMAT,
        "stalker-soc-ee": SOC_EE_FORMAT,
        "stalker-cs-ee": CS_EE_FORMAT,
        "stalker-cop-ee": COP_EE_FORMAT,
    }

    helpers = runpy.run_path(str(python_repo / "tests" / "test_xray_save.py"))
    state_base = helpers["_state_base"]
    base_item_state = helpers["_base_item_state"]
    spawn = helpers["_spawn"]
    object_record = helpers["_object_record"]
    chunk = helpers["_chunk"]

    oracle_revision = subprocess.run(
        ["git", "-C", str(python_repo), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    output_dir.mkdir(parents=True, exist_ok=True)
    vectors: list[dict[str, object]] = []

    for release_id, version, outer, alife, stash_name in CASES:
        actor_update = struct.pack("<H", 0) + b"\x00"
        item_update = struct.pack("<H", 0) + b"\x00"
        actor = spawn(
            "actor",
            0,
            0xFFFF,
            version,
            state_base(version, money=1234),
            actor_update,
        )
        box = spawn(
            "inventory_box",
            0x0010,
            0xFFFF,
            version,
            base_item_state(version),
            item_update,
            name_replace=stash_name,
        )
        empty_box = spawn(
            "inventory_box",
            0x0011,
            0xFFFF,
            version,
            base_item_state(version),
            item_update,
            name_replace="unknown_empty_stash",
        )
        stash_item_name = {
            "stalker-cs-ee": "bandage_marsh",
            "stalker-cop-ee": "bandage_zaton",
        }.get(release_id, "bandage_stash")
        stash_item_state = base_item_state(version)
        if version > 123:
            stash_item_state = (
                stash_item_state[:-4]
                + struct.pack("<I", 1)
                + b"upg_stash_test\x00"
            )
        backpack_item_state = base_item_state(version)
        if version > 123:
            backpack_item_state = (
                backpack_item_state[:-4]
                + struct.pack("<I", 1)
                + b"upg_stash_test\x00"
            )
        stash_item = spawn(
            stash_item_name,
            0x2345,
            0x0010,
            version,
            stash_item_state,
            item_update,
        )
        if release_id in {"stalker-cs", "stalker-cs-ee"}:
            backpack_client_data = b"\x02\x03\x00"
        else:
            backpack_client_data = b"\x02" + struct.pack("<H", 3)
        actor_item = spawn(
            "bandage_backpack",
            0x3456,
            0,
            version,
            backpack_item_state,
            item_update,
            backpack_client_data,
        )
        objects = struct.pack("<I", 5) + b"".join(
            object_record(value, item_update)
            for value in (actor, box, empty_box, stash_item, actor_item)
        )
        raw = b"".join(
            (
                chunk(0, struct.pack("<I", outer if alife is None else alife)),
                chunk(5, struct.pack("<Qff", 123456, 10.0, 1.0)),
                chunk(1, b"\x00" * 8),
                chunk(2, objects),
                chunk(9, b"registry"),
            )
        )
        source = struct.pack("<III", 0xFFFFFFFF, outer, len(raw)) + lzo1x_compress(raw)
        spec = specs[release_id]
        parsed = parse_xray(source, spec, with_inventory=True)
        stashes = xray_stashes(parsed)
        if len(stashes) != 1:
            raise SystemExit(f"Python oracle found {len(stashes)} stashes for {release_id}")
        stash = stashes[0]
        if (stash.object_id, stash.name, [item.object_id for item in stash.items]) != (
            0x0010,
            stash_name,
            [0x2345],
        ):
            raise SystemExit(f"Python oracle produced unexpected stash contents for {release_id}")

        expected_take = take_from_stash(source, spec, 0x2345)
        expected_raw = XRayContainer.from_bytes(expected_take).raw
        moved = parse_xray(expected_take, spec, with_inventory=True).object_by_id(0x2345)
        if moved.parent_id != parsed.actor_id:
            raise SystemExit(f"Python oracle failed to move the stash item for {release_id}")
        if xray_stashes(parse_xray(expected_take, spec, with_inventory=True)):
            raise SystemExit(f"Python oracle left a non-empty stash after take for {release_id}")

        slug = release_id.removeprefix("stalker-")
        names = {
            "source": f"xray-stash-{slug}-source.sav",
            "expectedTake": f"xray-stash-{slug}-take.sav",
            "expectedTakeRaw": f"xray-stash-{slug}-take.raw",
        }
        (output_dir / names["source"]).write_bytes(source)
        (output_dir / names["expectedTake"]).write_bytes(expected_take)
        (output_dir / names["expectedTakeRaw"]).write_bytes(expected_raw)
        vectors.append(
            {
                "releaseId": release_id,
                **names,
                "sourceSha256": sha256(source),
                "expectedTakeSha256": sha256(expected_take),
                "expectedTakeRawSha256": sha256(expected_raw),
                "actorId": parsed.actor_id,
                "boxId": stash.object_id,
                "boxName": stash.name,
                "level": stash.level,
                "stashItemName": stash.items[0].name,
                "stashItemUpgrades": ["upg_stash_test"] if version > 123 else [],
                "stashItemId": 0x2345,
                "backpackItemId": 0x3456,
            }
        )

    (output_dir / "xray-stash-vectors.json").write_text(
        json.dumps(
            {"oracleRevision": oracle_revision, "vectors": vectors},
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    return len(vectors)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--python-repo", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    print(generate(args.python_repo, args.output_dir))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
