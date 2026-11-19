#nullable disable
using System;
using System.Collections.Generic;

namespace BurningEmpiresCoopCompat
{
    // ClanHomeFix's plain rules, without game types, so Coodsz Host's tests can run them.
    internal static class ClanHomeRules
    {
        // A home that names a settlement this map never built: the XML pointed at it, the game
        // made a stand in for it, and no settlement XML filled it in, so it has no town, castle,
        // village or hideout behind it. No home at all is left alone, as the game leaves it.
        public static bool IsMissingSettlement(object settlement, object component) =>
            settlement != null && component == null;

        // The town a clan with a missing home gets instead: the first town of the clan's own
        // culture, else the first town, in the game's settlement order. Null with no towns.
        public static T PickTown<T>(IEnumerable<T> settlements, Func<T, bool> isTown, Func<T, string> cultureId, string clanCultureId)
            where T : class
        {
            if (settlements == null || isTown == null)
                return null;
            T firstTown = null;
            foreach (var settlement in settlements)
            {
                if (settlement == null || !isTown(settlement))
                    continue;
                if (!string.IsNullOrEmpty(clanCultureId) && cultureId != null
                    && string.Equals(cultureId(settlement), clanCultureId, StringComparison.Ordinal))
                    return settlement;
                if (firstTown == null)
                    firstTown = settlement;
            }
            return firstTown;
        }
    }
}
