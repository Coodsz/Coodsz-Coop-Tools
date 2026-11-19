// Coodsz Server Tools: the PvP host rules (block leaving and the response timer in player vs player
// encounters) without the game or Coop, so the launcher tests can run them (tests\BannerlordCoopLauncher.Tests links this file).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Coodsz.ServerRules;

/// <summary>
/// The PvP rules Coodsz Host writes to <c>coodsz-server-tools.json</c>, next to the clan cap and the
/// server rules: <c>{ "pvpBlockLeave": false, "pvpResponseTimer": false, "pvpResponseSeconds": 30 }</c>.
/// Both rules are off by default, so a missing key or file changes nothing in Coop.
/// </summary>
public sealed class PvpRulesSettings
{
    public const string BlockLeaveKey = "pvpBlockLeave";
    public const string TimerKey = "pvpResponseTimer";
    public const string SecondsKey = "pvpResponseSeconds";

    public const int DefaultSeconds = 30;
    public const int FewestSeconds = 10;
    public const int MostSeconds = 300;

    public static readonly PvpRulesSettings Off = new PvpRulesSettings(false, false, DefaultSeconds);

    public PvpRulesSettings(bool blockLeave, bool responseTimer, int seconds)
    {
        BlockLeave = blockLeave;
        ResponseTimer = responseTimer;
        Seconds = Clamp(seconds);
    }

    /// <summary>The caught player can't pick Leave in an encounter with an enemy player (factions at war).</summary>
    public bool BlockLeave { get; }

    /// <summary>An encounter nobody answers in <see cref="Seconds"/> ends: a battle when at war, otherwise both part ways.</summary>
    public bool ResponseTimer { get; }

    public int Seconds { get; }

    public static int Clamp(int seconds) => Math.Max(FewestSeconds, Math.Min(MostSeconds, seconds));

    private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Reads the keys with System.Text.Json, the same way Coodsz Host's PvpRulesFile does; anything missing or
    /// broken keeps its default (off, 30 seconds).
    /// </summary>
    public static PvpRulesSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Off;
        try
        {
            using var document = JsonDocument.Parse(json!, ReadOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Off;
            var seconds = root.TryGetProperty(SecondsKey, out var value) && value.ValueKind == JsonValueKind.Number &&
                          value.TryGetInt32(out var number) ? number : DefaultSeconds;
            return new PvpRulesSettings(Flag(root, BlockLeaveKey), Flag(root, TimerKey), seconds);
        }
        catch (JsonException)
        {
            return Off;
        }
    }

    private static bool Flag(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;

    public string Describe() =>
        "PvP block leaving " + (BlockLeave ? "on" : "off") + ", PvP response timer " +
        (ResponseTimer ? Seconds.ToString(CultureInfo.InvariantCulture) + " seconds" : "off");
}

/// <summary>How an encounter that nobody answered in time ends.</summary>
public enum PvpTimeout
{
    /// <summary>Not timed (a trade), or the timer is off.</summary>
    None,
    /// <summary>Coop's EndSession with HostileDemandAccepted, the same path as the caught player picking Refuse.</summary>
    Battle,
    /// <summary>Coop's EndSession with Left: both part ways and no war is declared.</summary>
    Left,
}

/// <summary>
/// The decisions, on Coop's own enum names (PlayerPartyInteractionOption, Phase and Proposal in
/// GameInterface.Services.MapEvents.PlayerPartyInteractions), read as text so a reordered enum can't break them.
/// </summary>
public static class PvpRule
{
    public const string LeaveOption = "Leave";
    public const string TradePhase = "TradeActive";
    public const string TradeProposal = "Trade";

    /// <summary>
    /// True when the caught player's options lose Leave: the rule is on, the two are at war and they are not
    /// trading. Coop's barter Cancel is the same Leave option (PlayerPartyTradeContext.PublishLeave, v0.1.6), and
    /// Coop ends it as TradeDeclined, so a trade can always be left (Coodsz's rule: trades and friendly talks can leave).
    /// </summary>
    public static bool StripsLeave(bool ruleOn, bool atWar, string? lastPhase, string? proposal) =>
        ruleOn && atWar && Timed(lastPhase, proposal);

