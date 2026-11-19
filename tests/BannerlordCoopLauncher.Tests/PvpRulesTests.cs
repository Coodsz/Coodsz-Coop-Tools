using BannerlordCoopLauncher.Core.Server;
using Coodsz.ServerRules;

namespace BannerlordCoopLauncher.Tests;

// The PvP rules: block leaving and the response timer in player vs player encounters (PvpRulesRules.cs,
// linked, the rules the Coodsz Server Tools module runs), the settings Coodsz Host writes, and the names
// the module patches in Coop v0.1.6.
public sealed class PvpRulesTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coodsz-pvprules-" + Guid.NewGuid().ToString("N"));

    public PvpRulesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // Coop v0.1.6 PlayerPartyInteractionOption and Phase names the rules compare against
    // (source/GameInterface/Services/MapEvents/PlayerPartyInteractions/PlayerPartyInteractionTypes.cs).
    private enum Option { None, TradeProposal, AcceptProposal, DeclineProposal, Leave, HostileDemand, RefuseHostileDemand, YieldHostileDemand, LeaveClan }

    // ---- block leaving ----------------------------------------------------------------------------

    [Fact]
    public void Leave_is_ignored_only_when_the_rule_is_on_and_the_two_are_at_war()
    {
        Assert.True(PvpRule.BlocksLeave(true, true, "Leave", "HostileDemandPending", "HostileDemand"));
        Assert.False(PvpRule.BlocksLeave(true, false, "Leave", "HostileDemandPending", "HostileDemand"));
        Assert.False(PvpRule.BlocksLeave(false, true, "Leave", "HostileDemandPending", "HostileDemand"));
        Assert.False(PvpRule.BlocksLeave(true, true, null, "HostileDemandPending", "HostileDemand"));
    }

    [Theory]
    [InlineData("TradeActive", "Trade")]
    [InlineData("ProposalPending", "Trade")]
    [InlineData("TradeActive", null)]
    [InlineData(null, "Trade")]
    public void A_trade_between_enemies_can_still_be_left(string? phase, string? proposal)
    {
        // Coop's barter Cancel sends Leave (PlayerPartyTradeContext.PublishLeave, v0.1.6), which Coop ends as TradeDeclined.
        Assert.False(PvpRule.BlocksLeave(true, true, "Leave", phase, proposal));
        Assert.False(PvpRule.StripsLeave(true, true, phase, proposal));
        Assert.True(PvpRule.StripsLeave(true, true, "WaitingForProposal", "None"));
    }

    [Theory]
    [InlineData("RefuseHostileDemand")]
    [InlineData("YieldHostileDemand")]
    [InlineData("DeclineProposal")]
    [InlineData("AcceptProposal")]
    [InlineData("LeaveClan")]
    public void Every_other_answer_still_goes_through(string option) =>
        Assert.False(PvpRule.BlocksLeave(true, true, option, "HostileDemandPending", "HostileDemand"));

    [Fact]
    public void Leave_is_taken_out_of_what_the_caught_player_sees_and_nothing_else()
    {
        Assert.Empty(PvpRule.WithoutLeave(new[] { Option.Leave }));
        Assert.Equal(new object[] { Option.AcceptProposal, Option.DeclineProposal },
            PvpRule.WithoutLeave(new[] { Option.AcceptProposal, Option.DeclineProposal, Option.Leave }));
        Assert.Equal(new object[] { Option.RefuseHostileDemand, Option.YieldHostileDemand },
            PvpRule.WithoutLeave(new[] { Option.RefuseHostileDemand, Option.YieldHostileDemand }));
        // The responder's clan options, which Coop adds on top, stay.
        Assert.Equal(new object[] { Option.LeaveClan }, PvpRule.WithoutLeave(new[] { Option.Leave, Option.LeaveClan }));
    }

    // ---- the response timer ---------------------------------------------------------------------------

    [Fact]
    public void A_session_expires_after_the_seconds_and_not_before()
    {
        var timers = new PvpTimers();
        timers.Started("s1", Now);
        Assert.Empty(timers.Expired(Now.AddSeconds(29), 30));
        Assert.Equal(["s1"], timers.Expired(Now.AddSeconds(30), 30).Select(e => e.SessionId));
    }

    [Fact]
    public void Every_state_change_starts_the_clock_again()
    {
        var timers = new PvpTimers();
        timers.Started("s1", Now);
        timers.StateChanged("s1", "HostileDemandPending", Now.AddSeconds(25));
        Assert.Empty(timers.Expired(Now.AddSeconds(50), 30));
        var expired = Assert.Single(timers.Expired(Now.AddSeconds(55), 30));
        Assert.Equal(("s1", "HostileDemandPending"), (expired.SessionId, expired.Phase));
    }

    [Fact]
    public void Turning_a_rule_on_starts_every_open_clock_again()
    {
        var timers = new PvpTimers();
        Assert.False(timers.RuleTurnedOn(false, false));
        timers.Started("s1", Now);
        timers.StateChanged("s1", "HostileDemandPending", Now);
        // 40 seconds later the host turns on a 30 second timer: the demand gets its full 30 seconds from now.
        Assert.True(timers.RuleTurnedOn(false, true));
        timers.Restamp(Now.AddSeconds(40));
        Assert.Empty(timers.Expired(Now.AddSeconds(69), 30));
        var expired = Assert.Single(timers.Expired(Now.AddSeconds(70), 30));
        Assert.Equal(("s1", "HostileDemandPending"), (expired.SessionId, expired.Phase));

        Assert.False(timers.RuleTurnedOn(false, true));
        Assert.True(timers.RuleTurnedOn(true, true));
        Assert.False(timers.RuleTurnedOn(false, true));
        Assert.False(timers.RuleTurnedOn(false, false));
        Assert.True(timers.RuleTurnedOn(true, false));
    }

    [Fact]
    public void A_state_for_a_session_the_module_never_saw_is_not_timed()
    {
        var timers = new PvpTimers();
        timers.StateChanged("other", "WaitingForProposal", Now);
        Assert.Equal(0, timers.Count);
        Assert.Empty(timers.Expired(Now.AddMinutes(10), 30));
    }

    [Fact]
    public void Ended_or_disconnected_sessions_are_dropped()
    {
        // Coop's EndSession runs for an answer, the attacker leaving and a disconnect alike; the module's postfix drops the session.
        var timers = new PvpTimers();
        timers.Started("answered", Now);
        timers.Started("disconnected", Now);
        timers.Started("waiting", Now);
        timers.Ended("answered");
        timers.Ended("disconnected");
        timers.Ended(null);
        timers.Ended("never started");
        Assert.Equal(["waiting"], timers.Expired(Now.AddSeconds(30), 30).Select(e => e.SessionId));
        Assert.Equal(1, timers.Count);
    }

    [Fact]
    public void The_seconds_are_kept_inside_the_limits()
    {
        var timers = new PvpTimers();
        timers.Started("s1", Now);
        // 1 second is read as the fewest, 10.
        Assert.Empty(timers.Expired(Now.AddSeconds(9), 1));
        Assert.Single(timers.Expired(Now.AddSeconds(10), 1));
        Assert.Empty(timers.Expired(Now.AddSeconds(299), 100000));
        Assert.Single(timers.Expired(Now.AddSeconds(300), 100000));
    }

    [Theory]
    [InlineData(true, "HostileDemandPending", "HostileDemand", PvpTimeout.Battle)]
    [InlineData(true, "ProposalPending", "Vassal", PvpTimeout.Battle)]
    [InlineData(true, "WaitingForResponse", "HostileDemand", PvpTimeout.Battle)]
    [InlineData(false, "HostileDemandPending", "HostileDemand", PvpTimeout.Left)]
    [InlineData(false, "ProposalPending", "Mercenary", PvpTimeout.Left)]
    public void On_the_caught_players_turn_war_starts_the_battle_and_peace_just_ends(bool hostileNow, string phase, string proposal, PvpTimeout outcome)
    {
        Assert.True(PvpRule.CaughtPlayersTurn(phase));
        Assert.Equal(outcome, PvpRule.OnTimeout(hostileNow, phase, proposal, timerOn: true, blockLeaveOn: false));
        Assert.Equal(outcome, PvpRule.OnTimeout(hostileNow, phase, proposal, timerOn: true, blockLeaveOn: true));
    }

    [Theory]
    [InlineData(null, "None")]
    [InlineData("InitialOptions", "None")]
    [InlineData("WaitingForProposal", "None")]
    [InlineData("HostileDemandConfirm", "HostileDemand")]
    public void On_the_attackers_turn_the_caught_player_goes_free_and_no_battle_starts(string? phase, string proposal)
    {
        Assert.False(PvpRule.CaughtPlayersTurn(phase));
        // At war or not: an attacker who does nothing never starts a battle by waiting.
        Assert.Equal(PvpTimeout.Left, PvpRule.OnTimeout(true, phase, proposal, timerOn: true, blockLeaveOn: false));
        Assert.Equal(PvpTimeout.Left, PvpRule.OnTimeout(false, phase, proposal, timerOn: true, blockLeaveOn: false));
    }

    [Fact]
    public void Block_leaving_always_releases_the_caught_player_from_an_idle_attacker_even_with_the_timer_off()
    {
        Assert.Equal(PvpTimeout.Left, PvpRule.OnTimeout(true, "WaitingForProposal", "None", timerOn: false, blockLeaveOn: true));
        // With the timer off the caught player's own turn is not timed: they can still answer as long as they like.
        Assert.Equal(PvpTimeout.None, PvpRule.OnTimeout(true, "HostileDemandPending", "HostileDemand", timerOn: false, blockLeaveOn: true));
        // Both off: Coop as it is.
        Assert.Equal(PvpTimeout.None, PvpRule.OnTimeout(true, "WaitingForProposal", "None", timerOn: false, blockLeaveOn: false));
    }

    [Theory]
    [InlineData("WaitingForProposal", "None")]
    [InlineData("OfferServices", "None")]
    [InlineData("MarriageOptions", "None")]
    public void With_only_block_leaving_a_friendly_meeting_stays_as_Coop_has_it(string phase, string proposal)
    {
        // Friendly talks keep their Leave, so an attacker browsing services or a marriage offer is never cut off.
        Assert.Equal(PvpTimeout.None, PvpRule.OnTimeout(false, phase, proposal, timerOn: false, blockLeaveOn: true));
        // With the response timer on, an idle attacker ends any meeting, friendly ones included.
        Assert.Equal(PvpTimeout.Left, PvpRule.OnTimeout(false, phase, proposal, timerOn: true, blockLeaveOn: true));
    }

    [Fact]
    public void A_pair_that_made_peace_during_the_meeting_is_not_put_back_at_war()
    {
        // The module passes hostileNow = IsHostile from the start AND Coop's AreHostile asked again at timeout.
        var atStart = true;
        var stillAtWar = false;
        Assert.Equal(PvpTimeout.Left, PvpRule.OnTimeout(atStart && stillAtWar, "HostileDemandPending", "HostileDemand", timerOn: true, blockLeaveOn: true));
        var pvp = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszServerTools", "src", "PvpRules.cs"));
        Assert.Contains("Hostile(session) && (!PvpRule.CaughtPlayersTurn(phase) || StillAtWar(session))", pvp);
        Assert.Contains("AccessTools.Method(type, \"AreHostile\")", pvp);
    }

    // ---- telling the caught player why ----------------------------------------------------------------

    [Fact]
    public void The_caught_player_is_told_why_and_when_they_go_free()
    {
        Assert.Equal("The host turned off leaving when you're caught by a player at war. If the other player does nothing for 45 seconds, you go free. If you don't answer their demand in time, the battle starts.",
            PvpRule.LeaveBlockedMessage(true, 45));
        Assert.Equal("The host turned off leaving when you're caught by a player at war. If the other player does nothing for 45 seconds, you go free.",
            PvpRule.LeaveBlockedMessage(false, 45));
        // Coop's chat line limit (ChatMessageLimits.MaxMessageLength, v0.1.6).
        Assert.True(PvpRule.LeaveBlockedMessage(true, 300).Length <= 256);
    }

    [Fact]
    public void The_note_goes_out_once_per_session_and_again_for_a_new_one()
    {
        var timers = new PvpTimers();
        Assert.True(timers.FirstNotice("s1"));
        Assert.False(timers.FirstNotice("s1"));
        Assert.True(timers.FirstNotice("s2"));
        Assert.False(timers.FirstNotice(null));
        timers.Ended("s1");
        Assert.True(timers.FirstNotice("s1"));
        // A send that failed hands the note back, so the next chance sends it.
        timers.ForgetNotice("s2");
        Assert.True(timers.FirstNotice("s2"));
    }

    [Fact]
    public void Turning_block_leaving_off_gives_Leave_back_to_a_caught_player_who_lost_it()
    {
        var timers = new PvpTimers();
        var call = new object?[] { "session", "HostileDemandPending", null, new[] { "Leave", "Refuse" }, null };
        timers.LastOptions("s1", call);
        timers.LastOptions("s2", call);
        // A later state that went out with Leave, and a session that ended, have nothing to give back.
        timers.LastOptions("s2", null);
        timers.LastOptions("s3", call);
        timers.Ended("s3");
        var taken = Assert.Single(timers.TakeStripped());
        Assert.Equal("s1", taken.SessionId);
        Assert.Same(call, taken.Call);
        Assert.Empty(timers.TakeStripped());

        timers.LastOptions("gone", call);
        timers.Prune(id => id != "gone");
        Assert.Empty(timers.TakeStripped());
    }

    [Fact]
    public void Peace_made_while_the_caught_player_waits_gives_Leave_back_at_once_with_block_leaving_on()
    {
        var timers = new PvpTimers();
        var call = new object?[] { "session", "HostileDemandPending", null, new[] { "Leave", "Refuse" }, null };
        // Block leaving on and both meetings at war: Leave is taken out and Coop's call is kept.
        Assert.True(PvpRule.StripsLeave(true, true, "HostileDemandPending", "HostileDemand"));
        timers.LastOptions("peace", call);
        timers.LastOptions("war", call);
        // The first pair makes peace while the caught player waits. Coop sends no new options, so the next tick
        // gives Leave back to that one only, and the pair still at war keeps it hidden.
        var atWarNow = new Dictionary<string, bool> { ["peace"] = false, ["war"] = true };
        var taken = Assert.Single(timers.TakeStripped(id => PvpRule.GivesLeaveBack(true, atWarNow[id])));
        Assert.Equal("peace", taken.SessionId);
        Assert.Same(call, taken.Call);
        Assert.Empty(timers.TakeStripped(id => PvpRule.GivesLeaveBack(true, atWarNow[id])));
        Assert.Equal(1, timers.StrippedCount);
        // Block leaving off gives it back to everyone.
        Assert.True(PvpRule.GivesLeaveBack(false, true));
        Assert.Equal("war", Assert.Single(timers.TakeStripped(id => PvpRule.GivesLeaveBack(false, atWarNow[id]))).SessionId);

        // The module runs Leave back with block leaving on too, and picks by the war state now.
        var pvp = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszServerTools", "src", "PvpRules.cs"));
        var leaveBack = pvp.IndexOf("private static void LeaveBack()", StringComparison.Ordinal);
        Assert.DoesNotContain("if (ServerRules.Pvp.BlockLeave) return;", pvp);
        Assert.True(pvp.IndexOf("Timers.TakeStripped(id => PvpRule.GivesLeaveBack(blockLeave, AtWarNow(sessions[id])))", leaveBack, StringComparison.Ordinal) > leaveBack);
    }

    [Fact]
    public void A_Leave_back_that_failed_is_tried_again_on_later_ticks_up_to_the_cap()
    {
        var timers = new PvpTimers();
        var call = new object?[] { "session", "HostileDemandPending", null, new[] { "Leave", "Refuse" }, null };
        timers.LastOptions("s1", call);
        var tries = 0;
        // Each tick takes the call and puts it back after a failure, until the tries are used up.
        while (true)
        {
            var taken = Assert.Single(timers.TakeStripped());
            tries++;
            if (!timers.RetryStripped(taken.SessionId, taken.Call)) break;
        }
        Assert.Equal(PvpTimers.LeaveBackTries, tries);
        Assert.Empty(timers.TakeStripped());

        // A newer call Coop made while one was out is kept, and starts the count again.
        timers.LastOptions("s2", call);
        timers.TakeStripped();
        Assert.True(timers.RetryStripped("s2", call));
        var newer = new object?[] { "session", "ProposalPending", null, new[] { "Leave" }, null };
        timers.TakeStripped();
        timers.LastOptions("s2", newer);
        Assert.True(timers.RetryStripped("s2", call));
        Assert.Same(newer, Assert.Single(timers.TakeStripped()).Call);
        for (var i = 1; i < PvpTimers.LeaveBackTries; i++)
        {
            Assert.True(timers.RetryStripped("s2", newer));
            timers.TakeStripped();
        }
        Assert.False(timers.RetryStripped("s2", newer));
        Assert.False(timers.RetryStripped(null, call));

        // sessionsById is checked before the saved calls are taken, so a Coop that renamed it keeps them.
        var pvp = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszServerTools", "src", "PvpRules.cs"));
        var leaveBack = pvp.IndexOf("private static void LeaveBack()", StringComparison.Ordinal);
        var check = pvp.IndexOf("CoopBridge.Field(_handler!, \"sessionsById\") is not IDictionary sessions", leaveBack, StringComparison.Ordinal);
        Assert.True(leaveBack > 0 && check > leaveBack && check < pvp.IndexOf("Timers.TakeStripped(", leaveBack, StringComparison.Ordinal));
        Assert.Contains("if (!done && !Timers.RetryStripped(id, call))", pvp);
    }

    [Fact]
    public void A_send_that_throws_is_tried_again_and_a_sent_note_is_not()
    {
        var timers = new PvpTimers();
        var sends = 0;
        // TellPeer throws when Coop's chat packet or SendImmediate does not resolve.
        Assert.Throws<MissingMethodException>(() => timers.TellOnce("s1", () => { sends++; throw new MissingMethodException("SendImmediate"); }));
        Assert.True(timers.TellOnce("s1", () => sends++));
        Assert.False(timers.TellOnce("s1", () => sends++));
        Assert.Equal(2, sends);
        Assert.False(timers.TellOnce(null, () => sends++));
        Assert.Equal(2, sends);
    }

    [Fact]
    public void The_tick_cleanup_drops_gone_sessions_from_the_clock_and_the_notes()
    {
        var timers = new PvpTimers();
        timers.Started("open", Now);
        timers.Started("gone", Now);
        timers.FirstNotice("told and gone");
        timers.FirstNotice("open");
        timers.Prune(id => id == "open");
        Assert.Equal(1, timers.Count);
        Assert.True(timers.FirstNotice("told and gone"));
        Assert.True(timers.FirstNotice("gone"));
        Assert.False(timers.FirstNotice("open"));
    }

    [Theory]
    [InlineData(true, "TradeActive", "Trade")]
    [InlineData(true, "ProposalPending", "Trade")]
    [InlineData(false, "TradeActive", "Trade")]
    [InlineData(true, "TradeActive", "None")]
    public void Trades_are_never_timed(bool hostile, string phase, string proposal)
    {
        Assert.False(PvpRule.Timed(phase, proposal));
        Assert.Equal(PvpTimeout.None, PvpRule.OnTimeout(hostile, phase, proposal, timerOn: true, blockLeaveOn: true));
    }

    // ---- the settings file ----------------------------------------------------------------------------

    [Theory]
    [InlineData(null, false, false, 30)]
    [InlineData("not json", false, false, 30)]
    [InlineData("{ \"maxClansPerKingdom\": 12, \"unstuck\": \"hostOnly\" }", false, false, 30)]
    [InlineData("{ \"pvpBlockLeave\": true }", true, false, 30)]
    [InlineData("{\"pvpBlockLeave\":false,\"pvpResponseTimer\":true,\"pvpResponseSeconds\":45}", false, true, 45)]
    [InlineData("{ // a host's note\n \"pvpBlockLeave\": true, \"pvpResponseSeconds\": \"45\", }", true, false, 30)]
    [InlineData("{ \"pvpBlockLeave\": \"true\", \"pvpResponseTimer\": TRUE }", false, false, 30)]
    [InlineData("{ \"pvpResponseTimer\": true, \"pvpResponseSeconds\": 3 }", false, true, 10)]
    [InlineData("{ \"pvpResponseTimer\": true, \"pvpResponseSeconds\": 99999 }", false, true, 300)]
    public void The_module_reads_the_rules_with_both_off_by_default(string? json, bool block, bool timer, int seconds)
    {
        var read = PvpRulesSettings.Parse(json);
        Assert.Equal(block, read.BlockLeave);
        Assert.Equal(timer, read.ResponseTimer);
        Assert.Equal(seconds, read.Seconds);
    }

    [Fact]
    public void The_host_and_the_module_agree_on_the_keys_and_limits()
    {
        Assert.Equal(PvpRulesFile.BlockLeaveKey, PvpRulesSettings.BlockLeaveKey);
        Assert.Equal(PvpRulesFile.TimerKey, PvpRulesSettings.TimerKey);
        Assert.Equal(PvpRulesFile.SecondsKey, PvpRulesSettings.SecondsKey);
        Assert.Equal(PvpRulesFile.DefaultSeconds, PvpRulesSettings.DefaultSeconds);
        Assert.Equal(PvpRulesFile.FewestSeconds, PvpRulesSettings.FewestSeconds);
        Assert.Equal(PvpRulesFile.MostSeconds, PvpRulesSettings.MostSeconds);
        Assert.Equal(new HostPvpRules(false, false, 30), HostPvpRules.Defaults);
    }

    [Fact]
    public void The_module_reads_what_the_host_wrote_and_the_other_rules_stay()
    {
        var path = ClanCapFile.PathIn(_dir);
        Assert.Equal(HostPvpRules.Defaults, PvpRulesFile.Read(path));

        ClanCapFile.Write(path, new ClanCap(12, true));
        ServerRulesFile.Write(path, new HostServerRules(UnstuckChoice.HostOnly, false, true));
        foreach (var rules in new[] { new HostPvpRules(true, false, 30), new HostPvpRules(false, true, 10), new HostPvpRules(true, true, 300) })
        {
            PvpRulesFile.Write(path, rules);
            Assert.Equal(rules, PvpRulesFile.Read(path));
            var module = PvpRulesSettings.Parse(File.ReadAllText(path));
            Assert.Equal(rules.BlockLeave, module.BlockLeave);
            Assert.Equal(rules.ResponseTimer, module.ResponseTimer);
            Assert.Equal(rules.Seconds, module.Seconds);
        }

        // The clan cap and the server rules, written by the same page into the same file, are still there.
        Assert.Equal(new ClanCap(12, true), ClanCapFile.Read(path));
        Assert.Equal(new HostServerRules(UnstuckChoice.HostOnly, false, true), ServerRulesFile.Read(path));
        // And saving those again keeps the PvP rules.
        ClanCapFile.Write(path, new ClanCap(3, false));
        ServerRulesFile.Write(path, HostServerRules.Defaults);
        Assert.Equal(new HostPvpRules(true, true, 300), PvpRulesFile.Read(path));
    }

    [Theory]
    [InlineData("", 30)]
    [InlineData("  ", 30)]
    [InlineData("10", 10)]
    [InlineData(" 45 ", 45)]
    [InlineData("300", 300)]
    public void The_seconds_box_reads_a_whole_number(string text, int seconds) =>
        Assert.Equal(seconds, PvpRulesFile.ParseBox(text));

    [Theory]
    [InlineData("9")]
    [InlineData("301")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("thirty")]
    public void A_bad_seconds_box_stops_the_save_with_a_reason(string text)
    {
        var error = Assert.Throws<InvalidOperationException>(() => PvpRulesFile.ParseBox(text));
        Assert.Equal(PvpRulesFile.BadSeconds, error.Message);
    }

    // ---- the module's wiring --------------------------------------------------------------------------

    // Coop v0.1.6, PlayerPartyInteractionHandler.cs: the methods the module patches, with their parameter counts
    // (TryStartSession :152, HandleResponderOption :702, SendInitiatorState :834, SendResponderState :869,
    // EndSession :910) and the fields and members it reads.
    [Fact]
    public void The_module_patches_the_v016_handler_by_name()
    {
        var src = Path.Combine(RepoRoot(), "compat", "CoodszServerTools", "src");
        var pvp = File.ReadAllText(Path.Combine(src, "PvpRules.cs"));
        Assert.Contains("\"GameInterface.Services.MapEvents.PlayerPartyInteractions.PlayerPartyInteractionHandler\"", pvp);
        foreach (var (method, parameters) in new[]
                 {
                     ("HandleResponderOption", 2), ("SendResponderState", 5), ("SendInitiatorState", 5),
                     ("TryStartSession", 4), ("EndSession", 3), ("Handle_NetworkPlayerPartyInteractionShown", 1),
                 })
            Assert.Contains("Find(\"" + method + "\", " + parameters + ")", pvp);
        foreach (var member in new[] { "\"sessionsById\"", "\"sessionsByPartyId\"", "\"AttackerId\"", "\"IsHostile\"", "\"SessionId\"", "\"Proposal\"", "\"ResponderPeer\"", "\"InitiatorPeer\"", "\"network\"" })
            Assert.Contains(member, pvp);
        // Coop's own outcomes: Refuse's path for enemies, Left for everyone else.
        Assert.Contains("\"HostileDemandAccepted\"", pvp);
        Assert.Contains("\"Left\"", pvp);
        Assert.Equal("Leave", PvpRule.LeaveOption);
        Assert.Equal("TradeActive", PvpRule.TradePhase);
        Assert.Equal("Trade", PvpRule.TradeProposal);

        var rules = File.ReadAllText(Path.Combine(src, "ServerRules.cs"));
        Assert.Contains("PvpRules.Patch(_harmony);", rules);
        Assert.Contains("PvpRules.Tick(dt);", rules);
        Assert.Contains("PvpRulesSettings.Parse(text)", rules);

        // The note to the caught player rides Coop's own System chat line (ServerChatHandler.SendSystemMessage, v0.1.6).
        Assert.Contains("CoopBridge.TellPeer(network, peer,", pvp);
        // A send that fails hands the note back (TellOnce), and TellPeer throws when Coop's types don't resolve.
        Assert.Contains("Timers.TellOnce(id, () => CoopBridge.TellPeer(", pvp);
        var bridge = File.ReadAllText(Path.Combine(src, "CoopBridge.cs"));
        Assert.Contains("\"GameInterface.Services.Chat.Messages.NetworkChatMessage\"", bridge);
        Assert.Contains("\"SendImmediate\"", bridge);
        Assert.Contains("throw new MissingMethodException(\"SendImmediate\")", bridge);
        Assert.Contains("throw new MissingMemberException(\"NetworkChatMessage or ChatChannel\")", bridge);

        // Turning block leaving off mid meeting gives Leave back: the tick runs LeaveBack on Coop's calls kept here.
        Assert.Contains("try { if (_blockActive) LeaveBack(); }", pvp);
        Assert.Contains("Timers.LastOptions(id,", pvp);
    }

    [Fact]
    public void The_Settings_page_has_the_PvP_card_and_saves_it()
    {
        var host = Path.Combine(RepoRoot(), "src", "CoodszHost");
        var xaml = File.ReadAllText(Path.Combine(host, "MainWindow.xaml"));
        foreach (var name in new[] { "PvpRulesGroup", "PvpBlockLeaveBox", "PvpTimerBox", "PvpSecondsBox" })
            Assert.Contains("x:Name=\"" + name + "\"", xaml);
        Assert.Contains("Content=\"Block leaving in player battles\"", xaml);
        Assert.Contains("Text=\"If the other player does nothing, the caught player goes free after the seconds below.\"", xaml);
        Assert.Contains("Content=\"Start the battle when the caught player doesn't answer\"", xaml);

        var settings = File.ReadAllText(Path.Combine(host, "MainWindow.Settings.cs"));
        Assert.Contains("var pvpRules = PvpRulesFromPage();", settings);
        Assert.Contains("SavePvpRules(pvpRules);", settings);
        Assert.Contains("LoadPvpRules();", settings);
        Assert.Contains("StartPvpRules();", File.ReadAllText(Path.Combine(host, "MainWindow.xaml.cs")));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
