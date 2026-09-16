"""Scaffold setup and Steam runtime download, with builds mocked unless noted."""

import hashlib
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
import urllib.error
import zipfile
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import steam_runtime
import setup_mod


def fresh_copy(destination):
    shutil.copytree(ROOT, destination, ignore=shutil.ignore_patterns(
        ".git", "bin", "obj", "__pycache__", "references", "prepared", "first-upload", "archive",
        "mod_id.txt", "content", ".release-state", "workshop.json", ".prepared-manifest.sha256", ".workflow.lock", "output"))


@unittest.skipUnless((ROOT / "MyMod.csproj").exists(), "One-time setup tests apply to the unnamed template")
class SetupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="sts2 template ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "mod with spaces"
        fresh_copy(self.root)

    def test_setup_renames_every_executable_reference_and_validates_english(self):
        setup_mod.setup(self.root, "ExampleMod", 'Example "Mod"', "An Author", "A description.")
        self.assertFalse((self.root / "MyMod.csproj").exists())
        metadata = json.loads((self.root / "ExampleMod.json").read_text())
        self.assertEqual(metadata["id"], "ExampleMod")
        self.assertEqual(metadata["name"], 'Example "Mod"')
        self.assertEqual(metadata["author"], "An Author")
        listing = json.loads((self.root / "workshop/localizations/english.json").read_text())
        self.assertEqual(listing, {"title": 'Example "Mod"', "description": "A description."})
        for relative in ("install.sh", "src/MainFile.cs", "src/Localization.cs",
                         "scripts/workshop_release.py", "ExampleMod.csproj"):
            text = (self.root / relative).read_text()
            self.assertNotIn("MyMod", text)
            self.assertIn("ExampleMod", text)
        result = subprocess.run([sys.executable, str(self.root / "scripts/validate_localization.py"),
                                 "--english-only"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse((self.root / "workshop/mod_id.txt").exists())
        self.assertFalse((self.root / "localization-review.json").exists())
        with self.assertRaisesRegex(ValueError, "already been named"):
            setup_mod.setup(self.root, "AnotherMod", "Another", "Author", "Description")

    def test_invalid_identity_leaves_files_untouched(self):
        before = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob("*") if p.is_file()}
        for identity in ("MyMod", "../OtherMod", "lowercase", "Bad-Id", "X" * 65):
            with self.subTest(identity=identity), self.assertRaises(ValueError):
                setup_mod.setup(self.root, identity, "Name", "Author", "Description")
        with self.assertRaises(ValueError):
            setup_mod.setup(self.root, "ExampleMod", "界" * 43, "Author", "Description")
        after = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob("*") if p.is_file()}
        self.assertEqual(before, after)


class UploaderDownloadTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="sts2 uploader ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.destination = self.root / "references/ModUploader-osx-arm64"

    def bundle(self, names=None):
        output = io.BytesIO()
        with zipfile.ZipFile(output, "w") as archive:
            for name in names if names is not None else (*steam_runtime.UPLOADER_FILES, "ModUploader.pdb"):
                archive.writestr(name, f"official {name}")
        return output.getvalue()

    def download(self, data, digest=None):
        with patch.object(steam_runtime.urllib.request, "urlopen", return_value=io.BytesIO(data)) as fetch, \
             patch.object(steam_runtime, "UPLOADER_SHA256", digest or hashlib.sha256(data).hexdigest()):
            result = steam_runtime.ensure_uploader(self.root)
        self.assertEqual(fetch.call_args.args[0].full_url, steam_runtime.UPLOADER_URL)
        return result

    def test_downloads_complete_bundle_and_reuses_it_offline(self):
        executable = self.download(self.bundle())
        self.assertTrue(executable.stat().st_mode & 0o100)
        self.assertEqual((self.destination / "ModUploader.pdb").read_text(), "official ModUploader.pdb")
        self.assertTrue((self.destination / "template/content/README.md").is_file())
        with patch.object(steam_runtime.urllib.request, "urlopen") as fetch:
            self.assertEqual(steam_runtime.ensure_uploader(self.root), executable)
        fetch.assert_not_called()
        self.assertEqual(list(self.destination.parent.glob(".uploader-*")), [])

    def test_repairs_incomplete_copy_and_preserves_local_extras(self):
        self.destination.mkdir(parents=True)
        (self.destination / "ModUploader").write_text("incomplete old copy")
        (self.destination / "mod-uploader.log").write_text("keep this log")
        self.download(self.bundle())
        self.assertEqual((self.destination / "ModUploader").read_text(), "official ModUploader")
        self.assertTrue((self.destination / "libsteam_api.dylib").is_file())
        self.assertEqual((self.destination / "mod-uploader.log").read_text(), "keep this log")

    def test_failed_download_preserves_existing_files_and_cleans_temporary_files(self):
        self.destination.mkdir(parents=True)
        executable = self.destination / "ModUploader"
        executable.write_text("original")
        with patch.object(steam_runtime.urllib.request, "urlopen",
                          side_effect=urllib.error.URLError("offline")):
            with self.assertRaisesRegex(ValueError, "Retry, or manually extract"):
                steam_runtime.ensure_uploader(self.root)
        self.assertEqual(executable.read_text(), "original")
        self.assertEqual(list(self.destination.parent.glob(".uploader-*")), [])

    def test_rejects_bad_checksum_incomplete_archive_and_unsafe_paths(self):
        cases = ((self.bundle(), "0" * 64, "checksum mismatch"),
                 (self.bundle(["ModUploader"]), None, "incomplete"),
                 (self.bundle(["../escaped"]), None, "Unsafe"),
                 (self.bundle(["/absolute"]), None, "Unsafe"))
        for data, digest, message in cases:
            with self.subTest(message=message), self.assertRaisesRegex(ValueError, message):
                self.download(data, digest)
            self.assertFalse(self.destination.exists())
            self.assertEqual(list(self.destination.parent.glob(".uploader-*")), [])
        self.assertFalse((self.root / "references/escaped").exists())


if __name__ == "__main__":
    unittest.main()
