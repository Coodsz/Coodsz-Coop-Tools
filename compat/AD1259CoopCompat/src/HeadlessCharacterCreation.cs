// Completes sandbox character creation on the dedicated server, which has no screen and no player.
//
// The server's main hero is only a placeholder (each Coop player creates their own hero when joining),
// so it gets a culture that owns a town on the active map and otherwise the game's own defaults. The
// sequence mirrors what the character-creation screens do, using the v1.4.8 API of the server's own
// TaleWorlds.CampaignSystem (CharacterCreationState / CharacterCreationManager / CharacterCreationContent):
//   culture stage      -> CharacterCreationContent.SetSelectedCulture
//   face generator     -> confirmed unchanged
//   narrative stage    -> first suitable option of every backstory menu (the game applies their effects)
//   banner editor      -> confirmed unchanged
//   clan naming        -> SetMainCharacterName("Dedicated Server"), the name Coodsz's server add-on uses
//   review, options    -> confirmed unchanged
// After the last stage the game itself applies the culture, moves the main party to the culture's
// starting point and switches to the campaign map (CharacterCreationManager.NextStage ->
// CharacterCreationState.FinalizeCharacterCreationState).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace AD1259CoopCompat
{
    internal static class HeadlessCharacterCreation
    {
        internal const string ServerHeroName = "Dedicated Server";

        private static readonly MethodInfo LaunchMethod =
            AccessTools.Method(typeof(SandBoxGameManager), "LaunchSandboxCharacterCreation");

        // Declared on MBGameManager (TaleWorlds.MountAndBlade), with a protected setter.
        private static readonly MethodInfo IsLoadedSetter =
            AccessTools.PropertySetter(typeof(MBGameManager), "IsLoaded");

        public static bool IsSupported
        {
            get { return LaunchMethod != null && IsLoadedSetter != null; }
        }

        /// <summary>Runs character creation to the campaign map; returns a one-line summary or throws.</summary>
        public static string Run(SandBoxGameManager gameManager)
        {
            LaunchMethod.Invoke(gameManager, null);

            CharacterCreationState state = ActiveState() as CharacterCreationState;
            if (state == null)
            {
                throw new InvalidOperationException("character creation did not start; the active game state is " + StateName());
            }

            CharacterCreationManager manager = state.CharacterCreationManager;
            var content = manager.CharacterCreationContent;
            var stages = new List<string>();
            int limit = manager.GetTotalStagesCount() + 2;
            for (int step = 0; step < limit && ActiveState() is CharacterCreationState; step++)
            {
                CharacterCreationStageBase stage = manager.CurrentStage;
                stages.Add(stage == null ? "(none)" : stage.GetType().Name.Replace("CharacterCreation", string.Empty));
                if (stage is CharacterCreationCultureStage)
                {
                    CultureObject culture = ChooseCulture(content);
                    content.SetSelectedCulture(culture, manager);
                    Log.Info("placeholder hero culture: " + culture.StringId + ".");
                }
                else if (stage is CharacterCreationNarrativeStage)
                {
                    CompleteNarrative(manager);
                }
                else if (stage is CharacterCreationClanNamingStage)
                {
                    content.SetMainCharacterName(ServerHeroName);
                }
                else if (!(stage is CharacterCreationFaceGeneratorStage || stage is CharacterCreationBannerEditorStage ||
                           stage is CharacterCreationReviewStage || stage is CharacterCreationOptionsStage))
                {
                    Log.Warn("character-creation stage " + (stage == null ? "(none)" : stage.GetType().FullName) +
                             " is not known to the patch; it is confirmed with its defaults.");
                }
                manager.NextStage();
            }

            if (ActiveState() is CharacterCreationState)
            {
                throw new InvalidOperationException("character creation did not finish after " + limit + " stages (" +
                                                    string.Join(" > ", stages) + ")");
            }
            if (!(ActiveState() is MapState))
            {
                throw new InvalidOperationException("character creation finished but the game is in " + StateName() +
                                                    ", not on the campaign map");
            }
            if (Campaign.Current == null || Hero.MainHero == null || MobileParty.MainParty == null)
            {
                throw new InvalidOperationException("character creation finished without a campaign, main hero or main party");
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{0} settlements, {1} kingdoms, {2} living heroes; placeholder hero '{3}' ({4}); stages {5}",
                Settlement.All.Count, Kingdom.All.Count, Campaign.Current.AliveHeroes.Count, ServerHeroName,
                Hero.MainHero.Culture == null ? "no culture" : Hero.MainHero.Culture.StringId, string.Join(" > ", stages));
        }

        /// <summary>Marks the game manager loaded, which SandBoxGameManager.OnLoadFinished does at its end.</summary>
        public static void MarkLoaded(SandBoxGameManager gameManager)
        {
            IsLoadedSetter.Invoke(gameManager, new object[] { true });
        }

        private static CultureObject ChooseCulture(TaleWorlds.CampaignSystem.CharacterCreationContent.CharacterCreationContent content)
        {
            var withTown = new HashSet<CultureObject>(Settlement.All
                .Where(s => s != null && s.IsTown && s.Culture != null)
                .Select(s => s.Culture));

            List<CultureObject> offered = content.GetCultures().Where(c => c != null).ToList();
            if (offered.Count == 0)
            {
                // No module registered a character-creation culture; offer the main cultures that own a town,
                // with the game's default bonuses, so the culture stage has something to select.
                foreach (CultureObject culture in MBObjectManager.Instance.GetObjectTypeList<CultureObject>()
                             .Where(c => c != null && c.IsMainCulture && withTown.Contains(c)))
                {
                    content.AddCharacterCreationCulture(culture, 1, 10);
                    offered.Add(culture);
                }
                Log.Warn("no culture was registered for character creation; offering the " + offered.Count +
                         " main cultures that own a town.");
            }
            if (offered.Count == 0)
            {
                throw new InvalidOperationException("no culture is available for the server's placeholder hero");
            }

            string wanted = Environment.GetEnvironmentVariable("AD1259COOP_START_CULTURE");
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                CultureObject match = offered.FirstOrDefault(c =>
                    string.Equals(c.StringId, wanted.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
                Log.Warn("AD1259COOP_START_CULTURE=" + wanted + " is not offered; choices: " +
                         string.Join(", ", offered.Select(c => c.StringId)));
            }

            // A culture that owns a town and has a starting point puts the placeholder party somewhere real on the map.
            return offered.FirstOrDefault(c => withTown.Contains(c) && c.StartingPoint.IsNonZero())
                   ?? offered.FirstOrDefault(withTown.Contains)
                   ?? offered[0];
        }

        // Picks the first option each backstory menu offers for the chosen culture, as a player clicking through would.
        private static void CompleteNarrative(CharacterCreationManager manager)
        {
            if (manager.CharacterCreationMenuCount == 0) return;
            string menuId = "(start)";
            try
            {
                manager.StartNarrativeStage();
                for (int i = 0; i <= manager.CharacterCreationMenuCount; i++)
                {
                    NarrativeMenu menu = manager.CurrentMenu;
                    if (menu == null) return;
                    menuId = menu.StringId;
                    NarrativeMenuOption option = manager.GetSuitableNarrativeMenuOptions().FirstOrDefault();
                    if (option == null)
                    {
                        Log.Warn("backstory menu '" + menuId + "' offers no option for this culture; the rest of the " +
                                 "backstory keeps its defaults.");
                        return;
                    }
                    manager.OnNarrativeMenuOptionSelected(option);
                    if (!manager.TrySwitchToNextMenu()) return;
                }
                Log.Warn("the backstory menus did not end after " + manager.CharacterCreationMenuCount + " choices; continuing.");
            }
            catch (Exception e)
            {
                // Drop half-applied choices; the placeholder hero then simply keeps the default skills.
                manager.ResetMenuOptions();
                Log.Warn("backstory menu '" + menuId + "' failed (" + e.GetType().Name + ": " + e.Message +
                         "); the placeholder hero keeps the default skills.");
            }
        }

        private static TaleWorlds.Core.GameState ActiveState()
        {
            GameStateManager manager = Game.Current != null ? Game.Current.GameStateManager : null;
            TaleWorlds.Core.GameState state = manager == null ? null : manager.ActiveState;
            return state ?? (GameStateManager.Current == null ? null : GameStateManager.Current.ActiveState);
        }

        private static string StateName()
        {
            TaleWorlds.Core.GameState state = ActiveState();
            return state == null ? "(none)" : state.GetType().Name;
        }
    }
}
