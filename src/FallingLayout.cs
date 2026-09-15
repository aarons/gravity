using System.Numerics;

namespace Gravity;

/// <summary>Fixed-step circle physics, isolated from Godot and the game's random streams.</summary>
internal static class FallingLayout
{
    public readonly record struct Body(Vector2 Position, float Radius, bool Anchored = false);
    public const float StepSeconds = 1f / 60f;
    public const int Steps = 480;

    public static Vector2[][] Simulate(IReadOnlyList<Body> bodies, float left, float right, float floor)
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
