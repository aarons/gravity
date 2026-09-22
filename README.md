# Gravity

A gameplay mod for Slay the Spire 2.

**Requires game v0.107.1 or later.**

Encounters fall into a pile. There are no paths to follow so they can be chosen
in any order. After 15 encounters (configurable) the boss is unlocked. Have fun!

## How it works

- The starting Ancient stays in its original position and must be visited first.
- Then choose **any unvisited encounter**, in any order. Visited encounters
  stay visible in the pile and cannot be selected again.
- By default, the boss unlocks after **15 encounters**. Face it then or keep
  exploring for as long as there are unvisited encounters. Enable encounter locking
  in settings to stop exploring once the boss unlocks.
  Entering the boss ends ordinary encounter selection.
  Double-boss acts retain their second boss, which unlocks after the first.
  Each act starts a fresh count.
- On the first unobstructed map opening, encounters fall with a simple circle collision
  simulation. Bosses descend into a separate row above the pile and stay there;
  they never collide with ordinary encounters. Everything settles in about four
  seconds; selection waits until the pile settles. The fall waits for the map to
  fade in and pauses while another screen covers it, including mod selection pages.
  Closing before the fall starts keeps it pending. Closing during visible playback
  finishes the fall; reopening keeps the settled positions.
- The map scrolls only across the compact pile. Directional controller navigation
  follows the new positions. The status bar's stairs indicator shows encounter
  progress in place of total floors climbed, both on the map and in rooms.
  It fills from `0/15` to `15/15`, then stays at the goal in your highlight color
  (gold by default), even if you keep exploring with encounter locking off.
  Its **Encounters** tooltip
  explains the per-act goal; click the stairs or number to open settings.
  With the requirement off, only the number
  visited is shown. There is no separate map counter.
- Each boss has a progress ring that fills counterclockwise from the
  left of the lock badge at 12 o'clock, around the bottom and up to its right.
  Locked bosses stay dim; the available boss gets a steady highlight ring
  (gold by default). The ring has one segment per required encounter, switching
  to a continuous track above 40. The second
  boss stays locked until the first is visited, even at `15/15`. Cleared bosses
  keep a muted ring and check mark. Opening the map when a boss is available
  scrolls to the boss row.
- Visit order drives floor numbers and current-act room history. Progress uses
  the game's existing saved coordinates. The layout is deterministic and uses no
  gameplay RNG; a small `user://gravity_viewed_maps.cfg` file remembers which maps
  have already animated.

## Settings

### Run settings

Open the game's **Mods** page and select **Gravity**. Run settings are shown
directly in its information panel, under **Gravity settings** without a tagline.
Directly below the header: **Changes to settings are applied to a new run.
The host’s settings are used in a co-op game.** A new run snapshots both rules;
later acts, saves, and reloads retain them. Existing saves without a requirement
snapshot use 15; saves without the lock setting allow continued exploration.
Clients' personal defaults are not overwritten.
If a new co-op run reaches client initialization without the host's settings,
Gravity uses the client's configured defaults for that run and writes a diagnostic
message to the log. Matching local settings provide a fallback in this case;
received host settings always take precedence, including the default of 15.

If **BaseLib** or **RitsuLib** is installed and enabled, Gravity also appears in
its mod-settings panel. RitsuLib provides main-menu and pause-menu shortcuts;
BaseLib provides a **Mod Configuration** entry in the settings menu. Both libraries are optional,
and every entry point uses the same saved preferences. These panels show only run
settings; color and pulse controls are available through the in-run appearance popup.

**How many encounters are required to unlock the boss?**

- **None required, boss is always available**
- **After 15 encounters - Game default** (selected by default)
- **After all encounters - A very long playthrough**
- **After a custom number of encounters:** Enter **0–999** or use minus/plus. The
  custom number starts at **15** and is remembered when switching choices. At **0**,
  Custom and None required are both checked, and the number control stays visible.
  Selecting the 15-encounter option restores the default rule.

A custom number above the act's available encounters requires visiting them all. The
starting Ancient must still be visited first, and double bosses remain sequential.
The custom field and its indented capping explanation appear only with Custom
selected: **Setting the number higher than available encounters won't add more,
it will just require all of them to be visited.**

- **Lock normal encounters once the boss unlocks:** Off by default. When enabled,
  reaching the required count leaves only the boss selectable. The checkbox is
  disabled and has no effect with **None required, boss is always available**
  (including a custom count of 0). Switching back to a requirement remembers the
  checkbox choice. It appears in the native Mods, BaseLib, and RitsuLib panels.

### In-run appearance

Click the **stairs icon or encounter count** in the top bar, on the map or in a
room, to open the smaller appearance panel with live boss-ring and encounter-pulse
examples.

