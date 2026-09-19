using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using static Gravity.MainFile;

namespace Gravity;

internal static class GravityModSettings
{
    private sealed class Binding
    {
        public required GravitySettingsPanel Panel;
        public required ScrollContainer Scroll;
        public required Control Description;
        public required Control Image;
        public required bool DescriptionVisible;
        public required bool ImageVisible;
        public required NModMenuRow Row;
        public required NodePath Right;
        public required NodePath Next;
    }
    private static readonly ConditionalWeakTable<NModInfoContainer, Binding> Bindings = new();

    public static void Select(NModdingScreen screen, NModMenuRow row)
    {
        var info = screen.GetNode<NModInfoContainer>("%ModInfoContainer");
        Clear(info);
        if (row.Mod?.manifest?.id != ModId || row.Mod.state != ModLoadState.Loaded) return;

        var panel = new GravitySettingsPanel();
        var description = info.GetNode<Control>("ModDescription");
        var image = info.GetNode<Control>("ModImage");
        var scroll = new ScrollContainer
        {
            Name = "GravitySettings", FollowFocus = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var binding = new Binding
        {
            Panel = panel, Scroll = scroll, Description = description, Image = image,
            DescriptionVisible = description.Visible,
            ImageVisible = image.Visible, Row = row, Right = row.FocusNeighborRight, Next = row.FocusNext,
        };
        Bindings.Add(info, binding);
        // Settings need only their heading; reclaim the image and description space.
        image.Hide();
        description.Hide();
        info.AddChild(scroll);
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scroll.OffsetLeft = 25;
        scroll.OffsetRight = -25;
        scroll.OffsetTop = 110;
        scroll.OffsetBottom = -16;
        scroll.AddChild(panel.Content);
        var controls = panel.FocusControls;
        row.FocusNeighborRight = row.FocusNext = row.GetPathTo(controls[0]);
        foreach (var control in controls)
            if (control.FocusNeighborLeft.ToString().Length == 0)
                control.FocusNeighborLeft = control.GetPathTo(row);
        controls[0].FocusPrevious = controls[0].FocusNeighborTop = controls[0].GetPathTo(row);
        controls[^1].FocusNext = controls[^1].FocusNeighborBottom = controls[^1].GetPathTo(row);
    }

    public static void Clear(NModInfoContainer info)
    {
        if (!Bindings.TryGetValue(info, out var binding)) return;
        Bindings.Remove(info);
        binding.Panel.Flush();
        binding.Scroll.Hide();
        info.RemoveChild(binding.Scroll);
        binding.Scroll.QueueFree();
        binding.Description.Visible = binding.DescriptionVisible;
        binding.Image.Visible = binding.ImageVisible;
        if (GodotObject.IsInstanceValid(binding.Row))
        {
            binding.Row.FocusNeighborRight = binding.Right;
            binding.Row.FocusNext = binding.Next;
        }
    }
}

[HarmonyPatch(typeof(NModdingScreen), nameof(NModdingScreen.OnRowSelected))]
internal static class GravityModSettingsEntryPatch
{
    private static void Prefix(NModdingScreen __instance) =>
        GravityModSettings.Clear(__instance.GetNode<NModInfoContainer>("%ModInfoContainer"));
    private static void Postfix(NModdingScreen __instance, NModMenuRow row) => GravityModSettings.Select(__instance, row);
}

[HarmonyPatch(typeof(NModInfoContainer), "Clear")]
internal static class GravityModSettingsClearPatch
{
    // Stable v0.107.1 has no Clear method; row changes already clean up above.
    private static bool Prepare() => AccessTools.Method(typeof(NModInfoContainer), "Clear") != null;
    private static void Prefix(NModInfoContainer __instance) => GravityModSettings.Clear(__instance);
}
