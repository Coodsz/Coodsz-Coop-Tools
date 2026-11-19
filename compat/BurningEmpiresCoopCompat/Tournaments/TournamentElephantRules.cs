#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BurningEmpiresCoopCompat
{
    // NoTournamentElephants' plain rules, without game types, so Coodsz Host's tests can run them.
    internal static class TournamentElephantRules
    {
        // Burning Empires Elephants rides on the "elephant" monster, and its harnesses are named
        // be_elephant_armor_*. A mount or harness counts as elephant gear when its monster is the
        // elephant or its item id names an elephant. An empty slot is never elephant gear.
        public static bool IsElephantGear(string itemId, string monsterId)
        {
            if (string.Equals(monsterId, "elephant", StringComparison.Ordinal))
                return true;
            return !string.IsNullOrEmpty(itemId) && itemId.IndexOf("elephant", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // The slots to empty on one fighter: an elephant mount goes with its harness, and an
        // elephant harness on another mount goes on its own. Horses and camels stay.
        public static (bool Mount, bool Harness) FighterSlotsToClear(string mountId, string mountMonsterId, string harnessId)
        {
            if (IsElephantGear(mountId, mountMonsterId))
                return (true, true);
            return (false, IsElephantGear(harnessId, null));
        }

        // Only a mount or harness is dropped from the prizes, so other gear that names an
        // elephant stays a prize.
        public static bool IsElephantPrize(string itemId, string monsterId, bool mountOrHarness) =>
            mountOrHarness && IsElephantGear(itemId, monsterId);

        public static string MatchLine(int fighters, int mounts, int harnesses) =>
            "Tournament elephants: took " + Count(mounts, "elephant mount", "elephant mounts") + " and "
            + Count(harnesses, "elephant harness", "elephant harnesses") + " off "
            + Count(fighters, "fighter", "fighters") + " in this tournament match.";

        public static string PrizeLine(IEnumerable<string> itemIds)
        {
            var ids = (itemIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)).ToList();
            return "Tournament elephants: dropped " + Count(ids.Count, "elephant prize", "elephant prizes")
                + " from the tournament prizes (" + string.Join(", ", ids) + ").";
        }

        private static string Count(int count, string one, string many) => count + " " + (count == 1 ? one : many);
    }
}
