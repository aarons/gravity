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
multiplayer fixtures use stand-ins for sockets and scenes; offline checks do not
replace in-game testing of UI, full runs, or live co-op.
Settings tests include standard and custom Sealed Deck runs with Gravity both on
and off, disk save/load, multiplayer startup, saved-lobby joins, live-rejoin
messages, and the client's remembered settings when a reconnect snapshot is missing.
The optional `--map-only` run isolates travel, history, and reveal eligibility from
the settings and network patches, including resumed acts and fresh later acts.

Gravity sends an immutable settings snapshot through the game's native reliable
message transport immediately before its start/load/rejoin message. The client
consumes the snapshot before accepting that run. Delivery and
ordering use the existing transport, without a separate acknowledgment or retry
protocol. Missing or invalid host settings use a fixed local snapshot while the
native start/join proceeds. Gravity logs each fallback and attempts a dismissible
warning once per connection. If the UI is unavailable, another modal is open, or
display fails, that attempt is skipped without deferral or retry. Save fallbacks
without connection context only log. New runs use current
preferences. Clients remember their most recent co-op snapshot in
`user://gravity_run_settings.json`, matched by host and seed for load/rejoin
fallbacks; starting a new run always captures fresh settings. Failure tests
deliberately withhold or alter settings, rather than model normal packet loss.
JSON save data is restored independently and does not extend native
network packets. Old Gravity modifiers are read and removed during save loading.

Mod messages share the game's message-ID registry. The integration harness checks
native discovery and stable IDs with another mod and reversed load order; this is
not a claim that arbitrary mod combinations or game versions are compatible.
Before release, test a live host/client run with different defaults, a saved-run
load, and a client restart/rejoin, using matching gameplay mods on all peers.

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
the preserved installation and commands for this development machine. Check both
directions when changing compatibility: build the harness and mod against each
branch, then run that exact output against both sets of game assemblies.

The join-flow receiver hooks the service setter on stable and the constructor on
beta. Its service getter is resolved at runtime because its return type differs
between branches. The headless fixtures also resolve model initialization and RNG
construction at runtime and avoid native client-service constructors; these test
setup APIs differ even where Gravity's gameplay APIs remain compatible.

Verified September 27, 2026 with stable Steam build **23811903** and public-beta
build **24724944**: both Release builds completed with zero warnings, and the full
offline integration suite passed in all four combinations:

| Built against | Run against stable | Run against beta |
| --- | --- | --- |
| Stable | Passed | Passed |
| Beta | Passed | Passed |

Stable skips the beta-only co-op version-comparison check because it uses the
older handshake API. These results do not replace live co-op or full-run testing.

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
x64. These checks do not verify visual behavior or full-run compatibility.
