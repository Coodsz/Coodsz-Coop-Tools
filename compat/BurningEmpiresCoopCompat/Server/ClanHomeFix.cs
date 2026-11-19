using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BurningEmpiresCoopCompat
{
    // Clan homes on settlements Burning Empires' map does not have, on the server's fresh campaign.
    // Coop's coop_team clan names town_S1, a Calradia town, as its home. Burning Empires' map has
    // no town_S1, so the clan keeps the game's empty stand in for it: no town behind it and no
    // place on the nav mesh. With no fiefs that stand in is also the clan's mid settlement, and
    // CharacterRelationCampaignBehavior's new game pass asks the distance between mid settlements.
    // The path query for it reaches the engine with face -1 and the server dies with an access
    // violation. Just before Campaign.OnNewGameCreated raises the new game event, every clan
    // whose home has no settlement behind it gets a real town (ClanHomeRules) and its mid
    // settlement is worked out again. Only on the dedicated server's fresh campaign.
    internal static class ClanHomeFix
    {
        public static void Install(Harmony harmony)
        {
            if (!FreshCampaign.IsDedicatedServerProcess())
                return;
            try
            {
                var target = AccessTools.Method(typeof(Campaign), "OnNewGameCreated", new[] { typeof(CampaignGameStarter) });
                if (target == null)
                {
                    SubModule.Log("Clan homes: Campaign.OnNewGameCreated not found; homes on missing settlements are kept.");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(ClanHomeFix), nameof(OnNewGameCreatedPrefix)));
            }
            catch (Exception ex)
            {
                SubModule.Log("Clan homes could not hook Campaign.OnNewGameCreated: " + ex.Message);
            }
        }

        private static void OnNewGameCreatedPrefix()
        {
            if (!FreshCampaign.FreshStartActive)
                return;
            try
            {
                var fixedCount = 0;
                foreach (var clan in Clan.All)
                {
                    if (clan != null && (Missing(clan.InitialHomeSettlement) || Missing(clan.HomeSettlement)))
                        fixedCount += Fix(clan) ? 1 : 0;
                }
                foreach (var kingdom in Kingdom.All)
                {
                    if (kingdom != null && Missing(kingdom.InitialHomeSettlement))
                        SubModule.Log("Clan homes: kingdom " + kingdom.StringId + " has home '" + kingdom.InitialHomeSettlement.StringId
                            + "', which this map does not have; the distance guard keeps its path queries off the engine.");
                }
                SubModule.Log("Clan homes: " + fixedCount + " clan home(s) on missing settlements moved to a real town.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Clan homes: could not check the clans' homes: " + ex);
            }
        }

        private static bool Missing(Settlement settlement) =>
            ClanHomeRules.IsMissingSettlement(settlement, settlement?.SettlementComponent);

        private static bool Fix(Clan clan)
        {
            var old = (clan.HomeSettlement ?? clan.InitialHomeSettlement)?.StringId ?? "?";
            var town = ClanHomeRules.PickTown(Settlement.All,
                s => s.IsTown && s.SettlementComponent != null, s => s.Culture?.StringId, clan.Culture?.StringId);
            if (town == null)
            {
                SubModule.Log("Clan homes: clan " + clan.StringId + " has home '" + old + "', which this map does not have, and the map has no town to use.");
                return false;
            }
            try
            {
                clan.SetInitialHomeSettlement(town);
                clan.CalculateMidSettlement();
                SubModule.Log("Clan homes: clan " + clan.StringId + " had home '" + old + "', which this map does not have; home is now "
                    + town.StringId + " (mid settlement " + (clan.FactionMidSettlement?.StringId ?? "none") + ").");
                return true;
            }
            catch (Exception ex)
            {
                SubModule.Log("Clan homes: could not move clan " + clan.StringId + "'s home to " + town.StringId + ": " + ex.Message);
                return false;
            }
        }
    }
}
