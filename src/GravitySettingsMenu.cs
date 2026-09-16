using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.sts2.Core.Nodes.TopBar;
using static Gravity.MainFile;

namespace Gravity;

internal static class GravitySettingsMenu
{
    private sealed class Binding(RunState run)
    {
        public RunState Run = run;
        public PopupPanel? Popup;
    }
    private static readonly ConditionalWeakTable<NTopBarFloorIcon, Binding> Bindings = new();
    private static readonly Vector2I MenuSize = new(940, 520);

    public static void Attach(NTopBarFloorIcon icon, RunState run)
    {
        if (Bindings.TryGetValue(icon, out var existing)) { existing.Run = run; return; }
        var binding = new Binding(run);
        Bindings.Add(icon, binding);
        icon.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        // Both the stairs and number belong to this native clickable HBox.
        icon.GetNode<Control>("FloorIconPositioner").MouseFilter = Control.MouseFilterEnum.Ignore;
        icon.GetNode<Control>("FloorNumLabel").MouseFilter = Control.MouseFilterEnum.Ignore;
        var normal = icon.Modulate;
        icon.Focused += _ => icon.Modulate = normal * new Color(1.2f, 1.2f, 1.2f, 1f);
        icon.Unfocused += _ => icon.Modulate = normal;
        icon.Released += _ =>
        {
            if (binding.Popup != null || !GravityRules.Applies(binding.Run)) return;
            NHoverTipSet.Remove(icon);
            Open(icon, binding);
        };
    }

