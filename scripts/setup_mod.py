"""Name a fresh copy of the template; uses only Python's standard library."""

import argparse
import json
from pathlib import Path
import re
import sys

from validate_localization import validate_workshop_table

ROOT = Path(__file__).resolve().parents[1]


def setup(root, mod_id, name, author, description):
    if not re.fullmatch(r"[A-Z][A-Za-z0-9_]{0,63}", mod_id) or mod_id == "MyMod":
        raise ValueError("Choose an ID other than MyMod: start with A-Z, then letters, digits or underscores (max 64).")
    if any(not value.strip() or "\0" in value for value in (name, author, description)):
        raise ValueError("Name, author and description must be nonempty text without NUL.")
    if not (root / "MyMod.csproj").is_file():
        raise ValueError("This copy has already been named, or is not a fresh template.")
    for relative in (f"{mod_id}.csproj", f"{mod_id}.json", "workshop/mod_id.txt",
                     "workshop/prepared", "workshop/first-upload"):
        if (root / relative).exists():
            raise ValueError(f"Use a fresh template copy; {relative} already exists.")
    listing = {"title": name, "description": description}
    validate_workshop_table(listing, "Workshop listing")
    metadata = json.loads((root / "MyMod.json").read_text(encoding="utf-8"))
    metadata.update(id=mod_id, name=name, author=author, description=description)

    # Change only template-owned text. No Git history, dependencies or generated output.
    candidates = [root / "MyMod.csproj", root / "README.md", *root.glob("*.sh")]
    for directory in ("src", "scripts", "tests"):
        candidates.extend(p for p in (root / directory).rglob("*")
                          if p.suffix in (".cs", ".csproj", ".py", ".md")
                          and not any(part in ("bin", "obj", "__pycache__") for part in p.parts)
                          and p.name not in ("setup_mod.py", "test_template.py"))
    replacements = {p: p.read_text(encoding="utf-8").replace("MyMod", mod_id) for p in candidates}
    for path, text in replacements.items():
        path.write_text(text, encoding="utf-8")
    (root / "MyMod.csproj").rename(root / f"{mod_id}.csproj")
    (root / f"{mod_id}.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (root / "MyMod.json").unlink()
    (root / "workshop/localizations/english.json").write_text(
        json.dumps(listing, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    # Any prior reviews refer to the placeholder listing, not this mod.
    (root / "localization-review.json").unlink(missing_ok=True)
    print(f"Named this mod {name} ({mod_id}). Next: edit README.md and src/, then run ./install.sh.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--id", required=True, help="Unique C# identifier, e.g. ExampleMod")
    parser.add_argument("--name", required=True, help="Player-facing name")
    parser.add_argument("--author", required=True)
    parser.add_argument("--description", required=True)
    args = parser.parse_args()
    try:
        setup(ROOT, args.id, args.name, args.author, args.description)
    except (ValueError, OSError) as error:
        print(f"Setup failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