    /// <summary>True when the caught player's Leave is ignored: StripsLeave and the option is Leave.</summary>
    public static bool BlocksLeave(bool ruleOn, bool atWar, string? option, string? lastPhase, string? proposal) =>
        StripsLeave(ruleOn, atWar, lastPhase, proposal) && string.Equals(option, LeaveOption, StringComparison.Ordinal);

    /// <summary>
    /// True when a caught player whose options went out without Leave gets it back now: the host turned block
    /// leaving off, or the two made peace during the meeting (Coop sends no new options for that).
    /// </summary>
    public static bool GivesLeaveBack(bool ruleOn, bool atWarNow) => !ruleOn || !atWarNow;

    /// <summary>The options without Leave, for what the caught player is shown.</summary>
    public static List<object> WithoutLeave(System.Collections.IEnumerable options) =>
        options.Cast<object>().Where(o => !string.Equals(o?.ToString(), LeaveOption, StringComparison.Ordinal)).ToList();

    /// <summary>Trades are never timed: the trade screen, or a trade offer waiting for an answer.</summary>
    public static bool Timed(string? lastPhase, string? proposal) =>
        !string.Equals(lastPhase, TradePhase, StringComparison.Ordinal) &&
        !string.Equals(proposal, TradeProposal, StringComparison.Ordinal);

    /// <summary>
    /// True when the caught player must answer: a confirmed hostile demand (HostileDemandPending), a proposal
    /// (ProposalPending), or the attacker waiting on them (WaitingForResponse). Any other phase is the attacker's turn.
    /// </summary>
    public static bool CaughtPlayersTurn(string? lastPhase) =>
        lastPhase is "HostileDemandPending" or "ProposalPending" or "WaitingForResponse";

    /// <summary>
    /// How a session with no change for the seconds ends. Trades never time out.
    /// The caught player's turn (response timer on): a battle when the two are at war now, otherwise both part ways.
    /// The attacker's turn: Left, so the caught player goes free. With the response timer on that is any meeting;
    /// with only block leaving on it is a meeting between players at war, where Leave is hidden and an idle attacker
    /// could otherwise hold the caught player with nothing to pick. Friendly talks stay as Coop has them. The timer
    /// never starts a war (Coodsz's call, 2026-10-28).
    /// </summary>
    public static PvpTimeout OnTimeout(bool hostileNow, string? lastPhase, string? proposal, bool timerOn, bool blockLeaveOn)
    {
        if (!Timed(lastPhase, proposal)) return PvpTimeout.None;
        if (CaughtPlayersTurn(lastPhase))
            return !timerOn ? PvpTimeout.None : hostileNow ? PvpTimeout.Battle : PvpTimeout.Left;
        return timerOn || (blockLeaveOn && hostileNow) ? PvpTimeout.Left : PvpTimeout.None;
    }

    /// <summary>
    /// The one System chat line the caught player gets when the rule hides or refuses Leave. English only:
    /// the server sends the text as it is and the player's game shows it unchanged.
    /// </summary>
    public static string LeaveBlockedMessage(bool timerOn, int seconds) =>
        "The host turned off leaving when you're caught by a player at war. If the other player does nothing for " +
        PvpRulesSettings.Clamp(seconds).ToString(CultureInfo.InvariantCulture) + " seconds, you go free." +
        (timerOn ? " If you don't answer their demand in time, the battle starts." : "");
}

/// <summary>
/// The module's own clock for Coop's encounter sessions, by session id. Coop's session has no time on
/// it, so the clock starts when the session starts and starts again at every state change. Safe across
/// threads: Coop starts sessions on its network thread and sends states on the game thread.
/// </summary>
public sealed class PvpTimers
{
    private const int MostTracked = 500;

    private readonly Dictionary<string, (DateTime Changed, string? Phase)> _sessions =
        new Dictionary<string, (DateTime Changed, string? Phase)>(StringComparer.Ordinal);
    private readonly object _gate = new object();

    public int Count
    {
        get { lock (_gate) return _sessions.Count; }
    }

