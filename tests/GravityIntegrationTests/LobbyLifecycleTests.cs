using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gravity;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

// Real lobby hooks and native message discovery/serialization; sockets, profile
// writes and scenes are fixtures. This does not establish live co-op compatibility.
internal static class LobbyLifecycleTests
{
    private static string cachePath = "";
    private static int warnings;

    public static void Run()
    {
        cachePath = Path.Combine(Path.GetTempPath(), $"gravity-run-settings-{Guid.NewGuid()}.json");
        var fixture = new Harmony("Gravity.LobbyLifecycleFixture");
        fixture.Patch(AccessTools.PropertyGetter(typeof(GravityRunSettingsCache), "CachePath"),
            prefix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(CacheLocation)));
        fixture.Patch(AccessTools.Method(typeof(GravitySettingsSync), "ShowWarning"),
            prefix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(RecordWarning)));
        fixture.Patch(AccessTools.Method(typeof(StartRunLobby), "UpdatePreferredAscension"),
            prefix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(Skip)));
        foreach (var name in new[] { "BeginRunForAllPlayers", "HandleLobbyBeginRunMessage" })
            fixture.Patch(AccessTools.Method(typeof(StartRunLobby), name),
                transpiler: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(WithoutLogging)));
        foreach (var name in new[] { "HandleLoadJoinResponseMessage", "HandleRejoinResponseMessage", "OnDisconnected" })
            fixture.Patch(AccessTools.Method(typeof(JoinFlow), name),
                transpiler: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(WithoutLogging)));
        fixture.Patch(AccessTools.Method(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer)),
            prefix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(Skip)) { priority = Priority.Last });
        // Allow the actual Gravity send prefixes to run without initializing the
        // native host socket, scene listener, or save profiles.
        fixture.Patch(AccessTools.Method(typeof(LoadRunLobby), "HandleClientLoadJoinRequestMessage"),
            prefix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(Skip)) { priority = Priority.Last });
        fixture.Patch(AccessTools.Method(typeof(RunLobby), "HandleClientRejoinRequestMessage"),
            prefix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(Skip)) { priority = Priority.Last });
        fixture.Patch(AccessTools.Method(typeof(MainFile), "Localize"),
            prefix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(English)));
        try
        {
            NewRuns();
            SavedRuns();
            SynchronizationFailures();
            ResumingFallback();
            ReceiverLifecycle();
        }
        finally
        {
            fixture.UnpatchAll(fixture.Id);
            GravitySettings.RestoreEncounterPreferences(15, true);
            GravitySettings.LockEncountersAfterBossUnlock = false;
            File.Delete(cachePath);
        }
        Console.WriteLine("Passed native start/load/rejoin transfer, warning-only fallback, remembered settings across sessions, preference isolation, disconnect cleanup, and another mod's messages.");
    }

    private static void NewRuns()
    {
        foreach (var requirement in new[] { 0, -1, 15, 99 })
        foreach (var locked in new[] { false, true })
        {
            var host = new Endpoint(true);
            var client = new Endpoint(false);
            RunState? clientRun = null;
            RunState? hostRun = null;
            StartRunLobby? clientLobby = null;
            StartRunLobby? hostLobby = null;
            var clientListener = Proxy<IStartRunLobbyListener>((method, args) =>
            {
                if (method.Name == "BeginRun") clientRun = Start(clientLobby!, (IReadOnlyList<ModifierModel>)args![2]!);
                return null;
            });
            var hostListener = Proxy<IStartRunLobbyListener>((method, args) =>
            {
                if (method.Name == "BeginRun") hostRun = Start(hostLobby!, (IReadOnlyList<ModifierModel>)args![2]!);
                return null;
            });
            clientLobby = Lobby(client.Service, clientListener);
            hostLobby = Lobby(host.Service, hostListener);
            hostLobby.Act1 = "overgrowth";
            var other = (OtherSettingsModifier)ModelDb.Modifier<OtherSettingsModifier>().ToMutable();
            other.CombatsLeft = 871;
            other.IsUsed = true;
            var original = new List<ModifierModel> { other };
            GravitySettings.RestoreEncounterPreferences(requirement, true);
            GravitySettings.LockEncountersAfterBossUnlock = locked;
            Invoke(hostLobby, "BeginRunForAllPlayers", "GRAVITYTEST", original);
            Check(host.Sent[0] is GravityRunSettingsMessage && host.Sent[1] is LobbyBeginRunMessage,
                "The snapshot must precede the native start on the reliable channel");
            Check(original.Count == 1 && ReferenceEquals(original[0], other), "Host modified another owner's modifier list");
            GravitySettings.RestoreEncounterPreferences(4, true);
            GravitySettings.LockEncountersAfterBossUnlock = !locked;
            var otherReceived = 0;
            client.Service.RegisterMessageHandler<OtherSettingsMessage>((m, _) => otherReceived = m.Value);
            client.Deliver(host.Sent[0]);
            client.Deliver(new OtherSettingsMessage { Value = 998877 });
            client.Deliver(host.Sent[1]);
            Check(otherReceived == 998877, "Gravity interfered with another mod's independent settings message");
            foreach (var run in new[] { clientRun, hostRun })
            {
                Check(run != null && GravityRunSettings.Get(run) == requirement
                    && GravityRunSettings.GetLockEncounters(run) == locked, "The actual lobby hooks lost host settings");
                Check(run!.Modifiers.Count == 1 && run.Modifiers.OfType<OtherSettingsModifier>().Single().CombatsLeft == 871,
                    "Gravity must preserve other modifiers and add none of its own");
            }
            Check(GravitySettings.NextRunRequirement == 4 && GravitySettings.LockEncountersAfterBossUnlock == !locked,
                "Host snapshot changed the client's personal defaults");
        }
    }

    private static StartRunLobby Lobby(INetGameService service, IStartRunLobbyListener listener)
    {
        // Native constructors initialize Godot's Logger/OS and cursor state.
        var lobby = (StartRunLobby)RuntimeHelpers.GetUninitializedObject(typeof(StartRunLobby));
        AccessTools.Field(typeof(StartRunLobby), "<NetService>k__BackingField").SetValue(lobby, service);
        AccessTools.Field(typeof(StartRunLobby), "<LobbyListener>k__BackingField").SetValue(lobby, listener);
        var players = AccessTools.Field(typeof(StartRunLobby), "<Players>k__BackingField");
        players.SetValue(lobby, Activator.CreateInstance(players.FieldType));
        AccessTools.Field(typeof(StartRunLobby), "<GameMode>k__BackingField").SetValue(lobby, GameMode.Standard);
        AccessTools.Method(typeof(RegisterGravitySettingsReceiverPatch), "Postfix").Invoke(null, [service]);
        service.RegisterMessageHandler<LobbyBeginRunMessage>((message, sender) =>
            Invoke(lobby, "HandleLobbyBeginRunMessage", message, sender));
        return lobby;
    }

    private static JoinFlow Join(INetGameService service)
    {
        var join = (JoinFlow)RuntimeHelpers.GetUninitializedObject(typeof(JoinFlow));
        AccessTools.Field(typeof(JoinFlow), "<NetService>k__BackingField").SetValue(join, service);
        AccessTools.Method(typeof(RegisterGravitySettingsReceiverPatch), "Postfix").Invoke(null, [service]);
        // Begin normally subscribes this native handler before requesting a run.
        service.Disconnected += info => Invoke(join, "OnDisconnected", info);
        return join;
    }

    private static RunState Start(StartRunLobby lobby, IReadOnlyList<ModifierModel> modifiers)
    {
        var state = SettingsTests.NewState(modifiers);
        var manager = (RunManager)RuntimeHelpers.GetUninitializedObject(typeof(RunManager));
        manager.SetUpNewMultiplayer(state, lobby, false);
        return state;
    }

    private static void SavedRuns()
    {
        foreach (var rejoin in new[] { false, true })
        {
            var host = new Endpoint(true);
            var client = new Endpoint(false); // A fresh client has no earlier snapshot.
            var join = Join(client.Service);
            var run = SettingsTests.NewState();
            GravityRunSettings.Set(run, 27, true);
            var player = (MegaCrit.Sts2.Core.Entities.Players.Player)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
            AccessTools.Field(player.GetType(), "<NetId>k__BackingField").SetValue(player, 2UL);
            AccessTools.Field(typeof(RunState), "_players").SetValue(run, new List<MegaCrit.Sts2.Core.Entities.Players.Player> { player });
            // The minimal fixture save has no scene-backed player inventory.
            var save = SettingsTests.Save(SettingsTests.NewState());
            GravityRunSettings.Store(save.ExtraFields, GravityRunSettings.GetSnapshot(run));
            save.Players = [new SerializablePlayer { NetId = 2UL }];
            GravitySettings.RestoreEncounterPreferences(2, true);
            GravitySettings.LockEncountersAfterBossUnlock = false;
            if (!rejoin)
            {
                var lobby = (LoadRunLobby)RuntimeHelpers.GetUninitializedObject(typeof(LoadRunLobby));
                AccessTools.Field(typeof(LoadRunLobby), "<NetService>k__BackingField").SetValue(lobby, host.Service);
                AccessTools.Field(typeof(LoadRunLobby), "<Run>k__BackingField").SetValue(lobby, save);
                Invoke(lobby, "HandleClientLoadJoinRequestMessage", default(ClientLoadJoinRequestMessage), 2UL);
            }
            else
            {
                var lobby = (RunLobby)RuntimeHelpers.GetUninitializedObject(typeof(RunLobby));
                AccessTools.Field(typeof(RunLobby), "_netService").SetValue(lobby, host.Service);
                AccessTools.Field(typeof(RunLobby), "_playerCollection").SetValue(lobby, run);
                Invoke(lobby, "HandleClientRejoinRequestMessage", default(ClientRejoinRequestMessage), 2UL);
            }
            Check(host.Sent.Count == 1, "A joining player must receive one saved snapshot");
            client.Deliver(host.Sent.Single());
            save.Players = [];
            var wireSave = RoundTripSave(save);
            if (!rejoin)
            {
                var completion = new TaskCompletionSource<ClientLoadJoinResponseMessage>();
                AccessTools.Field(typeof(JoinFlow), "_loadJoinCompletion").SetValue(join, completion);
                Invoke(join, "HandleLoadJoinResponseMessage", new ClientLoadJoinResponseMessage { serializableRun = wireSave }, 1UL);
                Check(completion.Task.IsCompletedSuccessfully, "Native load flow did not accept the snapshot");
            }
            else
            {
                var completion = new TaskCompletionSource<ClientRejoinResponseMessage>();
                AccessTools.Field(typeof(JoinFlow), "_rejoinCompletion").SetValue(join, completion);
                Invoke(join, "HandleRejoinResponseMessage", new ClientRejoinResponseMessage { serializableRun = wireSave }, 1UL);
                Check(completion.Task.IsCompletedSuccessfully, "Native rejoin flow did not accept the snapshot");
            }
            var restored = RunState.FromSerializable(wireSave);
            Check(GravityRunSettings.Get(restored) == 27 && GravityRunSettings.GetLockEncounters(restored),
                "Joining must restore the run snapshot, not the host's current defaults");
            Check(GravitySettings.NextRunRequirement == 2 && !GravitySettings.LockEncountersAfterBossUnlock,
                "Joining changed personal defaults");
        }
    }

    private static SerializableRun RoundTripSave(SerializableRun save)
    {
        var writer = new PacketWriter();
        save.Serialize(writer);
        var reader = new PacketReader();
        reader.Reset(writer.Buffer);
        var result = new SerializableRun();
        result.Deserialize(reader);
        return result;
    }

    private static void SynchronizationFailures()
    {
        // Fault injection, not normal packet loss: all native flows continue,
        // warn once, and capture the fallback without changing preferences.
        foreach (var requirement in new int?[] { null, -22, 1001 })
        foreach (var flow in new[] { "start", "load", "rejoin" })
        {
            File.Delete(cachePath);
            warnings = 0;
            GravitySettings.RestoreEncounterPreferences(31, true);
            GravitySettings.LockEncountersAfterBossUnlock = true;
            var client = new Endpoint(false);
            RunState? run = null;
            if (flow == "start")
            {
                StartRunLobby? lobby = null;
                lobby = Lobby(client.Service, Proxy<IStartRunLobbyListener>((method, args) =>
                {
                    if (method.Name == "BeginRun") run = Start(lobby!, (IReadOnlyList<ModifierModel>)args![2]!);
                    return null;
                }));
                if (requirement is int count) client.Deliver(new GravityRunSettingsMessage { Requirement = count });
                Invoke(lobby, "BeginRunLocally", "GRAVITYTEST", new List<ModifierModel>());
            }
            else
            {
                var join = Join(client.Service);
                var save = RoundTripSave(SettingsTests.Save(SettingsTests.NewState()));
                if (requirement is int count) client.Deliver(new GravityRunSettingsMessage { Requirement = count });
                run = CompleteJoin(join, save, flow == "rejoin");
            }
            Check(run != null && GravityRunSettings.GetSnapshot(run) == new GravitySettingsSnapshot(31, true),
                "Missing or invalid host data must continue with current settings");
            Check(warnings == 1, "A failed transfer must show one warning");
            Check(!client.Disconnected && client.Sent.Count == 0, "Fallback must neither disconnect nor request a re-sync");
            Check(GravitySettings.NextRunRequirement == 31 && GravitySettings.LockEncountersAfterBossUnlock,
                "Fallback must not change personal defaults");
            GravitySettings.RestoreEncounterPreferences(2, true);
            GravitySettings.LockEncountersAfterBossUnlock = false;
            client.Deliver(new GravityRunSettingsMessage { Requirement = 99 });
            Check(GravityRunSettings.GetSnapshot(run!) == new GravitySettingsSnapshot(31, true),
                "Preference changes or late messages must not change an active run");
            var json = JsonSerializer.Serialize(SettingsTests.Save(run!), JsonSerializationUtility.Options);
            var restored = RunState.FromSerializable(JsonSerializer.Deserialize<SerializableRun>(json, JsonSerializationUtility.Options)!);
            Check(GravityRunSettings.GetSnapshot(restored) == new GravitySettingsSnapshot(31, true),
                "The chosen fallback must survive disk save/load");
        }

        warnings = 0;
        var missingCapture = new Endpoint(false);
        var emptyLobby = Lobby(missingCapture.Service, Proxy<IStartRunLobbyListener>((_, _) => null));
        Check(GravityRunSettings.GetSnapshot(Start(emptyLobby, [])) == GravitySettingsSnapshot.FromPreferences()
            && warnings == 1 && !missingCapture.Disconnected, "A missing startup capture must also allow play");
    }

    private static RunState CompleteJoin(JoinFlow join, SerializableRun save, bool rejoin)
    {
        Task completion;
        if (!rejoin)
        {
            var source = new TaskCompletionSource<ClientLoadJoinResponseMessage>();
            AccessTools.Field(typeof(JoinFlow), "_loadJoinCompletion").SetValue(join, source);
            Invoke(join, "HandleLoadJoinResponseMessage", new ClientLoadJoinResponseMessage { serializableRun = save }, 1UL);
            completion = source.Task;
        }
        else
        {
            var source = new TaskCompletionSource<ClientRejoinResponseMessage>();
            AccessTools.Field(typeof(JoinFlow), "_rejoinCompletion").SetValue(join, source);
            Invoke(join, "HandleRejoinResponseMessage", new ClientRejoinResponseMessage { serializableRun = save }, 1UL);
            completion = source.Task;
        }
        Check(completion.IsCompletedSuccessfully, "The native join must finish without waiting or failing");
        return RunState.FromSerializable(save);
    }

    private static void ResumingFallback()
    {
        foreach (var rejoin in new[] { false, true })
        {
            var original = new Endpoint(false);
            var sync = GravitySettingsSync.For(original.Service);
            original.Deliver(new GravityRunSettingsMessage { Requirement = 27, LockEncounters = true });
            sync.Take("GRAVITYTEST");
            original.RaiseDisconnected();
            GravitySettings.RestoreEncounterPreferences(4, true);
            GravitySettings.LockEncountersAfterBossUnlock = false;
            warnings = 0;

            // New service, no pending snapshot, and changed preferences. Only
            // the on-disk cache connects this join to the previous session.
            var client = new Endpoint(false);
            var join = Join(client.Service);
            var save = RoundTripSave(SettingsTests.Save(SettingsTests.NewState()));
            var run = CompleteJoin(join, save, rejoin);
            Check(GravityRunSettings.GetSnapshot(run) == new GravitySettingsSnapshot(27, true)
                && warnings == 1 && !client.Disconnected, "Resume must prefer the remembered snapshot to new defaults");

            client.Deliver(new GravityRunSettingsMessage { Requirement = 99 });
            var next = RoundTripSave(SettingsTests.Save(SettingsTests.NewState()));
            run = CompleteJoin(join, next, rejoin);
            Check(GravityRunSettings.GetSnapshot(run) == new GravitySettingsSnapshot(99, false)
                && warnings == 1, "A fresh host snapshot must override the remembered fallback without a warning");
            Check(GravityRunSettingsCache.Read(1, "GRAVITYTEST") == new GravitySettingsSnapshot(99, false),
                "Host settings must update the remembered run");
        }

        Check(GravityRunSettingsCache.Read(3, "GRAVITYTEST") == null
            && GravityRunSettingsCache.Read(1, "DIFFERENT") == null, "Do not reuse a different host or run's settings");
        var fresh = new Endpoint(false);
        Check(GravitySettingsSync.For(fresh.Service).Take("GRAVITYTEST") == GravitySettingsSnapshot.FromPreferences(),
            "A new run with the same seed must capture fresh defaults, not reuse the previous run");
        File.WriteAllText(cachePath, "{broken");
        Check(GravitySettingsSync.For(fresh.Service).Take("GRAVITYTEST", resuming: true) == GravitySettingsSnapshot.FromPreferences()
            && !fresh.Disconnected, "An unreadable cache must not prevent joining");
    }

    private static void ReceiverLifecycle()
    {
        var valid = new GravityRunSettingsMessage { Requirement = 99, LockEncounters = true };
        var client = new Endpoint(false);
        _ = Join(client.Service);
        var sync = GravitySettingsSync.For(client.Service);
        client.Deliver(valid);
        client.Deliver(new GravityRunSettingsMessage { Requirement = 4 }, 3UL);
        Check(sync.Take("GRAVITYTEST") == new GravitySettingsSnapshot(99, true), "Only the host can supply run settings");
        Check(sync.Take("GRAVITYTEST") == GravitySettingsSnapshot.FromPreferences(), "A consumed snapshot must not be reused for a new run");
        Check(!client.Disconnected, "Missing settings must allow play");

        var cleanup = new Endpoint(false);
        _ = Join(cleanup.Service);
        cleanup.Deliver(valid);
        cleanup.RaiseDisconnected();
        Check(!cleanup.Handlers.ContainsKey(typeof(GravityRunSettingsMessage)), "Disconnect must unregister the receiver");
        Check(GravitySettingsSync.For(cleanup.Service).Take("GRAVITYTEST") == GravitySettingsSnapshot.FromPreferences(),
            "A new connection must not inherit a pending snapshot");
    }

    private static bool CacheLocation(ref string __result) { __result = cachePath; return false; }
    private static void RecordWarning() => warnings++;
    private static bool Skip() => false;
    private static bool English(string key, ref string __result) { __result = Localization.Get(key, "eng"); return false; }
    private static IEnumerable<CodeInstruction> WithoutLogging(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType?.Name is "Logger" or "Log")
            {
                for (var i = 0; i < method.GetParameters().Length + (method.IsStatic ? 0 : 1); i++)
                    yield return new CodeInstruction(OpCodes.Pop);
            }
            else yield return instruction;
        }
    }
    private static object? Invoke(object owner, string name, params object?[] args)
    {
        try { return AccessTools.Method(owner.GetType(), name).Invoke(owner, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> callback) where T : class
    {
        var instance = DispatchProxy.Create<T, LobbyProxy>();
        ((LobbyProxy)(object)instance).Callback = callback;
        return instance;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class Endpoint
    {
        internal INetGameService Service { get; }
        internal List<INetMessage> Sent { get; } = [];
        internal Dictionary<Type, List<Delegate>> Handlers { get; } = [];
        internal bool Disconnected;
        private Action<NetErrorInfo>? disconnected;
        private readonly FakeTransport transport = new();

        internal Endpoint(bool host)
        {
            Service = host ? Proxy<INetHostGameService>(Call) : Proxy<INetClientGameService>(Call);
            object? Call(MethodInfo method, object?[]? args)
            {
                switch (method.Name)
                {
                    case "get_Type": return host ? NetGameType.Host : NetGameType.Client;
                    case "get_NetId": return host ? 1UL : 2UL;
                    case "get_IsConnected": return !Disconnected;
                    case "get_NetClient": return transport;
                    case "SendMessage": Sent.Add((INetMessage)args![0]!); break;
                    case "RegisterMessageHandler":
                        var type = method.GetGenericArguments()[0];
                        if (!Handlers.ContainsKey(type)) Handlers[type] = [];
                        Handlers[type].Add((Delegate)args![0]!);
                        break;
                    case "UnregisterMessageHandler":
                        var key = method.GetGenericArguments()[0];
                        if (Handlers.TryGetValue(key, out var list))
                        {
                            list.Remove((Delegate)args![0]!);
                            if (list.Count == 0) Handlers.Remove(key);
                        }
                        break;
                    case "Disconnect": RaiseDisconnected((NetError)args![0]!); break;
                    case "add_Disconnected": disconnected += (Action<NetErrorInfo>)args![0]!; break;
                    case "remove_Disconnected": disconnected -= (Action<NetErrorInfo>)args![0]!; break;
                }
                return null;
            }
        }

        internal void Deliver(INetMessage message, ulong sender = 1)
        {
            var writer = new PacketWriter();
            writer.WriteByte((byte)message.ToId());
            message.Serialize(writer);
            writer.WriteInt(123456);
            var reader = new PacketReader();
            reader.Reset(writer.Buffer);
            Check(MessageTypes.TryGetMessageType(reader.ReadByte(), out var type), "Native message ID was not registered");
            var received = (INetMessage)Activator.CreateInstance(type!)!;
            received.Deserialize(reader);
            Check(reader.ReadInt() == 123456, "Message lost packet alignment");
            foreach (var handler in Handlers[type!].ToArray())
            {
                try { handler.DynamicInvoke(received, sender); }
                catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
            }
        }
        internal void RaiseDisconnected(NetError reason = NetError.Quit)
        {
            Disconnected = true;
            disconnected?.Invoke(new NetErrorInfo(reason, true));
        }
    }

    private sealed class FakeTransport() : NetClient(null!)
    {
        public override bool IsConnected => true;
        public override ulong NetId => 2;
        public override ulong HostNetId => 1;
        public override void Update() { }
        public override void SendMessageToHost(byte[] bytes, int length, NetTransferMode mode, int channel = 0) { }
        public override void DisconnectFromHost(NetError reason, bool now = false) { }
        public override string? GetRawLobbyIdentifier() => "test";
    }
}

public class LobbyProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Callback { get; set; } = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Callback(method!, args);
}

public struct OtherSettingsMessage : INetMessage
{
    public int Value;
    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;
    public void Serialize(PacketWriter writer) => writer.WriteInt(Value);
    public void Deserialize(PacketReader reader) => Value = reader.ReadInt();
}
