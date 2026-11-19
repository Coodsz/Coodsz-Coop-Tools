// Coodsz Server Tools: the PvP host rules. Block leaving in player vs player encounters between enemies,
// and a response timer that ends an encounter nobody answers. The rules are in PvpRulesRules.cs; this
// file patches Coop's PlayerPartyInteractionHandler on the dedicated server.

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Coodsz.ServerRules;

namespace CoodszServerTools;

/// <summary>
/// The PvP rules on the Settings page of Coodsz Host, read from coodsz-server-tools.json (ServerRules.Pvp).
/// </summary>
/// <remarks>
/// <para>
/// Coop (v0.1.6) runs a player vs player encounter as a session on the server
/// (GameInterface.Services.MapEvents.PlayerPartyInteractions.PlayerPartyInteractionHandler). The caught
/// player is always offered Leave ("I must leave now."), even when the two are at war, and Leave ends the
/// session before any battle exists. Block leaving: a prefix on HandleResponderOption ignores the caught
/// player's Leave when the two are at war now (IsHostile and Coop's AreHostile asked again) and not trading (Coop's
/// barter Cancel is the same Leave), and a prefix on SendResponderState takes Leave out of what
/// that player is shown. A modded client can still send Leave, so the server check is the one that counts.
/// </para>
/// <para>
/// Response timer: postfixes on TryStartSession, SendInitiatorState and SendResponderState start the clock
/// and start it again at every state change; a postfix on EndSession drops the session (an answer, the
/// attacker leaving, a disconnect). The module's tick ends a session with no change for the host's seconds
/// through Coop's own EndSession, by whose turn it is. The caught player's turn (a demand or proposal to
/// answer, response timer on): HostileDemandAccepted when the two are still at war now (what Refuse does, so
/// Coop starts the battle), otherwise Left. The attacker's turn (response timer or block leaving on): Left, so
/// an idle attacker can't hold the caught player. The timer never starts a war, and trades are never timed.
/// </para>
/// <para>
/// Countdown: while the response timer is on and it is the caught player's turn, both players see the seconds
/// left at the bottom of the screen when their game has the Coodsz client add on (it asked in the settings sync
/// request). The tick works out what each player should see from the same clocks (PvpCountdowns) and sends a
/// start, which their game counts down by itself, or a stop on an answer, Leave, a timeout or any other end of the
/// meeting, peace made during a meeting at war, the turn passing to the attacker, or the host turning the timer off.
/// Players without the add on get nothing new; the timer runs the same for them.
/// </para>
/// <para>
/// The settings file is read live. Turning block leaving off mid meeting gives Leave back: the tick sends Coop's
/// own SendResponderState call again with Leave in it, or ends the meeting as Left when it can't. A pair that made
/// peace while the caught player waits gets Leave back the same way, with block leaving still on.
/// </para>
/// <para>
/// Every target is found by name. One that is missing or changed turns off only its rule, said once in
/// the log, and Coop runs as before.
/// </para>
/// </remarks>
internal static class PvpRules
{
    public const string HandlerType = "GameInterface.Services.MapEvents.PlayerPartyInteractions.PlayerPartyInteractionHandler";

    private static readonly PvpTimers Timers = new PvpTimers();
    // Set from Coop's network and game threads, read on the game thread's tick.
    private static volatile object? _handler;
    private static volatile MethodInfo? _endSession;
    private static volatile MethodInfo? _responderState;
    private static volatile MethodInfo? _areHostile;
    private static volatile bool _blockActive;
    private static volatile bool _timerActive;
    private static float _sinceCheck;
    private static bool _noSessionsLogged;

