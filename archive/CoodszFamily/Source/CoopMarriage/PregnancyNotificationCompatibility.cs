using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.LogEntries;

namespace CoopMarriage;

// Native visibility calls Mother.Clan.Equals(Hero.MainHero.Clan). Dramalord can
// conceive clanless heroes; a dedicated/local perspective can also be absent.
// Only suppress that inapplicable local notification, not the conception event,
// canonical pregnancy, encyclopedia history or Coop replication.
[HarmonyPatch(typeof(PregnancyLogEntry), nameof(PregnancyLogEntry.IsVisibleNotification), MethodType.Getter)]
internal static class PregnancyNotificationCompatibility
{
    [HarmonyPrefix]
    private static bool Prefix(PregnancyLogEntry __instance, ref bool __result)
    {
        if (__instance.Mother?.Clan != null && Hero.MainHero?.Clan != null) return true;
        __result = false;
        return false;
    }
}
