using System.Text.Json;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Gravity;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

internal static class SettingsTests
{
    public static void Run()
    {
        VerifyPreferences();
        VerifyLobbyMessageTransport();
        VerifyMissingLobbySettings();
        var info = JsonSerializationUtility.Options.GetTypeInfo(typeof(SerializableExtraRunFields));
        foreach (var requirement in new[] { -1, 0, 1, 15, 60, 99, 999, 1000 })
        foreach (var lockEncounters in new[] { false, true })
        {
            var fields = new ExtraRunFields { StartedWithNeow = true, TestSubjectKills = 3, FreedRepy = true };
            GravityRunSettings.Set(fields, requirement, lockEncounters);
            var save = fields.ToSerializable();
            var json = JsonSerializer.Serialize(save, info);
            using var document = JsonDocument.Parse(json);
            Check(document.RootElement.TryGetProperty("gravity_encounters", out var stored) && stored.GetInt32() == requirement,
                "Rule missing from native JSON metadata: " + json);
            var restored = (SerializableExtraRunFields)JsonSerializer.Deserialize(json, info)!;
            var loaded = ExtraRunFields.FromSerializable(restored);
            Check(GravityRunSettings.Get(loaded) == requirement, "Requirement must survive native JSON save/load");
            Check(document.RootElement.GetProperty("gravity_lock_encounters").GetBoolean() == lockEncounters
                && GravityRunSettings.GetLockEncounters(loaded) == lockEncounters,
                "Encounter lock must survive native JSON save/load");
            Check(loaded.StartedWithNeow && loaded.TestSubjectKills == 3 && loaded.FreedRepy, "Native extra fields must survive");
            var runJson = JsonSerializationUtility.ToJson(new SerializableRun { ExtraFields = save });
            var runSave = JsonSerializer.Deserialize<SerializableRun>(runJson, JsonSerializationUtility.Options)!;
            Check(GravityRunSettings.Get(runSave.ExtraFields) == requirement, "Full run save dropped its encounter snapshot");
            Check(GravityRunSettings.GetLockEncounters(runSave.ExtraFields) == lockEncounters, "Full run save dropped its lock snapshot");

            var writer = new PacketWriter();
            save.Serialize(writer);
            writer.WriteInt(123456); // Catch accidental packet misalignment after our extension.
            var reader = new PacketReader();
            reader.Reset(writer.Buffer);
            var network = new SerializableExtraRunFields();
            network.Deserialize(reader);
            Check(GravityRunSettings.Get(network) == requirement && reader.ReadInt() == 123456, "Network round trip lost run settings or packet alignment");
            Check(GravityRunSettings.GetLockEncounters(network) == lockEncounters, "Network round trip lost encounter lock");

            var nextRunRequirement = requirement == 1000 ? 999 : requirement;
            GravitySettings.RestoreEncounterPreferences(nextRunRequirement, true);
            GravitySettings.LockEncountersAfterBossUnlock = lockEncounters;
            var message = new LobbyBeginRunMessage { playersInLobby = [], modifiers = [], seed = "gravity-test", act1 = "test" };
            writer.Reset();
            message.Serialize(writer);
            writer.WriteInt(654321);
            // The receiving client has its own default before reading the host's message.
            GravitySettings.RestoreEncounterPreferences(9, true);
            GravitySettings.LockEncountersAfterBossUnlock = !lockEncounters;
            reader.Reset(writer.Buffer);
            var received = new LobbyBeginRunMessage();
            received.Deserialize(reader);
            // Simulate the struct copies made by generic network dispatch.
            var copy = received;
            Check(GravityRunSettings.Get(copy.modifiers) == nextRunRequirement && reader.ReadInt() == 654321,
                "Host's rule must survive lobby message serialization and struct copies");
            Check(GravityRunSettings.GetLockEncounters(copy.modifiers) == lockEncounters, "Lobby message lost the host's encounter lock");

            var lobby = (StartRunLobby)RuntimeHelpers.GetUninitializedObject(typeof(StartRunLobby));
            AccessTools.Field(typeof(StartRunLobby), "<NetService>k__BackingField").SetValue(lobby,
                RuntimeHelpers.GetUninitializedObject(typeof(NetClientGameService)));
            AccessTools.Method(typeof(ReceiveLobbySettingsPatch), "Prefix").Invoke(null, [lobby, copy]);
            var clientRun = NewState();
            AccessTools.Method(typeof(MultiplayerRunSettingsPatch), "Prefix").Invoke(null, [clientRun, lobby]);
            GravityRunSettings.Initialize(clientRun);
            Check(GravityRunSettings.Get(clientRun.ExtraFields) == nextRunRequirement,
                "Client initialization must preserve the host's snapshot");
            Check(GravitySettings.NextRunRequirement == 9, "Receiving host settings must not overwrite personal defaults");
            Check(GravityRunSettings.GetLockEncounters(clientRun.ExtraFields) == lockEncounters
                && GravitySettings.LockEncountersAfterBossUnlock == !lockEncounters,
                "Client must use the host's lock without overwriting its personal default");
            var soloRun = NewState();
            GravityRunSettings.Initialize(soloRun);
            Check(GravityRunSettings.Get(soloRun.ExtraFields) == 9, "Next solo run must still use the client's default");
            Check(GravityRunSettings.GetLockEncounters(soloRun.ExtraFields) == !lockEncounters,
                "Next solo run must use the client's own lock setting");

            GravitySettings.RestoreEncounterPreferences(9, true);
            Check(GravityRunSettings.Get(loaded) == requirement, "Changing defaults must not change an existing run");
            Check(GravityRunSettings.GetLockEncounters(loaded) == lockEncounters, "Changing defaults must not change an existing lock");
        }
        var legacy = (SerializableExtraRunFields)JsonSerializer.Deserialize("{}", info)!;
        Check(GravityRunSettings.Get(ExtraRunFields.FromSerializable(legacy)) == 15, "Legacy saves retain 15 regardless of current defaults");
        Check(!GravityRunSettings.GetLockEncounters(ExtraRunFields.FromSerializable(legacy)), "Legacy saves must default to continued exploration");
        var oldSnapshot = (SerializableExtraRunFields)JsonSerializer.Deserialize("{\"gravity_encounters\":7}", info)!;
        Check(GravityRunSettings.Get(oldSnapshot) == 7 && !GravityRunSettings.GetLockEncounters(oldSnapshot),
            "Existing snapshots without the lock must retain their requirement and continued exploration");
        var reversed = (SerializableExtraRunFields)JsonSerializer.Deserialize("{\"gravity_lock_encounters\":true,\"gravity_encounters\":7}", info)!;
        Check(GravityRunSettings.Get(reversed) == 7 && GravityRunSettings.GetLockEncounters(reversed),
            "Snapshot properties must load in either order");
        var invalid = (SerializableExtraRunFields)JsonSerializer.Deserialize("{\"gravity_encounters\":-22}", info)!;
        Check(GravityRunSettings.Get(invalid) == 15, "Invalid saved requirements must fall back safely");
        RunState NewState()
        {
            var state = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
            AccessTools.Property(typeof(RunState), nameof(RunState.ExtraFields)).SetValue(state, new ExtraRunFields());
            return state;
        }
        GravitySettings.RestoreEncounterPreferences(7, true);
        GravitySettings.LockEncountersAfterBossUnlock = true;
        var active = NewState();
        GravityRunSettings.Initialize(active);
        GravitySettings.RestoreEncounterPreferences(11, true);
        GravitySettings.LockEncountersAfterBossUnlock = false;
        GravityRunSettings.Initialize(active);
        var next = NewState();
        GravityRunSettings.Initialize(next);
        Check(GravityRunSettings.Get(active.ExtraFields) == 7 && GravityRunSettings.Get(next.ExtraFields) == 11,
            "Only a new playthrough should snapshot edited defaults");
        Check(GravityRunSettings.GetLockEncounters(active.ExtraFields) && !GravityRunSettings.GetLockEncounters(next.ExtraFields),
            "Only a new playthrough should snapshot an edited encounter lock");
        GravitySettings.RestoreEncounterPreferences(15, true);
        VerifyVersionMatching();
        Console.WriteLine("Passed native JSON, save conversion, network packet, lobby rule, and next-run isolation checks.");
    }

