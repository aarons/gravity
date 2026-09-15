using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using Vec = System.Numerics.Vector2;

namespace Gravity;

internal sealed class GravityMapView
{
    private static readonly ConditionalWeakTable<NMapScreen, GravityMapView> Views = new();
    private static readonly ConfigFile ViewedMaps = new();
    private static bool _loaded;
    private readonly NMapScreen _screen;
    private readonly RunState _run;
    private readonly NMapPoint[] _nodes;
    private readonly Control _container;
    private readonly Label _status;
    private readonly Vec[][] _frames;
    private readonly Vector2[] _centerOffsets;
    private readonly string _key;
    private readonly float _floor;
    private readonly float _top;
    private bool _opened;
    private float _elapsed;
    public bool Falling { get; private set; }
    public float MinScroll => _screen.Size.Y - 100f - _floor;
    public float MaxScroll => Math.Max(MinScroll, 160f - _top);

    public static GravityMapView? Get(NMapScreen screen) => Views.TryGetValue(screen, out var view) ? view : null;

    public static void Attach(NMapScreen screen, RunState run, ulong seed,
        Dictionary<MapCoord, NMapPoint> points)
    {
        if (Get(screen) is { } old) old._status.QueueFree();
        Views.Remove(screen);
        if (GravityRules.Applies(run))
        {
            var view = new GravityMapView(screen, run, seed, points);
            Views.Add(screen, view);
            if (screen.IsOpen) view.Open();
        }
    }

    private GravityMapView(NMapScreen screen, RunState run, ulong seed,
        Dictionary<MapCoord, NMapPoint> points)
    {
        _screen = screen;
        _run = run;
        _container = screen.GetNode<Control>("TheMap");
        _nodes = points.Values.OrderBy(node => node.Point.coord.row).ThenBy(node => node.Point.coord.col).ToArray();
        _centerOffsets = new Vector2[_nodes.Length];
        var bodies = new FallingLayout.Body[_nodes.Length];
        for (var i = 0; i < _nodes.Length; i++)
        {
            var node = _nodes[i];
            if (node is NBossMapPoint) node.Scale = Vector2.One * 0.65f;
            _centerOffsets[i] = node.Size * 0.5f;
            var center = node.Position + _centerOffsets[i];
            var radius = Math.Max(node.Size.X, node.Size.Y) * node.Scale.X * 0.5f + 12f;
            bodies[i] = new FallingLayout.Body(new Vec(center.X, center.Y), radius,
                node.Point.coord == run.Map.StartingMapPoint.coord);
        }
        var anchor = bodies.Single(body => body.Anchored);
        _floor = anchor.Position.Y + anchor.Radius + 24f;
        _frames = FallingLayout.Simulate(bodies, anchor.Position.X - 570f, anchor.Position.X + 510f, _floor);
        _top = _frames[^1].Select((position, i) => position.Y - bodies[i].Radius).Min();
        var startTime = AccessTools.Field(typeof(RunManager), "_startTime").GetValue(RunManager.Instance);
        var identity = $"v1:{startTime}:{seed}:{run.CurrentActIndex}:" + string.Join(";", _nodes.Select(node =>
            $"{node.Point.coord.col},{node.Point.coord.row}"));
        _key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        if (!_loaded)
        {
            ViewedMaps.Load("user://gravity_viewed_maps.cfg");
            _loaded = true;
        }
        _status = new Label
        {
            Name = "GravityProgress", MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(30f, 105f), Size = new Vector2(screen.Size.X - 60f, 70f)
        };
        _status.AddThemeFontSizeOverride("font_size", 26);
        _status.AddThemeColorOverride("font_color", new Color("f5deb3"));
        _status.AddThemeColorOverride("font_outline_color", new Color("211c18"));
        _status.AddThemeConstantOverride("outline_size", 6);
        screen.AddChild(_status);
    }

