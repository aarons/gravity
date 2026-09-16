#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
update_localizations=true
prepare_args=(prepare --)
for arg in "$@"; do
    case "$arg" in
        --help|-h)
            echo "Usage: ./prepare.sh [--skip-localizations] [MSBuild arguments]"
            echo "Update localizations, validate translation freshness, and prepare an English Workshop manifest plus all languages."
            echo "  --skip-localizations  Skip updating localizations; freshness validation still runs."
            echo "Builds a frozen release in workshop/prepared/. Does not install or publish."
            exit 0
            ;;
        --skip-localizations) update_localizations=false ;;
        *) prepare_args+=("$arg") ;;
    esac
done
if [[ "$update_localizations" == true ]]; then
    "$project_dir/update-localizations.sh"
fi
exec python3 "$project_dir/scripts/workshop_release.py" "${prepare_args[@]}"
