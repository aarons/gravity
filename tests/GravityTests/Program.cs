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
Check(Progress(visits.ToArray()).Available.SetEquals(encounters.Except(visits).Append(100)),
    "Boss unlocks at 15 while unvisited encounters remain available");
var extraVisit = Progress(visits.Append(11).ToArray());
Check(extraVisit.EncountersVisited == 16 && extraVisit.Available.Contains(100) && !extraVisit.Available.Contains(11),
    "Players can continue exploring beyond the unlock requirement");
foreach (var requirement in new[] { 0, 1, 15, 60, 99, 999, -1 })
foreach (var lockEncounters in new[] { false, true })
{
    var target = requirement == -1 ? 60 : Math.Min(requirement, 60);
    GravityProgress<int> WithRule(IEnumerable<int> rooms) => new(0, encounters, [100, 101], rooms, requirement, lockEncounters);
    Check(WithRule([]).Available.SetEquals([0]), "Even unrestricted runs must start at the Ancient");
    Check(WithRule([0]).RequiredEncounters == target, "All and oversized requirements match the act pool");
    for (var n = 0; n <= 60; n++)
    {
        var path = new[] { 0 }.Concat(encounters.Take(n)).ToArray();
        var state = WithRule(path);
        Check(state.Available.Contains(100) == (n >= target), "Boss must unlock exactly at the chosen minimum");
        Check(!state.Available.Contains(101), "Second boss stays locked until first boss");
        var locked = lockEncounters && target > 0 && n >= target;
        Check(state.Available.Intersect(encounters).Count() == (locked ? 0 : 60 - n),
            "Optional lock must close encounters at the requirement, except when the boss is always unlocked");
        if (n >= target)
        {
            Check(WithRule(path.Append(100)).Available.SetEquals([101]), "Entering boss closes encounters");
            Check(WithRule(path.Concat(new[] { 100, 101 })).Available.Count == 0, "Boss chain ends the act");
        }
    }
}
Check(new GravityProgress<int>(0, [1, 2, 2], [100], [0, 1], -1).RequiredEncounters == 2,
    "All counts distinct nodes only");
Check(Progress(visits.Concat(visits).ToArray()).EncountersVisited == 15, "Duplicate visits do not add progress");
visits.Add(100);
Check(Progress(visits.ToArray()).Available.SetEquals([101]), "Double boss unlocks sequentially");
visits.Add(101);
Check(Progress(visits.ToArray()).Available.Count == 0, "No more rooms after the final boss");
Check(Progress().EncountersVisited == 0, "A new act starts with no progress");
var serialized = System.Text.Json.JsonSerializer.Serialize(visits);
Check(Progress(System.Text.Json.JsonSerializer.Deserialize<int[]>(serialized)!).Available.Count == 0,
    "Reconstructed progress matches saved visits");

foreach (var bossCount in new[] { 1, 2, 3, 4, 10, 25 })
foreach (var requirement in new[] { 0, 15 })
foreach (var locked in new[] { false, true })
{
    var bosses = Enumerable.Range(100, bossCount).ToArray();
    var path = new List<int> { 0 };
    path.AddRange(encounters.Take(requirement));
    for (var cleared = 0; cleared <= bossCount; cleared++)
    {
        var saved = System.Text.Json.JsonSerializer.Serialize(path);
        var state = new GravityProgress<int>(0, encounters, bosses,
            System.Text.Json.JsonSerializer.Deserialize<int[]>(saved)!, requirement, locked);
        Check(state.Available.Intersect(bosses).SequenceEqual(bosses.Skip(cleared).Take(1)),
            "Every boss must unlock in order, including after save reconstruction");
        Check(state.EncountersVisited == requirement, "Bosses must not increase encounter progress");
        if (cleared > 0) Check(!state.Available.Intersect(encounters).Any(), "Boss chain must keep encounters closed");
        if (cleared < bossCount) path.Add(bosses[cleared]);
    }
}