    /// <summary>A new session (Coop's TryStartSession returned true).</summary>
    public void Started(string sessionId, DateTime now)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_gate)
        {
            // Sessions that ended without the module seeing it are dropped by the tick; this is a last guard.
            if (_sessions.Count >= MostTracked && !_sessions.ContainsKey(sessionId)) _sessions.Clear();
            _sessions[sessionId] = (now, null);
        }
    }

    /// <summary>A state Coop sent for a session the module knows: the clock starts again.</summary>
    public void StateChanged(string sessionId, string? phase, DateTime now)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_gate)
        {
            if (_sessions.ContainsKey(sessionId)) _sessions[sessionId] = (now, phase);
        }
    }

    /// <summary>The session ended (an answer, the attacker leaving, a disconnect or the timer).</summary>
    public void Ended(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_gate)
        {
            _sessions.Remove(sessionId!);
            _told.Remove(sessionId!);
            _stripped.Remove(sessionId!);
            _leaveBackTries.Remove(sessionId!);
        }
    }

    private readonly HashSet<string> _told = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, object?[]> _stripped = new Dictionary<string, object?[]>(StringComparer.Ordinal);

    /// <summary>
    /// The caught player's last options: the call Coop made (its arguments before Leave was taken out) when the
    /// rule took Leave out, or null when they went out as Coop made them.
    /// </summary>
    public void LastOptions(string? sessionId, object?[]? strippedCall)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_gate)
        {
            if (strippedCall is null)
            {
                _stripped.Remove(sessionId!);
                return;
            }
            if (_stripped.Count >= MostTracked && !_stripped.ContainsKey(sessionId!)) _stripped.Clear();
            _stripped[sessionId!] = strippedCall;
            _leaveBackTries.Remove(sessionId!);
        }
    }

    /// <summary>How many sessions' last options went out without Leave.</summary>
    public int StrippedCount
    {
        get { lock (_gate) return _stripped.Count; }
    }

    /// <summary>Every session whose last options went out without Leave, with Coop's call; the list is emptied.</summary>
    public List<(string SessionId, object?[] Call)> TakeStripped()
    {
        lock (_gate)
        {
            var taken = _stripped.Select(s => (s.Key, s.Value)).ToList();
            _stripped.Clear();
            return taken;
        }
    }

    /// <summary>
    /// The sessions whose last options went out without Leave and that <paramref name="which"/> picks, with Coop's
    /// call; only those are taken. The check runs outside the lock, since it asks Coop about the session.
    /// </summary>
    public List<(string SessionId, object?[] Call)> TakeStripped(Func<string, bool> which)
    {
        List<string> ids;
        lock (_gate) ids = _stripped.Keys.ToList();
        var picked = ids.Where(which).ToList();
        var taken = new List<(string SessionId, object?[] Call)>();
        lock (_gate)
            foreach (var id in picked)
                if (_stripped.TryGetValue(id, out var call))
                {
                    _stripped.Remove(id);
                    taken.Add((id, call));
                }
        return taken;
    }

    /// <summary>How many ticks a session that could not get Leave back is tried again.</summary>
    public const int LeaveBackTries = 5;

    private readonly Dictionary<string, int> _leaveBackTries = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Giving Leave back failed (neither the resend nor the end went through), so the call is kept for the next
    /// tick, up to LeaveBackTries times. False once the tries are used up: the caller says the player was left
    /// without Leave. A newer call Coop made in the meantime is kept as it is.
    /// </summary>
    public bool RetryStripped(string? sessionId, object?[] call)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        lock (_gate)
        {
            if (_stripped.ContainsKey(sessionId!)) return true;
            _leaveBackTries.TryGetValue(sessionId!, out var tries);
            if (tries + 1 >= LeaveBackTries)
            {
                _leaveBackTries.Remove(sessionId!);
                return false;
            }
            _leaveBackTries[sessionId!] = tries + 1;
            _stripped[sessionId!] = call;
            return true;
        }
    }

    /// <summary>The note did not go out, so the next chance sends it.</summary>
    public void ForgetNotice(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_gate) _told.Remove(sessionId!);
    }

    /// <summary>Drops every timed, told or stripped session that is no longer open in Coop (ended without the module seeing it).</summary>
    public void Prune(Func<string, bool> stillOpen)
    {
        List<string> ids;
        lock (_gate) ids = _sessions.Keys.Concat(_told).Concat(_stripped.Keys).Distinct().ToList();
        foreach (var id in ids)
            if (!stillOpen(id)) Ended(id);
    }

    /// <summary>True the first time for a session, so the caught player is told why Leave is gone once, not at every state.</summary>
    public bool FirstNotice(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        lock (_gate)
        {
            if (_told.Count >= MostTracked && !_told.Contains(sessionId!)) _told.Clear();
            return _told.Add(sessionId!);
        }
    }

    /// <summary>
    /// Sends the note once per session. A send that throws hands the note back (ForgetNotice) and rethrows,
    /// so the next strip or refusal tries again. False when the session was already told.
    /// </summary>
    public bool TellOnce(string? sessionId, Action send)
    {
        if (!FirstNotice(sessionId)) return false;
        try
        {
            send();
        }
        catch (Exception)
        {
            ForgetNotice(sessionId);
            throw;
        }
        return true;
    }

    /// <summary>
    /// Every open session's clock starts again from now, keeping its phase. For a rule the host just turned on:
    /// the clock ran while the rules were off, so a demand that already sat longer than the seconds would end at once.
    /// </summary>
    public void Restamp(DateTime now)
    {
        lock (_gate)
            foreach (var id in _sessions.Keys.ToList())
                _sessions[id] = (now, _sessions[id].Phase);
    }

    /// <summary>True when the block leaving or response timer rule went from off to on since the last call.</summary>
    public bool RuleTurnedOn(bool blockLeave, bool responseTimer)
    {
        lock (_gate)
        {
            var turnedOn = (blockLeave && !_lastBlockLeave) || (responseTimer && !_lastResponseTimer);
            _lastBlockLeave = blockLeave;
            _lastResponseTimer = responseTimer;
            return turnedOn;
        }
    }

    private bool _lastBlockLeave;
    private bool _lastResponseTimer;

    public string? PhaseOf(string sessionId)
    {
        lock (_gate) return _sessions.TryGetValue(sessionId, out var entry) ? entry.Phase : null;
    }

    /// <summary>The sessions with no state change for <paramref name="seconds"/> or more, with their last phase.</summary>
    public List<(string SessionId, string? Phase)> Expired(DateTime now, int seconds)
    {
        var limit = TimeSpan.FromSeconds(PvpRulesSettings.Clamp(seconds));
        lock (_gate)
            return _sessions.Where(s => now - s.Value.Changed >= limit).Select(s => (s.Key, s.Value.Phase)).ToList();
    }

    /// <summary>Every timed session with the time its clock started and its last phase, for the countdown.</summary>
    public List<(string SessionId, DateTime Changed, string? Phase)> Open()
    {
        lock (_gate) return _sessions.Select(s => (s.Key, s.Value.Changed, s.Value.Phase)).ToList();
    }
}

