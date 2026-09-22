using System.Runtime.CompilerServices;
using System.Reflection;
using System.Runtime.Loader;
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
        AccessTools.Property(typeof(RunState), nameof(RunState.Modifiers)).SetValue(run, Array.Empty<MegaCrit.Sts2.Core.Models.ModifierModel>());
        GravityRunSettings.Initialize(run);
        var first = MapTravel.GetTravelablePointsFrom(run, map.StartingMapPoint).ToArray();
        Check(first.SequenceEqual([map.StartingMapPoint]), "Patched API must require the Ancient");
        run.AddVisitedMapCoord(map.StartingMapPoint.coord);
        history[0].Add(new MapPointHistoryEntry());
        var choices = MapTravel.GetTravelablePointsFrom(run, map.StartingMapPoint).ToArray();
        Check(choices.Length == 60, "Patched API must expose every encounter");
        Check(GravityTopBarProgress.Text(run) == "0/15", "A new act must start with an empty goal");
        var order = choices.OrderByDescending(point => point.coord.row).Take(15).ToArray();
        for (var i = 0; i < order.Length; i++)
        {
            var point = order[i];
            Check(MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).Contains(point), "Backward travel failed");
            run.AddVisitedMapCoord(point.coord);
            var entry = new MapPointHistoryEntry();
            history[0].Add(entry);
            Check(ReferenceEquals(run.GetHistoryEntryFor(new MapLocation(point.coord, 0)), entry), "History used row instead of visit order");
            Check(GravityTopBarProgress.Text(run) == $"{i + 1}/15", "Counter must advance up to the goal");
        }
        var unlocked = MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).ToArray();
        Check(unlocked.Length == 46 && unlocked.Contains(map.BossMapPoint), "Boss unlock must preserve the remaining encounters");
        GravityRunSettings.Set(run, 16, true);
        Check(MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).Count() == 45,
            "Lock must allow encounters before the requirement is met");
        GravityRunSettings.Set(run, 15, true);
        Check(MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).SequenceEqual([map.BossMapPoint]),
            "Lock must remove normal encounters from the patched travel API when the boss unlocks");
        GravityRunSettings.Set(run, 1, true);
        Check(MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).SequenceEqual([map.BossMapPoint]),
            "Lock must also hold above the requirement");
        foreach (var requirement in new[] { 1, 15, 60, 1000, -1 })
        {
            GravityRunSettings.Set(run, requirement);
            GravitySettings.RestoreEncounterPreferences(0, true);
            var capped = requirement == -1 ? 60 : Math.Min(requirement, 60);
            Check(GravityRules.Progress(run).RequiredEncounters == capped, "Rules must cap the snapshot to the act pool");
            Check(GravityTopBarProgress.Text(run) == $"{Math.Min(15, capped)}/{capped}",
                "Counter must cap displayed progress at the snapshotted goal, not edited defaults");
            Check(MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).Contains(map.BossMapPoint) == (15 >= capped),
                "Travel must use the same snapshot as the counter");
        }
        GravityRunSettings.Set(run, 0, true);
        Check(MapTravel.GetTravelablePointsFrom(run, run.CurrentMapPoint!).Count() == 46,
            "Always-unlocked bosses must ignore the encounter lock");
        Check(GravityRules.Progress(run).RequiredEncounters == 0, "Disabled requirement did not reach travel rules");
        Check(GravityTopBarProgress.Text(run) == "15", "Off must hide the denominator");
        GravityRunSettings.Set(run, 15);
        foreach (var point in choices.Except(order).Take(2))
        {
            run.AddVisitedMapCoord(point.coord);
            Check(GravityTopBarProgress.Text(run) == "15/15", "Extra encounters must keep the completed goal display");
        }
        Check(GravityRules.Progress(run).EncountersVisited == 17, "Display capping must preserve the actual encounter count");
        GravityRunSettings.Set(run, 0);
        Check(GravityTopBarProgress.Text(run) == "17", "An unrestricted counter must continue counting past 15");
        GravitySettings.RestoreEncounterPreferences(15, true);
        run.AddVisitedMapCoord(map.BossMapPoint.coord);
        Check(MapTravel.GetTravelablePointsFrom(run, map.BossMapPoint).SequenceEqual([map.SecondBossMapPoint!]), "Second boss order failed");
        run.AddVisitedMapCoord(map.SecondBossMapPoint!.coord);
        Check(MapTravel.GetTravelablePointsFrom(run, map.SecondBossMapPoint).SequenceEqual([map.ThirdBoss]),
            "A third boss outside the grid must remain reachable through the patched travel API");
        run.AddVisitedMapCoord(map.ThirdBoss.coord);
        Check(!MapTravel.GetTravelablePointsFrom(run, map.ThirdBoss).Any(), "The final boss must end the chain");
        Check(GravityRules.Progress(run).EncountersVisited == 17, "Extra bosses must not count as encounters");
        var fourth = new MapPoint(2, 19) { PointType = MapPointType.Boss };
        GravityRules.RegisterBosses(map, [fourth, map.ThirdBoss, map.BossMapPoint]);
        Check(GravityRules.Bosses(map).Select(point => point.coord).SequenceEqual(
            new[] { map.BossMapPoint, map.SecondBossMapPoint, map.ThirdBoss, fourth }.Select(point => point!.coord)),
            "Screen-added bosses must be merged in map order without duplicates");
        Check(GravityRules.Progress(run).Available.SetEquals([fourth.coord]),
            "A boss registered by another mod's map UI must unlock after the linked bosses");
        GravityRules.RegisterBosses(map, []);
        Check(!GravityRules.Progress(run).Available.Any(), "Rebuilding the map must discard stale screen registrations");
        if (Environment.GetEnvironmentVariable("GRAVITY_BETTEREXPERIENCE_DLL") is { Length: > 0 } dll)
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll));
            var type = assembly.GetType("BetterExperience.AscensionReworkThirdBossActMap", throwOnError: true)!;
            var inner = new TestMap();
            inner.SecondBossMapPoint!.RemoveChildPoint(inner.ThirdBoss);
            run.Map = (ActMap)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                null, [inner], null)!;
            var bosses = GravityRules.Bosses(run.Map);
            Check(bosses.Count == 3 && ReferenceEquals(bosses[2], run.Map.GetPoint(inner.ThirdBoss.coord)),
                "BetterExperience's actual third-boss map must expose all three bosses before the UI opens");
            AccessTools.Field(typeof(RunState), "_visitedMapCoords").SetValue(run,
                new List<MapCoord>(run.VisitedMapCoords.Where(coord => coord != inner.ThirdBoss.coord)));
            Check(MapTravel.GetTravelablePointsFrom(run, inner.SecondBossMapPoint).SequenceEqual([bosses[2]]),
                "BetterExperience's actual third boss must be reachable after its second boss");
            Console.WriteLine("Passed BetterExperience third-boss map discovery and travel checks.");
        }
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
        public MapPoint ThirdBoss { get; } = new(2, 18) { PointType = MapPointType.Boss };
        public override MapPoint? GetPoint(MapCoord coord) => coord == ThirdBoss.coord ? ThirdBoss : base.GetPoint(coord);
        public TestMap()
        {
            BossMapPoint.AddChildPoint(SecondBossMapPoint!);
            SecondBossMapPoint!.AddChildPoint(ThirdBoss);
            for (var col = 0; col < 4; col++)
                for (var row = 1; row < 16; row++)
                    Grid[col, row] = new MapPoint(col, row) { PointType = MapPointType.Monster };
        }
    }
}
