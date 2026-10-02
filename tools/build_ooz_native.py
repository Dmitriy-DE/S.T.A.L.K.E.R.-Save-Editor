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
    apply_vendor_patches(candidates[0])
    return candidates[0]


# Our changes to the vendored sources, applied after the archive's hash was checked. Each one replaces an exact
# text that must occur once: a different upstream version fails the build instead of being patched blindly.
VENDOR_PATCHES = (
    # lzna.cpp initialised short_length[12][4] as short_length[0][i] for i < 48: an out-of-bounds index on the
    # inner array (undefined behaviour, reported by the compiler). Same values, written through both indexes.
    (
        "ooz/dep/ooz/lzna.cpp",
        "  for (i = 0; i < 48; i++)\n    lut->short_length[0][i] = 0x2000;\n",
        "  for (i = 0; i < 12; i++)\n    for (int j = 0; j < 4; j++)\n      lut->short_length[i][j] = 0x2000;\n",
    ),
)


def apply_vendor_patches(source_root: Path) -> None:
    for relative, expected, replacement in VENDOR_PATCHES:
        path = source_root / relative
        text = path.read_text(encoding="utf-8").replace("\r\n", "\n")
        if text.count(expected) != 1:
            raise SystemExit(f"{relative}: the text of a vendor patch was not found exactly once")
        path.write_text(text.replace(expected, replacement), encoding="utf-8", newline="\n")


def _emscripten_env() -> tuple[dict[str, str], Path]:
    """emcc of the .NET wasm-tools workload (DOTNET_ROOT or ~/.dotnet), with the variables its config expects."""

    roots = [Path(os.environ[name]) for name in ("DOTNET_ROOT",) if os.environ.get(name)] + [Path.home() / ".dotnet", Path("/usr/lib/dotnet"), Path("/usr/share/dotnet")]
    for root in roots:
        sdks = sorted((root / "packs").glob("Microsoft.NET.Runtime.Emscripten.*.Sdk.*/*/tools"))
        nodes = sorted((root / "packs").glob("Microsoft.NET.Runtime.Emscripten.*.Node.*/*/tools/bin/node"))
        if sdks and nodes:
            tools = sdks[-1]
            env = dict(os.environ)
            env.update(
                DOTNET_EMSCRIPTEN_LLVM_ROOT=str(tools / "bin"),
                DOTNET_EMSCRIPTEN_BINARYEN_ROOT=str(tools),
                DOTNET_EMSCRIPTEN_NODE_JS=str(nodes[-1]),
                EM_CACHE=str(Path(tempfile.gettempdir()) / "save-editor-emcache"),
                EM_FROZEN_CACHE="0",
            )
            return env, tools / "emscripten"
    raise SystemExit("emcc not found: install the wasm-tools workload (dotnet workload install wasm-tools)")


def build_wasm(output_dir: Path, archive_path: Path = ARCHIVE) -> Path:
    """stalker_ooz.a for the browser build (NativeFileReference of StalkerSaveEditor.Browser)."""

    output_dir = output_dir.expanduser().resolve()
    _check_archive(archive_path)
    wrapper = Path(__file__).with_name("ooz_native.cpp")
    env, emscripten = _emscripten_env()
    output_dir.mkdir(parents=True, exist_ok=True)
    output = output_dir / "stalker_ooz.a"
    with tempfile.TemporaryDirectory(prefix="save-editor-ooz-wasm-") as temporary:
        work = Path(temporary)
        source_root = _source_root(work / "source", archive_path)
        ooz_root = source_root / "ooz" / "dep" / "ooz"
        objects = []
        for source in [wrapper, *(ooz_root / name for name in COMPRESSOR_SOURCES)]:
            obj = work / (source.stem + ".o")
            subprocess.run(
                [sys.executable, str(emscripten / "emcc.py"), "-O2", "-std=c++14", "-fvisibility=hidden",
                 f"-I{source_root}", f"-I{ooz_root / 'simde'}", "-c", str(source), "-o", str(obj)],
                check=True, cwd=work, env=env)
            objects.append(str(obj))
        output.unlink(missing_ok=True)
        subprocess.run([sys.executable, str(emscripten / "emar.py"), "rcs", str(output), *objects], check=True, env=env)
    print(output)
    return output


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
    parser.add_argument("--wasm", action="store_true", help="build stalker_ooz.a for the browser with the wasm-tools emcc")
    args = parser.parse_args()
    (build_wasm if args.wasm else build)(args.output_dir)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
