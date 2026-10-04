using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using static Gravity.MainFile;

namespace Gravity;

internal sealed class GravityEncounterSelector
{
    private sealed record Row(NMapLegendItem Item, MegaLabel Label, MapPointType Type,
        string Text, Color Color, Color LabelColor, Control.CursorShape Cursor, Callable Released)
    {
        public bool Available { get; set; }
        public Callable FocusChanged { get; set; }
    }

    private readonly NMapScreen _screen;
    private readonly RunState _run;
    private readonly NMapPoint[] _points;
    private readonly GravityMapView _view;
    private readonly MegaLabel _header;
    private readonly string _headerText;
    private readonly Control _legendItems;
    private readonly float _legendItemsLeft;
    private readonly float _legendItemsRight;
    private readonly Row[] _rows;

    public GravityEncounterSelector(NMapScreen screen, RunState run, NMapPoint[] points, GravityMapView view)
    {
        _screen = screen;
        _run = run;
        _points = points;
        _view = view;
        _header = screen.GetNode<MegaLabel>("MapLegend/Header");
        _headerText = _header.Text;
        _header.SetTextAutoSize(Localize("map.choose"));
        _legendItems = screen.GetNode<Control>("MapLegend/LegendItems");
        _legendItemsLeft = _legendItems.OffsetLeft;
        _legendItemsRight = _legendItems.OffsetRight;
        // Move the rows and their click targets together, preserving their width and anchors.
        _legendItems.OffsetLeft -= 20f;
        _legendItems.OffsetRight -= 20f;
        _rows = _legendItems.GetChildren()
            .OfType<NMapLegendItem>().Select(item =>
            {
                var type = (MapPointType)AccessTools.Field(typeof(NMapLegendItem), "_pointType").GetValue(item)!;
                var label = item.GetNode<MegaLabel>("MegaLabel");
                var released = Callable.From<NButton>(_ => Select(type));
                var row = new Row(item, label, type, label.Text, item.Modulate, label.SelfModulate,
                    item.MouseDefaultCursorShape, released);
                row.FocusChanged = Callable.From<NClickableControl>(_ => RefreshTextColor(row));
                item.Connect(NClickableControl.SignalName.Released, released);
                item.Connect(NClickableControl.SignalName.Focused, row.FocusChanged);
                item.Connect(NClickableControl.SignalName.Unfocused, row.FocusChanged);
                return row;
            }).ToArray();
    }

    public void Refresh(GravityProgress<MapCoord> progress)
    {
        var counts = _points.Where(point => progress.Available.Contains(point.Point.coord))
            .GroupBy(point => point.Point.PointType).ToDictionary(group => group.Key, group => group.Count());
        foreach (var row in _rows)
        {
            var count = counts.GetValueOrDefault(row.Type);
            row.Available = count > 0;
            var text = string.Format(Localize("map.choose.count"), row.Text, count);
            if (row.Label.Text != text) row.Label.SetTextAutoSize(text);
            row.Item.Modulate = count > 0 ? row.Color : row.Color * new Color(0.55f, 0.55f, 0.55f, 0.65f);
            row.Item.MouseDefaultCursorShape = count > 0 ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
            RefreshTextColor(row);
            // Keep exhausted rows focusable for the native descriptions and legend hotkey.
            // Select rechecks availability, so they cannot initiate travel.
        }
    }

    private static void RefreshTextColor(Row row)
    {
        // Follow the native icon's combined mouse/controller focus without changing text metrics.
        row.Label.SelfModulate = row.Available && row.Item.Get(NClickableControl.PropertyName.IsFocused).AsBool()
            ? row.LabelColor * new Color(0.8f, 0.8f, 0.8f, 1f)
            : row.LabelColor;
    }

    private void Select(MapPointType type)
    {
        if (!_screen.IsOpen || !_screen.IsTravelEnabled || _screen.IsTraveling
            || !ActiveScreenContext.Instance.IsCurrent(_screen)
            || _screen.Drawings.GetLocalDrawingMode() != DrawingMode.None) return;
        var available = GravityRules.Progress(_run).Available;
        var candidates = _points.Where(point => point.Point.PointType == type
            && available.Contains(point.Point.coord)).ToArray();
        if (candidates.Length == 0) return;
        // A UI convenience must not advance any of the seeded gameplay RNG streams.
        var selected = candidates[Random.Shared.Next(candidates.Length)];
        _view.Finish();
        _view.SetScroll(_screen.Size.Y / 2f - (selected.Position.Y + selected.Size.Y / 2f));
        // Use the point's native handler for tutorial guards and normal local/co-op voting.
        selected.Call(NMapPoint.MethodName.OnRelease);
    }

    public void Detach()
    {
        _header.SetTextAutoSize(_headerText);
        _legendItems.OffsetLeft = _legendItemsLeft;
        _legendItems.OffsetRight = _legendItemsRight;
        foreach (var row in _rows)
        {
            row.Item.Disconnect(NClickableControl.SignalName.Released, row.Released);
            row.Item.Disconnect(NClickableControl.SignalName.Focused, row.FocusChanged);
            row.Item.Disconnect(NClickableControl.SignalName.Unfocused, row.FocusChanged);
            row.Label.SetTextAutoSize(row.Text);
            row.Label.SelfModulate = row.LabelColor;
            row.Item.Modulate = row.Color;
            row.Item.MouseDefaultCursorShape = row.Cursor;
        }
    }
}
