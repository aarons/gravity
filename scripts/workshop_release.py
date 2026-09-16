"""Freeze and check releases. Publishing never builds or invokes translation models."""

import argparse
from contextlib import contextmanager
import fcntl
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
from zipfile import BadZipFile, ZIP_DEFLATED, ZipFile

from update_localizations import read_sources, read_state, review_reason, targets
from validate_localization import supported_languages, unique_keys, validate
from steam_runtime import ensure_uploader


ROOT = Path(__file__).resolve().parents[1]
APP_ID = 2868840


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_keys)


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def hashes(directory):
    return {p.relative_to(directory).as_posix(): digest(p.read_bytes())
            for p in sorted(directory.rglob("*")) if p.is_file()}


@contextmanager
def workflow_lock(root):
    with (root / "workshop/.workflow.lock").open("a") as stream:
        try:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise ValueError("Another prepare or release is running in this checkout") from None
        yield


def inputs(root):
    paths = set()
    for pattern in ("*.csproj", "*.props", "*.sh", "scripts/prompts/*.md", "NuGet.Config", "global.json", "supported-languages.json",
                    "localization-review.json", "Gravity.json", "src/**/*.cs",
                    "localization/*.json", "scripts/*.py", "workshop/settings.json",
                    "workshop/image.png", "workshop/localizations/*.json",
                    "tools/WorkshopLocalization/*.cs", "tools/WorkshopLocalization/*.csproj",
                    "tools/WorkshopLocalization/vendor/*", "references/ModUploader-osx-arm64/libsteam_api.dylib"):
        paths.update(root.glob(pattern))
    paths.update(p for p in (root / "workshop/previews").glob("*") if p.name != ".DS_Store")
    return {p.relative_to(root).as_posix(): digest(p.read_bytes()) for p in sorted(paths) if p.is_file()}


def item_id(path):
    if not path.exists():
        return None
    value = path.read_text().strip()
    if not re.fullmatch(r"[0-9]+", value) or not 0 < int(value) < 2**64:
        raise ValueError(f"{path}: expected a nonzero uint64 Workshop item ID")
    return str(int(value))


def identity(root, recover=False):
    workspace = root / "workshop"
    item = item_id(workspace / "mod_id.txt")
    legacy = item_id(workspace / "first-upload/mod_id.txt")
    receipt_path = workspace / ".release-state/creation.json"
    receipt = read_json(receipt_path) if receipt_path.exists() else None
    if receipt is not None:
        if (not isinstance(receipt, dict) or receipt.get("version") != 1
                or type(receipt.get("completed")) is not bool
                or not isinstance(receipt.get("owner"), str)
                or not re.fullmatch(r"[0-9]+", receipt["owner"])
                or not 0 < int(receipt["owner"]) < 2**64
                or (receipt["completed"] and receipt.get("itemId") is None)):
            raise ValueError("Invalid Workshop creation receipt")
    saved = receipt.get("itemId") if receipt is not None else None
    if saved is not None and (not isinstance(saved, str) or not re.fullmatch(r"[0-9]+", saved)
                              or not 0 < int(saved) < 2**64):
        raise ValueError("Invalid saved Workshop item ID")
    ids = {value for value in (item, legacy, saved) if value is not None}
    if len(ids) > 1:
        raise ValueError("Workshop item IDs disagree; preserve them and resolve the mismatch before releasing")
    known = next(iter(ids), None)
    if recover and item is None and known is not None:
        with (workspace / "mod_id.txt").open("x") as stream:
            stream.write(known + "\n")
        print(f"Recovered Workshop item ID {known} into workshop/mod_id.txt")
    return known, receipt