/// <summary>Who a countdown goes to. The numbers travel in NetworkPvpCountdown.Role, so they never change.</summary>
public enum PvpCountdownRole
{
    /// <summary>The caught player, who sees "Answer in {0} s".</summary>
    Caught = 1,
    /// <summary>The player who caught them, who sees "They have {0} s to answer".</summary>
    Attacker = 2,
}

/// <summary>
/// One open session as the countdown sees it: its clock start, Coop's last phase, the session's Proposal, and
/// PeaceMade when the meeting started between players at war who are no longer at war (Coop's IsHostile and AreHostile
/// asked again, the check LeaveBack uses).
/// </summary>
public readonly record struct PvpCountdownSession(string SessionId, DateTime Changed, string? Phase, string? Proposal, bool PeaceMade = false);

/// <summary>A countdown to send (SecondsLeft above 0, for the clock Changed and the host's Limit seconds) or to take down (Stop) for one player of one session.</summary>
public readonly record struct PvpCountdownStep(string SessionId, PvpCountdownRole Role, bool Stop, int SecondsLeft, object? Peer,
    DateTime Changed = default, int Limit = 0);

/// <summary>
/// Which countdowns the players with the Coodsz client add on should see, worked out on every PvP tick from the
/// response timer's own clocks. A countdown shows only while the response timer is on and it is the caught
/// player's turn in a timed meeting (the same turn whose timeout starts the battle or ends the meeting); both
/// players get it. A clock that starts again (a new state, or a rule turned on) or a new number of seconds sends
/// it again with the time left; a session that ended, a turn that passed, peace or a rule turned off sends a stop.
/// PeaceMade is asked again every tick, not kept: while the two stay at peace the meeting shows no countdown, also
/// after LeaveBack sends the caught player's options again (which starts the clock again). If they go back to war
/// during the meeting, the countdown starts again from the clock as it is then.
/// Nothing is ever sent while the response timer is off and no countdown is up.
/// </summary>
public sealed class PvpCountdowns
{
    private const int MostShown = 500;

