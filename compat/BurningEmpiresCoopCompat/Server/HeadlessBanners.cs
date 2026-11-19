using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BurningEmpiresCoopCompat
{
    // Banner colours before Burning Empires' factions load on the server's fresh campaign.
    // Every clan in the Factions XML reads its banner colour from BannerManager while it loads.
    // On a PC, TaleWorlds.MountAndBlade.View makes BannerManager and loads its colours and icons
    // while it loads, but the headless dedicated server never loads that module. The server's
    // own fix runs after the Factions XML is read, so every clan failed to load, the clan list
    // came out empty and the server died after making its heroes.
    // MBSubModuleBase.OnGameStart runs inside Campaign.OnInitialize, before SandBox reads the
    // Factions XML, so the colours and icons are loaded there the way the view module does.
    // The server's later check finds the colours already there and does nothing.
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
                    return;
                // Same calls as ViewSubModule.InitializeBannerVisualManager. The game exists now,
                // so LoadBannerIcons also takes the XMLs meant for the campaign game type.
                BannerManager.Initialize();
                BannerManager.Instance.LoadBannerIcons();
                SubModule.Log(HasBaseColors()
                    ? "Fresh campaign: banner colours and icons loaded before the factions (headless server)."
                    : "Fresh campaign: banner colours still missing after loading the banner icons; Burning Empires' clans may not load.");
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
