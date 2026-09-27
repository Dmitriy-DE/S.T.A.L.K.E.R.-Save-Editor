#!/usr/bin/env python3
"""Generate synthetic X-Ray faction vectors from the read-only Python oracle."""

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
    ("stalker-soc", 118, 3, None, None),
    ("stalker-cs", 124, 5, None, None),
    ("stalker-cop", 128, 6, None, None),
    ("stalker-cs-ee", 128, 6, 54, "marsh"),
    ("stalker-cop-ee", 128, 6, 54, "zaton"),
)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def relation_row(character_id: int, communities: tuple[tuple[int, int], ...]) -> bytes:
    personal = ((0x22, -7),)
    return (
        struct.pack("<H", character_id)
        + struct.pack("<I", len(personal))
        + b"".join(struct.pack("<Hi", key, value) for key, value in personal)
        + struct.pack("<I", len(communities))
        + b"".join(struct.pack("<ii", key, value) for key, value in communities)
    )


def relation_registry(*, info_portions_have_timestamp: bool) -> bytes:
    info_portion = struct.pack("<HI", 0x1234, 1) + b"fixture_info_portion\x00"
    if info_portions_have_timestamp:
        info_portion += struct.pack("<Q", 123456789)
    return (
        struct.pack("<I", 1)
        + info_portion
        + struct.pack("<I", 2)
        + relation_row(0, ((0, 100), (1, -100)))
        + relation_row(0x99, ((0, 25),))
        + b"REGISTRY-TAIL"
    )


def add_ee_marker(
    source: bytes,
    *,
    marker: str,
    version: int,
    spawn,
    object_record,
    base_item_state,
    lzo1x_compress,
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
                marker,
                0x7777,
                0,
                version,
                base_item_state(version),
                update,
            )
            payload = (count + 1).to_bytes(4, "little") + payload[4:] + object_record(guard, update)
        chunks.append(chunk.type.to_bytes(4, "little") + len(payload).to_bytes(4, "little") + payload)
    raw = b"".join(chunks)
    return struct.pack("<III", 0xFFFFFFFF, container.version, len(raw)) + lzo1x_compress(raw)


