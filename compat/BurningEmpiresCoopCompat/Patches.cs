using System;
using System.Reflection;
using HarmonyLib;

namespace BurningEmpiresCoopCompat
{
    // Registers every Burning Empires hook. Each area of the patch lives in its own file and
    // adds its hooks here. One area that fails to patch is logged and the others still run.
    internal static class Patches
    {
        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;
            count += Try("WorldDataTransfer", () => WorldDataTransfer.Apply(harmony, burningEmpires));
            count += Try("ClientBehaviorGates", () => ClientBehaviorGates.Apply(harmony, burningEmpires));
            count += Try("DsHeadlessGuards", () => DsHeadlessGuards.Apply(harmony, burningEmpires));
            count += Try("PlayerExemptions", () => PlayerExemptions.Apply(harmony, burningEmpires));
            count += Try("TimeFlowSync", () => TimeFlowSync.Apply(harmony, burningEmpires));
            count += Try("DsPopups", () => DsPopups.Apply(harmony, burningEmpires));
            count += Try("DsSaveSafety", () => DsSaveSafety.Apply(harmony, burningEmpires));
            count += Try("CoopGreyOuts", () => CoopGreyOuts.Apply(harmony, burningEmpires));
            count += Try("CoopDisabledFeatures", () => CoopDisabledFeatures.Apply(harmony, burningEmpires));
            count += Try("BeDecisionSync", () => BeDecisionSync.Apply(harmony, burningEmpires));
            count += Try("FoundingReligion", () => FoundingReligion.Apply(harmony, burningEmpires));
            count += Try("VolunteerPublish", () => VolunteerPublish.Apply(harmony, burningEmpires));
            count += Try("RecruitDeduction", () => RecruitDeduction.Apply(harmony, burningEmpires));
            count += Try("SafeTick", () => SafeTick.Apply(harmony, burningEmpires));
            count += Try("NoTournamentElephants", () => NoTournamentElephants.Apply(harmony, burningEmpires));
            return count;
        }

        private static int Try(string area, Func<int> apply)
        {
            try
            {
                return apply();
            }
            catch (Exception ex)
            {
                SubModule.Log(area + " could not be patched: " + ex);
                return 0;
            }
        }
    }
}
