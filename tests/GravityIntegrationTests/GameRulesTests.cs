using System.Runtime.CompilerServices;
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
        Check(MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).SequenceEqual([map.BossMapPoint]), "Boss did not unlock at 15");
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
