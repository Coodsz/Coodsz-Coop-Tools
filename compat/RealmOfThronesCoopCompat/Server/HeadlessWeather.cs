using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Core;

namespace RealmOfThronesCoopCompat
{
    // No weather on the server's fresh campaign. Weather samples scene textures the headless map
    // does not have, so the nightly server drops MapWeatherCampaignBehavior's listeners just before
    // the campaign's fourth loading state (CampaignLoadFixups). On a loaded game the behaviors have
    // registered by then; on a new game they only register inside that state, in
    // Campaign.OnNewGameCreatedInternal, so the server's removal finds nothing and the weather
    // would start at the session launch. Every module's OnNewGameCreated runs right after the
    // behaviors registered and before OnSessionStart, so the listeners go here, with the same call
    // the server uses.
    internal static class HeadlessWeather
    {
        public static void OnNewGameCreated(Game game)
        {
            if (!FreshCampaign.FreshStartActive || !(game?.GameType is Campaign campaign))
                return;
            try
            {
                var weather = campaign.GetCampaignBehavior<MapWeatherCampaignBehavior>();
                if (weather == null)
                {
                    SubModule.Log("Fresh campaign: no weather behavior to unregister.");
                    return;
                }
                CampaignEventDispatcher.Instance.RemoveListeners(weather);
                SubModule.Log("Fresh campaign: weather behavior unregistered before the session starts (the headless map has no weather data).");
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign: could not unregister the weather behavior: " + ex);
            }
        }
    }
}
