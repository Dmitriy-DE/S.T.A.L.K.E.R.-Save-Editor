#!/usr/bin/env python3
"""S.T.A.L.K.E.R. Enhanced Edition Companion Mod Packaging Tool.

Prepares and packages the in-game companion mod for S.T.A.L.K.E.R.: Legends of
the Zone Trilogy (Enhanced Edition), creating the required desc.json metadata,
directory structure, and optionally compressing the archive into .xrp/.pack
using xrCompress under Wine/Proton.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path


GAME_CODES = {
    "soc": "SOC",
    "cs": "CS",
    "cop": "COP",
}


def find_xrcompress(user_path: str | None = None) -> Path | None:
    """Locate xrCompress.exe from argument or well-known cache locations."""
    if user_path:
        p = Path(user_path).expanduser().resolve()
        if p.is_file():
            return p
        print(f"Warning: specified xrCompress not found: {user_path}", file=sys.stderr)

    candidates = [
        Path.home() / ".cache/gemini-tmp/stk-utils/stk-utils/workshop/xrCompress.exe",
        Path.home() / ".local/share/stk-utils/workshop/xrCompress.exe",
        Path("tools/bin/xrCompress.exe"),
    ]
    for c in candidates:
        if c.is_file():
            return c.resolve()
    return None


def can_run_wine() -> bool:
    """Check if wine or wine64 is installed and executable."""
    return shutil.which("wine") is not None or shutil.which("wine64") is not None


def compress_archive(
    source_dir: Path,
    output_archive: Path,
    xrcompress_path: Path,
) -> bool:
    """Compress source directory with xrCompress under Wine."""
    wine_bin = shutil.which("wine") or shutil.which("wine64")
    if not wine_bin:
        print("Error: wine is required to run xrCompress.exe on Linux.", file=sys.stderr)
        return False

    source_dir = source_dir.resolve()
    cache_dir = Path.home() / ".cache/gemini-tmp"
    cache_dir.mkdir(parents=True, exist_ok=True)
    temp_stage = cache_dir / f"_stage_{source_dir.name}"
    if temp_stage.exists():
        shutil.rmtree(temp_stage)
    shutil.copytree(source_dir, temp_stage)

    try:
        cmd = [wine_bin, str(xrcompress_path.resolve()), str(temp_stage.resolve()), "-store"]
        env = dict(os.environ)
        env["WINEDEBUG"] = "-all"
        res = subprocess.run(
            cmd,
            cwd=str(xrcompress_path.parent.resolve()),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=env,
            text=True,
            timeout=120,
        )
        if res.returncode != 0:
            print(f"xrCompress failed (exit {res.returncode}):\n{res.stderr}", file=sys.stderr)
            return False

        generated_xdb = temp_stage.parent / f"{temp_stage.name}.xdb0"
        if not generated_xdb.is_file():
            print(f"Error: expected output {generated_xdb} was not created by xrCompress.", file=sys.stderr)
            return False

        output_archive.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(generated_xdb), str(output_archive))
        return True
    finally:
        if temp_stage.exists():
            shutil.rmtree(temp_stage)


def build_package(
    source_dir: Path,
    out_dir: Path,
    game: str,
    version: str,
    author: str,
    title: str,
    desc: str,
    preview: Path | None = None,
    compress: bool = True,
    xrcompress_path: Path | None = None,
) -> int:
    """Build the EE mod package layout and metadata."""
    if not source_dir.is_dir():
        print(f"Error: source directory does not exist: {source_dir}", file=sys.stderr)
        return 1

    game_key = game.lower()
    if game_key not in GAME_CODES:
        print(f"Error: invalid game '{game}'. Must be one of: {list(GAME_CODES.keys())}", file=sys.stderr)
        return 1

    out_dir.mkdir(parents=True, exist_ok=True)
    archive_name = f"save_editor_companion_{game_key}.xrp"
    archive_path = out_dir / archive_name

    preview_name = None
    if preview and preview.is_file():
        preview_name = f"preview{preview.suffix.lower()}"
        shutil.copyfile(preview, out_dir / preview_name)

    compression_done = False
    if compress:
        tool = find_xrcompress(str(xrcompress_path) if xrcompress_path else None)
        if tool and can_run_wine():
            print(f"Compressing {source_dir} -> {archive_path} using {tool.name}...")
            compression_done = compress_archive(source_dir, archive_path, tool)
            if compression_done:
                print(f"Successfully generated archive: {archive_path}")
            else:
                print("Warning: archive compression failed; staging loose files.", file=sys.stderr)
        else:
            print(
                "Notice: xrCompress.exe or Wine not found; staging files for manual compression.",
                file=sys.stderr,
            )

    if not compression_done:
        dest_gamedata = out_dir / "gamedata"
        if dest_gamedata.exists():
            shutil.rmtree(dest_gamedata)
        if (source_dir / "gamedata").is_dir():
            shutil.copytree(source_dir / "gamedata", dest_gamedata)
        else:
            shutil.copytree(source_dir, dest_gamedata)
    else:
        dest_gamedata = out_dir / "gamedata"
        if dest_gamedata.exists():
            shutil.rmtree(dest_gamedata)

    metadata = {
        "title": title,
        "game": game_key,
        "version": version,
        "author": author,
        "description": desc,
        "preview": preview_name,
        "entry_point": "gamedata/scripts/save_editor_companion.script",
        "package_file": archive_name if compression_done else None,
        "steam_workshop": {
            "game_code": GAME_CODES[game_key],
            "published_file_id": 0,
            "visibility": "public",
        },
    }

    desc_path = out_dir / "desc.json"
    with open(desc_path, "w", encoding="utf-8") as f:
        json.dump(metadata, f, ensure_ascii=False, indent=2)

    print(f"Wrote metadata: {desc_path}")
    print(f"Mod package prepared at: {out_dir}")
    print("\nSteam Workshop Upload Command:")
    print(
        f"  xrSWS_Upload.exe --mode=create --game={GAME_CODES[game_key]} "
        f"--path=\"{out_dir}\" --preview=\"{out_dir / (preview_name or 'preview.jpg')}\" "
        f'--title="{title}" --desc="{desc}"'
    )
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Pack S.T.A.L.K.E.R. companion mod for Enhanced Edition (Workshop / local)."
    )
    parser.add_argument(
        "source",
        type=Path,
        help="Path to mod source directory (containing gamedata/).",
    )
    parser.add_argument(
        "--game",
        type=str,
        required=True,
        choices=["soc", "cs", "cop"],
        help="Target game: soc, cs, or cop.",
    )
    parser.add_argument(
        "--output",
        "-o",
        type=Path,
        default=Path("dist/ee_companion"),
        help="Target output directory (default: dist/ee_companion).",
    )
    parser.add_argument(
        "--version",
        type=str,
        default="1.0.0",
        help="Mod version (default: 1.0.0).",
    )
    parser.add_argument(
        "--author",
        type=str,
        default="S.T.A.L.K.E.R. Save Editor Team",
        help="Mod author name.",
    )
    parser.add_argument(
        "--title",
        type=str,
        default="Save Editor Companion",
        help="Mod title for Steam Workshop.",
    )
    parser.add_argument(
        "--desc",
        type=str,
        default="In-game companion mod for S.T.A.L.K.E.R. Save Editor.",
        help="Mod description text.",
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=None,
        help="Path to preview image (.jpg, .png).",
    )
    parser.add_argument(
        "--no-compress",
        action="store_true",
        help="Do not invoke xrCompress, stage loose folder structure with desc.json.",
    )
    parser.add_argument(
        "--xrcompress",
        type=Path,
        default=None,
        help="Path to xrCompress.exe executable.",
    )

    args = parser.parse_args()
    return build_package(
        source_dir=args.source,
        out_dir=args.output,
        game=args.game,
        version=args.version,
        author=args.author,
        title=args.title,
        desc=args.desc,
        preview=args.preview,
        compress=not args.no_compress,
        xrcompress_path=args.xrcompress,
    )


if __name__ == "__main__":
    sys.exit(main())