    /// <summary>From ServerRules.PatchCoop, once Coop's GameInterface is loaded.</summary>
    public static void Patch(Harmony harmony)
    {
        var type = AccessTools.TypeByName(HandlerType);
        if (type is null)
        {
            CoopBridge.Log("PvP rules: Coop's PlayerPartyInteractionHandler was not found, so the PvP rules are off.");
            return;
        }

        MethodInfo? Find(string name, int parameters)
        {
            var method = AccessTools.Method(type, name);
            return method != null && !method.IsStatic && method.GetParameters().Length == parameters ? method : null;
        }

        var respond = Find("HandleResponderOption", 2);
        var responderState = Find("SendResponderState", 5);
        var initiatorState = Find("SendInitiatorState", 5);
        var start = Find("TryStartSession", 4);
        var end = Find("EndSession", 3);
        var sessions = AccessTools.Field(type, "sessionsById");
        var byParty = AccessTools.Field(type, "sessionsByPartyId");
        // Coop's own war check (private static AreHostile(PartyBase, PartyBase), v0.1.6 :1233), asked again when Leave
        // is dropped and at timeout, so a pair that made peace during the meeting can leave and isn't put back at war.
        var areHostile = AccessTools.Method(type, "AreHostile");
        _areHostile = areHostile != null && areHostile.IsStatic && areHostile.GetParameters().Length == 2 && areHostile.ReturnType == typeof(bool) ? areHostile : null;
        if (_areHostile is null)
            CoopBridge.Log("PvP rules: AreHostile did not resolve, so Leave follows the war state from the meeting's start and a timed out answer ends the meeting instead of starting a battle.");

        if (respond != null && responderState != null)
        {
            harmony.Patch(respond, prefix: new HarmonyMethod(typeof(PvpRules), nameof(RespondPrefix)));
            harmony.Patch(responderState, prefix: new HarmonyMethod(typeof(PvpRules), nameof(ResponderStatePrefix)));
            // To give Leave back when the host turns the rule off mid meeting (LeaveBack).
            _responderState = responderState;
            _endSession = end;
            _blockActive = true;
            // The caught player's peer is only known once their client says the dialog is shown, so the
            // note goes out from there (or from the first strip or refusal after it).
            var shown = Find("Handle_NetworkPlayerPartyInteractionShown", 1);
            if (shown != null && sessions != null)
                harmony.Patch(shown, postfix: new HarmonyMethod(typeof(PvpRules), nameof(ShownPostfix)));
            else
                CoopBridge.Log("PvP rules: Handle_NetworkPlayerPartyInteractionShown did not resolve, so the caught player is told why only when Leave is refused.");
        }
        else
        {
            CoopBridge.Log("PvP rules: HandleResponderOption or SendResponderState did not resolve, so Block leaving in player battles is off.");
        }

        if (start != null && start.ReturnType == typeof(bool) && end != null && sessions != null && byParty != null &&
            responderState != null && initiatorState != null)
        {
            _endSession = end;
            harmony.Patch(start, postfix: new HarmonyMethod(typeof(PvpRules), nameof(StartPostfix)));
            harmony.Patch(responderState, postfix: new HarmonyMethod(typeof(PvpRules), nameof(StatePostfix)));
            harmony.Patch(initiatorState, postfix: new HarmonyMethod(typeof(PvpRules), nameof(StatePostfix)));
            harmony.Patch(end, postfix: new HarmonyMethod(typeof(PvpRules), nameof(EndPostfix)));
            _timerActive = true;
        }
        else
        {
            CoopBridge.Log("PvP rules: TryStartSession, EndSession, a state method, sessionsById or sessionsByPartyId did not resolve, so the response timer and the idle attacker release are off.");
        }

        if (_blockActive || _timerActive)
            CoopBridge.Log("PvP rules: the hooks resolved (" + (_blockActive ? "block leaving" : "") +
                           (_blockActive && _timerActive ? ", " : "") + (_timerActive ? "response timer" : "") + "). " + ServerRules.Pvp.Describe() + ".");
    }

    private static bool Hostile(object? session) => session != null && CoopBridge.Field(session, "IsHostile") is true;

    // At war now: Coop's IsHostile from the session's start, and Coop's AreHostile asked again when it resolved, so a
    // pair that made peace mid meeting gets Leave back. A check that fails counts as peace, so Leave stays.
    private static bool AtWarNow(object? session) => Hostile(session) && (_areHostile is null || StillAtWar(session!));

    // Coop's phase as the module last saw it (null without the timer hooks) and the session's Proposal, for the trade check.
    private static string? PhaseOf(object session) =>
        CoopBridge.Field(session, "SessionId") is string id ? Timers.PhaseOf(id) : null;

