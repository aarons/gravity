using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace Gravity;

internal static class GravityMapReveal
{
    private sealed record ResumedAct(int Index);
    private static readonly ConditionalWeakTable<RunState, ResumedAct> Resumed = new();

    internal static void MarkResumed(RunState run) =>
        Resumed.AddOrUpdate(run, new(run.CurrentActIndex));

    internal static bool CanReveal(RunState run)
    {
        // Loading and rejoining restore a RunState. Later acts can still reveal.
        if (Resumed.TryGetValue(run, out var act) && act.Index == run.CurrentActIndex) return false;
        // A fresh act may wait through the Ancient and an extra selection page.
        // Once ordinary play starts, presentation should simply stay settled.
        return run.VisitedMapCoords.All(coord => coord == run.Map.StartingMapPoint.coord
            && run.Map.StartingMapPoint.PointType == MapPointType.Ancient);
    }
}

[HarmonyPatch(typeof(RunState), nameof(RunState.FromSerializable))]
internal static class ResumedMapRevealPatch
{
    private static void Postfix(RunState __result) => GravityMapReveal.MarkResumed(__result);
}
