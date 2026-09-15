using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Gravity;

/// <summary>Non-interactive progress artwork; the game's map points still own input and focus.</summary>
internal sealed class GravityProgressDisplay
{
    private static readonly Color Track = new("514b43");
    private static readonly Color Ink = new("211c18");
    private readonly List<(NMapPoint Boss, Control Ring, float Radius)> _bosses = [];
    private int _visited;
    private bool _falling;
    private int _required;

    public GravityProgressDisplay(IEnumerable<(NMapPoint Node, float Radius)> bosses)
    {
        foreach (var (boss, radius) in bosses)
        {
            var ring = new Control { Name = "GravityBossProgress", MouseFilter = Control.MouseFilterEnum.Ignore };
            // Siblings keep the ring independent of the locked icon's dimming and hover scale.
            boss.GetParent().AddChild(ring);
            ring.Draw += () => DrawRing(ring, boss, radius);
            _bosses.Add((boss, ring, radius));
        }
    }

    public void Update(int visited, int required, bool falling)
    {
        _visited = visited;
        _required = required;
        _falling = falling;
        foreach (var (boss, ring, _) in _bosses)
        {
            ring.Visible = required > 0;
            boss.Modulate = boss.State == MapPointState.Untravelable ? new Color(0.6f, 0.6f, 0.6f) : Colors.White;
            ring.QueueRedraw();
        }
    }

    public void UpdatePositions()
    {
        foreach (var (boss, ring, _) in _bosses)
            ring.Position = boss.Position + boss.Size / 2f;
    }

    public void Free()
    {
        foreach (var (_, ring, _) in _bosses)
            if (GodotObject.IsInstanceValid(ring)) ring.QueueFree();
    }

    private void DrawRing(Control ring, NMapPoint boss, float radius)
    {
        var available = !_falling && boss.State == MapPointState.Travelable;
        var cleared = boss.State == MapPointState.Traveled;
        DrawArtwork(ring, radius, _visited, _required, available, cleared);
    }

    internal static void DrawArtwork(Control ring, float radius, int visited, int required, bool available, bool cleared)
    {
        if (required <= 0) return;
        var gold = GravitySettings.HighlightColor;
        var muted = gold.Lerp(new Color("625f59"), 0.45f);
        var color = available ? gold : cleared || visited < required ? muted : new Color("81796c");
        // Dense requirements use a continuous track so tiny segments don't overlap.
        var count = Math.Min(required, 40);
        var filled = Math.Clamp(visited / (float)required, 0f, 1f) * count;
        // Start immediately left of the top lock and fill counterclockwise, leaving
        // room for the badge so neither the first nor last segment hides behind it.
        var lockGap = MathF.Asin(Math.Clamp(20f / radius, 0f, 1f));
        var segmentAngle = (Mathf.Tau - 2f * lockGap) / count;
        for (var i = 0; i < count; i++)
        {
            var gap = required > 40 ? 0f : Math.Min(0.035f, segmentAngle * 0.15f);
            var end = -Mathf.Pi / 2f - lockGap - i * segmentAngle - gap;
            var start = end - segmentAngle + gap * 2f;
            var points = Math.Max(8, (int)MathF.Ceiling((end - start) / 0.09f) + 1);
            ring.DrawArc(Vector2.Zero, radius, start, end, points, Ink, 8f, true);
            ring.DrawArc(Vector2.Zero, radius, start, end, points, Track, 4f, true);
            var portion = Math.Clamp(filled - i, 0f, 1f);
            if (portion > 0f) ring.DrawArc(Vector2.Zero, radius, end - (end - start) * portion, end, points, color, 4f, true);
        }

        // A lock/check badge makes sequential boss availability readable without relying on color.
        var badge = new Vector2(0f, -radius);
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
