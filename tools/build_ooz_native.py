#!/usr/bin/env python3
"""Build the native Kraken library from the Python editor's pinned ooz sources."""

from __future__ import annotations

import argparse
import os
import runpy
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


PYTHON_ORACLE_REVISION = "6f3839cb870161290ae1d404c40e291485c29e37"


def build(python_repo: Path, output_dir: Path) -> Path:
    python_repo = python_repo.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    revision = subprocess.run(
        ["git", "-C", str(python_repo), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    if revision != PYTHON_ORACLE_REVISION:
        raise SystemExit(
            "Python codec oracle revision mismatch: "
            f"expected {PYTHON_ORACLE_REVISION}, found {revision}"
        )

    oracle_builder_path = python_repo / "tools" / "build_ooz_encoder.py"
    if not oracle_builder_path.is_file():
        raise SystemExit(f"Python oracle is missing {oracle_builder_path.name}")

    oracle_builder = runpy.run_path(str(oracle_builder_path))
    archive_path = python_repo / "third_party" / "pyooz" / "pyooz-0.0.8.tar.gz"
    if not archive_path.is_file():
        raise SystemExit(f"Python oracle is missing {archive_path.name}")
    wrapper = Path(__file__).with_name("ooz_native.cpp")
    if not wrapper.is_file():
        raise SystemExit(f"Native wrapper is missing {wrapper.name}")

    output_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="save-editor-ooz-") as temporary:
        work = Path(temporary)
        source_root = oracle_builder["_source_root"](work / "source", archive_path)
        ooz_root = source_root / "ooz" / "dep" / "ooz"
        sources = [ooz_root / name for name in oracle_builder["_COMPRESSOR_SOURCES"]]
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
                command.append("-std=c++11")
            command.extend(f"-I{directory}" for directory in include_dirs)
            command.extend([str(wrapper), *(str(path) for path in sources), "-o", str(output)])

        subprocess.run(command, check=True, cwd=work)

    print(output)
    return output


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    build(args.python_repo, args.output_dir)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
