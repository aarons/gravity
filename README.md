# Gravity

A gameplay mod for Slay the Spire 2. Encounters fall into a pile, with no paths
to follow. Choose them in any order and unlock the boss after 15 encounters—or
set your own pace.

**Requires game v0.107.1 or later.** No separate Harmony mod, BaseLib, RitsuLib,
or asset pack is required.

## How it works

Visit the starting Ancient, then choose any unvisited encounter. Before Neow is
unlocked, complete the opening fight instead; it counts toward boss progress.
Bosses sit above the pile, with progress rings showing how close they are to
unlocking. The stairs indicator in the top bar tracks encounters visited this act.

By default, ordinary encounters lock after 15 visits, requiring you to move on to
the boss. Acts with multiple bosses keep their boss sequence. Each
act starts a fresh encounter count. The pile stays in place when you reopen the
map or reload a save, and controller navigation follows the new layout.
Fresh acts make a best-effort falling reveal when the map is visible, allowing
extra selection pages after the Ancient to finish first. The map stays usable
while waiting; closing it or choosing a destination finishes the reveal immediately.
Resuming or rejoining opens directly to the settled pile, even if that player's
local animation history is missing.

In **Compendium → Run History**, long acts wrap encounters onto additional rows.
The page scrolls vertically, keeping run details, relics, and deck entries aligned.

## Settings

Open **Mods → Gravity** to configure new runs. If installed, BaseLib and RitsuLib
also provide access through their mod-settings panels.

- **Boss requirement:** Choose none, the default 15, all encounters, or a custom
  count from 0–999. A count above the available encounters requires visiting them all.
- **Encounter locking:** Stop ordinary exploration once the boss unlocks.
  On by default for new players; turn it off to keep exploring. Has no effect when
  no encounters are required.

Run settings are fixed when a new run starts and persist through saves and later
acts. Co-op uses the host's settings without changing clients' personal defaults.
The host sends the run's settings at startup and when a player joins a saved or
ongoing run. If synchronization fails, play continues with your current settings.
Gravity attempts one warning per connection, skipping it if another dialog is open.
When resuming, Gravity first tries its locally remembered settings for that host and run.
Settings are saved separately from gameplay modifiers; older Gravity saves migrate
automatically, preserving Neow's normal starting choices.

Click the **stairs icon or encounter count** in the top bar to adjust appearance
at any time. Choose a highlight color for boss rings and completed progress, or
reset it to gold. Adjust encounter pulse strength from Off to 100% (default 25%).
These settings include live previews, save automatically, and remain personal in co-op.

## Compatibility

Start a fresh run with Gravity enabled and use the same Gravity version for all
co-op players. Maps with fewer than 15 encounters retain their normal behavior.

Mods that replace map travel or draw route overlays may conflict. The original
map graph remains available to game content, so effects based on its rows or paths
may need compatibility work. Map Guide's Pathfinder still describes that original graph.

Offline checks cover progression, saved settings, and multiplayer payloads;
they do not establish live co-op or full-run compatibility across game versions.

## Development

Building requires the Steam game, .NET 9 SDK, and Python 3 for the workflow scripts.
The project references assemblies from your installed game.

```sh
./install.sh                 # Validate English, build Release, and install locally.
# Restart the game to load the new build.
dotnet build -c Release       # Build without installing.
```

For a nonstandard Steam library, pass `-p:Sts2Path="/path/to/Slay the Spire 2"`.
`Sts2DataDir` and `ModsPath` can also be overridden. `./install.sh --uninstall`
removes the local development copy, including any extra files in that mod folder.

### Local stable snapshot

On Aaron's development machine, the stable installation is preserved outside
Steam at `/Users/aaron/code/sts2-game-builds/stable-23811903/game`
(public branch, Steam build **23811903**, copied September 27, 2026).
The parent folder contains `steam-appmanifest.acf` and `snapshot.json` with
verified SHA-256 checksums for all 430 copied files. Steam can switch the regular
installation to beta without replacing this copy. Keep the snapshot unchanged;
build against it without installing into it:

```sh
dotnet build -c Release -p:Sts2Path=/Users/aaron/code/sts2-game-builds/stable-23811903/game
dotnet run --project tests/GravityIntegrationTests -c Release \
  -p:Sts2Path=/Users/aaron/code/sts2-game-builds/stable-23811903/game
```

To test an already-built integration harness and mod DLL against stable without
rebuilding either:

```sh
GRAVITY_TEST_GAME_DATA="/Users/aaron/code/sts2-game-builds/stable-23811903/game/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64" \
  dotnet tests/GravityIntegrationTests/bin/Release/net9.0/GravityIntegrationTests.dll
```

Check Steam's currently selected branch before using the default build paths.
The snapshot is a separate installation copy, not an isolated save profile.

Keep mod behavior in `src/` and player-facing text in the localization files,
using the existing helper with literal `Localize("key")` calls. Feature work
focuses on English; update this overview with relevant features and UI context
before the dedicated translation pass. See [AGENTS.md](AGENTS.md) for project
conventions and changelog format.

| Command | Purpose |
| --- | --- |
| `./update-localizations.sh` | Update and review game and Workshop translations using Codex. |
| `./update-localizations.sh --check` | Check translation structure and review freshness without edits or model calls. |
| `./prepare.sh` | Update translations, validate, build, and freeze a release without publishing. |
| `./release.sh --dry-run` | Verify and inspect the prepared release offline. |
| `./release.sh` | Publish the prepared release to Steam Workshop; first publication is private. |

Update `Gravity.json`'s version and the bilingual changelog in
`workshop/settings.json` together before preparing a release. Review the prepared
release before publishing; changes to its inputs require preparing again.

Further guidance:

- [Local multiplayer test](docs/multiplayer-test.md): isolated beta host/client setup,
  different preferences, reconnect checks, and the beta's live-rejoin UI limitation.
- [Development checks](docs/development.md): tests, alternate game installations,
  and optional library integrations.
- [Localization](docs/localization.md): source files, review workflow, and updater options.
- [Publishing](docs/publishing.md): Workshop setup, release preparation, and retry recovery.

## Project layout

```text
src/                         Mod behavior, physics, UI, and localization helper
Gravity.csproj, Gravity.json  Project and game metadata
localization/                Game text; English source is eng.json
workshop/localizations/      Workshop listings; English source is english.json
workshop/settings.json       Publishing settings and bilingual changelog
scripts/                     Workflow scripts, validators, and Codex prompts
tools/WorkshopLocalization/   Steam Workshop publisher
tests/                       Mod behavior, integration, and tooling checks
docs/                        Development workflow references
```
