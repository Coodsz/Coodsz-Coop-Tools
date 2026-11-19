using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace RealmOfThronesCoopCompat
{
    // Runs Realm of Thrones' scripted story on the dedicated server, where no screen exists
    // (the table and the lines: RotStoryRules). The host registers ROTEventBehavior with its
    // events on (its row in RotHostTables) and these patches, all from ROT.dll found by name, make
    // its events safe there:
    // - EventBase.OpenPopup pushes no game state. The event is queued and answered when the
    //   behavior's daily, hourly or frame tick returns, as a player's click comes after that tick:
    //   Continue (HandleDoneButton), the only button of every popup. Stacked popups are answered
    //   top first, as a player would close them. With players on the link a popup whose answer
    //   changes the world is asked of them first and answered later (RotStoryPopups, S41).
    //   OpenPopup is small enough to be inlined into its callers, which would skip the prefix. The server's Harmony (2.4.2 on MonoMod.Core)
    //   turns inlining off for every method it patches, and the events' StartEvent and tick code
    //   is first compiled after this runs, so no caller holds an inlined copy.
    // - The reward screen of King's Landing (InventoryScreenHelper.OpenScreenAsReceiveItems)
    //   would push a game state too, so it is skipped while a popup is answered.
    // - Realm of Thrones' story music (PsaiCore.StopMusic and PlaySegment) is skipped, on a
    //   stand in PsaiCore, so psai never reaches the engine's music channels.
    // - The four scripted sieges: the arming code in Continue runs as above. The muster leaves
    //   every player's party and every party in a player's army out, never disbands a player's
    //   army, and the server never pulls its own party into the battle (Coop brings players in).
    //   A siege battle a player is in counts as started, which Realm of Thrones' encounter menu
    //   does for the player in single player and the server never opens.
    // - Player tests that pointed at the one local player mean every player: the invasions spare
    //   players' fiefs and kingdoms (Borash only when a player's clan owns it, as its own test
    //   reads the owner alone), and before Renly's and Stannis' succession every player's
    //   clan leaves that kingdom in rebellion, as single player does for the player's clan. Their
    //   own test reads Hero.MainHero.Clan without a check; the server has its own main hero from
    //   world ready on (the Host: at world ready line), and without one the succession is
    //   skipped, as it would stop half way.
    // - What Coop does not send goes over the link (RotWorldSync, S16): the clan each succession
    //   takes off the campaign's clan list, and King's Landing's scene names that the Blackwater
    //   swaps and swaps back.
    // Off with COODSZ_ROT_STORY=off, and below the host's full level, where the behavior is not
    // registered. A popup guard or tick drain that cannot be applied keeps the behavior's events
    // off, or once a campaign has started its ticks. Every patch has its own try and log line, a
    // failed one counts with the host's failures (RotHost.AddStoryFailures), and none lets an
    // exception out.
    internal static class RotStory
    {
        private const BindingFlags AnyDeclared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const int WatchMilliseconds = 500;
        private const string PsaiAssembly = "TaleWorlds.PSAI";
        private const string PsaiType = "psai.net.PsaiCore";

        private static readonly List<object> Pending = new List<object>();
        private static readonly Dictionary<string, int> PopupCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly List<SiegeWatch> Sieges = new List<SiegeWatch>();
        private static readonly HashSet<string> NoMainHero = new HashSet<string>(StringComparer.Ordinal);
        private static Assembly _rot;
        private static Type _eventBase;
        private static MethodInfo _handleDone;
        private static MethodInfo _handleCancel;
        private static PropertyInfo _title;
        private static PropertyInfo _isActive;
        private static PropertyInfo _totalEvents;
        private static FieldInfo _events;
        private static bool _tried;
        private static bool _draining;
        private static bool _musicLogged;
        private static int _musterReplaced;
        private static int _lastWatch;
        private static int _failed;
        private static int _musterDisbands;
        private static object _muster;
        private static MethodInfo _borash;
        private static MethodInfo _kingsLanding;
        private static int _borashReplaced;
        private static bool _borashLogged;
        // The popup guard, the tick drains and the music skip are all in.
        private static bool _startsEvents;

        // One scripted siege's event, read for the started mark.
        private sealed class SiegeWatch
        {
            public string Name;
            public FieldInfo Instance;
            public FieldInfo Leader;
            public FieldInfo Started;
            public bool FailureLogged;
        }

        public static void Install()
        {
            SubModule.Log(RotStoryRules.SwitchLine(Environment.GetEnvironmentVariable(RotStoryRules.Variable)));
            SubModule.Log(RotPopupRules.SwitchLine(Environment.GetEnvironmentVariable(RotPopupRules.Variable), RotStoryRules.StoryOn));
            if (!RotStoryRules.StoryOn)
                return;
            if (!RotHostRules.HostsBehaviors(RotHost.Running))
            {
                SubModule.Log("Story: the host runs at level " + RotHost.Running.ToString().ToLowerInvariant() + ", not full, so ROTEventBehavior is not registered and nothing is patched.");
                return;
            }
            // The host loads ROT.dll before this runs; a ROT.dll that is not there yet is asked
            // for again at campaign start, and only then given up on.
            if (!TryInstall(false))
                Coop.CampaignBegan += () => TryInstall(true);
        }

        private static bool TryInstall(bool last)
        {
            try
            {
                if (_tried)
                    return true;
                var rot = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => !assembly.IsDynamic && assembly.GetName().Name == "ROT");
                if (rot == null && !last)
                    return false;
                _tried = true;
                if (rot == null)
                {
                    // ROTEventBehavior lives in ROT.dll, so without it there is no story to run.
                    SubModule.Log(RotStoryRules.Loud + "ROT.dll is not loaded at campaign start, so no story patch is applied.");
                    return true;
                }
                if (!Patch(rot) && !BlockEvents(rot, last))
                    SubModule.Log(RotStoryRules.Loud + "ROTEVENTBEHAVIOR RUNS WITHOUT ITS POPUP GUARD: a story popup would stay open on the server for good.");
                // Counted with the host's failures, so a broken story holds a new world back at full.
                RotHost.AddStoryFailures(_failed);
            }
            catch (Exception ex)
            {
                SubModule.Log(RotStoryRules.Loud + "the story patches could not be set up: " + ex);
                RotHost.AddStoryFailures(1);
            }
            return true;
        }

        // Every story patch, each with its own try. True when the popup guard and the three tick
        // drains are in, without which an event would wait for a click for good.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Patch(Assembly rot)
        {
            _rot = rot;
            var harmony = new Harmony(MapRedirect.HarmonyId);
            int applied = 0, failed = 0;
            bool Apply(string name, Action patch)
            {
                try
                {
                    patch();
                    applied++;
                    return true;
                }
                catch (Exception ex)
                {
                    failed++;
                    SubModule.Log(RotStoryRules.Loud + "patch FAILED: " + name + ": " + RotHost.Short(ex));
                    return false;
                }
            }

            _eventBase = rot.GetType(RotStoryRules.BaseType, false);
            var behavior = rot.GetType(RotStoryRules.BehaviorType, false);
            var critical = Apply("EventBase.OpenPopup", () =>
            {
                _handleDone = Find(_eventBase, RotStoryRules.BaseType, "HandleDoneButton");
                _handleCancel = _eventBase.GetMethod("HandleCancelButton", AnyDeclared, null, Type.EmptyTypes, null);
                _title = _eventBase.GetProperty("Title");
                _isActive = _eventBase.GetProperty("IsActive") ?? throw new MissingMemberException(RotStoryRules.BaseType, "IsActive");
                _totalEvents = _eventBase.GetProperty("TotalEvents") ?? throw new MissingMemberException(RotStoryRules.BaseType, "TotalEvents");
                harmony.Patch(Find(_eventBase, RotStoryRules.BaseType, "OpenPopup"), prefix: Hook(nameof(OpenPopupPrefix)));
            });
            foreach (var tick in new[] { "OnDailyTick", "OnHourlyTick", "OnTick" })
                critical &= Apply("ROTEventBehavior." + tick, () => harmony.Patch(Find(behavior, RotStoryRules.BehaviorType, tick), postfix: Hook(tick == "OnTick" ? nameof(FramePostfix) : nameof(TickPostfix))));
            Apply("ROTEventBehavior.OnSessionLaunched", () =>
            {
                _events = behavior?.GetField("_events", AnyDeclared) ?? throw new MissingFieldException(RotStoryRules.BehaviorType, "_events");
                harmony.Patch(Find(behavior, RotStoryRules.BehaviorType, "OnSessionLaunched"), postfix: Hook(nameof(SessionPostfix)));
            });
            Apply("InventoryScreenHelper.OpenScreenAsReceiveItems", () => harmony.Patch(
                Find(typeof(Campaign).Assembly.GetType(RotStoryRules.ReceiveItemsType, false), RotStoryRules.ReceiveItemsType, "OpenScreenAsReceiveItems"), prefix: Hook(nameof(ReceiveItemsPrefix))));

            // TaleWorlds.PSAI is in the server's own bin, maybe not loaded yet.
            Type psai = null;
            var music = Apply("PsaiCore.StopMusic", () =>
            {
                psai = Coop.FindType(PsaiType) ?? Assembly.Load(PsaiAssembly).GetType(PsaiType, true);
                harmony.Patch(Find(psai, PsaiType, "StopMusic", typeof(bool)), prefix: Hook(nameof(SkipMusic)));
            });
            music &= Apply("PsaiCore.PlaySegment", () => harmony.Patch(Find(psai, PsaiType, "PlaySegment", typeof(int)), prefix: Hook(nameof(SkipMusic))));
            // PsaiCore.Instance would make psai's Logik first, whose music channels call the
            // engine's native Music functions (an offline run on the server's DLLs dies there), and
            // the server never made one: the game only starts its music off a dedicated server.
            // With both calls skipped it gets a stand in without a Logik, unless one was made before.
            if (music)
                music = Apply("PsaiCore stand in", () =>
                {
                    var singleton = psai.GetField("s_singleton", AnyStatic) ?? throw new MissingFieldException(PsaiType, "s_singleton");
                    if (singleton.GetValue(null) == null)
                        singleton.SetValue(null, FormatterServices.GetUninitializedObject(psai));
                });

            foreach (var name in RotStoryRules.Sieges)
            {
                var type = rot.GetType(RotStoryRules.Namespace + name, false);
                Apply(name + ".SetupSiegeAttackers", () =>
                {
                    _musterReplaced = 0;
                    _musterDisbands = 0;
                    harmony.Patch(Find(type, name, "SetupSiegeAttackers"), prefix: Hook(nameof(MusterPrefix)), finalizer: Hook(nameof(MusterFinalizer)), transpiler: Hook(nameof(MusterTranspiler)));
                    if (_musterReplaced != 1 || _musterDisbands != 2)
                        throw new InvalidOperationException("its muster test of MobileParty.IsMainParty was found " + _musterReplaced + " times (not once) and its army disbands "
                            + _musterDisbands + " times (not twice)");
                });
                Apply(name + ".SetupPlayerEncounter", () => harmony.Patch(Find(type, name, "SetupPlayerEncounter"), prefix: Hook(nameof(PullInPrefix))));
                Apply(name + " started mark", () => Sieges.Add(new SiegeWatch
                {
                    Name = name,
                    Instance = type?.GetField("Instance", AnyStatic) ?? throw new MissingFieldException(name, "Instance"),
                    Leader = type.GetField("SiegeLeaderParty", AnyDeclared) ?? throw new MissingFieldException(name, "SiegeLeaderParty"),
                    Started = type.GetField("SiegeStarted", AnyDeclared) ?? throw new MissingFieldException(name, "SiegeStarted"),
                }));
            }
            foreach (var name in RotStoryRules.Invasions)
                Apply(name + ".IsPlayerKing", () => harmony.Patch(Find(rot.GetType(RotStoryRules.Namespace + name, false), name, "IsPlayerKing", typeof(Clan)), prefix: Hook(nameof(PlayerKingPrefix))));
            Apply(RotStoryRules.BorashEvent + ".DailyTickEvent Borash", () =>
            {
                _borash = AccessTools.PropertyGetter(rot.GetType(RotStoryRules.SettlementsType, false), "Borash") ?? throw new MissingMemberException(RotStoryRules.SettlementsType, "Borash");
                _borashReplaced = 0;
                harmony.Patch(Find(rot.GetType(RotStoryRules.Namespace + RotStoryRules.BorashEvent, false), RotStoryRules.BorashEvent, "DailyTickEvent"), transpiler: Hook(nameof(BorashTranspiler)));
                if (_borashReplaced != 1)
                    throw new InvalidOperationException("its Borash.OwnerClan != Clan.PlayerClan test was found " + _borashReplaced + " times, not once");
            });
            // The finalizer hands the clans the succession took off the campaign's clan list to the
            // link (S16, RotWorldSync); it runs also when the prefix skipped the succession.
            foreach (var succession in RotStoryRules.Successions)
                Apply(succession.Type + "." + succession.Method, () => harmony.Patch(Find(rot.GetType(RotStoryRules.Namespace + succession.Type, false), succession.Type, succession.Method),
                    prefix: Hook(nameof(SuccessionPrefix)), finalizer: Hook(nameof(SuccessionFinalizer))));
            // King's Landing's scenes for the Battle of the Blackwater and back (S16).
            Apply(RotStoryRules.SceneEvent + "." + RotStoryRules.SceneMethod, () =>
            {
                var type = rot.GetType(RotStoryRules.Namespace + RotStoryRules.SceneEvent, false);
                _kingsLanding = AccessTools.PropertyGetter(rot.GetType(RotStoryRules.SettlementsType, false), "KingsLanding") ?? throw new MissingMemberException(RotStoryRules.SettlementsType, "KingsLanding");
                harmony.Patch(Find(type, RotStoryRules.SceneEvent, RotStoryRules.SceneMethod, typeof(bool)), prefix: Hook(nameof(ScenePrefix)), finalizer: Hook(nameof(SceneFinalizer)));
            });

            SubModule.Log((failed > 0 ? RotStoryRules.Loud : "Story: ") + applied + " of " + (applied + failed) + " story hooks applied"
                + (failed > 0 ? ", " + failed + " FAILED (the lines above say which)" : "") + "; the popup guard and the tick drains are " + (critical ? "in." : "NOT in."));
            _failed = failed;
            _startsEvents = critical && music;
            return critical;
        }

        // Without the popup guard or the tick drains, ROTEventBehavior keeps its events off, as
        // the host does for a behavior kept for its data. Once a campaign has started its events
        // are registered, so its three ticks, where every event starts and runs, do nothing instead.
        private static bool BlockEvents(Assembly rot, bool late)
        {
            try
            {
                var behavior = rot.GetType(RotStoryRules.BehaviorType, false);
                var harmony = new Harmony(MapRedirect.HarmonyId);
                foreach (var method in late ? new[] { "OnDailyTick", "OnHourlyTick", "OnTick" } : new[] { "RegisterEvents" })
                    harmony.Patch(Find(behavior, RotStoryRules.BehaviorType, method), prefix: Hook(nameof(NoEvents)));
                SubModule.Log(RotStoryRules.Loud + "ROTEventBehavior keeps its " + (late ? "ticks" : "events") + " off, since a story popup would stay open on the server for good. The story stands still.");
                return true;
            }
            catch (Exception ex)
            {
                SubModule.Log(RotStoryRules.Loud + "ROTEventBehavior's events could not be kept off either: " + RotHost.Short(ex));
                return false;
            }
        }

        // The Night King's death: the server guard that replaces ROTOthersCampaignBehavior's hourly
        // tick asks for NightKingDeathEvent here, as Realm of Thrones' own tick starts it. Only with
        // the popup guard, the tick drains and the music skip in, so its popup is answered at the
        // next frame tick and psai is never reached. Returns the end of the guard's log line.
        internal static string StartNightKingDeath()
        {
            const string what = "its story event (NightKingDeathEvent) ";
            if (!_startsEvents)
                return what + "is not started: the story does not run on this server (the Story lines say why)";
            try
            {
                var started = Trigger("NightKingDeathEvent");
                if (started == null)
                    return "!!! " + what + "is not started: ROT.SubModule.EventBehavior or its TriggerEvent(Type) is missing";
                return what + (started.Value ? "started" : "had already run");
            }
            catch (Exception ex)
            {
                return "!!! " + what + "could not be started: " + RotHost.Short(ex);
            }
        }

        // The story test hook (RotPopupRules.TestPlan) starts its event here, the way the Night
        // King's death does. Returns the line to log.
        internal static string FireTestEvent(string eventType)
        {
            if (!_startsEvents)
                return RotPopupRules.TestFailedLine(eventType, "the story does not run on this server (the Story lines say why)");
            try
            {
                var started = Trigger(eventType);
                return started == null
                    ? RotPopupRules.TestFailedLine(eventType, "ROT.SubModule.EventBehavior or its TriggerEvent(Type) is missing")
                    : RotPopupRules.TestFiredLine(eventType, started.Value);
            }
            catch (Exception ex)
            {
                return RotPopupRules.TestFailedLine(eventType, RotHost.Short(ex));
            }
        }

        // ROTEventBehavior.TriggerEvent(Type) through ROT.SubModule.EventBehavior; null when either is missing.
        private static bool? Trigger(string eventType)
        {
            var behavior = _rot.GetType(RotHostTables.SubModuleType, true).GetField("EventBehavior", AnyStatic)?.GetValue(null);
            var trigger = behavior?.GetType().GetMethod("TriggerEvent", AnyDeclared, null, new[] { typeof(Type) }, null);
            if (trigger == null)
                return null;
            return (bool)trigger.Invoke(behavior, new object[] { _rot.GetType(RotStoryRules.Namespace + eventType, true) });
        }

        private static MethodInfo Find(Type type, string typeName, string method, params Type[] parameters)
        {
            if (type == null)
                throw new TypeLoadException(typeName + " is not in the loaded assemblies");
            var found = parameters.Length == 0
                ? type.GetMethods(AnyDeclared).Where(m => m.Name == method).ToList()
                : type.GetMethods(AnyDeclared).Where(m => m.Name == method && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)).ToList();
            if (found.Count != 1)
                throw new MissingMethodException(typeName, method + (found.Count == 0 ? "" : " (" + found.Count + " overloads)"));
            return found[0];
        }

        private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(RotStory).GetMethod(name, AnyStatic));

        private static bool NoEvents() => false;

        // No game state on the server: the event waits for its answer at the end of the tick.
        private static bool OpenPopupPrefix(object __instance)
        {
            try
            {
                if (!Coop.OnServerProcess)
                    return true;
                Pending.Add(__instance);
            }
            catch
            {
                // A popup screen would never close on the server.
            }
            return false;
        }

        // The reward screen pushes an inventory state for the main party; on the server nobody
        // takes the items, so the answer only completes the event.
        private static bool ReceiveItemsPrefix()
        {
            try
            {
                if (!Coop.OnServerProcess || !_draining)
                    return true;
                SubModule.Log("Story: a story popup's answer would open the receive items screen; skipped on the server, nobody gets the items.");
            }
            catch { }
            return false;
        }

        private static void TickPostfix() => Drain();

        private static void FramePostfix()
        {
            Drain();
            WatchSieges();
        }

        // Answers every queued popup, top first, with Continue, and logs it. A popup the players
        // are asked (RotStoryPopups) is answered later through ApplyChosen.
        internal static void Drain()
        {
            if (Pending.Count == 0 || _draining)
                return;
            _draining = true;
            try
            {
                while (Pending.Count > 0)
                {
                    var popup = Pending[Pending.Count - 1];
                    Pending.RemoveAt(Pending.Count - 1);
                    if (!RotStoryPopups.Take(popup))
                        Answer(popup, RotPopupRules.DefaultOption);
                }
            }
            catch (Exception ex)
            {
                SubModule.Log(RotStoryRules.Loud + "answering the story popups failed: " + ex.Message);
            }
            finally
            {
                _draining = false;
            }
        }

        // A popup the players were asked, answered with the option they chose or the default, on
        // the game thread, the same way Drain answers one. False when it threw (logged).
        internal static bool ApplyChosen(object popup, int option)
        {
            var was = _draining;
            _draining = true;
            try
            {
                return Answer(popup, option);
            }
            finally
            {
                _draining = was;
            }
        }

        // option 0 is Continue (HandleDoneButton), 1 the second button (HandleCancelButton), which
        // no popup of Realm of Thrones 8.1.8 shows.
        private static bool Answer(object popup, int option)
        {
            var type = popup?.GetType();
            if (type == null || _handleDone == null)
                return false;
            PopupCounts.TryGetValue(type.Name, out var count);
            PopupCounts[type.Name] = ++count;
            string title = null;
            try { title = _title?.GetValue(popup, null) as string; }
            catch { }
            try
            {
                // A virtual call, so the event's own override runs.
                if (option == 1 && _handleCancel != null)
                    _handleCancel.Invoke(popup, null);
                else
                    _handleDone.Invoke(popup, null);
                SubModule.Log(RotStoryRules.PopupLine(type.Name, count, title, RotStoryRules.Row(type.FullName)));
                return true;
            }
            catch (Exception ex)
            {
                SubModule.Log(RotStoryRules.Loud + "event " + type.Name + " answer " + RotStoryRules.Answer + " FAILED: " + RotHost.Short(ex)
                    + " The event is not asked again, so it may stand still from here.");
                // A siege's Continue ends with SetupSiegeArmy = true, which lets its army form at
                // the next hourly tick; set it here so a failed step before it does not stall the siege.
                if (!RotStoryRules.Sieges.Contains(type.Name))
                    return false;
                try
                {
                    (type.GetField("SetupSiegeArmy", AnyDeclared) ?? throw new MissingFieldException(type.Name, "SetupSiegeArmy")).SetValue(popup, true);
                    SubModule.Log("Story: " + type.Name + ": its army still forms at the next hourly tick (SetupSiegeArmy set).");
                }
                catch (Exception armEx)
                {
                    SubModule.Log(RotStoryRules.Loud + type.Name + ": SetupSiegeArmy could not be set, so this siege stands still: " + RotHost.Short(armEx));
                }
                return false;
            }
        }

        // The server plays no music.
        private static bool SkipMusic()
        {
            try
            {
                if (!Coop.OnServerProcess)
                    return true;
                if (!_musicLogged)
                {
                    _musicLogged = true;
                    SubModule.Log("Story: Realm of Thrones' story music is skipped on the server (PsaiCore); later calls are skipped without a line.");
                }
            }
            catch { }
            return false;
        }

        // The muster's single player test "mobileParty.IsMainParty" becomes "main party, a
        // player's party or a party in a player's army", so a player's party is not moved or put
        // into the siege army and a player's army is not pulled out of its settlement or disbanded.
        // Its two DisbandArmyAction calls go through DisbandUnlessPlayers. A list, not an
        // iterator: the compiler's iterator class would name Harmony's CodeInstruction in its
        // interfaces, and the module's types must list without Harmony.
        private static IEnumerable<CodeInstruction> MusterTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var isMainParty = AccessTools.PropertyGetter(typeof(MobileParty), nameof(MobileParty.IsMainParty));
            var disband = AccessTools.Method(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByUnknownReason), new[] { typeof(Army) });
            var replacement = AccessTools.Method(typeof(RotStory), nameof(LeftOutOfMuster));
            var disbandReplacement = AccessTools.Method(typeof(RotStory), nameof(DisbandUnlessPlayers));
            var result = new List<CodeInstruction>();
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(isMainParty))
                {
                    _musterReplaced++;
                    result.Add(new CodeInstruction(OpCodes.Call, replacement) { labels = instruction.labels, blocks = instruction.blocks });
                }
                else if (instruction.Calls(disband))
                {
                    _musterDisbands++;
                    result.Add(new CodeInstruction(OpCodes.Call, disbandReplacement) { labels = instruction.labels, blocks = instruction.blocks });
                }
                else
                    result.Add(instruction);
            }
            return result;
        }

        // The siege event whose muster runs, for DisbandUnlessPlayers.
        private static void MusterPrefix(object __instance) => _muster = __instance;

        private static Exception MusterFinalizer(Exception __exception)
        {
            _muster = null;
            return __exception;
        }

        // The muster disbands the army its own leader sits in, and a mustered lord's other army.
        // A player's army is never disbanded: the story's leader only leaves it (the mustered
        // lords in a player's army are already left out by LeftOutOfMuster).
        public static void DisbandUnlessPlayers(Army army)
        {
            try
            {
                if (army != null && Coop.OnServerProcess && Coop.IsPlayerHero(army.LeaderParty?.LeaderHero))
                {
                    var leader = _muster?.GetType().GetField("SiegeLeaderParty", AnyDeclared)?.GetValue(_muster) as MobileParty;
                    if (leader != null && leader.Army == army && leader != army.LeaderParty)
                        leader.Army = null;
                    SubModule.Log("Story: the siege muster leaves player " + army.LeaderParty.LeaderHero.StringId + "'s army to Coop: it is not disbanded"
                        + (leader != null && leader.Army == null ? "; " + leader.StringId + " leaves it to lead the siege." : "."));
                    return;
                }
            }
            catch
            {
                // Realm of Thrones' own step then.
            }
            DisbandArmyAction.ApplyByUnknownReason(army);
        }

        // Daenerys' invasion hands Borash to Yezzan unless Clan.PlayerClan owns it; its other fiefs
        // also go through IsPlayerKing. On the server Clan.PlayerClan is the server's own clan, so
        // "ROTSettlements.Borash.OwnerClan != Clan.PlayerClan" compares with the owner itself when
        // a player's clan owns it: dup the owner and replace Clan.PlayerClan with OwnerIfPlayer.
        private static IEnumerable<CodeInstruction> BorashTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var ownerClan = AccessTools.PropertyGetter(typeof(TaleWorlds.CampaignSystem.Settlements.Settlement), "OwnerClan");
            var playerClan = AccessTools.PropertyGetter(typeof(Clan), nameof(Clan.PlayerClan));
            var replacement = AccessTools.Method(typeof(RotStory), nameof(OwnerIfPlayer));
            var list = instructions.ToList();
            var result = new List<CodeInstruction>();
            for (var i = 0; i < list.Count; i++)
            {
                if (i >= 2 && list[i].Calls(playerClan) && list[i - 1].Calls(ownerClan) && list[i - 2].Calls(_borash))
                {
                    _borashReplaced++;
                    result.Add(new CodeInstruction(OpCodes.Dup) { labels = list[i].labels, blocks = list[i].blocks });
                    result.Add(new CodeInstruction(OpCodes.Call, replacement));
                    continue;
                }
                result.Add(list[i]);
            }
            return result;
        }

        public static Clan OwnerIfPlayer(Clan owner)
        {
            try
            {
                if (owner != null && Coop.OnServerProcess && Coop.IsPlayerClan(owner))
                {
                    if (!_borashLogged)
                    {
                        _borashLogged = true;
                        SubModule.Log("Story: " + RotStoryRules.BorashEvent + " leaves Borash to clan " + owner.StringId + ": a player's clan owns it, as single player spares the player's own.");
                    }
                    return owner;
                }
            }
            catch
            {
                // Realm of Thrones' own test then.
            }
            return Clan.PlayerClan;
        }

        public static bool LeftOutOfMuster(MobileParty party)
        {
            if (party == null || party.IsMainParty)
                return true;
            try
            {
                if (!Coop.OnServerProcess)
                    return false;
                if (Coop.IsPlayerHero(party.LeaderHero))
                {
                    SubModule.Log("Story: the siege muster leaves player party " + party.StringId + " (" + party.LeaderHero.StringId + ") to Coop: it is not moved or put into the army.");
                    return true;
                }
                var armyLeader = party.Army?.LeaderParty?.LeaderHero;
                if (!Coop.IsPlayerHero(armyLeader))
                    return false;
                SubModule.Log("Story: the siege muster leaves " + party.StringId + " in player " + armyLeader.StringId + "'s army: the army is not pulled out of its settlement or disbanded.");
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Realm of Thrones would start a player encounter for the server's own party.
        private static bool PullInPrefix(object __instance)
        {
            try
            {
                if (!Coop.OnServerProcess)
                    return true;
                SubModule.Log("Story: " + __instance?.GetType().Name + " would pull the server's own party into the siege battle; skipped, Coop brings players in.");
            }
            catch { }
            return false;
        }

        // "Is this clan the player's or in the player's kingdom" for every player: a player's
        // clan, or a clan of a kingdom a player's clan rules. Broader than the original, which needs
        // a kingdom the player rules, since single player spares the player's own clan by a separate
        // Clan.PlayerClan test that here covers only the server's clan; a player's clan is spared
        // with or without a kingdom. Before the Coop session starts no hero counts as a player's,
        // so nobody is spared then; the invasions come years into the story.
        private static bool PlayerKingPrefix(object __instance, Clan __0, ref bool __result)
        {
            try
            {
                if (!Coop.OnServerProcess)
                    return true;
                var ruler = __0?.Kingdom?.RulingClan;
                __result = __0 != null && (Coop.IsPlayerClan(__0) || (ruler != null && Coop.IsPlayerClan(ruler)));
                if (__result)
                    SubModule.Log("Story: " + __instance?.GetType().Name + " leaves clan " + __0.StringId + " alone: it is a player's clan or in a kingdom a player rules.");
                return false;
            }
            catch
            {
                return true;
            }
        }

        // Before the succession every player's clan in that kingdom leaves it in rebellion. The
        // event's own tests point at the server's clan, which is in no kingdom. They read
        // Hero.MainHero.Clan without a check, after Renly's or Stannis' clan is already destroyed,
        // so without a main hero with a clan the succession is skipped instead of stopping half way:
        // Renly's event then completes without it, Stannis' is asked again the next day.
        private static bool SuccessionPrefix(object __instance)
        {
            try
            {
                if (!Coop.OnServerProcess)
                    return true;
                RotWorldSync.BeginClanList();
                var name = __instance?.GetType().Name;
                Clan own = null;
                try { own = Hero.MainHero?.Clan; }
                catch { }
                if (own == null)
                {
                    if (NoMainHero.Add(name ?? ""))
                        SubModule.Log(RotStoryRules.Loud + name + ": the server has no Hero.MainHero with a clan, which its succession reads without a check, so the succession is SKIPPED (logged once).");
                    return false;
                }
                var succession = RotStoryRules.Successions.FirstOrDefault(s => s.Type == name);
                var kingdom = _rot?.GetType(RotStoryRules.KingdomsType, false)?.GetProperty(succession.Kingdom ?? "", AnyStatic)?.GetValue(null, null) as Kingdom;
                if (kingdom == null)
                {
                    SubModule.Log("Story: " + name + ": " + RotStoryRules.KingdomsType + "." + succession.Kingdom + " not found; players' clans in it are moved like the others.");
                    return true;
                }
                foreach (var clan in kingdom.Clans.Where(Coop.IsPlayerClan).ToList())
                {
                    ChangeKingdomAction.ApplyByLeaveWithRebellionAgainstKingdom(clan, false);
                    SubModule.Log("Story: " + name + ": player clan " + clan.StringId + " leaves " + kingdom.StringId + " in rebellion before the succession, as the player's clan does in single player.");
                }
            }
            catch (Exception ex)
            {
                SubModule.Log(RotStoryRules.Loud + "the player step before a succession failed: " + RotHost.Short(ex));
            }
            return true;
        }

        // Renly's and Stannis' succession take their clan off the campaign's clan list, which Coop
        // does not send. RotWorldSync never throws.
        private static Exception SuccessionFinalizer(object __instance, Exception __exception)
        {
            RotWorldSync.EndClanList(__instance?.GetType().Name);
            return __exception;
        }

        // ToggleKingsLandingScene sets the four scene names of King's Landing's center (the
        // Blackwater scene and back), which Coop does not send.
        private static void ScenePrefix()
        {
            try
            {
                if (Coop.OnServerProcess)
                    RotWorldSync.BeginScenes(_kingsLanding?.Invoke(null, null) as TaleWorlds.CampaignSystem.Settlements.Settlement);
            }
            catch
            {
                // Before a campaign there is no King's Landing; nothing to compare then.
            }
        }

        private static Exception SceneFinalizer(Exception __exception)
        {
            RotWorldSync.EndScenes(RotStoryRules.SceneEvent + "." + RotStoryRules.SceneMethod);
            return __exception;
        }

        // A siege battle a player is in counts as started, so the event completes when the siege
        // ends instead of mustering again. Read twice a second at most.
        private static void WatchSieges()
        {
            var now = Environment.TickCount;
            if (unchecked(now - _lastWatch) < WatchMilliseconds && _lastWatch != 0)
                return;
            _lastWatch = now;
            foreach (var siege in Sieges)
            {
                try
                {
                    var story = siege.Instance.GetValue(null);
                    if (story == null || !(bool)_isActive.GetValue(story, null) || (bool)siege.Started.GetValue(story))
                        continue;
                    var leader = siege.Leader.GetValue(story) as MobileParty;
                    var battle = leader?.SiegeEvent == null ? null : leader.MapEvent;
                    var player = battle?.InvolvedParties.Select(party => party?.MobileParty?.LeaderHero).FirstOrDefault(Coop.IsPlayerHero);
                    if (player == null)
                        continue;
                    siege.Started.SetValue(story, true);
                    SubModule.Log("Story: " + siege.Name + ": player hero " + player.StringId + " is in the siege battle, so the siege counts as started (Realm of Thrones' encounter menu marks it for the player).");
                }
                catch (Exception ex)
                {
                    if (siege.FailureLogged)
                        continue;
                    siege.FailureLogged = true;
                    SubModule.Log(RotStoryRules.Loud + siege.Name + ": the started mark failed (logged once): " + RotHost.Short(ex));
                }
            }
        }

        // The game start line, once the behavior made its event list.
        private static void SessionPostfix(object __instance)
        {
            try
            {
                // A popup still queued from the last world is not answered in this one.
                Pending.Clear();
                PopupCounts.Clear();
                NoMainHero.Clear();
                var types = _rot.GetTypes().Where(type => type.IsClass && !type.IsAbstract && _eventBase.IsAssignableFrom(type)).Select(type => type.FullName).ToList();
                var events = (_events?.GetValue(__instance) as IEnumerable)?.Cast<object>().Where(e => e != null).ToList() ?? new List<object>();
                var done = events.Count(e => (int)_totalEvents.GetValue(e, null) > 0);
                var active = events.Count(e => (bool)_isActive.GetValue(e, null));
                SubModule.Log(RotStoryRules.SummaryLine(RotStoryRules.Compare(types), types.Count, events.Count, done, active));
            }
            catch (Exception ex)
            {
                SubModule.Log(RotStoryRules.Loud + "the game start line failed: " + RotHost.Short(ex));
            }
        }
    }
}
