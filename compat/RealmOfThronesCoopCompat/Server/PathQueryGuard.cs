using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Map.DistanceCache;
using TaleWorlds.CampaignSystem.Settlements;

namespace RealmOfThronesCoopCompat
{
    // Settlement to settlement path queries the dedicated server cannot survive, adapted from
    // Burning Empires Coop Compat. A distance the cache does not hold is worked out by
    // SandBoxNavigationCache.GetRealDistanceAndLandRatioBetweenSettlements, which hands both
    // settlements' nav mesh faces to the engine. A face of -1 (a settlement with no place on the
    // nav mesh) ends the server with an access violation instead of an exception. On the server
    // only, such a pair is answered as far apart (PathQueryRules) without asking the engine,
    // logged once for each kind. The first few real queries are logged before and after the
    // engine call, so a live test shows whether the path query itself works.
    internal static class PathQueryGuard
    {
        private const int Breadcrumbs = 5;

        private static readonly HashSet<PathQueryRules.Unsafe> Logged = new HashSet<PathQueryRules.Unsafe>();
        private static int _queries;
        private static int _checkFailedLogged;

        public static void Install()
        {
            var harmony = new Harmony(MapRedirect.HarmonyId);
            var element = typeof(NavigationCacheElement<Settlement>);
            var target = AccessTools.Method(typeof(SandBoxNavigationCache), "GetRealDistanceAndLandRatioBetweenSettlements",
                new[] { element, element, typeof(float).MakeByRefType() });
            if (target == null)
            {
                SubModule.Log("Path guard: SandBoxNavigationCache.GetRealDistanceAndLandRatioBetweenSettlements not found; path queries are not checked.");
                return;
            }
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(PathQueryGuard), nameof(RealDistancePrefix)),
                postfix: new HarmonyMethod(typeof(PathQueryGuard), nameof(RealDistancePostfix)));
            SubModule.Log("Path guard: dedicated server settlement path query check ready.");
        }

        private static bool RealDistancePrefix(NavigationCacheElement<Settlement> __0, NavigationCacheElement<Settlement> __1,
            ref float __2, ref float __result, out int __state)
        {
            __state = 0;
            try
            {
                var kind = PathQueryRules.CheckPair(Check(__0, out var face1), Check(__1, out var face2));
                if (kind != PathQueryRules.Unsafe.None)
                {
                    LogOnce(kind, __0, __1);
                    __2 = PathQueryRules.SkippedLandRatio;
                    __result = PathQueryRules.SkippedDistance;
                    return false;
                }
                var query = Interlocked.Increment(ref _queries);
                if (query <= Breadcrumbs)
                {
                    __state = query;
                    Breadcrumb("Path guard: path query " + query + "/" + Breadcrumbs + " " + Name(__0) + " (face " + face1 + ") -> "
                        + Name(__1) + " (face " + face2 + ") goes to the engine.");
                }
            }
            catch (Exception ex)
            {
                // Once: a check that throws would otherwise log on every path query.
                if (Interlocked.Exchange(ref _checkFailedLogged, 1) == 0)
                    SubModule.Log("Path guard: could not check a path query, so it goes to the engine as it is (logged once): " + ex.Message);
            }
            return true;
        }

        private static void RealDistancePostfix(float __result, int __state)
        {
            if (__state > 0)
                Breadcrumb("Path guard: path query " + __state + "/" + Breadcrumbs + " came back from the engine: distance "
                    + __result.ToString("0.##", CultureInfo.InvariantCulture) + ".");
        }

        private static PathQueryRules.Unsafe Check(NavigationCacheElement<Settlement> element, out int face)
        {
            face = -1;
            var settlement = element.Settlement;
            if (settlement == null || settlement.SettlementComponent == null)
                return PathQueryRules.Check(settlement != null, false, face);
            var position = element.IsPortUsed ? element.PortPosition : element.GatePosition;
            face = position.Face.FaceIndex;
            return PathQueryRules.Check(true, true, face);
        }

        private static void LogOnce(PathQueryRules.Unsafe kind, NavigationCacheElement<Settlement> first, NavigationCacheElement<Settlement> second)
        {
            lock (Logged)
            {
                if (!Logged.Add(kind))
                    return;
            }
            Breadcrumb("Path guard: path query " + Name(first) + " -> " + Name(second) + " skipped (" + kind
                + "), answered as far apart so the engine never gets face -1. Logged once for this kind.");
        }

        private static string Name(NavigationCacheElement<Settlement> element) =>
            (element.Settlement?.StringId ?? "null") + (element.IsPortUsed ? " port" : "");

        // A native crash right after leaves no time for a buffered console, so it is flushed.
        private static void Breadcrumb(string text)
        {
            SubModule.Log(text);
            try { Console.Out.Flush(); }
            catch { }
        }
    }
}
