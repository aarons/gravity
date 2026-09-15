#!/usr/bin/env bash

set -euo pipefail

project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_file="$project_dir/Gravity.csproj"

uninstall=false
for arg in "$@"; do
    shift
    case "$arg" in
        --uninstall) uninstall=true ;;
        -h|--help)
            echo "Usage: ./install.sh [--uninstall] [MSBuild options]"
            echo "  --uninstall  Remove only the local Gravity development copy."
            echo "  Default: build and install Gravity locally."
            exit 0
            ;;
        *) set -- "$@" "$arg" ;;
    esac
done

if ! command -v dotnet >/dev/null 2>&1; then
    echo "Error: the .NET 9 SDK is required, but 'dotnet' was not found." >&2
    echo "Install it from https://dotnet.microsoft.com/download/dotnet/9.0" >&2
    exit 1
fi

dotnet_version="$(dotnet --version)"
dotnet_major="${dotnet_version%%.*}"
if [[ ! "$dotnet_major" =~ ^[0-9]+$ ]] || (( dotnet_major < 9 )); then
    echo "Error: the .NET 9 SDK is required; found version $dotnet_version." >&2
    echo "Install it from https://dotnet.microsoft.com/download/dotnet/9.0" >&2
    exit 1
fi

if [[ "$uninstall" == true ]]; then
    dotnet msbuild "$project_file" "$@" -target:UninstallMod
    echo "Local development copies removed. Restart Slay the Spire 2 to test your Workshop subscription."
    exit 0
fi

echo "Building and installing Gravity..."
python3 "$project_dir/scripts/validate_localization.py" --english-only
dotnet build "$project_file" --configuration Release "$@" -p:InstallMod=true
echo "Gravity installed successfully. Restart Slay the Spire 2 to load it."
echo "Use English in the game's language settings for local English testing. No translations were changed."