    // What the player of each session and role was last sent: the clock and seconds it counts, and the peer for the stop.
    private readonly Dictionary<(string SessionId, PvpCountdownRole Role), (DateTime Changed, int Seconds, object? Peer)> _shown =
        new Dictionary<(string, PvpCountdownRole), (DateTime, int, object?)>();

    public int Shown => _shown.Count;

    /// <summary>True when this session's countdown should be up: the response timer is on and the caught player must answer.</summary>
    public static bool Shows(PvpRulesSettings settings, string? phase, string? proposal) =>
        settings.ResponseTimer && PvpRule.CaughtPlayersTurn(phase) && PvpRule.Timed(phase, proposal);

    /// <summary>Whole seconds left on a clock that started at <paramref name="changed"/>, rounded up; 0 when it ran out.</summary>
    public static int SecondsLeft(DateTime changed, DateTime now, int seconds)
    {
        var left = PvpRulesSettings.Clamp(seconds) - (now - changed).TotalSeconds;
        return left <= 0 ? 0 : (int)Math.Ceiling(left);
    }

    /// <summary>
    /// The steps for this tick. <paramref name="peer"/> finds a session's player for a role (null while Coop does not
    /// know it yet, so that one is tried again next tick). The caller sends each step and reports it with
    /// <see cref="Sent"/>; a stop is forgotten whether it went out or not.
    /// </summary>
    public List<PvpCountdownStep> Plan(IEnumerable<PvpCountdownSession> open, PvpRulesSettings settings, DateTime now,
        Func<string, PvpCountdownRole, object?> peer)
    {
        var steps = new List<PvpCountdownStep>();
        var wanted = new HashSet<(string, PvpCountdownRole)>();
        foreach (var session in open)
        {
            if (string.IsNullOrEmpty(session.SessionId) || session.PeaceMade || !Shows(settings, session.Phase, session.Proposal)) continue;
            var left = SecondsLeft(session.Changed, now, settings.Seconds);
            if (left <= 0) continue;
            foreach (var role in new[] { PvpCountdownRole.Caught, PvpCountdownRole.Attacker })
            {
                var key = (session.SessionId, role);
                wanted.Add(key);
                if (_shown.TryGetValue(key, out var shown) && shown.Changed == session.Changed && shown.Seconds == settings.Seconds) continue;
                var target = peer(session.SessionId, role);
                if (target != null) steps.Add(new PvpCountdownStep(session.SessionId, role, false, left, target, session.Changed, settings.Seconds));
            }
        }

        foreach (var pair in _shown.ToList())
            if (!wanted.Contains(pair.Key))
            {
                steps.Add(new PvpCountdownStep(pair.Key.SessionId, pair.Key.Role, true, 0, pair.Value.Peer));
                _shown.Remove(pair.Key);
            }
        return steps;
    }

    /// <summary>A countdown went out for the clock it counts; a failed send is not reported, so the next tick tries again.</summary>
    public void Sent(PvpCountdownStep step)
    {
        if (step.Stop) return;
        if (_shown.Count >= MostShown && !_shown.ContainsKey((step.SessionId, step.Role))) _shown.Clear();
        _shown[(step.SessionId, step.Role)] = (step.Changed, step.Limit, step.Peer);
    }
}
