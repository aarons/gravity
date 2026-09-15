using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace Gravity;

internal static class GravityPulse
{
    public static float Scale(float phase) => 1f + (Mathf.Sin(phase) * 0.25f + 0.2f) * GravitySettings.PulsePercent / 100f;
}

[HarmonyPatch(typeof(NNormalMapPoint), nameof(NNormalMapPoint._Process))]
internal static class EncounterPulsePatch
{
    private static void Prefix(float ____elapsedTime, out float __state) => __state = ____elapsedTime;

    private static void Postfix(NNormalMapPoint __instance, IRunState ____runState,
        Control ____iconContainer, float ____elapsedTime, float __state)
    {
        if (GravitySettings.PulsePercent == 100 || !__instance.IsEnabled || !GravityRules.Applies(____runState)) return;
        // Only replace the idle pulse; native hover/press feedback uses the inner icon.
        if (GravitySettings.PulsePercent == 0 || ____elapsedTime != __state)
            ____iconContainer.Scale = Vector2.One * GravityPulse.Scale(____elapsedTime);
    }
}
