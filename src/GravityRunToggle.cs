using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using static Gravity.MainFile;

namespace Gravity;

// The character screen is reused between lobbies; refresh from preferences each
// time it opens. Clients leave the choice to the host, like ascension.
internal static class GravityRunToggle
{
    internal static void Refresh(NCharacterSelectScreen screen)
    {
        var toggle = screen.GetNodeOrNull<CheckButton>("GravityRunToggle");
        if (toggle == null)
        {
            toggle = new CheckButton
            {
                Name = "GravityRunToggle",
                Text = Localize("run.enabled"),
                Theme = GravitySettingsPopup.CreateTheme(),
                CustomMinimumSize = new Vector2(180, 48),
                AnchorLeft = 1, AnchorRight = 1,
                GrowHorizontal = Control.GrowDirection.Begin,
                OffsetLeft = -244, OffsetRight = -64,
            };
            StyleToggle(toggle);
            screen.AddChild(toggle);
            var portraits = screen.GetNode<Control>("CharSelectButtons/ButtonContainer");
            void PositionBelowPortraits()
            {
                var toScreen = screen.GetGlobalTransform().AffineInverse();
                var bottom = float.NegativeInfinity;
                foreach (var portrait in portraits.GetChildren().OfType<Control>().Where(child => child.Visible))
                    bottom = Mathf.Max(bottom, (toScreen * portrait.GetGlobalTransform() * portrait.Size).Y);
                if (float.IsNegativeInfinity(bottom))
                    bottom = (toScreen * portraits.GetGlobalTransform() * portraits.Size).Y;
                toggle.OffsetTop = bottom + 3;
                toggle.OffsetBottom = toggle.OffsetTop + toggle.GetCombinedMinimumSize().Y;
            }
            // Defer until containers finish layout, including added mod characters.
            var place = Callable.From(PositionBelowPortraits);
            portraits.ItemRectChanged += () => place.CallDeferred();
            portraits.ChildOrderChanged += () => place.CallDeferred();
            if (portraits is Container container)
                container.SortChildren += () => place.CallDeferred();
            screen.Resized += () => place.CallDeferred();
            toggle.VisibilityChanged += () => place.CallDeferred();
            place.CallDeferred();
            toggle.Toggled += enabled =>
            {
                GravitySettings.Enabled = enabled;
                GravitySettings.Save();
            };
        }
        var client = screen.Lobby.NetService.Type == NetGameType.Client;
        toggle.Visible = !client;
        toggle.Disabled = false;
        toggle.SetPressedNoSignal(GravitySettings.Enabled);
        var characters = screen.GetNode<Control>("CharSelectButtons/ButtonContainer")
            .GetChildren().OfType<NCharacterSelectButton>().Where(button => button.Visible && !button.IsLocked).ToArray();
        foreach (var character in characters)
            character.FocusNeighborBottom = character.GetPathTo(client ? character : toggle);
        if (!client && characters.Length > 0)
        {
            toggle.FocusNeighborTop = toggle.FocusNext = toggle.FocusPrevious = toggle.GetPathTo(characters[^1]);
            toggle.FocusNeighborBottom = toggle.GetPath();
        }
    }

    private static void StyleToggle(CheckButton toggle)
    {
        toggle.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        toggle.AddThemeFontSizeOverride("font_size", 22);
        toggle.AddThemeConstantOverride("h_separation", 16);
        toggle.AddThemeConstantOverride("outline_size", 4);
        toggle.AddThemeColorOverride("font_outline_color", new Color("211A16"));
        // CheckButton has separate hover_pressed styling. Keep every background
        // identical so selection never turns the whole row into a pressed button.
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            toggle.AddThemeStyleboxOverride(state, new StyleBoxEmpty
            {
                ContentMarginLeft = 8, ContentMarginRight = 8,
                ContentMarginTop = 10, ContentMarginBottom = 10,
            });
            toggle.AddThemeColorOverride($"font_{(state == "normal" ? "" : state + "_")}color",
                new Color(state == "disabled" ? "8C8478" : state.StartsWith("hover") ? "FFF0D2" : "D6C9AF"));
        }
        toggle.AddThemeColorOverride("font_focus_color", new Color("FFF0D2"));
        toggle.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        foreach (var enabled in new[] { false, true })
        foreach (var disabled in new[] { false, true })
        {
            var texture = SwitchTexture(enabled, disabled);
            var name = enabled ? "checked" : "unchecked";
            toggle.AddThemeIconOverride(name + (disabled ? "_disabled" : ""), texture);
            toggle.AddThemeIconOverride(name + (disabled ? "_disabled" : "") + "_mirrored", texture);
        }
    }

    private static Texture2D SwitchTexture(bool enabled, bool disabled)
    {
        // A substantial thumb, warm metal rim, and inset track echo the game's
        // controls. Position and a check mark communicate state without color.
        var thumbX = enabled ? 40 : 14;
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="54" height="28" viewBox="0 0 54 28">
              <g opacity="{(disabled ? "0.45" : "1")}">
                <rect x="1" y="1" width="52" height="26" rx="13" fill="#211D19" stroke="{(enabled ? "#C3A260" : "#827665")}" stroke-width="2"/>
                <rect x="4" y="4" width="46" height="20" rx="10" fill="{(enabled ? "#806332" : "#38332D")}"/>
                <circle cx="{thumbX}" cy="14" r="10" fill="{(enabled ? "#F0DEAE" : "#B3A793")}" stroke="#30271C" stroke-width="1.5"/>
                {(enabled ? "<path d=\"M35 14 L39 18 L45 10\" fill=\"none\" stroke=\"#514024\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" : "")}
              </g>
            </svg>
            """;
        using var image = new Image();
        image.LoadSvgFromString(svg);
        return ImageTexture.CreateFromImage(image);
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
internal static class ShowGravityRunTogglePatch
{
    private static void Postfix(NCharacterSelectScreen __instance) => GravityRunToggle.Refresh(__instance);
}

[HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
internal static class LockGravityRunTogglePatch
{
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        if (__instance.GetNodeOrNull<CheckButton>("GravityRunToggle") is { } toggle)
            toggle.Disabled = true;
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), "OnUnreadyPressed")]
internal static class UnlockGravityRunTogglePatch
{
    private static void Postfix(NCharacterSelectScreen __instance) => GravityRunToggle.Refresh(__instance);
}
