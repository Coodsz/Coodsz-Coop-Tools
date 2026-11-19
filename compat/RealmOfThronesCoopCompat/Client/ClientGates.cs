using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RealmOfThronesCoopCompat
{
    // Players' PCs only. In a Coop session the server runs Realm of Thrones' world code and Coop
    // sends the result, so on a player's PC in the server's world RoT's world changing handlers and
    // patches stop (ClientRules' tables, applied by name once ROT.dll is loaded; nothing names it at
    // compile time). Also here: the roster edit inside ROTOthersCampaignBehavior.OnDailyTick and an
    // elephant ridden by another PC's agent. ClientDisable does the disable set, the Storyline Wars
    // editor and the mission start check. Single player and the server never see any of it.
    internal static class ClientGates
    {
        private static readonly object Sync = new object();
        private static readonly object LogSync = new object();
        private static readonly List<ClientRules.Gate> Pending = new List<ClientRules.Gate>();
        private static readonly List<string> Missing = new List<string>();
        private static readonly HashSet<string> On = new HashSet<string>();
        private static readonly List<string> Rebuilt = new List<string>();
        private static Harmony _harmony;
        private static Assembly _rot;
        private static bool _done;
        private static bool _dailyDone;
        private static bool _elephantDone;
        private static bool _actionsLoaded;
        private static int _behaviorsOn;
        private static int _patchesOn;
        private static int _dailyTickCalls;

        // True once every RoT method that reads ROTDuelsBehavior.Instance without a null check is gated.
        internal static bool DuelsReadersGated()
        {
            lock (Sync)
                return ClientRules.DuelsReadersGated(On);
        }

        public static void Install()
        {
            _harmony = new Harmony(MapRedirect.HarmonyId);
            ClientRules.IsGated = IsGated;
            ClientRules.OnError = scope => AnswerOnError(scope, null);
            Pending.AddRange(ClientRules.BehaviorGates);
            Pending.AddRange(ClientRules.PatchGates);
            Coop.SessionChanged += _ => _state = null;
            ClientDisable.Install(_harmony);
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryApply(e.LoadedAssembly, false);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryApply(assembly, false);
            // A type that does not load yet, or a lookup or patch that throws, is asked for again at
            // campaign start, and only then given up on, with its name in the summary line.
            Coop.CampaignBegan += () =>
            {
                _actionsLoaded = true;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    TryApply(assembly, true);
                if (_rot == null && !_done)
                {
                    _done = true;
                    SubModule.Log("!!! Client gates: a campaign started but ROT.dll is not loaded, so none of Realm of Thrones' world code is gated on this PC.");
                }
            };
        }

        // The game has loaded its action data by the first screen (SubModule), so the elephant gate
        // goes on now (ClientRules.PatchElephantTick says why not before).
        internal static void ActionsLoaded()
        {
            _actionsLoaded = true;
            TryApply(_rot, false);
        }

        // The gate's answer, kept for a second (the event tick asks every frame and Coop's server
        // check walks every loaded assembly) and asked again on a session edge or a new game. The
        // elephant gate asks from the parallel tick, so the answer is one object swapped whole.
        private sealed class State
        {
            public readonly Game Game;
            public readonly int CheckedAt;
            public readonly bool World;
            public readonly bool Session;

            public State(Game game, int checkedAt, bool world, bool session)
            {
                Game = game;
                CheckedAt = checkedAt;
                World = world;
                Session = session;
            }
        }

        private static volatile State _state;
        private static bool? _worldLogged;

        internal static bool IsGated(ClientRules.Scope scope)
        {
            var game = Game.Current;
            var now = Environment.TickCount;
            var state = _state;
            if (state == null || unchecked(now - state.CheckedAt) >= 1000 || !ReferenceEquals(game, state.Game))
            {
                try
                {
                    var playerPc = Coop.OnPlayerPc;
                    var loading = (game?.GameManager as SandBoxGameManager)?.LoadingSavedGame;
                    state = new State(game, now, ClientRules.Gated(playerPc, loading, ClientRules.Scope.World), ClientRules.Gated(playerPc, loading, ClientRules.Scope.Session));
                }
                catch (Exception ex)
                {
                    LogErrorOnce(ex);
                    state = new State(game, now, ErrorAnswer(state?.World), ErrorAnswer(state?.Session));
                }
                _state = state;
                lock (LogSync)
                {
                    if (_worldLogged != state.World && (state.World || _worldLogged != null))
                    {
                        _worldLogged = state.World;
                        SubModule.Log(state.World
                            ? "Client gates: on, this PC is in the server's world."
                            : "Client gates: off, this PC left the server's world.");
                    }
                }
            }
            return scope == ClientRules.Scope.World ? state.World : state.Session;
        }

        private static bool _errorLogged;

        private static void LogErrorOnce(Exception ex)
        {
            lock (LogSync)
            {
                if (_errorLogged)
                    return;
                _errorLogged = true;
            }
            SubModule.Log("!!! Client gates: whether this PC is in the server's world could not be read" + (ex == null ? "" : " (" + ex.GetType().Name + ": " + ex.Message + ")")
                + ", so the gates stay on while a Coop session runs (logged once).");
        }

        // The answer when the check throws: gated while Coop's session runs; when even that cannot
        // be read, the last answer, else gated.
        private static bool ErrorAnswer(bool? last)
        {
            bool? inSession;
            try { inSession = Coop.InSession; }
            catch { inSession = null; }
            return ClientRules.ErrorAnswer(inSession, last);
        }

        internal static bool AnswerOnError(ClientRules.Scope scope, Exception ex)
        {
            LogErrorOnce(ex);
            var state = _state;
            return ErrorAnswer(state == null ? (bool?)null : scope == ClientRules.Scope.World ? state.World : state.Session);
        }

        private static void TryApply(Assembly assembly, bool last)
        {
            try
            {
                if (assembly == null || assembly.GetName().Name != "ROT")
                    return;
                lock (Sync)
                {
                    if (_done)
                        return;
                    if (_rot == null)
                    {
                        _rot = assembly;
                        ClientDisable.RotLoaded(assembly, last);
                    }
                    ApplySpecial(last);
                    var patched = new List<MethodInfo>();
                    foreach (var gate in Pending.ToList())
                    {
                        Type type;
                        try { type = assembly.GetType(gate.Type, false); }
                        catch { type = null; }
                        if (type == null && !last)
                            continue;
                        // Null: the lookup or the patch threw, maybe since a type it needs is not
                        // loaded yet, so it is asked again until the campaign starts.
                        var applied = Apply(gate, type, last, out var method);
                        if (applied == null)
                            continue;
                        Pending.Remove(gate);
                        if (applied == false)
                            continue;
                        On.Add(gate.Name);
                        if (ClientRules.PatchGates.Contains(gate))
                        {
                            _patchesOn++;
                            patched.Add(method);
                        }
                        else
                        {
                            _behaviorsOn++;
                        }
                    }
                    Rebuild(patched);
                    if (Pending.Count > 0 || !_dailyDone || !_elephantDone)
                        return;
                    _done = true;
                    var missingPatches = Missing.Where(name => ClientRules.PatchGates.Any(gate => gate.Name == name)).ToList();
                    var missingBehaviors = Missing.Except(missingPatches).ToList();
                    SubModule.Log(ClientRules.SummaryLine("Client gates", _behaviorsOn, missingBehaviors));
                    SubModule.Log(ClientRules.SummaryLine("Client patch gates", _patchesOn, missingPatches)
                        + " Game methods rebuilt so they call the gated patches: " + Rebuilt.Count
                        + (Rebuilt.Count == 0 ? "." : " (" + string.Join(", ", Rebuilt) + ")."));
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Client gates could not be applied: " + ex.Message);
            }
        }

        // True when patched, false when the method is not there, null to ask again later.
        private static bool? Apply(ClientRules.Gate gate, Type type, bool last, out MethodInfo method)
        {
            method = null;
            try
            {
                method = type == null ? null : ClientRules.FindMethod(type, gate.Method, gate.Args);
                if (method == null)
                {
                    Missing.Add(gate.Name);
                    return false;
                }
                var prefix = gate.Answer == ClientRules.Answer.RunOriginal ? nameof(ClientRules.RunOriginalInWorld)
                    : gate.Scope == ClientRules.Scope.Session ? nameof(ClientRules.SkipInSession)
                    : nameof(ClientRules.SkipInWorld);
                _harmony.Patch(method, prefix: new HarmonyMethod(typeof(ClientRules), prefix));
                return true;
            }
            catch (Exception ex)
            {
                if (!last)
                    return null;
                SubModule.Log("Client gate: could not patch " + gate.Name + ": " + ex.Message);
                Missing.Add(gate.Name);
                return false;
            }
        }

        // The game methods Realm of Thrones patched were built when RoT loaded, and a small RoT patch
        // method (patch9 is 13 bytes of IL) may sit inlined in that copy, out of reach of the gate on
        // it. Adding and removing an empty postfix makes Harmony build each such game method again,
        // now calling RoT's method (Harmony keeps a patched method from being inlined).
        private static void Rebuild(List<MethodInfo> patched)
        {
            if (patched.Count == 0)
                return;
            var empty = new HarmonyMethod(typeof(ClientGates), nameof(EmptyPostfix));
            foreach (var original in Harmony.GetAllPatchedMethods().ToList())
            {
                try
                {
                    var info = Harmony.GetPatchInfo(original);
                    var uses = info != null && info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers)
                        .Any(patch => patched.Any(method => Same(patch.PatchMethod, method)));
                    if (!uses)
                        continue;
                    _harmony.Patch(original, postfix: empty);
                    _harmony.Unpatch(original, empty.method);
                    Rebuilt.Add(original.DeclaringType?.Name + "." + original.Name);
                }
                catch (Exception ex)
                {
                    SubModule.Log("Client patch gates: could not rebuild " + original?.DeclaringType?.Name + "." + original?.Name + ": " + ex.Message);
                }
            }
        }

        private static void EmptyPostfix()
        {
        }

        internal static bool Same(MethodInfo a, MethodInfo b) =>
            a != null && b != null && a.MetadataToken == b.MetadataToken && a.Module == b.Module;

        // The two gates that are not a plain skip, each its own try and line. A type that is not
        // there yet or a patch that throws is tried again on the next pass and given up on at the last.
        private static void ApplySpecial(bool last)
        {
            if (!_dailyDone)
            {
                try
                {
                    var type = _rot.GetType("ROT.CampaignBehaviors.ROTOthersCampaignBehavior", false);
                    var daily = type == null ? null : ClientRules.FindMethod(type, "OnDailyTick", null);
                    if (daily == null)
                    {
                        if (type != null || last)
                        {
                            Missing.Add("ROTOthersCampaignBehavior.OnDailyTick");
                            _dailyDone = true;
                        }
                    }
                    else
                    {
                        _harmony.Patch(daily, transpiler: new HarmonyMethod(typeof(ClientGates), nameof(DailyTickTranspiler)));
                        _dailyDone = true;
                        if (_dailyTickCalls == 1)
                        {
                            _behaviorsOn++;
                            SubModule.Log("Client gate: ROTOthersCampaignBehavior.OnDailyTick adds no hero to a White Walker party here; its invasion flag and party growth still count.");
                        }
                        else
                        {
                            Missing.Add("ROTOthersCampaignBehavior.OnDailyTick (" + _dailyTickCalls + " roster calls)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (last)
                    {
                        SubModule.Log("Client gate: could not patch ROTOthersCampaignBehavior.OnDailyTick: " + ex.Message);
                        Missing.Add("ROTOthersCampaignBehavior.OnDailyTick");
                        _dailyDone = true;
                    }
                }
            }
            if (!_elephantDone)
            {
                try
                {
                    var type = _rot.GetType("RoT_Elephants.RoTElephantAgentComponent", false);
                    var patched = ClientRules.PatchElephantTick(type, _actionsLoaded,
                        tick => _harmony.Patch(tick, prefix: new HarmonyMethod(typeof(ClientGates), nameof(ElephantPrefix))));
                    if (patched == true)
                    {
                        _elephantDone = true;
                        _behaviorsOn++;
                        SubModule.Log("Client gate: an elephant whose rider another PC moves does not attack here. Coop's puppet riders have no controller in town and village fights; in battles that is not checked yet.");
                    }
                    else if (patched == false && (type != null || last))
                    {
                        Missing.Add("RoTElephantAgentComponent.OnTickParallel");
                        _elephantDone = true;
                    }
                }
                catch (Exception ex)
                {
                    if (last)
                    {
                        SubModule.Log("Client gate: could not patch RoTElephantAgentComponent.OnTickParallel: " + ex.Message);
                        Missing.Add("RoTElephantAgentComponent.OnTickParallel");
                        _elephantDone = true;
                    }
                }
            }
        }

        // The stray White Walker hero RoT puts into a war party each day: the one roster call in
        // OnDailyTick goes through RosterOnServer, with the same stack and the same int answer.
        private static IEnumerable<CodeInstruction> DailyTickTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var addToCounts = AccessTools.Method(typeof(TroopRoster), nameof(TroopRoster.AddToCounts),
                new[] { typeof(CharacterObject), typeof(int), typeof(bool), typeof(int), typeof(int), typeof(bool), typeof(int) });
            var wrapper = AccessTools.Method(typeof(ClientGates), nameof(RosterOnServer));
            var list = instructions.ToList();
            _dailyTickCalls = 0;
            foreach (var instruction in list.Where(instruction => addToCounts != null && instruction.Calls(addToCounts)))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = wrapper;
                _dailyTickCalls++;
            }
            return list;
        }

        private static int RosterOnServer(TroopRoster roster, CharacterObject character, int count, bool insertAtFront, int woundedCount, int xpChange, bool removeDepleted, int index)
        {
            bool gated;
            try { gated = IsGated(ClientRules.Scope.World); }
            catch (Exception ex) { gated = AnswerOnError(ClientRules.Scope.World, ex); }
            return gated ? 0 : roster.AddToCounts(character, count, insertAtFront, woundedCount, xpChange, removeDepleted, index);
        }

        private static FieldInfo _componentAgent;
        private static PropertyInfo _riderAgent;
        private static PropertyInfo _controller;

        // An elephant whose rider another PC moves (a Coop puppet, controller None) attacks only on
        // that PC. RoT already skips the main agent's mount. Runs on the parallel tick, so it only
        // reads. The agent is read by reflection: naming Agent's members here would need
        // TaleWorlds.DotNet at compile time.
        private static bool ElephantPrefix(AgentComponent __instance)
        {
            try
            {
                if (!IsGated(ClientRules.Scope.World))
                    return true;
                _componentAgent = _componentAgent ?? AccessTools.Field(typeof(AgentComponent), "Agent");
                var agent = _componentAgent?.GetValue(__instance);
                _riderAgent = _riderAgent ?? agent?.GetType().GetProperty("RiderAgent", BindingFlags.Instance | BindingFlags.Public);
                var rider = agent == null ? null : _riderAgent?.GetValue(agent, null);
                _controller = _controller ?? rider?.GetType().GetProperty("Controller", BindingFlags.Instance | BindingFlags.Public);
                return rider == null || _controller == null || !Equals(_controller.GetValue(rider, null), AgentControllerType.None);
            }
            catch
            {
                return true;
            }
        }
    }
}
