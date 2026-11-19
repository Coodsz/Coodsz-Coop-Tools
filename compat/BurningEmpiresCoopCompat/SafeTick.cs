using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BurningEmpiresCoopCompat
{
    // On the co-op server, one Burning Empires tick handler that throws must not jam the
    // campaign. The game runs a timed event's handlers before it moves the event's next time
    // forward, so a throwing daily handler makes the whole daily event fire again and again,
    // and later ticks (weekly, other mods) starve. This logs the error once per handler and
    // swallows it. Single player keeps the normal behaviour.
    internal static class SafeTick
    {
        private static readonly string[] TickHandlerNames =
        {
            "OnTick", "OnCampaignTick", "UpdateBE_Daily",
            "DailyTick", "OnDailyTick", "WeeklyTick", "OnWeeklyTick",
            "HourlyTick", "OnHourlyTick", "OnDailyTickSettlement", "OnHourlyTickParty",
        };

        private static readonly HashSet<string> Reported = new HashSet<string>();

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var finalizer = new HarmonyMethod(typeof(SafeTick), nameof(Finalizer));
            var count = 0;
            foreach (var type in SafeTypes(burningEmpires))
            {
                if (type.IsAbstract || !typeof(CampaignBehaviorBase).IsAssignableFrom(type))
                    continue;
                foreach (var name in TickHandlerNames)
                {
                    var method = AccessTools.DeclaredMethod(type, name);
                    if (method == null || method.IsAbstract || method.ContainsGenericParameters)
                        continue;
                    try
                    {
                        harmony.Patch(method, finalizer: finalizer);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        SubModule.Log("Could not guard " + type.Name + "." + name + ": " + ex.Message);
                    }
                }
            }
            return count;
        }

        private static Exception Finalizer(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception == null || !Coop.OnServer)
                return __exception;

            var key = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name;
            lock (Reported)
            {
                if (Reported.Add(key))
                    SubModule.Log("Burning Empires " + key + " failed on the server and was skipped: " + __exception);
            }
            return null;
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
        }
    }
}
