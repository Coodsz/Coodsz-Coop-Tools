using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace CoopMarriage;

// Bannerlord returns Hero._defaultAge while birth/death is disabled. Native
// newborns have a default age of 0.001, so use their saved birthday for age.
[HarmonyPatch(typeof(Hero), "get_Age")]
internal static class ChildAgeProgressionPatch
{
    private const float NativeNewbornDefaultAgeMaximum = 0.01f;

    private static readonly AccessTools.FieldRef<Hero, float> DefaultAge =
        AccessTools.FieldRefAccess<Hero, float>("_defaultAge");

    private static readonly AccessTools.FieldRef<Hero, CampaignTime> BirthDay =
        AccessTools.FieldRefAccess<Hero, CampaignTime>("_birthDay");

    [HarmonyPostfix]
    private static void Postfix(Hero __instance, ref float __result)
    {
        if (__instance == null || !CampaignOptions.IsLifeDeathCycleDisabled || !__instance.IsAlive ||
            (__instance.Mother == null && __instance.Father == null) ||
            DefaultAge(__instance) > NativeNewbornDefaultAgeMaximum)
        {
            return;
        }

        float naturalAge = BirthDay(__instance).ElapsedYearsUntilNow;
        if (!float.IsNaN(naturalAge) && !float.IsInfinity(naturalAge) && naturalAge >= 0f)
        {
            __result = naturalAge;
        }
    }
}