    // Coop: the caught player picked an option. Leave is ignored when the rule is on, the two are at war and they are
    // not trading (Coop's barter Cancel is the same Leave).
    private static bool RespondPrefix(object __instance, object[] __args)
    {
        try
        {
            _handler = __instance;
            var session = __args.Length > 0 ? __args[0] : null;
            var option = __args.Length > 1 ? __args[1]?.ToString() : null;
            if (session is null || !string.Equals(option, PvpRule.LeaveOption, StringComparison.Ordinal)) return true;
            var settings = ServerRules.Pvp;
            if (!PvpRule.BlocksLeave(settings.BlockLeave, settings.BlockLeave && AtWarNow(session), option, PhaseOf(session),
                    CoopBridge.Field(session, "Proposal")?.ToString())) return true;
            CoopBridge.Log("PvP rules: ignored Leave from " + CoopBridge.Field(session!, "ResponderName") + " in an encounter with an enemy player.");
            TellCaughtPlayer(__instance, session!);
            return false;
        }
        catch (Exception) { return true; }
    }

    // Coop: the caught player's options are about to go out. Leave is taken out for enemies, so the line doesn't show.
    // Harmony writes the changed __args back into the call. Coop's call is kept while the caught player's last
    // options have no Leave, so LeaveBack can send it again as Coop made it once the host turns the rule off.
    private static void ResponderStatePrefix(object __instance, object?[] __args)
    {
        try
        {
            _handler = __instance;
            var id = __args.Length > 0 && __args[0] != null ? CoopBridge.Field(__args[0]!, "SessionId") as string : null;
            // Coop's call: (session, phase, proposal, options, enabledOptions). A trade keeps its Leave (barter Cancel).
            if (!ServerRules.Pvp.BlockLeave || __args.Length < 5 ||
                !PvpRule.StripsLeave(true, AtWarNow(__args[0]), __args[1]?.ToString(), __args[2]?.ToString()))
            {
                Timers.LastOptions(id, null);
                return;
            }
            var call = (object?[])__args.Clone();
            var options = __args[3];
            var more = __args[4];
            __args[3] = Strip(__args[3]);
            __args[4] = Strip(__args[4]);
            Timers.LastOptions(id, ReferenceEquals(options, __args[3]) && ReferenceEquals(more, __args[4]) ? null : call);
            if (!ReferenceEquals(options, __args[3])) TellCaughtPlayer(__instance, __args[0]!);
        }
        catch (Exception) { }
    }

    // One System chat line to the caught player, once per session: why Leave is gone. Coop's own chat
    // packet (NetworkChatMessage on the System channel) to the session's ResponderPeer through the
    // handler's network, so players need nothing. English only.
    private static void TellCaughtPlayer(object handler, object session)
    {
        try
        {
            var network = CoopBridge.Field(handler, "network");
            var peer = CoopBridge.Field(session, "ResponderPeer");
            if (network is null || peer is null) return;
            var id = CoopBridge.Field(session, "SessionId") as string;
            var settings = ServerRules.Pvp;
            // Not sent (TellPeer throws): the note is handed back, so the next strip or refusal tries again.
            Timers.TellOnce(id, () => CoopBridge.TellPeer(network, peer, "", PvpRule.LeaveBlockedMessage(settings.ResponseTimer && _timerActive, settings.Seconds)));
        }
        catch (Exception e)
        {
            var inner = e is TargetInvocationException { InnerException: { } cause } ? cause : e;
            CoopBridge.Log("PvP rules: could not tell the caught player why: " + inner.GetType().Name);
        }
    }

    // Coop: a client said it shows the dialog, which is when the server learns the caught player's peer.
    private static void ShownPostfix(object __instance, object[] __args)
    {
        try
        {
            _handler = __instance;
            if (!ServerRules.Pvp.BlockLeave || __args.Length < 1 || __args[0] is null) return;
            var message = CoopBridge.Field(__args[0], "What");
            var id = message is null ? null : CoopBridge.Field(message, "SessionId") as string;
            if (id is null || CoopBridge.Field(__instance, "sessionsById") is not IDictionary sessions) return;
            var session = sessions[id];
            if (session != null && PvpRule.StripsLeave(true, AtWarNow(session), PhaseOf(session), CoopBridge.Field(session, "Proposal")?.ToString()))
                TellCaughtPlayer(__instance, session);
        }
        catch (Exception) { }
    }

    // A typed copy of Coop's option array without Leave (the element type is Coop's enum).
    private static object? Strip(object? options)
    {
        if (options is not Array array) return options;
        var element = array.GetType().GetElementType()!;
        var kept = PvpRule.WithoutLeave(array);
        if (kept.Count == array.Length) return options;
        var copy = Array.CreateInstance(element, kept.Count);
        for (var i = 0; i < kept.Count; i++) copy.SetValue(kept[i], i);
        return copy;
    }

