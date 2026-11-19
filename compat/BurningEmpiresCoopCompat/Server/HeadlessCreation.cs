using System;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;

namespace BurningEmpiresCoopCompat
{
    // The new campaign FreshCampaign starts on the dedicated server. Vanilla's new game plays the
    // intro video and pushes the character creation screens in OnLoadFinished, then marks the
    // game loaded while creation is still open. The server has no screens and starts hosting as
    // soon as the game is loaded, so here character creation runs to its end first.
    internal sealed class HeadlessCampaignManager : SandBoxGameManager
    {
        public HeadlessCampaignManager() : base(CreateCampaign)
        {
        }

        private static Campaign CreateCampaign() => new Campaign(CampaignGameMode.Campaign);

        public override void OnLoadFinished()
        {
            HeadlessCreation.Run();
            IsLoaded = true;
        }
    }

    // Character creation without a screen:
    // - The state is built but never pushed, so no screen opens and no stage is activated. The
    //   face generator stage's completion reads the active state, which would not be this one.
    // - Building it raises OnCharacterCreationInitialized, so the game's handler (with Burning
    //   Empires' cultures and menus) fills the content as it does in single player.
    // - A culture with a starting point is picked; no background menus are chosen. The server's
    //   hero is nobody's character and Coop removes its party once the campaign is ready.
    // - ApplyFinalEffects and FinalizeCharacterCreationState then run as after the last stage:
    //   the hero gets the culture and home, the map state is pushed and
    //   OnCharacterCreationIsOver lets Burning Empires set up its world data.
    internal static class HeadlessCreation
    {
        public static void Run()
        {
            var state = new CharacterCreationState();
            var manager = state.CharacterCreationManager;
            var content = manager.CharacterCreationContent;

            var culture = PickCulture(content);
            if (culture == null)
                throw new InvalidOperationException("Character creation offers no culture.");
            content.SetSelectedCulture(culture, manager);
            SubModule.Log("Fresh campaign: headless character creation picked the " + culture.StringId + " culture.");

            try
            {
                manager.ApplyFinalEffects();
            }
            catch (Exception ex)
            {
                // The map can still open without every final effect; the error is kept.
                SubModule.Log("Fresh campaign: a character creation final effect failed: " + ex);
            }

            try
            {
                state.FinalizeCharacterCreationState();
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign: character creation could not finish, so the server cannot open the campaign map: " + ex);
                throw;
            }
            SubModule.Log("Fresh campaign: character creation done; the new campaign is on the map.");
        }

        private static CultureObject PickCulture(CharacterCreationContent content)
        {
            CultureObject first = null;
            foreach (var culture in content.GetCultures())
            {
                if (culture == null)
                    continue;
                first = first ?? culture;
                if (culture.StartingPoint.IsNonZero())
                    return culture;
            }
            return first;
        }
    }
}
