using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace Gravity;

internal static class GravityRules
{
    // Tutorial and single-room debug maps do not contain the required encounter pool.
    public static bool Applies(IRunState? run) => run is RunState
        && run.Map.StartingMapPoint.PointType == MapPointType.Ancient
        && Encounters(run.Map).Take(15).Count() == 15;

    public static IEnumerable<MapPoint> Encounters(ActMap map) => map.GetAllMapPoints()
        .Where(point => point.coord != map.StartingMapPoint.coord
            && point.PointType is not (MapPointType.Boss or MapPointType.Unassigned));

    public static GravityProgress<MapCoord> Progress(RunState run) => new(
        run.Map.StartingMapPoint.coord,
        Encounters(run.Map).Select(point => point.coord).ToArray(),
        run.Map.SecondBossMapPoint is { } second
            ? [run.Map.BossMapPoint.coord, second.coord] : [run.Map.BossMapPoint.coord],
        run.VisitedMapCoords, GravityRunSettings.Get(run.ExtraFields));

    public static int VisitIndex(RunState run, MapCoord coord)
    {
        for (var i = 0; i < run.VisitedMapCoords.Count; i++)
            if (run.VisitedMapCoords[i] == coord) return i;
        return -1;
    }
}
