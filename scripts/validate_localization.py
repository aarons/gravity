"""Validate translation files and localization keys using only Python's standard library."""

import json
from pathlib import Path
import re
import sys


def unique_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate key: {key}")
        result[key] = value
    return result


def supported_languages(root=None):
    root = root or Path(__file__).resolve().parents[1]
    path = root / "supported-languages.json"
    catalog = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_keys)
    if not isinstance(catalog, dict) or len(catalog) != 14 or any(
        not re.fullmatch(r"[a-z]{3}", game)
        or not isinstance(steam, str) or not re.fullmatch(r"[a-z]+", steam)
        for game, steam in catalog.items()
    ) or len(set(catalog.values())) != 14 or catalog.get("eng") != "english":
        raise ValueError("supported-languages.json must map the 14 game locales to unique Steam codes")
    return catalog


def read_table(path):
    table = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_keys)
    if not isinstance(table, dict) or not table:
        raise ValueError(f"{path}: expected a nonempty object")
    for key, value in table.items():
        if not isinstance(value, str) or not value.strip():
            raise ValueError(f"{path}: {key} must contain nonempty text")
    return table


def validate_game_table(table, english, source):
    missing, extra = english.keys() - table.keys(), table.keys() - english.keys()
    if missing or extra:
        raise ValueError(f"{source}: missing {sorted(missing)}, extra {sorted(extra)}")


def validate_english_keys(root, english):
    used = set()
    for source in (root / "src").rglob("*.cs"):
        used.update(re.findall(r'Localize\("([^"]+)"\)', source.read_text(encoding="utf-8")))
    expected = set(english)
    if used != expected:
        raise ValueError(f"Localization keys: missing translations {sorted(used - expected)}, "
                         f"unused translations {sorted(expected - used)}")


def validate_workshop_table(table, source):
    if set(table) != {"title", "description"}:
        raise ValueError(f"{source}: expected exactly title and description")
    # Match tools/WorkshopLocalization/ListingFiles.cs, including UTF-8 byte limits.
    for key, limit in (("title", 128), ("description", 7999)):
        if "\0" in table[key] or len(table[key].encode("utf-8")) > limit:
            raise ValueError(f"{source}: {key} must have no NUL and at most {limit} UTF-8 bytes")


def validate(root=None):
    root = root or Path(__file__).resolve().parents[1]
    translations = {}
    for path in sorted((root / "localization").glob("*.json")):
        translations[path.stem] = read_table(path)

    if "eng" not in translations:
        raise ValueError("eng.json is required for fallback text")
    supported = supported_languages(root)
    missing_languages = supported.keys() - translations.keys()
    extra_languages = translations.keys() - supported.keys()
    if missing_languages or extra_languages:
        raise ValueError(f"Languages: missing {sorted(missing_languages)}, "
                         f"unsupported {sorted(extra_languages)}")
    expected = set(translations["eng"])
    for language, table in translations.items():
        validate_game_table(table, translations["eng"], language)

    validate_english_keys(root, translations["eng"])
    print(f"Validated {len(expected)} localization keys in {len(translations)} languages.")


if __name__ == "__main__":
    try:
        if sys.argv[1:] == ["--english-only"]:
            root = Path(__file__).resolve().parents[1]
            validate_english_keys(root, read_table(root / "localization/eng.json"))
            print("Validated English localization keys. Other translations were not checked or changed.")
        elif sys.argv[1:]:
            raise ValueError("Usage: validate_localization.py [--english-only]")
        else:
            validate()
    except (ValueError, OSError) as error:
        print(f"Localization validation failed: {error}", file=sys.stderr)
        sys.exit(1)
