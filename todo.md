**I recommend replacing the settings modifier with dedicated Gravity run data and a dedicated settings message.** The Neow fix addresses the symptom, but I would treat it as an interim fix.

I should have caught this earlier: a gameplay modifier isn’t a neutral storage container. Even without combat hooks, its presence changes what the game—and potentially other mods—sees. Hiding it from individual consumers leaves us chasing exceptions.

The design I’d use:

- **Separate run state.** Store an immutable Gravity settings snapshot associated with the run, outside `RunState.Modifiers`. Keep personal defaults separate.
- **Dedicated multiplayer synchronization.** The host sends a versioned Gravity settings message. Clients validate and acknowledge it before gameplay starts. Repeat that exchange on reconnect, using the run’s saved settings.
- **Dedicated save data.** Persist a namespaced Gravity payload alongside the run’s serialized data. Keep disk persistence separate from network synchronization; adding JSON fields doesn’t automatically include them in reconnect packets.
- **Explicit synchronization failures.** A client missing the host’s settings should wait or report an error. Our current fallback to the client’s personal defaults can silently give players different rules and should go.

Your installed game already discovers mod-defined network messages and provides send/receive handlers, so there’s a plausible native extension point. However, those messages participate in a shared ID registry. We would need to verify compatibility across supported game versions and mod combinations; I wouldn’t promise isolation merely because the message is custom.

Harmony may still be needed for narrow lifecycle hooks—creating, saving, loading, and reconnecting runs. The architectural goal is that **settings transport requires no patches to Ancient choices, modifier displays, or modifier validation**, and never rewrites another mod’s payload or serialization registry.

Before shipping that replacement, I’d require tests for host/client defaults differing, reconnects, save migration, missing or stale messages, and another mod transferring its own settings. Existing saves would need a migration that reads and removes only Gravity’s old modifier.
