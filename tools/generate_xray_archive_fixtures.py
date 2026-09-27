#!/usr/bin/env python3
"""Generate small X-Ray archive fixtures and verify them with the Python oracle."""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import subprocess
import sys
import zlib
from pathlib import Path


FILES = {
    "gamedata/config/items.ltx": b"[ammo_test]\nclass = AMMO\ninv_name = st_ammo_test\n",
    "gamedata/configs/text/eng/items.xml": (
        b'<?xml version="1.0"?>\n'
        b'<string_table><string id="st_ammo_test"><text>Test rounds</text>'
        b"</string></string_table>\n"
    ),
    "gamedata/textures/ui/icon.dds": b"synthetic-dds-data\x00\x01\x02",
}


def chunk(chunk_type: int, body: bytes) -> bytes:
    return struct.pack("<II", chunk_type, len(body)) + body


def build_archive() -> bytes:
    encoded = [(name, name.encode("utf-8"), data) for name, data in FILES.items()]
    header_size = sum(14 + len(name_bytes) + 4 for _, name_bytes, _ in encoded)
    metadata = chunk(666, b"[header]\nentry_point = $fs_root$\\gamedata\\\n")
    data_start = len(metadata) + 8 + header_size + 8
    header = bytearray()
    data = bytearray()
    for name, name_bytes, content in encoded:
        header.extend(
            struct.pack(
                "<HIII",
                16 + len(name_bytes),
                len(content),
                len(content),
                zlib.crc32(content) & 0xFFFFFFFF,
            )
        )
        header.extend(name_bytes)
        header.extend(struct.pack("<I", data_start + len(data)))
        data.extend(content)
    return metadata + chunk(1, bytes(header)) + chunk(0, bytes(data))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", required=True, type=Path)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "tests" / "Fixtures" / "xray-archive",
    )
    args = parser.parse_args()
    python_repo = args.python_repo.expanduser().resolve()
    output_dir = args.output_dir.expanduser().resolve()
    sys.path.insert(0, str(python_repo))
    from editor.xray_catalog import _read_xray_archive

    oracle_revision = subprocess.run(
        ["git", "-C", str(python_repo), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()

    output_dir.mkdir(parents=True, exist_ok=True)
    archive_path = output_dir / "synthetic.db"
    archive_path.write_bytes(build_archive())

    expected_configs = {
        name: content
        for name, content in FILES.items()
        if name.endswith((".ltx", ".xml"))
    }
    actual_configs = _read_xray_archive(archive_path)
    if actual_configs != expected_configs:
        raise SystemExit("Python X-Ray archive oracle did not reproduce the synthetic config entries")

    icon_name = "gamedata/textures/ui/icon.dds"
    actual_icon = _read_xray_archive(
        archive_path,
        suffixes=(".dds",),
        names=frozenset({icon_name}),
    )
    if actual_icon != {icon_name: FILES[icon_name]}:
        raise SystemExit("Python X-Ray archive oracle did not reproduce the synthetic asset entry")

    entries = []
    for index, (name, content) in enumerate(FILES.items()):
        data_file = f"entry-{index:02d}.bin"
        (output_dir / data_file).write_bytes(content)
        entries.append(
            {
                "name": name,
                "data": data_file,
                "size": len(content),
                "sha256": hashlib.sha256(content).hexdigest(),
            }
        )

    manifest = {
        "pythonOracleRevision": oracle_revision,
        "archive": archive_path.name,
        "archiveSha256": hashlib.sha256(archive_path.read_bytes()).hexdigest(),
        "entries": entries,
    }
    (output_dir / "manifest.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
