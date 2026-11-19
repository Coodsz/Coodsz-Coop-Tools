#nullable disable
using System.Collections.Generic;
using System.Linq;

namespace RealmOfThronesCoopCompat
{
    // The plain rules of the server guards (RotGuards): the rows of the host's tables they took
    // out of later, the player tests their patches answer, and their log lines. On the server
    // Hero.MainHero and Clan.PlayerClan are the server's own hero and clan, so wherever Realm of
    // Thrones asks "is this the player", a guard asks "is this any player's hero or clan" instead.
    // No game types, so the launcher tests can run them.
    internal static class RotGuardsRules
    {
        public const string Prefix = "Guards: ";
        // Starts every line that means a guard is missing, as the host's own loud lines do.
        public const string Loud = "Guards: !!! ";

        // Realm of Thrones tells White Walkers and wights by race: 1 and 2.
        public const int WhiteWalkerRace = 1;
        public const int WightRace = 2;

        // Coop's own guards, found by name once Coop's assemblies are loaded.
        public const string CoopAiThinkGuardType = "GameInterface.Services.MobilePartyAIs.Patches.SkipPlayerPartyAiThinkPatch";
        public const string CoopAiThinkGuardMethod = "Prefix";
        public const string CoopPrisonerGuardType = "GameInterface.Services.MobileParties.Patches.Disable.RecruitPrisonersCampaignBehaviorPatches";
        public const string CoopPrisonerGuardMethod = "DailyTickAIMobilePartyPrefix";
        public const string CoopPeacePatchType = "GameInterface.Services.Stances.Patches.MakePeaceActionPatch";
        public const string CoopPeacePatchMethod = "Postfix";

        private const string Behaviors = "ROT.CampaignBehaviors.";
        private const string Others = "ROT.HarmonyPatches.Core.OthersPatches";

        // The rows the guards took out of later, as "type" or "type.method". Each one's new
        // answer and reason is its row in RotHostTables.
        public static readonly string[] MovedBehaviors =
        {
            Behaviors + "ROTStorylineWars",
            Behaviors + "ROTCoreBehavior",
            Behaviors + "ROTTroopRecruiter",
            Behaviors + "ROTOthersCampaignBehavior",
            Behaviors + "ROTRelationshipsBehavior",
        };

        // It waited for ROTOthersCampaignBehavior, which is hosted now.
        public const string MovedModel = "ROT.Models.ROTPartySizeLimitModel";

        public static readonly string[] MovedPatches =
        {
            "ROT.SubModule+RecruitPrisonersCampaignBehaviorPatch",
            "ROT.HarmonyPatches.Core.AIThinkPatch",
            "ROT.HarmonyPatches.ModCompatibility.Diplomacy+DeclareWarActionPatch",
            Others + ".patch6",
            Others + ".patch7",
            Others + ".patch9",
            Others + ".patch10",
            Others + ".patch23",
            Others + ".patch27",
        };

        // The table row a moved name stands for: a class row by its type, a method row by type and method.
        public static PatchRow PatchRowOf(IEnumerable<PatchRow> rows, string moved)
        {
            var dot = moved.LastIndexOf('.');
            var method = dot > 0 && moved.Substring(dot + 1).StartsWith("patch") ? moved.Substring(dot + 1) : null;
            var type = method == null ? moved : moved.Substring(0, dot);
            return rows.FirstOrDefault(row => row.Type == type && row.Method == method);
        }

        public static string MovedLine(string kind, string name, HostDecision decision, string why) =>
            Prefix + "moved from later: " + kind + " " + name + " is now " + DecisionName(decision) + ": " + why + ".";

        public static string DecisionName(HostDecision decision) =>
            decision == HostDecision.Host ? "hosted" : decision == HostDecision.Guard ? "run inside a server guard" : decision == HostDecision.Skip ? "skipped" : "not hosted yet";

        public static string SummaryLine(int applied, int failed, int waitingForCoop, int moved) =>
            (failed > 0 ? Loud : Prefix) + applied + " guards in, " + failed + " failed" + (waitingForCoop > 0 ? ", " + waitingForCoop + " wait for Coop's types until a campaign starts" : "")
            + "; " + moved + " rows moved from later." + (failed > 0 ? " A Realm of Thrones rule whose guard failed does not run on this server (the lines above say which)." : "");

        // A hosted rule wrote a culture field Coop does not send to players (plan item S16). One
        // line per write, naming the site and what it wrote, for the builder of that piece.
        public static string CultureWriteLine(string site, string settlementId, string cultureId) =>
            Prefix + "culture write Coop does not send (S16): " + site + " wrote " + CultureFields(site) + " of " + (settlementId ?? "a settlement")
            + " (its culture is " + (cultureId ?? "none") + " now).";