    // Coop started a session: its id is in sessionsByPartyId under the attacker's party id.
    private static void StartPostfix(object __instance, bool __result, object[] __args)
    {
        try
        {
            _handler = __instance;
            if (!__result || __args.Length < 2 || __args[1] is null) return;
            var attacker = CoopBridge.Field(__args[1], "AttackerId") as string;
            if (attacker != null && CoopBridge.Field(__instance, "sessionsByPartyId") is IDictionary byParty && byParty[attacker] is string id)
                Timers.Started(id, DateTime.UtcNow);
        }
        catch (Exception) { }
    }

    // Coop sent a state to one side: the clock starts again from now.
    private static void StatePostfix(object __instance, object[] __args)
    {
        try
        {
            _handler = __instance;
            if (__args.Length < 2 || __args[0] is null) return;
            if (CoopBridge.Field(__args[0], "SessionId") is string id)
                Timers.StateChanged(id, __args[1]?.ToString(), DateTime.UtcNow);
        }
        catch (Exception) { }
    }

    private static void EndPostfix(object[] __args)
    {
        try
        {
            if (__args.Length > 0 && __args[0] != null) Timers.Ended(CoopBridge.Field(__args[0], "SessionId") as string);
        }
        catch (Exception) { }
    }

    // Coop's own AreHostile on the two parties as they are now. Anything that can't be checked counts as peace,
    // so a timeout never starts a war.
    private static bool StillAtWar(object session)
    {
        try
        {
            var handler = _handler;
            var check = _areHostile;
            if (handler is null || check is null) return false;
            var initiator = CoopBridge.CoopObject<TaleWorlds.CampaignSystem.Party.PartyBase>(handler, CoopBridge.Field(session, "InitiatorPartyId") as string ?? "");
            var responder = CoopBridge.CoopObject<TaleWorlds.CampaignSystem.Party.PartyBase>(handler, CoopBridge.Field(session, "ResponderPartyId") as string ?? "");
            return initiator != null && responder != null && check.Invoke(null, new object[] { initiator, responder }) is true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>From the module's application tick (the game thread): ends sessions nobody answered in time.</summary>
    public static void Tick(float dt)
    {
        _sinceCheck += dt;
        if (_sinceCheck < 1f) return;
        _sinceCheck = 0f;
        // The clocks run while the rules are off, so a rule the host just turned on gives every open meeting its
        // full seconds from now.
        var settings = ServerRules.Pvp;
        if (Timers.RuleTurnedOn(settings.BlockLeave, settings.ResponseTimer)) Timers.Restamp(DateTime.UtcNow);
        if (_handler is null) return;
        // A failure here must not stop the other server rules, which share the tick.
        try { if (_blockActive) LeaveBack(); }
        catch (Exception e) { CoopBridge.Log("PvP rules: giving Leave back failed: " + e.GetType().Name + ": " + e.Message); }
        if (!_timerActive || _endSession is null) return;
        try { EndExpired(); }
        catch (Exception e) { CoopBridge.Log("PvP rules: the response timer check failed: " + e.GetType().Name + ": " + e.Message); }
        // After the timeouts, so a meeting that just ended gets its stop in the same tick.
        try { SendCountdowns(); }
        catch (Exception e) { CoopBridge.Log("PvP rules: the countdown failed: " + e.GetType().Name + ": " + e.Message); }
    }

    // ---- the countdown players with the Coodsz client add on see -------------------------------------------------

    private static readonly PvpCountdowns Countdowns = new PvpCountdowns();
    private static Func<object?, string, int, int, bool>? _countdown;
    private static Func<object?, string, bool>? _countdownStop;
    private static bool _countdownMissing;
    private static int _countdownErrors;

    /// <summary>
    /// The settings sync's PvpCountdownServer (CoodszSettingsSync.Coop.dll), which knows the players whose add on
    /// asked for the countdown. False until the sync loaded, and for good when it has no countdown.
    /// </summary>
    private static bool BindCountdown()
    {
        if (_countdown != null && _countdownStop != null) return true;
        if (_countdownMissing) return false;
        var sync = ToolsSubModule.SettingsSync;
        if (sync is null || !sync.Running) return false;
        const string type = "CoodszSettingsSync.PvpCountdownServer";
        try
        {
            _countdown = sync.Entry(type, "Countdown")?.CreateDelegate(typeof(Func<object?, string, int, int, bool>)) as Func<object?, string, int, int, bool>;
            _countdownStop = sync.Entry(type, "Stop")?.CreateDelegate(typeof(Func<object?, string, bool>)) as Func<object?, string, bool>;
        }
        catch (Exception) { _countdown = null; }
        if (_countdown != null && _countdownStop != null) return true;
        _countdownMissing = true;
        CoopBridge.Log("PvP rules: the settings sync has no PvP countdown, so players see no countdown; the response timer still runs.");
        return false;
    }

    // One session for the countdown. The war check (AtWarNow, the one LeaveBack uses, so Coop's AreHostile on the game
    // thread) runs only for a meeting that would count down, so a pair that made peace gets its stop at once.
    private static PvpCountdownSession Counted(object? session, string id, DateTime changed, string? phase, PvpRulesSettings settings)
    {
        var proposal = session is null ? null : CoopBridge.Field(session, "Proposal")?.ToString();
        var peaceMade = session != null && PvpCountdowns.Shows(settings, phase, proposal) && Hostile(session) && !AtWarNow(session);
        return new PvpCountdownSession(id, changed, phase, proposal, peaceMade);
    }

    // Starts and stops the countdown by what the response timer's clocks say now (PvpCountdowns.Plan). Only players
    // whose add on asked get anything (PvpCountdownServer checks), and nothing goes out while the timer is off.
    private static void SendCountdowns()
    {
        var settings = ServerRules.Pvp;
        if (!settings.ResponseTimer && Countdowns.Shown == 0) return;
        if (!BindCountdown() || CoopBridge.Field(_handler!, "sessionsById") is not IDictionary sessions) return;

        var open = Timers.Open().Select(s => Counted(sessions[s.SessionId], s.SessionId, s.Changed, s.Phase, settings)).ToList();
        object? Peer(string id, PvpCountdownRole role) =>
            sessions[id] is { } session ? CoopBridge.Field(session, role == PvpCountdownRole.Caught ? "ResponderPeer" : "InitiatorPeer") : null;

        foreach (var step in Countdowns.Plan(open, settings, DateTime.UtcNow, Peer))
        {
            try
            {
                if (step.Stop) _countdownStop!(step.Peer, step.SessionId);
                else if (_countdown!(step.Peer, step.SessionId, step.SecondsLeft, (int)step.Role)) Countdowns.Sent(step);
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException { InnerException: { } cause } ? cause : e;
                if (++_countdownErrors <= 5)
                    CoopBridge.Log("PvP rules: sending a countdown failed: " + inner.GetType().Name + ": " + inner.Message +
                                   (_countdownErrors == 5 ? ". Further errors are not logged." : ""));
            }
        }
    }

    // The host turned block leaving off (ServerRules reads the file live), or the two made peace during the meeting,
    // while caught players still have options without Leave. Coop sends no new options on peace, so Leave would stay
    // hidden until the next state or the timer. Coop's own call goes out again with Leave in it, or the session ends
    // as Left when it can't.
    // Each saved call is tried on its own, so one failure doesn't drop the rest, and one that fails is kept for the
    // next tick (PvpTimers.RetryStripped, a few tries) before the log says the player was left without Leave.
    private static void LeaveBack()
    {
        var blockLeave = ServerRules.Pvp.BlockLeave;
        if (blockLeave && Timers.StrippedCount == 0) return;
        // Checked before the saved calls are taken, so a Coop update that renamed sessionsById keeps them.
        if (CoopBridge.Field(_handler!, "sessionsById") is not IDictionary sessions)
        {
            if (!_noSessionsLogged)
                CoopBridge.Log("PvP rules: Coop's sessionsById did not resolve, so Leave can't be given back to caught players.");
            _noSessionsLogged = true;
            return;
        }
        // With block leaving on, only the pairs no longer at war (a gone session too, so it is dropped).
        foreach (var (id, call) in Timers.TakeStripped(id => PvpRule.GivesLeaveBack(blockLeave, AtWarNow(sessions[id]))))
        {
            bool done;
            try { done = GiveLeaveBack(sessions, id, call, blockLeave ? "the two made peace" : "block leaving is off"); }
            catch (Exception e)
            {
                CoopBridge.Log("PvP rules: giving Leave back failed for one encounter: " + e.GetType().Name + ": " + e.Message);
                done = false;
            }
            if (!done && !Timers.RetryStripped(id, call))
                CoopBridge.Log("PvP rules: gave up giving Leave back after " + PvpTimers.LeaveBackTries + " tries, so a caught player has no Leave until the meeting changes.");
        }
    }

    // True when the session is done with: Leave was sent again, the meeting ended, or Coop already closed it.
    private static bool GiveLeaveBack(IDictionary sessions, string id, object?[] call, string why)
    {
        var session = sessions[id];
        if (session is null) return true;
        if (_responderState is { } resend && call.Length == 5)
        {
            var sent = false;
            try
            {
                call[0] = session;
                // The resend goes through StatePostfix, so the idle clock starts again from now.
                resend.Invoke(_handler, call);
                sent = true;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException { InnerException: { } cause } ? cause : e;
                CoopBridge.Log("PvP rules: could not send the options with Leave again: " + inner.GetType().Name + ".");
            }

            if (sent)
            {
                CoopBridge.Log("PvP rules: " + why + ", so " + CoopBridge.Field(session, "ResponderName") + " has Leave again.");
                return true;
            }
        }

        if (_endSession is not { } end) return false;
        try
        {
            end.Invoke(_handler, new[] { session, Enum.Parse(end.GetParameters()[1].ParameterType, "Left"), null });
        }
        catch (Exception e)
        {
            var inner = e is TargetInvocationException { InnerException: { } cause } ? cause : e;
            CoopBridge.Log("PvP rules: could not end an encounter that had no Leave: " + inner.GetType().Name + ": " + inner.Message);
            return false;
        }

        // Only after a good end, so a failed one keeps the response timer as a backstop.
        Timers.Ended(id);
        CoopBridge.Log("PvP rules: " + why + ", so the encounter between " + CoopBridge.Field(session, "InitiatorName") +
                       " and " + CoopBridge.Field(session, "ResponderName") + " ends.");
        return true;
    }

    private static void EndExpired()
    {
        if (CoopBridge.Field(_handler!, "sessionsById") is not IDictionary sessions) return;
        // Sessions that ended without the module seeing it, timed, told or stripped.
        Timers.Prune(id => sessions.Contains(id));

        // Block leaving always runs the idle attacker release, even with the response timer off.
        var settings = ServerRules.Pvp;
        if (!settings.ResponseTimer && !settings.BlockLeave) return;
        foreach (var (id, phase) in Timers.Expired(DateTime.UtcNow, settings.Seconds))
        {
            var session = sessions[id];
            if (session is null) continue;

            var proposal = CoopBridge.Field(session, "Proposal")?.ToString();
            // The caught player's turn asks Coop again whether they are still at war. The attacker's turn uses the same
            // IsHostile that hid Leave, so a caught player who has no Leave is always released from an idle attacker.
            var hostileNow = Hostile(session) && (!PvpRule.CaughtPlayersTurn(phase) || StillAtWar(session));
            var outcome = PvpRule.OnTimeout(hostileNow, phase, proposal, settings.ResponseTimer, settings.BlockLeave);
            if (outcome == PvpTimeout.None) continue;

            Timers.Ended(id);
            try
            {
                var outcomeType = _endSession!.GetParameters()[1].ParameterType;
                var value = Enum.Parse(outcomeType, outcome == PvpTimeout.Battle ? "HostileDemandAccepted" : "Left");
                CoopBridge.Log("PvP rules: " + (PvpRule.CaughtPlayersTurn(phase) ? "the caught player" : "the attacker") +
                               " did nothing for " + settings.Seconds + " seconds between " +
                               CoopBridge.Field(session, "InitiatorName") + " and " + CoopBridge.Field(session, "ResponderName") +
                               (outcome == PvpTimeout.Battle ? ", so the battle starts." : ", so the encounter ends."));
                _endSession.Invoke(_handler, new[] { session, value, null });
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException { InnerException: { } cause } ? cause : e;
                CoopBridge.Log("PvP rules: could not end a timed out encounter: " + inner.GetType().Name + ": " + inner.Message);
            }
        }
    }
}