- **Highlight color:** Choose directly on the slider's color spectrum and preview
  it immediately on boss progress rings, the completed top-bar counter, and in the example. **Reset to gold**
  restores the default. Both sliders have large drag handles and click targets.
- **Encounter pulse:** Adjust idle pulse strength from **Off** to **100%** (the
  original effect), with **25%** as the default. Changes apply immediately; hover
  and press feedback remain. The live preview uses a larger encounter icon for
  easier comparison with the map.

The inline and library panels scroll as needed and support keyboard/controller
focus. The in-run appearance popup scales to the viewport and closes with
Escape/back or Done. Changes save automatically; leaving a settings panel also
commits a number still being edited.

Preferences persist in `user://gravity.cfg`. The active encounter requirement and
lock are stored in the normal run save's extra fields and included in multiplayer data.
All co-op players must use the same Gravity version. Color and pulse are personal.

Requires the Steam game and .NET 9 SDK to build. References the installed game
assemblies, including its bundled Harmony; no separate Harmony mod, BaseLib,
RitsuLib, or asset pack is required. The mod is marked `affects_gameplay: true`.

## Development status and compatibility

The original release was tested on public beta **v0.111.0** (32 patches). On
stable **v0.107.1**, it failed during initialization because the Mods panel has
no `NModInfoContainer.Clear` method. That cleanup patch is now conditional;
row changes still clean up Gravity's settings panel on both branches.

The compatibility build passes all 31 applicable patches and the travel,
save/load, packet, lobby-settings, and optional-settings checks on stable
**v0.107.1**. The beta-only native handshake comparison test is explicitly
skipped on stable. Startup, the Mods settings panel, and map behavior have also
been confirmed in-game on stable. A beta recheck of this build is still pending.
Earlier game versions have not been verified.

If the game reports that Gravity's DLL assembly failed to initialize, check the
game version and the exception in `SlayTheSpire2/logs/godot.log`. This message
can result from a failed Harmony patch, but does not identify the cause itself.
Stable compatibility can be checked without booting the game by running
`GravityIntegrationTests` against its assemblies with the `Sts2DataDir` override.

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
   separate boss landing row, and shorter scrolling. Click during the fall: no
   room enters. Close early and reopen: both the pile and boss row should be settled.
   In each act, check that the bottom encounter row stays above the parchment's
   torn lower edge.
   With Hextech Runes, finish the post-Ancient selections before viewing the map:
   the full fall should still play. Check brief map opens between selection pages,
   covering the map partway through the fall, and saving/reloading before playback:
   unseen animation should remain pending, and covered animation should pause.
2. Visit the Ancient. Choose rooms from different original rows, including a
   top-row room early. The boss stays locked and each floor advances by one.
3. Reopen and save/reload. The pile and visited rooms should stay fixed; unknown
   rooms should show the correct revealed icon and history.
4. Check the status bar counter and boss rings at 0, 1, 14, and 15 encounters.
   Hover or controller-focus the stairs: the tooltip should say **Encounters**.
   Check that `15/15` fits beside the boss icon, including in a small viewport,
   and that the counter updates in rooms and after reloading. The starting
   Ancient and both bosses should leave the count unchanged. The first filled
   segment should be immediately left of the lock at 12 o'clock, with progress
   continuing counterclockwise around the bottom and ending to its right.
   Complete 15: the map opens on
   the boss row and the available boss has a steady gold ring. Unvisited ordinary
   encounters remain selectable by default. Visit more encounters and check that
   the counter stays at `15/15` with the full ring and chosen highlight color.
   With encounter locking enabled in a separate run, unvisited ordinary encounters
   should become dim and unselectable when the boss unlocks.
   Change the highlight color and reset to gold: the completed
   counter should update immediately, including in rooms and after reloading.
   In a double-boss act, the second stays dim with its lock until the first is
   visited. Check that the next act resets progress to `0/15` and its normal text color.
5. Check mouse, controller, drawing tools, fast mode, a small viewport, and co-op
   votes. Restart with the same seed in a new run: the new run should animate.
