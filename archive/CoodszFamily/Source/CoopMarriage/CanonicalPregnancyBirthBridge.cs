using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace CoopMarriage;

// Restore native pregnancy routes suppressed by Coop, on the authority only.
// Keep native records and Coop's existing conception/delivery synchronization.
[HarmonyPatch(typeof(PregnancyCampaignBehavior), nameof(PregnancyCampaignBehavior.RegisterEvents))]
internal static class CanonicalPregnancyBirthBridge
{
    private const string CoopDisableType = "GameInterface.Services.Heroes.Patches.Disable.DisablePregnancyCampaignBehavior";
    private static readonly ConditionalWeakTable<PregnancyCampaignBehavior, Listeners> Installed = new();
    private static readonly FieldInfo Records = AccessTools.Field(typeof(PregnancyCampaignBehavior), "_heroPregnancies");
    private static readonly FieldInfo Mother = AccessTools.Field(typeof(PregnancyCampaignBehavior).GetNestedType("Pregnancy", BindingFlags.Public | BindingFlags.NonPublic), "Mother");
    private static readonly MethodInfo Daily = AccessTools.Method(typeof(PregnancyCampaignBehavior), "DailyTickHero", new[] { typeof(Hero) });
    private static readonly MethodInfo Conceived = AccessTools.Method(typeof(PregnancyCampaignBehavior), "ChildConceived", new[] { typeof(Hero) });

    [HarmonyPostfix]
    private static void Postfix(PregnancyCampaignBehavior __instance, bool __runOriginal)
    {
        if (__runOriginal || Installed.TryGetValue(__instance, out _)) return;
        var registration = AccessTools.Method(typeof(PregnancyCampaignBehavior), nameof(PregnancyCampaignBehavior.RegisterEvents));
        if (Harmony.GetPatchInfo(registration)?.Prefixes.Any(p => p.PatchMethod.DeclaringType?.FullName == CoopDisableType) != true) return;
        if (Records == null || Mother == null || Daily == null || Conceived == null)
            throw new NotSupportedException("COOP Family: native pregnancy API differs; refusing incomplete childbirth support.");
        var listeners = new Listeners(__instance);
        CampaignEvents.DailyTickHeroEvent.AddNonSerializedListener(listeners, listeners.OnDaily);
        CampaignEvents.OnChildConceivedEvent.AddNonSerializedListener(listeners, listeners.OnConceived);
        CampaignEvents.HeroKilledEvent.AddNonSerializedListener(listeners, listeners.OnKilled);
        Installed.Add(__instance, listeners);
        RuntimeProof.Write("Native pregnancy events restored with authority guards: NPC/player conception, canonical records, delivery and maternal-death cleanup");
    }

    private sealed class Listeners
    {
        private readonly PregnancyCampaignBehavior _behavior;
        internal Listeners(PregnancyCampaignBehavior behavior) { _behavior = behavior; }

        internal void OnDaily(Hero hero)
        {
            if (!CoopCompatibility.IsAuthoritative || hero == null) return;
            Daily.Invoke(_behavior, new object[] { hero });
        }

        internal void OnConceived(Hero mother)
        {
            if (!CoopCompatibility.IsAuthoritative || mother == null || !mother.IsFemale || !mother.IsAlive) return;
            var records = Records.GetValue(_behavior) as IList;
            if (records == null) throw new InvalidOperationException("COOP Family: canonical pregnancy records unavailable.");
            // DramalordCoopFix inserts an exact-father record BEFORE the native
            // action. Preserve it, including non-spouse fathers; never duplicate.
            foreach (var record in records)
                if (ReferenceEquals(Mother.GetValue(record), mother)) return;
            if (mother.Spouse == null)
            {
                RuntimeProof.Write("Conception has no canonical record or spouse; father cannot be inferred, mother=" + mother.StringId);
                return;
            }
            Conceived.Invoke(_behavior, new object[] { mother });
            RuntimeProof.Write("Canonical conception recorded: mother=" + mother.StringId + "; father=" + mother.Spouse.StringId);
        }

        internal void OnKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (CoopCompatibility.IsAuthoritative && victim != null)
                _behavior.OnHeroKilled(victim, killer, detail, showNotification);
        }
    }
}
