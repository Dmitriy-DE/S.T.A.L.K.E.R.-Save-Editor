#!/usr/bin/env python3
"""Build tiny synthetic X-Ray game archives from vanilla-shaped fixtures."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
import struct
import zlib


ROOT = Path(__file__).parents[1]
FIXTURES = ROOT / "tests" / "Fixtures" / "companion-installer"
SCRIPT_FILES = ("scripts/bind_stalker.script", "scripts/ui_main_menu.script")


def config_directory(game: str) -> str:
    return "config" if game == "soc" else "configs"


def chunk(kind: int, body: bytes) -> bytes:
    return struct.pack("<II", kind, len(body)) + body


def archive_for_files(files: list[tuple[str, bytes]]) -> tuple[bytes, list[dict[str, object]]]:
    header_size = sum(14 + len(name.encode("utf-8")) + 4 for name, _ in files)
    data_start = 8 + header_size + 8
    header = bytearray()
    data = bytearray()
    manifest = []
    for name, content in files:
        encoded_name = name.encode("utf-8")
        header += struct.pack(
            "<HIII",
            16 + len(encoded_name),
            len(content),
            len(content),
            zlib.crc32(content) & 0xFFFFFFFF,
        )
        header += encoded_name
        header += struct.pack("<I", data_start + len(data))
        data += content
        manifest.append(
            {
                "name": name,
                "size": len(content),
                "sha256": hashlib.sha256(content).hexdigest(),
            }
        )
    return chunk(1, bytes(header)) + chunk(0, bytes(data)), manifest


def archive_for(game: str) -> tuple[bytes, list[dict[str, object]]]:
    game_root = FIXTURES / "vanilla" / game
    config_path = f"{config_directory(game)}/misc/quest_items.ltx"
    names = (*SCRIPT_FILES, config_path)
    files = [(f"gamedata/{name}", (game_root / name).read_bytes()) for name in names]
    return archive_for_files(files)


def write_vanilla_fixtures() -> None:
    """Create compact, synthetic scripts with the encoding and syntax shape used by X-Ray."""
    for game in ("soc", "cs", "cop"):
        game_root = FIXTURES / "vanilla" / game / "scripts"
        game_root.mkdir(parents=True, exist_ok=True)
        config_root = FIXTURES / "vanilla" / game / config_directory(game) / "misc"
        config_root.mkdir(parents=True, exist_ok=True)
        (config_root / "quest_items.ltx").write_text(
            f"; synthetic vanilla quest items for {game}\n[quest_items]\ndevice_pda = quest\n",
            encoding="cp1251",
            newline="\n",
        )
        bind = (
            f"-- synthetic {game} actor fixture: фикстура\r\n"
            "function actor_binder:update(delta)\r\n"
            "\tobject_binder.update(self, delta)\r\n"
            "end\r\n"
        )
        if game == "cop":
            bind += (
                "\r\n"
                "function actor_binder:use_inventory_item(obj)\r\n"
                "\tself:use_inventory_item(obj)\r\n"
                "end\r\n"
            )
        else:
            bind += (
                "\r\n"
                "function actor_binder:reinit()\r\n"
                "\tself.object:set_callback(callback.on_item_drop, self.on_item_drop, self)\r\n"
                "end\r\n"
            )
        menu = (
            f"-- synthetic {game} menu fixture: меню\r\n"
            "function main_menu:OnKeyboard(dik, keyboard_action)\r\n"
            "\tif keyboard_action == ui_events.WINDOW_KEY_PRESSED then\r\n"
            "\t\tif dik == DIK_keys.DIK_Q then\r\n"
            "\t\t\tself:Close()\r\n"
            "\t\tend\r\n"
            "\t \t\r\n"
            "\tend\r\n"
            "\treturn true\r\n"
            "end\r\n"
        )
        (game_root / "bind_stalker.script").write_bytes(bind.encode("cp1251"))
        (game_root / "ui_main_menu.script").write_bytes(menu.encode("cp1251"))


def main() -> None:
    write_vanilla_fixtures()
    games = {}
    for game in ("soc", "cs", "cop"):
        archive, entries = archive_for(game)
        archive_name = f"{game}.db"
        (FIXTURES / archive_name).write_bytes(archive)
        games[game] = {"archive": archive_name, "entries": entries}
    patch_name = "gamedata/scripts/ui_main_menu.script"
    vanilla_menu = (FIXTURES / "vanilla" / "cop" / "scripts" / "ui_main_menu.script").read_bytes()
    for patch_number in (1, 2):
        patch_contents = vanilla_menu + f"\r\n-- synthetic patch overlay {patch_number:02d}\r\n".encode("ascii")
        patch_archive, patch_entries = archive_for_files([(patch_name, patch_contents)])
        patch_archive_name = f"cop-patch-{patch_number:02d}.db"
        (FIXTURES / patch_archive_name).write_bytes(patch_archive)
        games["cop"][f"patchArchive{patch_number:02d}"] = patch_archive_name
        games["cop"][f"patchEntries{patch_number:02d}"] = patch_entries
    (FIXTURES / "manifest.json").write_text(
        json.dumps({"synthetic": True, "games": games}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(f"Generated synthetic archives for {len(games)} games in {FIXTURES}")


if __name__ == "__main__":
    main()
