"""Release boundaries tested in isolated checkouts; no game install or Steam connection."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import workshop_release as workflow


class WorkshopReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="workshop release ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name in ("scripts", "src", "localization", "workshop/localizations"):
            shutil.copytree(ROOT / name, self.root / name, ignore=shutil.ignore_patterns("__pycache__"))
        for name in ("supported-languages.json", "Gravity.json",
                     "Gravity.csproj", "Sts2PathDiscovery.props", "install.sh", "package.sh", "release.sh",
                     "workshop/settings.json"):
            shutil.copy2(ROOT / name, self.root / name)
        # Synthetic data is confined to temporary test workspaces.
        from update_localizations import fingerprint
        catalog = workflow.read_json(self.root / "supported-languages.json")
        records = {}
        for directory, source, languages in (
            ("localization", "eng", catalog.keys()),
            ("workshop/localizations", "english", catalog.values()),
        ):
            table = workflow.read_json(self.root / directory / f"{source}.json")
            for language in languages:
                relative = f"{directory}/{language}.json"
                workflow.write_json(self.root / relative, table)
                if language != source:
                    records[relative] = {"english_sha256": fingerprint(table),
                                         "translation_sha256": fingerprint(table)}
        workflow.write_json(self.root / "localization-review.json", {"version": 1, "translations": records})
        (self.root / "workshop/mod_id.txt").write_text("123456789\n")
        (self.root / "workshop/image.png").write_bytes(b"test image, never uploaded" * 2)
        self.native = self.root / "references/ModUploader-osx-arm64/libsteam_api.dylib"
        self.native.parent.mkdir(parents=True)
        self.native.write_bytes(b"fake native library: never loaded")
        previews = self.root / "workshop/previews"
        previews.mkdir()
        (previews / ".DS_Store").write_bytes(b"finder metadata")
        (previews / "example.png").write_bytes(b"a" * 32)
        self.calls = []

    def build(self, command, **kwargs):
        self.calls.append(command)
        output = Path(command[command.index("--output") + 1])
        output.mkdir(parents=True)
        if "Gravity.csproj" in command[2]:
            (output / "Gravity.dll").write_bytes(b"mod with all game translations")
        else:
            (output / "WorkshopLocalization.dll").write_bytes(b"fake publisher")
        return subprocess.CompletedProcess(command, 0)

    def package(self, build=None):
        with patch.object(workflow.subprocess, "run", side_effect=build or self.build):
            workflow.package(self.root, [])

    def test_english_review_and_all_translations_are_frozen_without_changing_sources(self):
        before = workflow.inputs(self.root)
        self.package()
        self.assertEqual(before, workflow.inputs(self.root))
        prepared = workflow.check(self.root)
        manifest = workflow.read_json(self.root / "workshop/workshop.json")
        english = workflow.read_json(self.root / "workshop/localizations/english.json")
        self.assertEqual({k: manifest[k] for k in english}, english)
        self.assertEqual(len(list((prepared / "localizations").glob("*.json"))), 14)
        self.assertFalse((prepared / "previews/.DS_Store").exists())
        self.assertTrue((self.root / "workshop/previews/.DS_Store").exists())
        self.assertEqual(len(self.calls), 2)
        self.assertIn("-p:InstallMod=false", self.calls[0])
        self.assertEqual(len(list((self.root / "archive").glob("*.zip"))), 1)
        self.package()
        self.assertEqual(len(list((self.root / "archive").glob("*.zip"))), 1)

    def test_stale_translations_fail_before_building_and_preserve_previous_package(self):
        self.package()
        before = workflow.hashes(self.root / "workshop/prepared")
        path = self.root / "workshop/localizations/english.json"
        english = workflow.read_json(path)
        english["title"] += " changed"
        workflow.write_json(path, english)
        self.calls.clear()
        with self.assertRaisesRegex(ValueError, "Translations need review"):
            self.package()
        self.assertEqual(self.calls, [])
        self.assertEqual(before, workflow.hashes(self.root / "workshop/prepared"))

    def test_build_failure_or_source_race_preserves_previous_output(self):
        self.package()
        before = workflow.hashes(self.root / "workshop/prepared")
        def fail(command, **kwargs):
            raise subprocess.CalledProcessError(1, command)
        with self.assertRaises(subprocess.CalledProcessError):
            self.package(fail)
        self.assertEqual(before, workflow.hashes(self.root / "workshop/prepared"))
        def change(command, **kwargs):
            result = self.build(command, **kwargs)
            (self.root / "src/MainFile.cs").write_text("changed while building")
            return result
        with self.assertRaisesRegex(ValueError, "changed during packaging"):
            self.package(change)
        self.assertEqual(before, workflow.hashes(self.root / "workshop/prepared"))

    def test_release_rejects_changed_sources_prepared_files_and_review_copy(self):
        self.package()
        for name in ("src/MainFile.cs", "workshop/localizations/japanese.json",
                     "workshop/settings.json", "workshop/prepared/localizations/japanese.json",
                     "workshop/prepared/content/Gravity.dll", "workshop/workshop.json",
                     "workshop/prepared/release-manifest.json",
                     "workshop/content/Gravity.dll", "workshop/mod_id.txt"):
            with self.subTest(name=name):
                path = self.root / name
                before = path.read_bytes()
                path.write_bytes(before + b" changed")
                with self.assertRaises(ValueError):
                    workflow.check(self.root)
                path.write_bytes(before)
        workflow.check(self.root)

    def test_release_only_launches_prebuilt_publisher_and_forwards_language(self):
        self.package()
        with patch.object(workflow.subprocess, "run", return_value=subprocess.CompletedProcess([], 1)) as run:
            status = workflow.release(self.root, argparse.Namespace(language="japanese", dry_run=False, previews_only=False))
            self.assertEqual(status, 1)
            command = run.call_args.args[0]
            self.assertEqual(command[0], "dotnet")
            self.assertTrue(command[1].endswith("prepared/publisher/WorkshopLocalization.dll"))
            self.assertIn("publish", command)
            self.assertEqual(command[-2:], ["--language", "japanese"])
            self.assertFalse(any(word in command for word in ("run", "build", "package", "update-localizations.sh")))

    def test_preview_only_release_forwards_option_without_building(self):
        self.package()
        with patch.object(workflow.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)) as run:
            workflow.release(self.root, argparse.Namespace(language=None, dry_run=True, previews_only=True))
            self.assertEqual(run.call_args.args[0][-2:], ["--dry-run", "--previews-only"])

    def test_release_shell_dry_run_never_builds_or_invokes_updater(self):
        self.package()
        binaries = self.root / "fake-bin"
        binaries.mkdir()
        log = self.root / "calls.json"
        dotnet = binaries / "dotnet"
        dotnet.write_text(f"#!{sys.executable}\nimport json,os,sys\nfrom pathlib import Path\n"
                          "Path(os.environ['CALL_LOG']).write_text(json.dumps(sys.argv[1:]))\n")
        dotnet.chmod(0o755)
        result = subprocess.run(["bash", str(self.root / "release.sh"), "--dry-run", "--language", "japanese"],
                                capture_output=True, text=True,
                                env={**os.environ, "PATH": f"{binaries}:{os.environ['PATH']}", "CALL_LOG": str(log)})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        args = json.loads(log.read_text())
        self.assertTrue(args[0].endswith("WorkshopLocalization.dll"))
        self.assertEqual(args[-3:], ["--language", "japanese", "--dry-run"])

    def test_listing_only_changes_do_not_change_shared_content_fingerprint(self):
        self.package()
        before = workflow.read_json(self.root / "workshop/prepared/release-manifest.json")["contentFingerprint"]
        path = self.root / "workshop/localizations/japanese.json"
        listing = workflow.read_json(path)
        listing["title"] += "!"
        workflow.write_json(path, listing)
        from update_localizations import fingerprint
        records_path = self.root / "localization-review.json"
        records = workflow.read_json(records_path)
        records["translations"]["workshop/localizations/japanese.json"]["translation_sha256"] = fingerprint(listing)
        workflow.write_json(records_path, records)
        self.package()
        after = workflow.read_json(self.root / "workshop/prepared/release-manifest.json")["contentFingerprint"]
        self.assertEqual(before, after)

    def test_lock_blocks_concurrent_package_and_release(self):
        with workflow.workflow_lock(self.root):
            with self.assertRaisesRegex(ValueError, "Another package or release"):
                with workflow.workflow_lock(self.root):
                    self.fail("concurrent workflow should not enter")

    def test_install_validates_only_english_and_does_not_modify_workshop(self):
        (self.root / "localization/jpn.json").write_text("invalid translation under development")
        before = workflow.hashes(self.root / "workshop")
        binaries = self.root / "fake-bin"
        binaries.mkdir()
        dotnet = binaries / "dotnet"
        dotnet.write_text('#!/bin/sh\nif [ "$1" = "--version" ]; then echo 9.0.317; fi\n')
        dotnet.chmod(0o755)
        result = subprocess.run(["bash", str(self.root / "install.sh")], capture_output=True, text=True,
                                env={**os.environ, "PATH": f"{binaries}:{os.environ['PATH']}"})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Validated English", result.stdout)
        self.assertEqual(before, workflow.hashes(self.root / "workshop"))


class CSharpPublisherTests(unittest.TestCase):
    def test_publisher_failure_and_resume_behavior_without_steam(self):
        result = subprocess.run(["dotnet", "run", "--project", str(ROOT / "tests/WorkshopPublisherTests"),
                                 "--configuration", "Release"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("publisher regression checks passed without Steam", result.stdout)


if __name__ == "__main__":
    unittest.main()
