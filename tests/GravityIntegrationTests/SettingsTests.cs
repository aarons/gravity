using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gravity;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

internal static class SettingsTests
{
    public static void InitializeModels()
    {
        // Stand in for the completed mod loader, then use the real discovery and ID
        // initialization. No manual ModelDb.Inject or packet-ID seeding for Gravity.
        var mods = new List<Mod>();
        foreach (var assembly in new[] { typeof(MainFile).Assembly, typeof(SettingsTests).Assembly })
        {
            var mod = (Mod)RuntimeHelpers.GetUninitializedObject(typeof(Mod));
            var assemblyField = AccessTools.Field(typeof(Mod), "assembly");
            if (assemblyField != null) assemblyField.SetValue(mod, assembly);
            else AccessTools.Field(typeof(Mod), "assemblies").SetValue(mod, new List<Assembly> { assembly });
            mod.state = ModLoadState.Loaded;
            var field = AccessTools.Field(typeof(Mod), "manifest");
            var manifest = Activator.CreateInstance(field.FieldType)!;
            AccessTools.Field(field.FieldType, "id").SetValue(manifest, assembly.GetName().Name);
            field.SetValue(mod, manifest);
            mods.Add(mod);
        }
        AccessTools.Field(typeof(ModManager), "_mods").SetValue(null, mods);
        AccessTools.Property(typeof(ModManager), nameof(ModManager.State)).SetValue(null, ModManagerState.Initialized);
        var assemblyInfo = typeof(Mod).Assembly.GetType("MegaCrit.Sts2.Core.Modding.AssemblyInfo");
        if (assemblyInfo != null) AccessTools.Method(assemblyInfo, "Init").Invoke(null, null);
        // The final diagnostic uses Godot's OS API. Omit only logging in this headless fixture.
        var fixture = new Harmony("Gravity.SettingsFixture");
        fixture.Patch(AccessTools.Method(typeof(GravitySettingsSync), "ShowWarning"),
            prefix: new HarmonyMethod(typeof(SettingsTests), nameof(SuppressWarning)));
        fixture.Patch(AccessTools.Method(typeof(ModelIdSerializationCache), nameof(ModelIdSerializationCache.Init)),
            transpiler: new HarmonyMethod(typeof(SettingsTests), nameof(WithoutLogging)));
        fixture.Patch(AccessTools.Constructor(typeof(UnlockState), [typeof(IEnumerable<UnlockState>)]),
            prefix: new HarmonyMethod(typeof(SettingsTests), nameof(EmptyPlayersUnlocks)));
        // Beta adds an optional injected-model list. Resolve the signature at
        // runtime so one compiled harness can exercise both game branches.
        var initializeModels = AccessTools.Method(typeof(ModelDb), nameof(ModelDb.Init));
        initializeModels.Invoke(null, initializeModels.GetParameters().Select(p => p.DefaultValue).ToArray());
        ModelIdSerializationCache.Init();
        ModelDb.InitIds();
        var cache = typeof(ModifierModel).Assembly.GetType("MegaCrit.Sts2.Core.Saves.Runs.SavedPropertiesTypeCache");
        if (cache != null) AccessTools.Method(cache, "InjectTypeIntoCache").Invoke(null, [typeof(OtherSettingsModifier)]);
        MessageTypes.Initialize();
        Check(!MessageTypes.TryGetMessageType(256, out _), "Native message IDs must fit the game's one-byte packet header");
        Check(MessageTypes.TryGetMessageType(MessageTypes.TypeToId<GravityRunSettingsMessage>(), out var messageType)
            && messageType == typeof(GravityRunSettingsMessage), "Native discovery must register Gravity's message");
        Check(MessageTypes.TypeToId<OtherSettingsMessage>() != MessageTypes.TypeToId<GravityRunSettingsMessage>(),
            "Mod messages must have distinct native IDs");
        var ids = new[] { MessageTypes.TypeToId<GravityRunSettingsMessage>(), MessageTypes.TypeToId<OtherSettingsMessage>(),
            MessageTypes.TypeToId<LobbyBeginRunMessage>() };
        mods.Reverse();
        if (assemblyInfo != null) AccessTools.Method(assemblyInfo, "Init").Invoke(null, null);
        MessageTypes.Initialize();
        Check(ids.SequenceEqual(new[] { MessageTypes.TypeToId<GravityRunSettingsMessage>(),
            MessageTypes.TypeToId<OtherSettingsMessage>(), MessageTypes.TypeToId<LobbyBeginRunMessage>() }),
            "Reversing mod load order must preserve native message IDs");
    }

