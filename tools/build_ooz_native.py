"""Build the native Kraken library (stalker_ooz) from the vendored pyooz 0.0.8 sources.

    python tools/build_ooz_native.py --output-dir artifacts/native

The sources (third_party/pyooz/pyooz-0.0.8.tar.gz, SHA-256 in provenance.json) were moved here
from the Python editor so the C# repository builds on its own.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tarfile
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ARCHIVE = ROOT / "third_party" / "pyooz" / "pyooz-0.0.8.tar.gz"
COMPRESSOR_SOURCES = (
    "bitknit.cpp",
    "lzna.cpp",
    "kraken.cpp",
    "compress.cpp",
    "compr_entropy.cpp",
    "compr_kraken.cpp",
    "compr_leviathan.cpp",
    "compr_match_finder.cpp",
    "compr_mermaid.cpp",
    "compr_multiarray.cpp",
    "compr_tans.cpp",
)


def _check_archive(archive_path: Path) -> None:
    provenance = json.loads((archive_path.parent / "provenance.json").read_text(encoding="utf-8"))
    expected = json.dumps(provenance)
    digest = hashlib.sha256(archive_path.read_bytes()).hexdigest()
    if digest not in expected:
        raise SystemExit(f"{archive_path.name}: SHA-256 {digest} is not the one recorded in provenance.json")


def _source_root(work: Path, archive_path: Path) -> Path:
    work.mkdir(parents=True, exist_ok=True)
    destination = work.resolve()
    with tarfile.open(archive_path, "r:gz") as archive:
        for member in archive.getmembers():
            target = (destination / member.name).resolve()
            if target != destination and destination not in target.parents:
                raise SystemExit(f"unsafe path in the pyooz archive: {member.name}")
        archive.extractall(destination, filter="data")
    candidates = sorted(path for path in work.iterdir() if path.is_dir() and path.name.startswith("pyooz-"))
    if len(candidates) != 1:
        raise SystemExit("the pyooz archive has no single source root")
    return candidates[0]


def build(output_dir: Path, archive_path: Path = ARCHIVE) -> Path:
    output_dir = output_dir.expanduser().resolve()
    _check_archive(archive_path)
    wrapper = Path(__file__).with_name("ooz_native.cpp")
    if not wrapper.is_file():
        raise SystemExit(f"Native wrapper is missing {wrapper.name}")

    output_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="save-editor-ooz-") as temporary:
        work = Path(temporary)
        source_root = _source_root(work / "source", archive_path)
        ooz_root = source_root / "ooz" / "dep" / "ooz"
        sources = [ooz_root / name for name in COMPRESSOR_SOURCES]
        missing = [path for path in sources if not path.is_file()]
        if missing:
            raise SystemExit("The pyooz archive is missing a required source file")

        output_name = {
            "win32": "stalker_ooz.dll",
            "darwin": "libstalker_ooz.dylib",
        }.get(sys.platform, "libstalker_ooz.so")
        output = output_dir / output_name
        include_dirs = [source_root, ooz_root / "simde"]

        if sys.platform == "win32":
            compiler = shutil.which("cl")
            if compiler is None:
                raise SystemExit("MSVC cl.exe is unavailable; initialize the Visual Studio environment")
            object_dir = work / "objects"
            object_dir.mkdir()
            command = [
                compiler,
                "/nologo",
                "/O2",
                "/EHsc",
                "/std:c++14",
                "/LD",
                *(f"/I{directory}" for directory in include_dirs),
                f"/Fo{object_dir}{os.sep}",
                str(wrapper),
                *(str(path) for path in sources),
                "/link",
                f"/OUT:{output}",
            ]
        else:
            compiler_name = "clang++" if sys.platform == "darwin" else "c++"
            compiler = shutil.which(compiler_name)
            if compiler is None:
                raise SystemExit(f"C++ compiler {compiler_name} is unavailable")
            command = [
                compiler,
                "-O2",
                "-fPIC",
                "-fvisibility=hidden",
                "-dynamiclib" if sys.platform == "darwin" else "-shared",
            ]
            if sys.platform == "darwin":
                # One library for both osx-arm64 and osx-x64 packages.
                command.extend(["-std=c++11", "-arch", "arm64", "-arch", "x86_64"])
            command.extend(f"-I{directory}" for directory in include_dirs)
            command.extend([str(wrapper), *(str(path) for path in sources), "-o", str(output)])

        subprocess.run(command, check=True, cwd=work)

    print(output)
    return output


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    build(args.output_dir)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
