# Saved modifier implementation handoff

Replace Gravity’s custom settings transport with a native saved `ModifierModel`.
The game already serializes modifiers in its start-run message and run saves, so
this should provide a simpler path for carrying the host’s fixed encounter rules.
The current fallback to client preferences avoids resetting to 15 but still allows
host/client disagreement. The original cause of missing settings remains unconfirmed.

Stable support is required, including host settings synchronization. Target one mod
build that works on both stable and beta; do not ship the migration with sync disabled
on stable. The README records passing existing settings-sync checks on v0.107.1;
the unverified part is the proposed saved-modifier replacement.

## Todo

- [ ] **Verify stable APIs first.** Inspect v0.107.1 modifier registration, saved
  properties, lobby transport, and load/rejoin behavior before implementing against
  beta APIs. Use the shared API surface and isolate any necessary branch differences
  in a small compatibility helper. Treat equivalent sync behavior on both branches
  as a release requirement.
- [ ] **Add the modifier.** Store encounter requirement and encounter locking as
  `[SavedProperty]` values. Keep appearance settings personal.
- [ ] **Capture rules once.** Add the modifier before the host starts the run; use
  local preferences for solo runs. Clients consume the host’s modifier. Later
  preference changes must not affect the active run.
- [ ] **Use the modifier consistently.** Read active rules from it for encounter
  selection, boss unlocking, and progress UI. Verify persistence across act
  changes, saves, loading, and reconnects.
- [ ] **Handle missing data explicitly.** Reject new multiplayer startup if the
  required snapshot is missing or invalid; show a concise error instead of
  substituting client preferences.
- [ ] **Preserve existing saves.** Migrate Gravity’s saved extra fields into the
  modifier. Retain historical defaults for saves without Gravity settings.
- [ ] **Remove superseded sync code.** Retire lobby packet extensions and temporary
  snapshot transfers once the replacement works. Keep only the legacy save-reading
  code needed for migration.
- [ ] **Check native integration.** Ensure the modifier does not unintentionally
  change normal-run classification, appear in random modifier selection, or
  introduce unwanted UI.
- [ ] **Validate and document.** Test deliberately different host/client settings,
  immediate startup, save/load, reconnects, and legacy saves on stable v0.107.1
  and beta v0.111.0. Exercise the full lobby-to-run transition, not just packet
  round trips. Test the same candidate DLL against both branches, using game-path
  overrides for the assembly checks. Update the README and require matching Gravity
  versions across peers. Cross-branch multiplayer is not implied by supporting both
  branches separately.

## Starting points

- Current implementation: `src/GravityRunSettings.cs`.
- Existing coverage: `tests/GravityIntegrationTests/SettingsTests.cs`.
- Native v0.111.0: `ModifierModel.ToSerializable()` captures saved properties;
  `ModifierModel.FromSerializable()` restores them. `LobbyBeginRunMessage.modifiers`
  already carries serialized modifiers.
- Existing mod example:
  [Ban Enemy Mod snapshot modifier](https://github.com/starrysky9959/slay-the-spire-2-ban-enemy-mod/blob/main/BanEnemyModCode/Models/BanEncounterSnapshotModifier.cs)
  and [lobby integration](https://github.com/starrysky9959/slay-the-spire-2-ban-enemy-mod/blob/main/BanEnemyModCode/Patches/StartRunLobbySyncPatches.cs).

Native serialization was inspected on v0.111.0. Full lifecycle behavior and stable
compatibility still need verification; the upstream example is a reference, not
proof of compatibility or reliability for Gravity.
