// AD1259 <-> Coop dedicated server: companion templates on a map that lacks some template types.
//
// The game's CompanionsCampaignBehavior.GetCompanionTemplateToSpawn returns null when the template type
// it picks has no free template, and its callers skip the spawn. Coop 0.1.7 adds a postfix
// (GameInterface.Services.Companions.Patches.Disable.CompanionsCampaignBehaviorPatches
// .GetCompanionTemplateToSpawnPostfix) that fills that null with list[random.Next(list.Count)] from the
// template list of the type GetCompanionTemplateTypeToSpawn picks, which Coop also randomises over every
// template type. AD1259 has no wanderer templates for some types, so that list can be empty and
// OnNewGameCreated threw ArgumentOutOfRangeException while the new world was created (crash CR-U2W9J).
//
//  1. A postfix that runs before Coop's fills the null first, the way Coop means to: from the picked
//     type's list when it has templates, otherwise from any type that has templates. Coop's postfix then
//     sees a template and does nothing.
//  2. A finalizer turns the same out of range or missing key error into "no companion this time", which
//     the game's callers already handle, in case another patch still throws it.
//
// Only active when Coop is loaded; the game's own null result is kept when no type has a template.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace AD1259CoopCompat
{
    internal static class CompanionTemplateGuard
    {
        private static FieldInfo _templates;
        private static MethodInfo _typeToSpawn;
        private static readonly Random Random = new Random();
        private static int _filled;
        private static int _caught;

        public static void Install(Harmony harmony)
        {
            if (!MapProvider.IsModuleActive("Coop") && !MapProvider.IsModuleActive("CoopNightly")) return;
            Type behavior = typeof(CompanionsCampaignBehavior);
            MethodInfo target = AccessTools.Method(behavior, "GetCompanionTemplateToSpawn", Type.EmptyTypes);
            _templates = AccessTools.Field(behavior, "_companionsOfTemplates");
            _typeToSpawn = AccessTools.Method(behavior, "GetCompanionTemplateTypeToSpawn", Type.EmptyTypes);
            if (target == null || target.ReturnType != typeof(CharacterObject) || _templates == null)
            {
                Log.Warn("CompanionsCampaignBehavior.GetCompanionTemplateToSpawn or _companionsOfTemplates was not found; " +
                         "the companion template guard is not installed.");
                return;
            }

            harmony.Patch(target,
                postfix: new HarmonyMethod(typeof(CompanionTemplateGuard), nameof(FillEmptyResult)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(CompanionTemplateGuard), nameof(SwallowEmptyListError)));
            Log.Info("companion template guard armed: a template type without templates on this map no longer stops the world.");
        }

        private static void FillEmptyResult(CompanionsCampaignBehavior __instance, ref CharacterObject __result)
        {
            if (__result != null || __instance == null) return;
            try
            {
                IDictionary templates = _templates.GetValue(__instance) as IDictionary;
                if (templates == null) return;

                var lists = new List<IList>();
                int preferred = -1;
                object preferredType = null;
                try { preferredType = _typeToSpawn == null ? null : _typeToSpawn.Invoke(__instance, null); }
                catch { }
                foreach (DictionaryEntry entry in templates)
                {
                    IList list = entry.Value as IList;
                    if (list == null) continue;
                    if (preferredType != null && Equals(entry.Key, preferredType)) preferred = lists.Count;
                    lists.Add(list);
                }

                var counts = new int[lists.Count];
                for (int i = 0; i < lists.Count; i++) counts[i] = lists[i].Count;
                int[] pick = CompanionTemplateLogic.Pick(counts, preferred, Random.Next());
                if (pick == null) return;   // no type has a template: the game's null stands, no companion spawns
                __result = lists[pick[0]][pick[1]] as CharacterObject;
                if (__result != null && System.Threading.Interlocked.Increment(ref _filled) == 1)
                {
                    Log.Info("a companion template was picked from a template type this map has (logged once).");
                }
            }
            catch (Exception e)
            {
                Log.Warn("companion template guard could not pick a template: " + FreshCampaign.Unwrap(e));
            }
        }

        private static Exception SwallowEmptyListError(Exception __exception, ref CharacterObject __result)
        {
            if (!(__exception is ArgumentOutOfRangeException) && !(__exception is KeyNotFoundException)) return __exception;
            __result = null;
            if (System.Threading.Interlocked.Increment(ref _caught) <= 3)
            {
                Log.Warn("a companion template pick read an empty template list; no companion spawns this time: " +
                         __exception.Message);
            }
            return null;
        }
    }

    /// <summary>Engine-independent part of the companion template pick, testable outside the game.</summary>
    public static class CompanionTemplateLogic
    {
        /// <summary>
        /// Picks a template as { list index, template index }: from the preferred list when it has templates,
        /// otherwise evenly from every list that has any. Null when no list has a template.
        /// </summary>
        public static int[] Pick(int[] counts, int preferred, int roll)
        {
            if (counts == null || counts.Length == 0) return null;
            if (roll < 0) roll = roll == int.MinValue ? 0 : -roll;
            if (preferred >= 0 && preferred < counts.Length && counts[preferred] > 0)
            {
                return new[] { preferred, roll % counts[preferred] };
            }

            long total = 0;
            foreach (int count in counts) if (count > 0) total += count;
            if (total == 0) return null;

            long r = roll % total;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] <= 0) continue;
                if (r < counts[i]) return new[] { i, (int)r };
                r -= counts[i];
            }
            return null;
        }
    }
}