    private static void VerifyMissingLobbySettings()
    {
        var receive = AccessTools.Method(typeof(ReceiveLobbySettingsPatch), "Prefix");
        var initialize = AccessTools.Method(typeof(MultiplayerRunSettingsPatch), "Prefix");
        GravitySettings.RestoreEncounterPreferences(3, true);
        GravitySettings.LockEncountersAfterBossUnlock = true;

        // Cover both a missing receive hook and a received message lacking its snapshot.
        foreach (var receivedMessage in new[] { false, true })
        {
            var lobby = NewClientLobby();
            if (receivedMessage)
            {
                var message = new LobbyBeginRunMessage { modifiers = [] };
                receive.Invoke(null, [lobby, message]);
                Check(!GravityRunSettings.TryCopy(lobby, new object()),
                    "Missing message settings must not manufacture a host snapshot of 15");
            }
            var state = NewState();
            initialize.Invoke(null, [state, lobby]);
            Check(GravityRunSettings.Get(state.ExtraFields) == 3
                && GravityRunSettings.GetLockEncounters(state.ExtraFields),
                "Missing host settings must fall back to the client's configured rules");
            GravitySettings.RestoreEncounterPreferences(9, true);
            GravityRunSettings.Initialize(state);
            Check(GravityRunSettings.Get(state.ExtraFields) == 3,
                "Fallback rules must be snapshotted, not follow later preference changes");
            var reloaded = ExtraRunFields.FromSerializable(state.ExtraFields.ToSerializable());
            Check(GravityRunSettings.Get(reloaded) == 3 && GravityRunSettings.GetLockEncounters(reloaded),
                "Fallback rules must survive save conversion");
            GravitySettings.RestoreEncounterPreferences(3, true);
        }

        foreach (var hostRequirement in new[] { 0, -1, 3, 15, 99 })
        {
            var lobby = NewClientLobby();
            var message = new LobbyBeginRunMessage { modifiers = [] };
            GravityRunSettings.Set(message.modifiers, hostRequirement, false);
            receive.Invoke(null, [lobby, message]);
            var state = NewState();
            initialize.Invoke(null, [state, lobby]);
            GravityRunSettings.Initialize(state);
            Check(GravityRunSettings.Get(state.ExtraFields) == hostRequirement
                && !GravityRunSettings.GetLockEncounters(state.ExtraFields),
                "Received host rules, including 15, must override the client's preferences");
            Check(GravitySettings.NextRunRequirement == 3 && GravitySettings.LockEncountersAfterBossUnlock,
                "Host rules must not overwrite the client's personal preferences");
        }
        GravitySettings.RestoreEncounterPreferences(15, true);
        GravitySettings.LockEncountersAfterBossUnlock = false;
        Console.WriteLine("Passed missing-host fallback and host-rule precedence checks.");

        static StartRunLobby NewClientLobby()
        {
            var lobby = (StartRunLobby)RuntimeHelpers.GetUninitializedObject(typeof(StartRunLobby));
            AccessTools.Field(typeof(StartRunLobby), "<NetService>k__BackingField").SetValue(lobby,
                RuntimeHelpers.GetUninitializedObject(typeof(NetClientGameService)));
            return lobby;
        }
        static RunState NewState()
        {
            var state = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
            AccessTools.Property(typeof(RunState), nameof(RunState.ExtraFields)).SetValue(state, new ExtraRunFields());
            return state;
        }
    }

