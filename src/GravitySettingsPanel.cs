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

    public GravitySettingsPanel(bool showTitle = true)
    {
        var content = Content;
        content.Theme = GravitySettingsPopup.CreateTheme();
        content.AddThemeConstantOverride("separation", 12);
        content.AddChild(_saveTimer);
        if (showTitle)
        {
            var title = Label(Localize("settings.title"), 32);
            title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            content.AddChild(title);
        }
        var heading = Label(Localize("settings.encounters"), 26);
        heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(heading);
        var group = new ButtonGroup();
        CheckBox Option(string text, bool independent = false)
        {
            var optionRow = new HBoxContainer();
            var option = new CheckBox { ButtonGroup = independent ? null : group, CustomMinimumSize = new Vector2(48, 48) };
            StyleButton(option);
            optionRow.AddChild(option);
            var caption = Label(text, 22);
            caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            caption.MouseFilter = Control.MouseFilterEnum.Stop;
            caption.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            caption.GuiInput += input =>
            {
                if (!option.Disabled && input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    option.GrabFocus();
                    option.ButtonPressed = !independent || !option.ButtonPressed;
                    caption.AcceptEvent();
                }
            };
            optionRow.AddChild(caption);
            content.AddChild(optionRow);
            return option;
        }
        var none = Option(Localize("settings.encounters_none"));
        var normal = Option(Localize("settings.encounters_default"));
        var all = Option(Localize("settings.encounters_all"));
        var custom = Option(Localize("settings.encounters_custom"));
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
        row.AddChild(minus);
        row.AddChild(number);
        row.AddChild(plus);
        row.AddChild(Label(Localize("settings.custom_range"), 20, "C4CCD1"));
        var customHint = Label(Localize("settings.custom_hint"), 20, "C4CCD1");
        customHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(customHint);
        var lockEncounters = Option(Localize("settings.lock_encounters"), independent: true);
        var guidance = Label(Localize("settings.next_run"), 20, "C4CCD1");
        guidance.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(guidance);
        var editing = false;
        Action? refreshCustomFocus = null;
        void Refresh()
        {
            number.Text = GravitySettings.CustomEncounterCount.ToString(CultureInfo.InvariantCulture);
            editing = false;
            none.SetPressedNoSignal(GravitySettings.Mode == GravitySettings.EncounterMode.None);
            normal.SetPressedNoSignal(GravitySettings.Mode == GravitySettings.EncounterMode.Default);
            all.SetPressedNoSignal(GravitySettings.Mode == GravitySettings.EncounterMode.All);
            custom.SetPressedNoSignal(GravitySettings.Mode == GravitySettings.EncounterMode.Custom);
            if (!custom.ButtonPressed && (number.HasFocus() || minus.HasFocus() || plus.HasFocus()))
                none.GrabFocus();
            row.Visible = customHint.Visible = custom.ButtonPressed;
            lockEncounters.SetPressedNoSignal(GravitySettings.LockEncountersAfterBossUnlock);
            lockEncounters.Disabled = GravitySettings.NextRunRequirement == 0;
            if (lockEncounters.Disabled && lockEncounters.HasFocus()) none.GrabFocus();
            lockEncounters.FocusMode = lockEncounters.Disabled ? Control.FocusModeEnum.None : Control.FocusModeEnum.All;
            lockEncounters.GetParent<Control>().Modulate = new Color(1f, 1f, 1f, lockEncounters.Disabled ? 0.45f : 1f);
            refreshCustomFocus?.Invoke();
        }
        void SetNumber(int value)
        {
            GravitySettings.CustomEncounterCount = value;
            GravitySettings.Mode = GravitySettings.CustomEncounterCount == 0
                ? GravitySettings.EncounterMode.None : GravitySettings.EncounterMode.Custom;
            Refresh();
            Changed();
        }
        void Commit()
        {
            if (!editing) return;
            if (int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) SetNumber(value);
            else Refresh();
        }
        void Select(GravitySettings.EncounterMode mode)
        {
            Commit();
            GravitySettings.Mode = mode;
            Refresh();
            Changed();
        }
        // Programmatic selection when a caption is clicked also emits Toggled.
        none.Toggled += selected => { if (selected) Select(GravitySettings.EncounterMode.None); };
        normal.Toggled += selected => { if (selected) Select(GravitySettings.EncounterMode.Default); };
        all.Toggled += selected => { if (selected) Select(GravitySettings.EncounterMode.All); };
        custom.Toggled += selected => { if (selected) Select(GravitySettings.EncounterMode.Custom); };
        lockEncounters.Toggled += selected =>
        {
            GravitySettings.LockEncountersAfterBossUnlock = selected;
            Changed();
        };
        number.TextChanged += _ => editing = true;
        number.TextSubmitted += _ => Commit();
        number.FocusExited += Commit;
        _commit = Commit;
        minus.Pressed += () => { Commit(); SetNumber(GravitySettings.CustomEncounterCount - 1); };
        plus.Pressed += () => { Commit(); SetNumber(GravitySettings.CustomEncounterCount + 1); };
        Refresh();
        _error = Label("", 20, "FFAE94");
        _error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _error.Hide();
        content.AddChild(_error);
        _saveTimer.Timeout += Save;
        content.TreeExiting += Flush;
        content.VisibilityChanged += () =>
        {
            if (!content.IsVisibleInTree()) { Flush(); return; }
            Refresh();
        };
        FocusControls = [none, normal, all, custom, minus, number, plus, lockEncounters];
        // Skip hidden custom controls while preserving host links at the panel edges.
        // Godot skips the lock checkbox when its FocusMode is None.
        void LinkCustomFocus()
        {
            Control beforeLock = custom.ButtonPressed ? plus : custom;
            custom.FocusNext = custom.FocusNeighborBottom = custom.GetPathTo(custom.ButtonPressed ? minus : lockEncounters);
            lockEncounters.FocusPrevious = lockEncounters.FocusNeighborTop = lockEncounters.GetPathTo(beforeLock);
        }
        // Paths can only be assigned once this panel is attached to its host.
        content.Ready += () =>
        {
            GravitySettingsPopup.LinkFocus(FocusControls);
            refreshCustomFocus = LinkCustomFocus;
            LinkCustomFocus();
            minus.FocusNeighborRight = minus.GetPathTo(number);
            number.FocusNeighborLeft = number.GetPathTo(minus);
            number.FocusNeighborRight = number.GetPathTo(plus);
            plus.FocusNeighborLeft = plus.GetPathTo(number);
        };
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
