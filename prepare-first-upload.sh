#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    echo "Usage: ./prepare-first-upload.sh [MSBuild arguments]"
    echo "Build an English-only private workspace for the official uploader. Does not upload."
    echo "Requires workshop/image.png and the official uploader under references/ModUploader-osx-arm64/."
    exit 0
fi
exec python3 "$project_dir/scripts/prepare_first_upload.py" "$@"
