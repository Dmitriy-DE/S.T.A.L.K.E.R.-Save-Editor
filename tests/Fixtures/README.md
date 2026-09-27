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

The small X-Ray database archive under `xray-archive/` is synthetic. Its
generator verifies the config, localization, and asset entries through the
Python oracle's `editor.xray_catalog._read_xray_archive` before writing the
fixture bytes and expected entry data:

```bash
PYTHONDONTWRITEBYTECODE=1 python tools/generate_xray_archive_fixtures.py \
  --python-repo /path/to/S.T.A.L.K.E.R.-Save_Editor
```

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

S2 stash-transfer vectors under `writer-s2-stash/` are generated from the
Python `test_s2_stash.py::_save_with_stash` fixture and
`save_format._stash_to_player_in_raw`. The source and rejection-case containers
are synthetic; the expected byte-for-byte result is the decompressed raw
payload. Regenerate them with:

```bash
python tools/generate_s2_stash_fixtures.py \
  --python-repo /path/to/S.T.A.L.K.E.R.-Save_Editor \
  --encoder-dir /path/to/temporary/ooz-encoder
```

The S2 `move_items` capability remains `unsupported` in the Python registry,
so the C# `Prepare` entry point fails closed until that capability is enabled.

X-Ray stash reader and take vectors under `xray-stashes/` use the Python
`xray_stashes` and `take_from_stash` oracle with synthetic registry objects for
the original trilogy and Enhanced Edition formats. Regenerate them with:

```bash
python tools/generate_xray_stash_fixtures.py \
  --python-repo /path/to/S.T.A.L.K.E.R.-Save_Editor \
  --output-dir tests/Fixtures/xray-stashes
```

The fixture set contains no game saves. Backpack-to-stash transfers and direct
stash additions are C#-only synthetic contracts under decision D14; Python has
no corresponding writer oracle.
