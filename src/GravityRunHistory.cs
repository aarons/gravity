using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;

namespace Gravity;

[HarmonyPatch(typeof(NActHistoryEntry), nameof(NActHistoryEntry._Ready))]
internal static class RunHistoryWrappingPatch
{
    private static void Postfix(NActHistoryEntry __instance)
    {
        // An unbounded HBox pushes the centered history page past the viewport.
        // Keep the native entries (and their floor numbers, tooltips and player
        // bindings), but let the existing vertical scroller handle extra rows.
        var encounters = new HFlowContainer
        {
            Name = "GravityEncounters",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        encounters.AddThemeConstantOverride("h_separation", __instance.GetThemeConstant("separation"));
        encounters.AddThemeConstantOverride("v_separation", 0);
        __instance.AddChild(encounters);
        foreach (var entry in __instance.Entries)
            entry.Reparent(encounters, false);

        // Left/right follows visit order even across a line break. Up/down keeps
        // Godot's spatial navigation between rows and acts.
        for (var i = 0; i < __instance.Entries.Count; i++)
        {
            var entry = __instance.Entries[i];
            if (i > 0) entry.FocusNeighborLeft = __instance.Entries[i - 1].GetPath();
            if (i + 1 < __instance.Entries.Count)
                entry.FocusNeighborRight = __instance.Entries[i + 1].GetPath();
        }

        // Align the act title with its first row, including when the act wraps.
        var title = __instance.GetNode<Control>("%Title");
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        title.CustomMinimumSize = new Vector2(title.CustomMinimumSize.X,
            __instance.Entries.FirstOrDefault()?.GetCombinedMinimumSize().Y ?? 0);
    }
}
