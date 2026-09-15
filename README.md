# Gravity

A gameplay mod for Slay the Spire 2. The map loses its paths and its encounters
fall into a compact pile around the starting Ancient.

## How it works

- The starting Ancient stays in its original position and must be visited first.
- Then choose **any 15 distinct encounters**, in any order. Visited encounters
  stay visible in the pile and cannot be selected again.
- After encounter 15, only the boss becomes available. Double-boss acts retain
  their second boss, which unlocks after the first. Each act starts a fresh count.
- On the first map opening, encounters and bosses fall with a simple circle
  collision simulation. They settle in about four seconds; selection waits until
  the pile settles. Closing the map early finishes the fall. Reopening keeps the
  settled positions.
- The map scrolls only across the compact pile. Directional controller navigation
  follows the new positions. A status line above the map shows progress.
- Visit order drives floor numbers and current-act room history. Progress uses
  the game's existing saved coordinates. The layout is deterministic and uses no
  gameplay RNG; a small `user://gravity_viewed_maps.cfg` file remembers which maps
  have already animated.

Requires the Steam game and .NET 9 SDK to build. References the installed game
assemblies, including its bundled Harmony; no separate Harmony mod, BaseLib, or
asset pack is required. The mod is marked `affects_gameplay: true`.

## Development status and compatibility

This is an initial implementation. Offline progression/physics tests and patch
installation checks are provided below. In-game visual and full-run validation
is still required, especially controller navigation, saves, and multiplayer.

Use a fresh run with Gravity enabled throughout. Tutorial/debug maps with no
starting Ancient or fewer than 15 encounters keep their normal behavior. Other
mods that replace map travel or draw route overlays may conflict; Map Guide's
Pathfinder still describes the original graph. The original graph remains in
the save for game content that inspects it, but Gravity replaces travel choices
and does not render its paths. Relics/events that reason about original map rows
or paths may need additional compatibility work. All co-op players need Gravity.

Build and run the behavior checks:

```sh
dotnet build -c Release
dotnet run --project tests/GravityTests
dotnet run --project tests/GravityIntegrationTests
python3 scripts/validate_localization.py --english-only
```

`GravityIntegrationTests` installs then removes the Harmony patches in a separate
process against your installed game assemblies; it does not open or change a run.

In-game acceptance checks:

1. Start a new act. Open the map, check the fall, anchored Ancient, absent paths,
   boss visibility, and shorter scrolling. Click during the fall: no room enters.
2. Visit the Ancient. Choose rooms from different original rows, including a
   top-row room early. The boss stays locked and each floor advances by one.
3. Reopen and save/reload. The pile and visited rooms should stay fixed; unknown
   rooms should show the correct revealed icon and history.
4. Complete 15 encounters. Only the boss is selectable. Check a double-boss act
   and entering the next act.
5. Check mouse, controller, drawing tools, fast mode, a small viewport, and co-op
   votes. Restart with the same seed in a new run: the new run should animate.

## Everyday commands

| Command | What it does |
| --- | --- |
| `./install.sh` | Validate English, build Release, copy this mod's DLL and JSON into the local game. |
| `./install.sh --uninstall` | Remove this mod's local development directory. |
| `./update-localizations.sh` | Create/review stale game and Workshop translations using Codex. |
| `./update-localizations.sh --check` | Check structure and review freshness without edits or model calls. |
| `./package.sh` | Update translations, validate, build and freeze a release; no install or upload. |
| `./package.sh --skip-localizations` | Skip model calls; still require valid, reviewed translations. |
| `./release.sh --dry-run` | Verify and describe the prepared release offline. |
| `./release.sh` | Publish the prepared content and all supported listing languages. |

Restart the game after installing. A plain `dotnet build` only builds; installation
is explicitly enabled by `install.sh`. For a nonstandard Steam library:

```sh
./install.sh -p:Sts2Path="/path/to/Slay the Spire 2"
```

`Sts2DataDir` and `ModsPath` are also overridable MSBuild properties. Use the same
path overrides with packaging if needed. Uninstall removes this mod's entire local
folder, including files you put there; keep source files in this checkout.

## Localization

- Game English: `localization/eng.json`.
- Workshop English: `workshop/localizations/english.json` (independent source).
- `supported-languages.json`: the inherited 14 game-to-Steam locale mappings.
  `esp`/`latam` is Latin American Spanish; `spa`/`spanish` is Castilian Spanish.
- `scripts/prompts/`: separate Codex review instructions for game UI and listings.
- `localization-review.json`: generated content fingerprints; commit with translations.

New feature work focuses on English. The template ships only English, with no
pre-approved translations. When ready, `./update-localizations.sh` creates all
26 non-English files in separate Codex sessions. It uses your configured model;
`--model MODEL` overrides it. `--context "What changed"` adds guidance, and `--force`
reviews every translation. Workshop translations include a short AI translation
note; adjust its policy in `scripts/prompts/workshop-localization.md` if desired.