    public void Open()
    {
        if (!_opened)
        {
            Falling = !ViewedMaps.HasSectionKey("viewed", _key);
            _opened = true;
            ViewedMaps.SetValue("viewed", _key, true);
            // This is presentation state only; encounter progress is in the normal run save.
            var error = ViewedMaps.Save("user://gravity_viewed_maps.cfg");
            if (error != Error.Ok) GD.PushWarning($"[Gravity] Could not save viewed map state: {error}");
        }
        ApplyFrame(Falling ? 0 : FallingLayout.Steps);
        _screen.GetNode<NMapMarker>("TheMap/MapMarker").ResetMapPoint();
        if (!Falling) RefreshMarker();
        SetScroll(MinScroll);
        UpdateStatus();
        UpdateNavigation();
        // Keep the game's first-map tutorial: its completion gates the Ancient's click handler.
        _screen.CallDeferred("InitMapPrompt");
    }

    public void Process(double delta)
    {
        if (!_screen.IsOpen) return;
        if (Falling)
        {
            _elapsed += (float)Math.Min(delta, 0.1) * 2f;
            var frame = Math.Min(FallingLayout.Steps, (int)(_elapsed / FallingLayout.StepSeconds));
            ApplyFrame(frame);
            if (frame == FallingLayout.Steps)
            {
                Falling = false;
                RefreshMarker();
                UpdateNavigation();
                UpdateStatus();
            }
        }
        _status.Size = new Vector2(_screen.Size.X - 60f, 70f);
    }

    public void Finish()
    {
        if (!_opened) return;
        Falling = false;
        ApplyFrame(FallingLayout.Steps);
        UpdateNavigation();
    }

    private void RefreshMarker()
    {
        var current = _nodes.FirstOrDefault(node => node.Point.coord == _run.CurrentMapCoord);
        if (current != null) _screen.GetNode<NMapMarker>("TheMap/MapMarker").SetMapPoint(current);
    }

    private void ApplyFrame(int frame)
    {
        for (var i = 0; i < _nodes.Length; i++)
        {
            if (_nodes[i].Point.coord == _run.Map.StartingMapPoint.coord) continue;
            var point = _frames[frame][i];
            _nodes[i].Position = new Vector2(point.X, point.Y) - _centerOffsets[i];
        }
    }

    public void SetScroll(float y)
    {
        var position = new Vector2(0f, Math.Clamp(y, MinScroll, MaxScroll));
        _container.Position = position;
        AccessTools.Field(typeof(NMapScreen), "_targetDragPos").SetValue(_screen, position);
    }

    public void ScrollToFocusedPoint()
    {
        if (_screen.GetViewport().GuiGetFocusOwner() is not NMapPoint point) return;
        var center = point.Position.Y + point.Size.Y / 2;
        SetScroll(_screen.Size.Y / 2f - center);
    }

    public void UpdateStatus()
    {
        var progress = GravityRules.Progress(_run);
        _status.Text = Falling ? MainFile.Localize("map.falling")
            : !progress.Started ? MainFile.Localize("map.ancient_first")
            : progress.EncountersVisited < 15
                ? string.Format(MainFile.Localize("map.progress"), progress.EncountersVisited, 15)
                : MainFile.Localize("map.boss_unlocked");
    }

    public void UpdateNavigation()
    {
        var candidates = _nodes.Where(node => node.State == MapPointState.Travelable).ToArray();
        foreach (var node in _nodes)
        {
            var center = node.Position + node.Size / 2;
            NodePath Neighbor(Vector2 direction)
            {
                var nearest = candidates.Where(other => other != node)
                    .Select(other => (Node: other, Delta: other.Position + other.Size / 2 - center))
                    .Where(other => other.Delta.Dot(direction) > 1f)
                    .OrderBy(other => other.Delta.LengthSquared()
                        + MathF.Pow(other.Delta.Cross(direction), 2) * 3f)
                    .FirstOrDefault();
                return (nearest.Node ?? node).GetPath();
            }
            node.FocusNeighborLeft = Neighbor(Vector2.Left);
            node.FocusNeighborRight = Neighbor(Vector2.Right);
            node.FocusNeighborTop = Neighbor(Vector2.Up);
            node.FocusNeighborBottom = Neighbor(Vector2.Down);
        }
    }
}
