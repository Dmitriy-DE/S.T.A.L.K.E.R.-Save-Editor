#!/usr/bin/env python3
"""Generate byte-parity vectors for S2 stash-to-player transfer from Python."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import runpy
import struct
import subprocess
import sys
import zlib
from collections.abc import Callable
from pathlib import Path
from typing import Any


PYTHON_ORACLE_REVISION = "6f3839cb870161290ae1d404c40e291485c29e37"
STASHED_HANDLE = 0x30000010


def generate(python_repo: Path, output_dir: Path, encoder_dir: Path | None) -> int:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    revision = _revision(python_repo)
    if revision != PYTHON_ORACLE_REVISION:
        raise SystemExit(
            "Python stash oracle revision mismatch: "
            f"expected {PYTHON_ORACLE_REVISION}, found {revision}"
        )

    if encoder_dir is not None:
        sys.path.insert(0, str(encoder_dir.expanduser().resolve()))
    sys.path.insert(0, str(python_repo))
    tests_dir = python_repo / "tests"
    sys.path.insert(0, str(tests_dir))

    import save_format as sf
    from editor.codec import compress, load_encoder

    test_path = tests_dir / "test_s2_stash.py"
    spec = importlib.util.spec_from_file_location("python_oracle_s2_stash", test_path)
    if spec is None or spec.loader is None:
        raise SystemExit(f"Cannot load Python fixture source: {test_path.name}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)

    source_raw = module._save_with_stash()
    expected_raw = sf._stash_to_player_in_raw(source_raw, STASHED_HANDLE)
    encoder = load_encoder()
    source_save = _pack_save(source_raw, encoder, compress)
    no_stash_namespace = runpy.run_path(str(tests_dir / "conftest.py"))
    no_stash_save = no_stash_namespace["synthetic_save"].__wrapped__()
    marker = b"\xff\xff\xff\xff\x06\x01\x00\x00\x00\x06"
    ambiguous_save = _pack_save(source_raw + marker, encoder, compress)
    stash_layout = sf.locate_stash_layout(source_raw)
    truncated_raw = source_raw[: stash_layout.grid_end_offset - 1]
    truncated_save = _pack_save(truncated_raw, encoder, compress)
    unflagged_raw = bytearray(source_raw)
    object_offset, *_ = sf.locate_object_record(source_raw, STASHED_HANDLE)
    unflagged_raw[object_offset + sf.OBJ_STASH_FLAG_OFFSET] = 0
    unflagged_save = _pack_save(bytes(unflagged_raw), encoder, compress)
    missing_bit_raw = bytearray(source_raw)
    missing_bit_raw[object_offset + sf.OBJ_FLAGS_OFFSET] &= ~sf.OBJ_FLAGS_STASH_BIT & 0xFF
    missing_bit_save = _pack_save(bytes(missing_bit_raw), encoder, compress)
    expected_record_offset, *_ = sf.locate_object_record(expected_raw, STASHED_HANDLE)

    if sf.locate_stash_layout(source_raw).live_handles != (STASHED_HANDLE,):
        raise SystemExit("Python fixture no longer has the expected live stash handle")
    if sf.locate_stash_layout(expected_raw).live_handles:
        raise SystemExit("Python oracle failed to clear the stash handle")
    if sf.locate_inventory_layout(expected_raw).owned_handles[-1] != STASHED_HANDLE:
        raise SystemExit("Python oracle failed to append the item to player inventory")

    output_dir.mkdir(parents=True, exist_ok=True)
    source_name = "s2-stash-source.sav"
    source_raw_name = "s2-stash-source.raw"
    expected_raw_name = "s2-stash-expected.raw"
    no_stash_name = "s2-stash-no-stash.sav"
    ambiguous_name = "s2-stash-ambiguous.sav"
    truncated_name = "s2-stash-truncated.sav"
    unflagged_name = "s2-stash-unflagged.sav"
    missing_bit_name = "s2-stash-missing-flag-bit.sav"
    (output_dir / source_name).write_bytes(source_save)
    (output_dir / source_raw_name).write_bytes(source_raw)
    (output_dir / expected_raw_name).write_bytes(expected_raw)
    (output_dir / no_stash_name).write_bytes(no_stash_save)
    (output_dir / ambiguous_name).write_bytes(ambiguous_save)
    (output_dir / truncated_name).write_bytes(truncated_save)
    (output_dir / unflagged_name).write_bytes(unflagged_save)
    (output_dir / missing_bit_name).write_bytes(missing_bit_save)

    manifest = {
        "expectedRaw": expected_raw_name,
        "expectedRawSha256": hashlib.sha256(expected_raw).hexdigest(),
        "negativeFixtures": {
            "ambiguous": ambiguous_name,
            "flagBitMissing": missing_bit_name,
            "missing": no_stash_name,
            "truncated": truncated_name,
            "unflagged": unflagged_name,
        },
        "handle": STASHED_HANDLE,
        "oracleRevision": revision,
        "source": source_name,
        "sourceRaw": source_raw_name,
        "sourceRawSha256": hashlib.sha256(source_raw).hexdigest(),
        "sourceSha256": hashlib.sha256(source_save).hexdigest(),
        "sourceRecordOffset": object_offset,
        "sourceStashFlag": source_raw[object_offset + sf.OBJ_STASH_FLAG_OFFSET],
        "sourceObjectFlags": source_raw[object_offset + sf.OBJ_FLAGS_OFFSET],
        "expectedRecordOffset": expected_record_offset,
        "expectedStashFlag": expected_raw[expected_record_offset + sf.OBJ_STASH_FLAG_OFFSET],
        "expectedObjectFlags": expected_raw[expected_record_offset + sf.OBJ_FLAGS_OFFSET],
    }
    (output_dir / "s2-stash-vectors.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"Generated Python S2 stash vector from {revision}")
    return 0


def _pack_save(raw: bytes, encoder: Any, compress_function: Callable[..., bytes]) -> bytes:
    stream = compress_function(raw, encoder=encoder, level=5)
    body = struct.pack("<I", len(raw)) + stream
    return body + struct.pack("<I", zlib.crc32(body) & 0xFFFFFFFF)


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
    parser.add_argument("--encoder-dir", type=Path)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parents[1]
        / "tests"
        / "Fixtures"
        / "writer-s2-stash",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir, args.encoder_dir)


if __name__ == "__main__":
    raise SystemExit(main())
