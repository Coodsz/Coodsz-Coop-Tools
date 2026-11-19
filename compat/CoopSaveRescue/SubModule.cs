using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

// Matches the Version in SubModule.xml, so coodsz-modules.txt shows it.
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace CoopSaveRescue
{
    // Save repair module for the Coop dedicated server (vanilla or modded saves). The Coodsz
    // server tools load it on every start as DedicatedServer.CoopSaveRescue (Coop leaves
    // DedicatedServer.* modules out of the join check, so players can join while it is on).
    // It only changes data when it finds one of the known problems below.
    //
    // Three modes, from COODSZ_SAVE_RESCUE (set by the tools) or a CoopSaveRescue.mode file
    // next to the DLL (the plain Coop zip writes "full"):
    //  auto (default): the fixes that only touch data that is already broken. A broken battle
    //        is closed only after it failed several updates in a row.
    //  load: auto, plus one hero that fails to load is skipped so the rest of the save loads.
    //        The tools use it once after the server crashed while loading the save.
    //  full: everything, also the empty lord party reset (check 5). Manual Rescue save only.
    //
    // Known cases it fixes (each logged with [CoopSaveRescue]):
    //  1. Saved open menu: a save made while a game menu was open puts the map back
    //     into menu mode on load (MapState.OnLoadingFinished -> EnterMenuMode). The
    //     dedicated server has no screen and crashes (access violation). The menu is
    //     not reopened; the encounter is closed and the main party is put outside.
    //  2. Hostile town: the main party inside a town or castle of a faction it is at
    //     war with. The game opens "fortification_crime_rating" to throw it out, which
    //     crashes the server. The party is moved out like every other party.
    //  3. Broken battle: a battle whose update throws every tick stops the whole map
    //     (everyone stands still). The error is caught and the battle is closed.
    //  4. Empty battle: a battle where one side has no healthy men left never finishes,
    //     so the lords in it stay locked (seen after a siege: "0 men left" on one side).
    //     Twenty seconds after load every battle except raids is checked; an empty one
    //     is given to the side that still has men and closed.
    //  7. Killed lord left alive: a hero whose death was recorded (DeathMark set, perks and
    //     developer already cleared) but whose state was never set to Dead crashes
    //     Hero.AfterLoad and the whole load (access violation after "weather behavior
    //     unregistered"). Such a hero is marked Dead before AfterLoad runs; any other
    //     error in one hero's AfterLoad is logged and skipped so the rest still loads.
    //  8. Kingdom with no leader in a war: DefaultDiplomacyModel.GetRelationScore reads
    //     Leader.Clan of both kingdoms and throws when a ruling clan has no leader. Seen with
    //     TAOM: on its War of the Ring day Isengard declares war, Coop's OnWarDeclared prefix
    //     builds a call to war for every ally, the throw reaches Game.OnTick and the server
    //     shuts down. Only when it really throws there, that relation counts as neutral (0, what
    //     the game gives any pair that is not on bad terms), and a call to war that still
    //     crashes while it is built or added is skipped. The war itself is already declared.
    // 10. Hero missing from a troop list: when a clan is destroyed (seen with TAOM on its daily
    //     clan tick) KillCharacterAction.MakeDead takes each dead hero off his party's troop list.
    //     When the hero is not on that list the game looks him up as index -1, TroopRoster
    //     .AddToCountsAtIndex throws IndexOutOfRangeException before it changes anything, and the
    //     server stops every time that day comes. Only then (inside MakeDead, an index outside the
    //     list when the call started, and nothing being added) the change is skipped, since there is
    //     nothing to take off, and the death and the clan's end go on.
    // 11. Hero named with no first name: Coop 0.1.6's Hero.SetName prefix sends the new name to the
    //     players and turns both names into text first (HeroChangeNameData), so a name call with one
    //     of them missing throws NullReferenceException up to Game.OnTick and the server stops every
    //     time that day comes (seen with TAOM when a boy is born, HeroCreator.DeliverOffSpring; Coop
    //     #4104). Server only, and only on a Coop build that still turns them into text (Coop fixed it
    //     on development in eb9fddeaa): the missing name is filled with the other one before Coop
    //     sees it, what the game itself does for a hero with no first name when a save loads, so the
    //     server and every player get the same name. A boy with a player father has neither name at
    //     first (his father's character has none); he gets an empty one until the game names him a
    //     moment later, and Coop sends that real name.
    // 12. Child copied from a broken character: a boy is made from a copy of his father's character
    //     (GetCharacterTemplateForOffspring, also in TAOM's own model). In Coop 0.1.6 a player hero's character
    //     has no culture and no name of its own, so Hero.SetInitialValuesFromCharacter throws
    //     NullReferenceException in CharacterObject.StealthEquipments (Culture is null) up to Game.OnTick and
    //     the server stops (Coop #4104, fixed on development in #3858). Server only, and only inside a birth
    //     when the parent's character really is like that: the child is copied from a lord template of the
    //     same gender and race instead (the parent's culture, then the other parent's, then any culture, then
    //     a healthy lord). The game sets the child's culture, name, looks and gear right after, as for any birth.
    //     The player parent's character has no body shape either, and the child's looks read it
    //     (DefaultHeroCreationModel.GetStaticBodyProperties), so before the birth a parent whose character
    //     has no culture or no body shape gets them back the way Coop's #3858 does: the hero's culture, and the
    //     body shape of the character it came from, else main_hero's (then a lord template's). They are kept after.
    // 13. Server's own hero held prisoner: a save with the server's own hero (Hero.MainHero on the server,
    //     never a player's hero) as a prisoner keeps PlayerCaptivity on, so the game reopens a captivity menu
    //     on every tick and the server's map stands still (seen with TAOM, CR-DFX5S). Server only: the game's
    //     captivity update (PlayerCaptivity.Update) is skipped, as Coop's own open fix for it does, so no menu
    //     opens and the world moves. The hero stays the captor's prisoner and Coop's removed main party is left
    //     alone (the game's own escape throws on that party). Nothing in the save changes, so the line is not FIXED.
    // After the campaign has run for a minute it prints a report line.
    public sealed class SubModule : MBSubModuleBase
    {
        public const string Tag = "[CoopSaveRescue] ";
        private const string HarmonyId = "coodsz.coopsaverescue";
        private const string CrimeMenu = "fortification_crime_rating";
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static readonly object Gate = new object();
        private static readonly List<string> Fixes = new List<string>();
        private static readonly Dictionary<MapEvent, int> Failures = new Dictionary<MapEvent, int>();
        private static readonly HashSet<MapEvent> Logged = new HashSet<MapEvent>();
        private static readonly List<MapEvent> ToClose = new List<MapEvent>();
        private static readonly HashSet<MapEvent> Closed = new HashSet<MapEvent>();
        private static volatile bool _leavePending;
        private static volatile bool _captivityLogged;
        private static volatile bool _captivitySeen;
        private static DateTime _campaignSeen = DateTime.MinValue;
        private static bool _reported;
        private static bool _emptySeen;
        private static bool _emptyChecked;
        private static bool _partiesChecked;
        private static bool _linksSeen;
        private static bool _linksChecked;
        private static int _patches;
        private static object _harmony;
        // Found on the first look and fixed on the second one five seconds later, only when still
        // broken, so a battle that is still being set up is never touched.
        private static readonly HashSet<MapEvent> EmptyCandidates = new HashSet<MapEvent>();
        private static readonly Dictionary<PartyBase, MapEvent> LinkCandidates = new Dictionary<PartyBase, MapEvent>();

        public const string ModeVariable = "COODSZ_SAVE_RESCUE";
        public const string ModeFile = "CoopSaveRescue.mode";
        private enum RescueMode { Auto, Load, Full }
        private static RescueMode _mode = RescueMode.Auto;

        private static RescueMode ReadMode()
        {
            string value = null;
            try { value = Environment.GetEnvironmentVariable(ModeVariable); }
            catch { }
            if (string.IsNullOrWhiteSpace(value))
            {
                try
                {
                    var folder = System.IO.Path.GetDirectoryName(typeof(SubModule).Assembly.Location);
                    var file = folder == null ? null : System.IO.Path.Combine(folder, ModeFile);
                    if (file != null && System.IO.File.Exists(file))
                        value = System.IO.File.ReadAllText(file);
                }
                catch { }
            }
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "full": return RescueMode.Full;
                case "load": return RescueMode.Load;
                default: return RescueMode.Auto;
            }
        }

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _mode = ReadMode();
            object harmony;
            try
            {
                // Harmony is used through reflection so this DLL does not depend on one Harmony build.
                var harmonyType = Type.GetType("HarmonyLib.Harmony, 0Harmony", throwOnError: true);
                harmony = Activator.CreateInstance(harmonyType, HarmonyId);
                _harmony = harmony;
            }
            catch (Exception ex)
            {
                Log("Harmony is not loaded, nothing patched: " + ex.Message);
                return;
            }

            Patch(harmony, typeof(MapState).GetMethod("OnLoadingFinished", Any), nameof(LoadingFinishedPrefix), null, "MapState.OnLoadingFinished");
            Patch(harmony, typeof(PartyDiplomaticHandlerCampaignBehavior).GetMethod("CheckSettlementSuitabilityForParties", Any),
                nameof(CheckPrefix), null, "PartyDiplomaticHandlerCampaignBehavior.CheckSettlementSuitabilityForParties");
            Patch(harmony, typeof(GameMenu).GetMethod("SwitchToMenu", Any, null, new[] { typeof(string) }, null), nameof(MenuPrefix), null, "GameMenu.SwitchToMenu");
            Patch(harmony, typeof(GameMenu).GetMethod("ActivateGameMenu", Any, null, new[] { typeof(string) }, null), nameof(MenuPrefix), null, "GameMenu.ActivateGameMenu");
            Patch(harmony, typeof(GameMenu).GetMethod("RunOnInit", Any), nameof(RunOnInitPrefix), null, "GameMenu.RunOnInit");
            Patch(harmony, typeof(MapEvent).GetMethod("Update", Any, null, Type.EmptyTypes, null), null, nameof(UpdateFinalizer), "MapEvent.Update");
            // Skipping a hero that fails to load lets a broken save load half fixed, so only after a load crash or by hand.
            Patch(harmony, typeof(Hero).GetMethod("AfterLoad", Any), nameof(HeroAfterLoadPrefix), _mode == RescueMode.Auto ? null : nameof(HeroAfterLoadFinalizer), "Hero.AfterLoad");
            // Both only act when the game method really threw, so they are on in every mode.
            Patch(harmony, typeof(DefaultDiplomacyModel).GetMethod("GetRelationScore", Any, null, new[] { typeof(IFaction), typeof(IFaction), typeof(IFaction) }, null),
                null, nameof(RelationScoreFinalizer), "DefaultDiplomacyModel.GetRelationScore");
            Patch(harmony, typeof(AllianceCampaignBehavior).GetMethod("OnWarDeclared", Any), null, nameof(WarDeclaredFinalizer), "AllianceCampaignBehavior.OnWarDeclared");
            // Server only, and only while a hero is being made dead and the roster call really threw on an index outside the list.
            if (GameNetwork.IsDedicatedServer)
            {
                Patch(harmony, typeof(KillCharacterAction).GetMethod("MakeDead", Any, null, new[] { typeof(Hero), typeof(bool) }, null),
                    nameof(MakeDeadPrefix), nameof(MakeDeadFinalizer), "KillCharacterAction.MakeDead");
                Patch(harmony, typeof(TroopRoster).GetMethod("AddToCountsAtIndex", Any, null, new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool) }, null),
                    nameof(RosterIndexPrefix), nameof(RosterIndexFinalizer), "TroopRoster.AddToCountsAtIndex");
                Patch(harmony, typeof(HeroCreator).GetMethod("DeliverOffSpring", Any, null, new[] { typeof(Hero), typeof(Hero), typeof(bool) }, null),
                    nameof(DeliverOffSpringPrefix), nameof(DeliverOffSpringFinalizer), "HeroCreator.DeliverOffSpring");
                Patch(harmony, typeof(HeroCreator).GetMethod("CreateHero", Any, null, new[] { typeof(CharacterObject), typeof(bool), typeof(CampaignTime), typeof(CampaignTime) }, null),
                    nameof(CreateHeroPrefix), null, "HeroCreator.CreateHero");
                Patch(harmony, typeof(PlayerCaptivity).GetMethod("Update", Any, null, new[] { typeof(float) }, null),
                    nameof(CaptivityUpdatePrefix), null, "PlayerCaptivity.Update", FirstPriority);
            }
            Log("Ready in " + _mode.ToString().ToLowerInvariant() + " mode (" + _patches + " of 14 checks active, plus the empty-battle and ended-battle checks after load"
                + (_mode == RescueMode.Full ? " and the empty-lord-party check). Let the server save once the Report line is in. Full mode is meant for one start only." : ")."));
        }

        // Coop's GameInterface can load after this module (in the TAOM server it rides in DedicatedServer.Windows),
        // so the check on Coop's own types waits until every module is loaded.
        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            InstallNameGuard(lastChance: false);
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            InstallNameGuard(lastChance: true);
        }

        // Updates a battle may fail in a row before it is closed. A single failure can pass by itself.
        private static int CloseAfterFailures => _mode == RescueMode.Full ? 1 : 3;

        // A prefix with a priority runs before the ones without (Harmony's Priority.First).
        private static void Patch(object harmony, MethodBase target, string prefix, string finalizer, string name, int priority = -1)
        {
            if (target == null)
            {
                Log(name + " was not found in this game version; that check is off.");
                return;
            }
            try
            {
                var harmonyMethodType = Type.GetType("HarmonyLib.HarmonyMethod, 0Harmony", throwOnError: true);
                object Wrap(string method) => method == null ? null
                    : Activator.CreateInstance(harmonyMethodType, typeof(SubModule).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static));
                var patch = harmony.GetType().GetMethods()
                    .First(m => m.Name == "Patch" && m.GetParameters().Length == 5 && m.GetParameters()[0].ParameterType == typeof(MethodBase));
                var before = Wrap(prefix);
                if (before != null && priority >= 0)
                {
                    var field = harmonyMethodType.GetField("priority");
                    if (field == null)
                        throw new MissingFieldException("HarmonyMethod.priority");
                    field.SetValue(before, priority);
                }
                patch.Invoke(harmony, new[] { target, before, null, null, Wrap(finalizer) });
                _patches++;
            }
            catch (Exception ex)
            {
                Log("Could not patch " + name + ": " + (ex.InnerException ?? ex).Message);
            }
        }

        // ---- 1. Saved open menu -------------------------------------------------

        private static void LoadingFinishedPrefix(MapState __instance)
        {
            // A save loaded later in the same run says again whether the server's hero is a prisoner.
            _captivityLogged = false;
            try
            {
                var menuId = __instance?.GameMenuId;
                if (string.IsNullOrEmpty(menuId))
                {
                    Log("Save loaded; no menu was open in it.");
                    return;
                }
                __instance.GameMenuId = null;
                _leavePending = true;
                Fix("The save had the \"" + menuId + "\" menu open; the server would crash reopening it. Closed it.");
            }
            catch (Exception ex)
            {
                Log("Checking the saved menu failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool RunOnInitPrefix(GameMenu __instance)
        {
            if (__instance == null || __instance.StringId != CrimeMenu)
                return true;
            _leavePending = true;
            Log("Skipped opening \"" + CrimeMenu + "\" on the server.");
            return false;
        }

        // ---- 2. Hostile town ----------------------------------------------------

        private static void CheckPrefix(IEnumerable<MobileParty> parties)
        {
            try
            {
                var main = MobileParty.MainParty;
                if (main == null || parties == null || !parties.Contains(main))
                    return;
                var settlement = main.CurrentSettlement;
                if (settlement == null || !settlement.IsFortification || main.MapFaction == null || settlement.MapFaction == null
                    || !main.MapFaction.IsAtWarWith(settlement.MapFaction))
                    return;
                if (main.IsTransitionInProgress)
                    main.CancelNavigationTransition();
                LeaveSettlementAction.ApplyForParty(main);
                Fix("The main party was inside " + settlement.Name + " (" + settlement.StringId + ") of " + settlement.MapFaction.Name
                    + ", which it is at war with. Moved it outside.");
            }
            catch (Exception ex)
            {
                Log("Moving the main party out failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool MenuPrefix(string menuId)
        {
            if (menuId != CrimeMenu)
                return true;
            _leavePending = true;
            Log("Blocked the \"" + CrimeMenu + "\" menu on the server.");
            return false;
        }

        private static void LeaveSettlementNow()
        {
            var main = MobileParty.MainParty;
            try { PlayerEncounter.Finish(true); }
            catch (Exception ex) { Log("Closing the encounter failed: " + ex.GetType().Name + ": " + ex.Message); }
            try
            {
                if (main != null && main.CurrentSettlement != null)
                {
                    var place = main.CurrentSettlement.Name;
                    if (main.IsTransitionInProgress)
                        main.CancelNavigationTransition();
                    LeaveSettlementAction.ApplyForParty(main);
                    Fix("Moved the main party out of " + place + ".");
                }
            }
            catch (Exception ex)
            {
                Log("Moving the main party out failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            Log("Main party is " + (main?.CurrentSettlement == null ? "outside" : "still inside " + main.CurrentSettlement.Name)
                + "; open menu: " + (Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId ?? "none") + ".");
        }

        // ---- 13. Server's own hero held prisoner --------------------------------

        // Nobody plays the server's own hero (Hero.MainHero on the server; players' captivity is Coop's own
        // PlayerCaptivityServerHandler). While it is a prisoner, PlayerCaptivity.Update opens a captivity menu on every
        // tick and an open menu stops campaign time, so no party moves. On the server the update is skipped, the same as
        // Coop's own open fix for it (PlayerCaptivityRobustnessPatches), so once that lands both skip it and nothing changes.
        // The hero stays the captor's prisoner: the game's own escape puts him back in the main party Coop removed
        // and throws there. The update does nothing while the hero is free, so skipping it always is safe.
        // Nothing in the save changes, so the line is a plain one: a FIXED line would make the tools save
        // and back up the game on every start. The report line says it too.
        // First priority, so it runs (and logs) before Coop's own prefix, which also returns false on the server.
        private static bool CaptivityUpdatePrefix()
        {
            if (!_captivityLogged)
            {
                try
                {
                    if (PlayerCaptivity.IsCaptive)
                    {
                        _captivityLogged = true;
                        _captivitySeen = true;
                        Log("The server's own hero is a prisoner of " + (PlayerCaptivity.CaptorParty?.Name?.ToString() ?? "?")
                            + " in the save. The server keeps the captivity menu shut, so the world moves; the hero stays a prisoner.");
                    }
                }
                catch
                {
                    _captivityLogged = true;
                }
            }
            return false;
        }

        // ---- 3. Broken battle ---------------------------------------------------

        // Runs after MapEvent.Update. Returning null swallows the exception so the
        // other battles and the rest of the map keep ticking. The count is of failures in
        // a row: an update that works clears it, so a working battle that fails now and
        // then is never closed. The stack trace is logged once per battle.
        private static Exception UpdateFinalizer(MapEvent __instance, Exception __exception)
        {
            if (__instance == null)
                return __exception;
            if (__exception == null)
            {
                if (Failures.Count != 0)
                    lock (Gate)
                        Failures.Remove(__instance);
                return null;
            }
            lock (Gate)
            {
                if (Closed.Contains(__instance))
                    return null;
                Failures.TryGetValue(__instance, out var count);
                Failures[__instance] = ++count;
                if (count == 1 && Logged.Add(__instance))
                    Log("Battle " + Describe(__instance) + " failed its update: " + __exception.GetType().Name + ": " + __exception.Message
                        + Environment.NewLine + __exception.StackTrace);
                if (count == CloseAfterFailures && !ToClose.Contains(__instance))
                    ToClose.Add(__instance);
            }
            return null;
        }

        private static void CloseBattle(MapEvent mapEvent, string kind = "broken battle")
        {
            var description = Describe(mapEvent);
            foreach (var line in PartyLines(mapEvent))
                Log("  " + line);
            try
            {
                mapEvent.FinalizeEvent();
                if (mapEvent.IsFinalized)
                {
                    Fix("Closed " + kind + " " + description + ".");
                    return;
                }
                Log("FinalizeEvent returned but the battle is not finished; forcing it.");
            }
            catch (Exception ex)
            {
                Log("FinalizeEvent failed (" + ex.GetType().Name + ": " + ex.Message + "); forcing it.");
            }

            var detached = 0;
            foreach (var side in Sides(mapEvent))
            {
                List<MapEventParty> parties;
                try { parties = side.Parties?.ToList() ?? new List<MapEventParty>(); }
                catch { continue; }
                foreach (var party in parties)
                {
                    try
                    {
                        if (party?.Party != null && party.Party.MapEventSide != null)
                        {
                            party.Party.MapEventSide = null;
                            detached++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("  Could not detach " + Name(party) + ": " + ex.Message);
                    }
                }
            }
            try
            {
                var state = typeof(MapEvent).GetField("_state", Any);
                if (state != null)
                    state.SetValue(mapEvent, Enum.ToObject(state.FieldType, 2)); // MapEventState.WaitingRemoval
                typeof(MapEvent).GetField("_isFinishCalled", Any)?.SetValue(mapEvent, true);
            }
            catch (Exception ex)
            {
                Log("  Could not mark the battle finished: " + ex.Message);
            }
            Fix("Force-closed " + kind + " " + description + " (" + detached + " parties detached).");
        }

        // ---- 7. Killed lord left alive -----------------------------------------

        private static readonly FieldInfo HeroPerks = typeof(Hero).GetField("_heroPerks", Any);
        private static readonly FieldInfo HeroDeveloper = typeof(Hero).GetField("_heroDeveloper", Any);

        // Runs before Hero.AfterLoad. When a hero is killed the game clears the perks and the
        // developer and sets DeathMark; if the save still has the hero alive, AfterLoad uses the
        // cleared data and the server crashes. Finishing the death the game already started is
        // the only safe choice: the data that would bring the hero back is gone.
        private static void HeroAfterLoadPrefix(Hero __instance)
        {
            try
            {
                if (__instance == null || __instance.HeroState == Hero.CharacterStates.Dead || HeroPerks == null || HeroDeveloper == null)
                    return;
                if (HeroPerks.GetValue(__instance) != null && HeroDeveloper.GetValue(__instance) != null)
                    return;
                var name = __instance.Name?.ToString() ?? "?";
                if (__instance.DeathMark == KillCharacterAction.KillCharacterActionDetail.None)
                {
                    Log("Hero " + __instance.StringId + " (" + name + ") has no hero data but was never killed; left as is.");
                    return;
                }
                var state = __instance.HeroState;
                __instance.ChangeState(Hero.CharacterStates.Dead);
                Fix("Hero " + __instance.StringId + " (" + name + ") was killed (" + __instance.DeathMark + ") but the save still had the hero "
                    + state + " with the hero data gone; the server would crash loading it. Marked the hero dead.");
            }
            catch (Exception ex)
            {
                Log("Checking hero " + (__instance?.StringId ?? "?") + " failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Runs after Hero.AfterLoad. Returning null swallows the exception so one bad hero
        // does not stop the whole save from loading.
        private static Exception HeroAfterLoadFinalizer(Hero __instance, Exception __exception)
        {
            if (__exception == null)
                return null;
            string id;
            try { id = __instance?.StringId ?? "?"; }
            catch { id = "?"; }
            Fix("Hero " + id + " failed to load: " + __exception.GetType().Name + ": " + __exception.Message + ". Skipped it so the rest of the save loads.");
            return null;
        }

        // ---- 8. Kingdom with no leader in a war ---------------------------------

        private static readonly HashSet<string> NoLeaderSeen = new HashSet<string>();

        // Runs after DefaultDiplomacyModel.GetRelationScore, which reads factionDeclaresWar.Leader.Clan,
        // factionDeclaredWar.Leader.Clan and evaluatingFaction.Leader.Clan. Only a NullReferenceException
        // while one of those is really missing is swallowed, and the relation then counts as neutral
        // (0, what the game returns for any pair that is not on bad terms). Every other error goes on.
        private static Exception RelationScoreFinalizer(IFaction factionDeclaresWar, IFaction factionDeclaredWar, IFaction evaluatingFaction,
            ref float __result, Exception __exception)
        {
            if (__exception == null)
                return null;
            if (!(__exception is NullReferenceException))
                return __exception;
            var missing = NoLeader(factionDeclaresWar) ?? NoLeader(factionDeclaredWar) ?? NoLeader(evaluatingFaction);
            if (missing == null)
                return __exception;
            __result = 0f;
            bool first;
            lock (Gate)
                first = NoLeaderSeen.Add(missing);
            // One FIXED line per kingdom without a leader; the game asks this many times a day.
            if (first)
            {
                string caller;
                try { caller = new System.Diagnostics.StackTrace(false).ToString(); }
                catch { caller = ""; }
                var what = caller.IndexOf("CallToWar", StringComparison.Ordinal) >= 0 ? "A call to war" : "Weighing a war";
                Fix(what + " crashed (" + FactionLabel(factionDeclaresWar) + " against " + FactionLabel(factionDeclaredWar) + ") because "
                    + missing + ". Counted that relation as neutral so the war goes on and the server keeps running.");
            }
            return null;
        }

        // Runs after AllianceCampaignBehavior.OnWarDeclared, around Coop's prefix that replaces it on the
        // server. The war is already declared when this event runs. Only an error thrown while a call to
        // war is built (ProposeCallToWarAgreementDecision) or added (Kingdom.AddDecision) is swallowed:
        // the calls to war for this war are skipped and the other war listeners still run.
        private static Exception WarDeclaredFinalizer(IFaction faction1, IFaction faction2, Exception __exception)
        {
            if (__exception == null)
                return null;
            string trace;
            try { trace = __exception.StackTrace ?? ""; }
            catch { trace = ""; }
            if (!IsCallToWarError(trace))
                return __exception;
            Fix("Skipped 1 call to war that crashed (" + FactionLabel(faction1) + " against " + FactionLabel(faction2) + "): "
                + __exception.GetType().Name + ": " + __exception.Message.TrimEnd('.') + ". The war itself is declared.");
            Log("  " + trace.Replace(Environment.NewLine, Environment.NewLine + "  "));
            return null;
        }

        // The stack of an error that was thrown while a call to war was built or added.
        public static bool IsCallToWarError(string trace) =>
            !string.IsNullOrEmpty(trace)
            && (trace.IndexOf("ProposeCallToWarAgreementDecision", StringComparison.Ordinal) >= 0
                || trace.IndexOf("Kingdom.AddDecision", StringComparison.Ordinal) >= 0);

        // What is missing for GetRelationScore, or null when the leader and the leader's clan are there.
        private static string NoLeader(IFaction faction)
        {
            try
            {
                if (faction == null)
                    return "a kingdom is missing";
                var leader = faction.Leader;
                if (leader == null)
                    return FactionLabel(faction) + " has no leader";
                if (leader.Clan == null)
                    return "the leader of " + FactionLabel(faction) + " has no clan";
                return null;
            }
            catch (Exception ex)
            {
                return FactionLabel(faction) + " could not be read (" + ex.GetType().Name + ")";
            }
        }

        private static string FactionLabel(IFaction faction)
        {
            if (faction == null)
                return "?";
            try
            {
                var name = faction.Name?.ToString();
                return string.IsNullOrEmpty(name) ? faction.StringId ?? "?" : name + " [" + faction.StringId + "]";
            }
            catch
            {
                return "?";
            }
        }

        // ---- 10. Hero missing from a troop list --------------------------------

        // The hero KillCharacterAction.MakeDead is working on, only while it runs (the roster guard is off otherwise).
        [ThreadStatic]
        private static Hero _dyingHero;
        private static readonly HashSet<string> RosterSkipSeen = new HashSet<string>();
        private static readonly PropertyInfo RosterOwner = typeof(TroopRoster).GetProperty("OwnerParty", Any);

        // Runs before MakeDead (Coop 0.1.6 calls it from its own KillCharacterAction prefix too).
        // The hero that was being made dead before is kept, in case one death leads to another.
        private static void MakeDeadPrefix(Hero victim, out Hero __state)
        {
            __state = _dyingHero;
            _dyingHero = victim;
        }

        // Runs after MakeDead, also when it threw, so the roster guard never stays on.
        private static Exception MakeDeadFinalizer(Exception __exception, Hero __state)
        {
            _dyingHero = __state;
            return __exception;
        }

        // Runs before every TroopRoster.AddToCountsAtIndex on the server, so it only notes whether this call
        // may be skipped: inside MakeDead, an index outside the list before anything changed, nothing added,
        // and the hero being made dead really is not on this list (so the log names the right hero).
        private static void RosterIndexPrefix(TroopRoster __instance, int index, int countChange, out int? __state)
        {
            var hero = _dyingHero;
            __state = hero != null && countChange <= 0 && (index < 0 || index >= __instance.Count)
                && __instance.FindIndexOfTroop(hero.CharacterObject) < 0 ? index : (int?)null;
        }

        // Runs after TroopRoster.AddToCountsAtIndex. The game reads data[index] on its first line, so with an
        // index outside the list it throws before it changes anything. Only that IndexOutOfRangeException, in
        // a call the prefix marked, is swallowed: there is nothing to take off, and MakeDead and the clan's end
        // go on. Every other error, and every other roster change, goes on as before.
        // Known leftover: the skipped call is also what clears the dead hero's PartyBelongedTo (OnHeroRemoved),
        // so a dead hero can stay pointed at that party. It was already broken that way; it is left as is.
        private static Exception RosterIndexFinalizer(TroopRoster __instance, int countChange, ref int __result, Exception __exception, int? __state)
        {
            if (__exception == null || __state == null || !(__exception is IndexOutOfRangeException))
                return __exception;
            __result = -1;
            try
            {
                var hero = _dyingHero;
                var id = hero?.StringId ?? "?";
                bool first;
                lock (Gate)
                    first = RosterSkipSeen.Add(id);
                if (first)
                {
                    var party = RosterOwner?.GetValue(__instance) as PartyBase;
                    Fix("Hero " + id + " (" + (hero?.Name?.ToString() ?? "?") + ", clan " + (hero?.Clan?.Name?.ToString() ?? "none")
                        + ") could not be taken off the troop list of " + (party == null ? "a party" : party.Name + " [" + (party.MobileParty?.StringId ?? party.Settlement?.StringId ?? "?") + "]")
                        + ": the hero was not on it (index " + __state.Value + ", the list has " + __instance.Count + ", change " + countChange
                        + "). Skipped that change so the death goes on and the server keeps running.");
                }
            }
            catch (Exception ex)
            {
                Log("Describing a skipped troop list change failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            return null;
        }

        // ---- 11. Hero named with no first name ---------------------------------

        private const string NameDataType = "GameInterface.Services.Heroes.Data.HeroChangeNameData";
        private const int FirstPriority = 800;
        private static bool _nameGuardDone;
        private static readonly HashSet<string> NameFixSeen = new HashSet<string>();

        // Server only, once. Coop 0.1.6's HeroChangeNameData keeps both names as text (string FirstName) and calls
        // ToString() on them in its constructor; the fixed build (eb9fddeaa) keeps the TextObjects and takes a
        // missing one, so there the check stays off. Until Coop is loaded nothing is decided; the last call says why it is off.
        private static void InstallNameGuard(bool lastChance)
        {
            if (_nameGuardDone || _harmony == null || !GameNetwork.IsDedicatedServer)
                return;
            try
            {
                var typeByName = Type.GetType("HarmonyLib.AccessTools, 0Harmony")
                    ?.GetMethod("TypeByName", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                var data = typeByName?.Invoke(null, new object[] { NameDataType }) as Type;
                if (data == null)
                {
                    if (lastChance)
                    {
                        _nameGuardDone = true;
                        Log("Coop's hero name sync (" + NameDataType + ") was not found; the no-first-name check is off.");
                    }
                    return;
                }
                _nameGuardDone = true;
                if (data.GetProperty("FirstName", Any)?.PropertyType != typeof(string)
                    || data.GetConstructor(Any, null, new[] { typeof(Hero), typeof(TextObject), typeof(TextObject) }, null) == null)
                {
                    Log("This Coop build already takes a hero name with no first name (" + NameDataType + " changed); the no-first-name check is off.");
                    return;
                }
                var patched = _patches;
                Patch(_harmony, typeof(Hero).GetMethod("SetName", Any, null, new[] { typeof(TextObject), typeof(TextObject) }, null),
                    nameof(SetNamePrefix), null, "Hero.SetName", FirstPriority);
                if (_patches > patched)
                    Log("No-first-name check on: this Coop build stops the server on a hero named with no first name (Coop #4104, a boy being born); "
                        + "such a name is filled in before Coop sends it.");
            }
            catch (Exception ex)
            {
                Log("The no-first-name check could not start: " + (ex.InnerException ?? ex).Message);
            }
        }

        // Runs before Coop's own Hero.SetName prefix (higher priority), so Coop and the game both get the filled in names.
        // One name missing: it gets the other one, what Hero.AfterLoad does for a hero with no first name, and the server
        // and every player end up with the same name.
        // Both missing: the Hero constructor names a new hero after the character it was copied from (SetInitialValuesFromCharacter
        // passes characterObject.Name twice), and a boy is copied from his father's character, which has no name when the father
        // is a player (seen in CR-36XTG; why Coop leaves it nameless is not traced). The game names the child for real right after
        // (HeroCreator.InitializeHeroFromSettings), through a SetName Coop sends like any other, so this first name only has to
        // be something. It gets one empty name, since null would also stop the server in Coop's own name field sync. A hero made by CreateBasicHero is not named again
        // and keeps the empty name, which is still safer than null.
        private static void SetNamePrefix(Hero __instance, ref TextObject fullName, ref TextObject firstName)
        {
            if (fullName != null && firstName != null)
                return;
            var none = fullName == null && firstName == null;
            var missing = none ? "name at all" : firstName == null ? "first name" : "full name";
            if (none)
                fullName = firstName = new TextObject("");
            else if (firstName == null)
                firstName = fullName;
            else
                fullName = firstName;
            try
            {
                var id = __instance?.StringId ?? "?";
                bool first;
                lock (Gate)
                    first = NameFixSeen.Add(id);
                if (first && none)
                    Fix("Hero " + id + " (from " + (__instance?.CharacterObject?.OriginalCharacter?.StringId ?? "?") + ") was named with no " + missing
                        + ", which stops the server in this Coop build (Coop #4104). Gave it an empty name so the server keeps running. A child of a player gets his real name right after, and Coop sends that name to the players.");
                else if (first)
                    Fix("Hero " + id + " (" + fullName + ", clan " + (__instance?.Clan?.Name?.ToString() ?? "none") + ") was named with no " + missing
                        + ", which stops the server in this Coop build. Used the other name for both, as the game does when a save loads, so the server keeps running and the players get the same name.");
            }
            catch (Exception ex)
            {
                Log("Describing a filled in hero name failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ---- 12. Child copied from a broken character ---------------------------

        [ThreadStatic]
        private static Hero[] _birthParents;
        private static readonly FieldInfo BasicName = typeof(BasicCharacterObject).GetField("_basicName", Any);

        // Runs before HeroCreator.DeliverOffSpring, so CreateHero knows it is making a child and whose.
        // The parents of a birth already going on are kept, in case one birth leads to another.
        private static void DeliverOffSpringPrefix(Hero mother, Hero father, out Hero[] __state)
        {
            __state = _birthParents;
            _birthParents = new[] { mother, father };
            RepairParent(mother);
            RepairParent(father);
        }

        // BasicCharacterObject.BodyPropertyRange has a protected setter.
        private static readonly MethodInfo SetBodyRange = typeof(BasicCharacterObject).GetProperty("BodyPropertyRange", Any)?.GetSetMethod(true);

        // Coop's own fix (#3858, RepairMissingParentCharacterData) fills the parent's culture and body shape back in at birth.
        // The same here, only when one really is missing, and kept after the birth since other code trips on them too.
        private static void RepairParent(Hero parent)
        {
            var character = parent?.CharacterObject;
            if (character == null)
                return;
            try
            {
                var basic = (BasicCharacterObject)character;
                var missing = new List<string>();
                var sources = new List<string>();
                if (basic.Culture == null && parent.Culture != null)
                {
                    basic.Culture = parent.Culture;
                    missing.Add("culture");
                    sources.Add("the hero's own culture " + parent.Culture.StringId);
                }
                if (basic.BodyPropertyRange == null && SetBodyRange != null)
                {
                    var source = new[] { character.OriginalCharacter, Game.Current.ObjectManager.GetObject<CharacterObject>("main_hero") }
                        .FirstOrDefault(candidate => candidate?.BodyPropertyRange != null)
                        ?? SafeTemplate(character.IsFemale, character.Race, true, parent.Culture);
                    if (source?.BodyPropertyRange != null)
                    {
                        SetBodyRange.Invoke(character, new object[] { source.BodyPropertyRange });
                        missing.Add("body shape");
                        sources.Add(source.StringId);
                    }
                }
                if (missing.Count > 0)
                    Fix("Hero " + (parent.Name?.ToString() ?? "?") + " [" + parent.StringId + "] had no " + string.Join(" or ", missing)
                        + " on the server, filled them from " + string.Join(" and ", sources) + " before the birth so it doesn't stop the server (Coop #4104).");
            }
            catch (Exception ex)
            {
                Log("Repairing a parent before a birth failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Runs after DeliverOffSpring, also when it threw, so the check never stays on.
        private static Exception DeliverOffSpringFinalizer(Exception __exception, Hero[] __state)
        {
            _birthParents = __state;
            return __exception;
        }

        // Runs before HeroCreator.CreateHero. DeliverOffSpring passes the parent's own character as the template
        // (the father's for a boy), and CreateHero copies it (CharacterObject.CreateFrom) before the Hero constructor
        // reads the copy's culture and name. Only inside a birth and only when that character is broken it is swapped.
        private static void CreateHeroPrefix(ref CharacterObject character, bool useCharacterAsTemplate)
        {
            var parents = _birthParents;
            if (parents == null || !useCharacterAsTemplate || character == null || !character.IsHero)
                return;
            try
            {
                var broken = Broken(character);
                if (broken == null)
                    return;
                var parent = character.HeroObject;
                var other = parent == parents[0] ? parents[1] : parents[0];
                var template = SafeTemplate(character.IsFemale, character.Race, parent?.Culture, other?.Culture);
                var who = (parent?.Name?.ToString() ?? "?") + " [" + (parent?.StringId ?? "?") + "]";
                if (template == null)
                {
                    Log("Child of hero " + who + " copies a broken character (" + broken + ") and no lord template was found to use instead.");
                    return;
                }
                character = template;
                Fix("Child of hero " + who + " would have copied that hero's broken character (" + broken + ", as a player hero has in this Coop build), used "
                    + template.StringId + " instead so the birth doesn't stop the server (Coop #4104).");
            }
            catch (Exception ex)
            {
                Log("Checking a newborn's template failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // What Hero.SetInitialValuesFromCharacter would trip on in a copy of this character, or null when nothing.
        // The copy keeps the character's own culture and name (BasicCharacterObject.FillFrom), not the hero's.
        private static string Broken(CharacterObject character)
        {
            var problems = new List<string>();
            var culture = ((BasicCharacterObject)character).Culture;
            if (culture == null)
                problems.Add("no culture");
            else if (culture is CultureObject full && full.DefaultBattleEquipmentRoster != null && full.DefaultStealthEquipmentRoster == null)
                problems.Add("a culture with no stealth gear");
            if (BasicName != null && BasicName.GetValue(character) == null)
                problems.Add("no name");
            return problems.Count == 0 ? null : string.Join(" and ", problems);
        }

        // A lord template of that gender, the same race first: the parents' cultures, then any culture, then a healthy lord's own character.
        private static CharacterObject SafeTemplate(bool female, int race, params CultureObject[] cultures) =>
            SafeTemplate(female, race, false, cultures);

        // With needsBody, only a template that has a body shape to copy counts, so a broken character is never the pick.
        private static CharacterObject SafeTemplate(bool female, int race, bool needsBody, params CultureObject[] cultures)
        {
            var candidates = cultures.Where(culture => culture != null)
                .Concat(Game.Current.ObjectManager.GetObjectTypeList<CultureObject>())
                .SelectMany(culture => culture.LordTemplates ?? Enumerable.Empty<CharacterObject>())
                .Concat(Hero.AllAliveHeroes.Where(hero => hero.IsLord).Select(hero => hero.CharacterObject))
                .Where(template => template != null && template.IsFemale == female && Broken(template) == null
                    && (!needsBody || template.BodyPropertyRange != null));
            return candidates.FirstOrDefault(template => template.Race == race) ?? candidates.FirstOrDefault();
        }

        // ---- 4. Empty battle ----------------------------------------------------

        // First look (act false) only notes the empty battles; the second one closes those still empty.
        private static void CheckEmptyBattles(bool act)
        {
            List<MapEvent> events;
            try
            {
                events = Campaign.Current?.MapEventManager?.MapEvents?.ToList() ?? new List<MapEvent>();
            }
            catch (Exception ex)
            {
                Log("Reading the battles failed: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            var empty = 0;
            foreach (var mapEvent in events)
            {
                try
                {
                    // A raid with no defenders left is normal: the village is being looted.
                    if (mapEvent == null || mapEvent.IsFinalized || mapEvent.IsRaid)
                        continue;
                    lock (Gate)
                    {
                        if (Closed.Contains(mapEvent))
                            continue;
                    }

                    var sides = Sides(mapEvent).ToArray();
                    if (sides.Length != 2)
                        continue;
                    var defenders = HealthyMen(sides[0]);
                    var attackers = HealthyMen(sides[1]);
                    if (defenders > 0 && attackers > 0)
                        continue;
                    if (!act)
                    {
                        EmptyCandidates.Add(mapEvent);
                        continue;
                    }
                    if (!EmptyCandidates.Contains(mapEvent))
                        continue;

                    empty++;
                    Log("Battle " + Describe(mapEvent) + " has " + defenders + " defending and " + attackers
                        + " attacking men; it cannot finish by itself.");
                    // The side that still has men wins, as the game would have decided.
                    if (defenders > 0)
                        mapEvent.SetOverrideWinner(BattleSideEnum.Defender);
                    else if (attackers > 0)
                        mapEvent.SetOverrideWinner(BattleSideEnum.Attacker);
                    lock (Gate)
                        Closed.Add(mapEvent);
                    CloseBattle(mapEvent, "empty battle");
                }
                catch (Exception ex)
                {
                    Log("Checking battle " + Describe(mapEvent) + " failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            if (act)
                Log("Checked " + events.Count + " battles for an empty side; " + empty + " found.");
        }

        // Check 6: parties still linked to a battle the game already ended. Coop can finish a
        // broken battle while a save loads without letting its parties go; the battle is gone
        // from the world (map_event debug no longer lists it) but the parties still point at it,
        // so they never move and players still see "field battle in progress" over them.
        // First look (act false) only notes the links; the second one frees a party still tied to the same ended battle.
        private static void CheckEndedBattleLinks(bool act)
        {
            HashSet<MapEvent> live;
            List<PartyBase> parties;
            try
            {
                live = new HashSet<MapEvent>(Campaign.Current?.MapEventManager?.MapEvents ?? Enumerable.Empty<MapEvent>());
                parties = (MobileParty.All ?? Enumerable.Empty<MobileParty>()).Where(party => party != null).Select(party => party.Party)
                    .Concat((Settlement.All ?? Enumerable.Empty<Settlement>()).Where(settlement => settlement != null).Select(settlement => settlement.Party))
                    .Where(party => party != null)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log("Reading the parties failed: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            var freed = new Dictionary<MapEvent, List<string>>();
            foreach (var party in parties)
            {
                try
                {
                    var mapEvent = party.MapEvent;
                    if (mapEvent == null || (!mapEvent.IsFinalized && live.Contains(mapEvent)))
                        continue;
                    if (!act)
                    {
                        LinkCandidates[party] = mapEvent;
                        continue;
                    }
                    if (!LinkCandidates.TryGetValue(party, out var seen) || seen != mapEvent)
                        continue;
                    if (!freed.TryGetValue(mapEvent, out var names))
                        freed[mapEvent] = names = new List<string>();
                    names.Add(party.Name?.ToString() ?? "?");
                    // Leaving the side also lets go of the parties attached to this one.
                    party.MapEventSide = null;
                }
                catch (Exception ex)
                {
                    Log("Freeing " + (party.Name?.ToString() ?? "?") + " from an ended battle failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            if (!act)
                return;
            foreach (var pair in freed)
                Fix("Freed " + pair.Value.Count + " parties from a battle that had already ended (" + Describe(pair.Key) + "): " + string.Join(", ", pair.Value) + ".");
            Log("Checked parties for links to an ended battle; " + freed.Values.Sum(names => names.Count) + " freed.");
        }

        // Check 5: lord parties left with no healthy men outside any battle (for example after
        // Coop force-finished a broken battle on load). An empty party has nothing to do, so the
        // lord sits on the map for good. The lord gets the game's own "escaped after defeat"
        // handling: the empty party goes, the lord becomes a fugitive, and the game brings him
        // back in a settlement later to raise a new party. Nobody is killed.
        private static void CheckEmptyLordParties()
        {
            List<MobileParty> parties;
            try
            {
                parties = MobileParty.All?.ToList() ?? new List<MobileParty>();
            }
            catch (Exception ex)
            {
                Log("Reading the parties failed: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            var reset = 0;
            foreach (var party in parties)
            {
                string name = "?";
                try
                {
                    if (party == null || !party.IsActive || !party.IsLordParty || party.IsMainParty || party.IsDisbanding)
                        continue;
                    var lord = party.LeaderHero;
                    if (lord == null || !lord.IsAlive || lord.IsHumanPlayerCharacter || lord.IsPrisoner)
                        continue;
                    // Coop player parties (their ids start with "Player") are never touched.
                    if ((party.StringId ?? "").StartsWith("Player", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (party.MapEvent != null || party.MemberRoster.TotalHealthyCount > 0)
                        continue;

                    name = lord.Name?.ToString() ?? lord.StringId;
                    var clan = lord.Clan?.Name?.ToString() ?? "no clan";

                    // Let go of anyone held in the empty party, then anyone travelling with the lord.
                    foreach (var element in party.PrisonRoster.GetTroopRoster().ToList())
                        if (element.Character?.HeroObject is Hero prisoner && prisoner.IsAlive)
                            EndCaptivityAction.ApplyByEscape(prisoner, null, false);
                    foreach (var element in party.MemberRoster.GetTroopRoster().ToList())
                        if (element.Character?.HeroObject is Hero member && member != lord && member.IsAlive)
                            MakeHeroFugitiveAction.Apply(member, false);

                    var army = party.Army;
                    if (army != null)
                    {
                        if (army.LeaderParty == party)
                            DisbandArmyAction.ApplyByArmyLeaderIsDead(army);
                        else
                            party.Army = null;
                    }

                    MakeHeroFugitiveAction.Apply(lord, false);
                    reset++;
                    Fix("Reset " + name + " (" + clan + "): the party had no healthy men and could not move. The lord will come back in a settlement and raise a new party.");
                }
                catch (Exception ex)
                {
                    Log("Resetting " + name + " failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            Log("Checked lord parties for an empty party; " + reset + " reset.");
        }

        private static int HealthyMen(MapEventSide side)
        {
            var total = 0;
            List<MapEventParty> parties;
            try { parties = side.Parties?.ToList() ?? new List<MapEventParty>(); }
            catch { return 0; }
            foreach (var party in parties)
            {
                try { total += party?.Party?.NumberOfHealthyMembers ?? 0; }
                catch { }
            }
            return total;
        }

        // ---- Tick: run the queued fixes outside the game's own loops, then report.

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if (Campaign.Current == null)
                return;

            if (_leavePending)
            {
                _leavePending = false;
                LeaveSettlementNow();
            }

            List<MapEvent> batch = null;
            lock (Gate)
            {
                if (ToClose.Count > 0)
                {
                    batch = ToClose.ToList();
                    ToClose.Clear();
                    foreach (var mapEvent in batch)
                        Closed.Add(mapEvent);
                }
            }
            if (batch != null)
                foreach (var mapEvent in batch)
                    CloseBattle(mapEvent);

            if (_campaignSeen == DateTime.MinValue)
                _campaignSeen = DateTime.UtcNow;
            // After the load has settled, before the report. Each check looks twice, five seconds apart.
            var seconds = (DateTime.UtcNow - _campaignSeen).TotalSeconds;
            if (!_emptySeen && seconds >= 20)
            {
                _emptySeen = true;
                CheckEmptyBattles(act: false);
            }
            if (!_emptyChecked && seconds >= 25)
            {
                _emptyChecked = true;
                CheckEmptyBattles(act: true);
            }
            // After the closed battles have let their parties go: first unhook parties from
            // battles that already ended, then (full mode) reset the lords left with an empty party.
            if (!_linksSeen && seconds >= 30)
            {
                _linksSeen = true;
                CheckEndedBattleLinks(act: false);
            }
            if (!_linksChecked && seconds >= 35)
            {
                _linksChecked = true;
                CheckEndedBattleLinks(act: true);
            }
            // Removes a lord's party and frees its prisoners, so it never runs by itself.
            if (!_partiesChecked && seconds >= 40)
            {
                _partiesChecked = true;
                if (_mode == RescueMode.Full)
                    CheckEmptyLordParties();
            }
            if (!_reported && seconds >= 60)
            {
                _reported = true;
                string[] fixes;
                lock (Gate)
                    fixes = Fixes.ToArray();
                const string Captive = " The server's own hero is a prisoner in this save, and the captivity menu stays shut while this module loads.";
                Log(fixes.Length == 0
                    ? (_captivitySeen ? "Report: nothing to fix was found." + Captive
                        : "Report: nothing to fix was found. This save does not have the problems this module knows.")
                    : (_mode == RescueMode.Full
                        ? "Report: fixed " + fixes.Length + " problem(s). Save now to keep the fixes. Full mode is meant for one start only."
                        : "Report: fixed " + fixes.Length + " problem(s). The next save keeps the fixes.") + (_captivitySeen ? Captive : ""));
            }
        }

        // ---- helpers ------------------------------------------------------------

        private static void Fix(string text)
        {
            lock (Gate)
                Fixes.Add(text);
            Log("FIXED: " + text);
        }

        private static IEnumerable<MapEventSide> Sides(MapEvent mapEvent)
        {
            MapEventSide[] sides = null;
            try { sides = typeof(MapEvent).GetField("_sides", Any)?.GetValue(mapEvent) as MapEventSide[]; }
            catch { }
            return sides?.Where(side => side != null) ?? Enumerable.Empty<MapEventSide>();
        }

        private static IEnumerable<string> PartyLines(MapEvent mapEvent)
        {
            var index = 0;
            foreach (var side in Sides(mapEvent))
            {
                var label = index++ == 0 ? "Defenders" : "Attackers";
                List<MapEventParty> parties;
                try { parties = side.Parties?.ToList() ?? new List<MapEventParty>(); }
                catch (Exception ex) { parties = new List<MapEventParty>(); label += " (could not read: " + ex.Message + ")"; }
                yield return label + ": " + parties.Count + " parties";
                foreach (var party in parties)
                    yield return "    " + Name(party);
            }
        }

        private static string Name(MapEventParty party)
        {
            if (party == null)
                return "<empty slot>";
            try
            {
                var basic = party.Party;
                if (basic == null)
                    return "<party missing>";
                return basic.Name + " [" + (basic.MobileParty?.StringId ?? basic.Settlement?.StringId ?? "?") + "]";
            }
            catch (Exception ex)
            {
                return "<unreadable: " + ex.Message + ">";
            }
        }

        private static string Describe(MapEvent mapEvent)
        {
            try
            {
                var place = mapEvent.MapEventSettlement?.Name?.ToString();
                return "'" + mapEvent + "'" + (place != null ? " at " + place : "");
            }
            catch
            {
                return "<unreadable battle>";
            }
        }

        private static void Log(string text)
        {
            var line = Tag + text;
            try { Debug.Print(line); }
            catch { }
            try { Console.WriteLine(line); }
            catch { }
        }
    }
}
