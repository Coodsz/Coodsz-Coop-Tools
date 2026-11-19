using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security;
using HarmonyLib;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace RealmOfThronesCoopCompat
{
    // Players' PCs only, in the server's world of a Coop join. The first join test died natively
    // two seconds after the map opened: Coop's server keeps every party visible and that flag is
    // saved, so in the first map frame NavalDLC.View built a ship visual for every party at sea
    // (224 or more, 600 to 1,260 entities each) and the engine died at about 180,000 ship entities,
    // the same number in both tries (rotcrash notes). Coop's own visual limiter is off on release
    // clients. A prefix on NavalDLC.View's NavalMobilePartyVisual.AddShipVisual (the one place a
    // party's ship is made; RefreshPartyIcon calls it from ValidateIsDirty) applies
    // JoinRules.ShipVisualRule to a new ship only: none for a party this player cannot see, and at
    // most 4 a frame. ValidateIsDirty itself always runs, so fading, blockade ships, rafts and the
    // clean up of a party that left the sea or the player's sight work as NavalDLC wrote them.
    // RefreshPartyIcon clears the party's naval dirty flag before it asks for the ship, so a held
    // party waits in a list and is marked dirty again once it may have its ship, at most 4 a frame.
    // A ship that is let through is made inside a frame that can catch a corrupted state exception
    // on .NET Framework, so a native failure there is logged with its stack before it goes on as
    // before. When a check cannot be read, every new ship but the player's own is held and that is
    // logged once. Whether the budget applies is decided once per Game (a saved game loaded in a
    // Coop session on a player's PC), so it holds until that game ends, also after Coop clears its
    // session. Never installed on the server. While on the map one "Ship visuals since the last
    // line" line comes at most once a minute when ships were let through, held or delayed, with the
    // seeing range, whether Coop's sweep ran, how near the nearest held ship came, whether the main
    // party is at sea and whether it is night. COODSZ_ROT_SHIPS=off removes the budget for a test.
    internal static class ShipVisualBudget
    {
        private const string VisualTypeName = "NavalDLC.View.Map.Visuals.NavalMobilePartyVisual";
        private const string ManagerTypeName = "NavalDLC.View.Map.Managers.NavalMobilePartyVisualManager";
        private const string SweepTypeName = "GameInterface.Services.MobileParties.PartyVisibilitySweep";

        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static bool _patched;
        private static bool _sweepPatched;
        private static bool _missingLogged;
        private static bool _failureLogged;

        // Per Game.
        private static Game _game;
        private static bool _active;
        private static bool _sweepDone;
        private static Stopwatch _onMap;
        private static bool _reported;
        private static int _built;
        // Coop's sweep runs while the hero is switched, before the map (and this budget's first
        // tick for the Game) opens, so the Game it ran for is kept apart from the per Game reset, weakly so an old Game is never kept alive.
        private static WeakReference<Game> _sweepGame;
        private static bool _shipsOffSaid;
        // Since the last minute line.
        private static Stopwatch _sinceReport;
        private static int _minuteBuilt;
        private static int _minuteNotVisible;
        private static int _minuteDelayed;
        // The nearest ship held back as not in view, measured each time the rule held it, and
        // the held ones that came within twice the seeing range.
        private static float _minuteNearest = float.MaxValue;
        private static bool _minuteNearestVisible;
        private static readonly HashSet<object> MinuteNearby = new HashSet<object>();
        private static readonly HashSet<object> HeldNotVisible = new HashSet<object>();
        private static readonly HashSet<object> HeldOverBudget = new HashSet<object>();
        // Held visuals whose party lost its naval dirty flag, to be marked dirty again.
        private static readonly HashSet<object> Waiting = new HashSet<object>();

        // Per manager tick.
        private static bool _checkFailed;
        private static int _builtThisFrame;
        private static float _sightRange;
        private static TaleWorlds.Library.Vec2 _mainPosition;
        private static MobileParty _mainParty;

        private static Func<object, PartyBase> _party;
        private static FieldInfo _shipField;
        private static Action<object> _addShip;
        [ThreadStatic] private static bool _inGuard;

        public static void Install()
        {
            _harmony = new Harmony(MapRedirect.HarmonyId);
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
            {
                string name;
                try { name = e.LoadedAssembly.GetName().Name; }
                catch { return; }
                if (name == "NavalDLC.View" || name == "GameInterface")
                    TryPatch(false);
            };
            Coop.SessionChanged += started =>
            {
                if (started)
                    TryPatch(false);
            };
            Coop.CampaignBegan += () => TryPatch(true);
            TryPatch(false);
        }

        private static void TryPatch(bool last)
        {
            try
            {
                lock (Gate)
                {
                    if (!_patched)
                    {
                        var visual = Coop.FindType(VisualTypeName);
                        var manager = Coop.FindType(ManagerTypeName);
                        if (visual == null || manager == null)
                        {
                            if (last && !_missingLogged)
                            {
                                _missingLogged = true;
                                SubModule.Log("Ship visuals: NavalDLC.View's ship visual is not loaded (War Sails off?), so there is no ship budget.");
                            }
                        }
                        else
                        {
                            _patched = true;
                            PatchVisual(visual, manager);
                        }
                    }
                    if (!_sweepPatched)
                    {
                        var sweep = Coop.FindType(SweepTypeName)?.GetMethod("RebuildAroundMainParty", BindingFlags.Instance | BindingFlags.Public);
                        if (sweep != null)
                        {
                            _sweepPatched = true;
                            _harmony.Patch(sweep, postfix: new HarmonyMethod(typeof(ShipVisualBudget), nameof(SweepPostfix)));
                        }
                        else if (last)
                        {
                            _sweepPatched = true;
                            SubModule.Log("Ship visuals: Coop's PartyVisibilitySweep not found; a party counts as visible while it is within the main party's seeing range.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _patched = true;
                SubModule.Log("Ship visuals could not hook NavalDLC's ship visuals: " + ex.Message);
            }
        }

        private static void PatchVisual(Type visual, Type manager)
        {
            var addShip = visual.GetMethod("AddShipVisual", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            var tick = manager.GetMethod("OnTick", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float), typeof(float) }, null);
            var mapEntity = visual.GetProperty("MapEntity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _shipField = visual.GetField("_shipEntity", BindingFlags.Instance | BindingFlags.NonPublic);
            if (addShip == null || tick == null || mapEntity == null || _shipField == null)
            {
                SubModule.Log("Ship visuals: NavalMobilePartyVisual.AddShipVisual, its manager's OnTick, MapEntity or _shipEntity not found; there is no ship budget.");
                return;
            }
            var instance = Expression.Parameter(typeof(object), "visual");
            _party = Expression.Lambda<Func<object, PartyBase>>(Expression.Convert(Expression.Property(Expression.Convert(instance, visual), mapEntity), typeof(PartyBase)), instance).Compile();
            _addShip = Expression.Lambda<Action<object>>(Expression.Call(Expression.Convert(instance, visual), addShip), instance).Compile();
            _harmony.Patch(tick, prefix: new HarmonyMethod(typeof(ShipVisualBudget), nameof(TickPrefix)));
            _harmony.Patch(addShip, prefix: new HarmonyMethod(typeof(ShipVisualBudget), nameof(AddShipPrefix)));
            SubModule.Log("Ship visuals: NavalDLC's new ship visuals are made at most " + JoinRules.ShipBuildsPerFrame + " a frame, and only for parties this player can see, in the server's world.");
        }

        // Coop's sweep returns without work while the main party has no position; the saved
        // "visible to all" flags then still stand, so it only counts once it could run.
        private static void SweepPostfix()
        {
            try
            {
                var main = MobileParty.MainParty;
                if (JoinRules.SweepRan(Campaign.Current != null, main != null && main.Position.ToVec2().IsValid))
                {
                    _sweepDone = true;
                    _sweepGame = new WeakReference<Game>(Game.Current);
                }
            }
            catch { }
        }

        // Once per manager tick: whether the budget applies (once per Game), what the main party
        // sees, and the held visuals that may have their ship now.
        private static void TickPrefix()
        {
            try
            {
                _builtThisFrame = 0;
                _checkFailed = false;
                var game = Game.Current;
                if (!ReferenceEquals(game, _game))
                    StartGame(game);
                if (!_active)
                    return;
                _mainParty = MobileParty.MainParty;
                _sightRange = _mainParty == null ? 0f : _mainParty.SeeingRange;
                _mainPosition = _mainParty == null ? default : _mainParty.Position.ToVec2();
                if (_onMap == null)
                    _onMap = Stopwatch.StartNew();
                else if (!_reported && _onMap.Elapsed.TotalSeconds >= 10)
                    Report();
                else if (_reported)
                    MinuteReport();
                Rearm();
            }
            catch (Exception ex)
            {
                // With the budget on, a frame whose checks cannot be read holds every new ship.
                if (_active)
                    CheckFailed("the frame's checks", ex);
            }
        }

        private static void StartGame(Game game)
        {
            lock (Gate)
            {
                _game = game;
                _active = false;
                _sweepDone = false;
                _onMap = null;
                _reported = false;
                _built = 0;
                _sinceReport = null;
                _minuteBuilt = _minuteNotVisible = _minuteDelayed = 0;
                ClearNearest();
                HeldNotVisible.Clear();
                HeldOverBudget.Clear();
                Waiting.Clear();
            }
            if (game == null)
                return;
            try
            {
                var join = Coop.OnPlayerPc && (game.GameManager as SandBoxGameManager)?.LoadingSavedGame == true;
                var variable = Environment.GetEnvironmentVariable(JoinRules.ShipsVariable);
                var on = JoinRules.ProtectionOn(variable, out var notUnderstood);
                var start = JoinRules.ShipGameStart(_sweepGame != null && _sweepGame.TryGetTarget(out var swept) && ReferenceEquals(swept, game), join, on);
                _active = start.Active;
                _sweepDone = start.SweepDone;
                if (join && !_shipsOffSaid && (!on || notUnderstood))
                {
                    _shipsOffSaid = true;
                    SubModule.Log(on ? JoinRules.NotUnderstoodLine(JoinRules.ShipsVariable, variable, "Ship visuals budget") : JoinRules.ShipsOffLine(variable));
                }
            }
            catch (Exception ex)
            {
                // Unknown: hold for this game, as a failed check does.
                _active = true;
                CheckFailed("whether this game is a Coop join", ex);
            }
        }

        // Marks held visuals dirty again once the rule lets them have their ship, at most the
        // frame's budget, so NavalDLC's next ValidateIsDirty asks for the ship again.
        private static void Rearm()
        {
            if (Waiting.Count == 0)
                return;
            var rearmed = 0;
            foreach (var visual in Waiting.ToList())
            {
                if (rearmed >= JoinRules.ShipBuildsPerFrame)
                    break;
                var party = _party(visual);
                var mobile = party?.MobileParty;
                if (mobile == null || !mobile.IsActive || _shipField.GetValue(visual) != null)
                {
                    Waiting.Remove(visual);
                    continue;
                }
                if (Rule(visual, party, mobile, rearmed) != JoinRules.ShipBuild.Build)
                    continue;
                Waiting.Remove(visual);
                mobile.SetNavalVisualAsDirty();
                rearmed++;
            }
        }

        private static JoinRules.ShipBuild Rule(object visual, PartyBase party, MobileParty mobile, int builtThisFrame)
        {
            var hasShip = _shipField.GetValue(visual) != null;
            var isMain = mobile != null && mobile == _mainParty;
            if (_checkFailed)
                return JoinRules.ShipVisualRuleUnreadable(hasShip, isMain);
            var visibleFlag = party != null && party.IsVisible;
            var distance = mobile != null && _mainParty != null ? mobile.Position.ToVec2().Distance(_mainPosition) : float.MaxValue;
            var withinSight = distance <= _sightRange;
            var rule = JoinRules.ShipVisualRule(hasShip, isMain, _sweepDone, visibleFlag, withinSight, builtThisFrame);
            if (rule == JoinRules.ShipBuild.NotVisible && distance < float.MaxValue)
                NoteHeld(visual, distance, visibleFlag);
            return rule;
        }

        private static void NoteHeld(object visual, float distance, bool visibleFlag)
        {
            lock (Gate)
            {
                if (distance < _minuteNearest)
                {
                    _minuteNearest = distance;
                    _minuteNearestVisible = visibleFlag;
                }
                if (distance <= _sightRange * 2)
                    MinuteNearby.Add(visual);
            }
        }

        private static void ClearNearest()
        {
            _minuteNearest = float.MaxValue;
            _minuteNearestVisible = false;
            MinuteNearby.Clear();
        }

        private static bool AddShipPrefix(object __instance, bool __runOriginal)
        {
            if (_inGuard)
                return true;
            // Another prefix (Coop's own limiter on debug builds) already held it back.
            if (!__runOriginal)
                return false;
            if (!_active)
                return true;
            JoinRules.ShipBuild rule;
            bool hasShip;
            try
            {
                var party = _party(__instance);
                hasShip = _shipField.GetValue(__instance) != null;
                rule = Rule(__instance, party, party?.MobileParty, _builtThisFrame);
            }
            catch (Exception ex)
            {
                CheckFailed("a party's checks", ex);
                hasShip = SafeHasShip(__instance);
                rule = JoinRules.ShipVisualRuleUnreadable(hasShip, SafeIsMain(__instance));
            }
            if (rule != JoinRules.ShipBuild.Build)
            {
                lock (Gate)
                {
                    Waiting.Add(__instance);
                    if (!_reported)
                        (rule == JoinRules.ShipBuild.OverBudget ? HeldOverBudget : HeldNotVisible).Add(__instance);
                    if (rule == JoinRules.ShipBuild.OverBudget)
                        _minuteDelayed++;
                    else
                        _minuteNotVisible++;
                }
                return false;
            }
            if (!hasShip)
            {
                _builtThisFrame++;
                _minuteBuilt++;
                if (!_reported)
                    _built++;
            }
            BuildGuarded(__instance);
            return false;
        }

        private static bool SafeHasShip(object visual)
        {
            try { return _shipField.GetValue(visual) != null; }
            catch { return false; }
        }

        private static bool SafeIsMain(object visual)
        {
            try { return _party(visual)?.MobileParty == MobileParty.MainParty; }
            catch { return false; }
        }

        private static void CheckFailed(string what, Exception ex)
        {
            _checkFailed = true;
            if (_failureLogged)
                return;
            _failureLogged = true;
            SubModule.Log("!!! Ship visuals: " + what + " could not be read (" + ex.GetType().Name + ": " + ex.Message
                + "), so every new ship visual but this player's own is held back while that lasts (logged once).");
        }

        // Runs AddShipVisual (through this prefix again, which lets it through) in a frame that
        // .NET Framework lets catch a native access violation: logged with its stack, then thrown on.
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static void BuildGuarded(object visual)
        {
            _inGuard = true;
            try
            {
                _addShip(visual);
            }
            catch (Exception ex)
            {
                SubModule.Log("!!! Ship visuals: building a ship visual failed (" + ex.GetType().Name + "): " + ex);
                try { Console.Out.Flush(); }
                catch { }
                throw;
            }
            finally
            {
                _inGuard = false;
            }
        }

        // At most once a minute on the map, only when something was let through, held or delayed.
        private static void MinuteReport()
        {
            if (_sinceReport == null)
            {
                _sinceReport = Stopwatch.StartNew();
                return;
            }
            int built, notVisible, delayed, waiting, nearby;
            float? nearest;
            bool nearestVisible;
            lock (Gate)
            {
                if (!JoinRules.ShipReportDue(_sinceReport.Elapsed.TotalSeconds, _minuteBuilt, _minuteNotVisible, _minuteDelayed))
                    return;
                built = _minuteBuilt;
                notVisible = _minuteNotVisible;
                delayed = _minuteDelayed;
                waiting = Waiting.Count;
                nearest = _minuteNearest < float.MaxValue ? _minuteNearest : (float?)null;
                nearestVisible = _minuteNearestVisible;
                nearby = MinuteNearby.Count;
                _minuteBuilt = _minuteNotVisible = _minuteDelayed = 0;
                ClearNearest();
            }
            _sinceReport.Restart();
            SubModule.Log(JoinRules.ShipMinuteLine(built, notVisible, delayed, waiting, _sightRange, _sweepDone, nearest, nearby, nearestVisible, Read(() => _mainParty.IsCurrentlyAtSea),
                Read(() => Campaign.Current.IsNight)));
        }

        // Null when it cannot be read (no main party or campaign).
        private static bool? Read(Func<bool> read)
        {
            try { return read(); }
            catch { return null; }
        }

        private static void Report()
        {
            _reported = true;
            // The minute lines count from here, so the first ten seconds are not counted twice.
            lock (Gate)
            {
                _minuteBuilt = _minuteNotVisible = _minuteDelayed = 0;
                ClearNearest();
            }
            string entities = null;
            try
            {
                var scene = Campaign.Current?.MapSceneWrapper?.GetType().GetProperty("Scene")?.GetValue(Campaign.Current.MapSceneWrapper, null);
                entities = scene?.GetType().GetProperty("RootEntityCount")?.GetValue(scene, null)?.ToString();
            }
            catch { }
            int notVisible, overBudget;
            lock (Gate)
            {
                notVisible = HeldNotVisible.Count;
                overBudget = HeldOverBudget.Count;
            }
            SubModule.Log(JoinRules.ShipBudgetLine(_built, notVisible, overBudget, entities)
                + " Coop's visibility sweep " + (_sweepDone ? "has run." : "has not run yet."));
        }
    }
}
