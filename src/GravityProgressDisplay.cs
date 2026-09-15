using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Gravity;

/// <summary>Non-interactive progress artwork; the game's map points still own input and focus.</summary>
internal sealed class GravityProgressDisplay
{
    private static readonly Color Gold = new("eac477");
    private static readonly Color MutedGold = new("a89166");
    private static readonly Color Track = new("514b43");
    private static readonly Color Ink = new("211c18");
    private readonly Label _counter;
    private readonly List<(NMapPoint Boss, Control Ring, float Radius)> _bosses = [];
    private int _visited;
    private bool _falling;

    public GravityProgressDisplay(NMapScreen screen, IEnumerable<(NMapPoint Node, float Radius)> bosses)
    {
        _counter = new Label
        {
            Name = "GravityProgress", MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(120f, 36f), HorizontalAlignment = HorizontalAlignment.Left
        };
        _counter.AddThemeFontSizeOverride("font_size", 26);
        _counter.AddThemeColorOverride("font_color", Gold);
        _counter.AddThemeColorOverride("font_outline_color", Ink);
        _counter.AddThemeConstantOverride("outline_size", 6);
        screen.AddChild(_counter);
        foreach (var (boss, radius) in bosses)
        {
            var ring = new Control { Name = "GravityBossProgress", MouseFilter = Control.MouseFilterEnum.Ignore };
            // Siblings keep the ring independent of the locked icon's dimming and hover scale.
            boss.GetParent().AddChild(ring);
            ring.Draw += () => DrawRing(ring, boss, radius);
            _bosses.Add((boss, ring, radius));
        }
    }

    public void Update(int visited, bool falling)
    {
        _visited = Math.Clamp(visited, 0, GravityProgress<MapCoord>.RequiredEncounters);
        _falling = falling;
        _counter.Text = string.Format(MainFile.Localize("map.progress"), _visited,
            GravityProgress<MapCoord>.RequiredEncounters);
        foreach (var (boss, ring, _) in _bosses)
        {
            boss.Modulate = boss.State == MapPointState.Untravelable ? new Color(0.6f, 0.6f, 0.6f) : Colors.White;
            ring.QueueRedraw();
        }
    }

    public void UpdatePositions(float screenWidth)
    {
        // The parchment is centered on screen. Keep the counter inset at its upper-left
        // corner, independent of the scrolling encounters and boss row.
        _counter.Position = new Vector2(Math.Max(30f, screenWidth / 2f - 560f), 105f);
        foreach (var (boss, ring, _) in _bosses)
            ring.Position = boss.Position + boss.Size / 2f;
    }

    public void Free()
    {
        _counter.QueueFree();
        foreach (var (_, ring, _) in _bosses)
            if (GodotObject.IsInstanceValid(ring)) ring.QueueFree();
    }

    private void DrawRing(Control ring, NMapPoint boss, float radius)
    {
        var available = !_falling && boss.State == MapPointState.Travelable;
        var cleared = boss.State == MapPointState.Traveled;
        var color = available ? Gold : cleared ? MutedGold : _visited < GravityProgress<MapCoord>.RequiredEncounters
            ? MutedGold : new Color("81796c");
        const int count = GravityProgress<MapCoord>.RequiredEncounters;
        // Start immediately left of the bottom lock and fill clockwise, leaving
        // room for the badge so neither the first nor last segment hides behind it.
        var lockGap = MathF.Asin(Math.Clamp(20f / radius, 0f, 1f));
        var segmentAngle = (Mathf.Tau - 2f * lockGap) / count;
        for (var i = 0; i < count; i++)
        {
            var start = Mathf.Pi / 2f + lockGap + i * segmentAngle + 0.035f;
            var end = start + segmentAngle - 0.07f;
            ring.DrawArc(Vector2.Zero, radius, start, end, 8, Ink, 8f, true);
            ring.DrawArc(Vector2.Zero, radius, start, end, 8, i < _visited ? color : Track, 4f, true);
        }

        // A lock/check badge makes sequential boss availability readable without relying on color.
        var badge = new Vector2(0f, radius);
        ring.DrawCircle(badge, 16f, Ink);
        if (available || cleared)
        {
            ring.DrawPolyline([badge + new Vector2(-7f, 0f), badge + new Vector2(-2f, 5f),
                badge + new Vector2(8f, -6f)], color, 3f, true);
        }
        else
        {
            ring.DrawArc(badge + new Vector2(0f, -3f), 5f, Mathf.Pi, Mathf.Tau, 16, color, 2f, true);
            ring.DrawRect(new Rect2(badge + new Vector2(-7f, -3f), new Vector2(14f, 11f)), color);
            ring.DrawCircle(badge + new Vector2(0f, 1f), 1.5f, Ink);
        }
    }
}
