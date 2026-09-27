#!/usr/bin/env python3
"""Generate S2 condition and weapon-state vectors from the Python oracle."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import subprocess
import sys
from pathlib import Path


def generate(python_repo: Path, output_dir: Path) -> int:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    sys.path.insert(0, str(python_repo))

    from editor.codec import load_encoder
    from editor.formats import by_id
    from editor.models import EditPlan, SourceRef
    from editor.s2_item_state import read_s2_armor_condition, read_s2_weapon_condition
    from editor.prepare import prepare_edit
    import save_format as sf

    try:
        load_encoder()
    except Exception as exc:
        raise SystemExit(
            "The Python Kraken encoder is required for byte-for-byte S2 vectors. "
            "Build it with tools/build_ooz_encoder.py and put its output on "
            "PYTHONPATH before running this generator."
        ) from exc

    conftest = runpy.run_path(str(python_repo / "tests" / "conftest.py"))
    synthetic_save = conftest["synthetic_save"].__wrapped__()
    fixture = runpy.run_path(str(python_repo / "tests" / "test_s2_equipment_inventory.py"))
    armor_source = fixture["_save_with_equipped_armor"](synthetic_save)
    weapon_source = fixture["_save_with_grid_weapon"](synthetic_save, condition=0.75)
    armor_handle = fixture["EQUIPPED_HANDLE"]
    weapon_handle = fixture["WEAPON_HANDLE"]

    vectors = []
    cases = (
        ("armor", armor_source, armor_handle, 0.9, None, "armor"),
        ("weapon", weapon_source, weapon_handle, 0.9, None, "weapon"),
        ("armor-money", armor_source, armor_handle, 1.0, 900_000, "armor"),
    )
    output_dir.mkdir(parents=True, exist_ok=True)

    for case, source, handle, target, money, kind in cases:
        source_sha = hashlib.sha256(source).hexdigest()
        source_raw = sf.decompress_save(source)
        before = sf.inspect_save(source)
        item = next(item for item in before.inventory if item.handle == handle)
        source_name = f"s2-equipment-{case}-source.sav"
        expected_name = f"s2-equipment-{case}-expected.sav"
        expected_raw_name = f"s2-equipment-{case}-expected.raw"
        plan = EditPlan(
            source=SourceRef(kind="local", locator="synthetic-s2-equipment", sha256=source_sha),
            money=money,
            durability=((handle, target),),
        )
        prepared = prepare_edit(source, plan)
        expected = bytes(prepared.data)
        expected_raw = sf.decompress_save(expected)
        changed = [
            index
            for index, (left, right) in enumerate(zip(source_raw, expected_raw, strict=True))
            if left != right
        ]

        if kind == "armor":
            anchor = read_s2_armor_condition(
                source_raw,
                handle=handle,
                record_offset=item.record_offset,
                kind_code=item.kind_code,
            )
        else:
            starts = sf._record_start_map(source_raw, before.owned_handles)
            table = sf.locate_s2_item_name_table(
                source_raw,
                tuple(source_raw[offset + 8 : offset + 11] for offset in starts.values()),
            )
            if table is None:
                raise SystemExit("Python oracle did not locate the weapon name table")
            anchor = read_s2_weapon_condition(
                source_raw,
                handle=handle,
                record_offset=item.record_offset,
                record_end=item.record_end_guess,
                kind_code=item.kind_code,
                name_table=table,
            )
        if anchor is None:
            raise SystemExit(f"Python oracle did not confirm {case} condition anchor")

        allowed = set(range(anchor.value_offset, anchor.value_offset + 4))
        if money is not None:
            money_offset, _ = sf.locate_money(source_raw)
            allowed.update(range(money_offset, money_offset + 4))
        if not changed or not set(changed) <= allowed:
            raise SystemExit(f"Python oracle changed unexpected bytes for {case}: {changed}")

        after = sf.inspect_save(expected)
        edited = next(value for value in after.inventory if value.handle == handle)
        if abs(edited.condition - target) > 1e-6 or (money is not None and after.money != money):
            raise SystemExit(f"Python oracle failed {case} edit round-trip")

        (output_dir / source_name).write_bytes(source)
        (output_dir / expected_name).write_bytes(expected)
        (output_dir / expected_raw_name).write_bytes(expected_raw)
        vectors.append(
            {
                "case": case,
                "source": source_name,
                "sourceSha256": source_sha,
                "expected": expected_name,
                "expectedSha256": hashlib.sha256(expected).hexdigest(),
                "expectedRaw": expected_raw_name,
                "expectedRawSha256": hashlib.sha256(expected_raw).hexdigest(),
                "handle": handle,
                "kindCode": item.kind_code,
                "recordOffset": item.record_offset,
                "conditionOffset": anchor.value_offset,
                "sourceCondition": item.condition,
                "targetCondition": target,
                "displayName": item.display_name,
                "modules": list(item.modules) if item.modules is not None else None,
                "upgrades": list(item.upgrades) if item.upgrades is not None else None,
                "changedRawOffsets": changed,
                "money": money,
                "moneyOffset": sf.locate_money(source_raw)[0] if money is not None else None,
                "capability": by_id("stalker2").capabilities.support("edit_durability").maturity,
            }
        )

    manifest = {"oracleRevision": _revision(python_repo), "vectors": vectors}
    (output_dir / "s2-equipment-vectors.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"Generated Python S2 equipment vectors from {_revision(python_repo)}")
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
        / "writer-s2-equipment",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
