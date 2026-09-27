# Local multiplayer test (macOS public beta)

The game supports separate local processes through `--fastmp host_standard`,
`--fastmp join`, and `--clientId`. Gravity's launcher uses ENet on localhost port
33771 with Steam disabled. It creates disposable game copies with only Gravity,
separate Godot user directories, and timestamped logs. Your normal Steam game,
preferences, and active saves are not used by these instances.

Verified against the installed public-beta assemblies: v0.111.0, build 24724944.
This is a LAN transport test, not a Steam invitation/transport test.

## Prepare and launch

```sh
dotnet build -c Release
python3 scripts/validate_localization.py --english-only
python3 scripts/multiplayer_test.py prepare
python3 scripts/multiplayer_test.py host
# Wait for the host character-selection lobby, then:
python3 scripts/multiplayer_test.py client
```

`prepare` accepts `--game-path /path/to/game` (the directory containing the app),
and `--progress /path/to/progress.save` to copy existing unlock progress into both
test profiles. No active run is copied. When overriding the game path, build with
the matching `-p:Sts2Path=/path/to/game` first. All commands accept
`--session /absolute/path`; the default is `output/multiplayer/beta-test`.
Preparation refuses to overwrite an existing session. Quit both test processes
before preparing a different session: the host port is shared.

The session manifest records each user directory under
`~/Library/Application Support/GravityMultiplayerTests/`, player ID, executable,
and installed Gravity DLL hash. Reuse the session to preserve the run and IDs.
The host ID is 1 and the client ID is 1001. A new client ID cannot replace a
disconnected player in an existing run.

| Setting | Host preferences | Client preferences | Expected co-op run |
| --- | --- | --- | --- |
| Boss requirement | Custom: 2 | Default: 15 | 2 on both peers |
| Encounter locking | Off | On | Off on both peers |

Settings synchronize when the run starts, not when merely entering its lobby.
The client's personal Mods → Gravity preferences should remain at 15 / On.
Use the run's map counter and travel behavior to verify effective settings.

## Test checklist

1. Select characters and ready both peers. Start a fresh run. Finish the Ancient
   choices (or opening fight on a fresh profile). Verify both maps show the Gravity
   pile and a requirement of 2, with matching visit counts.
2. Let the client vote for an ordinary encounter, then have the host select the
   same one. Finish it normally. Repeat until the counter reaches 2. Confirm the
   boss unlocks on both peers and unvisited ordinary encounters remain selectable.
   This checks both host settings, including locking Off.
3. Return to the map after rewards. Quit only the client test window. Keep the
   host running and wait for it to notice the disconnect. Relaunch with
   `python3 scripts/multiplayer_test.py client`.
4. If live rejoin succeeds, verify the settled pile, matching visit count,
   requirement 2, boss unlocked, and ordinary encounters still selectable. Make
   a map vote on the client, confirm it on the host, and enter another encounter.
   Merely seeing the map is not sufficient.
5. Also test saved-run reconnect: quit the client, use the host's Save & Quit, then
   close its test window. Run `python3 scripts/multiplayer_test.py load-host`, wait
   for the saved-run lobby, then run the client command. Ready both peers and
   repeat the map checks and client-initiated travel.
6. Inspect both personal preferences after leaving the run. The client must still
   have 15 / On. Run `python3 scripts/multiplayer_test.py status` to inspect the
   separate config, remembered host snapshot, and host save's extra fields.

After Save & Quit, the original `host` launch automatically opens a new-game
lobby again: the game reprocesses `--fastmp host_standard` whenever it recreates
the main menu. This does not mean the saved run is missing. Close that host
process and use `load-host` instead; do not embark on another new run. The
`load-host` command uses `--fastmp load` with the same isolated save directory
and host ID. Close the client process too, then launch `client` after the saved
lobby is ready.

Launching normally through Steam uses a different save directory, Steam player
identity, and Steam transport. It will not resume this isolated LAN test, and
the localhost test client cannot join that Steam host. The beta's live-rejoin
rejection described below is in the shared join UI, so enabling Steam alone
does not fix that path.

### Known beta limitation: live rejoin UI

In this installed beta, `JoinFlow.Begin` requests and receives a
`ClientRejoinResponseMessage` for an ongoing run, but
`NJoinFriendScreen.JoinGameAsync` handles the returned `Running` state by showing
`NetError.RunInProgress` and disconnecting. The older `multiplayer test` console
screen also throws `NotImplementedException` for this state. The launcher does
not patch either game path.

If step 3 hits that error, record **live rejoin blocked by game UI**, not a Gravity
pass or failure. Step 5 exercises the distinct saved-lobby load path; passing it
does not establish live-rejoin compatibility. A live rejoin test needs a game build
with a working UI path or a separately scoped test harness for that path.

## Evidence

Logs are under `output/multiplayer/beta-test/{host,client}/`. Preserve the logs from
startup, disconnection, and reconnection. Look for Gravity load errors,
`Could not synchronize run settings`, `ClientRejoinResponseMessage`,
`ClientLoadJoinResponseMessage`, `RunInProgress`, and state-divergence errors.

The client's `gravity_run_settings.json` should contain
`"Settings":{"Requirement":2,"LockEncounters":false}`. The host save should
contain `gravity_encounters: 2` and `gravity_lock_encounters: false` in its extra
fields. The cache is supporting evidence only: a fallback can use remembered
settings, so inspect logs and verify actual map travel too.

Record outcomes separately for initial sync, encounter travel, live rejoin,
saved-lobby reconnect, and preservation of personal defaults. Do not mark an
unperformed step as passed.
