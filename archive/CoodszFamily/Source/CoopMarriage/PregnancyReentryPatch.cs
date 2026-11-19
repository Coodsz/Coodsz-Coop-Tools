using System.Collections;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace CoopMarriage;

// Make the common native action idempotent. Do not intercept IsPregnant's setter:
// Coop must still replicate true/false, and delivery must be able to clear it.
[HarmonyPatch(typeof(MakePregnantAction), nameof(MakePregnantAction.Apply))]
internal static class PregnancyReentryPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Hero mother) => mother == null || !mother.IsPregnant;
}

// Also protect the record writer when another mod publishes the conception event
// directly. Preserve the first father/due date; an existing record is not a new
// pregnancy. This intentionally permits DCF's first, preinserted exact-father record.
[HarmonyPatch(typeof(PregnancyCampaignBehavior), "ChildConceived")]
internal static class PregnancyRecordReentryPatch
{
    private static readonly FieldInfo Records = AccessTools.Field(typeof(PregnancyCampaignBehavior), "_heroPregnancies");
    private static readonly FieldInfo Mother = AccessTools.Field(typeof(PregnancyCampaignBehavior).GetNestedType("Pregnancy", BindingFlags.Public | BindingFlags.NonPublic), "Mother");

    [HarmonyPrefix]
    private static bool Prefix(PregnancyCampaignBehavior __instance, Hero __0)
    {
        if (__0 == null) return true;
        if (Records?.GetValue(__instance) is not IList records || Mother == null)
            throw new System.NotSupportedException("COOP Family: native pregnancy record API differs; duplicate conception cannot be checked.");
        foreach (object record in records)
            if (ReferenceEquals(Mother.GetValue(record), __0)) return false;
        return true;
    }
}
