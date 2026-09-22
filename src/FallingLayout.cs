using System.Numerics;

namespace Gravity;

/// <summary>Fixed-step circle physics, isolated from Godot and the game's random streams.</summary>
internal static class FallingLayout
{
    public readonly record struct Body(Vector2 Position, float Radius, bool Anchored = false, bool Boss = false);
    public const float StepSeconds = 1f / 60f;
    public const int Steps = 480;

    public static Vector2[][] Simulate(IReadOnlyList<Body> bodies, float left, float right, float floor)
    {
        // Bosses have separate landing rows; they never collide with or weigh down the pile.
        var pileIndices = Enumerable.Range(0, bodies.Count).Where(i => !bodies[i].Boss).ToArray();
        var bossIndices = Enumerable.Range(0, bodies.Count).Where(i => bodies[i].Boss).ToArray();
        var pile = pileIndices.Select(i => bodies[i]).ToArray();
        var pileFrames = SimulatePile(pile, left, right, floor);
        if (bossIndices.Length == 0) return pileFrames;

        var pileTop = pileFrames[^1].Select((position, i) => position.Y - pile[i].Radius).Min();
        var destinations = new Vector2[bossIndices.Length];
        var rowBottom = pileTop - 64f;
        for (var start = 0; start < bossIndices.Length;)
        {
            var end = start + 1;
            var rowWidth = bodies[bossIndices[start]].Radius * 2f;
            var rowRadius = bodies[bossIndices[start]].Radius;
            while (end < bossIndices.Length)
            {
                var radius = bodies[bossIndices[end]].Radius;
                if (rowWidth + 48f + radius * 2f > right - left) break;
                rowWidth += 48f + radius * 2f;
                rowRadius = Math.Max(rowRadius, radius);
                end++;
            }
            var x = (left + right - rowWidth) / 2f;
            for (var b = start; b < end; b++)
            {
                var radius = bodies[bossIndices[b]].Radius;
                destinations[b] = new Vector2(x + radius, rowBottom - rowRadius);
                x += radius * 2f + 48f;
            }
            rowBottom -= rowRadius * 2f + 48f;
            start = end;
        }

        var frames = new Vector2[Steps + 1][];
        for (var step = 0; step <= Steps; step++)
        {
            var frame = frames[step] = new Vector2[bodies.Count];
            for (var p = 0; p < pileIndices.Length; p++) frame[pileIndices[p]] = pileFrames[step][p];
            // Let the pile fall first, then lower the bosses gently into place without overshoot.
            var t = Math.Clamp((step / (float)Steps - 0.35f) / 0.45f, 0f, 1f);
            var ease = 1f - MathF.Pow(1f - t, 3f);
            for (var b = 0; b < bossIndices.Length; b++)
                frame[bossIndices[b]] = Vector2.Lerp(bodies[bossIndices[b]].Position, destinations[b], ease);
        }
        return frames;
    }

    private static Vector2[][] SimulatePile(IReadOnlyList<Body> bodies, float left, float right, float floor)
    {
        var positions = bodies.Select(body => body.Position).ToArray();
        var velocities = new Vector2[bodies.Count];
        var frames = new Vector2[Steps + 1][];
        frames[0] = positions.ToArray();
        for (var step = 1; step <= Steps; step++)
        {
            var previous = positions.ToArray();
            for (var i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].Anchored) continue;
                velocities[i].Y += 2100f * StepSeconds;
                positions[i] += velocities[i] * StepSeconds;
            }

            // Multiple passes resolve piles without allowing icons to sink into each other.
            for (var pass = 0; pass < 12; pass++)
            {
                for (var i = 0; i < bodies.Count; i++)
                {
                    for (var j = i + 1; j < bodies.Count; j++)
                    {
                        if (bodies[i].Anchored && bodies[j].Anchored) continue;
                        var delta = positions[j] - positions[i];
                        var distance = delta.Length();
                        var separation = bodies[i].Radius + bodies[j].Radius + 6f;
                        if (distance >= separation) continue;
                        var normal = distance > 0.001f ? delta / distance : Vector2.UnitX;
                        var correction = normal * (separation - distance);
                        var share = bodies[i].Anchored || bodies[j].Anchored ? 1f : 0.5f;
                        if (!bodies[i].Anchored) positions[i] -= correction * share;
                        if (!bodies[j].Anchored) positions[j] += correction * share;
                    }
                }
                for (var i = 0; i < bodies.Count; i++)
                {
                    if (bodies[i].Anchored) continue;
                    positions[i].X = Math.Clamp(positions[i].X, left + bodies[i].Radius, right - bodies[i].Radius);
                    positions[i].Y = Math.Min(positions[i].Y, floor - bodies[i].Radius);
                }
            }
            for (var i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].Anchored) continue;
                velocities[i] = (positions[i] - previous[i]) / StepSeconds * 0.985f;
                if (positions[i].Y >= floor - bodies[i].Radius - 0.1f)
                    velocities[i].X *= 0.8f;
            }
            frames[step] = positions.ToArray();
        }
        return frames;
    }
}
