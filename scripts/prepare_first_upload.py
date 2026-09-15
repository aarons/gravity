"""Prepare a private English workspace for creating a new Workshop item."""

from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile

from update_localizations import read_sources
from workshop_release import ROOT, write_json, workflow_lock


def prepare(root, build_args):
    workspace = root / "workshop"
    destination = workspace / "first-upload"
    if (workspace / "mod_id.txt").exists() or destination.exists():
        raise ValueError("A Workshop ID or first-upload workspace already exists. Reuse it; see README.md before retrying.")
    sources = read_sources(root)
    image = workspace / "image.png"
    if not image.is_file() or not 16 <= image.stat().st_size < 1_000_000:
        raise ValueError("Add workshop/image.png (less than 1 MB) before preparing the first upload.")
    uploader = root / "references/ModUploader-osx-arm64/ModUploader"
    if not uploader.is_file():
        raise ValueError("Extract the complete official uploader to references/ModUploader-osx-arm64/ first.")
    with tempfile.TemporaryDirectory(prefix=".first-upload-", dir=workspace) as temp:
        temporary = Path(temp)
        build = temporary / "build"
        subprocess.run(["dotnet", "build", str(root / "Gravity.csproj"), *build_args,
                        "--configuration", "Release", "--output", str(build), "-p:InstallMod=false"], check=True)
        prepared = temporary / "workspace"
        content = prepared / "content"
        content.mkdir(parents=True)
        shutil.copy2(build / "Gravity.dll", content)
        shutil.copy2(root / "Gravity.json", content)
        shutil.copy2(image, prepared / "image.png")
        write_json(prepared / "workshop.json", {**sources["workshop"], "visibility": "private",
                                               "tags": [], "dependencies": [], "contentDescriptors": []})
        prepared.rename(destination)
    print(f"Prepared private first-upload workspace: {destination}")
    print("Review its content and workshop.json, then run the official uploader:")
    print(f"(cd {shlex.quote(str(uploader.parent))} && ./ModUploader upload -w {shlex.quote(str(destination))})")
    print("After success, save the returned item ID for all subsequent releases:")
    print(shlex.join(["cp", str(destination / "mod_id.txt"), str(workspace / "mod_id.txt")]))
    print("Then run ./package.sh, review ./release.sh --dry-run, and publish with ./release.sh.")


def main():
    try:
        with workflow_lock(ROOT):
            prepare(ROOT, sys.argv[1:])
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"First-upload preparation failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
