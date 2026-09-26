#!/usr/bin/env python3
"""Generate synthetic X-Ray add-writer vectors from the Python oracle."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import struct
import sys
from pathlib import Path

ITEM_KEY = "exo_outfit"
TEMPLATE_ID = 0x2345
CAPABILITIES = ("edit_money", "edit_stacks", "add_items", "remove_items")


def generate(python_repo: Path, output_dir: Path) -> int:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    sys.path.insert(0, str(python_repo))
    sys.path.insert(0, str(python_repo / "tests"))

    from editor.catalog import ItemCatalog, ItemDefinition
    from editor.formats import by_id
    from editor.models import EditPlan, SourceRef
    from editor.xray_container import XRayContainer, lzo1x_compress
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

    helpers = runpy.run_path(str(python_repo / "tests" / "test_xray_save.py"))
    state_base = helpers["_state_base"]
    base_item_state = helpers["_base_item_state"]
    item_state = helpers["_item_state"]
    synthetic_ammo_fixture = helpers["_fixture"]
    spawn = helpers["_spawn"]
    object_record = helpers["_object_record"]
    chunk = helpers["_chunk"]

    cases = (
        ("stalker-soc", SOC_FORMAT, 118, 3, None, "outfit_soc_template", "u8", ()),
        ("stalker-cs", CS_FORMAT, 124, 5, None, "outfit_cs_template", "u8", ("upg_exo_a", "upg_exo_b")),
        ("stalker-cop", COP_FORMAT, 128, 6, None, "outfit_cop_template", "u16", ("upg_exo_a", "upg_exo_b")),
        ("stalker-soc-ee", SOC_EE_FORMAT, 118, 3, 51, "outfit_soc_ee_template", "u8", ()),
        ("stalker-cs-ee", CS_EE_FORMAT, 128, 6, 54, "outfit_marsh_template", "u8", ("upg_exo_a",)),
        ("stalker-cop-ee", COP_EE_FORMAT, 128, 6, 54, "outfit_zaton_template", "u16", ("upg_exo_a",)),
    )

    output_dir.mkdir(parents=True, exist_ok=True)
    vectors: list[dict[str, object]] = []
    outfit_sources: dict[str, bytes] = {}
    for release_id, spec, version, outer, alife, template_name, place_encoding, upgrades in cases:
        state = bytearray(base_item_state(version))
        if version > 123:
            encoded_upgrades = b"".join(value.encode("utf-8") + b"\x00" for value in upgrades)
            state = state[:-4] + struct.pack("<I", len(upgrades)) + encoded_upgrades

        if place_encoding == "u8":
            client_data = b"\x02\x01\x00\x00\x80\x3f\x00"
            source_place = 1
            expected_place = 3
        else:
            source_place = 1 | (1 << 4) | (1 << 10)
            expected_place = (source_place & 0xFFF0) | 3
            client_data = struct.pack("<BH", 2, source_place) + b"\x00\x00\x80\x3f\x00"

        actor = spawn(
            "actor",
            0,
            0xFFFF,
            version,
            state_base(version, money=1234),
            struct.pack("<H", 0),
        )
        update = struct.pack("<H", 0) + b"\x00"
        template = spawn(
            template_name,
            TEMPLATE_ID,
            0,
            version,
            bytes(state),
            update,
            client_data,
        )
        objects = struct.pack("<I", 2) + object_record(actor, struct.pack("<H", 0)) + object_record(template, update)
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
        outfit_sources[release_id] = source
        source_sha256 = hashlib.sha256(source).hexdigest()
        catalog = ItemCatalog(
            release_id,
            None,
            (
                ItemDefinition(
                    key=ITEM_KEY,
                    display_name="Synthetic outfit",
                    category="outfit",
                    unit_weight=1.0,
                    width=2,
                    height=2,
                    max_stack=None,
                    slots=("outfit",),
                    prototype=None,
                    source="synthetic-fixture",
                    class_name="II_OUTFIT",
                    serialization_family="outfit",
                ),
            ),
        )
        plan = EditPlan(
            source=SourceRef(kind="local", locator="synthetic-fixture", sha256=source_sha256),
            adds=((ITEM_KEY, 1, "inventory"),),
        )
        prepared = prepare_xray(source, plan, spec, catalog=catalog)
        expected = bytes(prepared.data)
        expected_raw = XRayContainer.from_bytes(expected).raw
        before = parse_xray(source, spec, with_inventory=True)
        after = parse_xray(expected, spec, with_inventory=True)
        added = tuple(
            obj
            for obj in after.objects
            if obj.object_id not in {existing.object_id for existing in before.objects}
        )
        if len(added) != 1 or added[0].name != ITEM_KEY or added[0].parent_id != before.actor_id:
            raise SystemExit(f"Python oracle failed the add round-trip for {release_id}")
        if place_encoding != "none":
            client = expected_raw[added[0].client_data_offset : added[0].client_data_end]
            actual_place = client[1] if place_encoding == "u8" else struct.unpack_from("<H", client, 1)[0]
            if actual_place != expected_place:
                raise SystemExit(f"Python oracle failed to reset {release_id} placement: {actual_place}")
        parsed_upgrades = added[0].upgrades
        if parsed_upgrades and parsed_upgrades != ():
            raise SystemExit(f"Python oracle kept upgrades on the new {release_id} outfit: {parsed_upgrades}")

        slug = release_id.removeprefix("stalker-")
        names = {
            "source": f"xray-add-{slug}-source.sav",
            "expected": f"xray-add-{slug}-expected.sav",
            "expectedRaw": f"xray-add-{slug}-expected.raw",
        }
        (output_dir / names["source"]).write_bytes(source)
        (output_dir / names["expected"]).write_bytes(expected)
        (output_dir / names["expectedRaw"]).write_bytes(expected_raw)
        python_format = by_id(release_id)
        vectors.append(
            {
                "releaseId": release_id,
                **names,
                "sourceSha256": source_sha256,
                "expectedSha256": hashlib.sha256(expected).hexdigest(),
                "expectedRawSha256": hashlib.sha256(expected_raw).hexdigest(),
                "itemKey": ITEM_KEY,
                "quantity": 1,
                "templateId": TEMPLATE_ID,
                "addedId": added[0].object_id,
                "placeEncoding": place_encoding,
                "sourcePlace": source_place,
                "expectedPlace": expected_place,
                "templateUpgrades": list(upgrades),
                "capabilities": {
                    name: python_format.capabilities.support(name).maturity
                    for name in CAPABILITIES
                },
            }
        )

    ammo_cases = (
        ("stalker-soc", SOC_FORMAT, 118, 3, False),
        ("stalker-cs", CS_FORMAT, 124, 5, False),
        ("stalker-cop", COP_FORMAT, 128, 6, False),
        ("stalker-soc-ee", SOC_EE_FORMAT, 118, 3, True),
        ("stalker-cs-ee", CS_EE_FORMAT, 128, 6, True),
        ("stalker-cop-ee", COP_EE_FORMAT, 128, 6, True),
    )
    ammo_key = "ammo_9x39_pab9"
    ammo_quantity = 17
    for release_id, spec, version, outer, is_ee in ammo_cases:
        if is_ee:
            outfit_source = outfit_sources[release_id]
            container = XRayContainer.from_bytes(outfit_source)
            matches = [value for value in container.chunks if value.type == 2]
            if len(matches) != 1:
                raise SystemExit(f"Synthetic EE fixture for {release_id} has no unique OBJECT chunk")
            object_chunk = matches[0]
            object_count = struct.unpack_from("<I", object_chunk.data)[0]
            ammo_update = struct.pack("<H", 0) + b"\x00" + struct.pack("<H", 30)
            ammo_template = spawn(
                ammo_key,
                0x3456,
                0,
                version,
                item_state(version, 30),
                ammo_update,
            )
            object_payload = (
                struct.pack("<I", object_count + 1)
                + object_chunk.data[4:]
                + object_record(ammo_template, ammo_update)
            )
            raw = b"".join(
                chunk(value.type, object_payload if value.type == 2 else value.data)
                for value in container.chunks
            )
            source = container.build(raw)
        else:
            source = synthetic_ammo_fixture(version=version, outer=outer)
        source_sha256 = hashlib.sha256(source).hexdigest()
        catalog = ItemCatalog(
            release_id,
            None,
            (
                ItemDefinition(
                    key=ammo_key,
                    display_name="Synthetic ammo",
                    category="ammo",
                    unit_weight=0.01,
                    width=1,
                    height=1,
                    max_stack=30,
                    slots=(),
                    prototype=None,
                    source="synthetic-fixture",
                    class_name="AMMO",
                    serialization_family="ammo",
                ),
            ),
        )
        prepared = prepare_xray(
            source,
            EditPlan(
                source=SourceRef(kind="local", locator="synthetic-fixture", sha256=source_sha256),
                adds=((ammo_key, ammo_quantity, "inventory"),),
            ),
            spec,
            catalog=catalog,
        )
        expected = bytes(prepared.data)
        expected_raw = XRayContainer.from_bytes(expected).raw
        before = parse_xray(source, spec, with_inventory=True)
        after = parse_xray(expected, spec, with_inventory=True)
        added = tuple(
            obj
            for obj in after.objects
            if obj.object_id not in {existing.object_id for existing in before.objects}
        )
        if len(added) != 1 or added[0].name != ammo_key or added[0].count != ammo_quantity:
            raise SystemExit(f"Python oracle failed the ammo add round-trip for {release_id}")

        slug = release_id.removeprefix("stalker-") + "-ammo"
        names = {
            "source": f"xray-add-{slug}-source.sav",
            "expected": f"xray-add-{slug}-expected.sav",
            "expectedRaw": f"xray-add-{slug}-expected.raw",
        }
        (output_dir / names["source"]).write_bytes(source)
        (output_dir / names["expected"]).write_bytes(expected)
        (output_dir / names["expectedRaw"]).write_bytes(expected_raw)
        python_format = by_id(release_id)
        vectors.append(
            {
                "releaseId": release_id,
                **names,
                "sourceSha256": source_sha256,
                "expectedSha256": hashlib.sha256(expected).hexdigest(),
                "expectedRawSha256": hashlib.sha256(expected_raw).hexdigest(),
                "itemKey": ammo_key,
                "quantity": ammo_quantity,
                "templateId": 0x3456 if is_ee else 0x1234,
                "addedId": 0x3457 if is_ee else 0x1235,
                "placeEncoding": "none",
                "sourcePlace": 0,
                "expectedPlace": 0,
                "templateUpgrades": [],
                "capabilities": {
                    name: python_format.capabilities.support(name).maturity
                    for name in CAPABILITIES
                },
            }
        )

    manifest = {
        "oracleRevision": _revision(python_repo),
        "vectors": vectors,
    }
    (output_dir / "xray-add-vectors.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"Generated {len(vectors)} synthetic X-Ray add vectors from {_revision(python_repo)}")
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
    parser.add_argument("--python-repo", type=Path, required=True)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).parents[1] / "tests" / "Fixtures" / "writer-add",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
