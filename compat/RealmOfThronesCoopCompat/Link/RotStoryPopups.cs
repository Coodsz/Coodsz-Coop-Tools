#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RealmOfThronesCoopCompat
{
    // Realm of Thrones' story popups on players' PCs (plan item S41; the rules and the message
    // shapes are RotPopupRules). The owner's decision: the first player to answer decides, within
    // 60 seconds, and the world waits meanwhile.
    //
    // Server. RotStory hands each popup here before it answers it (Take). With the switch on, the
    // link in a session and at least one player who entered the campaign with the link: a popup
    // whose answer changes the world waits in a queue and is asked one at a time on rot.story "ask"
    // to every player; the first valid answer is applied through RotStory.ApplyChosen, the same
    // path the default answer takes, and every player gets rot.story "result". After 60 seconds,
    // when the last such player left, or at the session's end the default answer is applied as
    // before. A player who enters while a question is open gets it with the time left. An
    // informational popup is answered at once as before and sent as rot.story "notice". For a live
    // test, COODSZ_ROT_STORY_TEST starts one question event once after the first player entered
    // (RotPopupRules.TestPlan); unset, it does nothing and logs nothing.
    //
    // The world waits through Coop's own time control (GameInterface's ITimeControlInterface, not
    // obfuscated, resolved from Coop's container like its own services): an unpause rule of ours
    // (AddUnpausePolicy) says no while a question holds time, and a pause lease
    // (ServerAcquireAutomaticPause) pauses now and resumes the speed before it on release. This is
    // how Coop's own catch up pause holds the world (OverloadedPeerManager). It cannot stick: our
    // rule says no only while a question is open and never later than the window plus a grace by
    // its own clock (PauseHold), so it frees time even if this code never ran again; the question
    // ends on an answer, the timeout, no players, the session's end or an error, each releasing
    // the hold and the lease; a lease Coop holds back (another rule keeps time paused) is asked
    // again for a while and then left to Coop, whose next accepted time request clears every
    // lease; and Coop's rules and leases live in the session's own time control, gone with it.
    // Coop keeps rules by a weak reference, so ours is an instance method of an object this class
    // keeps alive. Without Coop's time control the question still runs, with time running, and
    // the log says so.
    //
    // Player's PC. A question or notice is shown with the game's own question window
    // (InformationManager.ShowInquiry), its texts read in this PC's language from Realm of Thrones'
    // text ids the server found, its own text as the fallback, the time left as the window's timer.
    // A click sends rot.story "answer" once; the result closes the window (only when it is ours)
    // and shows one short line. Everything runs on the game thread and nothing throws out.
    internal static class RotStoryPopups
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string TimeControlType = "GameInterface.Services.Heroes.Interaces.ITimeControlInterface";
        private const string LeaseType = "GameInterface.Services.Heroes.Interaces.IAutomaticPauseLease";
        private const string PlayerManagerType = "GameInterface.Services.Players.IPlayerManager";
        private const string QueryManagerType = "TaleWorlds.MountAndBlade.GauntletUI.GauntletQueryManager";

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly HashSet<string> Said = new HashSet<string>();

        // ---- server ------------------------------------------------------------------------------

        private static readonly LinkedPlayers Players = new LinkedPlayers();
        private static readonly Queue<object> Waiting = new Queue<object>();
        private static readonly Dictionary<Type, List<string>> Literals = new Dictionary<Type, List<string>>();
        private static readonly bool SwitchOn = RotPopupRules.On(Environment.GetEnvironmentVariable(RotPopupRules.Variable), out _);
        private static Question _open;
        private static int _seq;
        private static long _lastPrune = long.MinValue;
        private static int _ignored;
        // The live test hook (RotPopupRules.TestPlan): read once, armed at the first player who
        // entered, fired once per process. Null when the variable is not set.
        private static readonly (string Event, int DelaySeconds, string Line)? TestPlan =
            RotPopupRules.TestPlan(Environment.GetEnvironmentVariable(RotPopupRules.TestVariable), Environment.GetEnvironmentVariable(RotPopupRules.TestDelayVariable));
        private static bool _testArmed;
        private static long _testDue = long.MaxValue;

        private sealed class Question
        {
            public StoryQuestion State;
            public object Popup;
            public StoryAsk Ask;
        }

        // From RotStory.Drain on the server, for each popup top first. True: this takes the popup
        // and answers it later through RotStory.ApplyChosen. False: RotStory answers it now, as
        // before (a notice is also sent then). Never throws.
        public static bool Take(object popup)
        {
            var type = popup?.GetType();
            if (type == null)
                return false;
            try
            {
                var why = OffReason();
                if (why != null)
                {
                    SubModule.Log(RotPopupRules.DefaultLine(type.Name, why));
                    return false;
                }
                Prune(true);
                if (Players.Count == 0)
                {
                    SubModule.Log(RotPopupRules.DefaultLine(type.Name, "no player with the link is in the campaign"));
                    return false;
                }
                if (!RotPopupRules.IsQuestion(RotStoryRules.Row(type.FullName)))
                {
                    SendNotice(popup);
                    return false;
                }
                var taken = ReferenceEquals(_open?.Popup, popup) || Waiting.Any(p => ReferenceEquals(p, popup));
                switch (RotPopupRules.Admit(taken, Waiting.Count))
                {
                    case TakeVerdict.AlreadyTaken:
                        return true;
                    case TakeVerdict.Full:
                        SubModule.Log(RotPopupRules.DefaultLine(type.Name, RotPopupRules.MaxWaiting + " questions already wait"));
                        return false;
                }
                Waiting.Enqueue(popup);
                return true;
            }
            catch (Exception ex)
            {
                SubModule.Log(RotPopupRules.Loud + "event " + type.Name + " could not be asked (" + ex.GetType().Name + ": " + ex.Message + "), so it gets its default answer.");
                return false;
            }
        }

        // Null when the popup path is on.
        private static string OffReason()
        {
            if (!SwitchOn)
                return RotPopupRules.Variable + "=off";
            if (!RotLink.Started)
                return "the link is off (its line says why)";
            if (!RotLink.InSession || !Coop.OnServer)
                return "no Coop session";
            return null;
        }

        // From RotLink's tick on the server in a session, every frame.
        public static void ServerTick()
        {
            try
            {
                var now = Clock.ElapsedMilliseconds;
                Prune(false);
                // No campaign (a save loads or unloads): nothing is applied, the question waits for
                // it or for the session's end; our hold still lets time go at its limit.
                if (Campaign.Current == null)
                {
                    Pause.Tick(now);
                    return;
                }
                // Not open any more: answered while there was no campaign.
                if (_open != null && (!_open.State.Open || _open.State.Tick(now, Players.Count)))
                    Close(_open);
                while (_open == null && Waiting.Count > 0)
                    OpenNext(now);
                if (_open == null && now >= _testDue)
                {
                    _testDue = long.MaxValue;
                    SubModule.Log(RotStory.FireTestEvent(TestPlan.Value.Event));
                }
                Pause.Tick(now);
            }
            catch (Exception ex)
            {
                SayOnce(RotPopupRules.Loud + "the server tick failed: " + ex.GetType().Name + " (said once per session).");
                Pause.End("an error");
            }
        }

        // Coop's "entered the campaign" for one player's connection, on the server.
        public static void Entered(object peer)
        {
            try
            {
                if (peer == null || !RotLink.PeerHasLink(peer))
                    return;
                if (TestPlan != null && !_testArmed && Coop.OnServerProcess)
                {
                    _testArmed = true;
                    if (TestPlan.Value.Event != null)
                        _testDue = Clock.ElapsedMilliseconds + TestPlan.Value.DelaySeconds * 1000L;
                    SubModule.Log(TestPlan.Value.Line);
                }
                if (!Players.Add(peer) || _open == null)
                    return;
                var left = _open.State.SecondsLeft(Clock.ElapsedMilliseconds);
                _open.Ask.SecondsLeft = left;
                if (RotLink.ToPlayer(peer, RotPopupRules.Channel, RotPopupRules.AskKey, _open.State.Seq, RotPopupRules.EncodeAsk(_open.Ask), null, out _))
                    SubModule.Log(RotPopupRules.LateJoinerLine(_open.State.Seq, _open.State.EventType, left));
            }
            catch (Exception ex)
            {
                SubModule.Log(RotPopupRules.Loud + "a player who entered could not get the open question: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // A player's answer, on the server's game thread.
        public static void Answered(object peer, int seq, IReadOnlyList<string> rest)
        {
            try
            {
                var who = PlayerName(peer);
                var open = _open;
                if (!RotPopupRules.DecodeAnswer(rest, out var option))
                    option = -1;
                var verdict = open == null ? AnswerVerdict.OldSeq : open.State.Answer(seq, option, who, Clock.ElapsedMilliseconds);
                if (verdict != AnswerVerdict.Applied)
                {
                    // A flood of bad answers says a few lines, not thousands.
                    if (++_ignored <= 20)
                        SubModule.Log(RotPopupRules.IgnoredLine(seq, who ?? "a connection that is no player", verdict));
                    return;
                }
                // Without a campaign the server tick applies it once there is one.
                if (Campaign.Current != null)
                    Close(open);
            }
            catch (Exception ex)
            {
                SubModule.Log(RotPopupRules.Loud + "an answer could not be handled: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void OpenNext(long now)
        {
            var popup = Waiting.Dequeue();
            var type = popup.GetType();
            if (Players.Count == 0)
            {
                SubModule.Log(RotPopupRules.DefaultLine(type.Name, "no player with the link is in the campaign any more"));
                RotStory.ApplyChosen(popup, RotPopupRules.DefaultOption);
                return;
            }
            StoryAsk ask;
            Question question;
            string reason;
            try
            {
                ask = Read(popup, false);
                question = new Question { Popup = popup, Ask = ask, State = new StoryQuestion(++_seq, type.Name, ask.Options.Count, now) };
                ask.Seq = question.State.Seq;
                ask.SecondsLeft = RotPopupRules.WindowSeconds;
                reason = RotLink.ToAll(RotPopupRules.Channel, RotPopupRules.AskKey, ask.Seq, RotPopupRules.EncodeAsk(ask), null, out var why) ? null : why ?? "not sent";
            }
            catch (Exception ex)
            {
                // It left the queue already, so it is answered here or never.
                ask = null;
                question = null;
                reason = ex.GetType().Name + ": " + ex.Message;
            }
            if (reason != null)
            {
                SubModule.Log(RotPopupRules.DefaultLine(type.Name, "the question could not be sent (" + reason + ")"));
                RotStory.ApplyChosen(popup, RotPopupRules.DefaultOption);
                return;
            }
            _open = question;
            _ignored = 0;
            var paused = Pause.Begin(now);
            SubModule.Log(RotPopupRules.QuestionSentLine(ask.Seq, type.Name, ask.Options.Count, Players.Count, paused));
        }

        // Applies what the question ended with, lets time run again and tells every player.
        private static void Close(Question question)
        {
            if (_open == question)
                _open = null;
            var state = question.State;
            var option = state.Chosen < 0 ? RotPopupRules.DefaultOption : state.Chosen;
            var optionText = option < question.Ask.Options.Count ? question.Ask.Options[option].Text : "";
            try
            {
                RotStory.ApplyChosen(question.Popup, option);
            }
            finally
            {
                Pause.End("question " + state.Seq + " ended");
            }
            SubModule.Log(state.End == QuestionEnd.Answered
                ? RotPopupRules.AppliedLine(state.Seq, state.EventType, state.AnsweredBy, optionText, state.SecondsTaken)
                : RotPopupRules.EndedLine(state.Seq, state.EventType, state.End, optionText, state.SecondsTaken));
            if (state.End != QuestionEnd.SessionEnded)
                RotLink.ToAll(RotPopupRules.Channel, RotPopupRules.ResultKey, state.Seq, RotPopupRules.EncodeResult(state.End, option, optionText, state.AnsweredBy), null, out _);
        }

        private static void SendNotice(object popup)
        {
            var ask = Read(popup, true);
            ask.Seq = ++_seq;
            if (RotLink.ToAll(RotPopupRules.Channel, RotPopupRules.NoticeKey, ask.Seq, RotPopupRules.EncodeAsk(ask), null, out var reason))
                SubModule.Log(RotPopupRules.NoticeSentLine(ask.Seq, popup.GetType().Name, Players.Count));
            else
                SubModule.Log(RotPopupRules.Log + "notice for event " + popup.GetType().Name + " not sent: " + reason + ".");
        }

        // The popup's texts and buttons as the server reads them, each with the text id Realm of
        // Thrones built it from when one reads the same here.
        private static StoryAsk Read(object popup, bool notice)
        {
            var type = popup.GetType();
            var ask = new StoryAsk { Notice = notice, EventType = type.Name };
            ask.Title = Text(popup, "Title");
            ask.Upper = Text(popup, "UpperText");
            ask.Lower = Text(popup, "LowerText");
            if (notice)
                return ask;
            ask.Options.Add(Text(popup, "DoneButtonText"));
            if (Property(popup, "CancelButtonEnabled") is bool cancel && cancel)
                ask.Options.Add(Text(popup, "CancelButtonText"));
            return ask;
        }

        private static StoryText Text(object popup, string property)
        {
            var text = Property(popup, property) as string ?? "";
            return new StoryText(RotPopupRules.IdFor(text, LiteralsOf(popup.GetType()), literal => new TextObject(literal).ToString()), text);
        }

        private static object Property(object popup, string name)
        {
            try { return popup.GetType().GetProperty(name, AnyInstance)?.GetValue(popup, null); }
            catch { return null; }
        }

        // Every "{=id}text" string literal in the event's own methods, read once per type.
        private static List<string> LiteralsOf(Type type)
        {
            if (Literals.TryGetValue(type, out var found))
                return found;
            found = new List<string>();
            try
            {
                foreach (var method in type.GetMethods(AnyInstance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => m.GetMethodBody() != null))
                {
                    foreach (var instruction in PatchProcessor.GetOriginalInstructions(method))
                    {
                        if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string literal && literal.StartsWith("{=", StringComparison.Ordinal))
                            found.Add(literal);
                    }
                }
            }
            catch (Exception ex)
            {
                SayOnce(RotPopupRules.Log + "text ids of " + type.Name + " could not be read (" + ex.GetType().Name + "), so players read its texts in the server's language.");
            }
            Literals[type] = found;
            return found;
        }

        // Drops connections that are gone, once a second (always when asked to).
        private static void Prune(bool now)
        {
            var at = Clock.ElapsedMilliseconds;
            if (!now && _lastPrune != long.MinValue && at - _lastPrune < RotPopupRules.PruneEveryMs)
                return;
            _lastPrune = at;
            Players.Prune(Connected);
        }

        // LiteNetLib's NetPeer.ConnectionState has the Connected flag (4) while the player is there.
        private static bool Connected(object peer)
        {
            var state = peer.GetType().GetProperty("ConnectionState")?.GetValue(peer, null);
            if (state == null)
            {
                SayOnce(RotPopupRules.Loud + "a player's connection state cannot be read, so a player who leaves still counts until the question times out (said once per session).");
                return true;
            }
            return (Convert.ToInt32(state) & 4) != 0;
        }

        // The player's hero name through Coop's player registry (IPlayerManager.TryGetPlayer for a
        // NetPeer, then Player.HeroId); "a player" when the registry cannot be read; null when it
        // says this connection is no player. Never an address.
        private static string PlayerName(object peer)
        {
            try
            {
                var manager = RotLink.Resolve(PlayerManagerType);
                var tryGet = manager == null ? null : Coop.FindType(PlayerManagerType)?.GetMethods()
                    .FirstOrDefault(m => m.Name == "TryGetPlayer" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.FullName == "LiteNetLib.NetPeer");
                if (tryGet == null || peer == null)
                    return "a player";
                var args = new[] { peer, null };
                if (!(bool)tryGet.Invoke(manager, args) || args[1] == null)
                    return null;
                var heroId = args[1].GetType().GetField("HeroId")?.GetValue(args[1]) as string;
                var name = heroId == null ? null : Hero.Find(heroId)?.Name?.ToString();
                return string.IsNullOrWhiteSpace(name) ? "a player" : RotPopupRules.Clip(name, RotPopupRules.MaxNameChars);
            }
            catch
            {
                return "a player";
            }
        }

        // ---- session end -------------------------------------------------------------------------

        // Server: an open question and the queue get the default answer while a campaign is there
        // (the world may be going away), time is let go. PC: our window closes.
        public static void SessionEnded()
        {
            try
            {
                var now = Clock.ElapsedMilliseconds;
                var open = _open;
                _open = null;
                // Still open it takes the default; answered without a campaign it takes that answer.
                if (open != null && Campaign.Current != null)
                {
                    open.State.SessionEnded(now);
                    Close(open);
                }
                while (Waiting.Count > 0)
                {
                    var popup = Waiting.Dequeue();
                    if (Campaign.Current != null)
                    {
                        SubModule.Log(RotPopupRules.DefaultLine(popup.GetType().Name, "the Coop session ended"));
                        RotStory.ApplyChosen(popup, RotPopupRules.DefaultOption);
                    }
                }
            }
            catch (Exception ex)
            {
                SubModule.Log(RotPopupRules.Loud + "the session end step failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Pause.SessionEnded();
                Players.Clear();
                Waiting.Clear();
                _ignored = 0;
                HideOurs();
                _pending.Clear();
                _shown = null;
                // The server's numbers start at 1 again in its next session.
                _closedSeq = 0;
                _stale.Clear();
                _notices.Clear();
                Said.Clear();
            }
        }

        private static void SayOnce(string line)
        {
            if (Said.Add(line))
                SubModule.Log(line);
        }

        // ---- the world waits ---------------------------------------------------------------------

        // Coop's unpause rule calls Allow on this object (kept alive here, since Coop holds rules
        // weakly); its declaring type and method name show in Coop's "limited to Pause by" line.
        private sealed class RotStoryPausePolicy
        {
            public readonly PauseHold Hold = new PauseHold();

            public bool Allow()
            {
                try { return Hold.Allows(Clock.ElapsedMilliseconds); }
                catch { return true; }
            }
        }

        private static class Pause
        {
            private static readonly RotStoryPausePolicy Policy = new RotStoryPausePolicy();
            private static readonly Func<bool> Rule = Policy.Allow;
            private static object _timeControl;
            private static object _lease;
            private static int _tries;
            private static long _nextTry;

            // True when Coop paused the world for the question.
            public static bool Begin(long now)
            {
                try
                {
                    var timeControl = RotLink.Resolve(TimeControlType);
                    var type = Coop.FindType(TimeControlType);
                    if (timeControl == null || type == null)
                    {
                        SubModule.Log(RotPopupRules.Log + "the world cannot wait: Coop's time control (" + TimeControlType + ") is not there, so time keeps running for the "
                            + RotPopupRules.WindowSeconds + " s.");
                        return false;
                    }
                    if (!ReferenceEquals(timeControl, _timeControl))
                    {
                        type.GetMethod("AddUnpausePolicy").Invoke(timeControl, new object[] { Rule });
                        _timeControl = timeControl;
                    }
                    // An older lease Coop still holds back is let go before our rule says no (it
                    // would refuse that release), or kept and used again rather than a second one.
                    var released = ReleaseLease();
                    _tries = 0;
                    Policy.Hold.Begin(now, RotPopupRules.WindowSeconds + RotPopupRules.HoldGraceSeconds);
                    if (released)
                        _lease = type.GetMethod("ServerAcquireAutomaticPause").Invoke(timeControl, null);
                    SubModule.Log(RotPopupRules.Log + "the world waits for the question (Coop's pause lease, and our unpause rule says no until it ends, at most "
                        + (RotPopupRules.WindowSeconds + RotPopupRules.HoldGraceSeconds) + " s).");
                    return true;
                }
                catch (Exception ex)
                {
                    Policy.Hold.End();
                    SubModule.Log(RotPopupRules.Loud + "the world cannot wait (" + (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message
                        + "), so time keeps running for the " + RotPopupRules.WindowSeconds + " s.");
                    return false;
                }
            }

            // Lets time run again: our rule says yes, then the lease asks Coop to resume.
            public static void End(string why)
            {
                if (!Policy.Hold.Holding && _lease == null)
                    return;
                Policy.Hold.End();
                if (_lease == null)
                    return;
                _tries = 0;
                if (ReleaseLease())
                    SubModule.Log(RotPopupRules.Log + "the world runs again (" + why + "; Coop resumes the speed before the pause).");
                else
                    SubModule.Log(RotPopupRules.Log + "our hold ended (" + why + "), but Coop keeps time paused for another reason; asking again once a second.");
            }

            public static void Tick(long now)
            {
                if (_lease == null || Policy.Hold.Holding || now < _nextTry)
                    return;
                _nextTry = now + 1000;
                if (ReleaseLease())
                {
                    SubModule.Log(RotPopupRules.Log + "the world runs again (Coop let the pause lease go after " + _tries + " more tries).");
                    return;
                }
                if (++_tries < RotPopupRules.ReleaseTries)
                    return;
                _lease = null;
                SubModule.Log(RotPopupRules.Log + "Coop still keeps time paused for its own reasons; the lease is left to Coop, whose next time request from a player clears it.");
            }

            public static void SessionEnded()
            {
                try
                {
                    Policy.Hold.End();
                    ReleaseLease();
                    if (_timeControl != null)
                        Coop.FindType(TimeControlType)?.GetMethod("RemoveUnpausePolicy")?.Invoke(_timeControl, new object[] { Rule });
                }
                catch { }
                _timeControl = null;
                _lease = null;
            }

            // True when there is no lease any more.
            private static bool ReleaseLease()
            {
                var lease = _lease;
                if (lease == null)
                    return true;
                try
                {
                    var released = (bool)(Coop.FindType(LeaseType)?.GetMethod("TryRelease")?.Invoke(lease, null) ?? true);
                    if (released)
                        _lease = null;
                    return released;
                }
                catch
                {
                    _lease = null;
                    return true;
                }
            }
        }

        // ---- player's PC -------------------------------------------------------------------------

        private sealed class Shown
        {
            public StoryAsk Ask;
            public InquiryData Data;
            public bool Answered;
            public bool ClosedByGame;
        }

        // What came before the shared world was live here, shown in order once it is.
        private static readonly List<(StoryAsk Ask, long At)> _pending = new List<(StoryAsk Ask, long At)>();
        private static Shown _shown;
        private static int _closedSeq;
        // Our windows the server closed while another window was in front; hidden when they come up.
        private static readonly List<InquiryData> _stale = new List<InquiryData>();
        private static readonly Queue<int> _notices = new Queue<int>();
        private static FieldInfo _activeQuery;
        private static bool _activeQueryLooked;

        // An ask, notice or result from the server, on the game thread.
        public static void Received(string key, int seq, IReadOnlyList<string> rest)
        {
            try
            {
                if (key == RotPopupRules.ResultKey)
                {
                    Result(seq, rest);
                    return;
                }
                var ask = RotPopupRules.DecodeAsk(seq, key == RotPopupRules.NoticeKey, rest);
                if (ask == null)
                {
                    SubModule.Log(RotPopupRules.Log + "a story " + key + " from the server could not be read; ignored.");
                    return;
                }
                if (ask.Notice)
                {
                    if (_notices.Contains(seq))
                        return;
                    _notices.Enqueue(seq);
                    while (_notices.Count > 32)
                        _notices.Dequeue();
                }
                else if (!RotPopupRules.IsNewQuestion(seq, ref _closedSeq) || _shown?.Ask.Seq == seq)
                {
                    // Already closed, or already on screen (Coop says "entered" again after a battle).
                    return;
                }
                if (!ask.Notice)
                {
                    // One question at a time: a newer one means the server closed the older.
                    if (_shown != null)
                        Close(_shown.Ask.Seq, null);
                    _pending.RemoveAll(item => !item.Ask.Notice);
                }
                _pending.Add((ask, Clock.ElapsedMilliseconds));
                ClientTick();
            }
            catch (Exception ex)
            {
                SubModule.Log(RotPopupRules.Loud + "a story message could not be shown: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // From RotLink's tick on a player's PC: shows what waits once the shared world is live,
        // and hides our closed windows that came to the front.
        public static void ClientTick()
        {
            try
            {
                HideStale();
                if (_pending.Count == 0 || Campaign.Current == null || !Coop.WorldLive)
                    return;
                var now = Clock.ElapsedMilliseconds;
                var items = _pending.ToList();
                _pending.Clear();
                foreach (var item in items)
                {
                    var left = item.Ask.Notice ? 0 : item.Ask.SecondsLeft - (int)((now - item.At) / 1000);
                    if (item.Ask.Notice || left > 0)
                        Show(item.Ask, left);
                }
            }
            catch (Exception ex)
            {
                SayOnce(RotPopupRules.Loud + "the story window step failed: " + ex.GetType().Name + " (said once per session).");
            }
        }

        private static void Show(StoryAsk ask, int left)
        {
            var title = Local(ask.Title);
            var body = Local(ask.Upper);
            var lower = Local(ask.Lower);
            if (lower.Length > 0)
                body = body.Length > 0 ? body + "\n\n" + lower : lower;
            var shown = new Shown { Ask = ask };
            InquiryData data;
            if (ask.Notice)
            {
                data = new InquiryData(title, body, true, false, GameTexts.FindText("str_ok").ToString(), "", () => shown.ClosedByGame = true, null);
            }
            else
            {
                var second = ask.Options.Count > 1;
                data = new InquiryData(title, body, true, second, Local(ask.Options[0]), second ? Local(ask.Options[1]) : "",
                    () => Choose(shown, 0), second ? () => Choose(shown, 1) : (Action)null, "", left, () => shown.ClosedByGame = true);
                _shown = shown;
            }
            shown.Data = data;
            InformationManager.ShowInquiry(data, false, true);
            SubModule.Log(RotPopupRules.ShownLine(ask, left));
        }

        // The player's click: the answer goes to the server once.
        private static void Choose(Shown shown, int option)
        {
            try
            {
                shown.ClosedByGame = true;
                if (shown.Answered)
                    return;
                shown.Answered = true;
                var sent = RotLink.ToServer(RotPopupRules.Channel, RotPopupRules.AnswerKey, shown.Ask.Seq, RotPopupRules.EncodeAnswer(option), out var reason);
                SubModule.Log(RotPopupRules.Log + "answer " + option + " for question " + shown.Ask.Seq + (sent ? " sent." : " not sent: " + reason + "."));
            }
            catch { }
        }

        private static void Result(int seq, IReadOnlyList<string> rest)
        {
            if (!RotPopupRules.DecodeResult(rest, out var end, out _, out var optionText, out var who))
            {
                SubModule.Log(RotPopupRules.Log + "a story result from the server could not be read; ignored.");
                return;
            }
            Close(seq, RotPopupRules.PlayerLine(end, optionText, who, _shown?.Ask.Seq == seq ? Local(_shown.Ask.Title) : null));
        }

        // Closes question seq here: the waiting one is dropped, ours on screen is hidden (or marked
        // to hide when it comes up), and line, if any, is shown once.
        private static void Close(int seq, string line)
        {
            _closedSeq = Math.Max(_closedSeq, seq);
            _pending.RemoveAll(item => !item.Ask.Notice && item.Ask.Seq <= seq);
            var shown = _shown;
            if (shown != null && shown.Ask.Seq == seq)
            {
                _shown = null;
                if (!shown.ClosedByGame)
                {
                    if (IsActive(shown.Data))
                        InformationManager.HideInquiry();
                    else
                        _stale.Add(shown.Data);
                }
            }
            if (line == null)
                return;
            InformationManager.DisplayMessage(new InformationMessage(line));
            SubModule.Log(RotPopupRules.Log + "question " + seq + " closed here: " + line);
        }

        private static void HideStale()
        {
            if (_stale.Count == 0)
                return;
            var active = ActiveQuery(out var readable);
            if (!readable)
            {
                _stale.Clear();
                return;
            }
            if (active is InquiryData data && _stale.Remove(data))
                InformationManager.HideInquiry();
        }

        private static void HideOurs()
        {
            try
            {
                if (_shown != null && !_shown.ClosedByGame && IsActive(_shown.Data))
                    InformationManager.HideInquiry();
            }
            catch { }
        }

        // Our window is the one on screen. The game's query manager keeps it in a private static
        // field; when that cannot be read nothing counts as ours, so another window (a quit
        // question, say) is never hidden by mistake and ours stays until the player closes it.
        private static bool IsActive(InquiryData data)
        {
            var active = ActiveQuery(out var readable);
            return readable && ReferenceEquals(active, data);
        }

        private static object ActiveQuery(out bool readable)
        {
            if (!_activeQueryLooked)
            {
                _activeQueryLooked = true;
                _activeQuery = Coop.FindType(QueryManagerType)?.GetField("_activeQueryData", BindingFlags.Static | BindingFlags.NonPublic);
                if (_activeQuery == null)
                    SubModule.Log(RotPopupRules.Log + "the game's query manager cannot be read, so a story window the server closed stays until the player closes it.");
            }
            readable = _activeQuery != null;
            try { return _activeQuery?.GetValue(null); }
            catch
            {
                readable = false;
                return null;
            }
        }

        // The text in this PC's language: Realm of Thrones' text id with the server's text as fallback.
        private static string Local(StoryText text)
        {
            var literal = RotPopupRules.ClientLiteral(text.Id, text.Text);
            if (literal == null)
                return text.Text;
            try { return new TextObject(literal).ToString(); }
            catch { return text.Text; }
        }
    }
}
