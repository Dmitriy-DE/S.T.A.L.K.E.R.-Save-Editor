# Synthetic codec fixtures

These byte fixtures are generated from the Python oracle's `_fixture` helper in
`tests/test_xray_save.py`, LZO vectors in `tests/test_xray_container.py`, and
`synthetic_save` fixture in `tests/conftest.py`. They contain no personal save
data.

Regenerate them with:

```bash
python tools/generate_fixtures.py --python-repo /path/to/S.T.A.L.K.E.R.-Save_Editor
```

`fixture-vectors.json` records the oracle Git revision and each container's
compressed stream range and expected decompressed payload file.
