using System.Globalization;
using Godot;
using static Gravity.MainFile;
using static Gravity.GravitySettingsMenu;

namespace Gravity;

// Shared by the native Mods screen and both optional library hosts.
internal sealed class GravitySettingsPanel
{
    public VBoxContainer Content { get; } = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    public Control[] FocusControls { get; }
    private readonly Action _commit;
    private readonly Godot.Timer _saveTimer = new() { OneShot = true, WaitTime = 0.35 };
    private readonly Label _error;
    private bool _dirty;

    public GravitySettingsPanel()
    {
        var content = Content;
        content.Theme = GravitySettingsPopup.CreateTheme();
        content.AddThemeConstantOverride("separation", 12);
        content.AddChild(_saveTimer);
        var heading = Label(Localize("settings.encounters"), 26);
        heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(heading);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        content.AddChild(row);
        var minus = new Button { Text = "−", CustomMinimumSize = new Vector2(48, 48) };
        var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(48, 48) };
        StyleButton(minus);
        StyleButton(plus);
        var number = new LineEdit
        {
            CustomMinimumSize = new Vector2(140, 48), Alignment = HorizontalAlignment.Center,
            MaxLength = 4, SelectAllOnFocus = true, VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.Number,
        };
        number.AddThemeStyleboxOverride("normal", Box("131F29", "647079", 1));
        number.AddThemeStyleboxOverride("focus", Box("131F29", "EAC477", 2));
        var resetEncounters = new Button { Text = Localize("settings.reset_encounters") };
        StyleButton(resetEncounters);
        row.AddChild(minus);
        row.AddChild(number);
        row.AddChild(plus);
        row.AddChild(resetEncounters);
        var hint = Label(Localize("settings.encounter_hint"), 20, "C4CCD1");
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(hint);
        var guidance = Label(Localize("settings.next_run"), 20, "C4CCD1");
        guidance.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(guidance);
        var numeric = GravitySettings.EncounterCount;
        var editing = false;
        void Refresh()
        {
            number.Text = numeric.ToString(CultureInfo.InvariantCulture);
            editing = false;
        }
        void SetNumber(int value)
        {
            numeric = Math.Clamp(value, 0, GravitySettings.MaxEncounterCount);
            GravitySettings.EncounterCount = numeric;
            Refresh();
            Changed();
        }
        void Commit()
        {
            if (!editing) return;
            if (int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) SetNumber(value);
            else Refresh();
        }
        number.TextChanged += _ => editing = true;
        number.TextSubmitted += _ => Commit();
        number.FocusExited += Commit;
        _commit = Commit;
        minus.Pressed += () => { Commit(); SetNumber(numeric - 1); };
        plus.Pressed += () => { Commit(); SetNumber(numeric + 1); };
        resetEncounters.Pressed += () => SetNumber(GravitySettings.DefaultEncounterCount);
        Refresh();
        content.AddChild(new HSeparator());
        var colorHeading = new HBoxContainer();
        var colorTitle = Label(Localize("settings.color"), 26);
        colorTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        colorHeading.AddChild(colorTitle);
        var reset = new Button { Text = Localize("settings.reset_color") };
        StyleButton(reset);
        colorHeading.AddChild(reset);
        content.AddChild(colorHeading);
        var hue = Slider(0, 360, GravitySettings.Hue);
        StyleColorSlider(hue);
        content.AddChild(hue);
        var pulseHeading = new HBoxContainer();
        var pulseTitle = Label(Localize("settings.pulse"), 26);
        pulseTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pulseHeading.AddChild(pulseTitle);
        var pulseValue = Label("", 22);
        pulseHeading.AddChild(pulseValue);
        content.AddChild(pulseHeading);
        var pulse = Slider(0, 100, GravitySettings.PulsePercent);
        content.AddChild(pulse);
        void RefreshPulse() => pulseValue.Text = GravitySettings.PulsePercent == 0 ? Localize("settings.off")
            : string.Format(Localize("settings.percent"), GravitySettings.PulsePercent);
        hue.ValueChanged += value =>
        {
            GravitySettings.Hue = (int)value;
            GravitySettings.RefreshAppearance();
            Changed();
        };
        reset.Pressed += () => hue.Value = GravitySettings.DefaultHue;
        pulse.ValueChanged += value =>
        {
            GravitySettings.PulsePercent = (int)value;
            RefreshPulse();
            GravitySettings.RefreshAppearance();
            Changed();
        };
        RefreshPulse();
        _error = Label("", 20, "FFAE94");
        _error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _error.Hide();
        content.AddChild(_error);
        _saveTimer.Timeout += Save;
        content.TreeExiting += Flush;
        content.VisibilityChanged += () =>
        {
            if (!content.IsVisibleInTree()) { Flush(); return; }
            numeric = GravitySettings.EncounterCount;
            Refresh();
            hue.SetValueNoSignal(GravitySettings.Hue);
            pulse.SetValueNoSignal(GravitySettings.PulsePercent);
            RefreshPulse();
        };
        FocusControls = [minus, number, plus, resetEncounters, reset, hue, pulse];
        // Paths can only be assigned once this panel is attached to its host.
        content.Ready += () => GravitySettingsPopup.LinkFocus(FocusControls);
    }

    private void Changed()
    {
        _dirty = true;
        if (Content.IsInsideTree()) _saveTimer.Start();
    }

    private void Save()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        _dirty = GravitySettings.Save() != Error.Ok;
        _error.Text = _dirty ? Localize("settings.save_error") : "";
        _error.Visible = _dirty;
    }

    public void Flush()
    {
        _commit();
        Save();
    }
}
