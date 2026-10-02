"""The vendored Kraken sources are patched exactly, or the build stops."""

import importlib.util
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("build_ooz_native", ROOT / "tools" / "build_ooz_native.py")
build_ooz_native = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(build_ooz_native)


class VendorPatchTests(unittest.TestCase):
    def test_the_real_archive_gets_the_bounds_fix(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = build_ooz_native._source_root(Path(temporary) / "source", build_ooz_native.ARCHIVE)
            text = (root / "ooz" / "dep" / "ooz" / "lzna.cpp").read_text(encoding="utf-8")

        self.assertNotIn("short_length[0][i]", text)
        self.assertIn("lut->short_length[i][j] = 0x2000;", text)

    def test_an_unexpected_source_stops_the_build(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "ooz" / "dep" / "ooz"
            source.mkdir(parents=True)
            (source / "lzna.cpp").write_text("int main() { return 0; }\n", encoding="utf-8")

            with self.assertRaises(SystemExit):
                build_ooz_native.apply_vendor_patches(Path(temporary))


if __name__ == "__main__":
    unittest.main()
