"""Fetch the pinned official bundle that supplies the native Steam runtime."""

from pathlib import Path
import hashlib
import shutil
import stat
import tempfile
import urllib.error
import urllib.request
import zipfile



UPLOADER_VERSION = "v0.2.0"
UPLOADER_URL = ("https://github.com/megacrit/sts2-mod-uploader/releases/download/"
                f"{UPLOADER_VERSION}/ModUploader-osx-arm64.zip")
UPLOADER_SHA256 = "7a21d6f0890485a96f4cc27793ffa48d415158af46eae6fcf751f080bff6151f"
UPLOADER_FILES = ("ModUploader", "libsteam_api.dylib", "steam_appid.txt",
                  "template/workshop.json", "template/README.md", "template/image.png",
                  "template/content/README.md")


def ensure_uploader(root):
    destination = root / "references/ModUploader-osx-arm64"
    executable = destination / "ModUploader"
    if all((destination / name).is_file() for name in UPLOADER_FILES):
        executable.chmod(executable.stat().st_mode | stat.S_IXUSR)
        return executable

    destination.parent.mkdir(parents=True, exist_ok=True)
    print(f"Downloading official macOS ARM64 uploader {UPLOADER_VERSION}...", flush=True)
    with tempfile.TemporaryDirectory(prefix=".uploader-", dir=destination.parent) as temp:
        temporary = Path(temp)
        archive = temporary / "uploader.zip"
        try:
            request = urllib.request.Request(UPLOADER_URL, headers={"User-Agent": "Gravity-prepare"})
            with urllib.request.urlopen(request, timeout=60) as response, archive.open("wb") as output:
                shutil.copyfileobj(response, output)
        except (OSError, urllib.error.URLError) as error:
            raise ValueError(f"Could not download the official uploader: {error}. "
                             "Retry, or manually extract the complete archive from "
                             f"{UPLOADER_URL} to {destination}.") from error
        if hashlib.sha256(archive.read_bytes()).hexdigest() != UPLOADER_SHA256:
            raise ValueError("Official uploader checksum mismatch; no local uploader files were changed.")

        extracted = temporary / "extracted"
        with zipfile.ZipFile(archive) as bundle:
            for member in bundle.infolist():
                path = Path(member.filename)
                if path.is_absolute() or ".." in path.parts or stat.S_ISLNK(member.external_attr >> 16):
                    raise ValueError(f"Unsafe uploader archive entry: {member.filename}")
            bundle.extractall(extracted)
        if not all((extracted / name).is_file() for name in UPLOADER_FILES):
            raise ValueError("Official uploader archive is incomplete; no local uploader files were changed.")
        (extracted / "ModUploader").chmod(0o755)

        # Preserve local extras, such as uploader logs, when repairing an incomplete copy.
        staged = temporary / "staged"
        if destination.exists():
            shutil.copytree(destination, staged)
        shutil.copytree(extracted, staged, dirs_exist_ok=True)
        previous = temporary / "previous"
        if destination.exists():
            destination.rename(previous)
        try:
            staged.rename(destination)
        except OSError:
            if previous.exists():
                previous.rename(destination)
            raise
    return executable