def preflight(root):
    validate(root)
    catalog = supported_languages(root)
    sources = read_sources(root)
    records = read_state(root)["translations"]
    stale = []
    for scope, target, _, _ in targets(root, catalog):
        reason = review_reason(root, scope, target, sources[scope], records)
        if reason:
            stale.append(f"{target}: {reason}")
    if stale:
        raise ValueError("Translations need review; run ./update-localizations.sh first:\n" + "\n".join(stale))
    settings = read_json(root / "workshop/settings.json")
    allowed = {"visibility", "changeNote", "tags", "dependencies", "contentDescriptors", "minBranch", "maxBranch"}
    if not isinstance(settings, dict) or set(settings) - allowed:
        raise ValueError("workshop/settings.json contains unsupported fields; title/description belong in localizations/english.json")
    if settings.get("visibility") not in ("public", "private", "unlisted", "friends_only"):
        raise ValueError("settings.json requires visibility: public, private, unlisted, or friends_only")
    for key in ("changeNote", "minBranch", "maxBranch"):
        if key in settings and (not isinstance(settings[key], str) or "\0" in settings[key]):
            raise ValueError(f"settings.json: {key} must be a string without NUL")
    for key in ("tags", "contentDescriptors", "dependencies"):
        if key in settings and not isinstance(settings[key], list):
            raise ValueError(f"settings.json: {key} must be an array")
    if any(not isinstance(t, str) or not t.strip() or "," in t or "\0" in t for t in settings.get("tags", [])):
        raise ValueError("settings.json: tags must be nonempty strings without commas or NUL")
    descriptors = {"nudity", "frequent_violence", "adult_only", "gratuitous_nudity", "general_mature"}
    if any(not isinstance(d, str) or d not in descriptors for d in settings.get("contentDescriptors", [])):
        raise ValueError("settings.json: unsupported content descriptor")
    if any(type(d) is not int or not 0 < d < 2**64 for d in settings.get("dependencies", [])):
        raise ValueError("settings.json: dependencies must be nonzero uint64 IDs")
    item, receipt = identity(root, recover=True)
    if item is None or (receipt and not receipt["completed"]):
        settings["visibility"] = "private"
    images = [root / "workshop/image.png"]
    for path in (root / "workshop/previews").glob("*"):
        if path.name == ".DS_Store":
            continue
        if not path.is_file() or path.suffix.lower() not in (".png", ".jpg", ".jpeg", ".gif"):
            raise ValueError(f"Preview is not a supported image: {path}")
        images.append(path)
    for path in images:
        if not path.is_file():
            raise ValueError(f"Add your Workshop image before preparation: {path}")
        if not 16 <= path.stat().st_size < 1_000_000:
            raise ValueError(f"Image must be at least 16 bytes and less than 1 MB: {path}")
    return {**sources["workshop"], **settings}, item


def archive(root, content):
    if not content.exists():
        return
    version = read_json(content / "Gravity.json")["version"]
    if not isinstance(version, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._+-]*", version):
        raise ValueError(f"Invalid archive version: {version}")
    files = {name: (content / name).read_bytes() for name in ("Gravity.dll", "Gravity.json")}
    directory = root / "archive"
    directory.mkdir(exist_ok=True)
    number = 1
    while True:
        suffix = "" if number == 1 else f"-{number}"
        path = directory / f"Gravity-{version}{suffix}.zip"
        try:
            output = ZipFile(path, "x", compression=ZIP_DEFLATED)
        except FileExistsError:
            try:
                with ZipFile(path) as previous:
                    if set(previous.namelist()) == set(files) and all(previous.read(k) == v for k, v in files.items()):
                        return
            except BadZipFile:
                pass
            number += 1
            continue
        try:
            with output:
                for name, data in files.items():
                    output.writestr(name, data)
        except BaseException:
            path.unlink()
            raise
        print(f"Archived release: {path}", flush=True)
        return


def prepare(root, build_args):
    workspace = root / "workshop"
    manifest, item = preflight(root)
    native = root / "references/ModUploader-osx-arm64/libsteam_api.dylib"
    if not native.is_file():
        ensure_uploader(root)
    before = inputs(root)
    # Use one filesystem so promotion and rollback are directory renames.
    with tempfile.TemporaryDirectory(prefix=".prepare-", dir=workspace) as temp:
        temporary = Path(temp)
        build = temporary / "build"
        prepared = temporary / "prepared"
        prepared.mkdir()
        subprocess.run(["dotnet", "build", str(root / "Gravity.csproj"), *build_args,
                        "--configuration", "Release", "--output", str(build), "-p:InstallMod=false"], check=True)
        (prepared / "content").mkdir()
        shutil.copy2(build / "Gravity.dll", prepared / "content")
        shutil.copy2(root / "Gravity.json", prepared / "content")
        subprocess.run(["dotnet", "build", str(root / "tools/WorkshopLocalization/WorkshopLocalization.csproj"),
                        "--configuration", "Release", "--output", str(prepared / "publisher")], check=True)
        # Match the native library already used by the official macOS uploader.
        shutil.copy2(root / "references/ModUploader-osx-arm64/libsteam_api.dylib", prepared / "publisher")
        (prepared / "publisher/steam_appid.txt").write_text(f"{APP_ID}\n")
        write_json(prepared / "workshop.json", manifest)
        if item is not None:
            (prepared / "mod_id.txt").write_text(item + "\n")
        shutil.copytree(workspace / "localizations", prepared / "localizations", ignore=shutil.ignore_patterns(".DS_Store"))
        shutil.copy2(workspace / "image.png", prepared)
        (prepared / "previews").mkdir()
        for path in (workspace / "previews").glob("*"):
            if path.name != ".DS_Store":
                shutil.copy2(path, prepared / "previews")
        if before != inputs(root) or identity(root)[0] != item:
            raise ValueError("Release inputs changed during preparation; run ./prepare.sh again")
        files = hashes(prepared)
        shared = {k: v for k, v in files.items() if k.startswith(("content/", "previews/")) or k == "image.png"}
        shared["settings"] = {k: v for k, v in manifest.items() if k not in ("title", "description")}
        content_hash = digest(json.dumps(shared, sort_keys=True, ensure_ascii=False).encode())
        write_json(prepared / "release-manifest.json", {
            "version": 2, "appId": APP_ID, "publishedFileId": item,
            "contentFingerprint": content_hash, "files": files, "inputs": before,
        })
        (temporary / ".prepared-manifest.sha256").write_text(digest((prepared / "release-manifest.json").read_bytes()) + "\n")
        archive(root, workspace / "content")
        archive(root, prepared / "content")
        # Prepare review copies before replacing any previous output.
        shutil.copytree(prepared / "content", temporary / "content")
        shutil.copy2(prepared / "workshop.json", temporary / "workshop.json")
        promoted, saved = [], []
        try:
            for name in ("content", "workshop.json", "prepared", ".prepared-manifest.sha256"):
                destination = workspace / name
                if destination.exists():
                    destination.rename(temporary / f"old-{name}")
                    saved.append(name)
                (temporary / name).rename(destination)
                promoted.append(name)
        except BaseException:
            for name in reversed(promoted):
                (workspace / name).rename(temporary / name)
            for name in reversed(saved):
                (temporary / f"old-{name}").rename(workspace / name)
            raise
    print("Preparation ready: workshop/workshop.json (English), workshop/content/, and workshop/prepared/.")
    print("Review the English manifest and prepared/localizations/. ./release.sh --dry-run checks the snapshot offline.")
    print("Preparation does not install or publish anything.")