    private static void EmptyPlayersUnlocks(ref IEnumerable<UnlockState> __0)
    {
        // No Godot-backed player inventories in the headless fixture.
        if (!__0.Any()) __0 = [UnlockState.all];
    }

    private static bool SuppressWarning() => false; // No Godot scene in the harness.

    private static IEnumerable<CodeInstruction> WithoutLogging(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType?.Name == "Log")
            {
                for (var i = 0; i < method.GetParameters().Length; i++) yield return new CodeInstruction(OpCodes.Pop);
            }
            else yield return instruction;
        }
    }

    public static void Run()
    {
        VerifyPreferences();
        VerifyVersionMatching();
        foreach (var requirement in new[] { -1, 0, 1, 15, 99, 999, 1000 })
        foreach (var locked in new[] { false, true })
        {
            var other = (OtherSettingsModifier)ModelDb.Modifier<OtherSettingsModifier>().ToMutable();
            other.CombatsLeft = 871;
            other.IsUsed = true;
            var state = NewState([other]);
            GravityRunSettings.Set(state, requirement, locked);
            GravitySettings.RestoreEncounterPreferences(4, true);
            GravitySettings.LockEncountersAfterBossUnlock = !locked;
            var snapshot = GravityRunSettings.GetSnapshot(state);
            var saved = Save(state);
            var json = JsonSerializer.Serialize(saved, JsonSerializationUtility.Options);
            Check(json.Contains("\"gravity_version\"") && !json.Contains("GRAVITY_SETTINGS_MODIFIER"), "Settings must use dedicated JSON data");
            var loaded = JsonSerializer.Deserialize<SerializableRun>(json, JsonSerializationUtility.Options)!;
            var restored = RunState.FromSerializable(loaded);
            Check(GravityRunSettings.GetSnapshot(restored) == snapshot, "Disk load changed run settings");
            var otherRestored = restored.Modifiers.OfType<OtherSettingsModifier>().Single();
            Check(restored.Modifiers.Count == 1 && otherRestored.CombatsLeft == 871 && otherRestored.IsUsed,
                "Settings must not add gameplay modifiers or alter another mod");
            Check(GravitySettings.NextRunRequirement == 4 && GravitySettings.LockEncountersAfterBossUnlock == !locked,
                "Loading must not change personal preferences");
            var writer = new PacketWriter();
            saved.Serialize(writer);
            var reader = new PacketReader();
            reader.Reset(writer.Buffer);
            var network = new SerializableRun();
            network.Deserialize(reader);
            var fallback = GravitySettingsSnapshot.FromPreferences();
            Check(GravityRunSettings.GetSnapshot(RunState.FromSerializable(network)) == fallback,
                "An unpaired network save must still load with a local snapshot");
        }
        VerifyMigration();
        GravitySettings.RestoreEncounterPreferences(15, true);
        GravitySettings.LockEncountersAfterBossUnlock = false;
        Console.WriteLine("Passed immutable settings, JSON persistence, network fallback, and legacy save migration checks.");
    }

    private static void VerifyMigration()
    {
        foreach (var (json, expected) in new[]
        {
            ("{}", new GravitySettingsSnapshot(15, false)),
            ("{\"gravity_encounters\":23,\"gravity_lock_encounters\":true}", new GravitySettingsSnapshot(23, true)),
            ("{\"gravity_encounters\":-22}", new GravitySettingsSnapshot(15, false)),
        })
        {
            var save = Save(NewState());
            save.ExtraFields = JsonSerializer.Deserialize<SerializableExtraRunFields>(json, JsonSerializationUtility.Options)!;
            Check(GravityRunSettings.GetSnapshot(RunState.FromSerializable(save)) == expected, "Legacy JSON migration failed");
        }
        foreach (var locked in new[] { false, true })
        {
            var other = (OtherSettingsModifier)ModelDb.Modifier<OtherSettingsModifier>().ToMutable();
            other.CombatsLeft = 871;
            var save = Save(NewState([other]));
            var foreign = save.Modifiers.Single();
            save.Modifiers.Add(new SerializableModifier
            {
                Id = new ModelId("MODIFIER", "GRAVITY_SETTINGS_MODIFIER"),
                Props = new SavedProperties
                {
                    ints = [new("CombatsLeft", 99)], bools = [new("IsUsed", locked)],
                },
            });
            var diskCopy = JsonSerializer.Deserialize<SerializableRun>(
                JsonSerializer.Serialize(save, JsonSerializationUtility.Options), JsonSerializationUtility.Options)!;
            Check(GravityRunSettings.Get(RunState.FromSerializable(diskCopy)) == 99,
                "Legacy disk saves must load without registering the retired model");
            var loaded = RunState.FromSerializable(save);
            Check(GravityRunSettings.Get(loaded) == 99 && GravityRunSettings.GetLockEncounters(loaded) == locked,
                "Retired modifier settings must migrate");
            Check(save.Modifiers.Count == 1 && ReferenceEquals(save.Modifiers[0], foreign),
                "Migration must remove only Gravity's old modifier");
            var again = RunState.FromSerializable(save);
            Check(GravityRunSettings.GetSnapshot(again) == GravityRunSettings.GetSnapshot(loaded), "Migration must be idempotent");
            Check(JsonSerializer.Serialize(Save(loaded), JsonSerializationUtility.Options).Contains("\"gravity_version\""),
                "Migrated settings must be written to the new payload");
        }
        foreach (var payload in new[]
        {
            "{\"gravity_version\":2,\"gravity_encounters\":15,\"gravity_lock_encounters\":false}",
            "{\"gravity_version\":1,\"gravity_encounters\":-22,\"gravity_lock_encounters\":false}",
            "{\"gravity_version\":1,\"gravity_encounters\":15}",
            "{\"gravity_version\":1,\"gravity_lock_encounters\":false}",
        })
        {
            var invalid = Save(NewState());
            invalid.ExtraFields = JsonSerializer.Deserialize<SerializableExtraRunFields>(payload, JsonSerializationUtility.Options)!;
            Throws<JsonException>(() => RunState.FromSerializable(invalid), "Invalid or future save settings must fail explicitly");
        }
    }

    internal static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception(message);
    }

    internal static RunState NewState(IReadOnlyList<ModifierModel>? modifiers = null)
    {
        return RunState.CreateForNewRun([], [], modifiers ?? [], GameMode.Standard, 0, "GRAVITYTEST");
    }

    internal static SerializableRun Save(RunState state) => new()
    {
        Modifiers = state.Modifiers.Select(m => m.ToSerializable()).ToList(),
        Players = state.Players.Select(p => p.ToSerializable()).ToList(),
        SerializableRng = state.Rng.ToSerializable(),
        SerializableOdds = state.Odds.ToSerializable(),
        SerializableSharedRelicGrabBag = state.SharedRelicGrabBag.ToSerializable(),
        ExtraFields = state.ExtraFields.ToSerializable(),
        GameMode = state.GameMode,
    };

    private static void VerifyPreferences()
    {
        Check(GravitySettings.LockEncountersAfterBossUnlock, "Encounter locking must default to on for new players");
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

// A second mod's model with independently stored values sharing native name tokens.
public sealed class OtherSettingsModifier : ModifierModel
{
    [SavedProperty] public int CombatsLeft { get; set; }
    [SavedProperty] public bool IsUsed { get; set; }
    [SavedProperty] public int OtherModSetting { get; set; } = 42;
}
