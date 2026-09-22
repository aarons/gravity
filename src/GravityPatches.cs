using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace Gravity;

[HarmonyPatch(typeof(MapTravel), nameof(MapTravel.GetTravelablePointsFrom))]
internal static class TravelChoicesPatch
{
    private static bool Prefix(IRunState runState, ref IEnumerable<MapPoint> __result)
    {
        if (!GravityRules.Applies(runState)) return true;
        __result = GravityRules.Progress((RunState)runState).Available.Select(coord => runState.Map.GetPoint(coord)!);
        return false;
    }
}

[HarmonyPatch(typeof(NMapScreen), "RecalculateTravelability")]
internal static class TravelabilityPatch
{
    private static bool Prefix(NMapScreen __instance, RunState ____runState,
        Dictionary<MapCoord, NMapPoint> ____mapPointDictionary)
    {
        if (!GravityRules.Applies(____runState)) return true;
        var progress = GravityRules.Progress(____runState);
        foreach (var (coord, node) in ____mapPointDictionary)
            node.State = progress.Visited.Contains(coord) ? MapPointState.Traveled
                : progress.Available.Contains(coord) ? MapPointState.Travelable : MapPointState.Untravelable;
        GravityMapView.Get(__instance)?.UpdateStatus();
        GravityMapView.Get(__instance)?.UpdateNavigation();
        return false;
    }
}

[HarmonyPatch(typeof(NMapScreen), "DrawPaths")]
internal static class RemovePathsPatch
{
    private static bool Prefix(RunState ____runState) => !GravityRules.Applies(____runState);
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
internal static class SetMapPatch
{
    // Let other mods add their boss nodes before taking the layout snapshot.
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(NMapScreen __instance, RunState ____runState, ulong seed,
        Dictionary<MapCoord, NMapPoint> ____mapPointDictionary) =>
        GravityMapView.Attach(__instance, ____runState, seed, ____mapPointDictionary);
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
internal static class OpenMapPatch
{
    private static void Prefix(NMapScreen __instance, ref bool ____hasPlayedAnimation, out bool __state)
    {
        __state = !__instance.IsOpen && GravityMapView.Get(__instance) != null;
        if (__state) ____hasPlayedAnimation = true;
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(NMapScreen __instance, bool __state)
    {
        if (__state) GravityMapView.Get(__instance)!.Open();
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen._Process))]
internal static class AnimateMapPatch
{
    private static void Postfix(NMapScreen __instance, double delta) => GravityMapView.Get(__instance)?.Process(delta);
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Close))]
internal static class CloseMapPatch
{
    private static void Prefix(NMapScreen __instance) => GravityMapView.Get(__instance)?.Finish();
}

[HarmonyPatch(typeof(NMapScreen), "UpdateScrollPosition")]
internal static class ScrollBoundsPatch
{
    private static void Prefix(NMapScreen __instance, ref Vector2 ____targetDragPos)
    {
        if (GravityMapView.Get(__instance) is { } view)
            ____targetDragPos.Y = Math.Clamp(____targetDragPos.Y, view.MinScroll, view.MaxScroll);
    }

    private static void Postfix(NMapScreen __instance, ref Vector2 ____targetDragPos)
    {
        if (GravityMapView.Get(__instance) is { } view)
            ____targetDragPos.Y = Math.Clamp(____targetDragPos.Y, view.MinScroll, view.MaxScroll);
    }
}

[HarmonyPatch(typeof(NMapScreen), "ProcessControllerEvent")]
internal static class ControllerScrollPatch
{
    // Godot handles directional focus using the spatial neighbors configured by Gravity.
    // The base handler scrolls to the original row, which is unrelated to the pile.
    private static bool Prefix(NMapScreen __instance, InputEvent inputEvent)
    {
        if (GravityMapView.Get(__instance) is not { } view) return true;
        if (inputEvent.IsActionPressed(MegaInput.up) || inputEvent.IsActionPressed(MegaInput.down)
            || inputEvent.IsActionPressed(MegaInput.left) || inputEvent.IsActionPressed(MegaInput.right)
            || inputEvent.IsActionPressed(MegaInput.select))
            Callable.From(view.ScrollToFocusedPoint).CallDeferred();
        return false;
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.OnMapPointSelectedLocally))]
internal static class LocalSelectionPatch
{
    private static bool Prefix(NMapScreen __instance, NMapPoint point, RunState ____runState) =>
        !GravityRules.Applies(____runState) || (GravityMapView.Get(__instance)?.Falling != true
            && !__instance.IsTraveling && __instance.IsTravelEnabled
            && GravityRules.Progress(____runState).Available.Contains(point.Point.coord));
}

[HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
internal static class ValidateVotePatch
{
    private static bool Prefix(RunState ____runState, MapVote? destination) =>
        !destination.HasValue || !GravityRules.Applies(____runState)
        || GravityRules.Progress(____runState).Available.Contains(destination.Value.coord);
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapCoord))]
internal static class ValidateTravelPatch
{
    private static bool Prefix(RunManager __instance, MapCoord coord, ref Task __result)
    {
        var run = __instance.DebugOnlyGetState();
        if (!GravityRules.Applies(run) || GravityRules.Progress(run!).Available.Contains(coord)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), "EnterMapCoordInternal")]
internal static class VisitFloorPatch
{
    private static bool Prefix(RunManager __instance, MapCoord coord,
        AbstractRoom? preFinishedRoom, bool saveGame, ref Task __result)
    {
        var run = __instance.DebugOnlyGetState();
        if (!GravityRules.Applies(run)) return true;
        // Both ordinary entry and save reload pass here, after the coord is recorded.
        __result = __instance.EnterMapPointInternal(GravityRules.VisitIndex(run!, coord) + 1,
            run!.Map.GetPoint(coord)!.PointType, preFinishedRoom, saveGame);
        return false;
    }
}

[HarmonyPatch(typeof(RunState), nameof(RunState.GetHistoryEntryFor))]
internal static class VisitHistoryPatch
{
    private static bool Prefix(RunState __instance, MapLocation location, ref MapPointHistoryEntry? __result)
    {
        if (!GravityRules.Applies(__instance) || location.actIndex != __instance.CurrentActIndex) return true;
        var index = location.coord.HasValue ? GravityRules.VisitIndex(__instance, location.coord.Value) : -1;
        __result = index >= 0 && location.actIndex < __instance.MapPointHistory.Count
            && index < __instance.MapPointHistory[location.actIndex].Count
                ? __instance.MapPointHistory[location.actIndex][index] : null;
        return false;
    }
}

[HarmonyPatch(typeof(NMapPointHistoryHoverTip), nameof(NMapPointHistoryHoverTip.Create))]
internal static class HistoryFloorTitlePatch
{
    private static void Prefix(MapPointHistoryEntry historyEntry, ref int floorNum)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (!GravityRules.Applies(run)) return;
        var floor = 0;
        foreach (var act in run!.MapPointHistory)
            foreach (var entry in act)
            {
                floor++;
                if (!ReferenceEquals(entry, historyEntry)) continue;
                floorNum = floor;
                return;
            }
    }
}

[HarmonyPatch(typeof(NNormalMapPoint), "UpdateIcon")]
internal static class VisitedUnknownIconPatch
{
    private static bool Prefix(NNormalMapPoint __instance, IRunState ____runState,
        TextureRect ____icon, TextureRect ____outline)
    {
        if (!GravityRules.Applies(____runState) || __instance.Point.PointType != MapPointType.Unknown
            || __instance.State != MapPointState.Traveled) return true;
        var history = ____runState.GetHistoryEntryFor(new MapLocation(__instance.Point.coord, ____runState.CurrentActIndex));
        if (history?.Rooms.FirstOrDefault() is { } room)
        {
            var type = typeof(NNormalMapPoint);
            var icon = (string)AccessTools.Method(type, "UnknownIconPath").Invoke(null, [room.RoomType])!;
            var outline = (string)AccessTools.Method(type, "UnknownOutlinePath").Invoke(null, [room.RoomType])!;
            ____icon.Texture = ResourceLoader.Load<Texture2D>(icon);
            ____outline.Texture = ResourceLoader.Load<Texture2D>(outline);
        }
        return false;
    }
}