def generate(python_repo: Path, output_dir: Path, golden_dir: Path) -> int:
    sys.dont_write_bytecode = True
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    golden_dir = golden_dir.expanduser().resolve()
    sys.path.insert(0, str(python_repo))
    sys.path.insert(0, str(python_repo / "tests"))

    from editor.catalog import FactionCatalog, FactionDefinition
    from editor.formats import by_id
    from editor.models import EditPlan, SourceRef
    from editor.xray_container import lzo1x_compress
    from editor.xray_save import (
        COP_EE_FORMAT,
        COP_FORMAT,
        CS_EE_FORMAT,
        CS_FORMAT,
        SOC_FORMAT,
        parse_xray,
        prepare_xray,
    )

    helpers = runpy.run_path(str(python_repo / "tests" / "test_xray_save.py"))
    fixture = helpers["_fixture"]
    spawn = helpers["_spawn"]
    object_record = helpers["_object_record"]
    base_item_state = helpers["_base_item_state"]
    catalog_releases = {
        "stalker-soc": (SOC_FORMAT, None),
        "stalker-cs": (CS_FORMAT, None),
        "stalker-cop": (COP_FORMAT, None),
        "stalker-cs-ee": (CS_EE_FORMAT, "stalker-cs"),
        "stalker-cop-ee": (COP_EE_FORMAT, "stalker-cop"),
    }

    output_dir.mkdir(parents=True, exist_ok=True)
    vectors: list[dict[str, object]] = []
    for release_id, version, outer, alife, marker in CASES:
        source = fixture(
            version=version,
            outer=outer,
            registry=relation_registry(info_portions_have_timestamp=release_id != "stalker-cop"),
            player_community=0,
            alife=alife,
        )
        if marker is not None:
            source = add_ee_marker(
                source,
                marker=marker,
                version=version,
                spawn=spawn,
                object_record=object_record,
                base_item_state=base_item_state,
                lzo1x_compress=lzo1x_compress,
            )
        source_name = f"{release_id.removeprefix('stalker-')}-source.sav"
        (output_dir / source_name).write_bytes(source)
        entry: dict[str, object] = {
            "releaseId": release_id,
            "source": source_name,
            "sourceSha256": sha256(source),
            "pythonCapabilities": {
                name: by_id(release_id).capabilities.support(name).maturity
                for name in ("edit_player_faction", "edit_relations")
            },
        }
        if marker is None:
            spec, catalog_release = catalog_releases[release_id]
            bandit_id = {
                "stalker-soc": 12,
                "stalker-cs": 6,
                "stalker-cop": 1,
            }[release_id]
            faction_catalog = FactionCatalog(
                release_id=catalog_release or release_id,
                source_root=None,
                factions=(
                    FactionDefinition("actor", "Actor", "fixture", catalog_release or release_id, 0),
                    FactionDefinition(
                        "bandit",
                        "Bandit",
                        "fixture",
                        catalog_release or release_id,
                        bandit_id,
                    ),
                ),
                goodwill_min=-3000,
                goodwill_max=1000,
            )
            plan = EditPlan(
                source=SourceRef(kind="local", locator="synthetic-factions", sha256=sha256(source)),
                player_faction="bandit",
                faction_relations=(("bandit", 375),),
            )
            player_plan = EditPlan(
                source=SourceRef(kind="local", locator="synthetic-player-faction", sha256=sha256(source)),
                player_faction="bandit",
            )
            player_expected = bytes(
                prepare_xray(source, player_plan, spec, faction_catalog=faction_catalog).data
            )
            player_name = f"{release_id.removeprefix('stalker-')}-player-faction.sav"
            (output_dir / player_name).write_bytes(player_expected)

            relation_plan = EditPlan(
                source=SourceRef(kind="local", locator="synthetic-relations", sha256=sha256(source)),
                faction_relations=(("bandit", 375),),
            )
            relation_expected = bytes(
                prepare_xray(source, relation_plan, spec, faction_catalog=faction_catalog).data
            )
            relation_name = f"{release_id.removeprefix('stalker-')}-relations.sav"
            (output_dir / relation_name).write_bytes(relation_expected)

            prepared = prepare_xray(
                source,
                plan,
                spec,
                faction_catalog=faction_catalog,
            )
            expected = bytes(prepared.data)
            parsed = parse_xray(expected, spec)
            if parsed.player_faction_index != bandit_id or dict(parsed.faction_relations).get(bandit_id) != 375:
                raise SystemExit(f"Python oracle failed faction round-trip for {release_id}")
            expected_name = f"{release_id.removeprefix('stalker-')}-expected.sav"
            (output_dir / expected_name).write_bytes(expected)
            entry.update(
                {
                    "expected": expected_name,
                    "expectedSha256": sha256(expected),
                    "expectedPlayerFaction": player_name,
                    "expectedPlayerFactionSha256": sha256(player_expected),
                    "expectedRelations": relation_name,
                    "expectedRelationsSha256": sha256(relation_expected),
                    "targetFaction": "bandit",
                    "targetFactionIndex": bandit_id,
                    "targetGoodwill": 375,
                }
            )
        else:
            parsed = parse_xray(source, catalog_releases[release_id][0])
            if parsed.player_faction_index != 0:
                raise SystemExit(f"Python oracle failed EE faction read for {release_id}")
        entry["playerFactionIndex"] = 0
        entry["actorRelations"] = [[0, 100], [1, -100]]
        vectors.append(entry)

    manifest = {
        "oracleRevision": subprocess.run(
            ["git", "-C", str(python_repo), "rev-parse", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip(),
        "vectors": vectors,
        "negativeCases": {
            "unknownFactionKey": "foreign-faction-key",
            "truncatedRelationRegistry": True,
            "enhancedWrites": "unsupported",
        },
    }
    vectors_json = json.dumps(manifest, indent=2, sort_keys=True) + "\n"
    (output_dir / "xray-faction-vectors.json").write_text(vectors_json, encoding="utf-8")
    golden_dir.mkdir(parents=True, exist_ok=True)
    (golden_dir / "xray-faction-vectors.json").write_text(vectors_json, encoding="utf-8")
    print(f"Generated {len(vectors)} X-Ray faction vectors from {manifest['oracleRevision']}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument(
        "--golden-dir",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "tests" / "golden",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir, args.golden_dir)


if __name__ == "__main__":
    raise SystemExit(main())
