using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.Fonts;
using static Gravity.MainFile;
using static Gravity.GravitySettingsMenu;

namespace Gravity;

// Both entry points share saving, dismissal, scaling, and focus restoration.
internal sealed class GravitySettingsPopup
{
    public PopupPanel Window { get; }
    public Panel Panel { get; }
    public Button Done { get; }
    public event Action? Closed;
    public Action? CommitInput { get; set; }
    private readonly Control _opener;
    private readonly Vector2I _size;
    private readonly Control? _previousFocus;
    private readonly Godot.Timer _saveTimer;
    private readonly Label _error;
    private bool _dirty;
    private bool _closed;

    public GravitySettingsPopup(Control opener, Vector2I size, string title)
    {
        _opener = opener;
        _size = size;
        _previousFocus = opener.GetViewport().GuiGetFocusOwner();
        Window = new PopupPanel
        {
            Name = "GravitySettings", Title = title, Exclusive = true, Transient = true, Unresizable = true,
            ContentScaleSize = size, ContentScaleMode = Godot.Window.ContentScaleModeEnum.CanvasItems,
        };
        opener.AddChild(Window);
        Panel = new Panel
        {
            Size = size,
            Theme = CreateTheme(),
        };
        Panel.AddThemeStyleboxOverride("panel", Box("202B35", "BFA16C", 2));
        Window.AddChild(Panel);
        var heading = Label(title, 32);
        heading.Position = new Vector2(32, 22);
        Panel.AddChild(heading);
        _error = Label("", 20, "FFAE94");
        _error.Position = new Vector2(32, size.Y - 80);
        _error.Size = new Vector2(size.X - 232, 60);
        _error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Panel.AddChild(_error);
        Done = new Button
        {
            Text = Localize("settings.done"), Position = new Vector2(size.X - 168, size.Y - 76),
            Size = new Vector2(136, 44),
        };
        StyleButton(Done);
        Panel.AddChild(Done);
        _saveTimer = new Godot.Timer { OneShot = true, WaitTime = 0.35 };
        Window.AddChild(_saveTimer);
        _saveTimer.Timeout += () => Save();
        Done.Pressed += Close;
        Window.CloseRequested += Close;
        Window.WindowInput += input =>
        {
            if (input.IsActionPressed("ui_cancel") || input is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                Window.SetInputAsHandled();
                Close();
            }
        };
        Window.PopupHide += () =>
        {
            CommitInput?.Invoke();
            Save();
            NotifyClosed();
            Window.QueueFree();
            if (GodotObject.IsInstanceValid(_previousFocus) && _previousFocus!.IsInsideTree()
                && _previousFocus.IsVisibleInTree()) _previousFocus.GrabFocus();
        };
        var viewport = opener.GetViewport();
        viewport.SizeChanged += Fit;
        opener.VisibilityChanged += OnOpenerVisibilityChanged;
        Window.TreeExiting += () =>
        {
            CommitInput?.Invoke();
            Save();
            viewport.SizeChanged -= Fit;
            opener.VisibilityChanged -= OnOpenerVisibilityChanged;
            NotifyClosed();
        };
    }

    public void Changed()
    {
        _dirty = true;
        _saveTimer.Start();
    }

    internal static Theme CreateTheme() => new()
    {
        DefaultFontSize = 22,
        DefaultFont = FontManager.GetSubstituteFont(LocManager.Instance.Language, FontType.Bold)
            ?? GD.Load<Font>("res://themes/kreon_bold_glyph_space_one.tres"),
    };

    private void NotifyClosed()
    {
        if (_closed) return;
        _closed = true;
        Closed?.Invoke();
    }

    private bool Save()
    {
        _saveTimer.Stop();
        if (!_dirty) return true;
        _dirty = GravitySettings.Save() != Error.Ok;
        _error.Text = _dirty ? Localize("settings.save_error") : "";
        return !_dirty;
    }

    private void Close()
    {
        CommitInput?.Invoke();
        if (Save()) Window.Hide();
    }

    private void OnOpenerVisibilityChanged()
    {
        if (!_opener.IsVisibleInTree()) Close();
    }

    private void Fit()
    {
        if (!Window.Visible) return;
        var available = _opener.GetViewportRect().Size - new Vector2(32, 32);
        var scale = Mathf.Max(0.1f, Mathf.Min(_opener.GetGlobalTransformWithCanvas().Scale.X,
            Mathf.Min(available.X / _size.X, available.Y / _size.Y)));
        Window.Size = new Vector2I((int)(_size.X * scale), (int)(_size.Y * scale));
        Window.MoveToCenter();
    }

    public void Show(Control first)
    {
        Window.PopupCentered(_size);
        Fit();
        first.GrabFocus();
    }

    public static void LinkFocus(params Control[] controls)
    {
        for (var i = 0; i < controls.Length; i++)
        {
            var current = controls[i];
            var previous = current.GetPathTo(controls[(i + controls.Length - 1) % controls.Length]);
            var next = current.GetPathTo(controls[(i + 1) % controls.Length]);
            current.FocusPrevious = current.FocusNeighborTop = previous;
            current.FocusNext = current.FocusNeighborBottom = next;
        }
    }
}