        public static string HeroCultureWriteLine(string site, string heroId, string cultureId) =>
            Prefix + "culture write Coop does not send (S16): " + site + " wrote Hero.Culture of " + (heroId ?? "a hero") + " (" + (cultureId ?? "none") + " now).";

        public static string CultureFields(string site)
        {
            var method = site?.Substring(site.LastIndexOf('.') + 1);
            switch (method)
            {
                case "ChangeToOthersCulture":
                case "RevertBackFromOthersCulture":
                    return "Settlement.Culture (and its villages' too)";
                case "ChangeNotableCulture":
                    return "Hero.Culture and Hero.VolunteerTypes of the notables";
                case "EvacuateHeroes":
                    return "Hero.Culture of the wanderers it turned";
                default:
                    return "a culture";
            }
        }

        // ROTTroopRecruiter.IsHeroManageable on the server: the recruiter swaps an AI lord's troops
        // for his clan's own, and never a player's. Realm of Thrones' own version exempts the one
        // local player, his clan, caravan leaders and, with the My Little Warband setting, the
        // lords of the kingdom he rules. Here every player counts, and the server's own hero and
        // clan stay exempt as they are in Realm of Thrones.
        public static bool RecruiterManages(bool hasHero, bool leadsCaravan, bool playerHero, bool inPlayerClan, bool serverHero, bool inServerClan,
            bool warbandSetting, bool inKingdomRuledByPlayer) =>
            hasHero && !leadsCaravan && !playerHero && !inPlayerClan && !serverHero && !inServerClan && !(warbandSetting && inKingdomRuledByPlayer);

        // OthersPatches.patch7 decides what happens to a hero taken prisoner. A player's hero and a
        // hero of a player's clan are left to the game and Coop's capture. Who took him does not
        // matter: a White Walker lord a player takes is executed, as Realm of Thrones does when the
        // player takes one, and only a White Walker party turns a prisoner.
        public static bool CaptureLeftToCoop(bool prisonerIsPlayer, bool prisonerInPlayerClan) =>
            prisonerIsPlayer || prisonerInPlayerClan;

        // ROTCoreBehavior.DailyTickClan on the server: a poor clan (10000 gold or less) gets its
        // tier times 5000 each day, but never a bandit or mercenary clan, three clans Realm of
        // Thrones names, a player's clan or the server's own. Realm of Thrones reads Hero.MainHero
        // for the last one, which the server does not have, so this rule does not.
        public const int PoorClanGold = 10000;
        public const int GoldPerTier = 5000;
        private static readonly string[] NoGoldClans = { "ROTclan_107", "ROTclan_112", "ROTclan_113" };

        public static int DailyGold(bool hasLeader, bool playerClan, bool serverClan, int gold, bool bandit, bool mercenary, string stringId, int tier) =>
            hasLeader && !playerClan && !serverClan && gold <= PoorClanGold && !bandit && !mercenary && !NoGoldClans.Contains(stringId) ? tier * GoldPerTier : 0;

        // At the Night King's death Realm of Thrones executes every White Walker or wight hero who
        // is not in the White Walker clan, unless he is the player's. Here every player is spared.
        public static bool SparedAtCollapse(bool playerHero, bool inPlayerClan, bool inServerClan) =>
            playerHero || inPlayerClan || inServerClan;

        // Kings protect the Night's Watch: when the Night's Watch and a kingdom ruled by a player's
        // clan go to war (that kingdom not the Free Folk), the Westerosi kingdoms declare war on the
        // first faction of that declaration, as Realm of Thrones does for the player's kingdom.
        // Returns the side (1 or 2) that is the protected player kingdom, which is left out of the
        // defenders, or 0 when the rule does not fire. A player who rules the Night's Watch itself
        // counts too, as in Realm of Thrones, but a player kingdom on the other side comes first.
        public static int ProtectedPlayerSide(bool settingOn, bool nightsWatch1, bool nightsWatch2, bool playerKingdom1, bool playerKingdom2, bool freeFolk1, bool freeFolk2)
        {
            if (!settingOn || (!nightsWatch1 && !nightsWatch2))
                return 0;
            var candidate1 = playerKingdom1 && !freeFolk1;
            var candidate2 = playerKingdom2 && !freeFolk2;
            if (candidate1 && !nightsWatch1)
                return 1;
            if (candidate2 && !nightsWatch2)
                return 2;
            return candidate1 ? 1 : candidate2 ? 2 : 0;
        }
    }
}
