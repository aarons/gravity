using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Gravity;

internal sealed record GravitySettingsSnapshot(int Requirement, bool LockEncounters)
{
    internal const int Version = 1;
    internal bool IsValid => Requirement is >= -1 and <= 1000;
    internal static GravitySettingsSnapshot Default => new(15, false);
    internal static GravitySettingsSnapshot FromPreferences() =>
        new(GravitySettings.NextRunRequirement, GravitySettings.LockEncountersAfterBossUnlock);
}

internal static class GravityRunSettings
{
    private sealed class Data
    {
        internal GravitySettingsSnapshot? Snapshot;
        internal bool FromNetwork;
        internal int? LegacyRequirement;
        internal bool? LegacyLock;
        internal int? Version;
    }

    // Run data belongs to the run's extra fields, never to a gameplay model or
    // the player's preferences. The same immutable value follows save conversions.
    private static readonly ConditionalWeakTable<object, Data> Fields = new();
    private static readonly ConditionalWeakTable<StartRunLobby, GravitySettingsSnapshot> Starting = new();
    private static readonly ModelId LegacyId = new("MODIFIER", "GRAVITY_SETTINGS_MODIFIER");

    internal static GravitySettingsSnapshot GetSnapshot(RunState run) =>
        Fields.GetOrCreateValue(run.ExtraFields).Snapshot
        ?? throw new InvalidOperationException("Gravity run settings have not been initialized.");

    public static int Get(RunState run) => GetSnapshot(run).Requirement;
    public static bool GetLockEncounters(RunState run) => GetSnapshot(run).LockEncounters;

    internal static void Set(RunState run, int requirement, bool locked = false) =>
        Store(run.ExtraFields, new(requirement, locked));

    internal static void Store(object fields, GravitySettingsSnapshot snapshot)
    {
        if (!snapshot.IsValid) throw new InvalidDataException("Invalid Gravity run settings.");
        Fields.GetOrCreateValue(fields).Snapshot = snapshot;
    }

    // Only singleplayer may initialize from personal preferences.
    public static void Initialize(RunState run)
    {
        var data = Fields.GetOrCreateValue(run.ExtraFields);
        data.Snapshot ??= GravitySettingsSnapshot.FromPreferences();
    }

    internal static void Capture(StartRunLobby lobby, GravitySettingsSnapshot snapshot)
    {
        Starting.Remove(lobby);
        Starting.Add(lobby, snapshot);
    }

    internal static void InitializeMultiplayer(RunState run, StartRunLobby lobby)
    {
        if (!Starting.TryGetValue(lobby, out var snapshot))
            throw GravitySettingsSync.Missing(lobby.NetService);
        Store(run.ExtraFields, snapshot);
        Starting.Remove(lobby);
    }

    internal static void Copy(object source, object destination)
    {
        if (Fields.TryGetValue(source, out var data) && data.Snapshot != null)
            Store(destination, data.Snapshot);
    }

    internal static GravitySettingsSnapshot ReadSave(SerializableRun save)
    {
        var data = Fields.GetOrCreateValue(save.ExtraFields);
        var legacy = save.Modifiers.Where(m => m.Id == LegacyId).ToArray();
        if (data.Snapshot == null)
        {
            // A network run must be paired with its separate settings message.
            // Disk saves predating Gravity settings use the original fixed rules.
            if (data.FromNetwork) throw new InvalidDataException("Missing Gravity settings for a network run.");
            if (data.Version is int version)
            {
                if (version != GravitySettingsSnapshot.Version || data.LegacyRequirement is not int count
                    || count is < -1 or > 1000 || data.LegacyLock is not bool locked)
                    throw new JsonException("Invalid or unsupported Gravity run settings.");
                Store(save.ExtraFields, new(count, locked));
            }
            else if (legacy.Length > 0)
            {
                var counts = legacy[0].Props?.ints?.Where(p => p.name == "CombatsLeft").ToArray();
                var locks = legacy[0].Props?.bools?.Where(p => p.name == "IsUsed").ToArray();
                if (legacy.Length != 1 || counts?.Length != 1 || locks?.Length != 1)
                    throw new InvalidDataException("Invalid legacy Gravity settings.");
                Store(save.ExtraFields, new(counts[0].value, locks[0].value));
            }
            else
            {
                Store(save.ExtraFields, data.LegacyRequirement is int requirement
                    ? new(requirement is >= -1 and <= 1000 ? requirement : 15, data.LegacyLock ?? false)
                    : GravitySettingsSnapshot.Default);
            }
        }
        // Remove only our retired data model, before the game resolves model IDs.
        // Leave every other modifier and its saved properties untouched.
        if (legacy.Length > 0) save.Modifiers = save.Modifiers.Where(m => m.Id != LegacyId).ToList();
        return data.Snapshot!;
    }

