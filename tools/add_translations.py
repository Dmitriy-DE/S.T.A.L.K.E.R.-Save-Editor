#!/usr/bin/env python3
"""Adds interface strings to the message list and the 14 locale files.

Input: a UTF-8 TSV, one string per line — the Russian source text, then its translation into
en, uk, de, fr, it, es, pl, cs, pt-BR, tr, ja, ko, zh-CN, zh-TW. An existing translation of the
same source text is replaced. The files keep their format (sorted, one-space indent).

    python3 tools/add_translations.py strings.tsv
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

LANGUAGES = ["en", "uk", "de", "fr", "it", "es", "pl", "cs", "pt-BR", "tr", "ja", "ko", "zh-CN", "zh-TW"]
I18N = Path(__file__).resolve().parent.parent / "src" / "StalkerSaveEditor.Desktop" / "i18n"


def write(path: Path, data: object) -> None:
    path.write_text(json.dumps(data, ensure_ascii=False, indent=1, sort_keys=True) + "\n", encoding="utf-8")


def main(tsv: str) -> int:
    rows = [line.rstrip("\n").split("\t") for line in Path(tsv).read_text(encoding="utf-8").splitlines() if line.strip()]
    bad = [row[0] for row in rows if len(row) != len(LANGUAGES) + 1]
    if bad:
        print("rows without all 15 columns: " + " | ".join(bad), file=sys.stderr)
        return 1

    messages_path = I18N / "_messages.json"
    messages = json.loads(messages_path.read_text(encoding="utf-8"))
    messages["messages"] = sorted(set(messages["messages"]) | {row[0] for row in rows})
    write(messages_path, messages)

    for index, language in enumerate(LANGUAGES, start=1):
        path = I18N / f"{language}.json"
        catalog = json.loads(path.read_text(encoding="utf-8"))
        for row in rows:
            catalog[row[0]] = row[index]
        write(path, catalog)

    print(f"{len(rows)} strings added to {len(LANGUAGES)} languages")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]) if len(sys.argv) == 2 else 2)
