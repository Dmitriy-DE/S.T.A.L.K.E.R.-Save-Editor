# Synthetic test fixtures

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

X-Ray stack writer vectors under `writer-stacks/` are synthetic and generated
from the Python `prepare_xray` oracle. Regenerate them with:

```bash
python tools/generate_xray_stack_fixtures.py --python-repo /path/to/S.T.A.L.K.E.R.-Save_Editor
```

The vectors cover original trilogy and Enhanced Edition formats and include
both packed saves and expected decompressed container bytes.

S2 money writer vectors under `writer-s2-money/` are generated from the
Python `prepare_edit` oracle and its native Kraken encoder. Build the encoder
into a temporary directory, add that directory to `PYTHONPATH`, then run:

```bash
python tools/generate_s2_money_fixtures.py --python-repo /path/to/S.T.A.L.K.E.R.-Save_Editor
```

The fixture manifest records the Python revision, source/output hashes, the
wallet offset, and the Python capability maturity.

S2 stack writer vectors under `writer-s2-stacks/` are generated from the
Python `prepare_edit` oracle and its native Kraken encoder. Build the encoder
into a temporary directory, add that directory to `PYTHONPATH`, then run:

```bash
python tools/generate_s2_stack_fixtures.py --python-repo /path/to/S.T.A.L.K.E.R.-Save_Editor
```

The manifest records the 32-bit object handle, requested count, source/output
hashes and capability maturity. The expected raw payload confirms that the
Python writer changes only the stack count and total-weight fields.