The updater skips a file only when validation and both English/translation hashes
match its review receipt. English changes invalidate that group; translation
edits invalidate that file. Completed reviews survive interruption. Review the
diff and check fonts/layout in-game before release. Hashes establish review
freshness, not translation quality. Prompt-policy changes require `--force`.

Local installation only validates English. Packaging requires all 14 locales in
both groups with current review receipts. Changing the supported language set
also requires updating the expected count in `scripts/validate_localization.py`
and `tools/WorkshopLocalization/ListingFiles.cs` and their tests.

Shared changelogs are maintained separately in `workshop/settings.json`:
version, English text, separator, matching Simplified Chinese text. See `AGENTS.md`.
The updater does not translate changelogs.

## First Workshop upload

1. Extract the complete [official Mega Crit uploader](https://github.com/megacrit/sts2-mod-uploader)
   for macOS ARM64 into `references/ModUploader-osx-arm64/`. Keep its native library
   and support files beside the executable. On Intel macOS use a matching native
   binary and adapt the inherited path if needed; that variant is untested here.
2. Add your own `workshop/image.png`, less than 1 MB. No borrowed artwork or live
   Workshop ID is included. Optionally add gallery images in `workshop/previews/`
   (PNG/JPG/GIF, each less than 1 MB, ordered by filename).
3. Run `./prepare-first-upload.sh` (accepts MSBuild path overrides). It builds into
   `workshop/first-upload/` with English listing text and **private** visibility.
   Review that workspace, then run the exact official upload command it prints.
4. After the uploader succeeds, copy `workshop/first-upload/mod_id.txt` to
   `workshop/mod_id.txt` using the printed command. Commit the latter; it identifies
   this mod's item for every subsequent release.
5. Set the intended visibility, tags and dependencies in `workshop/settings.json`,
   update the listing/changelog, and follow the normal release steps below.

First upload is a one-time bootstrap using the official uploader, independent of
our frozen-release publisher. It sends English content only; the next full release
adds all listing languages and the preview gallery. No helper uploads automatically
during setup or preparation. Creating a second item is never part of `release.sh`.

The preparation helper refuses to overwrite an existing first-upload workspace.
If uploading fails, inspect/retry that same workspace and preserve any returned
`mod_id.txt`. Check Workshop before retrying after a timeout. If you need to
rebuild *before any upload*, remove `workshop/first-upload/` and prepare again.

## Prepare and publish a release

```sh
# First update the root mod JSON's version and workshop/settings.json's changeNote.
./install.sh                    # Test in-game, initially in English.
./package.sh                    # Runs localization updates and freezes the release.
./release.sh --dry-run          # Review frozen inputs offline.
./release.sh                    # Explicit live publication, with Steam running.
```

Review `workshop/workshop.json`, `workshop/content/`, and
`workshop/prepared/localizations/`. The generated English manifest comes from the
English listing plus shared settings; edit those sources and package again.

Packaging snapshots the DLL, metadata, images, listing translations, publisher and
native Steam library, and hashes source/prepared files. It archives distinct builds
as versioned ZIPs under `archive/`. Failed builds preserve the previous package.
Release never builds, translates, or packages; changed inputs or review copies
require a new package. Package and release use a checkout-local lock.

Steam must be running and signed in to the item's owning account. The publisher
checks ownership, backs up returned listing metadata, verifies localized updates
by reading them back, and keeps retry receipts in `workshop/.release-state/`.
Preserve those receipts for retries. Previously verified live content/listings
are skipped when still current. Image delivery can lag; inspect failures before retrying.

```sh
./release.sh --language japanese    # Listing text only; no content upload.
./release.sh --previews-only         # Reupload the shared gallery.
# Either mode can also be combined with --dry-run.
```

The publisher carries a pinned Steamworks.NET managed DLL and its upstream MIT
license under `tools/WorkshopLocalization/vendor/`. The native Steam library and
game assemblies are local prerequisites and are not redistributed in this template.

## Layout and checks

```text
src/                         Gravity rules, physics, UI patches, and localization
Gravity.csproj, Gravity.json  Mod project and game metadata
Sts2PathDiscovery.props       Game and local mods path discovery
localization/                Embedded game text; English is authoritative
workshop/localizations/      Steam title/description by language
workshop/settings.json       Shared publishing settings and bilingual changelog
scripts/                     Python workflow, validators, and Codex prompts
tools/WorkshopLocalization/   Inherited Steam publisher and vendored managed library
tests/                       Offline workflow and publisher regression coverage
AGENTS.md                    Codex conventions, commands, and changelog format
```

Run checks after modifying the tooling:

```sh
python3 scripts/validate_localization.py --english-only
dotnet build -c Release
python3 -m unittest discover -s tests -v
```

Tests create synthetic translations and IDs only in temporary fixtures, mock Codex
and uploads, and exercise the publisher through an in-memory Steam adapter.
No tests install into the game, invoke real translation models, or connect to Steam.
Compilation and offline tests do not verify in-game UI or a live Workshop upload.
