"""Exercise the offline C# command with isolated workspaces; never invoke Steam."""

import json
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "tools/WorkshopLocalization"


class WorkshopLocalizationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release", "--nologo"],
                       check=True, capture_output=True, text=True)
        cls.command = ["dotnet", str(PROJECT / "bin/Release/net9.0/WorkshopLocalization.dll")]

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="workshop localization ")
        self.addCleanup(self.temp.cleanup)
        self.workspace = Path(self.temp.name)
        self.localizations = self.workspace / "localizations"
        self.localizations.mkdir()
        self.write_listing("english")
        (self.workspace / "mod_id.txt").write_text("123456789\n")

    def write_listing(self, language, **overrides):
        data = {"title": "My Mod", "description": "First paragraph.\n\nSecond paragraph."}
        data.update(overrides)
        (self.localizations / f"{language}.json").write_text(json.dumps(data), encoding="utf-8")

    def run_command(self, *args, success=True):
        result = subprocess.run([*self.command, *args, "--workspace", str(self.workspace)],
                                capture_output=True, text=True, cwd=self.workspace)
        self.assertEqual(result.returncode, 0 if success else 1, result.stdout + result.stderr)
        return result

    def test_dry_run_preserves_text_and_does_not_write(self):
        text = "本模组显示遭遇数量。\n\n[gold]金色数字[/gold]"
        self.write_listing("schinese", title="示例模组", description=text)
        before = {p.relative_to(self.workspace): p.read_bytes()
                  for p in self.workspace.rglob("*") if p.is_file()}
        result = self.run_command("dry-run", "--language", "schinese")
        plan = json.loads(result.stdout)
        self.assertEqual(plan["publishedFileId"], "123456789")
        self.assertEqual(plan["appId"], 2868840)
        self.assertEqual(plan["updates"], [dict(language="schinese", gameLanguage="zhs",
                                             title="示例模组", description=text)])
        self.assertEqual(len(plan["missingLanguages"]), 12)
        after = {p.relative_to(self.workspace): p.read_bytes()
                 for p in self.workspace.rglob("*") if p.is_file()}
        self.assertEqual(before, after)

    def test_all_languages_and_spanish_mapping(self):
        catalog = json.loads((ROOT / "supported-languages.json").read_text())
        for language in catalog.values():
            self.write_listing(language)
        plan = json.loads(self.run_command("dry-run", "--require-all").stdout)
        self.assertEqual(len(plan["updates"]), 14)
        mapping = {item["gameLanguage"]: item["language"] for item in plan["updates"]}
        self.assertEqual(mapping["esp"], "latam")
        self.assertEqual(mapping["spa"], "spanish")
        self.assertEqual(plan["missingLanguages"], [])

    def test_partial_coverage_is_explicit(self):
        self.assertIn("1/14", self.run_command("validate").stdout)
        self.assertIn("Missing Workshop translations", self.run_command("validate", "--require-all", success=False).stderr)
        self.run_command("dry-run", "--language", "japanese", success=False)
        (self.localizations / "english.json").unlink()
        self.assertIn("english.json is required", self.run_command("validate", success=False).stderr)

    def test_item_id(self):
        path = self.workspace / "mod_id.txt"
        path.unlink()
        self.run_command("validate")
        self.run_command("dry-run", success=False)
        for text in ("0", "-1", "+123", "1.2", "garbage", "18446744073709551616"):
            with self.subTest(text=text):
                path.write_text(text)
                self.run_command("validate", success=False)
        path.write_text("18446744073709551615")
        self.assertEqual(json.loads(self.run_command("dry-run").stdout)["publishedFileId"], path.read_text())

    def test_rejects_invalid_files_even_if_unselected(self):
        path = self.localizations / "schinese.json"
        for text in ('{', '[]', '{"title":"A","title":"B","description":"C"}',
                     '{"title":"A"}', '{"title":null,"description":"C"}',
                     '{"title":"A","description":"C","tags":[]}'):
            with self.subTest(text=text):
                path.write_text(text)
                self.run_command("dry-run", "--language", "english", success=False)
        path.unlink()
        for language in ("tchinese", "eng", "German"):
            self.write_listing(language)
            self.assertIn("unsupported locale", self.run_command("validate", success=False).stderr)
            (self.localizations / f"{language}.json").unlink()

    def test_utf8_limits_blank_and_nul(self):
        self.write_listing("english", title="界" * 42 + "ab", description="a" * 7999)
        self.run_command("validate")
        for data in (dict(title="界" * 43), dict(description="a" * 8000),
                     dict(title=" \n"), dict(description="x\0y")):
            with self.subTest(data=str(data)[:80]):
                self.write_listing("english", **data)
                self.run_command("validate", success=False)

    def test_invalid_command_line(self):
        for args in (("upload",), ("validate", "--language", "english"),
                     ("validate", "--typo"), ("dry-run", "--language"),
                     ("validate", "--require-all", "--require-all")):
            with self.subTest(args=args):
                self.run_command(*args, success=False)



if __name__ == "__main__":
    unittest.main()
