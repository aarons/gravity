using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Gravity;

internal static class GravityRunSettings
{
    private sealed record LegacyRule(int Requirement = 15, bool LockEncounters = false);
    private static readonly ConditionalWeakTable<object, LegacyRule> Legacy = new();
    private static readonly ConditionalWeakTable<StartRunLobby, GravitySettingsModifier> Starting = new();
    private static readonly ConditionalWeakTable<object, GravitySettingsModifier> StartingSolo = new();
    private static readonly PropertyInfo ModifiersProperty = AccessTools.Property(typeof(RunState), nameof(RunState.Modifiers));

    internal static GravitySettingsModifier? Snapshot(IEnumerable<ModifierModel> modifiers)
    {
        GravitySettingsModifier? snapshot = null;
        foreach (var modifier in modifiers)
        {
            if (modifier is not GravitySettingsModifier candidate) continue;
            if (snapshot != null || !candidate.IsValid) return null;
            snapshot = candidate;
        }
        return snapshot;
    }

    public static int Get(RunState run) => Snapshot(run.Modifiers)?.Requirement ?? 15;
    public static bool GetLockEncounters(RunState run) => Snapshot(run.Modifiers)?.LockEncounters ?? false;

    internal static void Set(RunState run, int requirement, bool locked = false) =>
        Attach(run, GravitySettingsModifier.Create(requirement, locked));

    private static void Attach(RunState run, GravitySettingsModifier snapshot) =>
        ModifiersProperty.SetValue(run, Merge(run.Modifiers, snapshot));

    internal static IReadOnlyList<ModifierModel> Merge(IEnumerable<ModifierModel> modifiers, GravitySettingsModifier snapshot) =>
        modifiers.Where(m => m is not GravitySettingsModifier).Append(snapshot).ToArray();

    public static void Initialize(RunState run)
    {
        if (Snapshot(run.Modifiers) == null) Attach(run, Fallback());
    }

    private static GravitySettingsModifier Fallback()
    {
        var snapshot = GravitySettingsModifier.FromPreferences();
        Console.WriteLine($"[Gravity] New-run settings snapshot missing or invalid; using local encounter requirement "
            + $"{snapshot.Requirement} and encounter lock {snapshot.LockEncounters} for this run.");
        return snapshot;
    }

    internal static void Capture(StartRunLobby lobby, IEnumerable<ModifierModel> modifiers)
    {
        var snapshot = Snapshot(modifiers) ?? Fallback();
        Starting.Remove(lobby);
        Starting.Add(lobby, snapshot);
    }

    // Called only at the standard screen's discarded-modifiers boundary. All modifiers
    // already supplied at that call site remain; only Gravity is recovered from the lobby.
    internal static IReadOnlyList<ModifierModel> Bridge(IReadOnlyList<ModifierModel> modifiers, StartRunLobby lobby) =>
        Starting.TryGetValue(lobby, out var snapshot) ? Merge(modifiers, snapshot) : modifiers;

    internal static void CaptureSolo(IReadOnlyList<ActModel> acts, StartRunLobby lobby)
    {
        if (!Starting.TryGetValue(lobby, out var snapshot)) return;
        StartingSolo.Remove(acts);
        StartingSolo.Add(acts, snapshot);
    }

    internal static IReadOnlyList<ModifierModel> BridgeSolo(IReadOnlyList<ModifierModel> modifiers, IReadOnlyList<ActModel> acts)
    {
        if (!StartingSolo.TryGetValue(acts, out var snapshot)) return modifiers;
        StartingSolo.Remove(acts);
        return Merge(modifiers, snapshot);
    }

    internal static void InitializeMultiplayer(RunState state, StartRunLobby lobby)
    {
        if (Starting.TryGetValue(lobby, out var snapshot)) Attach(state, snapshot);
        else if (Snapshot(state.Modifiers) == null)
            Attach(state, lobby.NetService.Type == NetGameType.Client ? Fallback() : GravitySettingsModifier.FromPreferences());
    }

    internal static void Load(RunState run, SerializableRun save)
    {
        if (Snapshot(run.Modifiers) != null) return;
        var rule = Legacy.TryGetValue(save.ExtraFields, out var stored) ? stored : new LegacyRule();
        Set(run, rule.Requirement, rule.LockEncounters);
    }

    internal static void ReadLegacyRequirement(object owner, int requirement)
    {
        var rule = Legacy.TryGetValue(owner, out var stored) ? stored : new LegacyRule();
        Legacy.Remove(owner);
        Legacy.Add(owner, rule with { Requirement = requirement is >= -1 and <= 1000 ? requirement : 15 });
    }

