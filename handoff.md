# Saved modifier implementation handoff

Replace Gravity’s custom settings transport with a native saved `ModifierModel`.
The game already serializes modifiers in its start-run message and run saves, so
this is a candidate for carrying the host’s fixed encounter rules. It still needs
startup integration; native serialization alone does not establish reliability or
better compatibility with other mods.

Stable support is required, including host settings synchronization. Target one mod
build that works on both stable and beta; do not ship the migration with sync disabled
on stable. The README records passing isolated settings-sync checks on v0.107.1,
but end-to-end host-settings transfer has never been proven. Treat the existing
transfer as broken until demonstrated otherwise; its failure cause is unconfirmed.

## Agreed fallback and compatibility behavior

- Use the host’s snapshot when it is received and valid. If it is missing or invalid
  during new-run startup, use that peer’s **current local configured settings**,
  including encounter requirement and locking. For clients, these are the client’s
  settings, not fixed defaults such as 15. Snapshot the fallback once for the run;
  later preference changes must not alter it.
- Log fallback use for diagnosis, but do not reject startup or add a blocking error,
  settings-agreement handshake, or Gravity-specific compatibility gate. Rely on the
  game’s existing mod-presence/version requirements. Matching mod versions does not
  imply matching settings; players can communicate and align their preferences.
  The possibility of different rules after a local fallback is accepted.
- Preserve other mods’ data and startup behavior. Prefer narrow Harmony hooks over
  replacing entire startup methods. Carry only Gravity’s snapshot through standard
  startup, preserve existing modifiers, and avoid globally enabling all modifiers
  in standard runs. Do not indiscriminately reorder or rebuild shared serialization
  registries. Reliable host synchronization remains a testing/release requirement,
  rather than an additional runtime startup gate.

## Stable v0.107.1 findings

The installed stable assembly was inspected before implementation. An isolated
probe passed 14 combinations of integer requirement and boolean locking through
native run-save JSON and `LobbyBeginRunMessage` packet serialization, followed by
`ModifierModel.FromSerializable()`. The probe explicitly registered its model and
saved properties and seeded its model-ID packet mapping; it did not exercise game
mod discovery or the full lobby-to-run transition.

- `ModifierModel.ToSerializable()` and `FromSerializable()` support saved properties.
  Save loading restores modifiers, and reconnect responses carry the serialized run.
- Stable discovers mod models, but its saved-property cache initially includes only
  built-in types. Explicit registration through
  `SavedPropertiesTypeCache.InjectTypeIntoCache()` is available and required.
  Verify consistent property network IDs and sufficient bit width across peers,
  including when other mods register saved properties.
- `NCharacterSelectScreen.BeginRun()` receives modifiers, but standard startup
  subsequently passes an empty modifier list onward. Preserve Gravity’s snapshot
  across this transition explicitly; adding it to the lobby alone will not work.
- `StartRunLobby.SetModifiers()` invokes `ModifiersChanged()`, which throws in the
  standard character-select screen. Do not use that flow unchanged.
- The top bar renders every active modifier. Hide only Gravity’s internal settings
  modifier where needed. The built-in modifier selection pools are explicit lists,
  so registering the model does not automatically add it to those pools.

These findings establish API feasibility, not full lifecycle or multiplayer
compatibility. The migration is now implemented; see the implementation checkpoint below.

## Todo

- [ ] **Build on the stable audit.** Use the findings above to verify actual mod
  registration, startup, and load/rejoin behavior on v0.107.1. Compare with beta,
  use the shared API surface, and isolate necessary branch differences in a small
  compatibility helper. Treat equivalent sync behavior as a release requirement.
- [x] **Add the modifier.** Store encounter requirement and encounter locking as
  `[SavedProperty]` values and explicitly register them in the saved-property cache.
  Keep appearance settings personal.
- [x] **Capture rules once.** Add the modifier before the host starts the run; use
  local preferences for solo runs. Clients consume the host’s modifier. Later
  preference changes must not affect the active run. Preserve Gravity’s snapshot
  through the standard character-select-to-run transition without discarding or
  modifying other mods’ data.
- [x] **Use the modifier consistently.** Read active rules from it for encounter
  selection, boss unlocking, and progress UI. Verify persistence across act
  changes, saves, loading, and reconnects.