def check(root):
    workspace = root / "workshop"
    prepared = workspace / "prepared"
    if digest((prepared / "release-manifest.json").read_bytes()) != (workspace / ".prepared-manifest.sha256").read_text().strip():
        raise ValueError("Prepared release manifest changed; run ./prepare.sh again")
    manifest = read_json(prepared / "release-manifest.json")
    if manifest.get("version") != 2 or manifest.get("appId") != APP_ID:
        raise ValueError("Unsupported prepared release; run ./prepare.sh again")
    item, _ = identity(root)
    if manifest["publishedFileId"] is not None and manifest["publishedFileId"] != item:
        raise ValueError("Workshop item ID changed after preparation; run ./prepare.sh again")
    if manifest["inputs"] != inputs(root):
        raise ValueError("Release inputs changed after preparation; run ./prepare.sh again and review the new prepared release")
    actual = hashes(prepared)
    actual.pop("release-manifest.json")
    if actual != manifest["files"]:
        raise ValueError("Prepared files changed or are missing; run ./prepare.sh again")
    if hashes(workspace / "content") != hashes(prepared / "content") or (
            (workspace / "workshop.json").read_bytes() != (prepared / "workshop.json").read_bytes()):
        raise ValueError("The Workshop review copy differs from the snapshot; edit sources and run ./prepare.sh again")
    return prepared


def release(root, args):
    prepared = check(root)
    command = ["dotnet", str(prepared / "publisher/WorkshopLocalization.dll"), "publish",
               "--workspace", str(prepared), "--state-directory", str(root / "workshop/.release-state"),
               "--item-directory", str(root / "workshop")]
    if args.language:
        command.extend(["--language", args.language])
    if args.dry_run:
        command.append("--dry-run")
    if args.previews_only:
        command.append("--previews-only")
    # Launch only the already-built publisher. Never dotnet run/build or an updater.
    return subprocess.run(command, cwd=prepared / "publisher").returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("prepare").add_argument("build_args", nargs=argparse.REMAINDER)
    publish = commands.add_parser("release")
    selection = publish.add_mutually_exclusive_group()
    selection.add_argument("--language", help="publish only this Steam language, without uploading shared content")
    selection.add_argument("--previews-only", action="store_true", help="reupload the shared gallery in filename order without content or listing updates")
    publish.add_argument("--dry-run", action="store_true", help="check and describe the frozen snapshot without Steam")
    commands.add_parser("check")
    args = parser.parse_args()
    try:
        with workflow_lock(ROOT):
            if args.command == "prepare":
                build_args = args.build_args
                if build_args[:1] == ["--"]:
                    build_args = build_args[1:]
                prepare(ROOT, build_args)
            elif args.command == "release":
                return release(ROOT, args)
            else:
                check(ROOT)
                print("Prepared release is intact and current.")
        return 0
    except (ValueError, OSError, KeyError, subprocess.CalledProcessError) as error:
        print(f"Workshop {args.command} failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
