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
        foreach (var assembly in new[] { typeof(GravitySettingsModifier).Assembly, typeof(SettingsTests).Assembly })
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
        fixture.Patch(AccessTools.Method(typeof(ModelIdSerializationCache), nameof(ModelIdSerializationCache.Init)),
            transpiler: new HarmonyMethod(typeof(SettingsTests), nameof(WithoutLogging)));
        fixture.Patch(AccessTools.Constructor(typeof(UnlockState), [typeof(IEnumerable<UnlockState>)]),
            prefix: new HarmonyMethod(typeof(SettingsTests), nameof(EmptyPlayersUnlocks)));
        // Match mod initialization timing, before beta's native cache is ready.
        var initialNames = LegacyPropertyCache != null ? PropertyNames() : null;
        var initialBits = LegacyPropertyCache != null ? PropertyBits() : 0;
        GravitySettingsModifier.RegisterSavedProperties();
        if (initialNames != null)
            Check(initialNames.SequenceEqual(PropertyNames()) && initialBits == PropertyBits(),
                "Initial stable registration must preserve property IDs and bit width");
        ModelDb.Init();
        if (LegacyPropertyCache == null) VerifyNativePropertyDiscovery();
        ModelIdSerializationCache.Init();
        ModelDb.InitIds();
        Check(ModelDb.Modifier<GravitySettingsModifier>() != null, "Game discovery must register Gravity's model");
        var names = PropertyNames();
        var bits = PropertyBits();
        GravitySettingsModifier.RegisterSavedProperties();
        Check(names.SequenceEqual(PropertyNames()) && bits == PropertyBits(),
            "Gravity must not change any saved-property network ID or its bit width");
        if (LegacyPropertyCache != null)
            AccessTools.Method(LegacyPropertyCache, "InjectTypeIntoCache").Invoke(null, [typeof(OtherSettingsModifier)]);
        var otherNames = PropertyNames();
        GravitySettingsModifier.RegisterSavedProperties();
        Check(otherNames.SequenceEqual(PropertyNames()) && bits == PropertyBits(),
            "Registration must preserve another mod's properties and be idempotent");
        Check(!ModelDb.GoodModifiers.Concat(ModelDb.BadModifiers).OfType<GravitySettingsModifier>().Any(), "Internal snapshot must not enter the selectable modifier pool");
    }

    private static void VerifyNativePropertyDiscovery()
    {
        // Compare beta's native registry with and without Gravity's properties.
        // Keep the same models and other mod loaded in both passes.
        var fixture = new Harmony("Gravity.PropertyDiscoveryFixture");
        var cacheProperties = AccessTools.Method(typeof(ModelIdSerializationCache), "CachePropertiesForType");
        fixture.Patch(cacheProperties,
            prefix: new HarmonyMethod(typeof(SettingsTests), nameof(SkipGravityProperties)));
        string[] names;
        int bits;
        var hashProperty = AccessTools.Property(typeof(ModelIdSerializationCache), "Hash");
        object? hash;
        try
        {
            ModelIdSerializationCache.Init();
            names = PropertyNames();
            bits = PropertyBits();
            hash = hashProperty.GetValue(null);
        }
        finally { fixture.UnpatchAll(fixture.Id); }
        AccessTools.Method(typeof(ModelIdSerializationCache), "ResetForTest").Invoke(null, null);
        ModelIdSerializationCache.Init();
        Check(names.SequenceEqual(PropertyNames()) && bits == PropertyBits(),
            "Native Gravity discovery must not add or reorder property IDs or change bit width");
        Check(Equals(hash, hashProperty.GetValue(null)),
            "Reused property tokens must not alter beta's native serialization hash");
        AccessTools.Method(typeof(ModelIdSerializationCache), "ResetForTest").Invoke(null, null);
    }

    private static bool SkipGravityProperties(Type __0) => __0 != typeof(GravitySettingsModifier);

    private static void EmptyPlayersUnlocks(ref IEnumerable<UnlockState> __0)
    {
        // No Godot-backed player inventories in the headless fixture.
        if (!__0.Any()) __0 = [UnlockState.all];
    }

    private static Type? LegacyPropertyCache => typeof(ModifierModel).Assembly
        .GetType("MegaCrit.Sts2.Core.Saves.Runs.SavedPropertiesTypeCache");
    private static Type PropertyCache => LegacyPropertyCache ?? typeof(ModelIdSerializationCache);
    private static int PropertyBits() => (int)AccessTools.Property(PropertyCache,
        LegacyPropertyCache != null ? "NetIdBitSize" : "PropertyIdBitSize").GetValue(null)!;
    private static string[] PropertyNames() => ((List<string>)AccessTools.Field(PropertyCache,
        "_netIdToPropertyNameMap").GetValue(null)!).ToArray();

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
        foreach (var requirement in new[] { -1, 0, 1, 15, 60, 99, 999, 1000 })
        foreach (var locked in new[] { false, true })
        {
            var snapshot = GravitySettingsModifier.Create(requirement, locked);
            var other = (OtherSettingsModifier)ModelDb.Modifier<OtherSettingsModifier>().ToMutable();
            other.CombatsLeft = 731;
            other.IsUsed = !locked;
            var message = new LobbyBeginRunMessage { playersInLobby = [],
                modifiers = [other.ToSerializable(), snapshot.ToSerializable()], seed = "gravity-test", act1 = "overgrowth" };
            var writer = new PacketWriter();
            message.Serialize(writer);
            writer.WriteInt(654321);
            var reader = new PacketReader();
            reader.Reset(writer.Buffer);
            INetMessage received = (INetMessage)Activator.CreateInstance(typeof(LobbyBeginRunMessage))!;
            received.Deserialize(reader);
            Check(reader.ReadInt() == 654321, "Native lobby packet must retain alignment");
            var restored = ((LobbyBeginRunMessage)received).modifiers.Select(ModifierModel.FromSerializable).ToList();
            var otherRestored = (OtherSettingsModifier)restored[0];
            Check(otherRestored.CombatsLeft == 731 && otherRestored.IsUsed == !locked && otherRestored.OtherModSetting == 42,
                "Another model using the same name tokens must retain its independent values");
            GravitySettings.RestoreEncounterPreferences(9, true);
            GravitySettings.LockEncountersAfterBossUnlock = !locked;
            var lobby = NewLobby();
            GravityRunSettings.Capture(lobby, restored);
            // Standard startup drops the input list. Recover only Gravity, while
            // preserving anything another patch already supplied at the boundary.
            var bridged = GravityRunSettings.Bridge([otherRestored], lobby);
            Check(ReferenceEquals(bridged[0], otherRestored), "Startup bridge must preserve other modifiers by identity");
            Check(GravityRunSettings.Bridge([], lobby).Count == 1, "Bridge must not globally enable lobby modifiers");
            var state = NewState(bridged);
            GravityRunSettings.InitializeMultiplayer(state, lobby);
            GravityRunSettings.Initialize(state);
            Verify(state, requirement, locked);
            Check(GravitySettings.NextRunRequirement == 9 && GravitySettings.LockEncountersAfterBossUnlock == !locked,
                "Receiving host settings must not overwrite personal defaults");
            Check(HideSettingsModifierPatch.Visible(state.Modifiers).SequenceEqual([otherRestored]),
                "Only Gravity's internal snapshot must be hidden");
            var save = Save(state);
            var json = JsonSerializationUtility.ToJson(save);
            Check(!json.Contains("gravity_encounters") && !json.Contains("gravity_lock_encounters"),
                "New JSON must stop writing legacy fields");
            var loaded = JsonSerializer.Deserialize<SerializableRun>(json, JsonSerializationUtility.Options)!;
            var loadedState = RunState.FromSerializable(loaded);
            Verify(loadedState, requirement, locked);
            // SerializableRun is the native payload used for save/load and reconnect.
            writer.Reset();
            save.Serialize(writer);
            writer.WriteInt(123456);
            reader.Reset(writer.Buffer);
            var reconnect = new SerializableRun();
            reconnect.Deserialize(reader);
            Check(reader.ReadInt() == 123456, "Native reconnect payload must retain alignment");
            var rejoined = RunState.FromSerializable(reconnect);
            Verify(rejoined, requirement, locked);
            GravitySettings.RestoreEncounterPreferences(5, true);
            GravitySettings.LockEncountersAfterBossUnlock = !locked;
            GravityRunSettings.Initialize(state);
            Verify(state, requirement, locked);
        }
        VerifyFallbackAndLegacy();
        VerifyVersionMatching();
        GravitySettings.RestoreEncounterPreferences(15, true);
        GravitySettings.LockEncountersAfterBossUnlock = false;
        Console.WriteLine("Passed native modifier discovery, registry preservation, 16 lobby/save/reconnect round trips, coexistence, fallback and legacy migration checks.");
    }

    private static void VerifyFallbackAndLegacy()
    {
        var absentLock = GravitySettingsModifier.Create(15, false).ToSerializable();
        absentLock.Props!.bools = null;
        foreach (var modifiers in new List<ModifierModel>[] { [],
            [GravitySettingsModifier.Create(-22, true)],
            [ModifierModel.FromSerializable(absentLock)],
            [GravitySettingsModifier.Create(3, true), GravitySettingsModifier.Create(4, false)] })
        {
            GravitySettings.RestoreEncounterPreferences(3, true);
            GravitySettings.LockEncountersAfterBossUnlock = true;
            var lobby = NewLobby();
            GravityRunSettings.Capture(lobby, modifiers);
            GravitySettings.RestoreEncounterPreferences(9, true);
            GravitySettings.LockEncountersAfterBossUnlock = false;
            var state = NewState();
            GravityRunSettings.InitializeMultiplayer(state, lobby);
            GravityRunSettings.Initialize(state);
            Verify(state, 3, true);
            var serializable = GravityRunSettings.Snapshot(state.Modifiers)!.ToSerializable();
            var loaded = NewState([ModifierModel.FromSerializable(serializable)]);
            Verify(loaded, 3, true);
        }
        foreach (var (json, requirement, locked) in new[] {
            ("{}", 15, false), ("{\"gravity_encounters\":7}", 7, false),
            ("{\"gravity_lock_encounters\":true,\"gravity_encounters\":7}", 7, true),
            ("{\"gravity_encounters\":-22}", 15, false) })
        {
            var fields = JsonSerializer.Deserialize<SerializableExtraRunFields>(json, JsonSerializationUtility.Options)!;
            var legacySave = Save(NewState());
            legacySave.ExtraFields = fields;
            var state = RunState.FromSerializable(legacySave);
            Verify(state, requirement, locked);
            var newer = NewState([GravitySettingsModifier.Create(0, false)]);
            GravityRunSettings.Load(newer, new SerializableRun { ExtraFields = fields });
            Verify(newer, 0, false);
        }
        var missingHook = NewState();
        GravityRunSettings.InitializeMultiplayer(missingHook, NewLobby());
        Verify(missingHook, 9, false);
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

    private static StartRunLobby NewLobby()
    {
        var lobby = (StartRunLobby)RuntimeHelpers.GetUninitializedObject(typeof(StartRunLobby));
        AccessTools.Field(typeof(StartRunLobby), "<NetService>k__BackingField").SetValue(lobby,
            RuntimeHelpers.GetUninitializedObject(typeof(NetClientGameService)));
        return lobby;
    }

    private static void Verify(RunState state, int requirement, bool locked) =>
        Check(GravityRunSettings.Get(state) == requirement && GravityRunSettings.GetLockEncounters(state) == locked,
            $"Expected {requirement}/{locked}, got {GravityRunSettings.Get(state)}/{GravityRunSettings.GetLockEncounters(state)}");

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

// A second mod's model with independently stored values sharing native name tokens.
public sealed class OtherSettingsModifier : ModifierModel
{
    [SavedProperty] public int CombatsLeft { get; set; }
    [SavedProperty] public bool IsUsed { get; set; }
    [SavedProperty] public int OtherModSetting { get; set; } = 42;
}