- [x] **Implement the agreed fallback.** If the new-run snapshot is missing or
  invalid, capture the peer’s current local requirement and locking settings and
  log the fallback. Let startup continue without an additional compatibility gate.
  Received valid host settings always take precedence, including a requirement of
  15 or locking disabled. Do not overwrite personal preferences.
- [x] **Preserve existing saves.** Migrate Gravity’s saved extra fields into the
  modifier. Retain historical defaults for saves without Gravity settings.
- [x] **Remove superseded sync code.** Retire custom lobby packet extensions and
  obsolete snapshot transfers once the replacement works. Retain the narrow bridge
  needed to preserve Gravity’s modifier through standard startup, plus legacy
  save-reading code needed for migration.
- [ ] **Check native integration.** Ensure the modifier does not unintentionally
  change normal-run classification, appear in random modifier selection, or
  introduce unwanted UI.
- [ ] **Validate and document.** Test deliberately different host/client settings,
  immediate startup, save/load, reconnects, and legacy saves on stable v0.107.1
  and beta v0.111.0. Exercise the full lobby-to-run transition, not just packet
  round trips. Also test missing/invalid snapshots using current local settings,
  fallback persistence, and coexistence with other multiplayer mods and saved
  properties. Test the same candidate DLL against both branches, using game-path
  overrides for the assembly checks. Update the README to describe host settings
  and the nonblocking local fallback accurately. Rely on existing game compatibility
  checks; do not add a Gravity-specific gate. Cross-branch multiplayer is not implied
  by supporting both branches separately.

## Starting points

- Current implementation: `src/GravityRunSettings.cs`.
- Existing coverage: `tests/GravityIntegrationTests/SettingsTests.cs`.
- Native v0.111.0: `ModifierModel.ToSerializable()` captures saved properties;
  `ModifierModel.FromSerializable()` restores them. `LobbyBeginRunMessage.modifiers`
  already carries serialized modifiers.
- Existing mod example:
  [Ban Enemy Mod snapshot modifier](https://github.com/starrysky9959/slay-the-spire-2-ban-enemy-mod/blob/main/BanEnemyModCode/Models/BanEncounterSnapshotModifier.cs)
  and [lobby integration](https://github.com/starrysky9959/slay-the-spire-2-ban-enemy-mod/blob/main/BanEnemyModCode/Patches/StartRunLobbySyncPatches.cs).

Native serialization was inspected on v0.111.0 and v0.107.1, with the isolated stable
probe described above. Full lifecycle behavior and the same candidate DLL on both
branches still need verification; the upstream example is a reference, not proof
of compatibility or reliability for Gravity.

## Implementation checkpoint

The saved-modifier migration is implemented in `src/GravitySettingsModifier.cs`
and `src/GravityRunSettings.cs`. The custom packet extensions and extra-field
writes are removed; legacy JSON fields are read during run-load migration.

To avoid perturbing other mods' saved-property registration, the internal model
uses private native wire-name tokens `CombatsLeft` (requirement) and `IsUsed`
(locking). Values are per model. These names already exist on stable; registration
adds only Gravity's type-to-property metadata, not global property names or IDs.
The registration check fails explicitly if a future game removes either token.
No shared registry is sorted, rebuilt, or resized by Gravity.

Host capture runs in a prefix on `BeginRunForAllPlayers`. `BeginRunLocally` captures
the received model per lobby. Narrow prefixes on the standard screen and game entry points bridge the discarded
modifier argument across the async transition (lobby identity in multiplayer, act-list
identity in solo). They preserve modifiers already supplied there and recover only Gravity. Multiplayer setup also covers
the bootstrap path. Standard-run classification is unchanged; top-bar display
filters only Gravity, without modifying the active modifier collection.

Stable offline coverage now includes native model discovery/ID setup (no manual
packet-ID seeding), unchanged saved-property IDs and width, 16 native lobby/save/
reconnect payload combinations, legacy migration, invalid/missing/duplicate
snapshots, fallback isolation, and another model's independent saved properties.
Eight lifecycle cases execute real host send, client receive, and lobby-local
startup methods with deliberately different defaults and a second packet extension.
The fixture invokes the patched screen and game entry points but replaces sockets,
profile writes, logging, and scene work; native save/load uses empty-player fixtures. This is not proof of live multiplayer.

Still required: real character-select transition and multiplayer acceptance on
stable, same-candidate beta checks, save/reconnect with real players, and testing
with actual multiplayer mods. No branch switch, game install, or publication was
performed as part of the implementation checkpoint. Update this section as those
checks are completed.
