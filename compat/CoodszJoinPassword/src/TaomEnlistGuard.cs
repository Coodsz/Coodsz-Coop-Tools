using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Coodsz.EnlistGuard
{
    // TAOM 2.0.29.5 enlists only on the authority: on a Coop client "I swear it" (taom_enlist_oath_swear)
    // returns early, so nothing closes the encounter. The conversation ends with the encounter still open
    // and the vanilla encounter menu starts a battle, so the player attacks the lord. On a Coop client
    // with TAOM this greys out "I wish to enlist under your command" (taom_enlist_offer) with a hint, and
    // as a safety net leaves the encounter after "I swear it", the same way a vanilla "leave" line does.
    // Both use the game's own conversation hooks (the sentence's clickable condition and consequence);
    // TAOM's own code is not patched.
    internal static class TaomEnlistGuard
    {
        private static FieldInfo _sentencesField;
        private static bool _activeNoted;
        private static bool _missingNoted;

        /// <summary>The Join Password module sets this when Coop starts connecting as a client in this game.</summary>
        internal static bool CoopClientStarted;

        /// <summary>After each campaign load or start, once TAOM has added its lines and the manager is built.</summary>
        internal static void OnCampaignReady(Action<string> log)
        {
            try
            {
                if (Campaign.Current?.ConversationManager == null) return;
                bool taom = TaomEnlistGuardRules.TaomLoaded(AppDomain.CurrentDomain.GetAssemblies().Select(SafeName));
                if (!TaomEnlistGuardRules.ShouldGuard(GameNetwork.IsDedicatedServer, CoopServer(), CoopClientStarted, taom)) return;

                _sentencesField = _sentencesField ?? AccessTools.Field(typeof(ConversationManager), "_sentences");
                var sentences = _sentencesField?.GetValue(Campaign.Current.ConversationManager) as List<ConversationSentence>;
                if (sentences == null)
                {
                    NoteMissing(log, "the game's conversation sentence list");
                    return;
                }

                ConversationSentence offer = sentences.FirstOrDefault(s => s != null && s.Id == TaomEnlistGuardRules.OfferId);
                ConversationSentence swear = sentences.FirstOrDefault(s => s != null && s.Id == TaomEnlistGuardRules.SwearId);
                if (swear != null && !(swear.OnConsequence?.Target is LeaveAfterSwear))
                    swear.OnConsequence = new LeaveAfterSwear(swear.OnConsequence).Run;
                if (offer == null)
                {
                    NoteMissing(log, TaomEnlistGuardRules.OfferId + (swear == null ? " and " + TaomEnlistGuardRules.SwearId : ""));
                    return;
                }

                // TAOM's own visibility condition stays; only the click is refused.
                offer.OnClickableCondition = OfferNotClickable;
                if (swear == null) NoteMissing(log, TaomEnlistGuardRules.SwearId);
                if (_activeNoted) return;
                _activeNoted = true;
                log(TaomEnlistGuardRules.ActiveLine);
            }
            catch (Exception e)
            {
                log("TAOM enlist guard could not start: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static bool OfferNotClickable(out TextObject explanation)
        {
            explanation = new TextObject(TaomEnlistGuardRules.Hint);
            return false;
        }

        private sealed class LeaveAfterSwear
        {
            private readonly ConversationSentence.OnConsequenceDelegate _inner;

            internal LeaveAfterSwear(ConversationSentence.OnConsequenceDelegate inner) => _inner = inner;

            internal void Run()
            {
                try
                {
                    _inner?.Invoke();
                }
                finally
                {
                    // The vanilla way out of a lord meeting: the encounter finishes instead of starting a battle.
                    if (PlayerEncounter.Current != null) PlayerEncounter.LeaveEncounter = true;
                }
            }
        }

        private static void NoteMissing(Action<string> log, string what)
        {
            if (_missingNoted) return;
            _missingNoted = true;
            log(TaomEnlistGuardRules.MissingLine(what));
        }

        // Coop's Common.ModInformation.IsServer; false when Coop is not there. The map check guard asks too.
        internal static bool CoopServer()
        {
            try
            {
                Type info = CoodszSettingsSync.CoopProbe.FindLoaded("Common", "Common.ModInformation");
                object value = info?.GetProperty("IsServer", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                return value is bool server && server;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string SafeName(Assembly assembly)
        {
            try { return assembly.IsDynamic ? null : assembly.GetName().Name; }
            catch (Exception) { return null; }
        }
    }
}