    private static void VerifyLobbyMessageTransport()
    {
        GravitySettings.RestoreEncounterPreferences(3, true);
        var message = new LobbyBeginRunMessage { playersInLobby = [], modifiers = [], seed = "gravity-test", act1 = "test" };
        var writer = new PacketWriter();
        message.Serialize(writer);
        writer.WriteInt(654321);
        var reader = new PacketReader();
        reader.Reset(writer.Buffer);
        // NetMessageBus creates a boxed struct and deserializes through INetMessage.
        INetMessage received = (INetMessage)Activator.CreateInstance(typeof(LobbyBeginRunMessage))!;
        received.Deserialize(reader);
        var copy = (LobbyBeginRunMessage)received;
        Check(GravityRunSettings.Get(copy.modifiers) == 3,
            $"Boxed lobby message lost host requirement: expected 3, got {GravityRunSettings.Get(copy.modifiers)}");
        Check(reader.ReadInt() == 654321, "Boxed lobby message lost packet alignment");
        // Initialize only the message under test without booting the game's mod loader.
        var cacheField = AccessTools.Field(typeof(MessageTypes), "_cache");
        var previousCache = cacheField.GetValue(null);
        var cache = RuntimeHelpers.GetUninitializedObject(cacheField.FieldType);
        AccessTools.Field(cacheField.FieldType, "_typeToId").SetValue(cache,
            new Dictionary<Type, int> { [typeof(LobbyBeginRunMessage)] = 0 });
        AccessTools.Field(cacheField.FieldType, "_idToType").SetValue(cache,
            new List<Type> { typeof(LobbyBeginRunMessage) });
        cacheField.SetValue(null, cache);
        try
        {
            // Avoid the constructor's Godot logger; these packet methods only use reader/writer.
            var bus = (NetMessageBus)RuntimeHelpers.GetUninitializedObject(typeof(NetMessageBus));
            AccessTools.Field(typeof(NetMessageBus), "_reader").SetValue(bus, new PacketReader());
            AccessTools.Field(typeof(NetMessageBus), "_writer").SetValue(bus, new PacketWriter());
            var packet = bus.SerializeMessage(1, message, out var length);
            Check(bus.TryDeserializeMessage(packet[..length], out var dispatched, out var sender),
                "Native message bus could not deserialize the lobby message");
            Check(sender == 1 && GravityRunSettings.Get(((LobbyBeginRunMessage)dispatched!).modifiers) == 3,
                "Native message bus lost the host's requirement");
        }
        finally
        {
            cacheField.SetValue(null, previousCache);
        }
        GravitySettings.RestoreEncounterPreferences(15, true);
    }

