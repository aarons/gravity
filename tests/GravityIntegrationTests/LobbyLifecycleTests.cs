using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Gravity;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

// Uses the real lobby methods and native packets; network sockets, profile writes,
// and the Godot screen are replaced. This is not a live two-player acceptance test.
internal static class LobbyLifecycleTests
{
    public static void Run()
    {
        var fixture = new Harmony("Gravity.LobbyLifecycleFixture");
        fixture.Patch(AccessTools.Method(typeof(StartRunLobby), "UpdatePreferredAscension"),
            prefix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(SkipProfileWrite)));
        foreach (var name in new[] { "BeginRunForAllPlayers", "HandleLobbyBeginRunMessage" })
            fixture.Patch(AccessTools.Method(typeof(StartRunLobby), name),
                transpiler: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(WithoutInstanceLogging)));
        fixture.Patch(AccessTools.Method(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Serialize)),
            postfix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(OtherModWrite)));
        fixture.Patch(AccessTools.Method(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Deserialize)),
            postfix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(OtherModRead)));
        foreach (var name in new[] { nameof(NGame.StartNewSingleplayerRun), nameof(NGame.StartNewMultiplayerRun) })
            fixture.Patch(AccessTools.Method(typeof(NGame), name),
                prefix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(CreateWithoutScene)) { priority = Priority.Last });
        fixture.Patch(AccessTools.Method(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun)),
            prefix: new HarmonyMethod(typeof(LobbyLifecycleTests), nameof(SkipProfileWrite)) { priority = Priority.Last });
        try
        {
            foreach (var requirement in new[] { 0, -1, 15, 99 })
            foreach (var locked in new[] { false, true })
            {
                RunState? clientState = null;
                RunState? hostState = null;
                StartRunLobby? client = null;
                StartRunLobby? host = null;
                var other = (OtherSettingsModifier)ModelDb.Modifier<OtherSettingsModifier>().ToMutable();
                other.CombatsLeft = 871;
                other.IsUsed = true;
                var clientListener = Proxy<IStartRunLobbyListener>((method, args) =>
                {
                    if (method.Name == "BeginRun") clientState = StartAtScreenBoundary(client!, args!, 871);
                    return null;
                });
                var hostListener = Proxy<IStartRunLobbyListener>((method, args) =>
                {
                    if (method.Name == "BeginRun") hostState = StartAtScreenBoundary(host!, args!, 871);
                    return null;
                });
                client = Lobby(Proxy<INetGameService>((method, _) => method.Name == "get_Type" ? NetGameType.Client : null), clientListener);
                host = Lobby(Proxy<INetHostGameService>((method, args) =>
                {
                    if (method.Name == "get_Type") return NetGameType.Host;
                    if (method.Name == "SendMessage" && args![0] is LobbyBeginRunMessage message)
                    {
                        var writer = new PacketWriter();
                        message.Serialize(writer);
                        writer.WriteInt(789);
                        // Change defaults before the client sees any data, including
                        // immediately-started lobbies with no settings-change event.
                        GravitySettings.RestoreEncounterPreferences(4, true);
                        GravitySettings.LockEncountersAfterBossUnlock = !locked;
                        var reader = new PacketReader();
                        reader.Reset(writer.Buffer);
                        var received = new LobbyBeginRunMessage();
                        received.Deserialize(reader);
                        Check(reader.ReadInt() == 789, "Lobby transport lost alignment");
                        AccessTools.Method(typeof(StartRunLobby), "HandleLobbyBeginRunMessage").Invoke(client, [received, 1UL]);
                    }
                    return null;
                }), hostListener);
                GravitySettings.RestoreEncounterPreferences(requirement, true);
                GravitySettings.LockEncountersAfterBossUnlock = locked;
                var original = new List<ModifierModel> { other };
                AccessTools.Method(typeof(StartRunLobby), "BeginRunForAllPlayers").Invoke(host, ["GRAVITYTEST", original]);
                Check(original.Count == 1 && ReferenceEquals(original[0], other), "Host hook mutated another owner's modifier list");
                foreach (var state in new[] { clientState, hostState })
                {
                    Check(state != null && GravityRunSettings.Get(state) == requirement
                        && GravityRunSettings.GetLockEncounters(state) == locked,
                        "Actual lobby send/receive/start hooks failed to preserve host rules");
                    Check(state!.GameMode == GameMode.Standard, "Internal snapshot changed standard run classification");
                }
            }
        }
        finally
        {
            fixture.UnpatchAll(fixture.Id);
            GravitySettings.RestoreEncounterPreferences(15, true);
            GravitySettings.LockEncountersAfterBossUnlock = false;
        }
        Console.WriteLine("Passed 8 native host-send/client-receive/lobby-start lifecycle cases with different defaults and another mod's snapshot (screen/network fixtures).");
    }

    private static RunState StartAtScreenBoundary(StartRunLobby lobby, object?[] args, int expectedOther)
    {
        var incoming = (IReadOnlyList<ModifierModel>)args[2]!;
        var other = incoming.OfType<OtherSettingsModifier>().Single();
        Check(other.CombatsLeft == expectedOther && other.IsUsed, "Lobby startup altered the other mod's settings");
        // Simulate a cooperating mod forwarding its own snapshot at the standard
        // screen boundary; Gravity must retain this entry and recover only its own.
        var acts = (List<ActModel>)args[1]!;
        var screen = (NCharacterSelectScreen)RuntimeHelpers.GetUninitializedObject(typeof(NCharacterSelectScreen));
        AccessTools.Field(typeof(NCharacterSelectScreen), "_lobby").SetValue(screen, lobby);
        screen.BeginRun((string)args[0]!, acts, incoming);
        var game = (NGame)RuntimeHelpers.GetUninitializedObject(typeof(NGame));
        // Invoke the actual patched entry points after the screen discards its list.
        // The fixture prefix skips asset loading only, after Gravity's prefix runs.
        var state = game.StartNewMultiplayerRun(lobby, false, acts, [other], (string)args[0]!, 0).GetAwaiter().GetResult();
        var solo = game.StartNewSingleplayerRun(null!, false, acts, [other], (string)args[0]!, GameMode.Standard).GetAwaiter().GetResult();
        Check(GravityRunSettings.Get(solo) == GravityRunSettings.Snapshot(incoming)!.Requirement
            && GravityRunSettings.GetLockEncounters(solo) == GravityRunSettings.Snapshot(incoming)!.LockEncounters,
            "Solo screen-to-game entry bridge lost the captured snapshot");
        Check(solo.Modifiers.Contains(other) && state.Modifiers.Contains(other), "Game-entry hooks dropped another mod's modifier");
        Check(GravityRunSettings.BridgeSolo([], acts).Count == 0, "A later solo startup must not reuse a consumed snapshot");
        GravityRunSettings.InitializeMultiplayer(state, lobby);
        GravityRunSettings.Initialize(state);
        return state;
    }

    private static StartRunLobby Lobby(INetGameService service, IStartRunLobbyListener listener)
    {
        var lobby = (StartRunLobby)RuntimeHelpers.GetUninitializedObject(typeof(StartRunLobby));
        AccessTools.Field(typeof(StartRunLobby), "<NetService>k__BackingField").SetValue(lobby, service);
        AccessTools.Field(typeof(StartRunLobby), "<LobbyListener>k__BackingField").SetValue(lobby, listener);
        AccessTools.Field(typeof(StartRunLobby), "<Players>k__BackingField").SetValue(lobby,
            new List<MegaCrit.Sts2.Core.Entities.Multiplayer.LobbyPlayer>());
        AccessTools.Field(typeof(StartRunLobby), "<GameMode>k__BackingField").SetValue(lobby, GameMode.Standard);
        lobby.Act1 = "overgrowth";
        return lobby;
    }

    private static bool CreateWithoutScene(IReadOnlyList<ModifierModel> modifiers, ref Task<RunState> __result)
    {
        __result = Task.FromResult(SettingsTests.NewState(modifiers));
        return false;
    }

    private static void OtherModWrite(PacketWriter writer) => writer.WriteInt(998877);
    private static void OtherModRead(PacketReader reader) =>
        Check(reader.ReadInt() == 998877, "Gravity interfered with another mod's lobby packet extension");

    private static bool SkipProfileWrite() => false;

    private static IEnumerable<CodeInstruction> WithoutInstanceLogging(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType?.Name == "Logger")
            {
                for (var i = 0; i < method.GetParameters().Length + 1; i++) yield return new CodeInstruction(OpCodes.Pop);
            }
            else yield return instruction;
        }
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> callback) where T : class
    {
        var instance = DispatchProxy.Create<T, LobbyProxy>();
        ((LobbyProxy)(object)instance).Callback = callback;
        return instance;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

public class LobbyProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Callback { get; set; } = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Callback(method!, args);
}
