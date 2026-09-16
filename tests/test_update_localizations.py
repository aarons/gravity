"""Exercise the updater end to end with a fake Codex CLI, without model or Steam calls."""

import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest


ROOT = Path(__file__).resolve().parents[1]


class UpdateLocalizationsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="localization review ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for directory in ("scripts", "localization", "workshop/localizations", "src"):
            shutil.copytree(ROOT / directory, self.root / directory,
                            ignore=shutil.ignore_patterns("__pycache__"))
        # Workflow tests start with complete catalogs, even during English-only work.
        # Fill missing keys only in these temporary fixtures; keep existing translations.
        english = json.loads((self.root / "localization/eng.json").read_text())
        self.game_key = next(iter(english))
        catalog = json.loads((ROOT / "supported-languages.json").read_text())
        for game, steam in catalog.items():
            (self.root / "localization" / f"{game}.json").write_text(json.dumps(english))
            (self.root / "workshop/localizations" / f"{steam}.json").write_bytes(
                (self.root / "workshop/localizations/english.json").read_bytes())
        for filename in ("update-localizations.sh", "supported-languages.json"):
            shutil.copy2(ROOT / filename, self.root / filename)
        self.bin = self.root / "fake-bin"
        self.bin.mkdir()
        self.log = self.root / "calls.jsonl"
        self.codex = self.bin / "codex"
        self.codex.write_text(f"#!{sys.executable}\n" + r'''
import json
import fcntl
import os
from pathlib import Path
import re
import sys
import time

prompt = sys.stdin.read()
target = re.search(r"Update only (\S+) for", prompt)[1]
def event(kind):
    with open(os.environ["CALL_LOG"] + ".events", "a", encoding="utf-8") as stream:
        fcntl.flock(stream, fcntl.LOCK_EX)
        stream.write(json.dumps({"event": kind, "target": target, "pid": os.getpid()}) + "\n")
        stream.flush()

with open(os.environ["CALL_LOG"], "a", encoding="utf-8") as stream:
    fcntl.flock(stream, fcntl.LOCK_EX)
    stream.write(json.dumps({"target": target, "prompt": prompt, "args": sys.argv[1:]}) + "\n")
event("start")
if os.environ.get("WAIT_FOR_JOBS"):
    deadline = time.monotonic() + 10
    while len(Path(os.environ["CALL_LOG"]).read_text().splitlines()) < int(os.environ["WAIT_FOR_JOBS"]):
        if time.monotonic() > deadline:
            sys.exit(2)
        time.sleep(0.01)
time.sleep(float(os.environ.get("JOB_DELAY", "0")))
if target == os.environ.get("FAIL_TARGET"):
    Path(target).write_text('{"partial": "unfinished"}')
    print("usage limit reached (simulated)")
    event("failure")
    sys.exit(1)
if os.environ.get("SUCCESS_DELAY"):
    time.sleep(float(os.environ["SUCCESS_DELAY"]))
if target == os.environ.get("INVALID_TARGET"):
    Path(target).write_text('{"wrong": "keys"}')
if os.environ.get("REPAIR"):
    source = "localization/eng.json" if target.startswith("localization/") else "workshop/localizations/english.json"
    Path(target).write_bytes(Path(source).read_bytes())
if os.environ.get("CHANGE_ENGLISH"):
    source = Path("localization/eng.json")
    data = json.loads(source.read_text())
    data[next(iter(data))] = "Changed during review"
    source.write_text(json.dumps(data))
event("end")
''', encoding="utf-8")
        self.codex.chmod(0o755)
        self.env = {**os.environ, "PATH": f"{self.bin}:{os.environ['PATH']}",
                    "CALL_LOG": str(self.log), "PYTHONDONTWRITEBYTECODE": "1"}

    def run_update(self, *args, status=0, **env):
        result = subprocess.run(["bash", str(self.root / "update-localizations.sh"), *args],
                                cwd=self.temp.name, env={**self.env, **env},
                                text=True, capture_output=True)
        self.assertEqual(result.returncode, status, result.stdout + result.stderr)
        return result

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []

    def records(self):
        return json.loads((self.root / "localization-review.json").read_text())["translations"]

    def events(self):
        path = Path(str(self.log) + ".events")
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def edit(self, relative, key, value):
        path = self.root / relative
        table = json.loads(path.read_text())
        table[key] = value
        path.write_text(json.dumps(table, ensure_ascii=False), encoding="utf-8")

    def test_first_review_records_unchanged_text_and_next_run_skips_without_codex(self):
        before = (self.root / "localization/deu.json").read_bytes()
        self.run_update("--model", "test-model", "--context", "Keep it friendly. $literal")
        calls = self.calls()
        self.assertEqual(len(calls), 26)
        self.assertEqual(len(self.records()), 26)
        self.assertEqual((self.root / "localization/deu.json").read_bytes(), before)
        for call in calls:
            self.assertIn("--model", call["args"])
            self.assertIn("test-model", call["args"])
            self.assertIn("Keep it friendly. $literal", call["prompt"])
            self.assertIn("English reference (", call["prompt"])
            self.assertIn("pedantic", call["prompt"])
            self.assertIn("natural", call["prompt"])
            self.assertNotIn("$english_json", call["prompt"])
        workshop = next(call for call in calls if call["target"].endswith("latam.json"))
        self.assertIn("public Steam Workshop listing", workshop["prompt"])
        self.assertIn("workshop/localizations/english.json", workshop["prompt"])
        self.assertIn("localization/esp.json", workshop["prompt"])
        self.assertIn("7999 UTF-8 bytes", workshop["prompt"])
        state_before = (self.root / "localization-review.json").read_bytes()
        # A broken executable proves current runs never invoke Codex.
        self.codex.write_text("#!/bin/sh\nexit 99\n")
        self.run_update()
        self.run_update("--check")
        self.assertEqual(len(self.calls()), 26)
        self.assertEqual((self.root / "localization-review.json").read_bytes(), state_before)

    def test_check_is_read_only_and_reports_missing_review_state(self):
        result = self.run_update("--check", status=1)
        self.assertIn("26 translation(s) need review", result.stdout)
        self.assertFalse((self.root / "localization-review.json").exists())
        self.assertFalse(self.log.exists())

    def test_each_english_source_invalidates_only_its_own_translations(self):
        self.run_update()
        self.edit("localization/eng.json", self.game_key, "Finish")
        self.run_update()
        self.assertEqual(len(self.calls()), 39)
        self.assertTrue(all(call["target"].startswith("localization/") for call in self.calls()[26:]))
        self.edit("workshop/localizations/english.json", "description", "Updated listing description.")
        self.run_update()
        self.assertEqual(len(self.calls()), 52)
        self.assertTrue(all(call["target"].startswith("workshop/") for call in self.calls()[39:]))

    def test_translation_edit_invalidates_one_file_and_force_reviews_all(self):
        self.run_update()
        self.edit("localization/deu.json", self.game_key, "Fertig!")
        self.run_update("--check", status=1)
        self.run_update()
        self.assertEqual(len(self.calls()), 27)
        self.assertEqual(self.calls()[-1]["target"], "localization/deu.json")
        self.run_update("--force")
        self.assertEqual(len(self.calls()), 53)

    def test_timestamps_formatting_key_order_and_json_escapes_do_not_invalidate(self):
        self.run_update()
        for index, path in enumerate(self.root.glob("**/*.json")):
            if path.name in ("localization-review.json", "supported-languages.json"):
                continue
            data = json.loads(path.read_text())
            path.write_text(json.dumps(dict(reversed(list(data.items()))), ensure_ascii=True))
            os.utime(path, (index + 1, index + 1))
        self.run_update("--check")
        self.run_update()
        self.assertEqual(len(self.calls()), 26)

    def test_failure_keeps_completed_reviews_and_retry_resumes(self):
        self.run_update("--jobs", "1", status=1, FAIL_TARGET="localization/fra.json")
        self.assertEqual(set(self.records()), {"localization/deu.json", "localization/esp.json"})
        self.run_update(REPAIR="1")
        self.assertEqual(len(self.calls()), 27)  # Only the failed file was repeated.
        self.assertEqual(len(self.records()), 26)
        self.run_update("--check")

    def test_invalid_output_and_english_change_are_not_recorded(self):
        result = self.run_update("--jobs", "1", status=1, INVALID_TARGET="localization/deu.json")
        self.assertIn("missing", result.stderr)
        self.assertFalse((self.root / "localization-review.json").exists())
        result = self.run_update("--jobs", "1", status=1, CHANGE_ENGLISH="1", REPAIR="1")
        self.assertIn("English changed while reviewing", result.stderr)
        self.assertFalse((self.root / "localization-review.json").exists())

    def test_missing_translation_is_created_and_invalid_translation_is_repaired(self):
        self.run_update()
        (self.root / "workshop/localizations/german.json").unlink()
        (self.root / "localization/deu.json").write_text('{"duplicate":"a","duplicate":"b"}')
        self.run_update("--check", status=1)
        self.run_update(REPAIR="1")
        self.assertEqual(len(self.calls()), 28)
        self.run_update("--check")

    def test_invalid_english_and_unknown_locales_fail_before_model_calls(self):
        path = self.root / "workshop/localizations/english.json"
        original = path.read_bytes()
        path.write_text('{"title":"Incomplete"}')
        self.run_update(status=1)
        self.assertFalse(self.log.exists())
        path.write_bytes(original)
        (self.root / "workshop/localizations/unknown.json").write_bytes(original)
        self.run_update(status=1)
        self.assertFalse(self.log.exists())

    def test_workshop_limits_validate_even_with_matching_fingerprints(self):
        self.run_update()
        path = "workshop/localizations/german.json"
        for key, value in (("title", "界" * 43), ("description", "a" * 8000),
                           ("description", "contains\0nul")):
            with self.subTest(key=key, value=value[:20]):
                self.edit(path, key, value)
                # Even a receipt for invalid content must not bypass validation.
                import hashlib
                data = json.loads((self.root / path).read_text())
                content = json.dumps(data, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
                state_path = self.root / "localization-review.json"
                state = json.loads(state_path.read_text())
                state["translations"][path]["translation_sha256"] = hashlib.sha256(content.encode()).hexdigest()
                state_path.write_text(json.dumps(state))
                self.assertIn("missing or invalid translation", self.run_update("--check", status=1).stdout)
                self.run_update(REPAIR="1")

    def test_invalid_state_and_cli_fail_without_calls(self):
        (self.root / "localization-review.json").write_text('{"version":2,"translations":{}}')
        self.run_update(status=1)
        self.assertFalse(self.log.exists())
        for args in (("--model",), ("--context",), ("--unknown",),
                     ("--model", ""), ("--context", " "), ("--jobs",),
                     ("--jobs", "0"), ("--jobs", "-1"), ("--jobs", "1.5")):
            self.run_update(*args, status=2)
        self.run_update("--help")

    def assert_parallel_run(self, jobs, *args):
        self.run_update(*args, WAIT_FOR_JOBS=str(jobs), JOB_DELAY="0.08")
        active = set()
        completed_game = set()
        maximum = 0
        for event in self.events():
            target = event["target"]
            if event["event"] == "start":
                if target.startswith("workshop/"):
                    self.assertEqual(len(completed_game), 13)
                active.add(target)
                maximum = max(maximum, len(active))
            else:
                active.remove(target)
                if target.startswith("localization/"):
                    completed_game.add(target)
        self.assertFalse(active)
        self.assertEqual(maximum, jobs)
        self.assertEqual(len(self.records()), 26)
        self.run_update("--check")

    def test_default_runs_four_jobs_and_waits_for_game_phase(self):
        self.assert_parallel_run(4)

    def test_concurrency_can_be_reduced_to_two(self):
        self.assert_parallel_run(2, "--jobs", "2")

    def test_parallel_failure_stops_queue_drains_successes_and_resumes(self):
        result = self.run_update(status=1, FAIL_TARGET="localization/deu.json",
                                 WAIT_FOR_JOBS="4", SUCCESS_DELAY="0.2")
        self.assertIn("usage limit reached", result.stdout)
        self.assertEqual(len(self.calls()), 4)
        self.assertEqual(set(self.records()), {
            "localization/esp.json", "localization/fra.json", "localization/ita.json"})
        self.run_update("--jobs", "2", REPAIR="1")
        self.assertEqual(len(self.calls()), 27)
        self.run_update("--check")

    def test_parallel_invalid_output_stops_queue(self):
        self.run_update(status=1, INVALID_TARGET="localization/deu.json", WAIT_FOR_JOBS="4",
                        JOB_DELAY="0.1")
        self.assertNotIn("localization/deu.json", self.records())
        self.assertTrue(all(call["target"].startswith("localization/") for call in self.calls()))

    def test_parallel_source_change_prevents_receipts_and_more_jobs(self):
        # One process changes English; its siblings wait until that change is visible.
        self.codex.write_text(self.codex.read_text().replace(
            'if os.environ.get("CHANGE_ENGLISH"):',
            'if os.environ.get("CHANGE_ENGLISH") and target == "localization/deu.json":'
        ).replace('event("end")', 'time.sleep(0.2)\nevent("end")'))
        self.run_update(status=1, CHANGE_ENGLISH="1", WAIT_FOR_JOBS="4")
        self.assertEqual(len(self.calls()), 4)
        self.assertFalse((self.root / "localization-review.json").exists())

    def test_interrupt_stops_children_and_concurrent_updater_is_rejected(self):
        process = subprocess.Popen(["bash", str(self.root / "update-localizations.sh")],
                                   cwd=self.root, env={**self.env, "JOB_DELAY": "30"},
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            deadline = time.monotonic() + 10
            while len(self.events()) < 4 and time.monotonic() < deadline:
                time.sleep(0.02)
            self.assertEqual(len(self.events()), 4)
            result = self.run_update(status=1)
            self.assertIn("Another localization update is running", result.stderr)
            self.assertEqual(len(self.calls()), 4)
            process.send_signal(signal.SIGINT)
            stdout, stderr = process.communicate(timeout=10)
            self.assertEqual(process.returncode, 130, stdout + stderr)
            for event in self.events():
                with self.assertRaises(ProcessLookupError):
                    os.kill(event["pid"], 0)
            self.assertFalse((self.root / "localization-review.json").exists())
            self.run_update("--jobs", "2")  # Lock is released after interruption.
        finally:
            if process.poll() is None:
                process.send_signal(signal.SIGINT)
                process.communicate(timeout=10)


if __name__ == "__main__":
    unittest.main()
