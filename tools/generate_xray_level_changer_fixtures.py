#!/usr/bin/env python3
"""Generate read-only X-Ray level-changer fixtures from the Python oracle."""

from __future__ import annotations

import argparse
import json
import struct
import subprocess
import sys
from pathlib import Path


def _zstring(value: str, encoding: str = "utf-8") -> bytes:
    return value.encode(encoding) + b"\x00"


def generate(python_repo: Path, fixture_dir: Path, golden_path: Path) -> int:
    python_repo = python_repo.expanduser().resolve()
    fixture_dir = fixture_dir.expanduser().resolve()
    golden_path = golden_path.expanduser().resolve()
    sys.path.insert(0, str(python_repo))
    from editor.xray_level_changer import parse_level_changer_state_suffix

    cases = (
        (
            "soc",
            "stalker-soc",
            118,
            struct.pack("<HI6f", 0x1234, 0x23456789, 1.25, -2.5, 3.75, 0.1, 0.2, 0.3)
            + _zstring("garbage")
            + _zstring("garbage_from_swamp")
            + b"\x01",
        ),
        (
            "cs",
            "stalker-cs",
            53,
            struct.pack("<HI4f", 9, 17, 4.0, 5.0, 6.0, 1.5)
            + _zstring("level")
            + _zstring("point"),
        ),
        (
            "cop",
            "stalker-cop",
            118,
            struct.pack("<HI6f", 0x2345, 0x12345678, -1.0, 2.0, 3.0, 0.4, 0.5, 0.6)
            + _zstring("zaton")
            + _zstring("zaton_from_skadovsk")
            + b"\x00",
        ),
        (
            "legacy",
            None,
            33,
            struct.pack("<II", 0xFFFFFFFF, 0xAABBCCDD)
            + _zstring("level")
            + _zstring("point"),
        ),
        (
            "cp1251",
            None,
            118,
            struct.pack("<HI6f", 1, 2, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0)
            + _zstring("болото", "cp1251")
            + _zstring("точка", "cp1251")
            + b"\x01",
        ),
    )

    fixture_dir.mkdir(parents=True, exist_ok=True)
    vectors = []
    for name, release_id, version, packet in cases:
        result = parse_level_changer_state_suffix(packet, version)
        fixture_name = f"synthetic-level-changer-{name}.bin"
        (fixture_dir / fixture_name).write_bytes(packet)
        vectors.append(
            {
                "case": name,
                "releaseId": release_id,
                "source": fixture_name,
                "objectVersion": version,
                "destGameVertexId": result.dest_game_vertex_id,
                "destLevelVertexId": result.dest_level_vertex_id,
                "destPosition": result.dest_position,
                "destDirection": result.dest_direction,
                "destLevelName": result.dest_level_name,
                "destLevelPointName": result.dest_level_point_name,
                "silent": result.silent,
                "consumedBytes": result.consumed_bytes,
            }
        )

    golden_path.parent.mkdir(parents=True, exist_ok=True)
    golden_path.write_text(
        json.dumps(
            {"oracleRevision": _revision(python_repo), "vectors": vectors},
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    print(f"Generated X-Ray level-changer fixtures from {_revision(python_repo)}")
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
        "--fixture-dir",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "tests" / "Fixtures" / "xray-level-changer",
    )
    parser.add_argument(
        "--golden",
        type=Path,
        default=Path(__file__).resolve().parents[1]
        / "tests"
        / "golden"
        / "xray-level-changer-vectors.json",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.fixture_dir, args.golden)


if __name__ == "__main__":
    raise SystemExit(main())
