using System;
using System.Collections.Generic;
using System.Linq;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RealmOfThronesCoopCompat
{
    // The new campaign FreshCampaign starts on the dedicated server, adapted from Burning Empires
    // Coop Compat. Vanilla's new game plays the intro video and pushes the character creation
    // screens in OnLoadFinished, then marks the game loaded while creation is still open. The
    // server has no screens and starts hosting as soon as the game is loaded, so here character
    // creation runs to its end first, without the base method.
    internal sealed class HeadlessCampaignManager : SandBoxGameManager
    {
        public HeadlessCampaignManager() : base(CreateCampaign)
        {
        }

        private static Campaign CreateCampaign() => new Campaign(CampaignGameMode.Campaign);

        private bool _finished;

        // The loading state calls this on each of its ticks until another state replaces it;
        // character creation pushes the map state, so it runs once, and never twice.
        public override void OnLoadFinished()
        {
            if (_finished)
                return;
            _finished = true;
            SubModule.Log("Fresh campaign: the new campaign loaded; character creation runs headless now.");
            HeadlessCreation.Run();
            // The world gate: a world that misses a floor is held back at full (never marked
            // loaded, so the server never serves or saves it).
            if (WorldCheck.Passes())
                IsLoaded = true;
        }
    }

    // The new world against floors from Realm of Thrones' data, in one line a tool can wait for
    // (FreshCampaignRules.WorldCheckFailures). True when the world may be marked loaded.
    internal static class WorldCheck
    {
        // A check that cannot run counts as failed (FreshCampaignRules.RunWorldCheck).
        public static bool Passes()
        {
            var asked = RotHostRules.Level(Environment.GetEnvironmentVariable(RotRules.HostVariable), out _);
            return FreshCampaignRules.RunWorldCheck(() => new FreshCampaignRules.WorldNumbers
            {
                Kingdoms = Kingdom.All.Count,
                Clans = Clan.All.Count,
                Settlements = Settlement.All.Count,
                UnownedFiefs = Settlement.All.Where(settlement => settlement != null && (settlement.IsTown || settlement.IsCastle) && settlement.OwnerClan == null)
                    .Select(settlement => settlement.StringId).ToList(),
                Year = CampaignTime.Now.GetYear,
                TimeModelRegistered = RotHost.TimeModelRegistered,
                HostFailures = RotHost.AllFailures(),
            }, FreshCampaign.ExpectedSettlements, asked, Environment.GetEnvironmentVariable(FreshCampaignRules.WorldCheckVariable), SubModule.Log);
        }
    }

    // Character creation without a screen:
    // - The state is built but never pushed, so no screen opens and no stage is activated. The
    //   face generator stage's completion reads the active state, which would not be this one.
    // - Building it raises OnCharacterCreationInitialized, so the game's handler fills the
    //   content (cultures and menus) as it does in single player.
    // - A culture with a starting point is picked; no background menus are chosen. The server's
    //   hero is nobody's character and Coop removes its party once the campaign is ready.
    // - ApplyFinalEffects and FinalizeCharacterCreationState then run as after the last stage:
    //   the hero gets the culture and home, the map state is pushed and OnCharacterCreationIsOver
    //   lets every campaign behavior set up its world data.
    internal static class HeadlessCreation
    {
        public static void Run()
        {
            var state = new CharacterCreationState();
            var manager = state.CharacterCreationManager;
            var content = manager.CharacterCreationContent;
            var cultures = content.GetCultures().Where(culture => culture != null).ToList();
            SubModule.Log("Fresh campaign: character creation offers " + cultures.Count + " culture(s): "
                + string.Join(", ", cultures.Select(culture => culture.StringId + (culture.StartingPoint.IsNonZero() ? "" : " (no starting point)"))) + ".");

            var index = FreshCampaignRules.PickCulture(cultures.Select(culture => new KeyValuePair<string, bool>(culture.StringId, culture.StartingPoint.IsNonZero())).ToList(), out var rule);
            if (index < 0)
            {
                SubModule.Log("Fresh campaign: character creation offers no culture the server can use (" + rule + "), so the server cannot open the campaign map.");
                throw new InvalidOperationException("Character creation offers no usable culture.");
            }
            var picked = cultures[index];
            content.SetSelectedCulture(picked, manager);
            SubModule.Log("Fresh campaign: headless character creation picked the " + picked.StringId + " culture (" + rule + ").");

            try
            {
                manager.ApplyFinalEffects();
                SubModule.Log("Fresh campaign: character creation final effects applied.");
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
            LogWorld();
        }

        // What the new world looks like, for the boot log.
        private static void LogWorld()
        {
            try
            {
                var now = CampaignTime.Now;
                var ownerless = Settlement.All.Count(settlement => settlement.OwnerClan == null);
                SubModule.Log("Fresh campaign: the world on " + now + " (year " + now.GetYear + ") has " + Hero.AllAliveHeroes.Count + " living heroes, "
                    + Clan.All.Count + " clans, " + Kingdom.All.Count + " kingdoms, " + MobileParty.All.Count + " parties and " + Settlement.All.Count
                    + " settlements, " + ownerless + " of them without an owner clan.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign: could not count the new world: " + ex.Message);
            }
        }
    }
}
