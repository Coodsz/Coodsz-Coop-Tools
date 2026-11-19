#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RealmOfThronesCoopCompat
{
    // Plain rules for the gates on a player's PC: which Realm of Thrones methods stop there, when,
    // the prefixes that stop them, the steps for Realm of Thrones' PlayerEncounter.Finish prefix,
    // the OFF messages and the log lines. No game or Harmony types, so the launcher tests run them.
    // ClientGates applies the tables by name once ROT.dll is loaded; ClientDisable does the rest.
    internal static class ClientRules
    {
        // World: a player's PC in the server's world (not the throwaway character creation campaign
        // of a join). Session: a player's PC in any campaign of a Coop session.
        public enum Scope
        {
            World,
            Session,
        }

        // Skip: the method does not run (a skipped bool method answers false). RunOriginal: a
        // Realm of Thrones bool prefix does not run and answers true, so the game method it sits
        // on runs as it would without Realm of Thrones (and Coop's own patches decide).
        public enum Answer
        {
            Skip,
            RunOriginal,
        }

        public sealed class Gate
        {
            public readonly string Type;
            public readonly string Method;
            // Parameter types as Type.ToString() writes them, only where the name is overloaded.
            public readonly string[] Args;
            public readonly Answer Answer;
            public readonly Scope Scope;
            public readonly string Why;

            public Gate(string type, string method, string why, Answer answer = Answer.Skip, Scope scope = Scope.World, string[] args = null)
            {
                Type = type;
                Method = method;
                Why = why;
                Answer = answer;
                Scope = scope;
                Args = args;
            }

            public string Name => Type.Substring(Type.LastIndexOf('.') + 1) + "." + Method;
        }

        private const string Events = "ROT.CampaignBehaviors.ROTEventBehavior";
        private const string Wars = "ROT.CampaignBehaviors.ROTStorylineWars";
        private const string Others = "ROT.CampaignBehaviors.ROTOthersCampaignBehavior";
        private const string Recruiter = "ROT.CampaignBehaviors.ROTTroopRecruiter";

        // Campaign behavior handlers that change the world (S24) and the enlistment entries (S27).
        // The server runs its own copies and Coop sends the result. Bookkeeping stays on: CanHeroDie
        // (it only answers whether a White Walker may die, from state both sides hold, so this PC's
        // death options match the server's; it writes nothing), the siege cooldown, the menus and
        // dialogs, SyncData, the recruiter's helpers the models use and the storyline war list's
        // readers. ROTRelationshipsBehavior.RegisterEvents still takes the vanilla romance tick off
        // DailyTickClan here: the server does the same when it hosts that behavior, so neither side
        // runs the vanilla NPC marriages. ROTOthersCampaignBehavior.OnDailyTick keeps
        // running (the invasion flag and the growth counter feed the party size model and CanHeroDie
        // here); only its roster edit is held back, by ClientGates' transpiler.
        public static readonly Gate[] BehaviorGates =
        {
            // The story events run from these five only (StartEvent, ConditionsMet, DailyTickEvent,
            // and the popup's Done button opened by StartEvent), so every story event row is covered.
            new Gate(Events, "OnDailyTick", "starts, runs and ends story events"),
            new Gate(Events, "OnHourlyTick", "drives the scripted sieges"),
            new Gate(Events, "OnTick", "drives the scripted sieges"),
            new Gate(Events, "TriggerEvent", "starts a story event"),
            new Gate(Events, "SkipEvent", "marks a story event done"),
            new Gate(Wars, "EnforceWars", "declares the storyline wars"),
            new Gate(Wars, "OnWarDeclared", "kings declare war for the Night's Watch"),
            // Night King death collapse, the wight player's peace and enlist prompt, the pre invasion leash.
            new Gate(Others, "OnHourlyTick", "hands over settlements, kills heroes, moves parties"),
            new Gate(Others, "OnGameLoaded", "converts White Walker settlements"),
            new Gate(Others, "OnSettlementOwnerChanged", "twin settlement owners and culture changes"),
            new Gate(Others, "OnMobilePartyCreated", "adds wights to a party"),
            new Gate(Others, "OnDailyTickParty", "adds wights to a party"),
            new Gate(Others, "OnRaidCompleted", "adds wights to a party"),
            new Gate(Others, "OnMapEventEnded", "adds wights and clears loot prisoners"),
            new Gate(Others, "SetupWars", "declares the White Walker wars"),
            new Gate(Others, "EnlistmentPopup", "the wight player's enlist prompt (wight start is off)"),
            new Gate(Others, "<RegisterEvents>b__16_2", "a random birthday for a new White Walker hero"),
            new Gate("ROT.CampaignBehaviors.ROTSpawnUniqueWanderers", "SpawnHeroes", "creates missing named wanderers"),
            new Gate(Recruiter, "OnTroopRecruited", "swaps recruited troops and gold"),
            new Gate(Recruiter, "OnDistributeLootToParty", "swaps troops after loot"),
            new Gate(Recruiter, "DailyTickParty", "swaps troops"),
            new Gate(Recruiter, "DailyTickSettlement", "swaps garrison troops"),
            new Gate("ROT.CampaignBehaviors.ROTRelationshipsBehavior", "CheckNPCMarriages", "marries NPC lords"),
            new Gate("ROT.CampaignBehaviors.ROTCoreBehavior", "DailyTickClan", "daily gold for poor clans"),
            // RoT's GameMenuOpened handler closes continue_siege_after_attack, a menu Coop drives in its
            // own siege flow, and reads ROTDuelsBehavior.Instance with no null check (null with duels off).
            // It only cleaned up after RoT's copy of PlayerEncounter.Finish, which is off in a session.
            new Gate("ROT.CampaignBehaviors.ROTCoreBehavior+<>c", "<RegisterEvents>b__1_0", "closes Coop's siege menu"),
            // Enlistment is off: nothing enlists, the behavior stays registered so
            // ROT.SubModule.EnlistmentBehavior is never null.
            new Gate("ROT.CampaignBehaviors.ROTEnlistmentBehavior", "Enlist", "enlistment is off"),
            // The End Battle option RoT adds to the vanilla encounter menu has no enlistment check
            // and switches menus in the middle of Coop's siege flow.
            new Gate("ROT.CampaignBehaviors.ROTEnlistmentMenuBehavior+<>c", "<AddMenus>b__4_5", "End Battle option on the encounter menu"),
        };

        private const string OthersPatches = "ROT.HarmonyPatches.Core.OthersPatches";
        private const string Duels = "ROT.HarmonyPatches.Core.DuelPatches";
        private const string Rbm = "ROT.HarmonyPatches.ModCompatibility.RBM";

        // Realm of Thrones' own Harmony patch methods (S26 and the patches of the disable set, S27).
        // The prefix goes on RoT's method itself. Skip on a void prefix or postfix leaves the game
        // method alone; RunOriginal on a bool prefix answers "run the original".
        public static readonly Gate[] PatchGates =
        {
            new Gate("ROT.HarmonyPatches.ModCompatibility.Diplomacy+DeclareWarActionPatch", "Prefix", "never vetoes a war the server declared", Answer.RunOriginal),
            new Gate(OthersPatches, "patch7", "converts, executes or frees a White Walker prisoner", Answer.RunOriginal),
            new Gate(OthersPatches, "patch6", "frees the player caught by White Walkers"),
            new Gate(OthersPatches, "patch11", "an executed prisoner rises as a White Walker", Answer.RunOriginal,
                args: new[] { "TaleWorlds.CampaignSystem.Hero", "TaleWorlds.CampaignSystem.Hero", "System.Boolean", "System.Boolean" }),
            new Gate("ROT.HarmonyPatches.Dragon.LootCollectorPatch", "Prefix", "removes dragon items from the losers' rosters", Answer.RunOriginal),
            new Gate("ROT.HarmonyPatches.NativeBugfixes.MarriageCrash+Patch_MarriageOfferGeneration", "Prefix", "MarriageCrash is off", Answer.RunOriginal),
            // The Night King duel is off; these read ROTDuelsBehavior.Instance in every battle.
            new Gate(Duels, "Patch1", "Night King duel: capture option"),
            new Gate(Duels, "Patch2", "Night King duel: leave option"),
            new Gate(Duels, "Patch3", "Night King duel: loot step", Answer.RunOriginal),
            // The one enlistment patch with no enlistment check that writes the world: the player's map event.
            // The other enlistment patches stop at their IsEnlisted check, and nobody is enlisted here.
            // Left alone on purpose:
            // - patch24 (a Mission.SpawnAgent prefix) swaps this PC's own hero's dragon for its flying
            //   or ground copy before the enlistment check. It only touches this PC's spawn data, and
            //   ROT.SubModule.DragonBehavior is always set, so dragons stay as RoT ships them here.
            // - patch28 reads the formation callout setting before its enlistment check, then returns
            //   true, so the game's sergeant message runs.
            // - patch41 only sets ROTEnlistmentBehavior.IsCurrentBattleNotSimulated, a flag only
            //   enlistment code reads.
            new Gate("ROT.HarmonyPatches.Core.EnlistmentPatches", "patch9", "sets IsPlayerSimulation after Attack"),
            // RBM's XML rewrite would give this PC other item stats than the server; the data is
            // read at each campaign's start, so it is off in the whole session.
            new Gate(Rbm, "DeserializeBasicCharacterObject", "RBM compat is off", scope: Scope.Session),
            new Gate(Rbm, "DeserializeEquipment", "RBM compat is off", scope: Scope.Session),
            new Gate(Rbm, "DeserializeArmorComponent", "RBM compat is off", scope: Scope.Session),
            new Gate(Rbm, "DeserializeBladeData", "RBM compat is off", scope: Scope.Session),
            new Gate(Rbm, "DeserializeWeaponComponentData", "RBM compat is off", scope: Scope.Session),
            new Gate(Rbm, "XmlFoo", "RBM compat is off", scope: Scope.Session),
        };

        public static bool Gated(bool playerPcInSession, bool? loadingSavedGame, Scope scope) =>
            playerPcInSession && (scope == Scope.Session || loadingSavedGame == true);

        // The live answer, set by ClientGates. Outside a Coop session it is always false.
        internal static Func<Scope, bool> IsGated = _ => false;

        // The answer when asking throws, set by ClientGates: logged once, and gated while a Coop
        // session runs (a gate that fails open would let a world rule run on a player's PC).
        internal static Func<Scope, bool> OnError = _ => false;

        public static bool ErrorAnswer(bool? inSession, bool? lastAnswer) => inSession ?? lastAnswer ?? true;

        private static bool Ask(Scope scope)
        {
            try { return IsGated(scope); }
            catch
            {
                try { return OnError(scope); }
                catch { return true; }
            }
        }

        // The Harmony prefixes. Returning false skips the method they sit on.
        public static bool SkipInWorld() => !Ask(Scope.World);

        public static bool SkipInSession() => !Ask(Scope.Session);

        public static bool RunOriginalInWorld(ref bool __result)
        {
            if (!Ask(Scope.World))
                return true;
            __result = true;
            return false;
        }

        // The method a gate names, declared on the type itself. Null when it is missing, or when the
        // name is overloaded and the gate gives no parameter types (never a guess).
        // RoTElephantAgentComponent.OnTickParallel reads action ids that static initializers look up
        // once in the game's action table, RoT's own and the game's ActionIndexCache (War Sails' hooks,
        // cutting ships loose, siege engines, ladders). Patching builds the method at once and runs
        // them, and the game loads its action data only after every OnSubModuleLoad, so a patch then
        // left those ids at -1 for the whole run. So the elephant gate waits for the first screen:
        // null until then, false when the method is not there, true once patched.
        public static bool? PatchElephantTick(Type type, bool actionsLoaded, Action<MethodInfo> patch)
        {
            if (!actionsLoaded)
                return null;
            var tick = type == null ? null : FindMethod(type, "OnTickParallel", null);
            if (tick == null)
                return false;
            patch(tick);
            return true;
        }

        public static MethodInfo FindMethod(Type type, string name, string[] args)
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var named = type.GetMethods(All).Where(method => method.Name == name).ToList();
            if (args != null)
                named = named.Where(method => method.GetParameters().Select(p => p.ParameterType.ToString()).SequenceEqual(args)).ToList();
            return named.Count == 1 ? named[0] : null;
        }

        // "Client gates: 24 on, 0 not found." or with the names that were not found.
        public static string SummaryLine(string what, int on, ICollection<string> missing) =>
            (missing.Count == 0 ? "" : "!!! ") + what + ": " + on + " on, " + missing.Count + " not found"
            + (missing.Count == 0 ? "." : " (" + string.Join(", ", missing) + "); those run as Realm of Thrones ships them.");

        public enum FinishStep
        {
            None,
            Remove,
            PutBack,
        }

        // Realm of Thrones' prefix on PlayerEncounter.Finish always skips the game's Finish and with it
        // Coop's patches there. It comes off on a player's PC for the whole Coop session and goes
        // back when the session ends.
        public static FinishStep Finish(bool playerPcInSession, bool removed) =>
            playerPcInSession && !removed ? FinishStep.Remove
            : !playerPcInSession && removed ? FinishStep.PutBack
            : FinishStep.None;

        // Realm of Thrones' clan finance model wraps the one before it and adds only an enlisted
        // soldier's wages. On top of the chain it breaks Coop's cast to the game's model in the clan
        // screen, so in the server's world it is taken out.
        public const string RotFinanceModel = "ROT.Models.ROTClanFinanceModel";

        public static bool IsRotFinanceWrapper(string typeName) => typeName == RotFinanceModel;

        // Realm of Thrones' two agent stat models wrap the one before it but leave out the two after
        // deployment calls, so with War Sails under them a sea battle never gets the figurehead,
        // crew and ammo bonuses. Only these two pass the calls on to the model under them.
        public static readonly string[] RotStatModels = { "ROT.Models.ROTAgentStatCalculateModel", "RoT_Elephants.RoTElephantAgentStatCalculateModel" };

        public static bool IsRotStatModel(string typeName) => RotStatModels.Contains(typeName);

        // The statics Realm of Thrones' agent spawn and battle patches read without a null check.
        public const string DuelsInstance = "ROT.CampaignBehaviors.ROTDuelsBehavior.Instance";

        public static readonly string[] MissionStatics = { "ROT.SubModule.EnlistmentBehavior", "ROT.SubModule.DragonBehavior", DuelsInstance };

        // Every Realm of Thrones method on a player's PC that reads ROTDuelsBehavior.Instance with no
        // null check. RoT only makes the duels behavior with duels on, so with these gated a null
        // Instance is what duels off looks like, not a fault.
        public static readonly string[] DuelsInstanceReaders = { "DuelPatches.Patch1", "DuelPatches.Patch2", "DuelPatches.Patch3", "ROTCoreBehavior+<>c.<RegisterEvents>b__1_0" };

        public static bool DuelsReadersGated(ICollection<string> on) => DuelsInstanceReaders.All(on.Contains);

        public static string StaticsLine(ICollection<string> missing, bool duelsReadersGated)
        {
            var duelsOff = duelsReadersGated && missing.Contains(DuelsInstance);
            var loud = missing.Where(name => !(duelsOff && name == DuelsInstance)).ToList();
            var line = loud.Count == 0
                ? "Client: Realm of Thrones' battle statics are set at mission start (" + string.Join(", ", MissionStatics.Except(missing)) + ")."
                : "!!! Client: at mission start " + string.Join(", ", loud) + (loud.Count == 1 ? " is" : " are")
                  + " null, so Realm of Thrones' agent spawn and battle patches will throw in this battle.";
            return duelsOff ? line + " " + DuelsInstance + " is null since duels are off; every reader of it is gated." : line;
        }

        // The features a player is told are off, once per session each.
        public enum Off
        {
            Enlistment,
            StorylineWarsDone,
            StorylineWarsCommand,
        }

        // The OFF messages, in English, all in one place so they can be translated later.
        public static readonly IReadOnlyDictionary<Off, string> OffTexts = new Dictionary<Off, string>
        {
            [Off.Enlistment] = "Enlisting in a lord's party is off in Coop.",
            [Off.StorylineWarsDone] = "Storyline wars can't be changed in Coop. Your changes were not kept.",
            [Off.StorylineWarsCommand] = "Storyline wars can't be edited in Coop.",
        };
    }
}
