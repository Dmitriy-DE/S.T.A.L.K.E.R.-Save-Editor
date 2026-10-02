from __future__ import annotations

import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "release"))
import publish_release


class PrivateInputTests(unittest.TestCase):
    def test_every_save_extension_the_editor_opens_is_rejected(self) -> None:
        loader = (ROOT / "src" / "StalkerSaveEditor.Desktop" / "ViewModels" / "SaveLibraryLoader.cs").read_text(encoding="utf-8")
        body = loader[loader.index("IsSupportedSaveFile(string path)"):]
        supported = set(re.findall(r'"(\.[a-z0-9]+)"', body[:body.index(";")]))
        self.assertTrue(supported, "save extensions were not found in SaveLibraryLoader")
        self.assertEqual(supported, publish_release.SAVE_SUFFIXES)
        for suffix in sorted(supported | {".bak", ".pem", ".key"}):
            with tempfile.TemporaryDirectory() as directory:
                (Path(directory) / ("private" + suffix)).write_bytes(b"x")
                with self.assertRaises(ValueError, msg=suffix):
                    publish_release.reject_private_inputs(Path(directory))


@unittest.skipUnless(shutil.which("openssl"), "openssl is required")
class SigningKeyTests(unittest.TestCase):
    def test_a_missing_key_and_a_key_the_app_does_not_trust_are_refused(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "not found"):
                publish_release.check_signing_key(Path(directory) / "missing.pem")
            key = Path(directory) / "other.pem"
            subprocess.run(["openssl", "ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", str(key)], check=True, capture_output=True)
            with self.assertRaisesRegex(ValueError, "does not match"):
                publish_release.check_signing_key(key)
            (Path(directory) / "latest.json").write_text("{}", encoding="utf-8")
            with self.assertRaises(ValueError):
                publish_release.sign_manifest(Path(directory), key)
            self.assertFalse((Path(directory) / "latest.json.sig").exists())

    def test_the_embedded_public_key_is_found(self) -> None:
        self.assertRegex(publish_release.embedded_public_key(), r"^[A-Za-z0-9+/=]{100,}$")


class CachePolicyTests(unittest.TestCase):
    def test_no_published_name_is_immutable(self) -> None:
        for name in ("latest.json", "latest.json.sig", "apt/dists/stable/InRelease", "SaveEditor-linux-x86_64.tar.gz", "apt/pool/main/x.deb"):
            policy = publish_release.cache_control(name)
            self.assertNotIn("immutable", policy)
            self.assertIn("must-revalidate", policy)
        self.assertIn("max-age=60", publish_release.cache_control("latest.json.sig"))
        worker = (ROOT / "infra" / "downloads-worker" / "worker.js").read_text(encoding="utf-8")
        self.assertNotIn('"public, max-age=31536000, immutable"', worker)


if __name__ == "__main__":
    unittest.main()
