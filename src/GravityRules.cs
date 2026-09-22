using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace Gravity;

internal static class GravityRules
{
    private static readonly ConditionalWeakTable<ActMap, List<MapPoint>> DisplayedBosses = new();

    public static void RegisterBosses(ActMap map, IEnumerable<MapPoint> points)
    {
        DisplayedBosses.Remove(map);
        DisplayedBosses.Add(map, points.Where(point => point.PointType == MapPointType.Boss).ToList());
    }

    // Tutorial and single-room debug maps do not contain the required encounter pool.
    public static bool Applies(IRunState? run) => run is RunState
        && run.Map.StartingMapPoint.PointType == MapPointType.Ancient
        && Encounters(run.Map).Take(15).Count() == 15;

    public static IEnumerable<MapPoint> Encounters(ActMap map) => map.GetAllMapPoints()
        .Where(point => point.coord != map.StartingMapPoint.coord
            && point.PointType is not (MapPointType.Boss or MapPointType.Unassigned));

    public static IReadOnlyList<MapPoint> Bosses(ActMap map)
    {
        // Extra bosses can live outside the grid, linked from the built-in boss points.
        var pending = new Queue<MapPoint>(map.GetAllMapPoints()
            .Append(map.StartingMapPoint).Append(map.BossMapPoint));
        if (map.SecondBossMapPoint is { } second) pending.Enqueue(second);
        // Some mods add nodes directly to the screen without linking them into the grid.
        if (DisplayedBosses.TryGetValue(map, out var displayed))
            foreach (var point in displayed) pending.Enqueue(point);
        var seen = new HashSet<MapCoord>();
        var bosses = new List<MapPoint>();
        while (pending.TryDequeue(out var point))
        {
            if (!seen.Add(point.coord)) continue;
            if (point.PointType == MapPointType.Boss) bosses.Add(point);
            foreach (var child in point.Children) pending.Enqueue(child);
        }
        return bosses.OrderBy(point => point.coord.row).ThenBy(point => point.coord.col).ToArray();
    }

    public static GravityProgress<MapCoord> Progress(RunState run) => new(
        run.Map.StartingMapPoint.coord,
        Encounters(run.Map).Select(point => point.coord).ToArray(),
        Bosses(run.Map).Select(point => point.coord).ToArray(),
        run.VisitedMapCoords, GravityRunSettings.Get(run.ExtraFields),
        GravityRunSettings.GetLockEncounters(run.ExtraFields));

    public static int VisitIndex(RunState run, MapCoord coord)
    {
        for (var i = 0; i < run.VisitedMapCoords.Count; i++)
            if (run.VisitedMapCoords[i] == coord) return i;
        return -1;
    }
}
