#!/usr/bin/env python3
"""Regenerate codec fixtures from the Python oracle's synthetic tests."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import shutil
import subprocess
import sys
import tempfile
from collections.abc import Callable
from pathlib import Path


def _oracle_revision(root: Path) -> str:
    return subprocess.run(
        ["git", "-C", str(root), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _load_fixtures(
    root: Path,
) -> tuple[
    Callable[..., bytes],
    Callable[[], bytes],
    Callable[[bytes], bytes],
    Callable[[bytes, int], bytes],
]:
    required = (
        root / "editor" / "xray_container.py",
        root / "tests" / "test_xray_save.py",
        root / "tests" / "conftest.py",
        root / "save_format.py",
        root / "tools" / "export_golden.py",
    )
    missing = [path for path in required if not path.is_file()]
    if missing:
        raise SystemExit(f"Not a Python Save Editor checkout: missing {missing[0].name}")

    sys.path.insert(0, str(root))
    sys.path.insert(0, str(root / "tests"))
    xray_codec = runpy.run_path(str(root / "editor" / "xray_container.py"))
    xray_tests = runpy.run_path(str(root / "tests" / "test_xray_save.py"))
    fixture_module = runpy.run_path(str(root / "tests" / "conftest.py"))
    synthetic_fixture = fixture_module["synthetic_save"]
    synthetic_save = getattr(synthetic_fixture, "__wrapped__", None)
    if not callable(synthetic_save):
        raise SystemExit("Python conftest synthetic_save fixture wrapper was not found")
    return (
        xray_tests["_fixture"],
        synthetic_save,
        xray_tests["lzo1x_compress"],
        xray_codec["lzo1x_decompress"],
    )


def generate(python_repo: Path, output_dir: Path) -> int:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    fixture_xray, synthetic_save, lzo_compress, lzo_decompress = _load_fixtures(python_repo)

    from editor.xray_container import XRayContainer
    from save_format import decompress_save

    xray_inputs = (
        ("xray-soc", {"version": 118, "outer": 3, "registry": b"registry-soc"}),
        (
            "xray-clear-sky",
            {"version": 124, "outer": 5, "registry": b"registry-clear-sky"},
        ),
        (
            "xray-call-of-pripyat",
            {"version": 128, "outer": 6, "registry": b"registry-call-of-pripyat"},
        ),
        (
            "xray-soc-ee",
            {"version": 118, "outer": 3, "alife": 51, "registry": b"registry-soc-ee"},
        ),
        (
            "xray-clear-sky-ee",
            {
                "version": 128,
                "outer": 6,
                "alife": 54,
                "section": "ammo_marsh_test",
                "registry": b"registry-clear-sky-ee",
            },
        ),
        (
            "xray-call-of-pripyat-ee",
            {
                "version": 128,
                "outer": 6,
                "alife": 54,
                "section": "ammo_zaton_test",
                "registry": b"registry-call-of-pripyat-ee",
            },
        ),
    )

    output_dir.mkdir(parents=True, exist_ok=True)
    vectors: list[dict[str, object]] = []
    generated_containers: list[str] = []
    for name, options in xray_inputs:
        container_bytes = fixture_xray(**options)
        parsed = XRayContainer.from_bytes(container_bytes)
        raw = parsed.raw
        container_path = f"{name}.sav"
        raw_path = f"{name}.raw"
        (output_dir / container_path).write_bytes(container_bytes)
        (output_dir / raw_path).write_bytes(raw)
        generated_containers.append(container_path)
        container_summary = {
            "magic": parsed.magic,
            "version": parsed.version,
            "unpackedSize": parsed.unpacked_size,
            "rawSha256": hashlib.sha256(raw).hexdigest(),
            "chunkTypes": list(parsed.chunk_types),
            "chunks": [
                {
                    "type": chunk.type,
                    "offset": chunk.offset,
                    "size": chunk.size,
                    "dataSha256": hashlib.sha256(chunk.data).hexdigest(),
                }
                for chunk in parsed.chunks
            ],
        }
        vectors.append(
            {
                "name": name,
                "codec": "lzo1x",
                "container": container_path,
                "raw": raw_path,
                "streamOffset": 12,
                "streamLength": len(container_bytes) - 12,
                "unpackedSize": len(raw),
                "containerSummary": container_summary,
            }
        )

    literal_inputs = (
        ("empty", b""),
        ("one-byte", b"a"),
        ("short-boundary", bytes(range(238))),
        ("long-boundary", bytes(range(239)) * 3),
        ("repeated", b"abc" * 1000),
    )
    for name, raw in literal_inputs:
        stream = lzo_compress(raw)
        if lzo_decompress(stream, len(raw)) != raw:
            raise SystemExit(f"Python LZO oracle failed its {name} fixture")
        raw_path = f"lzo1x-literal-{name}.raw"
        stream_path = f"lzo1x-literal-{name}.lzo"
        (output_dir / raw_path).write_bytes(raw)
        (output_dir / stream_path).write_bytes(stream)
        vectors.append(
            {
                "name": f"lzo1x-literal-{name}",
                "codec": "lzo1x-literal",
                "stream": stream_path,
                "raw": raw_path,
                "streamLength": len(stream),
                "unpackedSize": len(raw),
            }
        )

    extended_match_stream = (
        bytes([255])
        + b"A" * 238
        + b"\x40\x00" * 5383
        + b"\x10\x01\x04\x00"
        + b"\x11\x00\x00"
    )
    extended_match_raw = b"A" * (238 + 5383 * 3 + 10)
    if lzo_decompress(extended_match_stream, len(extended_match_raw)) != extended_match_raw:
        raise SystemExit("Python LZO oracle failed its extended M4 match fixture")
    match_raw_path = "lzo1x-extended-m4.raw"
    match_stream_path = "lzo1x-extended-m4.lzo"
    (output_dir / match_raw_path).write_bytes(extended_match_raw)
    (output_dir / match_stream_path).write_bytes(extended_match_stream)
    vectors.append(
        {
            "name": "lzo1x-extended-m4",
            "codec": "lzo1x-match",
            "stream": match_stream_path,
            "raw": match_raw_path,
            "streamLength": len(extended_match_stream),
            "unpackedSize": len(extended_match_raw),
        }
    )

    container_bytes = synthetic_save()
    raw = decompress_save(container_bytes)
    container_path = "synthetic-s2.sav"
    raw_path = "synthetic-s2.raw"
    (output_dir / container_path).write_bytes(container_bytes)
    (output_dir / raw_path).write_bytes(raw)
    vectors.append(
        {
            "name": "synthetic-s2",
            "codec": "kraken",
            "container": container_path,
            "raw": raw_path,
            "streamOffset": 4,
            "streamLength": len(container_bytes) - 8,
            "unpackedSize": len(raw),
        }
    )

    manifest = {
        "oracleRevision": _oracle_revision(python_repo),
        "vectors": vectors,
    }
    (output_dir / "fixture-vectors.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
    )

    _generate_golden_vectors(
        python_repo,
        output_dir,
        generated_containers,
        Path(__file__).resolve().parents[1] / "tests" / "golden" / "fixture-vectors.json",
    )
    print(
        f"Generated {len(vectors)} synthetic codec fixtures and Python golden vectors "
        f"in {output_dir.parent / 'golden'}"
    )
    return 0


def _generate_golden_vectors(
    python_repo: Path,
    fixture_dir: Path,
    container_names: list[str],
    golden_path: Path,
) -> None:
    exporter = python_repo / "tools" / "export_golden.py"
    with tempfile.TemporaryDirectory(prefix="save-editor-golden-") as temporary:
        temporary_root = Path(temporary)
        input_root = temporary_root / "synthetic-saves"
        input_root.mkdir()
        for name in container_names:
            shutil.copyfile(fixture_dir / name, input_root / name)

        output = temporary_root / "fixture-vectors.json"
        subprocess.run(
            [
                sys.executable,
                str(exporter),
                "--roots",
                str(input_root),
                "--out",
                str(output),
            ],
            check=True,
            cwd=python_repo,
        )
        golden_path.parent.mkdir(parents=True, exist_ok=True)
        golden_path.write_bytes(output.read_bytes())


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", type=Path, required=True)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "tests" / "Fixtures",
    )
    args = parser.parse_args()
    return generate(args.python_repo, args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
