//
// Two guards for clans and settlements that a custom campaign map (AD1259) does not define.
//
// Coop v0.1.6 spawns the lords of its own minor faction "coop_team" on the server (Coop's
// ConfiguredMinorFactionHeroSpawner). Coop's XML gives that clan the vanilla home
// "Settlement.town_S1". AD1259 removes every vanilla settlement, so the reference resolves to a
// placeholder object with no settlement component and no position. During world creation vanilla
// CharacterRelationCampaignBehavior.UpdateFriendshipAndEnemies asks for the distance between every
// pair of lords' faction mid-settlements; the placeholder is not in the map's distance cache, so the
// cache falls back to a native navigation-mesh path query from an invalid face, which ends the
// process with an access violation (seen on the server after updating to Coop v0.1.6).
//
//  - PlaceholderHomes: just before a new campaign's OnNewGameCreated event, any clan whose home is
//    such a placeholder gets a real town of the map instead (same culture when the map has one).
//  - DistanceMissGuard: a distance-cache miss involving anything that is not a real settlement on the
//    navigation mesh is answered as "unreachable" instead of asking the engine for a path. While this
//    patch builds a new world, a miss between two real settlements (which the map's own cache should
//    already contain) is estimated from straight-line distance. Every other miss runs unchanged.
//
// Both only act when a custom map provider is active. Escape hatches (value "1"):
//   AD1259COOP_DISABLE_DISTANCE_GUARD - leave distance-cache misses to the game
//   AD1259COOP_DISABLE_HOME_FIX       - leave clans' homes as the XML set them

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map.DistanceCache;
using TaleWorlds.CampaignSystem.Settlements;

namespace AD1259CoopCompat
{
    internal static class MapSettlements
    {
        private static bool _resolved;
        private static MapProvider _provider;

        /// <summary>The active custom map provider, or null on a vanilla map (resolved once per process).</summary>
        public static MapProvider Provider
        {
            get
            {
                if (!_resolved)
                {
                    _provider = MapProvider.Resolve();
                    _resolved = true;
                }
                return _provider;
            }
        }

        /// <summary>Null when the settlement is a real settlement of the loaded map; otherwise why it is not.</summary>
        public static string Problem(Settlement settlement, bool port)
        {
            if (settlement == null) return "no settlement";
            if (settlement.SettlementComponent == null)
            {
                return "an id this map does not define (placeholder without a settlement component)";
            }
            if (!ReferenceEquals(Settlement.Find(settlement.StringId), settlement)) return "not a registered settlement";
            bool onMesh = port ? settlement.PortPosition.Face.IsValid() : settlement.GatePosition.Face.IsValid();
            if (!onMesh) return (port ? "its port" : "its gate") + " is off the map's navigation mesh";
            return null;
        }

        public static string Describe(Settlement settlement, bool port, string problem)
        {
            string id = settlement == null ? "(null)" : (settlement.StringId ?? "(no id)");
            if (port) id += " (port)";
            return problem == null ? id : id + " [" + problem + "]";
        }
    }

    internal static class DistanceMissGuard
    {
        // DefaultMapDistanceModel's own value for "no route"; NavigationCache does not raise its
        // maximum-connected-distance for it.
        private const float Unreachable = 100000000f;
        private const float DetourFactor = 1.3f;
        private const int DetailedLogLines = 25;

        private static int _refused;
        private static int _estimated;
        private static int _logged;
        private static bool _failureLogged;

        public static void Install(Harmony harmony)
        {
            if (Env.IsSet("AD1259COOP_DISABLE_DISTANCE_GUARD"))
            {
                Log.Info("distance-cache miss guard disabled by AD1259COOP_DISABLE_DISTANCE_GUARD.");
                return;
            }
            MethodInfo target = AccessTools.Method(typeof(SandBoxNavigationCache), "GetRealDistanceAndLandRatioBetweenSettlements");
            if (target == null)
            {
                Log.Warn("SandBoxNavigationCache.GetRealDistanceAndLandRatioBetweenSettlements was not found in this game " +
                         "build; distance-cache misses are left to the game.");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(DistanceMissGuard), nameof(Prefix)) { priority = Priority.First });
        }

