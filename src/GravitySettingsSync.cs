using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Gravity;

internal enum GravitySettingsTransfer { NewRun, Load, Rejoin }

// Discovered by the game's MessageTypes.Initialize, like other mod messages.
// Use the same reliable channel and buffering as the native start/join response.
public struct GravityRunSettingsMessage : INetMessage
{
    internal int Version;
    internal ulong Sequence;
    internal GravitySettingsTransfer Transfer;
    internal string Seed;
    internal int Requirement;
    internal bool LockEncounters;

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(Version);
        writer.WriteULong(Sequence);
        writer.WriteInt((int)Transfer);
        writer.WriteString(Seed);
        writer.WriteInt(Requirement);
        writer.WriteBool(LockEncounters);
    }

    public void Deserialize(PacketReader reader)
    {
        Version = reader.ReadInt();
        Sequence = reader.ReadULong();
        Transfer = (GravitySettingsTransfer)reader.ReadInt();
        Seed = reader.ReadString();
        Requirement = reader.ReadInt();
        LockEncounters = reader.ReadBool();
    }
}

internal sealed class GravitySettingsSync
{
    private static readonly ConditionalWeakTable<INetGameService, GravitySettingsSync> Sessions = new();
    private readonly INetGameService service;
    private ulong sequence;
    private GravityRunSettingsMessage? pending;

    private GravitySettingsSync(INetGameService service)
    {
        this.service = service;
        if (service.Type == NetGameType.Client)
            service.RegisterMessageHandler<GravityRunSettingsMessage>(Receive);
        service.Disconnected += Disconnected;
    }

    internal static GravitySettingsSync For(INetGameService service) =>
        Sessions.GetValue(service, s => new GravitySettingsSync(s));

    private void Disconnected(NetErrorInfo _)
    {
        service.UnregisterMessageHandler<GravityRunSettingsMessage>(Receive);
        service.Disconnected -= Disconnected;
        pending = null;
        Sessions.Remove(service);
    }

    private void Receive(GravityRunSettingsMessage message, ulong senderId)
    {
        if (service is not INetClientGameService client || client.NetClient?.HostNetId != senderId) return;
        if (message.Version != GravitySettingsSnapshot.Version || message.Sequence <= sequence
            || !Enum.IsDefined(message.Transfer) || string.IsNullOrEmpty(message.Seed)
            || !new GravitySettingsSnapshot(message.Requirement, message.LockEncounters).IsValid
            || pending != null)
            throw Missing(service);
        sequence = message.Sequence;
        pending = message;
    }

    internal void Send(GravitySettingsSnapshot snapshot, GravitySettingsTransfer transfer, string seed, ulong? peer = null)
    {
        if (service.Type != NetGameType.Host) throw new InvalidOperationException("Only the host can send Gravity settings.");
        var message = new GravityRunSettingsMessage
        {
            Version = GravitySettingsSnapshot.Version, Sequence = ++sequence, Transfer = transfer,
            Seed = seed, Requirement = snapshot.Requirement, LockEncounters = snapshot.LockEncounters,
        };
        if (peer is ulong id) service.SendMessage(message, id);
        else service.SendMessage(message);
    }

    internal GravitySettingsSnapshot Take(GravitySettingsTransfer transfer, string? seed)
    {
        var message = pending;
        pending = null;
        if (message == null || message.Value.Transfer != transfer || message.Value.Seed != seed)
            throw Missing(service);
        return new(message.Value.Requirement, message.Value.LockEncounters);
    }

    internal static void Restore(INetGameService service, SerializableRun save, GravitySettingsTransfer transfer) =>
        GravityRunSettings.Store(save.ExtraFields, For(service).Take(transfer, save.SerializableRng.Seed));

    internal static InvalidDataException Missing(INetGameService service)
    {
        Console.WriteLine("[Gravity] Missing, stale, or incompatible host settings. Ending the connection.");
        service.Disconnect(NetError.InternalError);
        return new InvalidDataException(MainFile.Localize("settings.sync_error"));
    }
}

[HarmonyPatch]
internal static class RegisterGravitySettingsReceiverPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        AccessTools.GetDeclaredConstructors(typeof(StartRunLobby))
            .Concat(AccessTools.GetDeclaredConstructors(typeof(JoinFlow)));

    private static void Postfix(INetGameService netService)
    {
        if (netService.Type == NetGameType.Client) GravitySettingsSync.For(netService);
    }
}

[HarmonyPatch(typeof(StartRunLobby), "BeginRunForAllPlayers")]
internal static class HostRunSettingsPatch
{
    private static void Prefix(StartRunLobby __instance, string seed, bool ____isBeginningRun)
    {
        if (__instance.NetService.Type == NetGameType.Client || ____isBeginningRun) return;
        var snapshot = GravitySettingsSnapshot.FromPreferences();
        GravityRunSettings.Capture(__instance, snapshot);
        if (__instance.NetService.Type == NetGameType.Host)
            GravitySettingsSync.For(__instance.NetService).Send(snapshot, GravitySettingsTransfer.NewRun, seed);
    }
}

[HarmonyPatch(typeof(StartRunLobby), "BeginRunLocally")]
internal static class ClientRunSettingsPatch
{
    private static void Prefix(StartRunLobby __instance, string seed)
    {
        if (__instance.NetService.Type == NetGameType.Client)
            GravityRunSettings.Capture(__instance,
                GravitySettingsSync.For(__instance.NetService).Take(GravitySettingsTransfer.NewRun, seed));
    }
}

[HarmonyPatch(typeof(LoadRunLobby), "HandleClientLoadJoinRequestMessage")]
internal static class SendLoadedSettingsPatch
{
    private static void Prefix(LoadRunLobby __instance, ulong senderId)
    {
        if (__instance.NetService.Type != NetGameType.Host || !__instance.Run.Players.Any(p => p.NetId == senderId)) return;
        GravitySettingsSync.For(__instance.NetService).Send(GravityRunSettings.ReadSave(__instance.Run),
            GravitySettingsTransfer.Load, __instance.Run.SerializableRng.Seed!, senderId);
    }
}

[HarmonyPatch(typeof(RunLobby), "HandleClientRejoinRequestMessage")]
internal static class SendRejoinSettingsPatch
{
    private static void Prefix(INetGameService ____netService, IPlayerCollection ____playerCollection, ulong senderId)
    {
        if (____netService.Type != NetGameType.Host || ____playerCollection is not RunState run
            || run.GetPlayer(senderId) == null) return;
        GravitySettingsSync.For(____netService).Send(GravityRunSettings.GetSnapshot(run),
            GravitySettingsTransfer.Rejoin, run.Rng.StringSeed, senderId);
    }
}

[HarmonyPatch(typeof(JoinFlow), "HandleLoadJoinResponseMessage")]
internal static class ReceiveLoadedSettingsPatch
{
    private static void Prefix(JoinFlow __instance, ClientLoadJoinResponseMessage message) =>
        GravitySettingsSync.Restore(__instance.NetService, message.serializableRun, GravitySettingsTransfer.Load);
}

[HarmonyPatch(typeof(JoinFlow), "HandleRejoinResponseMessage")]
internal static class ReceiveRejoinSettingsPatch
{
    private static void Prefix(JoinFlow __instance, ClientRejoinResponseMessage message) =>
        GravitySettingsSync.Restore(__instance.NetService, message.serializableRun, GravitySettingsTransfer.Rejoin);
}
