using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;

namespace CoopMarriage;

// Retain the selected model's probability (x1.00), with only range validation.
// A mod's own model remains authoritative; there is no fertility bonus.
[HarmonyPatch(typeof(PregnancyCampaignBehavior), "RefreshSpouseVisit")]
internal static class PregnancyChancePatch
{
    internal static float NormalizeChance(float chance)
        => float.IsNaN(chance) || chance <= 0f ? 0f : Math.Min(1f, chance);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var probability = AccessTools.Method(typeof(PregnancyModel), nameof(PregnancyModel.GetDailyChanceOfPregnancyForHero), new[] { typeof(Hero) });
        var scale = AccessTools.Method(typeof(PregnancyChancePatch), nameof(NormalizeChance));
        int matches = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.Calls(probability))
            {
                matches++;
                yield return new CodeInstruction(OpCodes.Call, scale);
            }
        }
        if (matches != 1)
            throw new NotSupportedException("COOP Family: expected one native pregnancy chance call; found " + matches);
    }
}
