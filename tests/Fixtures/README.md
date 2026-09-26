# Synthetic codec fixtures

These byte fixtures are generated from the Python oracle's `_fixture` and
`_fixture_with_base_item` helpers in `tests/test_xray_save.py`, LZO vectors in
`tests/test_xray_container.py`, and `synthetic_save` fixture in
`tests/conftest.py`. `synthetic-s2-stash.raw` comes from
`tests/test_s2_stash.py::_save_with_stash` through
`tools/import_s2_reader_fixtures.py`. The companion
`tests/golden/fixture-vectors.json` is exported from only these generated save
bytes with the Python oracle's `tools/export_golden.py`. The fixtures contain no
personal save data.

Regenerate them with:

```bash
python tools/generate_fixtures.py --python-repo /path/to/S.T.A.L.K.E.R.-Save_Editor
python tools/import_s2_reader_fixtures.py --python-repo /path/to/S.T.A.L.K.E.R.-Save_Editor
```

`fixture-vectors.json` records the oracle Git revision and each container's
compressed stream range and expected decompressed payload file.
