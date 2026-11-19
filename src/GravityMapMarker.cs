using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Gravity;

internal static class GravityMapMarker
{
    // v0.113 widened the parameter from NMapPoint to Control. Bind at runtime so
    // one DLL can also run on the older stable branch.
    private static readonly Action<NMapMarker, NMapPoint> SetPoint =
        (AccessTools.Method(typeof(NMapMarker), nameof(NMapMarker.SetMapPoint), [typeof(Control)])
            ?? AccessTools.Method(typeof(NMapMarker), nameof(NMapMarker.SetMapPoint), [typeof(NMapPoint)]))
        .CreateDelegate<Action<NMapMarker, NMapPoint>>();

    public static void SetMapPoint(NMapMarker marker, NMapPoint point) => SetPoint(marker, point);
}