6. On the Mods page, select Gravity: settings should appear under **Gravity settings**,
   with no tagline or general boss-unlock explanation. Switch to another mod and
   confirm its original information layout returns. Navigate through the four
   encounter choices, custom number and minus/plus,
   and encounter-lock checkbox, then back to the mod list. Color and pulse controls
   should not appear in these panels.
   Check scrolling, Escape/back, a small viewport, and resizing. Enter a number and
   switch mods or leave without pressing Enter; reopen and restart to confirm
   persistence. Verify old All/off preferences select All/None, and custom numbers
   survive switching choices and restarting. Check the initial custom value of 15,
   entry of 0 and 999, capping 1000 to 999, and invalid input.
   Reducing Custom from 1 to 0 should check both Custom and None without hiding
   the control or moving focus; plus should return to 1 and uncheck None. Reopen
   at Custom 0 and verify both remain checked with the control visible.
   Labels should wrap, selecting a label should select its checkbox, and hidden custom controls
   should be skipped by keyboard/controller navigation.
   Repeat with neither library, only BaseLib, only RitsuLib, and both enabled.
   Gravity should appear once in each available library's settings panel, including
   RitsuLib's pause-menu entry. Edit through each entry point and verify the others
   show the same values and the active run goal stays put. RitsuLib should show its
   settings header without the mod-description tagline, including in the pause menu.
7. Open appearance settings by clicking the stairs, then the number, both in a room
   and on the map. Only color, pulse, and live previews should appear. Check keyboard
   and controller navigation, focus return, Escape/back, Done, and a small viewport.
   Drag both sliders from their handles and click above/below their tracks. Adjust
   color and pulse, including Off and reset to gold; confirm live examples, map
   updates, and persistence after restarting. The active encounter goal must stay put.
8. Choose None, the default 15, All, and custom values 1, 99, and 999 across
   separate new runs. Verify numeric capping without changing the saved default.
   The active run must stay unchanged after editing defaults, save/reload, and
   act transitions. Check boss unlock, continued exploration with locking off, no ring or
   denominator at 0, and sequential double bosses. The Ancient must still be
   visited first. Load a pre-settings save and confirm a goal of 15.
9. In co-op, start with different local defaults and check that everyone uses the
   host's requirement and encounter lock. Repeat after saving/loading and a client
   reconnect. Check that the client's next solo run still uses their own default, appearance settings
   remain independent, and a mismatched Gravity version is rejected on connection.
10. With fresh preferences, verify **Lock normal encounters once the boss unlocks**
    is unchecked in each configuration panel and the requirement is 15. Start a new
    run and verify normal encounters remain selectable after the boss unlocks.
    Enable locking and start another new run: normal encounters should remain selectable before
    the requirement, then become dim and unselectable exactly when the boss
    unlocks, with mouse, controller, and co-op votes. Save/reload and advance acts
    to check the rule persists and the count resets. Double bosses stay sequential.
    Turn the default off during the run and verify the active rule stays unchanged.
    Select None (also enter custom 0): the checkbox should dim, ignore clicks,
    and be skipped by keyboard/controller focus. A new run must allow normal
    encounters even if the disabled checkbox remains checked. Switch back to a
    requirement and confirm the checkbox choice survives, including after restart.

## Everyday commands

| Command | What it does |
| --- | --- |
| `./install.sh` | Validate English, build Release, copy this mod's DLL and JSON into the local game. |
| `./install.sh --uninstall` | Remove this mod's local development directory. |
| `./update-localizations.sh` | Create/review stale game and Workshop translations using Codex. |
| `./update-localizations.sh --check` | Check structure and review freshness without edits or model calls. |
| `./prepare.sh` | Update translations, validate, build and freeze a release; no install or upload. |
| `./prepare.sh --skip-localizations` | Skip model calls; still require valid, reviewed translations. |
| `./release.sh --dry-run` | Verify and describe the prepared release offline. |
| `./release.sh` | Create or update the Workshop item from the prepared release; first release is private. |

Restart the game after installing. A plain `dotnet build` only builds; installation
is explicitly enabled by `install.sh`. For a nonstandard Steam library:

```sh
./install.sh -p:Sts2Path="/path/to/Slay the Spire 2"
```

`Sts2DataDir` and `ModsPath` are also overridable MSBuild properties. Use the same
path overrides with preparation if needed. Uninstall removes this mod's entire local
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
26 non-English files in separate Codex sessions, with up to **4 running at once**.
Use `./update-localizations.sh --jobs 2` to reduce concurrency, or `--jobs 1` for
sequential reviews. `./prepare.sh` uses the four-job default; to prepare with fewer
jobs, run the updater with `--jobs 2`, then `./prepare.sh --skip-localizations`.
It uses your configured model;
`--model MODEL` overrides it. `--context "What changed"` adds guidance, and `--force`
reviews every translation. Workshop translations include a short AI translation
note; adjust its policy in `scripts/prompts/workshop-localization.md` if desired.

The updater skips a file only when validation and both English/translation hashes
match its review receipt. English changes invalidate that group; translation
edits invalidate that file. Game reviews finish before Workshop reviews start so
listing translations can consult the updated game terminology. Session output is
grouped by file and shown when each session finishes. Only one updater can run in
a checkout at a time. On a failed session or validation error, the updater stops
starting jobs, finishes active reviews, and saves successful results. It adds no
automatic retries; rerun with fewer jobs after resolving failures or usage limits.
Ctrl-C stops active sessions; completed reviews survive interruption. Review the
diff and check fonts/layout in-game before release. Hashes establish review
freshness, not translation quality. Prompt-policy changes require `--force`.

