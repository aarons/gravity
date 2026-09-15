"""Scaffold setup and first-upload preparation, with builds mocked unless noted."""

import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import prepare_first_upload
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
                         "scripts/workshop_release.py", "scripts/prepare_first_upload.py", "ExampleMod.csproj"):
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


class FirstUploadTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="sts2 first upload ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "fresh mod"
        fresh_copy(self.root)
        self.mod_id = next(self.root.glob("*.csproj")).stem
        (self.root / "workshop/image.png").write_bytes(b"test image never uploaded" * 2)
        uploader = self.root / "references/ModUploader-osx-arm64/ModUploader"
        uploader.parent.mkdir(parents=True)
        uploader.write_text("This fake executable must never be launched.")

    def build(self, command, **kwargs):
        self.assertEqual(command[:2], ["dotnet", "build"])
        self.assertIn("-p:InstallMod=false", command)
        output = Path(command[command.index("--output") + 1])
        output.mkdir(parents=True)
        (output / f"{self.mod_id}.dll").write_bytes(b"fake mod")
        return subprocess.CompletedProcess(command, 0)

    def test_first_upload_is_private_preparation_only_and_preserves_item_ids(self):
        with patch.object(prepare_first_upload.subprocess, "run", side_effect=self.build) as run:
            prepare_first_upload.prepare(self.root, ["-p:ModsPath=/unused"])
        self.assertEqual(run.call_count, 1)
        destination = self.root / "workshop/first-upload"
        self.assertEqual(json.loads((destination / "workshop.json").read_text())["visibility"], "private")
        self.assertEqual(set(p.name for p in (destination / "content").iterdir()),
                         {f"{self.mod_id}.dll", f"{self.mod_id}.json"})
        self.assertFalse((self.root / "workshop/mod_id.txt").exists())
        (destination / "mod_id.txt").write_text("123456789\n")
        with self.assertRaisesRegex(ValueError, "already exists"):
            prepare_first_upload.prepare(self.root, [])
        self.assertEqual((destination / "mod_id.txt").read_text(), "123456789\n")

    def test_failed_build_leaves_no_first_upload_workspace(self):
        with patch.object(prepare_first_upload.subprocess, "run",
                          side_effect=subprocess.CalledProcessError(1, ["dotnet", "build"])):
            with self.assertRaises(subprocess.CalledProcessError):
                prepare_first_upload.prepare(self.root, [])
        self.assertFalse((self.root / "workshop/first-upload").exists())
        self.assertEqual(list((self.root / "workshop").glob(".first-upload-*")), [])


if __name__ == "__main__":
    unittest.main()