for (var seed = 0; seed < 8; seed++)
{
    var random = new Random(seed);
    var bodies = new List<FallingLayout.Body> { new(new Vector2(0, 800), 105, true) };
    for (var row = 1; row <= 15; row++)
        for (var column = 0; column < 4; column++)
            bodies.Add(new(new Vector2(-450 + column * 265 + random.Next(-20, 21), 800 - row * 155), 44));
    var bossCount = new[] { 1, 2, 3, 4, 5, 10, 16, 25 }[seed];
    for (var boss = 0; boss < bossCount; boss++)
        bodies.Add(new(new Vector2(0, -1900 - boss * 350), 162, Boss: true));
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
    var pileIndices = Enumerable.Range(0, bodies.Count).Where(i => !bodies[i].Boss).ToArray();
    var bossIndices = Enumerable.Range(0, bodies.Count).Where(i => bodies[i].Boss).ToArray();
    var pileTop = pileIndices.Min(i => final[i].Y - bodies[i].Radius);
    Check(pileTop > -150, "Map did not compact enough");
    foreach (var boss in bossIndices)
    {
        Check(final[boss].Y + bodies[boss].Radius <= pileTop - 63.9f, "Boss must land above the entire pile");
        Check(frames.Skip((int)(FallingLayout.Steps * 0.8f)).All(frame => frame[boss] == final[boss]),
            "Boss must stay fixed after landing");
        Check(frames.All(frame => frame[boss].Y <= final[boss].Y + 0.01f), "Boss must not overshoot into the pile");
    }
    if (bossIndices.Length == 2)
        Check(final[bossIndices[0]].X < final[bossIndices[1]].X
            && final[bossIndices[0]].Y == final[bossIndices[1]].Y, "Double bosses must land in order on one row");
    var withoutBosses = FallingLayout.Simulate(pileIndices.Select(i => bodies[i]).ToArray(), -570, 510, 929);
    Check(frames.Select((frame, step) => pileIndices.Select(i => frame[i]).SequenceEqual(withoutBosses[step])).All(equal => equal),
        "Bosses must never affect encounter physics");
    Check(final.Zip(frames[^2]).Max(pair => Vector2.Distance(pair.First, pair.Second)) < 1,
        "Pile was still moving when frozen");
    if (seed == 0)
        Check(FallingLayout.Simulate(bodies, -570, 510, 929)[^1].SequenceEqual(final), "Reload must produce exactly the same pile");
}
var playback = new FallingPlayback(false);
for (var i = 0; i < 600; i++) playback.Process(1.0 / 60, false);
Check(playback.Frame == 0 && !playback.Completed, "Selection pages must not consume the first fall");
playback.Close(false);
Check(!playback.Completed, "Closing a covered map must preserve its unseen fall");
for (var attempt = 0; attempt < 10; attempt++)
{
    playback.Process(1.0 / 60, true);
    playback.Process(1.0 / 60, true);
    playback.Close(true);
}
Check(!playback.Started && !playback.Completed,
    "Brief map opens between selection pages must not consume the fall");
for (var i = 0; i < 60; i++) playback.Process(1.0 / 60, true);
Check(playback.Started && !playback.Completed && playback.Frame > 0,
    "An unobstructed map must begin falling");
var pausedFrame = playback.Frame;
for (var i = 0; i < 600; i++) playback.Process(1.0 / 60, false);
Check(playback.Frame == pausedFrame && !playback.Completed, "Covered animation must pause without catching up");
playback.Close(false);
Check(playback.Frame == pausedFrame && !playback.Completed, "Closing while covered must preserve remaining playback");
for (var i = 0; i < 600; i++) playback.Process(1.0 / 60, true);
Check(playback.Completed && playback.Frame == FallingLayout.Steps,
    "Returning to an unobstructed map must finish the remaining fall");
var skipped = new FallingPlayback(false);
for (var i = 0; i < 60; i++) skipped.Process(1.0 / 60, true);
skipped.Close(true);
Check(skipped.Completed && skipped.Frame == FallingLayout.Steps,
    "Closing during visible playback must retain the existing skip behavior");
var seen = new FallingPlayback(true);
seen.Process(1.0 / 60, false);
seen.Close(false);
Check(seen.Completed && seen.Frame == FallingLayout.Steps, "Previously viewed maps must remain settled");
foreach (var encountersVisited in new[] { 1, 15, 60 })
{
    var resumed = new FallingPlayback(false, encountersVisited);
    Check(resumed.Completed && resumed.Frame == FallingLayout.Steps,
        "A resumed act must immediately settle without a local viewing record");
    resumed.Process(1.0 / 60, false);
    resumed.Close(false);
    Check(resumed.Completed && resumed.Frame == FallingLayout.Steps,
        "Reconnect overlays must not leave an established act waiting for animation");
}
Console.WriteLine($"Passed {checks} Gravity progression, physics, and playback checks.");
