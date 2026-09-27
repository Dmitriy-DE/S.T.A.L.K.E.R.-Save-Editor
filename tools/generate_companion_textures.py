"""Write the companion menu's flat textures as uncompressed 32-bit DDS files.

Usage: python3 tools/generate_companion_textures.py
Each texture is an 8x8 solid colour; the menu stretches them into panels,
1-pixel lines, flat buttons and bars, so edges stay crisp at any resolution.
Colours follow the editor's palette (charcoal grounds, amber accent #D6A62D).
"""

from __future__ import annotations

import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TARGETS = [
    ROOT / "mods/companion/cop/gamedata/textures/ui/se_companion",
    ROOT / "mods/companion/cs/gamedata/textures/ui/se_companion",
    ROOT / "mods/companion/soc/gamedata/textures/ui/se_companion",
]
SIZE = 8

COLOURS = {  # name: (r, g, b, a)
    "shade": (0, 0, 0, 150),
    "window": (12, 13, 10, 240),
    "panel": (19, 22, 17, 235),
    "row": (27, 30, 24, 255),
    "line": (58, 63, 51, 255),
    "line_soft": (36, 41, 34, 255),
    "amber": (214, 166, 45, 255),
    "amber_dim": (143, 111, 34, 255),
    "btn_e": (29, 33, 26, 245),
    "btn_h": (45, 50, 39, 255),
    "btn_t": (143, 111, 34, 255),
    "btn_d": (21, 23, 19, 200),
    "tab_e": (0, 0, 0, 0),
    "tab_h": (35, 39, 31, 220),
    "tab_t": (45, 50, 39, 255),
    "bar_bg": (36, 41, 34, 255),
    "danger": (216, 90, 69, 255),
    "ok": (123, 203, 98, 255),
}


def dds(rgba: tuple[int, int, int, int]) -> bytes:
    """Uncompressed A8R8G8B8 DDS (DDSD_CAPS|HEIGHT|WIDTH|PITCH|PIXELFORMAT)."""

    flags = 0x1 | 0x2 | 0x4 | 0x8 | 0x1000
    pitch = SIZE * 4
    pixel_format = struct.pack(
        "<II4sIIIII", 32, 0x41, b"\0\0\0\0", 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000
    )
    header = struct.pack("<IIIIIII", 124, flags, SIZE, SIZE, pitch, 0, 1) + b"\0" * 44 + pixel_format
    header += struct.pack("<IIIII", 0x1000, 0, 0, 0, 0)
    r, g, b, a = rgba
    return b"DDS " + header + bytes((b, g, r, a)) * (SIZE * SIZE)


def main() -> None:
    for target in TARGETS:
        target.mkdir(parents=True, exist_ok=True)
        for name, colour in COLOURS.items():
            (target / f"{name}.dds").write_bytes(dds(colour))
        print(f"{len(COLOURS)} textures in {target.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
