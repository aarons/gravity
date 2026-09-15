#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    echo "Usage: ./release.sh [--dry-run] [--language STEAM_CODE | --previews-only]"
    echo "Publish the reviewed snapshot: shared content and all supported listing languages."
    echo "--language updates only that listing language; it never uploads shared content."
    echo "--previews-only reuploads all shared gallery images in filename order."
    echo "--dry-run checks and describes the snapshot offline, without Steam."
    echo "Never builds, packages, or translates. Steam must be running for live publication."
    exit 0
fi
exec python3 "$project_dir/scripts/workshop_release.py" release "$@"
