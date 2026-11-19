using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace StuckBattleFix
{
    // One sweep a few seconds after a campaign is loaded, for battles that never throw but never end either.
    //
    // 1. Stalled battles. Vanilla MapEvent.Update only simulates while both sides still have healthy
    //    troops, so a battle whose first update already ran and that now has nobody healthy on one side
    //    and no winner sits on the map for good (Coop issue #3129: a siege assault left behind after a
    //    desync, lords at 0 troops, the town can't be entered). It is ended the way vanilla ends a siege
    //    battle after its mission: CheckIfOneSideHasLost, then FinalizeEvent.
    // 2. Parties still linked to a battle that already ended or is no longer on the map. They are
    //    taken out of it.
    // Battles with a co-op player's party are left alone; Coop's own load cleanup handles those.
    internal static class LoadSweep
    {
        private const float DelaySeconds = 5f;

        private static readonly FieldInfo FirstUpdateIsDone = AccessTools.Field(typeof(MapEvent), "FirstUpdateIsDone");
        private static readonly FieldInfo IsFinishCalled = AccessTools.Field(typeof(MapEvent), "_isFinishCalled");
        private static readonly MethodInfo CheckIfOneSideHasLost = AccessTools.Method(typeof(MapEvent), "CheckIfOneSideHasLost");
        private static readonly FieldInfo PartyMapEventSide = AccessTools.Field(typeof(PartyBase), "_mapEventSide");
        private static readonly FieldInfo BesiegerParties = AccessTools.Field(typeof(TaleWorlds.CampaignSystem.Siege.BesiegerCamp), "_besiegerParties");

        private static Campaign _sweptCampaign;
        private static Campaign _seenCampaign;
        private static float _waited;

        // Called from SubModule.OnApplicationTick, outside MapEventManager.Tick.
        public static void Tick(float dt)
        {
            var campaign = Campaign.Current;
            if (campaign == null || campaign == _sweptCampaign || campaign.MapEventManager == null)
                return;
            if (campaign != _seenCampaign)
            {
                _seenCampaign = campaign;
                _waited = 0f;
            }
            _waited += dt;
            if (_waited < DelaySeconds)
                return;

            _sweptCampaign = campaign;
            Run(campaign);
        }

        private static void Run(Campaign campaign)
        {
            if (FirstUpdateIsDone == null || IsFinishCalled == null || CheckIfOneSideHasLost == null)
            {
                SubModule.Log("Load sweep skipped: this game version renamed MapEvent members it reads.");
                return;
            }

            var battles = campaign.MapEventManager.MapEvents.ToList();
            SubModule.Log("Load sweep: " + battles.Count + " battles on the map.");
            var ended = 0;
            foreach (var mapEvent in battles)
            {
                try
                {
                    if (!IsStalled(mapEvent))
                        continue;
                    if (HasPlayerParty(mapEvent))
                    {
                        SubModule.Log("Left stalled battle " + SubModule.Describe(mapEvent) + " alone: a player's party is in it.");
                        continue;
                    }
                    if (End(mapEvent))
                        ended++;
                }
                catch (Exception ex)
                {
                    SubModule.Log("Could not check battle " + SubModule.Describe(mapEvent) + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            var released = ReleaseEndedLinks(campaign);
            var sieges = EndEmptySieges(campaign);
            if (ended == 0 && released == 0 && sieges == 0)
                SubModule.Log("Load sweep found nothing stuck.");
            else
                SubModule.Log("Load sweep ended " + ended + " stalled battles, released " + released
                    + " parties from ended battles and ended " + sieges + " sieges nobody was besieging. Let the server save, then remove this mod.");
        }

        // A siege whose camp is empty, or whose besiegers have no healthy troops left, can't go on and nothing ends it:
        // after a broken assault the town stays besieged, players get the siege menu instead of the town and can't enter.
        // FinalizeSiegeEvent is the game's own end of a siege; it also finalizes a battle still attached to the town.
        private static int EndEmptySieges(Campaign campaign)
        {
            var manager = campaign.SiegeEventManager;
            if (manager == null || BesiegerParties == null)
                return 0;
            var ended = 0;
            foreach (var siege in manager.SiegeEvents.ToList())
            {
                string place = "a siege";
                try
                {
                    place = siege.BesiegedSettlement?.Name?.ToString() ?? place;
                    var besiegers = (BesiegerParties.GetValue(siege.BesiegerCamp) as IEnumerable<MobileParty>)?.Where(p => p != null).ToList()
                        ?? new List<MobileParty>();
                    var healthy = besiegers.Where(p => p.IsActive).Sum(p => p.MemberRoster?.TotalHealthyCount ?? 0);
                    SubModule.Log("Siege of " + place + ": " + besiegers.Count + " besieger parties, " + healthy + " healthy troops"
                        + (besiegers.Any(IsPlayerParty) ? ", a player's party is in the camp" : "") + ".");
                    if (besiegers.Any(IsPlayerParty) || healthy > 0)
                        continue;

                    SubModule.Log("Ending the siege of " + place + ": " + besiegers.Count + " besieger parties with " + healthy + " healthy troops.");
                    siege.FinalizeSiegeEvent();
                    ended++;
                    SubModule.Log("Ended the siege of " + place + ".");
                }
                catch (Exception ex)
                {
                    SubModule.Log("Could not end the siege of " + place + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            return ended;
        }

        // The only state vanilla Update can't end: its simulation already ran once, there is no winner, and one
        // side has nobody healthy left. Raids are left to their component, which ends them by village hit points.
        private static bool IsStalled(MapEvent mapEvent) =>
            mapEvent != null &&
            !mapEvent.IsFinalized &&
            !(bool)IsFinishCalled.GetValue(mapEvent) &&
            mapEvent.BattleState == BattleState.None &&
            (bool)FirstUpdateIsDone.GetValue(mapEvent) &&
            !mapEvent.IsRaid &&
            mapEvent.AttackerSide != null &&
            mapEvent.DefenderSide != null &&
            (mapEvent.AttackerSide.RecalculateMemberCountOfSide() == 0 ||
                mapEvent.DefenderSide.RecalculateMemberCountOfSide() == 0);

        private static bool End(MapEvent mapEvent)
        {
            SubModule.Log("Ending stalled battle " + SubModule.Describe(mapEvent) + " with "
                + mapEvent.AttackerSide.RecalculateMemberCountOfSide() + " healthy attackers and "
                + mapEvent.DefenderSide.RecalculateMemberCountOfSide() + " healthy defenders");
            foreach (var line in SubModule.PartyLines(mapEvent))
                SubModule.Log("  " + line);

            try
            {
                CheckIfOneSideHasLost.Invoke(mapEvent, null);
                mapEvent.FinalizeEvent();
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                SubModule.Log("Ending it failed (" + inner.GetType().Name + ": " + inner.Message + "); forcing it.");
                SubModule.ForceClose(mapEvent);
                return true;
            }

            SubModule.Log(mapEvent.IsFinalized
                ? "Ended it (" + mapEvent.BattleState + ")."
                : "FinalizeEvent returned but the battle is not finished (" + mapEvent.BattleState + ").");
            return mapEvent.IsFinalized;
        }

        private static int ReleaseEndedLinks(Campaign campaign)
        {
            var live = new HashSet<MapEvent>(campaign.MapEventManager.MapEvents);
            var released = 0;
            foreach (var party in MobileParty.All.ToList())
            {
                PartyBase basic = null;
                try
                {
                    basic = party?.Party;
                    var mapEvent = basic?.MapEventSide?.MapEvent;
                    if (mapEvent == null || (live.Contains(mapEvent) && !mapEvent.IsFinalized) || IsPlayerParty(party))
                        continue;

                    SubModule.Log("Releasing " + basic.Name + " [" + party.StringId + "] from ended battle " + SubModule.Describe(mapEvent));
                    try { basic.MapEventSide = null; }
                    catch { PartyMapEventSide?.SetValue(basic, null); }
                    if (basic.MapEventSide != null)
                        PartyMapEventSide?.SetValue(basic, null);
                    if (party.IsActive)
                        party.SetMoveModeHold();
                    released++;
                }
                catch (Exception ex)
                {
                    SubModule.Log("Could not release " + (basic?.Name?.ToString() ?? "a party") + ": " + ex.Message);
                }
            }

            // A town or castle defends its own siege assault with its settlement party. Left linked to the ended battle,
            // players get the siege menu instead of the town (which then crashes on the empty battle) and can't enter.
            foreach (var settlement in Settlement.All.ToList())
            {
                try
                {
                    var basic = settlement?.Party;
                    var mapEvent = basic?.MapEventSide?.MapEvent;
                    if (mapEvent == null)
                        continue;
                    var ended = !live.Contains(mapEvent) || mapEvent.IsFinalized;
                    SubModule.Log(settlement.Name + " is in battle " + SubModule.Describe(mapEvent) + (ended ? ", which already ended" : ", which is still running"));
                    if (!ended)
                        continue;

                    SubModule.Log("Releasing " + settlement.Name + " [" + settlement.StringId + "] from ended battle " + SubModule.Describe(mapEvent));
                    try { basic.MapEventSide = null; }
                    catch { PartyMapEventSide?.SetValue(basic, null); }
                    if (basic.MapEventSide != null)
                        PartyMapEventSide?.SetValue(basic, null);
                    released++;
                }
                catch (Exception ex)
                {
                    SubModule.Log("Could not release " + (settlement?.Name?.ToString() ?? "a settlement") + ": " + ex.Message);
                }
            }
            return released;
        }

        private static bool HasPlayerParty(MapEvent mapEvent) =>
            mapEvent.InvolvedParties.Any(party => party?.MobileParty != null && IsPlayerParty(party.MobileParty));

        // Coop names every player's party MobileParty_Player<number>.
        private static bool IsPlayerParty(MobileParty party) =>
            party != null && (party.IsMainParty || (party.StringId?.StartsWith("MobileParty_Player", StringComparison.Ordinal) ?? false));
    }
}
