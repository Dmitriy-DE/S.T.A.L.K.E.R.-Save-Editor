#!/usr/bin/env python3
"""
tools/convert_locales.py - Convert Python UI localization dictionaries into
neutral JSON format (tests/golden/locales/<lang>.json) and generate sample RESX.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def sanitize_key_for_resx(key: str) -> str:
    """Generate a valid XML / C# identifier from arbitrary string keys."""
    # Replace whitespace and invalid identifier chars with underscore
    clean = re.sub(r"[^a-zA-Z0-9_]", "_", key.strip())
    clean = re.sub(r"_+", "_", clean).strip("_")
    if not clean:
        clean = "String"
    if clean[0].isdigit():
        clean = "_" + clean
    return clean


RESX_HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="metadata">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" />
              </xsd:sequence>
              <xsd:attribute name="name" use="required" type="xsd:string" />
              <xsd:attribute name="type" type="xsd:string" />
              <xsd:attribute name="mimetype" type="xsd:string" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
"""


def export_sample_resx(en_dict: dict[str, str], out_path: Path) -> None:
    lines = [RESX_HEADER]
    seen_names: set[str] = set()

    for idx, (ru_key, en_val) in enumerate(sorted(en_dict.items())):
        base_name = sanitize_key_for_resx(ru_key)
        name = base_name
        counter = 1
        while name in seen_names:
            name = f"{base_name}_{counter}"
            counter += 1
        seen_names.add(name)

        if isinstance(en_val, list):
            serialized_val = "|".join(str(item) for item in en_val)
        else:
            serialized_val = str(en_val)

        # XML escape value and comment
        escaped_val = (
            serialized_val.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        )
        escaped_comment = (
            ru_key.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        )

        lines.append(
            f'  <data name="{name}" xml:space="preserve">\n'
            f"    <value>{escaped_val}</value>\n"
            f"    <comment>{escaped_comment}</comment>\n"
            f"  </data>"
        )

    lines.append("</root>\n")
    out_path.write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Convert Python locales to C# format."
    )
    parser.add_argument(
        "--source-dir",
        type=Path,
        default=Path("/home/dmytro/save-editor-review/worktrees/save-editor/locales"),
        help="Path to Python locales directory",
    )
    parser.add_argument(
        "--out-dir",
        type=Path,
        default=Path("tests/golden/locales"),
        help="Destination directory for converted locales",
    )
    args = parser.parse_args()

    source_dir: Path = args.source_dir
    out_dir: Path = args.out_dir
    out_dir.mkdir(parents=True, exist_ok=True)

    if not source_dir.is_dir():
        print(f"Error: Source directory {source_dir} not found.", file=sys.stderr)
        sys.exit(1)

    json_files = sorted(source_dir.glob("*.json"))
    manifest = {}

    ru_keys: set[str] = set()
    en_dict: dict[str, str] = {}

    for jf in json_files:
        lang = jf.stem
        if lang == "_messages":
            continue

        with open(jf, "r", encoding="utf-8") as f:
            data = json.load(f)

        if not isinstance(data, dict):
            continue

        ru_keys.update(data.keys())
        if lang == "en":
            en_dict = data

        sorted_data = {k: data[k] for k in sorted(data.keys())}
        dest_file = out_dir / f"{lang}.json"
        with open(dest_file, "w", encoding="utf-8") as f:
            json.dump(sorted_data, f, ensure_ascii=False, indent=2)
            f.write("\n")

        manifest[lang] = {
            "file": f"{lang}.json",
            "key_count": len(sorted_data),
        }
        print(f"Exported {lang}: {len(sorted_data)} keys -> {dest_file}")

    # Also generate ru.json (identity mapping)
    ru_sorted = {k: k for k in sorted(ru_keys)}
    ru_file = out_dir / "ru.json"
    with open(ru_file, "w", encoding="utf-8") as f:
        json.dump(ru_sorted, f, ensure_ascii=False, indent=2)
        f.write("\n")
    manifest["ru"] = {
        "file": "ru.json",
        "key_count": len(ru_sorted),
    }
    print(f"Exported ru: {len(ru_sorted)} keys -> {ru_file}")

    # Generate sample RESX for English
    resx_file = out_dir / "Resources.en.resx"
    export_sample_resx(en_dict, resx_file)
    print(f"Generated sample RESX -> {resx_file}")

    # Write manifest summary
    manifest_file = out_dir / "manifest.json"
    with open(manifest_file, "w", encoding="utf-8") as f:
        json.dump(
            {"languages": manifest, "total_unique_keys": len(ru_keys)},
            f,
            ensure_ascii=False,
            indent=2,
        )
        f.write("\n")
    print(f"Manifest written -> {manifest_file}")


if __name__ == "__main__":
    main()