        private static bool Prefix(NavigationCacheElement<Settlement> settlement1, NavigationCacheElement<Settlement> settlement2,
            ref float landRatio, ref float __result)
        {
            try
            {
                if (MapSettlements.Provider == null) return true;

                string problem1 = MapSettlements.Problem(settlement1.Settlement, settlement1.IsPortUsed);
                string problem2 = MapSettlements.Problem(settlement2.Settlement, settlement2.IsPortUsed);
                if (problem1 != null || problem2 != null)
                {
                    landRatio = 1f;
                    __result = Unreachable;
                    _refused++;
                    if (_logged < DetailedLogLines || IsMilestone(_refused))
                    {
                        _logged++;
                        Log.Warn("distance guard: no engine path query between " +
                                 MapSettlements.Describe(settlement1.Settlement, settlement1.IsPortUsed, problem1) + " and " +
                                 MapSettlements.Describe(settlement2.Settlement, settlement2.IsPortUsed, problem2) +
                                 "; treated as unreachable (" + _refused.ToString(CultureInfo.InvariantCulture) + " so far).");
                    }
                    return false;
                }

                if (FreshCampaign.Active)
                {
                    Settlement s1 = settlement1.Settlement;
                    Settlement s2 = settlement2.Settlement;
                    float straight = (settlement1.IsPortUsed ? s1.PortPosition : s1.GatePosition)
                        .Distance(settlement2.IsPortUsed ? s2.PortPosition : s2.GatePosition);
                    landRatio = 1f;
                    __result = straight * DetourFactor;
                    _estimated++;
                    if (_logged < DetailedLogLines || IsMilestone(_estimated))
                    {
                        _logged++;
                        Log.Warn("distance guard: " + MapSettlements.Describe(s1, settlement1.IsPortUsed, null) + " to " +
                                 MapSettlements.Describe(s2, settlement2.IsPortUsed, null) + " is missing from " +
                                 MapSettlements.Provider.ModuleId + "'s distance cache while the new world is built; " +
                                 "estimated from straight-line distance (" + _estimated.ToString(CultureInfo.InvariantCulture) +
                                 " so far).");
                    }
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    Log.Warn("distance guard could not inspect a cache miss; the game handles it: " + e.Message);
                }
                return true;
            }
        }

        private static bool IsMilestone(int count)
        {
            return count == 100 || count == 1000 || count == 10000 || count == 100000;
        }
    }

    internal static class PlaceholderHomes
    {
        public static void Install(Harmony harmony)
        {
            if (Env.IsSet("AD1259COOP_DISABLE_HOME_FIX"))
            {
                Log.Info("placeholder-home fix disabled by AD1259COOP_DISABLE_HOME_FIX.");
                return;
            }
            MethodInfo target = AccessTools.Method(typeof(Campaign), "OnNewGameCreated");
            MethodInfo setHome = AccessTools.Method(typeof(Clan), "SetInitialHomeSettlement");
            MethodInfo midSettlement = AccessTools.Method(typeof(Clan), "CalculateMidSettlement");
            if (target == null || setHome == null || midSettlement == null)
            {
                Log.Warn("Campaign.OnNewGameCreated, Clan.SetInitialHomeSettlement or Clan.CalculateMidSettlement was not " +
                         "found in this game build; clans' homes are left as the XML set them.");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PlaceholderHomes), nameof(Prefix)) { priority = Priority.First });
        }

        // Runs before any OnNewGameCreated listener: before minor-faction lords are spawned at their clan's home
        // and before lords' friendships are rolled from the distance between their factions' mid-settlements.
        private static void Prefix()
        {
            try
            {
                MapProvider provider = MapSettlements.Provider;
                if (provider == null) return;

                List<Settlement> towns = null;
                foreach (Clan clan in Clan.All.ToList())
                {
                    if (clan == null || clan.IsEliminated) continue;
                    Settlement initial = clan.InitialHomeSettlement;
                    Settlement home = clan.HomeSettlement;
                    string initialProblem = initial == null ? null : MapSettlements.Problem(initial, false);
                    string homeProblem = home == null ? null : MapSettlements.Problem(home, false);
                    if (initialProblem == null && homeProblem == null) continue;

                    if (towns == null)
                    {
                        towns = Settlement.All
                            .Where(s => s != null && s.IsTown && MapSettlements.Problem(s, false) == null)
                            .OrderBy(s => s.StringId, StringComparer.Ordinal)
                            .ToList();
                    }
                    Settlement pick = towns.FirstOrDefault(t => clan.Culture != null && t.Culture == clan.Culture) ??
                                      towns.FirstOrDefault();
                    if (pick == null)
                    {
                        Log.Warn(provider.ModuleId + " has no town to use as a home; clan '" + clan.StringId + "' keeps its home.");
                        return;
                    }

                    Settlement bad = initialProblem != null ? initial : home;
                    clan.SetInitialHomeSettlement(pick);
                    clan.CalculateMidSettlement();
                    bool cultureMatch = clan.Culture != null && pick.Culture == clan.Culture;
                    Log.Info("clan '" + clan.StringId + "' had home '" + (bad.StringId ?? "?") + "', which " + provider.ModuleId +
                             " does not define; it now starts at " + pick.StringId + " (" +
                             (cultureMatch ? "same culture" : "no " + (clan.Culture == null ? "matching" : clan.Culture.StringId) +
                                                              " town on this map") +
                             "; home " + (clan.HomeSettlement == null ? "-" : clan.HomeSettlement.StringId) +
                             ", mid-settlement " + (clan.FactionMidSettlement == null ? "-" : clan.FactionMidSettlement.StringId) + ").");
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not check clans' homes against " + (MapSettlements.Provider == null ? "the map" : MapSettlements.Provider.ModuleId) +
                         ": " + e);
            }
        }
    }
}
