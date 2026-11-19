using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BurningEmpiresCoopCompat
{
    // In single player Burning Empires asks the player for the state religion of a new kingdom
    // through a pop up before the kingdom is made. The co-op server has nobody to answer it, so
    // on the server:
    // - DsPopups makes Burning Empires skip its religion pop up, so the kingdom is always made;
    // - a new kingdom with no state religion gets the religion most of its fiefs' people follow,
    //   else the default religion of its culture. The pick is the same every time (ties go to the
    //   lower religion id) and is written straight into Burning Empires' data, so no gender
    //   enforcement runs for it.
    // Players' PCs and single player keep Burning Empires' own pop up.
    internal static class FoundingReligion
    {
        private const string CultureDefaultKey = "BECultureDefaultReligion:";

        private static MethodInfo _worldDataCurrent;
        private static PropertyInfo _kingdomReligions;
        private static PropertyInfo _settlementPopulations;
        private static PropertyInfo _customValues;
        private static MethodInfo _isFormalReligion;
        // PopulationData.Groups and PopulationGroup.ReligionId / Count, read on first use.
        private static PropertyInfo _groups;
        private static PropertyInfo _groupReligion;
        private static PropertyInfo _groupCount;

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;
            count += PatchKingdomCreated(harmony, burningEmpires);
            return count;
        }

        // ---- Founding religion on the server ----

        private static int PatchKingdomCreated(Harmony harmony, Assembly burningEmpires)
        {
            var manager = FindType(burningEmpires, "BurningEmpires.BESystems.Core.BEWorldDataManager");
            var worldData = FindType(burningEmpires, "BurningEmpires.BESystems.Core.BEWorldData");
            var assignment = FindType(burningEmpires, "BurningEmpires.BESystems.Religion.BEReligionAssignmentService");
            _worldDataCurrent = manager == null ? null : AccessTools.PropertyGetter(manager, "Current");
            _kingdomReligions = worldData == null ? null : AccessTools.Property(worldData, "KingdomReligions");
            _settlementPopulations = worldData == null ? null : AccessTools.Property(worldData, "SettlementPopulations");
            _customValues = worldData == null ? null : AccessTools.Property(worldData, "CustomValues");
            _isFormalReligion = assignment == null ? null : AccessTools.Method(assignment, "IsFormalReligion", new[] { typeof(string) });
            if (_worldDataCurrent == null || _kingdomReligions == null || _isFormalReligion == null)
            {
                SubModule.Log("Could not find Burning Empires' state religion data; a kingdom founded in co-op has no state religion.");
                return 0;
            }

            var method = AccessTools.Method(typeof(CampaignEventDispatcher), "OnKingdomCreated", new[] { typeof(Kingdom) });
            if (method == null)
            {
                SubModule.Log("Could not find CampaignEventDispatcher.OnKingdomCreated; a kingdom founded in co-op has no state religion.");
                return 0;
            }
            try
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(FoundingReligion), nameof(KingdomCreatedPostfix)));
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Could not hook CampaignEventDispatcher.OnKingdomCreated: " + ex.Message);
                return 0;
            }
        }

        // Runs after every listener, Burning Empires' own included.
        private static void KingdomCreatedPostfix(Kingdom __0)
        {
            if (__0 == null || !Coop.OnServer)
                return;
            try
            {
                AssignIfMissing(__0);
            }
            catch (Exception ex)
            {
                SubModule.Log("Could not pick a state religion for new kingdom " + __0.StringId + ": " + ex);
            }
        }

        private static void AssignIfMissing(Kingdom kingdom)
        {
            if (string.IsNullOrWhiteSpace(kingdom.StringId))
                return;
            var data = _worldDataCurrent.Invoke(null, null);
            if (data == null)
                return;
            var religions = _kingdomReligions.GetValue(data, null) as Dictionary<string, string>;
            if (religions != null && religions.TryGetValue(kingdom.StringId, out var current) && IsFormal(current))
                return;

            var source = "the majority of its fiefs";
            var religion = PickMajority(CountFiefReligions(data, kingdom.RulingClan));
            if (religion == null)
            {
                source = "its culture's default";
                religion = CultureDefault(data, kingdom.Culture ?? kingdom.RulingClan?.Culture);
            }
            if (religion == null)
            {
                SubModule.Log("New kingdom " + kingdom.StringId + " has no fief believers and no culture default religion; it keeps no state religion.");
                return;
            }

            if (religions == null)
            {
                religions = new Dictionary<string, string>(StringComparer.Ordinal);
                _kingdomReligions.SetValue(data, religions, null);
            }
            religions[kingdom.StringId] = religion;
            SubModule.Log("New kingdom " + kingdom.StringId + " takes state religion " + religion + " (" + source + ").");
        }

        // Believers of each formal religion across the ruling clan's fiefs, like Burning Empires
        // counts them for its own pop up.
        private static Dictionary<string, double> CountFiefReligions(object data, Clan clan)
        {
            var counts = new Dictionary<string, double>(StringComparer.Ordinal);
            if (clan?.Fiefs == null || _settlementPopulations == null)
                return counts;
            if (!(_settlementPopulations.GetValue(data, null) is IDictionary populations))
                return counts;

            foreach (var town in clan.Fiefs)
            {
                Settlement settlement = town?.Settlement;
                if (settlement == null || string.IsNullOrWhiteSpace(settlement.StringId) || !populations.Contains(settlement.StringId))
                    continue;
                var population = populations[settlement.StringId];
                if (population == null)
                    continue;
                _groups = _groups ?? AccessTools.Property(population.GetType(), "Groups");
                if (!(_groups?.GetValue(population, null) is IEnumerable groups))
                    continue;
                foreach (var group in groups)
                {
                    if (group == null)
                        continue;
                    _groupReligion = _groupReligion ?? AccessTools.Property(group.GetType(), "ReligionId");
                    _groupCount = _groupCount ?? AccessTools.Property(group.GetType(), "Count");
                    if (_groupReligion == null || _groupCount == null)
                        return counts;
                    var religion = _groupReligion.GetValue(group, null) as string;
                    var count = Convert.ToDouble(_groupCount.GetValue(group, null));
                    if (count <= 0 || !IsFormal(religion))
                        continue;
                    counts[religion] = counts.TryGetValue(religion, out var sum) ? sum + count : count;
                }
            }
            return counts;
        }

        // The religion with the most believers; on a tie the lower id by ordinal order, so the
        // answer never depends on the PC's language. Null when nobody believes in anything.
        internal static string PickMajority(IEnumerable<KeyValuePair<string, double>> counts)
        {
            string best = null;
            var bestCount = 0.0;
            foreach (var entry in counts)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value <= 0)
                    continue;
                if (best == null || entry.Value > bestCount
                    || (entry.Value == bestCount && string.CompareOrdinal(entry.Key, best) < 0))
                {
                    best = entry.Key;
                    bestCount = entry.Value;
                }
            }
            return best;
        }

        // Burning Empires keeps each culture's default religion in its CustomValues.
        private static string CultureDefault(object data, CultureObject culture)
        {
            if (culture == null || _customValues == null)
                return null;
            if (!(_customValues.GetValue(data, null) is Dictionary<string, string> values))
                return null;
            return values.TryGetValue(CultureDefaultKey + culture.StringId, out var religion) && IsFormal(religion) ? religion : null;
        }

        private static bool IsFormal(string religionId)
        {
            if (string.IsNullOrWhiteSpace(religionId))
                return false;
            try { return (bool)_isFormalReligion.Invoke(null, new object[] { religionId }); }
            catch { return false; }
        }

        private static Type FindType(Assembly burningEmpires, string fullName)
        {
            Type type = null;
            try { type = burningEmpires?.GetType(fullName, false); }
            catch { }
            type = type ?? AccessTools.TypeByName(fullName);
            if (type == null)
                SubModule.Log("Could not find Burning Empires type " + fullName + ".");
            return type;
        }
    }
}
