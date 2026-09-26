# Generated catalog data

`catalogs.json` and `catalog_names.json` are metadata-only outputs from the
Python editor's `tools/build_web_catalogs.py` and
`tools/build_official_names.py`. The files contain item, faction, upgrade, and
localized-name records; they do not contain game archives, save bytes, or
installation paths.

Refresh them from a Python editor checkout with:

```bash
python tools/import_catalog_assets.py --python-repo /path/to/S.T.A.L.K.E.R.-Save_Editor
```

The .NET Core project embeds these exact JSON files and reads them through the
catalog readers.
