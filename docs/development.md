# Development checks

See the [README](../README.md) for building and installing locally.

## Offline checks

```sh
dotnet build -c Release
dotnet run --project tests/GravityTests
dotnet run --project tests/GravityIntegrationTests
dotnet run --project tests/GravityIntegrationTests -- --map-only
python3 scripts/validate_localization.py --english-only
```

The integration harness installs and removes Harmony patches in a separate process
against the installed game assemblies. It does not open or change a run. Its
multiplayer fixtures use stand-ins for sockets and scenes. Offline and headless
checks do not establish rendered UI, live co-op, mixed-branch multiplayer, or
full-run compatibility.

Settings tests cover standard and custom runs, save/load and rewriting, legacy
migration, and multiplayer start/load/rejoin settings transfer and fallbacks.
They also check native message discovery and stable IDs across mod load order.
The optional `--map-only` run isolates travel, history, and reveal eligibility from
the settings and network patches, including resumed acts and fresh later acts.

When changing [settings synchronization](../src/GravitySettingsSync.cs), preserve
the ordering: send the immutable settings snapshot over the native reliable
transport before the start/load/rejoin message, and consume it before accepting
the run. There is no separate acknowledgment or retry protocol. Failure tests
withhold or alter settings; they do not simulate normal packet loss.

Missing or invalid host settings let the native flow continue with a fixed local
snapshot. Load/rejoin first tries `user://gravity_run_settings.json`, matched by
host and seed; otherwise, current preferences are captured. New runs always use
fresh settings. Each fallback logs and, with connection context, attempts one
warning per connection; skipped or failed popups are not retried.
[Save restoration](../src/GravityRunSettings.cs) handles JSON separately from
native network packets and migrates and removes legacy Gravity modifiers.

