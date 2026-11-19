#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace RealmOfThronesCoopCompat
{
    // What a story popup gets on the server, where nobody can click it. Every popup of Realm of
    // Thrones v8.1.8 has the Continue button only (CancelButtonEnabled is false in all of them and
    // none overrides HandleCancelButton), so Continue is the answer a player who closes it gets.
    // Continue: its HandleDoneButton changes the world. Informational: it changes nothing (no
    // override, an empty one, or code without an effect); the popup is only logged.
    internal enum StoryAnswer { Informational, Continue }

    // One story event of ROT.Events: what its popup's answer does on the server, and what its
    // tests of the one local player (Hero.MainHero, Clan.PlayerClan, MobileParty.MainParty) mean
    // there, where those are the server's own hero, clan and party that no player controls. The
    // server has them from world ready on (live logs: "Host: at world ready Hero.MainHero is
    // main_hero, Clan.PlayerClan is player_faction"); before that Hero.MainHero throws, but no event
    // runs before ROTEventBehavior's first daily tick.
    internal sealed class StoryEventRow
    {
        // The class name in ROT.Events.
        public readonly string Type;
        public readonly StoryAnswer Answer;
        public readonly string Does;
        // Null when the event never tests the local player.
        public readonly string Players;

        public StoryEventRow(string type, StoryAnswer answer, string does, string players = null)
        {
            Type = type;
            Answer = answer;
            Does = does;
            Players = players;
        }
    }

    // The plain rules of the server's story (RotStory): the COODSZ_ROT_STORY switch, the table
    // of every story event with its answer and its player branches, the comparison with the event
    // types ROT.dll really holds and the log lines. No game types, so the launcher tests can run them.
    internal static class RotStoryRules
    {
        public const string Variable = "COODSZ_ROT_STORY";
        public const string Namespace = "ROT.Events.";
        public const string BaseType = Namespace + "EventBase";
        public const string BehaviorType = "ROT.CampaignBehaviors.ROTEventBehavior";
        public const string KingdomsType = "ROT.Misc.ROTKingdoms";
        public const string SettlementsType = "ROT.Misc.ROTSettlements";
        // The invasion whose Borash step has its own Clan.PlayerClan test (RotStory.BorashTranspiler).
        public const string BorashEvent = "DanyInvasionEvent";
        public const string ReceiveItemsType = "Helpers.InventoryScreenHelper";
        // The only button of every popup.
        public const string Answer = "Continue";
        public const string Loud = "Story: !!! ";

        // Not set or "on" means on, "off" means off. Anything else means off too, and unknown
        // says so for the log, as the host's level switch does.
        public static bool On(string variable, out bool unknown)
        {
            unknown = false;
            var value = variable?.Trim();
            if (string.IsNullOrEmpty(value) || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
                return false;
            unknown = true;
            return false;
        }

        // Read once: the host's behavior table is built from it.
        public static readonly bool StoryOn = On(Environment.GetEnvironmentVariable(Variable), out _);

        public static string SwitchLine(string variable)
        {
            var on = On(variable, out var unknown);
            if (unknown)
                return "Story: off. " + Variable + "=" + CrashTraceRules.Shorten(variable.Trim()) + " is neither on nor off, so ROTEventBehavior stays registered with its events off.";
            var set = string.IsNullOrWhiteSpace(variable) ? Variable + " is not set" : Variable + "=" + variable.Trim();
            return on
                ? "Story: on (" + set + "): Realm of Thrones' story events run on the server, each popup gets its default answer without a screen."
                : "Story: off (" + set + "): ROTEventBehavior stays registered with its events off, so the story stands still.";
        }

        // ROTEventBehavior's row in the host's behavior table: hosted with the story on, else kept
        // registered with its events off for its saved data, as before.
        public static HostDecision Decision(bool on) => on ? HostDecision.Host : HostDecision.Later;

        public static string Why(bool on) => on
            ? "the story advances on the server: no popup screen, each popup gets Realm of Thrones' default answer and music is skipped (RotStory)"
            : Variable + "=off: the story stands still, its popups would open a game state only a click closes";

        // The step that swaps King's Landing's scenes for the Battle of the Blackwater and back.
        public const string SceneEvent = "BlackwaterSiegeEvent";
        public const string SceneMethod = "ToggleKingsLandingScene";

        public static readonly string[] Sieges ={ "HarrenhalSiegeEvent", "BlackwaterSiegeEvent", "BastardsSiegeEvent", "WallSiegeEvent" };

        // Their private IsPlayerKing(Clan) is the single player exemption from their hand overs.
        public static readonly string[] Invasions = { "AegonInvasionEvent", "DanyInvasionEvent" };

        // The succession step that moves a kingdom's clans and destroys it, and that kingdom's
        // property of ROT.Misc.ROTKingdoms. In single player the player's clan leaves in rebellion there.
        public static readonly (string Type, string Method, string Kingdom)[] Successions =
        {
            ("RenlyDeathEvent", "ExecuteRenly", "Stormlands"),
            ("StannisDeathEvent", "StartEvent", "Dragonstone"),
        };

        private const string Siege = "no player: the server never pulls its own party into the battle (SetupPlayerEncounter is skipped) and Coop brings players in; "
            + "the muster leaves every player's party and every party in a player's army out and never disbands a player's army (single player leaves out the main party); "
            + "the enlisted branches are dead (enlistment is off)";
        private const string Invasion = "any player: a settlement a player's clan owns or that lies in a kingdom a player rules is not handed over, and a clan in a kingdom a player rules does not defect "
            + "(single player spares the player and the player's kingdom)";

        // Every event in the order ROTEventBehavior adds them.
        public static readonly StoryEventRow[] Events =
        {
            new StoryEventRow("NedEvent", StoryAnswer.Informational, "Ned's death; the war, relation and influence changes run in StartEvent"),
            new StoryEventRow("RenlyDeathEvent", StoryAnswer.Informational, "two popups; Renly's death and the succession run in its day 14 tick",
                "any player: every player's clan in the Stormlands leaves in rebellion before the succession (single player does that to the player's clan); "
                + "it needs the server's own Hero.MainHero with a clan, else the succession is skipped; "
                + "Coop does not replicate the removal of Renly's clan from the campaign's clan list, the link sends it"),
            new StoryEventRow("StannisDeathEvent", StoryAnswer.Informational, "the succession runs in StartEvent before the popup",
                "any player: every player's clan in Dragonstone leaves in rebellion before the succession (single player does that to the player's clan); "
                + "it needs the server's own Hero.MainHero with a clan, else the succession waits a day; "
                + "Coop does not replicate the removal of Stannis' clan from the campaign's clan list, the link sends it"),
            new StoryEventRow("BlackwaterSiegeNotificationEvent", StoryAnswer.Informational, "Stannis sails; his release runs in StartEvent"),
            new StoryEventRow("BlackwaterSiegeEvent", StoryAnswer.Continue, "Joffrey and Lancel hold King's Landing, the town gets its battle scene and Stannis' army forms at the next hourly tick",
                Siege + "; Davos' roster spares CharacterObject.PlayerCharacter, the server's own, no player rides in his party"),
            new StoryEventRow("BlackwaterEvent", StoryAnswer.Continue, "if Stannis lost, 19 clans of Dragonstone change kingdoms"),
            new StoryEventRow("RedWeddingEvent", StoryAnswer.Informational, "two popups; the quest and Robb's execution on day 14 run in StartEvent and its day tick",
                "no player: the quest's success rewards Hero.MainHero and Clan.PlayerClan, but only the rescue mission reaches it and that is off on the server"),
            new StoryEventRow("RooseDeathEvent", StoryAnswer.Informational, "its popup sits behind a tick that never runs"),
            new StoryEventRow("JoffreyEvent", StoryAnswer.Informational, "the Purple Wedding runs in its day tick"),
            new StoryEventRow("EastwatchEvent", StoryAnswer.Informational, "the owner changes run in its day tick"),
            new StoryEventRow("DanyInvasionEvent", StoryAnswer.Informational, "the invasion runs in its day tick",
                Invasion + "; Borash has its own test, the owner only, so it stays with a player's clan that owns it and goes to Yezzan from anyone else, a player's vassal too, as in single player"),
            new StoryEventRow("AegonInvasionEvent", StoryAnswer.Informational, "the invasion runs in its day tick", Invasion),
            new StoryEventRow("RickardKarstarkExecutionEvent", StoryAnswer.Informational, "the execution runs in its day tick"),
            new StoryEventRow("RiverlandsDeclareWarEvent", StoryAnswer.Informational, "the relation changes run in its day tick"),
            new StoryEventRow("JeorMutinyEvent", StoryAnswer.Informational, "the mutiny runs in its day tick"),
            new StoryEventRow("NightKingDeathEvent", StoryAnswer.Informational, "a popup and music only; the server guard on ROTOthersCampaignBehavior's hourly tick starts it at the Night King's death"),
            new StoryEventRow("FistEvent", StoryAnswer.Informational, "a popup and music only"),
            new StoryEventRow("KingsLandingEvent", StoryAnswer.Continue, "the event completes; its reward screen would push a game state, so the server skips it and nobody gets the armour",
                "no player: it starts only when Clan.PlayerClan holds King's Landing and the Red Keep, and the server's own clan holds nothing"),
            new StoryEventRow("WallSiegeNotificationEvent", StoryAnswer.Informational, "Mance marches; his release runs in StartEvent"),
            new StoryEventRow("WallSiegeEvent", StoryAnswer.Continue, "Alliser Thorne and Jon Snow hold the Wall and Mance's army forms at the next hourly tick", Siege),
            new StoryEventRow("WallEvent", StoryAnswer.Informational, "its Continue code has no effect"),
            new StoryEventRow("BastardsSiegeNotificationEvent", StoryAnswer.Informational, "Jon calls the banners; his release and influence run in StartEvent"),
            new StoryEventRow("BastardsSiegeEvent", StoryAnswer.Continue, "Ramsay, Smalljon Umber and Harold Karstark hold Winterfell and Jon's army forms at the next hourly tick", Siege),
            new StoryEventRow("BastardsEvent", StoryAnswer.Continue, "if Jon lost, Karstark and Umber are put back into the North"),
            new StoryEventRow("RamsayEvent", StoryAnswer.Informational, "Roose's murder runs in its day tick"),
            new StoryEventRow("JonEvent", StoryAnswer.Informational, "Jon's kingship runs in its day tick"),
            new StoryEventRow("HarrenhalSiegeNotificationEvent", StoryAnswer.Informational, "Roose strikes a deal; his release runs in StartEvent"),
            new StoryEventRow("HarrenhalSiegeEvent", StoryAnswer.Continue, "Amory Lorch and Polliver hold Harrenhal and Roose Bolton's army forms at the next hourly tick", Siege),
            new StoryEventRow("HarrenhalEvent", StoryAnswer.Informational, "its Continue code has no effect; Harrenhal goes to Roose in StartEvent"),
        };

        public static StoryEventRow Row(string fullName) =>
            fullName != null && fullName.StartsWith(Namespace, StringComparison.Ordinal)
                ? Events.FirstOrDefault(row => row.Type == fullName.Substring(Namespace.Length))
                : null;

        // Every difference between the table and the event types of this ROT.dll (full names of
        // the classes that derive from EventBase and can be made). Empty when they match.
        public static List<string> Compare(IEnumerable<string> eventTypesInDll)
        {
            var dll = new HashSet<string>(eventTypesInDll ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var table = new HashSet<string>(Events.Select(row => Namespace + row.Type), StringComparer.Ordinal);
            var differences = dll.Where(type => !table.Contains(type)).OrderBy(type => type, StringComparer.Ordinal).Select(type => "event " + type + " is not in the story table").ToList();
            differences.AddRange(table.Where(type => !dll.Contains(type)).OrderBy(type => type, StringComparer.Ordinal).Select(type => "event " + type + " of the story table is not in ROT.dll"));
            return differences;
        }

        // The one line at game start: what the table knows, how ROT.dll compares and the state of
        // this world's event list.
        public static string SummaryLine(IList<string> differences, int typesInDll, int listed, int done, int active)
        {
            var known = Events.Length + " events known, " + Events.Count(row => row.Answer == StoryAnswer.Continue) + " with a default answer, "
                + Events.Count(row => row.Answer == StoryAnswer.Informational) + " informational, " + Events.Count(row => row.Players != null) + " with player branches";
            var world = "; this world lists " + listed + " events (" + done + " done, " + active + " active).";
            return differences == null || differences.Count == 0
                ? "Story: " + known + "; ROT.dll has " + typesInDll + " event types, all in the table" + world
                : Loud + known + "; ROT.dll HAS " + typesInDll + " EVENT TYPES AND DIFFERS FROM THE TABLE (" + differences.Count + "): " + string.Join("; ", differences)
                    + ". An event the table does not know still gets " + Answer + " for its popups" + world;
        }

        // The line for one popup the server answered. popup counts this event's popups in this run.
        public static string PopupLine(string type, int popup, string title, StoryEventRow row)
        {
            var name = "event " + type + (popup <= 1 ? " started" : " popup " + popup);
            var shown = string.IsNullOrWhiteSpace(title) ? "" : " (\"" + CrashTraceRules.Shorten(title.Trim()) + "\")";
            if (row == null)
                return Loud + name + shown + " is not in the story table (another Realm of Thrones version?): answer " + Answer + " applied, as for every popup.";
            return row.Answer == StoryAnswer.Continue
                ? "Story: " + name + shown + ", answer " + Answer + " applied: " + row.Does + "."
                : "Story: " + name + shown + ", informational popup logged and closed (" + row.Does + ").";
        }
    }
}
