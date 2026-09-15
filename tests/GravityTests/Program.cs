using Gravity;
using System.Numerics;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
var encounters = Enumerable.Range(1, 60).ToArray();
GravityProgress<int> Progress(params int[] visited) => new(0, encounters, [100, 101], visited);
Check(Progress().Available.SetEquals([0]), "The Ancient must be first");
Check(Progress(0).Available.SetEquals(encounters), "All encounters unlock after the Ancient");
var visits = new List<int> { 0 };
foreach (var room in new[] { 60, 4, 51, 7, 9, 40, 20, 3, 2, 31, 17, 15, 33, 44, 10 })
{
    var before = Progress(visits.ToArray());
    Check(before.Available.Contains(room), "Must allow backward and same-row choices");
    Check(!before.Available.Contains(100), "Boss must stay locked before encounter 15");
    visits.Add(room);
    var after = Progress(visits.ToArray());
    Check(!after.Available.Overlaps(visits), "Visited rooms must never be available");
    Check(after.EncountersVisited == visits.Count - 1, "Ancient must not count toward the 15");
}
Check(Progress(visits.ToArray()).Available.SetEquals([100]), "Only the boss unlocks at 15");
Check(Progress(visits.Concat(visits).ToArray()).EncountersVisited == 15, "Duplicate visits do not add progress");
visits.Add(100);
Check(Progress(visits.ToArray()).Available.SetEquals([101]), "Double boss unlocks sequentially");
visits.Add(101);
Check(Progress(visits.ToArray()).Available.Count == 0, "No more rooms after the final boss");
Check(Progress().EncountersVisited == 0, "A new act starts with no progress");
var serialized = System.Text.Json.JsonSerializer.Serialize(visits);
Check(Progress(System.Text.Json.JsonSerializer.Deserialize<int[]>(serialized)!).Available.Count == 0,
    "Reconstructed progress matches saved visits");

for (var seed = 0; seed < 8; seed++)
{
    var random = new Random(seed);
    var bodies = new List<FallingLayout.Body> { new(new Vector2(0, 800), 105, true) };
    for (var row = 1; row <= 15; row++)
        for (var column = 0; column < 4; column++)
            bodies.Add(new(new Vector2(-450 + column * 265 + random.Next(-20, 21), 800 - row * 155), 44));
    bodies.Add(new(new Vector2(0, -1900), 142));
    if (seed % 2 == 0) bodies.Add(new(new Vector2(0, -2200), 142));
    var frames = FallingLayout.Simulate(bodies, -570, 510, 929);
    var final = frames[^1];
    Check(frames.All(frame => frame[0] == bodies[0].Position), "The Ancient must never move");
    Check(frames.SelectMany(frame => frame).All(p => float.IsFinite(p.X) && float.IsFinite(p.Y)), "Physics must stay finite");
    for (var i = 1; i < bodies.Count; i++)
    {
        Check(final[i].X >= -570 + bodies[i].Radius - 0.1 && final[i].X <= 510 - bodies[i].Radius + 0.1,
            "An encounter escaped the map's sides");
        Check(final[i].Y <= 929 - bodies[i].Radius + 0.1, "An encounter fell through the floor");
        Check(final[i].Y > bodies[i].Position.Y, "An encounter did not fall");
        for (var j = 0; j < i; j++)
            Check(Vector2.Distance(final[i], final[j]) >= bodies[i].Radius + bodies[j].Radius - 1,
                $"Overlapping encounter hit areas (seed {seed}, bodies {i}/{j})");
    }
    Check(final.Select((p, i) => p.Y - bodies[i].Radius).Min() > -150, "Map did not compact enough");
    Check(final.Zip(frames[^2]).Max(pair => Vector2.Distance(pair.First, pair.Second)) < 1,
        "Pile was still moving when frozen");
    if (seed == 0)
        Check(FallingLayout.Simulate(bodies, -570, 510, 929)[^1].SequenceEqual(final), "Reload must produce exactly the same pile");
}
Console.WriteLine($"Passed {checks} Gravity progression and physics checks.");