Before release, use the [multiplayer test checklist](multiplayer-test.md#test-checklist)
to test a live host/client run with different defaults, a saved-run load, and a
client restart/rejoin, using matching gameplay mods on all peers.

After changing workflow or publisher tooling, run:

```sh
python3 -m unittest discover -s tests -v
```

Tooling tests use temporary fixtures, fake translation sessions, and an in-memory
Steam adapter. They do not install into the game, call real translation models,
or upload to Steam.

### Native run-history layout regression

`tests/GravityRunHistoryTests` is a test mod that runs inside Godot and exits the
game with status 0 on success or 1 on failure. Use a disposable game copy and an
isolated user directory, as in the [multiplayer test setup](multiplayer-test.md).
Build it with `dotnet build tests/GravityRunHistoryTests -c Release`, then copy its
DLL and JSON into that copy's `mods/GravityRunHistoryTests/` directory alongside
the freshly built Gravity mod. Enable both mods in the isolated profile.

Launch the copy with `--headless --force-steam off --clientId 1` and set
`GRAVITY_HISTORY_FIXTURE` to an existing three-act `.run` file containing normal
combats in each act. The fixture is read only; its Steam platform is changed to
offline in memory. The test opens the native history screen, checks 17, 18, 31,
and 100 encounters per act, switches back to short and empty histories, and
reopens the original fixture. It checks page bounds, encounter sizes, floor order,
and left/right focus links. Look for `HISTORY TEST PASS` in the game log.
For a rendered screenshot, omit `--headless` and set `GRAVITY_HISTORY_SCREENSHOT`
to an output PNG path. This does not replace manual tooltip and controller checks.

### Native run-setup regression

`tests/GravityRunSetupTests` is a Godot test mod for the custom-run screen and
disabled-run persistence. Build with:

```sh
dotnet build tests/GravityRunSetupTests -c Release
```

Install its DLL and JSON alongside the freshly built Gravity DLL
in a disposable game copy with an isolated user directory. Enable both mods in
that profile. This test starts and saves a custom run; never use a normal profile.

Launch with `--headless --force-steam off --clientId 1`. The test checks native
setup controls, toggle placement and focus links, reopening, and the scrolling
portrait layout used by BaseLib when extra characters are installed. It then
starts with Gravity disabled and verifies a native disk save, a save rewrite,
and restoration with the opposite personal preference. Look for
`RUN SETUP TEST PASS` and exit status 0. Relaunch the same isolated profile with
`GRAVITY_SETUP_RESUME=1` to check that the disabled choice survives a full process
restart. Omit `--headless` and set `GRAVITY_SETUP_SCREENSHOT` to
an output PNG path for a rendered setup screenshot. Check controller behavior
manually.

### Native encounter-selector regression

Build `tests/GravityEncounterSelectorTests` with `dotnet build
tests/GravityEncounterSelectorTests -c Release`. Install its DLL and JSON alongside
Gravity in a disposable game copy with an isolated profile, then launch with
`--headless --force-steam off --clientId 1`. This test starts a custom run and
changes its map; never use a normal profile.

The test exercises all six native legend rows, click bounds, available counts,
random selections, exhaustion, travel guards, boss locking, and map reattachment
and cleanup. It intercepts the native selection boundary instead of entering rooms.
Look for `ENCOUNTER SELECTOR TEST PASS` and exit status 0. For a rendered map,
omit `--headless` and set `GRAVITY_SELECTOR_SCREENSHOT` to an output PNG path.
Check controller behavior manually.

A fresh profile can show the map-selection tutorial, which blocks the fixture's
clicks. Complete that tutorial first, or rerun with the same disposable profile
after it has been marked seen.

## Alternate game installations

Use the `Sts2DataDir` MSBuild property to build against another installation's
assemblies. To check the same Release DLL against a second installation without
rebuilding it:

```sh
dotnet build -c Release tests/GravityIntegrationTests
GRAVITY_TEST_GAME_DATA="/path/to/game/data" \
  dotnet tests/GravityIntegrationTests/bin/Release/net9.0/GravityIntegrationTests.dll
```

The [README's local stable snapshot](../README.md#local-stable-snapshot) records
the preserved installations and commands for this development machine. Check both
directions when changing compatibility: build the harness and mod against each
branch, then run that exact output against both sets of game assemblies.

Keep reference snapshots unchanged; run native test mods in disposable copies
with isolated profiles.

Preserve runtime API binding when changing compatibility code:
[settings synchronization](../src/GravitySettingsSync.cs) selects the join-flow
service setter or constructors and resolves the service getter at runtime;
the [map marker](../src/GravityMapMarker.cs) supports both `SetMapPoint(Control)`
and `SetMapPoint(NMapPoint)`; the [encounter tooltip](../src/GravityTopBarProgress.cs)
avoids assigning `HoverTip.Id`, which is read-only in newer game versions.

The headless fixtures also adapt model initialization, RNG construction, and
custom-run flags to the available API and avoid native client-service constructors.
The co-op version-comparison test skips when the game lacks `PeerVersionInfo`.
Check these adaptations when updating the harness for a new game version.

If the game reports that Gravity failed to initialize, inspect the exception in
`SlayTheSpire2/logs/godot.log`. The generic assembly error does not identify the cause.

## Optional mod integrations

Gravity's BaseLib and RitsuLib settings adapters register at main-menu initialization
without compile-time dependencies on either library. If an optional API is incompatible,
the adapter logs a warning and the native Mods panel remains available. Gravity's
preferences live in `user://gravity.cfg`; BaseLib may also create an empty adapter
config file.

Set `GRAVITY_BASELIB_DLL` and/or `GRAVITY_RITSULIB_DLL` when running the integration
harness to check their local API contracts. For RitsuLib's multi-version distribution,
use the DLL under `compat/<game-version>/` and keep its companion `shared/` directory.

Set `GRAVITY_BETTEREXPERIENCE_DLL` to check BetterExperience's linked-boss map discovery
and travel. Use a matching process architecture; BetterExperience 1.5.5's DLL targets
x64.
