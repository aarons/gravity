"""Review stale translations with Codex and persist content-based review receipts."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
from string import Template
import subprocess
import sys
import tempfile

from validate_localization import (
    read_table, supported_languages, unique_keys, validate_english_keys,
    validate_game_table, validate_workshop_table,
)


ROOT = Path(__file__).resolve().parents[1]
STATE_FILE = "localization-review.json"
SOURCES = {
    "game": "localization/eng.json",
    "workshop": "workshop/localizations/english.json",
}


def fingerprint(table):
    # Ignore JSON indentation, key order, and equivalent Unicode escape spelling.
    content = json.dumps(table, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
    return hashlib.sha256(content.encode("utf-8")).hexdigest()


def read_state(root):
    path = root / STATE_FILE
    if not path.exists():
        return {"version": 1, "translations": {}}
    state = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_keys)
    if (not isinstance(state, dict) or set(state) != {"version", "translations"}
            or type(state["version"]) is not int or state["version"] != 1
            or not isinstance(state["translations"], dict)):
        raise ValueError(f"{STATE_FILE}: expected version 1 and a translations object")
    for target, record in state["translations"].items():
        if (not isinstance(record, dict)
                or set(record) != {"english_sha256", "translation_sha256"}
                or any(not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{64}", value)
                       for value in record.values())):
            raise ValueError(f"{STATE_FILE}: invalid fingerprint record for {target}")
    return state


def write_state(root, state):
    # Replace atomically so interruption cannot leave a partially written receipt.
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=root,
                                         prefix=".localization-review-", delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(state, stream, ensure_ascii=False, indent=2, sort_keys=True)
            stream.write("\n")
        temporary.replace(root / STATE_FILE)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def read_sources(root):
    sources = {scope: read_table(root / path) for scope, path in SOURCES.items()}
    validate_english_keys(root, sources["game"])
    validate_workshop_table(sources["workshop"], SOURCES["workshop"])
    return sources


def targets(root, catalog):
    result = []
    for scope, source in SOURCES.items():
        directory = Path(source).parent
        supported = set(catalog if scope == "game" else catalog.values())
        extra = [p.name for p in (root / directory).iterdir()
                 if p.suffix.lower() == ".json"
                 and (p.stem not in supported or p.suffix != ".json")]
        if extra:
            raise ValueError(f"{directory}: unsupported locale filenames: {sorted(extra)}")
        for game, steam in sorted(catalog.items()):
            if game != "eng":
                language = game if scope == "game" else steam
                result.append((scope, str(directory / f"{language}.json"), game, steam))
    return result


def read_translation(root, scope, target, english):
    table = read_table(root / target)
    if scope == "game":
        validate_game_table(table, english, target)
    else:
        validate_workshop_table(table, target)
    return table


def review_reason(root, scope, target, english, records, force=False):
    try:
        table = read_translation(root, scope, target, english)
    except (ValueError, OSError) as error:
        return f"missing or invalid translation: {error}"
    if force:
        return "forced review"
    record = records.get(target)
    if record is None:
        return "no previous review recorded"
    if record["english_sha256"] != fingerprint(english):
        return "English content changed"
    if record["translation_sha256"] != fingerprint(table):
        return "translation content changed since review"
    return None


def make_prompt(root, scope, target, game, steam, english, context):
    template = (root / "scripts/prompts" / f"{scope}-localization.md").read_text(encoding="utf-8")
    return Template(template).substitute(
        target=target, source=SOURCES[scope], game_language=game, steam_language=steam,
        english_json=json.dumps(english, ensure_ascii=False, indent=2), context=context,
    )


def run(root, args):
    catalog = supported_languages(root)
    sources = read_sources(root)  # Fail before model calls if either English source is invalid.
    state = read_state(root)
    entries = targets(root, catalog)
    pending = []
    for scope, target, game, steam in entries:
        reason = review_reason(root, scope, target, sources[scope], state["translations"], args.force)
        if reason:
            print(f"Review {target}: {reason}", flush=True)
            pending.append((scope, target, game, steam))
        else:
            print(f"Skip {target}: validated and reviewed against current English", flush=True)

    print(f"{len(pending)} translation(s) need review; {len(entries) - len(pending)} current.", flush=True)
    if args.check:
        return 1 if pending else 0
    if pending:
        codex = shutil.which("codex")
        if codex is None:
            raise ValueError("Codex CLI is required to review stale translations; install and authenticate it first")
        command = [codex, "exec", "--cd", str(root), "--sandbox", "workspace-write"]
        if args.model:
            command.extend(["--model", args.model])
        command.append("-")

    for scope, target, game, steam in pending:
        english = sources[scope]
        if read_sources(root) != sources:
            raise ValueError("English changed during this run; rerun to review the new source")
        prompt = make_prompt(root, scope, target, game, steam, english, args.context)
        print(f"Reviewing {target}...", flush=True)
        result = subprocess.run(command, input=prompt, text=True, encoding="utf-8", cwd=root)
        if result.returncode:
            raise ValueError(f"Codex failed for {target}; review partial edits before rerunning")
        if read_sources(root) != sources:
            raise ValueError(f"English changed while reviewing {target}; no review recorded for this file")
        table = read_translation(root, scope, target, english)
        state["translations"][target] = {
            "english_sha256": fingerprint(english),
            "translation_sha256": fingerprint(table),
        }
        write_state(root, state)

    # Recheck skipped and completed files, including changes made during later reviews.
    final_sources = read_sources(root)
    for scope, target, _, _ in targets(root, catalog):
        reason = review_reason(root, scope, target, final_sources[scope], state["translations"])
        if reason:
            raise ValueError(f"{target} still needs review: {reason}")
    print("All in-game and Workshop translations are validated and current.", flush=True)
    if pending:
        print(f"Review git diff -- localization/ workshop/localizations/ {STATE_FILE}", flush=True)
        print("Include new translation files and commit the review record with the translations.", flush=True)
    return 0


def nonempty(value):
    if not value.strip():
        raise argparse.ArgumentTypeError("requires a nonempty value")
    return value


def main():
    parser = argparse.ArgumentParser(prog="./update-localizations.sh", description=(
        "Review stale in-game and Workshop translations in separate Codex runs, then validate. "
        "Edits and content fingerprints remain in the working tree for review."))
    parser.add_argument("--context", type=nonempty, default="No additional release context supplied.",
                        help="extra translation context; use --force to also review current files")
    parser.add_argument("--model", type=nonempty, help="override the configured Codex model")
    parser.add_argument("--force", action="store_true", help="review every non-English translation")
    parser.add_argument("--check", action="store_true",
                        help="validate and report freshness without edits or Codex; exit 1 if review is needed")
    args = parser.parse_args()
    try:
        return run(ROOT, args)
    except (ValueError, OSError) as error:
        print(f"Localization update failed: {error}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Localization update interrupted; completed reviews are saved. Review partial edits before rerunning.",
              file=sys.stderr)
        return 130


if __name__ == "__main__":
    sys.exit(main())