    internal static void ReadLegacyLock(object owner, bool locked)
    {
        var rule = Legacy.TryGetValue(owner, out var stored) ? stored : new LegacyRule();
        Legacy.Remove(owner);
        Legacy.Add(owner, rule with { LockEncounters = locked });
    }
}

[HarmonyPatch(typeof(StartRunLobby), "BeginRunForAllPlayers")]
internal static class HostRunSettingsPatch
{
    private static void Prefix(StartRunLobby __instance, ref List<ModifierModel> modifiers)
    {
        if (__instance.NetService.Type == NetGameType.Client) return;
        modifiers = GravityRunSettings.Merge(modifiers, GravitySettingsModifier.FromPreferences()).ToList();
    }
}

[HarmonyPatch(typeof(StartRunLobby), "BeginRunLocally")]
internal static class CaptureRunSettingsPatch
{
    private static void Prefix(StartRunLobby __instance, List<ModifierModel> modifiers) =>
        GravityRunSettings.Capture(__instance, modifiers);
}

// The standard screen discards its modifier argument before its async fade. Keep
// only Gravity's snapshot with the act-list identity passed to the solo entry point.
// Multiplayer has a lobby argument and can use its lobby-scoped snapshot directly.
[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
internal static class CaptureStandardRunSettingsPatch
{
    private static void Prefix(List<ActModel> acts, StartRunLobby ____lobby) =>
        GravityRunSettings.CaptureSolo(acts, ____lobby);
}

[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
internal static class SoloRunSettingsBridgePatch
{
    private static void Prefix(IReadOnlyList<ActModel> acts, ref IReadOnlyList<ModifierModel> modifiers) =>
        modifiers = GravityRunSettings.BridgeSolo(modifiers, acts);
}

[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewMultiplayerRun))]
internal static class MultiplayerRunSettingsBridgePatch
{
    private static void Prefix(StartRunLobby lobby, ref IReadOnlyList<ModifierModel> modifiers) =>
        modifiers = GravityRunSettings.Bridge(modifiers, lobby);
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
internal static class MultiplayerRunSettingsPatch
{
    private static void Prefix(RunState state, StartRunLobby lobby) => GravityRunSettings.InitializeMultiplayer(state, lobby);
}

[HarmonyPatch(typeof(RunManager), "InitializeNewRun")]
internal static class NewRunSettingsPatch
{
    private static void Prefix(RunManager __instance) => GravityRunSettings.Initialize(__instance.DebugOnlyGetState()!);
}

[HarmonyPatch(typeof(RunState), nameof(RunState.FromSerializable))]
internal static class LoadRunSettingsPatch
{
    private static void Postfix(SerializableRun save, RunState __result) => GravityRunSettings.Load(__result, save);
}

// Read old JSON fields only. New saves and reconnects use native modifier serialization.
[HarmonyPatch(typeof(JsonSerializationUtility), nameof(JsonSerializationUtility.AlphabetizeProperties))]
internal static class RunSettingsJsonPatch
{
    private static void Prefix(JsonTypeInfo info)
    {
        if (info.Type != typeof(SerializableExtraRunFields)) return;
        var property = info.CreateJsonPropertyInfo(typeof(int), "gravity_encounters");
        property.Set = (owner, value) => GravityRunSettings.ReadLegacyRequirement(owner, (int)value!);
        info.Properties.Add(property);
        var lockProperty = info.CreateJsonPropertyInfo(typeof(bool), "gravity_lock_encounters");
        lockProperty.Set = (owner, value) => GravityRunSettings.ReadLegacyLock(owner, (bool)value!);
        info.Properties.Add(lockProperty);
    }
}

[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
internal static class HideSettingsModifierPatch
{
    internal static IReadOnlyList<ModifierModel> Visible(IReadOnlyList<ModifierModel> modifiers) =>
        modifiers.Where(m => m is not GravitySettingsModifier).ToArray();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(IRunState), nameof(IRunState.Modifiers));
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.Calls(getter)) yield return CodeInstruction.Call(typeof(HideSettingsModifierPatch), nameof(Visible));
        }
    }
}

// Standard runs still reject other gameplay modifiers as before. Exclude only
// our internal data from the diagnostic count, without changing the input list.
[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
internal static class StandardRunModifierDiagnosticPatch
{
    private static int GameplayCount(IReadOnlyCollection<ModifierModel> modifiers) =>
        modifiers.Count(m => m is not GravitySettingsModifier);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(IReadOnlyCollection<ModifierModel>), "Count");
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(StandardRunModifierDiagnosticPatch), nameof(GameplayCount));
            }
            yield return instruction;
        }
    }
}
