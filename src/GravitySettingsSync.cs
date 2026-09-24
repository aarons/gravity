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
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Gravity;

// Discovered by the game's MessageTypes.Initialize, like other mod messages.
// Use the same reliable channel and buffering as the native start/join response.
public struct GravityRunSettingsMessage : INetMessage
{
    internal int Requirement;
    internal bool LockEncounters;

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(Requirement);
        writer.WriteBool(LockEncounters);
    }

    public void Deserialize(PacketReader reader)
    {
        Requirement = reader.ReadInt();
        LockEncounters = reader.ReadBool();
    }
}

internal sealed class GravitySettingsSync
{
    private static readonly ConditionalWeakTable<INetGameService, GravitySettingsSync> Sessions = new();
    private readonly INetGameService service;
    private GravitySettingsSnapshot? pending;

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
        pending = new(message.Requirement, message.LockEncounters);
    }

    internal void Send(GravitySettingsSnapshot snapshot, ulong? peer = null)
    {
        if (service.Type != NetGameType.Host) throw new InvalidOperationException("Only the host can send Gravity settings.");
        var message = new GravityRunSettingsMessage
        {
            Requirement = snapshot.Requirement, LockEncounters = snapshot.LockEncounters,
        };
        if (peer is ulong id) service.SendMessage(message, id);
        else service.SendMessage(message);
    }

    // The next native start/join response consumes the preceding host snapshot.
    internal GravitySettingsSnapshot Take(string seed, bool resuming = false)
    {
        var snapshot = pending;
        pending = null;
        var host = ((INetClientGameService)service).NetClient?.HostNetId;
        if (snapshot == null || !snapshot.IsValid)
            snapshot = Fallback(resuming && host is ulong id ? GravityRunSettingsCache.Read(id, seed) : null);
        if (host is ulong hostId) GravityRunSettingsCache.Store(hostId, seed, snapshot);
        return snapshot;
    }

    internal static void Restore(INetGameService service, SerializableRun save) =>
        GravityRunSettings.Store(save.ExtraFields, For(service).Take(save.SerializableRng.Seed!, resuming: true));

    internal static GravitySettingsSnapshot Fallback(GravitySettingsSnapshot? saved = null)
    {
        var snapshot = saved ?? GravitySettingsSnapshot.FromPreferences();
        Console.WriteLine($"[Gravity] Could not synchronize run settings. Continuing with {snapshot}.");
        ShowWarning();
        return snapshot;
    }

    private static void ShowWarning()
    {
        try
        {
            var container = NModalContainer.Instance;
            if (container == null) return;
            if (container.OpenModal is Godot.Node open)
            {
                open.TreeExited += () => Godot.Callable.From(ShowWarning).CallDeferred();
                return;
            }
            var popup = NErrorPopup.Create(MainFile.Localize("settings.title"),
                MainFile.Localize("settings.sync_warning"), showReportBugButton: false);
            if (popup != null) container.Add(popup);
        }
        catch (Exception error)
        {
            // A warning must never prevent the native start/join handler finishing.
            Console.WriteLine($"[Gravity] Could not show settings warning: {error.Message}");
        }
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
    private static void Prefix(StartRunLobby __instance, bool ____isBeginningRun)
    {
        if (__instance.NetService.Type == NetGameType.Client || ____isBeginningRun) return;
        var snapshot = GravitySettingsSnapshot.FromPreferences();
        GravityRunSettings.Capture(__instance, snapshot);
        if (__instance.NetService.Type == NetGameType.Host)
            GravitySettingsSync.For(__instance.NetService).Send(snapshot);
    }
}

[HarmonyPatch(typeof(StartRunLobby), "BeginRunLocally")]
internal static class ClientRunSettingsPatch
{
    private static void Prefix(StartRunLobby __instance, string seed)
    {
        if (__instance.NetService.Type == NetGameType.Client)
            GravityRunSettings.Capture(__instance,
                GravitySettingsSync.For(__instance.NetService).Take(seed));
    }
}

[HarmonyPatch(typeof(LoadRunLobby), "HandleClientLoadJoinRequestMessage")]
internal static class SendLoadedSettingsPatch
{
    private static void Prefix(LoadRunLobby __instance, ulong senderId)
    {
        if (__instance.NetService.Type != NetGameType.Host || !__instance.Run.Players.Any(p => p.NetId == senderId)) return;
        GravitySettingsSync.For(__instance.NetService).Send(GravityRunSettings.ReadSave(__instance.Run), senderId);
    }
}

[HarmonyPatch(typeof(RunLobby), "HandleClientRejoinRequestMessage")]
internal static class SendRejoinSettingsPatch
{
    private static void Prefix(INetGameService ____netService, IPlayerCollection ____playerCollection, ulong senderId)
    {
        if (____netService.Type != NetGameType.Host || ____playerCollection is not RunState run
            || run.GetPlayer(senderId) == null) return;
        GravitySettingsSync.For(____netService).Send(GravityRunSettings.GetSnapshot(run), senderId);
    }
}

[HarmonyPatch(typeof(JoinFlow), "HandleLoadJoinResponseMessage")]
internal static class ReceiveLoadedSettingsPatch
{
    private static void Prefix(JoinFlow __instance, ClientLoadJoinResponseMessage message) =>
        GravitySettingsSync.Restore(__instance.NetService, message.serializableRun);
}

[HarmonyPatch(typeof(JoinFlow), "HandleRejoinResponseMessage")]
internal static class ReceiveRejoinSettingsPatch
{
    private static void Prefix(JoinFlow __instance, ClientRejoinResponseMessage message) =>
        GravitySettingsSync.Restore(__instance.NetService, message.serializableRun);
}
