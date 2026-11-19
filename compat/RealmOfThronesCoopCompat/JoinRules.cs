#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace RealmOfThronesCoopCompat
{
    // Plain rules for a player joining a Realm of Thrones server: Coop's session flags, the
    // client's gate on Realm of Thrones' bandit leader code and the join probe's log lines, without
    // game types, so the launcher tests can run them. The probe lines read the same on the server
    // and on a player's PC when the content is the same, so the two logs compare line by line.
    internal static class JoinRules
    {
        public const string WhiteWalkerCulture = "whitewalker";
        // Realm of Thrones 8.1.8: the White Walker clan (ROT-Content spclans.xml, culture
        // whitewalker) and one NPC clan it should be at war with, House Borrell of Sisterton, the
        // owner of town_S1 (ROT-Map settlements.xml).
        public const string WhiteWalkerClanId = "ROTclan_126";
        public const string SampleNpcClanId = "ROTclan_11";

        // A player's PC in a session. The dedicated server is never one, even when Coop's server
        // flag is missing or still false.
        public static bool OnPlayerPc(bool inSession, bool isServer, bool dedicatedProcess) =>
            inSession && !isServer && !dedicatedProcess;

        // A switch that turns a protection off for a test (COODSZ_ROT_PATIENCE, COODSZ_ROT_SHIPS):
        // only the exact value "off" (any case, trimmed) turns it off. Anything else, "0" or a typo,
        // keeps the protection on, and notUnderstood says so for one log line.
        public static bool ProtectionOn(string variable, out bool notUnderstood)
        {
            var value = (variable ?? "").Trim();
            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            {
                notUnderstood = false;
                return false;
            }
            notUnderstood = value.Length > 0 && !string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        public static string NotUnderstoodLine(string name, string variable, string what) =>
            what + ": " + name + "=" + CrashTraceRules.Shorten((variable ?? "").Trim()) + " is not understood (only \"off\" turns it off), so it stays on.";

        // CoopPatience's switch for a test: COODSZ_ROT_PATIENCE=off leaves Coop's own blocking
        // game thread wait and the server's join limit as they are.
        public const string PatienceVariable = "COODSZ_ROT_PATIENCE";

        public static bool PatienceOn(string variable) => ProtectionOn(variable, out _);

        public static string PatienceOffLine(string variable, string coopWait) =>
            "Coop patience: off (" + PatienceVariable + "=" + CrashTraceRules.Shorten((variable ?? "").Trim()) + "); Coop's own wait applies: " + (coopWait ?? "unreadable") + ".";

        // The server's join watchdog (Coop's NetworkJoinLimits.ReplayAppliedTimeout, 5 minutes)
        // drops a joining player who applies no replay for that long. A first join compiles
        // Realm of Thrones' terrain shaders for minutes after CampaignReady (live test 2026-11-11:
        // 5 minutes, dropped with JoinReplayAppliedTimeout), so Realm of Thrones servers wait 15
        // minutes, as long as Coop's own campaign entry limit. A longer limit of Coop's is kept.
        public static readonly TimeSpan JoinWait = TimeSpan.FromMinutes(15);

        public static TimeSpan ServerJoinWait(TimeSpan coop) => coop > JoinWait ? coop : JoinWait;

        // How often Coop's AbortStalledJoiners reads the limit today (the check and its log line).
        // Any other count means Coop changed the method, and the patch is taken off again.
        public const int ServerJoinReads = 2;

        // applied: the patch moved exactly the reads Coop has today and stays; otherwise it was taken
        // off again and Coop's own limit applies.
        public static string ServerPatienceLine(TimeSpan coop, TimeSpan wait, int reads, bool applied) =>
            applied
                ? "Coop patience (server): a joining player may apply nothing for " + Seconds(wait) + " before the server drops the join (Coop's own limit " + Seconds(coop)
                    + ", JoinReplayAppliedTimeout; " + reads + " reads in OverloadedPeerManager.AbortStalledJoiners now read ours), so a first join that compiles shaders gets in."
                : "!!! Coop patience (server): OverloadedPeerManager.AbortStalledJoiners reads NetworkJoinLimits.ReplayAppliedTimeout " + reads + " time(s), not the "
                    + ServerJoinReads + " this module knows, so Coop changed it; the patch is taken off again, Coop's own limit of "
                    + Seconds(coop) + " applies and a first join that compiles shaders for longer is dropped.";

        public static string ServerPatienceOffLine(string variable, TimeSpan coop) =>
            "Coop patience (server): off (" + PatienceVariable + "=" + CrashTraceRules.Shorten((variable ?? "").Trim()) + "); Coop's own join limit applies: " + Seconds(coop) + ".";

        public static string ServerPatienceMissingLine(string what) =>
            "!!! Coop patience (server): " + what + " not found, so Coop's own join limit applies and a first join that compiles shaders for over 5 minutes is dropped.";

        // The players' side of the same limit: while a join's replay is applied, Coop gives blocking
        // game thread work this long (CampaignState's frame drain), whatever BlockingTimeout says.
        // now is the field read back after it was set.
        public static string ClientJoinWaitLine(TimeSpan before, TimeSpan now) =>
            now >= JoinWait
                ? "Coop patience: NetworkJoinLimits.ReplayAppliedTimeout reads " + Seconds(now) + " after setting it (was " + Seconds(before) + "), so while a join catches up Coop waits that long for blocking game thread work."
                : "!!! Coop patience: NetworkJoinLimits.ReplayAppliedTimeout still reads " + Seconds(now) + " after setting it, so while a join catches up Coop waits only that long for blocking game thread work.";

        private static string Seconds(TimeSpan span) => ((long)span.TotalSeconds) + " s";

        // Coop's server flag as the module uses it. On the dedicated server process a missing or
        // false flag inside a session is an error worth one line, and the process is the server.
        public static bool IsServer(bool dedicatedProcess, bool? flag, bool inSession, out bool reportError)
        {
            reportError = dedicatedProcess && inSession && flag != true;
            return dedicatedProcess || flag == true;
        }

        public enum CoopState
        {
            // Coop's container and server flag are loaded (or, see below, taken as there).
            Ready,
            // Nothing settled yet; ask again later.
            LookAgain,
            // No Coop in this game, for the whole run.
            NoCoop,
        }

        // Coop.cs's resolve step. Ready once the container and the server flag are both loaded.
        // While a Coop module is active, or on the dedicated server, it looks again until then,
        // because Coop's assemblies load one by one and late. Otherwise, once every module is
        // loaded, Coop's assemblies present means Ready (a running session on a player's PC is the
        // safe guess) and none means no Coop.
        public static CoopState ResolveCoop(bool coopSeen, bool hasContainer, bool hasServerFlag, bool server, bool modulesLoaded, bool coopModuleActive)
        {
            if (hasContainer && hasServerFlag)
                return CoopState.Ready;
            if (server || coopModuleActive || !modulesLoaded)
                return CoopState.LookAgain;
            return coopSeen ? CoopState.Ready : CoopState.NoCoop;
        }

        public enum Gate
        {
            Run,
            Skip,
        }

        // Realm of Thrones' OnAfterGameInitializationFinished makes a special hero and the leader of
        // every bandit clan, on every start and load. On a player's PC in the server's world that is
        // a world change the server never hears of, so it is skipped when every bandit clan already
        // has a living leader from the server. With a leader missing it runs (a local leader is a
        // smaller harm than none) and says so loudly. Outside a session, and in the throwaway
        // character creation campaign of a join, it always runs. Line is null when there is
        // nothing to say.
        public static Gate BanditLeaderGate(bool playerPcInSession, bool? loadingSavedGame, bool everyBanditClanLed, out string line)
        {
            line = null;
            if (!playerPcInSession)
                return Gate.Run;
            if (loadingSavedGame != true)
            {
                line = "Load gate: Realm of Thrones' bandit leader code runs in the character creation campaign, a throwaway world.";
                return Gate.Run;
            }
            if (everyBanditClanLed)
            {
                line = "Load gate: Realm of Thrones' bandit leader code skipped in the server's world, every bandit clan already has a living leader.";
                return Gate.Skip;
            }
            line = "!!! Load gate: a bandit clan in the server's world has no living leader, so Realm of Thrones' code runs and makes a leader only this PC knows about.";
            return Gate.Run;
        }

        // The White Walker and wight player start is off in the first co-op version (owner's call):
        // on a player's PC in a session, character creation does not offer the whitewalker culture.
        // Only in the creation campaign (not the server's world, LoadingSavedGame true); single
        // player and outside a session nothing changes.
        public static bool HidesCulture(bool playerPcInSession, bool? loadingSavedGame, string cultureId) =>
            playerPcInSession && loadingSavedGame != true && string.Equals(cultureId, WhiteWalkerCulture, StringComparison.OrdinalIgnoreCase);

        // A world the server made headless stores 0 for both map CRCs, so a joiner's
        // Campaign.CheckMapUpdate would always see a changed map and re-place every settlement,
        // map event, army and party on that PC only. In the server's world on a player's PC the
        // sweep is skipped.
        public static bool SkipsMapSweep(bool playerPcInSession, bool? loadingSavedGame) =>
            playerPcInSession && loadingSavedGame == true;

        public enum ShipBuild
        {
            // Let NavalDLC's AddShipVisual make or refresh the party's ship.
            Build,
            // No ship yet and the party is not visible to this player now: held until it is.
            NotVisible,
            // This frame has made its share of new ships: held for a later frame.
            OverBudget,
            // The checks could not be read: held until they can.
            Unreadable,
        }

        // New ship builds let through per frame.
        public const int ShipBuildsPerFrame = 4;

        // The ship visual budget on a player's PC in the server's world (boot of the first join: the
        // transferred save marks every party visible, NavalDLC builds a ship for every party at sea
        // in the first map frame, and the client died at about 180,000 ship entities). It decides
        // only whether a party's ship is made (AddShipVisual); fading and clean up always run.
        // - A visual that already has its ship always goes on: refreshing it holds nothing back
        //   worth holding, and its party may need it now.
        // - The player's own party always goes on.
        // - Otherwise "visible" is Coop's fog of war once its PartyVisibilitySweep has really run
        //   (SweepRan), and until then the party being within the main party's seeing range (the
        //   save's flag says visible for everyone, so it is no answer before the sweep).
        // - At most ShipBuildsPerFrame new ships per frame. A held party waits and is marked dirty
        //   again once this rule lets it through, so a visible party gets its ship a few frames
        //   later at most.
        // - A party within the main party's seeing range is always in view, also after the sweep (live
        //   test 2026-11-11: no ship was ever let through, since the sweep's run before the map opened
        //   was forgotten and the flag alone then decided).
        public static ShipBuild ShipVisualRule(bool hasShip, bool isMainParty, bool sweepDone, bool visibleFlag, bool withinSight, int builtThisFrame)
        {
            if (hasShip || isMainParty)
                return ShipBuild.Build;
            var visibleNow = withinSight || (sweepDone && visibleFlag);
            if (!visibleNow)
                return ShipBuild.NotVisible;
            return builtThisFrame >= ShipBuildsPerFrame ? ShipBuild.OverBudget : ShipBuild.Build;
        }

        // A frame or party whose checks threw: no new ship but the player's own, never a build
        // for everything (the crash this budget is there for).
        public static ShipBuild ShipVisualRuleUnreadable(bool hasShip, bool isMainParty) =>
            hasShip || isMainParty ? ShipBuild.Build : ShipBuild.Unreadable;

        // Coop's sweep returns without work when there is no campaign or the main party has no
        // valid position; only a sweep that could run replaces the saved visible flags.
        public static bool SweepRan(bool campaign, bool mainPartyPositionValid) => campaign && mainPartyPositionValid;

        public static string ShipBudgetLine(int built, int heldNotVisible, int heldOverBudget, string entities) =>
            "Ship visuals: in the first 10 s on the map " + built + " ship visuals were let through to be built, " + heldNotVisible
            + " held back as not visible to this player and " + heldOverBudget + " delayed by the budget of " + ShipBuildsPerFrame + " a frame; the map scene has "
            + (entities ?? "an unread number of") + " root entities.";

        // COODSZ_ROT_SHIPS=off removes the budget for a test: NavalDLC builds every ship it asks for,
        // and the mass build at the join that crashed the first test can come back.
        public const string ShipsVariable = "COODSZ_ROT_SHIPS";

        public static bool ShipsOn(string variable) => ProtectionOn(variable, out _);

        // The budget's decision when a Game's first map tick comes. sweepGameIsThisGame: Coop's sweep
        // ran for this very Game (it runs at the hero switch, before the map opens, and is kept);
        // a sweep of another Game counts for nothing. The budget holds only on a player's PC loading
        // the server's world with the switch on; otherwise every ship goes to NavalDLC as it is.
        public static (bool Active, bool SweepDone) ShipGameStart(bool sweepGameIsThisGame, bool playerPcJoin, bool switchOn) =>
            (playerPcJoin && switchOn, sweepGameIsThisGame);

        public static string ShipsOffLine(string variable) =>
            "Ship visuals: off (" + ShipsVariable + "=" + CrashTraceRules.Shorten((variable ?? "").Trim()) + "): NavalDLC builds every ship visual it asks for, as without this module; "
            + "a join can crash again building hundreds at once.";

        // While on the map, at most once a minute and only when something happened since the last line.
        public const int ShipReportSeconds = 60;

        public static bool ShipReportDue(double secondsSinceLast, int built, int heldNotVisible, int delayed) =>
            secondsSinceLast >= ShipReportSeconds && built + heldNotVisible + delayed > 0;

        // The ship test's proof of whether a fleet ever came near: of the ships held back as not in
        // view since the last line, the nearest one's distance to the main party and its IsVisible
        // flag, and how many came within twice the seeing range (nearest null: none held back);
        // then whether the main party is at sea and whether it is night (null: could not be read).
        public static string ShipMinuteLine(int built, int heldNotVisible, int delayed, int waiting, float sightRange, bool sweepDone,
            float? nearestHeld, int heldNearby, bool nearestVisible, bool? atSea, bool? night) =>
            "Ship visuals since the last line: " + built + " let through, " + heldNotVisible + " held back as not in view, " + delayed + " delayed by the budget; "
            + waiting + " wait for their ship; seeing range " + Round(sightRange)
            + ", Coop's visibility sweep " + (sweepDone ? "has run" : "has not run") + "; "
            + (nearestHeld == null
                ? "no ship held back as not in view"
                : "nearest waiting ship " + Round(nearestHeld.Value) + " away (" + heldNearby + " within " + Round(sightRange * 2) + "), " + (nearestVisible ? "visible" : "not visible"))
            + "; " + (atSea == null ? "at sea or on land unread" : atSea.Value ? "you are at sea" : "you are on land")
            + "; " + (night == null ? "day or night unread" : night.Value ? "night" : "day") + ".";

        private static string Round(float value) => Math.Round(value, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        // "+81 s" since the session started, for the join probe's timing lines.
        public static string SinceSession(double? seconds) =>
            seconds == null ? "" : " (+" + Math.Round(seconds.Value) + " s since the Coop session started)";

        public static bool EveryClanLed(IEnumerable<(string Clan, string Leader, bool Alive)> clans)
        {
            var list = (clans ?? Enumerable.Empty<(string, string, bool)>()).ToList();
            return list.Count > 0 && list.All(clan => clan.Leader != null && clan.Alive);
        }

        // "Join probe: bandit clans 7, all with a living leader (lord_x)." or which have none.
        public static string BanditLine(IEnumerable<(string Clan, string Leader, bool Alive)> clans)
        {
            var list = (clans ?? Enumerable.Empty<(string, string, bool)>()).ToList();
            if (list.Count == 0)
                return "Join probe: bandit clans 0.";
            var leaders = list.Where(clan => clan.Leader != null).Select(clan => clan.Leader + (clan.Alive ? "" : " dead")).Distinct().ToList();
            var unled = list.Where(clan => clan.Leader == null || !clan.Alive).Select(clan => clan.Clan).ToList();
            return "Join probe: bandit clans " + list.Count + ", "
                + (unled.Count == 0 && list.Count > 0 ? "all with a living leader" : unled.Count + " without a living leader (" + string.Join(", ", unled.Take(10)) + ")")
                + "; leaders " + (leaders.Count == 0 ? "none" : string.Join(", ", leaders.Take(10))) + ".";
        }

        public static string WorldLine(string date, int year, int heroes, int clans, int kingdoms, int settlements) =>
            "Join probe: world on " + date + " (year " + year + "): heroes " + heroes + ", clans " + clans + ", kingdoms " + kingdoms + ", settlements " + settlements + ".";

        public static string RacesLine(IList<string> races) =>
            "Join probe: races " + (races?.Count ?? 0) + ": " + (races == null ? "" : string.Join(", ", races)) + ".";

        public static string ModulesLine(IEnumerable<KeyValuePair<string, string>> modules) =>
            "Join probe: modules " + string.Join(", ", (modules ?? Enumerable.Empty<KeyValuePair<string, string>>()).Select(module => module.Key + " " + module.Value)) + ".";

        public static string WalkerWarLine(string walkerClan, string npcClan, bool? atWar) =>
            "Join probe: White Walker clan " + (walkerClan ?? "not found") + " at war with " + npcClan + ": "
            + (atWar == null ? "unknown" : atWar.Value ? "yes" : "no") + ".";

        public static string PackageLine(int bytes, string culture, int race) =>
            "Join probe: hero package " + bytes + " bytes, culture " + (culture ?? "none") + ", race " + race + ".";
    }
}