Local installation only validates English. Preparation requires all 14 locales in
both groups with current review receipts. Changing the supported language set
also requires updating the expected count in `scripts/validate_localization.py`
and `tools/WorkshopLocalization/ListingFiles.cs` and their tests.

Shared changelogs are maintained separately in `workshop/settings.json`:
version, English text, separator, matching Simplified Chinese text. See `AGENTS.md`.
The updater does not translate changelogs.

## Workshop setup

Add your own `workshop/image.png`, less than 1 MB. Optionally add gallery images
in `workshop/previews/` (PNG/JPG/GIF, each less than 1 MB, ordered by filename).
Set tags, dependencies, and release visibility in `workshop/settings.json`.

Use the same prepare/release commands for the first upload and every update.
`./prepare.sh` can be rerun at any stage: it validates translations, builds, and
replaces the prepared snapshot without uploading. If the native Steam library is
missing, it downloads the checksum-verified
[official Mega Crit uploader v0.2.0 bundle](https://github.com/megacrit/sts2-mod-uploader/releases/tag/v0.2.0)
into `references/ModUploader-osx-arm64/`. Only its Steam runtime is used by our
publisher. This inherited runtime setup targets macOS ARM64; Intel is untested.

On the first full `./release.sh`, the publisher creates the Workshop item and
saves its ID automatically to `workshop/mod_id.txt` before uploading the prepared
content, all listing languages, and gallery. **The first release is private**,
even if settings specify public. Commit `workshop/mod_id.txt` and preserve it for
all subsequent releases. Once the first full release succeeds, the next preparation
uses the visibility in settings. To go public, set `"visibility": "public"`, prepare,
review the dry run, and release again. Releasing the same initial snapshot stays private.

Retries reuse the saved item and leave the prepared files unchanged. Repreparing
an incomplete first release also keeps it private. If Steam requires its Workshop
agreement, the ID is saved before stopping; accept the agreement and retry.
If creation times out before returning an ID, the publisher records the uncertain
attempt and stops subsequent creation attempts to avoid duplicates. Check your
Workshop items and save the created item's ID in `workshop/mod_id.txt`, then retry.
Only if Steam confirms no item exists should you remove
`workshop/.release-state/creation.json` and retry creation.

If you used the old first-upload flow, preparation automatically recovers
`workshop/first-upload/mod_id.txt` when present. An unused first-upload workspace
can remain in place; it is no longer needed. Conflicting saved IDs stop the workflow.

## Prepare and publish a release

```sh
# First update the root mod JSON's version and workshop/settings.json's changeNote.
./install.sh                    # Test in-game, initially in English.
./prepare.sh                    # Runs localization updates and freezes the release.
./release.sh --dry-run          # Review frozen inputs offline.
./release.sh                    # Explicit live publication, with Steam running.
```

Review `workshop/workshop.json`, `workshop/content/`, and
`workshop/prepared/localizations/`. The generated English manifest comes from the
English listing plus shared settings; edit those sources and prepare again.

Preparation snapshots the DLL, metadata, images, listing translations, publisher and
native Steam library, and hashes source/prepared files. It archives distinct builds
as versioned ZIPs under `archive/`. Failed builds preserve the previous prepared release.
Release never builds, translates, or prepares; changed inputs or review copies
require a new prepared release. Prepare and release use a checkout-local lock.

Steam must be running and signed in to the publishing account (the item’s owner for updates). The publisher
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

### Optional settings integrations

The adapters use [BaseLib's custom `ModConfig` UI](https://github.com/Alchyr/BaseLib-StS2/blob/master/Config/ModConfig.cs)
and [RitsuLib's custom settings controls](https://sts2-ritsulib.ritsukage.com/guide/mod-settings).
They register on the first main-menu initialization after mod loading, independent
of mod load order. Gravity has no compile-time references or required manifest
dependencies on either library. An incompatible optional API logs a warning and
leaves the native Mods panel available. BaseLib may create an empty adapter config
file; Gravity's actual preferences remain in `user://gravity.cfg`.

To check adapter contracts against local library DLLs without launching the game,
set `GRAVITY_BASELIB_DLL` and/or `GRAVITY_RITSULIB_DLL` to their installed paths
when running `dotnet run --project tests/GravityIntegrationTests`. For RitsuLib's
multi-version distribution, use the DLL under `compat/<game-version>/` and keep
its companion `shared/` directory in place.
