#!/usr/bin/env python3
"""Generate synthetic X-Ray upgrades writer vectors from Python editor oracle."""

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
SOURCE_UPGRADES = ("up_a_wpn_test", "legacy_unknown")
TARGET_UPGRADES = ("legacy_unknown", "up_c_wpn_test")
CAPABILITIES = ("edit_money", "edit_stacks", "edit_upgrades", "add_items", "remove_items")


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

    upg_tests = runpy.run_path(str(python_repo / "tests" / "test_xray_upgrades.py"))
    upgrade_fixture = upg_tests["_upgrade_fixture"]
    _catalog = upg_tests["_catalog"]

    cases = (
        ("stalker-cs", CS_FORMAT, {"version": 124, "outer": 5}),
        ("stalker-cop", COP_FORMAT, {"version": 128, "outer": 6}),
        ("stalker-cs-ee", CS_EE_FORMAT, {"version": 128, "outer": 6, "alife": 54}),
        ("stalker-cop-ee", COP_EE_FORMAT, {"version": 128, "outer": 6, "alife": 54}),
    )

    output_dir.mkdir(parents=True, exist_ok=True)
    vectors: list[dict[str, object]] = []

    for release_id, spec, opts in cases:
        source = upgrade_fixture(spec=spec, version=opts["version"], upgrades=SOURCE_UPGRADES)
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

        if release_id.endswith("-ee"):
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
            source=SourceRef(kind="local", locator="synthetic-upgrades", sha256=sha256),
            upgrades=((HANDLE, TARGET_UPGRADES),),
        )
        catalog = _catalog(spec, "up_a_wpn_test", "up_c_wpn_test")
        prepared = prepare_xray(source, plan, spec, upgrade_catalog=catalog)
        expected = bytes(prepared.data)
        source_raw = XRayContainer.from_bytes(source).raw
        expected_raw = XRayContainer.from_bytes(expected).raw

        parsed = parse_xray(expected, spec)
        item = parsed.inventory[0]
        if item.upgrades != TARGET_UPGRADES:
            raise SystemExit(f"Python oracle failed upgrades round-trip for {release_id}")

        changed_offsets = [i for i, (b1, b2) in enumerate(zip(source_raw, expected_raw)) if b1 != b2]

        slug = release_id.removeprefix("stalker-")
        names = {
            "source": f"xray-upgrades-{slug}-source.sav",
            "expected": f"xray-upgrades-{slug}-expected.sav",
            "expectedRaw": f"xray-upgrades-{slug}-expected.raw",
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
                "sourceUpgrades": list(SOURCE_UPGRADES),
                "targetUpgrades": list(TARGET_UPGRADES),
                "changedRawOffsets": changed_offsets,
                "capabilities": {
                    name: python_format.capabilities.support(name).maturity
                    for name in CAPABILITIES
                },
            }
        )

    # Negative case 1: unknown / foreign upgrade key not present in catalog
    cop_source = vectors[1]["source"]
    cop_bytes = (output_dir / cop_source).read_bytes()
    cop_sha = hashlib.sha256(cop_bytes).hexdigest()
    foreign_catalog = _catalog(COP_FORMAT, "up_a_wpn_test")
    foreign_plan = EditPlan(
        source=SourceRef(kind="local", locator="synthetic", sha256=cop_sha),
        upgrades=((HANDLE, ("up_unknown_key",)),),
    )
    try:
        prepare_xray(cop_bytes, foreign_plan, COP_FORMAT, upgrade_catalog=foreign_catalog)
        raise SystemExit("Python oracle unexpectedly accepted unknown upgrade key")
    except XRaySaveError:
        pass

    # Negative case 2: SoC format does not support upgrades vector
    soc_fixture = save_tests["_fixture"](version=118, outer=3)
    soc_sha = hashlib.sha256(soc_fixture).hexdigest()
    soc_plan = EditPlan(
        source=SourceRef(kind="local", locator="synthetic", sha256=soc_sha),
        upgrades=((0x1234, ("up_test",)),),
    )
    try:
        prepare_xray(soc_fixture, soc_plan, SOC_FORMAT)
        raise SystemExit("Python oracle unexpectedly allowed upgrades on SoC")
    except XRaySaveError:
        pass
    (output_dir / "xray-upgrades-soc-unsupported-source.sav").write_bytes(soc_fixture)

    manifest = {
        "oracleRevision": _revision(python_repo),
        "vectors": vectors,
        "negativeCases": {
            "unregisteredUpgradeKey": {
                "handle": HANDLE,
                "invalidUpgrades": ["up_unknown_key"],
                "expectedError": "upgrade not in catalog",
            },
            "unsupportedReleaseSoc": {
                "fixture": "xray-upgrades-soc-unsupported-source.sav",
                "releaseId": "stalker-soc",
                "expectedError": "upgrades not supported",
            },
            "unknownItemHandle": {
                "invalidHandle": 0x9999,
                "expectedError": "unknown item",
            },
        },
    }
    (output_dir / "xray-upgrades-vectors.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(f"Generated {len(vectors)} synthetic X-Ray upgrades vectors from {_revision(python_repo)}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", required=True, type=Path)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "tests" / "Fixtures" / "writer-upgrades",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
