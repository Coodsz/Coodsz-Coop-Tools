using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace RealmOfThronesCoopCompat
{
    // Banner colours before Realm of Thrones' factions load on the server's fresh campaign, adapted
    // from Burning Empires Coop Compat. Every clan in the Factions XML reads its banner colour from
    // BannerManager while it loads. On a PC the view module loads the colours, but the headless
    // server never loads it, and the nightly server's own fix (CampaignLoadFixups) only runs when
    // the campaign reaches its fourth loading state, after a new game has read the Factions XML,
    // so every clan would fail to load. MBSubModuleBase.OnGameStart runs inside
    // Campaign.OnInitialize, before SandBox reads the Factions XML, so the colours load here.
    // Only when they are missing: the server's later check then finds them and does nothing.
    internal static class HeadlessBanners
    {
        private const uint MissingColor = 3735928559u;

        public static void OnGameStart(Game game)
        {
            if (!FreshCampaign.FreshStartActive || game == null || !(game.GameType is Campaign))
                return;
            try
            {
                if (BannerManager.Instance != null && HasBaseColors())
                {
                    SubModule.Log("Fresh campaign: banner colours already loaded before the factions.");
                    return;
                }
                // The server build's Initialize only makes the manager; the colours and icons come
                // from LoadBannerIcons. The game exists now, so it also takes the campaign's XMLs.
                BannerManager.Initialize();
                BannerManager.Instance.LoadBannerIcons();
                SubModule.Log(HasBaseColors()
                    ? "Fresh campaign: banner colours and icons loaded before the factions (headless server)."
                    : "Fresh campaign: banner colours still missing after loading the banner icons; Realm of Thrones' clans may not load.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign: could not load the banner colours before the factions: " + ex);
            }
        }

        // The dedicated server's own check: the base red, blue, green and yellow are known.
        private static bool HasBaseColors() =>
            BannerManager.GetColor(BannerManager.Red) != MissingColor
            && BannerManager.GetColor(BannerManager.Blue) != MissingColor
            && BannerManager.GetColor(BannerManager.Green) != MissingColor
            && BannerManager.GetColor(BannerManager.Yellow) != MissingColor;
    }
}
