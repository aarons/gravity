using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.sts2.Core.Nodes.TopBar;

namespace Gravity;

internal static class GravityTopBarProgress
{
    private sealed class Binding(IRunState? run, MegaLabel label)
    {
        public IRunState? Run = run;
        private readonly bool _hadColorOverride = label.HasThemeColorOverride("font_color");
        private readonly Color _normalColor = label.GetThemeColor("font_color");

        public void RefreshColor()
        {
            var progress = Run is RunState run && Applies(run) ? GravityRules.Progress(run) : null;
            if (progress is { RequiredEncounters: > 0 } && progress.EncountersVisited >= progress.RequiredEncounters)
                label.AddThemeColorOverride("font_color", GravitySettings.HighlightColor);
            else if (_hadColorOverride)
                label.AddThemeColorOverride("font_color", _normalColor);
            else
                label.RemoveThemeColorOverride("font_color");
        }
    }

    private static readonly ConditionalWeakTable<MegaLabel, Binding> Bindings = new();
    // The native tooltip accepts LocStrings, while Gravity owns its localization.
    private static readonly PropertyInfo TipTitle = AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title));
    private static readonly PropertyInfo TipDescription = AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description));

    public static bool Applies(IRunState? run) => run?.Map != null && GravityRules.Applies(run);

    public static string Text(RunState run)
    {
        var progress = GravityRules.Progress(run);
        return progress.RequiredEncounters == 0 ? progress.EncountersVisited.ToString()
            : string.Format(MainFile.Localize("map.progress"),
                Math.Min(progress.EncountersVisited, progress.RequiredEncounters), progress.RequiredEncounters);
    }

    public static void UpdateColor(IRunState? run, MegaLabel label)
    {
        if (!Bindings.TryGetValue(label, out var binding))
        {
            if (!Applies(run)) return;
            binding = new Binding(run, label);
            Bindings.Add(label, binding);
            GravitySettings.AppearanceChanged += binding.RefreshColor;
            label.TreeExiting += () =>
            {
                GravitySettings.AppearanceChanged -= binding.RefreshColor;
                Bindings.Remove(label);
            };
        }
        binding.Run = run;
        binding.RefreshColor();
    }

    public static HoverTip Tooltip(RunState run)
    {
        object tip = new HoverTip { Id = "Gravity.Encounters" };
        TipTitle.SetValue(tip, MainFile.Localize("map.progress.title"));
        var required = GravityRules.Progress(run).RequiredEncounters;
        TipDescription.SetValue(tip, (required == 0 ? MainFile.Localize("map.progress.unrestricted")
            : string.Format(MainFile.Localize("map.progress.description"), required))
            + "\n\n" + MainFile.Localize("settings.open"));
        return (HoverTip)tip;
    }
}

[HarmonyPatch(typeof(NTopBarFloorIcon), "UpdateIcon")]
internal static class EncounterCounterPatch
{
    // Initialize and RoomEntered both use this hook, including reloads and new acts.
    private static bool Prefix(NTopBarFloorIcon __instance, IRunState ____runState, MegaLabel ____floorNumLabel)
    {
        GravityTopBarProgress.UpdateColor(____runState, ____floorNumLabel);
        if (!GravityTopBarProgress.Applies(____runState)) return true;
        GravitySettingsMenu.Attach(__instance, (RunState)____runState);
        // The native HBox containers expand to fit the text and move the boss icon.
        ____floorNumLabel.SetTextAutoSize(GravityTopBarProgress.Text((RunState)____runState));
        return false;
    }
}

[HarmonyPatch(typeof(NTopBarFloorIcon), "OnFocus")]
internal static class EncounterTooltipPatch
{
    private static bool Prefix(NTopBarFloorIcon __instance, IRunState ____runState)
    {
        if (!GravityTopBarProgress.Applies(____runState)) return true;
        NHoverTipSet.CreateAndShow(__instance, GravityTopBarProgress.Tooltip((RunState)____runState))?
            .SetGlobalPosition(__instance.GlobalPosition + new Vector2(0f, __instance.Size.Y + 20f));
        return false;
    }
}