    internal static void MarkNetwork(object fields) => Fields.GetOrCreateValue(fields).FromNetwork = true;

    internal static void ConfigureJson(JsonTypeInfo info)
    {
        if (info.Type != typeof(SerializableExtraRunFields)) return;
        // Use the game's existing primitive JSON metadata; no replacement
        // serializer, model registration, or extension of binary save packets.
        var version = info.CreateJsonPropertyInfo(typeof(int), "gravity_version");
        version.Get = _ => GravitySettingsSnapshot.Version;
        version.Set = (owner, value) => Fields.GetOrCreateValue(owner).Version = (int)value!;
        version.ShouldSerialize = (owner, _) => Fields.GetOrCreateValue(owner).Snapshot != null;
        info.Properties.Add(version);

        var count = info.CreateJsonPropertyInfo(typeof(int), "gravity_encounters");
        count.Get = owner => Fields.GetOrCreateValue(owner).Snapshot?.Requirement ?? 15;
        count.Set = (owner, value) => Fields.GetOrCreateValue(owner).LegacyRequirement = (int)value!;
        count.ShouldSerialize = version.ShouldSerialize;
        info.Properties.Add(count);

        var locked = info.CreateJsonPropertyInfo(typeof(bool), "gravity_lock_encounters");
        locked.Get = owner => Fields.GetOrCreateValue(owner).Snapshot?.LockEncounters ?? false;
        locked.Set = (owner, value) => Fields.GetOrCreateValue(owner).LegacyLock = (bool)value!;
        locked.ShouldSerialize = version.ShouldSerialize;
        info.Properties.Add(locked);
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSingleplayer))]
internal static class SoloRunSettingsPatch
{
    private static void Prefix(RunState state) => GravityRunSettings.Initialize(state);
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
internal static class MultiplayerRunSettingsPatch
{
    private static void Prefix(RunState state, StartRunLobby lobby) => GravityRunSettings.InitializeMultiplayer(state, lobby);
}

[HarmonyPatch(typeof(RunState), nameof(RunState.FromSerializable))]
internal static class LoadRunSettingsPatch
{
    private static void Prefix(SerializableRun save) => GravityRunSettings.ReadSave(save);
}

[HarmonyPatch(typeof(ExtraRunFields), nameof(ExtraRunFields.ToSerializable))]
internal static class SaveRunSettingsPatch
{
    private static void Postfix(ExtraRunFields __instance, SerializableExtraRunFields __result) =>
        GravityRunSettings.Copy(__instance, __result);
}

[HarmonyPatch(typeof(ExtraRunFields), nameof(ExtraRunFields.FromSerializable))]
internal static class RestoreRunSettingsPatch
{
    private static void Postfix(SerializableExtraRunFields save, ExtraRunFields __result) =>
        GravityRunSettings.Copy(save, __result);
}

[HarmonyPatch(typeof(SerializableExtraRunFields), nameof(SerializableExtraRunFields.Deserialize))]
internal static class NetworkRunSettingsPatch
{
    private static void Postfix(SerializableExtraRunFields __instance) => GravityRunSettings.MarkNetwork(__instance);
}

[HarmonyPatch(typeof(JsonSerializationUtility), nameof(JsonSerializationUtility.AlphabetizeProperties))]
internal static class RunSettingsJsonPatch
{
    private static void Prefix(JsonTypeInfo info) => GravityRunSettings.ConfigureJson(info);
}
