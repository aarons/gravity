using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using HarmonyLib;

namespace Gravity;

// Internal run data, never offered as a selectable gameplay modifier.
public sealed class GravitySettingsModifier : ModifierModel
{
    private bool lockWasSet;
    private bool lockEncounters;

    // Saved-property names are global network tokens, but values belong to each model.
    // Reuse existing native tokens to leave IDs, order and bit width exactly unchanged.
    // These private wire names have no connection to relic instances using the same tokens.
    [SavedProperty] private int CombatsLeft { get; set; } = int.MinValue;
    [SavedProperty] private bool IsUsed
    {
        get => lockEncounters;
        set { lockEncounters = value; lockWasSet = true; }
    }

    internal int Requirement => CombatsLeft;
    internal bool LockEncounters => IsUsed;
    internal bool IsValid => CombatsLeft is >= -1 and <= 1000 && lockWasSet;
    public override bool ShouldReceiveCombatHooks => false;

    internal static void RegisterSavedProperties()
    {
        // Stable needs explicit registration. Beta discovers saved properties with
        // model IDs after mod initialization, and no longer has this cache type.
        // Avoid a static reference so the same DLL can load on either branch.
        var cache = typeof(ModifierModel).Assembly.GetType("MegaCrit.Sts2.Core.Saves.Runs.SavedPropertiesTypeCache");
        if (cache == null) return;
        // Both tokens must already exist. Do not expand or repair another mod's registry.
        var getId = AccessTools.Method(cache, "GetNetIdForPropertyName");
        _ = getId.Invoke(null, [nameof(CombatsLeft)]);
        _ = getId.Invoke(null, [nameof(IsUsed)]);
        AccessTools.Method(cache, "InjectTypeIntoCache").Invoke(null, [typeof(GravitySettingsModifier)]);
    }

    internal static GravitySettingsModifier Create(int requirement, bool locked)
    {
        var result = (GravitySettingsModifier)ModelDb.Modifier<GravitySettingsModifier>().ToMutable();
        result.CombatsLeft = requirement;
        result.IsUsed = locked;
        return result;
    }

    internal static GravitySettingsModifier FromPreferences() =>
        Create(GravitySettings.NextRunRequirement, GravitySettings.LockEncountersAfterBossUnlock);
}