    private static void Open(NTopBarFloorIcon icon, Binding binding)
    {
        var menu = new GravitySettingsPopup(icon, MenuSize, Localize("settings.appearance_title"));
        var popup = menu.Window;
        binding.Popup = popup;
        menu.Closed += () => binding.Popup = null;
        var panel = menu.Panel;
        var left = new VBoxContainer { Position = new Vector2(32, 88), Size = new Vector2(548, 270) };
        left.AddThemeConstantOverride("separation", 12);
        panel.AddChild(left);
        var colorHeading = new HBoxContainer();
        colorHeading.AddThemeConstantOverride("separation", 16);
        left.AddChild(colorHeading);
        var colorTitle = Label(Localize("settings.color"), 26);
        colorTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        colorHeading.AddChild(colorTitle);
        var reset = new Button { Text = Localize("settings.reset_color") };
        StyleButton(reset);
        colorHeading.AddChild(reset);
        var hue = Slider(0, 360, GravitySettings.Hue);
        StyleColorSlider(hue);
        left.AddChild(hue);
        Spacer(left, 12);
        var pulseHeading = new HBoxContainer();
        left.AddChild(pulseHeading);
        var pulseTitle = Label(Localize("settings.pulse"), 26);
        pulseTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pulseHeading.AddChild(pulseTitle);
        var pulseValue = Label("", 22);
        pulseHeading.AddChild(pulseValue);
        var pulse = Slider(0, 100, GravitySettings.PulsePercent);
        left.AddChild(pulse);

        var previewPanel = new Panel { Position = new Vector2(612, 88), Size = new Vector2(296, 334) };
        previewPanel.AddThemeStyleboxOverride("panel", Box("283642", "647079", 1));
        panel.AddChild(previewPanel);
        var previewTitle = Label(Localize("settings.preview"), 24);
        previewTitle.Position = new Vector2(16, 18);
        previewPanel.AddChild(previewTitle);
        var ring = new Control { Position = new Vector2(148, 100), MouseFilter = Control.MouseFilterEnum.Ignore };
        previewPanel.AddChild(ring);
        ring.Draw += () =>
        {
            // Show partial progress and an available boss together, independent of this run.
            ring.DrawSetTransform(new Vector2(-65, 0));
            GravityProgressDisplay.DrawArtwork(ring, 47, 7, 15, false, false);
            ring.DrawSetTransform(new Vector2(65, 0));
            GravityProgressDisplay.DrawArtwork(ring, 47, 15, 15, true, false);
        };
        var ringCaption = Label(Localize("settings.boss_preview"), 20, "C4CCD1");
        ringCaption.Position = new Vector2(16, 153);
        ringCaption.Size = new Vector2(264, 40);
        ringCaption.HorizontalAlignment = HorizontalAlignment.Center;
        previewPanel.AddChild(ringCaption);
        var encounterIcon = new TextureRect
        {
            // Ignore the texture's native minimum size before setting the preview bounds.
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = GD.Load<Texture2D>("res://images/atlases/ui_atlas.sprites/map/icons/map_monster.tres"),
            // Allow for the asset's transparent padding so the visible icon matches the map more closely.
            Position = new Vector2(100, 194), Size = new Vector2(96, 96), PivotOffset = new Vector2(48, 48),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        previewPanel.AddChild(encounterIcon);
        var pulseCaption = Label(Localize("settings.pulse_preview"), 20, "C4CCD1");
        pulseCaption.Position = new Vector2(16, 290);
        pulseCaption.Size = new Vector2(264, 40);
        pulseCaption.HorizontalAlignment = HorizontalAlignment.Center;
        previewPanel.AddChild(pulseCaption);
        var animation = new Godot.Timer { WaitTime = 1.0 / 30, Autostart = true };
        popup.AddChild(animation);
        var phase = 0f;
        animation.Timeout += () =>
        {
            phase += 4f / 30f;
            encounterIcon.Scale = Vector2.One * GravityPulse.Scale(phase);
        };

        void Changed()
        {
            menu.Changed();
            GravitySettings.RefreshAppearance();
            ring.QueueRedraw();
            encounterIcon.Scale = Vector2.One * GravityPulse.Scale(phase);
        }
        hue.ValueChanged += value => { GravitySettings.Hue = (int)value; Changed(); };
        reset.Pressed += () => hue.Value = GravitySettings.DefaultHue;
        void RefreshPulse() => pulseValue.Text = GravitySettings.PulsePercent == 0 ? Localize("settings.off")
            : string.Format(Localize("settings.percent"), GravitySettings.PulsePercent);
        pulse.ValueChanged += value => { GravitySettings.PulsePercent = (int)value; RefreshPulse(); Changed(); };
        RefreshPulse();
        GravitySettingsPopup.LinkFocus(reset, hue, pulse, menu.Done);
        menu.Show(hue);
    }

    internal static Label Label(string text, int size, string color = "E7E2D8")
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color(color));
        return label;
    }

    private static void Spacer(Control parent, int height) => parent.AddChild(new Control { CustomMinimumSize = new Vector2(0, height), MouseFilter = Control.MouseFilterEnum.Ignore });

    internal static HSlider Slider(int min, int max, int value)
    {
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = 1, Value = value,
            CustomMinimumSize = new Vector2(0, 48), FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand, Scrollable = false,
        };
        slider.AddThemeStyleboxOverride("slider", Box("131F29", "647079", 1));
        slider.AddThemeStyleboxOverride("grabber_area", Box("847347", "BFA16C", 1));
        slider.AddThemeStyleboxOverride("grabber_area_highlight", Box("BFA16C", "EAC477", 1));
        // A large thumb and 48px control height make the full track easy to drag.
        // Equal thumb dimensions in every state keep the drag range stable.
        slider.AddThemeIconOverride("grabber", SliderThumb("F4E5C7", false));
        slider.AddThemeIconOverride("grabber_highlight", SliderThumb("FFFFFF", true));
        slider.AddThemeIconOverride("grabber_disabled", SliderThumb("A6ADB2", false));
        slider.AddThemeConstantOverride("center_grabber", 0);
        return slider;
    }

    internal static void StyleColorSlider(HSlider slider)
    {
        var gradient = new Gradient();
        gradient.SetColor(0, GravitySettings.ColorAt(0));
        gradient.SetColor(1, GravitySettings.ColorAt(360));
        for (var hue = 1; hue < 360; hue++)
            gradient.AddPoint(hue / 360f, GravitySettings.ColorAt(hue));
        slider.AddThemeStyleboxOverride("slider", new StyleBoxTexture
        {
            Texture = new GradientTexture2D { Gradient = gradient, Width = 720, Height = 12 },
            ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        slider.AddThemeStyleboxOverride("grabber_area", new StyleBoxEmpty());
        slider.AddThemeStyleboxOverride("grabber_area_highlight", new StyleBoxEmpty());
    }

    private static Texture2D SliderThumb(string fill, bool highlighted)
    {
        var svg = $$"""
            <svg xmlns="http://www.w3.org/2000/svg" width="36" height="44" viewBox="0 0 36 44">
              <rect x="1" y="1" width="34" height="42" rx="9" fill="#202B35"/>
              <rect x="4" y="4" width="28" height="36" rx="6" fill="#{{fill}}"/>
              <path d="M18 12v20" stroke="#202B35" stroke-width="{{(highlighted ? 4 : 2)}}" stroke-linecap="round"/>
            </svg>
            """;
        using var image = new Image();
        image.LoadSvgFromString(svg);
        return ImageTexture.CreateFromImage(image);
    }

    internal static void StyleButton(BaseButton button)
    {
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        button.AddThemeStyleboxOverride("normal", Box("283642", "647079", 1));
        button.AddThemeStyleboxOverride("hover", Box("354957", "EAC477", 2));
        button.AddThemeStyleboxOverride("pressed", Box("17232D", "EAC477", 2));
        button.AddThemeStyleboxOverride("focus", new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = new Color("EAC477"), BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2 });
    }

    internal static StyleBoxFlat Box(string fill, string border, int width) => new()
    {
        BgColor = new Color(fill), BorderColor = new Color(border),
        BorderWidthLeft = width, BorderWidthTop = width, BorderWidthRight = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
        ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,
    };
}
