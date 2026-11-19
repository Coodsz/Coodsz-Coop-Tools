#nullable disable
using System.Collections.Generic;

namespace BurningEmpiresCoopCompat
{
    // VolunteerPublish's plain rules, without game types, so Coodsz Host's tests can run them.
    internal static class VolunteerSlotRules
    {
        // Burning Empires sorts each notable's slots after its reroll with the empty ones last,
        // which moves troops to other indices. Vanilla keeps an empty slot where it was, and a
        // player whose recruit screen stayed open across the reroll still sends the old indices,
        // so Coop's commit found empty slots and dropped those recruits.
        // Puts the rerolled troops back into the slots that held a troop before, in order, and
        // the rest into the free slots, in order. The empty slots stay where they were and no
        // troop is dropped. Null when either array is missing.
        public static T[] KeepSlotPositions<T>(T[] before, T[] after) where T : class
        {
            if (before == null || after == null)
                return null;
            var troops = new List<T>();
            foreach (var troop in after)
            {
                if (troop != null)
                    troops.Add(troop);
            }

            var order = new List<int>();
            for (var i = 0; i < after.Length; i++)
            {
                if (i < before.Length && before[i] != null)
                    order.Add(i);
            }
            for (var i = 0; i < after.Length; i++)
            {
                if (i >= before.Length || before[i] == null)
                    order.Add(i);
            }

            var result = new T[after.Length];
            for (var k = 0; k < troops.Count; k++)
                result[order[k]] = troops[k];
            return result;
        }
    }
}
