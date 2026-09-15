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
        var info = JsonSerializationUtility.Options.GetTypeInfo(typeof(SerializableExtraRunFields));
        foreach (var requirement in new[] { -1, 0, 1, 15, 60, 1000 })
        {
            var fields = new ExtraRunFields { StartedWithNeow = true, TestSubjectKills = 3, FreedRepy = true };
            GravityRunSettings.Set(fields, requirement);
            var save = fields.ToSerializable();
            var json = JsonSerializer.Serialize(save, info);
            using var document = JsonDocument.Parse(json);
            Check(document.RootElement.TryGetProperty("gravity_encounters", out var stored) && stored.GetInt32() == requirement,
                "Rule missing from native JSON metadata: " + json);
            var restored = (SerializableExtraRunFields)JsonSerializer.Deserialize(json, info)!;
            var loaded = ExtraRunFields.FromSerializable(restored);
            Check(GravityRunSettings.Get(loaded) == requirement, "Requirement must survive native JSON save/load");
            Check(loaded.StartedWithNeow && loaded.TestSubjectKills == 3 && loaded.FreedRepy, "Native extra fields must survive");
            var runJson = JsonSerializationUtility.ToJson(new SerializableRun { ExtraFields = save });
            var runSave = JsonSerializer.Deserialize<SerializableRun>(runJson, JsonSerializationUtility.Options)!;
            Check(GravityRunSettings.Get(runSave.ExtraFields) == requirement, "Full run save dropped its encounter snapshot");

            var writer = new PacketWriter();
            save.Serialize(writer);
            writer.WriteInt(123456); // Catch accidental packet misalignment after our extension.
            var reader = new PacketReader();
            reader.Reset(writer.Buffer);
            var network = new SerializableExtraRunFields();
            network.Deserialize(reader);
            Check(GravityRunSettings.Get(network) == requirement && reader.ReadInt() == 123456, "Network round trip lost run settings or packet alignment");

            var nextRunRequirement = requirement == -1 ? GravitySettings.MaxEncounterCount : requirement;
            GravitySettings.EncounterCount = nextRunRequirement;
            var message = new LobbyBeginRunMessage { playersInLobby = [], modifiers = [], seed = "gravity-test", act1 = "test" };
            writer.Reset();
            message.Serialize(writer);
            writer.WriteInt(654321);
            // The receiving client has its own default before reading the host's message.
            GravitySettings.EncounterCount = 9;
            reader.Reset(writer.Buffer);
            var received = new LobbyBeginRunMessage();
            received.Deserialize(reader);
            // Simulate the struct copies made by generic network dispatch.
            var copy = received;
            Check(GravityRunSettings.Get(copy.modifiers) == nextRunRequirement && reader.ReadInt() == 654321,
                "Host's rule must survive lobby message serialization and struct copies");

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
            var soloRun = NewState();
            GravityRunSettings.Initialize(soloRun);
            Check(GravityRunSettings.Get(soloRun.ExtraFields) == 9, "Next solo run must still use the client's default");

            GravitySettings.EncounterCount = 9;
            Check(GravityRunSettings.Get(loaded) == requirement, "Changing defaults must not change an existing run");
        }
        var legacy = (SerializableExtraRunFields)JsonSerializer.Deserialize("{}", info)!;
        Check(GravityRunSettings.Get(ExtraRunFields.FromSerializable(legacy)) == 15, "Legacy saves retain 15 regardless of current defaults");
        var invalid = (SerializableExtraRunFields)JsonSerializer.Deserialize("{\"gravity_encounters\":-22}", info)!;
        Check(GravityRunSettings.Get(invalid) == 15, "Invalid saved requirements must fall back safely");
        RunState NewState()
        {
            var state = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
            AccessTools.Property(typeof(RunState), nameof(RunState.ExtraFields)).SetValue(state, new ExtraRunFields());
            return state;
        }
        GravitySettings.EncounterCount = 7;
        var active = NewState();
        GravityRunSettings.Initialize(active);
        GravitySettings.EncounterCount = 11;
        GravityRunSettings.Initialize(active);
        var next = NewState();
        GravityRunSettings.Initialize(next);
        Check(GravityRunSettings.Get(active.ExtraFields) == 7 && GravityRunSettings.Get(next.ExtraFields) == 11,
            "Only a new playthrough should snapshot edited defaults");
        GravitySettings.EncounterCount = 15;
        VerifyVersionMatching();
        Console.WriteLine("Passed native JSON, save conversion, network packet, lobby rule, and next-run isolation checks.");
    }

    private static void VerifyVersionMatching()
    {
        foreach (var remoteMods in new List<string>[] { ["Gravity-0.1.0"], ["Gravity-0.2.0"], [] })
        {
            var local = new PeerVersionInfo { version = "0.111.0", gameplayAffectingMods = ["Gravity-0.1.0"] };
            var remote = new PeerVersionInfo { version = local.version, gameplayAffectingMods = remoteMods };
            // HandshakeManager rejects the peer with ModMismatch when either of
            // these native comparisons finds a missing gameplay mod. Exercise
            // the comparison without initializing Godot's native network/logger.
            var comparison = new ConnectionFailureExtraInfo { localInfo = local, remoteInfo = remote };
            var matches = remoteMods.SequenceEqual(local.gameplayAffectingMods);
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
