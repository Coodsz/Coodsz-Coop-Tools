using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace StuckBattleFix
{
    // One-time repair for a co-op save with a broken battle on the map.
    //
    // A battle whose update throws on every campaign tick stops the whole map:
    // MapEventManager.Tick dies on it, so most parties never get their turn.
    // This mod:
    //   1. Catches that exception so the rest of the map keeps ticking.
    //   2. After a battle has failed once, closes it (FinalizeEvent).
    //      If closing throws too, it detaches the parties and marks the battle
    //      finished so the game drops it on the next tick.
    // A few seconds after a save is loaded it also ends battles that never throw but can never end
    // (one side with no healthy troops left, see LoadSweep).
    // Every step is written to the server log with "[StuckBattleFix]".
    // Load the save once with this mod, let it save, then remove the mod.
    public sealed class SubModule : MBSubModuleBase
    {
        private const string HarmonyId = "coodsz.stuckbattlefix";
        private const int FailuresBeforeFix = 1;

        private static readonly Dictionary<MapEvent, int> Failures = new Dictionary<MapEvent, int>();
        private static readonly List<MapEvent> ToFix = new List<MapEvent>();
        private static readonly HashSet<MapEvent> Fixed = new HashSet<MapEvent>();
        private static readonly object Gate = new object();
        private static int _closed;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                var update = AccessTools.Method(typeof(MapEvent), "Update");
                if (update == null)
                {
                    Log("MapEvent.Update was not found; nothing patched.");
                    return;
                }

                new Harmony(HarmonyId).Patch(update, finalizer: new HarmonyMethod(typeof(SubModule), nameof(UpdateFinalizer)));
                Log("Ready. Broken battles will be closed when they fail " + FailuresBeforeFix + " ticks in a row.");
            }
            catch (Exception ex)
            {
                Log("Could not patch MapEvent.Update: " + ex);
            }
        }

        // Runs after MapEvent.Update. Returning null swallows the exception so
        // MapEventManager.Tick goes on with the other battles and the campaign keeps ticking.
        private static Exception UpdateFinalizer(MapEvent __instance, Exception __exception)
        {
            if (__exception == null || __instance == null)
                return __exception;

            lock (Gate)
            {
                if (Fixed.Contains(__instance))
                    return null;
                Failures.TryGetValue(__instance, out var count);
                count++;
                Failures[__instance] = count;
                if (count == 1)
                    Log("Battle " + Describe(__instance) + " failed its update: " + __exception.GetType().Name + ": " + __exception.Message
                        + Environment.NewLine + __exception.StackTrace);
                if (count >= FailuresBeforeFix && !ToFix.Contains(__instance))
                    ToFix.Add(__instance);
            }

            return null;
        }

        // Fixes happen here, outside MapEventManager.Tick, so the battle list is not
        // changed while the game is walking through it.
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            LoadSweep.Tick(dt);
            List<MapEvent> batch;
            lock (Gate)
            {
                if (ToFix.Count == 0)
                    return;
                batch = ToFix.ToList();
                ToFix.Clear();
                foreach (var mapEvent in batch)
                    Fixed.Add(mapEvent);
            }

            if (Campaign.Current == null)
                return;

            foreach (var mapEvent in batch)
                Close(mapEvent);
        }

        private static void Close(MapEvent mapEvent)
        {
            var description = Describe(mapEvent);
            Log("Closing broken battle " + description);
            foreach (var line in PartyLines(mapEvent))
                Log("  " + line);

            try
            {
                mapEvent.FinalizeEvent();
                if (mapEvent.IsFinalized)
                {
                    _closed++;
                    Log("Closed it normally. Battles closed so far: " + _closed + ". Let the server save, then remove this mod.");
                    return;
                }

                Log("FinalizeEvent returned but the battle is not finished; forcing it.");
            }
            catch (Exception ex)
            {
                Log("FinalizeEvent failed (" + ex.GetType().Name + ": " + ex.Message + "); forcing it.");
            }

            ForceClose(mapEvent);
        }

        // Last resort: take every party out of the battle and mark it finished.
        // MapEventManager.Tick removes finished battles from the map on its next pass.
        internal static void ForceClose(MapEvent mapEvent)
        {
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
                var state = AccessTools.Field(typeof(MapEvent), "_state");
                if (state != null)
                    state.SetValue(mapEvent, Enum.ToObject(state.FieldType, 2)); // MapEventState.WaitingRemoval
                AccessTools.Field(typeof(MapEvent), "_isFinishCalled")?.SetValue(mapEvent, true);
            }
            catch (Exception ex)
            {
                Log("  Could not mark the battle finished: " + ex.Message);
            }

            _closed++;
            Log("Forced it closed (" + detached + " parties detached, finished=" + SafeIsFinalized(mapEvent) + "). Battles closed so far: " + _closed
                + ". Let the server save, then remove this mod.");
        }

        private static IEnumerable<MapEventSide> Sides(MapEvent mapEvent)
        {
            MapEventSide[] sides = null;
            try { sides = AccessTools.Field(typeof(MapEvent), "_sides")?.GetValue(mapEvent) as MapEventSide[]; }
            catch { }
            return sides?.Where(side => side != null) ?? Enumerable.Empty<MapEventSide>();
        }

        internal static IEnumerable<string> PartyLines(MapEvent mapEvent)
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
                var id = basic.MobileParty?.StringId ?? basic.Settlement?.StringId ?? "?";
                return basic.Name + " [" + id + "]";
            }
            catch (Exception ex)
            {
                return "<unreadable: " + ex.Message + ">";
            }
        }

        internal static string Describe(MapEvent mapEvent)
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

        private static bool SafeIsFinalized(MapEvent mapEvent)
        {
            try { return mapEvent.IsFinalized; }
            catch { return false; }
        }

        internal static void Log(string text)
        {
            var line = "[StuckBattleFix] " + text;
            try { Debug.Print(line); }
            catch { }
            try { Console.WriteLine(line); }
            catch { }
        }
    }
}
