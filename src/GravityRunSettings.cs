using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Gravity;

// Attach the snapshot to the game's extra fields so it follows normal saves,
// canonicalization, cloud sync, replays, and multiplayer reconnects.
internal static class GravityRunSettings
{
    private sealed record Rule(int Requirement, bool LockEncounters);
    private static readonly ConditionalWeakTable<object, Rule> Rules = new();
    public static int Get(object? owner) => owner != null && Rules.TryGetValue(owner, out var rule) ? rule.Requirement : 15;
    public static bool GetLockEncounters(object? owner) => owner != null
        && Rules.TryGetValue(owner, out var rule) && rule.LockEncounters;
    public static void Set(object owner, int value, bool lockEncounters = false)
    {
        Rules.Remove(owner);
        Rules.Add(owner, new Rule(value is >= -1 and <= 1000 ? value : 15, lockEncounters));
    }
    public static void Initialize(RunState run)
    {
        if (!Rules.TryGetValue(run.ExtraFields, out _))
            Set(run.ExtraFields, GravitySettings.NextRunRequirement, GravitySettings.LockEncountersAfterBossUnlock);
    }
    public static void Copy(object source, object destination) => Set(destination, Get(source), GetLockEncounters(source));
}

[HarmonyPatch(typeof(RunManager), "InitializeNewRun")]
internal static class NewRunSettingsPatch
{
    private static void Prefix(RunManager __instance) => GravityRunSettings.Initialize(__instance.DebugOnlyGetState()!);
}

[HarmonyPatch(typeof(ExtraRunFields), nameof(ExtraRunFields.ToSerializable))]
internal static class SaveRunSettingsPatch
{
    private static void Postfix(ExtraRunFields __instance, SerializableExtraRunFields __result) => GravityRunSettings.Copy(__instance, __result);
}

[HarmonyPatch(typeof(ExtraRunFields), nameof(ExtraRunFields.FromSerializable))]
internal static class LoadRunSettingsPatch
{
    private static void Postfix(SerializableExtraRunFields save, ExtraRunFields __result) => GravityRunSettings.Copy(save, __result);
}

[HarmonyPatch(typeof(JsonSerializationUtility), nameof(JsonSerializationUtility.AlphabetizeProperties))]
internal static class RunSettingsJsonPatch
{
    private static void Prefix(JsonTypeInfo info)
    {
        if (info.Type != typeof(SerializableExtraRunFields)) return;
        var property = info.CreateJsonPropertyInfo(typeof(int), "gravity_encounters");
        property.Get = owner => GravityRunSettings.Get(owner);
        property.Set = (owner, value) => GravityRunSettings.Set(owner, (int)value!, GravityRunSettings.GetLockEncounters(owner));
        info.Properties.Add(property);
        var lockProperty = info.CreateJsonPropertyInfo(typeof(bool), "gravity_lock_encounters");
        lockProperty.Get = owner => GravityRunSettings.GetLockEncounters(owner);
        lockProperty.Set = (owner, value) => GravityRunSettings.Set(owner, GravityRunSettings.Get(owner), (bool)value!);
        info.Properties.Add(lockProperty);
    }
}

[HarmonyPatch(typeof(SerializableExtraRunFields), nameof(SerializableExtraRunFields.Serialize))]
internal static class RunSettingsPacketWritePatch
{
    private static void Postfix(SerializableExtraRunFields __instance, PacketWriter writer)
    {
        writer.WriteInt(GravityRunSettings.Get(__instance));
        writer.WriteBool(GravityRunSettings.GetLockEncounters(__instance));
    }
}

[HarmonyPatch(typeof(SerializableExtraRunFields), nameof(SerializableExtraRunFields.Deserialize))]
internal static class RunSettingsPacketReadPatch
{
    private static void Postfix(SerializableExtraRunFields __instance, PacketReader reader) =>
        GravityRunSettings.Set(__instance, reader.ReadInt(), reader.ReadBool());
}

// The host's choice travels with the start message, before any client creates its run.
// The list retains its identity when this struct message is copied by the network layer.
[HarmonyPatch(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Serialize))]
internal static class LobbySettingsWritePatch
{
    private static void Postfix(PacketWriter writer)
    {
        writer.WriteInt(GravitySettings.NextRunRequirement);
        writer.WriteBool(GravitySettings.LockEncountersAfterBossUnlock);
    }
}

[HarmonyPatch(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Deserialize))]
internal static class LobbySettingsReadPatch
{
    private static void Postfix(LobbyBeginRunMessage __instance, PacketReader reader) =>
        GravityRunSettings.Set(__instance.modifiers, reader.ReadInt(), reader.ReadBool());
}

[HarmonyPatch(typeof(StartRunLobby), "HandleLobbyBeginRunMessage")]
internal static class ReceiveLobbySettingsPatch
{
    private static void Prefix(StartRunLobby __instance, LobbyBeginRunMessage message) => GravityRunSettings.Copy(message.modifiers, __instance);
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
internal static class MultiplayerRunSettingsPatch
{
    private static void Prefix(RunState state, StartRunLobby lobby)
    {
        if (lobby.NetService.Type == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Client)
            GravityRunSettings.Copy(lobby, state.ExtraFields);
        else GravityRunSettings.Set(state.ExtraFields, GravitySettings.NextRunRequirement, GravitySettings.LockEncountersAfterBossUnlock);
    }
}