    private static void VerifyPreferences()
    {
        Check(!GravitySettings.LockEncountersAfterBossUnlock, "Encounter locking must default to off");
        Check(GravitySettings.Mode == GravitySettings.EncounterMode.Default
            && GravitySettings.CustomEncounterCount == 15 && GravitySettings.NextRunRequirement == 15,
            "Both the initial selection and custom value must default to 15");
        foreach (var (legacy, enabled, expected, custom) in new[]
        {
            (15, true, 15, 15), (-1, true, -1, 15), (25, true, 25, 25),
            (25, false, 0, 25), (-1, false, 0, 15), (0, true, 0, 15), (1000, true, 999, 999),
        })
        {
            GravitySettings.RestoreEncounterPreferences(legacy, enabled);
            Check(GravitySettings.NextRunRequirement == expected && GravitySettings.CustomEncounterCount == custom,
                "Legacy preferences must retain their requirement and remembered custom value");
        }
        foreach (var mode in Enum.GetValues<GravitySettings.EncounterMode>())
        {
            GravitySettings.CustomEncounterCount = 37;
            GravitySettings.Mode = mode;
            var expected = mode switch
            {
                GravitySettings.EncounterMode.None => 0,
                GravitySettings.EncounterMode.All => -1,
                GravitySettings.EncounterMode.Custom => 37,
                _ => 15,
            };
            Check(GravitySettings.NextRunRequirement == expected, "Each selection must resolve to the correct run rule");
            // Restore precisely the three encounter values written to the config.
            var storedCustom = GravitySettings.CustomEncounterCount;
            GravitySettings.CustomEncounterCount = 15;
            GravitySettings.RestoreEncounterPreferences(expected, true, (int)mode, storedCustom);
            Check(GravitySettings.Mode == mode && GravitySettings.CustomEncounterCount == 37,
                "Reloading a preset must preserve the custom number");
            GravitySettings.Mode = GravitySettings.EncounterMode.Custom;
            Check(GravitySettings.NextRunRequirement == 37, "Returning to Custom must restore its remembered value");
        }
        foreach (var (input, expected) in new[] { (-1, 0), (0, 0), (15, 15), (999, 999), (1000, 999) })
        {
            GravitySettings.CustomEncounterCount = input;
            Check(GravitySettings.NextRunRequirement == expected, "Custom values must stay within 0–999");
        }
        GravitySettings.RestoreEncounterPreferences(15, true);
    }

    private static void VerifyVersionMatching()
    {
        var peerType = typeof(ConnectionFailureExtraInfo).Assembly
            .GetType("MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo");
        if (peerType == null)
        {
            Console.WriteLine("Skipped beta-only native co-op version comparison: this game uses the older handshake API.");
            return;
        }
        foreach (var remoteMods in new List<string>[] { ["Gravity-0.1.0"], ["Gravity-0.2.0"], [] })
        {
            dynamic local = Activator.CreateInstance(peerType)!;
            local.version = "0.111.0";
            local.gameplayAffectingMods = new List<string> { "Gravity-0.1.0" };
            dynamic remote = Activator.CreateInstance(peerType)!;
            remote.version = local.version;
            remote.gameplayAffectingMods = remoteMods;
            // HandshakeManager rejects the peer with ModMismatch when either of
            // these native comparisons finds a missing gameplay mod. Exercise
            // the comparison without initializing Godot's native network/logger.
            dynamic comparison = new ConnectionFailureExtraInfo();
            comparison.localInfo = local;
            comparison.remoteInfo = remote;
            var matches = remoteMods.SequenceEqual(new[] { "Gravity-0.1.0" });
            Check((comparison.GetMissingModsOnLocal(false).Count == 0
                && comparison.GetMissingModsOnRemote(false).Count == 0) == matches,
                "The game's co-op mod comparison must reject missing or mismatched Gravity versions");
        }
        Console.WriteLine("Passed co-op mod comparison checks for matching, mismatched, and missing Gravity versions.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
