using System.Runtime.CompilerServices;
using Gravity;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

internal static class GameRulesTests
{
    public static void Run()
    {
        // These two collections are the only state read by the patched rules. Avoid
        // initializing content databases or a native Godot scene in this smoke test.
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(RunState), "_visitedMapCoords").SetValue(run, new List<MapCoord>());
        var history = new List<List<MapPointHistoryEntry>> { new() };
        AccessTools.Field(typeof(RunState), "_mapPointHistory").SetValue(run, history);
        var map = new TestMap();
        run.Map = map;
        AccessTools.Property(typeof(RunState), nameof(RunState.ExtraFields)).SetValue(run, new ExtraRunFields());
        var first = MapTravel.GetTravelablePointsFrom(run, map.StartingMapPoint).ToArray();
        Check(first.SequenceEqual([map.StartingMapPoint]), "Patched API must require the Ancient");
        run.AddVisitedMapCoord(map.StartingMapPoint.coord);
        history[0].Add(new MapPointHistoryEntry());
        var choices = MapTravel.GetTravelablePointsFrom(run, map.StartingMapPoint).ToArray();
        Check(choices.Length == 60, "Patched API must expose every encounter");
        var order = choices.OrderByDescending(point => point.coord.row).Take(15).ToArray();
        for (var i = 0; i < order.Length; i++)
        {
            var point = order[i];
            Check(MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).Contains(point), "Backward travel failed");
            run.AddVisitedMapCoord(point.coord);
            var entry = new MapPointHistoryEntry();
            history[0].Add(entry);
            Check(ReferenceEquals(run.GetHistoryEntryFor(new MapLocation(point.coord, 0)), entry), "History used row instead of visit order");
        }
        var unlocked = MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).ToArray();
        Check(unlocked.Length == 46 && unlocked.Contains(map.BossMapPoint), "Boss unlock must preserve the remaining encounters");
        foreach (var requirement in new[] { 1, 15, 60, 1000, -1 })
        {
            GravityRunSettings.Set(run.ExtraFields, requirement);
            GravitySettings.RestoreEncounterPreferences(0, true);
            var capped = requirement == -1 ? 60 : Math.Min(requirement, 60);
            Check(GravityRules.Progress(run).RequiredEncounters == capped, "Rules must cap the snapshot to the act pool");
            Check(GravityTopBarProgress.Text(run) == $"15/{capped}", "Counter must show the snapshot, not edited defaults");
            Check(MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).Contains(map.BossMapPoint) == (15 >= capped),
                "Travel must use the same snapshot as the counter");
        }
        GravityRunSettings.Set(run.ExtraFields, 0);
        Check(GravityRules.Progress(run).RequiredEncounters == 0, "Disabled requirement did not reach travel rules");
        Check(GravityTopBarProgress.Text(run) == "15", "Off must hide the denominator");
        GravitySettings.RestoreEncounterPreferences(15, true);
        run.AddVisitedMapCoord(map.BossMapPoint.coord);
        Check(MapTravel.GetTravelablePointsFrom(run, map.BossMapPoint).SequenceEqual([map.SecondBossMapPoint!]), "Second boss order failed");
        Console.WriteLine("Passed real-game travel and room-history checks using the installed patches.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private sealed class TestMap : ActMap
    {
        protected override MapPoint?[,] Grid { get; } = new MapPoint?[4, 16];
        public override MapPoint StartingMapPoint { get; } = new(2, 0) { PointType = MapPointType.Ancient };
        public override MapPoint BossMapPoint { get; } = new(2, 16) { PointType = MapPointType.Boss };
        public override MapPoint? SecondBossMapPoint { get; } = new(2, 17) { PointType = MapPointType.Boss };
        public TestMap()
        {
            for (var col = 0; col < 4; col++)
                for (var row = 1; row < 16; row++)
                    Grid[col, row] = new MapPoint(col, row) { PointType = MapPointType.Monster };
        }
    }
}
