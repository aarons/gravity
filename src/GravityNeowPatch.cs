using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace Gravity;

// Neow replaces her normal choices when a run has modifiers. Our settings are
// saved as a modifier, but must not select that branch or its description.
[HarmonyPatch]
internal static class GravityNeowPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Neow), "GenerateInitialOptions");
        yield return AccessTools.PropertyGetter(typeof(Neow), nameof(Neow.InitialDescription));
    }

    private static int GameplayCount(IReadOnlyCollection<ModifierModel> modifiers) =>
        modifiers.Count(modifier => modifier is not GravitySettingsModifier);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(IReadOnlyCollection<ModifierModel>), "Count");
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(GravityNeowPatch), nameof(GameplayCount));
            }
            yield return instruction;
        }
    }
}
