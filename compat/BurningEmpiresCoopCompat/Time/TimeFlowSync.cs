using System;
using System.Reflection;
using HarmonyLib;

namespace BurningEmpiresCoopCompat
{
    // Burning Empires speeds up campaign time on every machine by its own setting (default 3x)
    // and lets a player hold Ctrl+Space for even more. In co-op the server owns the clock, so a
    // different speed on any PC fights Coop's time correction. In a session this:
    // - makes TimeFlowSpeed read 1 and turns the Ctrl+Space shortcut off, so menus and any other
    //   reader see the real value;
    // - skips Burning Empires' own Campaign.TickMapTime prefix, so the game's time step is left
    //   as is even if the JIT inlined the small settings getters into it;
    // - skips Burning Empires' map input prefix, so Ctrl+Space never swallows the Space key and
    //   the normal pause toggle keeps working.
    // The daylight lock only changes which hour the map is drawn at; it never changes the time
    // step, so it needs no change here. Single player keeps the normal behaviour.
    internal static class TimeFlowSync
    {
        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;

            var settings = burningEmpires.GetType("BurningEmpires.ModConfigurationMenu.BurningEmpiresGameplaySettings", false);
            if (settings == null)
            {
                SubModule.Log("Time flow sync: BurningEmpiresGameplaySettings not found.");
            }
            else
            {
                count += Patch(harmony, AccessTools.PropertyGetter(settings, "TimeFlowSpeed"),
                    "BurningEmpiresGameplaySettings.TimeFlowSpeed", postfix: nameof(TimeFlowSpeedPostfix));
                count += Patch(harmony, AccessTools.PropertyGetter(settings, "EnableCtrlSpaceTimeAcceleration"),
                    "BurningEmpiresGameplaySettings.EnableCtrlSpaceTimeAcceleration", postfix: nameof(CtrlSpacePostfix));
            }

            var timeFlow = burningEmpires.GetType("BurningEmpires.BESystems.Time.BETimeFlowPatches", false);
            count += Patch(harmony,
                timeFlow == null ? null : AccessTools.Method(timeFlow, "ApplyCampaignTimeMultiplier", new[] { typeof(float).MakeByRefType() }),
                "BETimeFlowPatches.ApplyCampaignTimeMultiplier", prefix: nameof(SkipInSession));

            var input = burningEmpires.GetType("BurningEmpires.BESystems.Time.BEMapTimeAccelerationInputPatch", false);
            var mapScreen = AccessTools.TypeByName("SandBox.View.Map.MapScreen");
            count += Patch(harmony,
                input == null || mapScreen == null ? null : AccessTools.Method(input, "BeginMapInput", new[] { mapScreen }),
                "BEMapTimeAccelerationInputPatch.BeginMapInput", prefix: nameof(SkipInSession));

            return count;
        }

        private static int Patch(Harmony harmony, MethodInfo method, string name, string prefix = null, string postfix = null)
        {
            if (method == null)
            {
                SubModule.Log("Time flow sync: " + name + " not found.");
                return 0;
            }
            try
            {
                harmony.Patch(method,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(TimeFlowSync), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(TimeFlowSync), postfix));
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Time flow sync: could not patch " + name + ": " + ex.Message);
                return 0;
            }
        }

        private static void TimeFlowSpeedPostfix(ref float __result)
        {
            if (Coop.InSession)
                __result = 1f;
        }

        private static void CtrlSpacePostfix(ref bool __result)
        {
            if (Coop.InSession)
                __result = false;
        }

        // Returning false skips Burning Empires' own prefix; outside a session it runs as normal.
        private static bool SkipInSession() => !Coop.InSession;
    }
}
