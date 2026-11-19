using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;

namespace CoopMarriage;

// Only the native birth decision uses this probability. Keep its random draw,
// twin/stillbirth rules, parents and authoritative Coop creation/sync intact.
[HarmonyPatch(typeof(PregnancyCampaignBehavior), "CheckOffspringToDeliver")]
internal static class BirthSexChancePatch
{
    internal static float FemaleProbability(PregnancyModel model) => 0.5f;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(PregnancyModel), nameof(PregnancyModel.DeliveringFemaleOffspringProbability));
        var balanced = AccessTools.Method(typeof(BirthSexChancePatch), nameof(FemaleProbability));
        int matches = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                // Both methods consume a PregnancyModel and return a float.
                // Preserve labels and exception blocks on the original node.
                instruction.opcode = OpCodes.Call;
                instruction.operand = balanced;
                matches++;
            }
            yield return instruction;
        }
        if (matches != 1)
            throw new NotSupportedException("COOP Family: expected one native birth sex probability call; found " + matches);
    }
}
