#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RealmOfThronesCoopCompat
{
    // The plain rules of Realm of Thrones' story popups on players' PCs (plan item S41, owner
    // decision H1.1): the COODSZ_ROT_POPUPS switch, one question's state, the messages on
    // rot.story with their size bounds, the text ids a PC reads in its own language, the players
    // who count, the pause hold and the log lines. No game or link types, so the launcher tests can
    // run them. The server half and the PC half are RotStoryPopups.
    //
    // A popup whose answer changes the world (StoryAnswer.Continue, or an event the story table
    // does not know) is a question: the first valid answer from any player with the link in the
    // campaign decides, within 60 seconds; without one, or when the last such player leaves, the
    // default answer (option 0, Continue) is taken as before. An informational popup is a notice:
    // the server answers it at once as before and players close it with OK.
    internal static class RotPopupRules
    {
        public const string Variable = "COODSZ_ROT_POPUPS";
        public const string Log = "Story popups: ";
        public const string Loud = "Story popups: !!! ";

        // Server to players: AskKey a question, NoticeKey a notice, ResultKey how a question ended.
        // Player to server: AnswerKey. Seq is the question's number on every one.
        public const string Channel = "rot.story";
        public const string AskKey = "ask";
        public const string NoticeKey = "notice";
        public const string AnswerKey = "answer";
        public const string ResultKey = "result";

        public const int WindowSeconds = 60;
        // The pause policy lets time run again this long after the window even if the server never
        // closed the question (its tick stopped), so it can never hold the world for good.
        public const int HoldGraceSeconds = 15;
        // A lease Coop does not let go at once (another rule holds time) is asked again once a
        // second this many times, then left to Coop, whose next accepted time request clears it.
        public const int ReleaseTries = 30;
        public const int PruneEveryMs = 1000;
        // The game's question window has two buttons; Realm of Thrones 8.1.8 uses only the first.
        public const int MaxOptions = 2;
        public const int DefaultOption = 0;
        // Questions waiting behind the open one; past this a popup gets its default answer at once.
        public const int MaxWaiting = 16;

        // Bounds of what a PC accepts, so a message cannot grow without limit.
        public const int MaxEventChars = 128;
        public const int MaxIdChars = 64;
        public const int MaxTitleChars = 200;
        public const int MaxTextChars = 4000;
        public const int MaxOptionChars = 120;
        public const int MaxNameChars = 100;

        // Only the exact value off turns it off, as COODSZ_ROT_SHIPS.
        public static bool On(string variable, out bool notUnderstood) => JoinRules.ProtectionOn(variable, out notUnderstood);

        public static string SwitchLine(string variable, bool storyOn)
        {
            if (!storyOn)
                return Log + "off: the story does not run on this server (COODSZ_ROT_STORY), so there is nothing to ask.";
            var on = On(variable, out var notUnderstood);
            if (notUnderstood)
                return JoinRules.NotUnderstoodLine(Variable, variable, "Story popups");
            return on
                ? Log + "on: a story question goes to every player with the link in the campaign, the first answer within " + WindowSeconds
                    + " s decides and the world waits; with no such player the server takes the default answer as before."
                : Log + "off (" + Variable + "=off): each story popup gets its default answer on the server as before, players see nothing.";
        }

        // A popup is a question when its answer changes the world, or when the story table does not
        // know its event (another Realm of Thrones version); else a notice.
        public static bool IsQuestion(StoryEventRow row) => row == null || row.Answer == StoryAnswer.Continue;

        // What the server does with a question popup: queue it; keep the one already waiting or
        // open when an event's tick opened the same popup again while the world waited (answering
        // it now as well would apply it twice); or answer it at once when the queue is full.
        public static TakeVerdict Admit(bool alreadyTaken, int waiting) =>
            alreadyTaken ? TakeVerdict.AlreadyTaken : waiting >= MaxWaiting ? TakeVerdict.Full : TakeVerdict.Queue;

        // Whether a PC shows question seq when the last question it closed was closedSeq, which it
        // updates. One at or below it was closed here already, except that one below it means the
        // server counts from 1 again (it restarted while this PC kept its session), so the count
        // starts over.
        public static bool IsNewQuestion(int seq, ref int closedSeq)
        {
            if (seq < closedSeq)
            {
                closedSeq = 0;
                return true;
            }
            return seq > closedSeq;
        }

        // The line for a popup the server answers itself, saying why it was not sent.
        public static string DefaultLine(string eventType, string why) =>
            Log + "event " + eventType + " gets its default answer on the server: " + why + ".";

        // ---- texts ---------------------------------------------------------------------------------

        // "{=id}text" as Realm of Thrones writes its texts. False for anything else, and for the
        // ids that mean "not translated" ("!" and "*").
        public static bool TryParseLiteral(string literal, out string id, out string text)
        {
            id = text = null;
            if (literal == null || !literal.StartsWith("{=", StringComparison.Ordinal))
                return false;
            var close = literal.IndexOf('}');
            if (close < 3)
                return false;
            id = literal.Substring(2, close - 2);
            text = literal.Substring(close + 1);
            return IsId(id);
        }

        // Letters, digits, '_', '-' and '.', at most MaxIdChars.
        public static bool IsId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= MaxIdChars && id.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.');

        // The id of the first literal that reads as text on the server (resolve is the server's own
        // TextObject), or "" when none does: a text built with variables then goes as the
        // server's text only.
        public static string IdFor(string text, IEnumerable<string> literals, Func<string, string> resolve)
        {
            if (string.IsNullOrEmpty(text) || literals == null || resolve == null)
                return "";
            foreach (var literal in literals)
            {
                if (!TryParseLiteral(literal, out var id, out _))
                    continue;
                string resolved;
                try { resolved = resolve(literal); }
                catch { continue; }
                if (resolved == text)
                    return id;
            }
            return "";
        }

        // What a PC hands its own TextObject: the id with the server's text as fallback, so the
        // player reads it in his language when the id is known there. Null means show the server's
        // text as it is (no id, or a text with braces the TextObject would read as variables).
        public static string ClientLiteral(string id, string serverText)
        {
            var text = serverText ?? "";
            if (!IsId(id) || text.IndexOf('{') >= 0 || text.IndexOf('}') >= 0)
                return null;
            return "{=" + id + "}" + text;
        }

        public static string Clip(string text, int max)
        {
            text = (text ?? "").Replace('\0', ' ');
            return text.Length <= max ? text : text.Substring(0, max);
        }

        // ---- messages ------------------------------------------------------------------------------

        // ask and notice: eventType, secondsLeft, title id and text, upper id and text, lower id and
        // text, the option count, then each option's id and text.
        public static List<string> EncodeAsk(StoryAsk ask)
        {
            var options = (ask.Options ?? new List<StoryText>()).Take(MaxOptions).ToList();
            var args = new List<string>
            {
                Clip(ask.EventType, MaxEventChars),
                Math.Max(0, Math.Min(WindowSeconds, ask.SecondsLeft)).ToString(CultureInfo.InvariantCulture),
            };
            AddText(args, ask.Title, MaxTitleChars);
            AddText(args, ask.Upper, MaxTextChars);
            AddText(args, ask.Lower, MaxTextChars);
            args.Add(options.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var option in options)
                AddText(args, option, MaxOptionChars);
            return args;
        }

        private static void AddText(List<string> args, StoryText text, int max)
        {
            args.Add(IsId(text.Id) ? text.Id : "");
            args.Add(Clip(text.Text, max));
        }

        // Null when the args are not one EncodeAsk wrote within the bounds. A question needs at
        // least one option; a notice has none (the PC shows its own OK).
        public static StoryAsk DecodeAsk(int seq, bool notice, IReadOnlyList<string> rest)
        {
            if (rest == null || rest.Count < 9 || seq <= 0)
                return null;
            if (!Number(rest[1], WindowSeconds, out var seconds) || !Number(rest[8], MaxOptions, out var count))
                return null;
            if (rest.Count != 9 + 2 * count || (notice ? count != 0 : count < 1) || rest[0].Length == 0 || rest[0].Length > MaxEventChars)
                return null;
            var ask = new StoryAsk { Seq = seq, Notice = notice, EventType = rest[0], SecondsLeft = seconds };
            if (!ReadText(rest, 2, MaxTitleChars, out ask.Title) || !ReadText(rest, 4, MaxTextChars, out ask.Upper) || !ReadText(rest, 6, MaxTextChars, out ask.Lower))
                return null;
            for (var i = 0; i < count; i++)
            {
                if (!ReadText(rest, 9 + 2 * i, MaxOptionChars, out var option))
                    return null;
                ask.Options.Add(option);
            }
            return ask;
        }

        private static bool ReadText(IReadOnlyList<string> rest, int at, int max, out StoryText text)
        {
            text = new StoryText(rest[at], rest[at + 1]);
            return (text.Id.Length == 0 || IsId(text.Id)) && text.Text.Length <= max;
        }

        private static bool Number(string value, int max, out int number) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number <= max;

        public static List<string> EncodeAnswer(int option) => new List<string> { option.ToString(CultureInfo.InvariantCulture) };

        public static bool DecodeAnswer(IReadOnlyList<string> rest, out int option)
        {
            option = -1;
            return rest != null && rest.Count == 1 && Number(rest[0], MaxOptions - 1, out option);
        }

        // result: how it ended, the option taken, its text on the server and who answered.
        public static List<string> EncodeResult(QuestionEnd end, int option, string optionText, string who) => new List<string>
        {
            EndName(end),
            option.ToString(CultureInfo.InvariantCulture),
            Clip(optionText, MaxOptionChars),
            Clip(who, MaxNameChars),
        };

        public static bool DecodeResult(IReadOnlyList<string> rest, out QuestionEnd end, out int option, out string optionText, out string who)
        {
            end = QuestionEnd.None;
            option = -1;
            optionText = who = null;
            if (rest == null || rest.Count != 4 || !Number(rest[1], MaxOptions - 1, out option) || rest[2].Length > MaxOptionChars || rest[3].Length > MaxNameChars)
                return false;
            end = EndOf(rest[0]);
            optionText = rest[2];
            who = rest[3];
            return end != QuestionEnd.None;
        }

        public static string EndName(QuestionEnd end)
        {
            switch (end)
            {
                case QuestionEnd.Answered: return "answered";
                case QuestionEnd.TimedOut: return "timeout";
                case QuestionEnd.NoPlayers: return "noplayers";
                case QuestionEnd.SessionEnded: return "ended";
                default: return "";
            }
        }

        public static QuestionEnd EndOf(string name)
        {
            switch (name)
            {
                case "answered": return QuestionEnd.Answered;
                case "timeout": return QuestionEnd.TimedOut;
                case "noplayers": return QuestionEnd.NoPlayers;
                case "ended": return QuestionEnd.SessionEnded;
                default: return QuestionEnd.None;
            }
        }

        // ---- log lines and the player's line -------------------------------------------------------

        public static string QuestionSentLine(int seq, string eventType, int options, int players, bool paused) =>
            Log + "question " + seq + " sent: event " + eventType + ", " + options + " option(s), to " + players + " player(s) with the link in the campaign; "
            + (paused ? "the world waits" : "time keeps running") + " for at most " + WindowSeconds + " s.";

        public static string NoticeSentLine(int seq, string eventType, int players) =>
            Log + "notice " + seq + " sent: event " + eventType + " to " + players + " player(s); the server answered it at once as before.";

        public static string AppliedLine(int seq, string eventType, string who, string option, double seconds) =>
            Log + "answer applied for question " + seq + " (event " + eventType + "): " + who + " chose \"" + option + "\" after " + Seconds(seconds) + " s.";

        public static string EndedLine(int seq, string eventType, QuestionEnd end, string option, double seconds)
        {
            var why = end == QuestionEnd.TimedOut ? "no answer in " + WindowSeconds + " s"
                : end == QuestionEnd.NoPlayers ? "the last player with the link left the campaign"
                : "the Coop session ended";
            return Log + "question " + seq + " (event " + eventType + ") timed out with the default: " + why + ", so \"" + option + "\" applied after " + Seconds(seconds) + " s.";
        }

        public static string LateJoinerLine(int seq, string eventType, int secondsLeft) =>
            Log + "late joiner got the open question " + seq + " (event " + eventType + ") with " + secondsLeft + " s left.";

        public static string IgnoredLine(int seq, string who, AnswerVerdict verdict) =>
            Log + "answer for question " + seq + " from " + who + " ignored: " + VerdictText(verdict) + ".";

        public static string VerdictText(AnswerVerdict verdict)
        {
            switch (verdict)
            {
                case AnswerVerdict.Duplicate: return "the question was already answered";
                case AnswerVerdict.Late: return "the question already ended";
                case AnswerVerdict.OldSeq: return "not the open question";
                case AnswerVerdict.Invalid: return "not one of its options";
                case AnswerVerdict.NotAPlayer: return "the sender is not a player";
                default: return "applied";
            }
        }

        public static string ShownLine(StoryAsk ask, int secondsLeft) =>
            Log + (ask.Notice ? "notice " + ask.Seq + " shown" : "question " + ask.Seq + " shown with " + secondsLeft + " s left") + " (event " + ask.EventType + ").";

        // The one short line a player sees when a question closed.
        public static string PlayerLine(QuestionEnd end, string option, string who, string title)
        {
            var about = string.IsNullOrWhiteSpace(title) ? "" : " (" + title.Trim() + ")";
            if (end == QuestionEnd.Answered)
                return (string.IsNullOrWhiteSpace(who) ? "A player" : who.Trim()) + " chose \"" + option + "\"" + about + ".";
            return "No answer in time, \"" + option + "\" was chosen" + about + ".";
        }

        private static string Seconds(double seconds) => Math.Max(0, seconds).ToString("0.0", CultureInfo.InvariantCulture);

        // ---- live test hook ------------------------------------------------------------------------

        // A live test of a story question without waiting for its day: on the server, with
        // COODSZ_ROT_STORY_TEST=<event> set, that event is started once, TestDelaySeconds after the
        // first player with the link entered, when no question is open. Only the events whose answer
        // changes the world (StoryAnswer.Continue) are allowed. Their real effects apply to the
        // world. Not set: nothing happens and nothing is logged.
        public const string TestVariable = "COODSZ_ROT_STORY_TEST";
        public const string TestDelayVariable = "COODSZ_ROT_STORY_TEST_DELAY";
        public const int TestDelaySeconds = 60;
        public const int TestDelayMin = 10;
        public const int TestDelayMax = 600;

        // Null when the variable is not set. Else the event to start (null when refused), the delay
        // and the line to log when the first player entered.
        public static (string Event, int DelaySeconds, string Line)? TestPlan(string variable, string delayVariable)
        {
            var name = variable?.Trim();
            if (string.IsNullOrEmpty(name))
                return null;
            var row = RotStoryRules.Events.FirstOrDefault(r => r.Type == name);
            if (row == null || row.Answer != StoryAnswer.Continue)
                return (null, 0, Loud + "test hook refused: " + TestVariable + "=" + CrashTraceRules.Shorten(name) + " is not a story event with a question ("
                    + string.Join(", ", RotStoryRules.Events.Where(r => r.Answer == StoryAnswer.Continue).Select(r => r.Type)) + "), so nothing fires.");
            var delay = TestDelaySeconds;
            var note = "";
            var delayText = delayVariable?.Trim();
            if (!string.IsNullOrEmpty(delayText))
            {
                if (int.TryParse(delayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= TestDelayMin && seconds <= TestDelayMax)
                    delay = seconds;
                else
                    note = " (" + TestDelayVariable + "=" + CrashTraceRules.Shorten(delayText) + " is not " + TestDelayMin + " to " + TestDelayMax + " seconds, so " + TestDelaySeconds + ")";
            }
            return (name, delay, Log + "test hook armed (" + TestVariable + "=" + name + "): the event starts once in " + delay + " s" + note
                + ", when no question is open. Its real effects apply to this world.");
        }

        public static string TestFiredLine(string eventType, bool started) =>
            Log + "test hook fired " + eventType + ": TriggerEvent returned " + (started ? "true (started)." : "false (it already ran or cannot start now).");

        public static string TestFailedLine(string eventType, string why) =>
            Loud + "test hook could not fire " + eventType + ": " + why + ".";
    }

    // A text a PC shows: Realm of Thrones' text id ("" when unknown) and the server's own text.
    internal struct StoryText
    {
        public StoryText(string id, string text)
        {
            Id = id ?? "";
            Text = text ?? "";
        }

        public string Id;
        public string Text;
    }

    // One ask or notice as read from its message.
    internal sealed class StoryAsk
    {
        public int Seq;
        public bool Notice;
        public string EventType;
        public int SecondsLeft;
        public StoryText Title;
        public StoryText Upper;
        public StoryText Lower;
        public List<StoryText> Options = new List<StoryText>();
    }

    internal enum QuestionEnd { None, Answered, TimedOut, NoPlayers, SessionEnded }

    internal enum AnswerVerdict { Applied, Duplicate, Late, OldSeq, Invalid, NotAPlayer }

    internal enum TakeVerdict { Queue, AlreadyTaken, Full }

    // One open question on the server. It ends once: by the first valid answer, at the deadline,
    // when no player with the link is left, or when the session ends; every way but the first takes
    // the default option. Times are milliseconds of one clock.
    internal sealed class StoryQuestion
    {
        public StoryQuestion(int seq, string eventType, int options, long askedAtMs, int windowSeconds = RotPopupRules.WindowSeconds)
        {
            Seq = seq;
            EventType = eventType ?? "";
            Options = Math.Max(1, Math.Min(RotPopupRules.MaxOptions, options));
            AskedAtMs = askedAtMs;
            DeadlineMs = askedAtMs + windowSeconds * 1000L;
        }

        public int Seq { get; }
        public string EventType { get; }
        public int Options { get; }
        public long AskedAtMs { get; }
        public long DeadlineMs { get; }
        public QuestionEnd End { get; private set; }
        public int Chosen { get; private set; } = -1;
        public string AnsweredBy { get; private set; }
        public long EndedAtMs { get; private set; }

        public bool Open => End == QuestionEnd.None;

        public double SecondsTaken => Math.Max(0, EndedAtMs - AskedAtMs) / 1000.0;

        public int SecondsLeft(long now) => Open ? (int)Math.Max(0, Math.Min(int.MaxValue, (DeadlineMs - now + 999) / 1000)) : 0;

        // who is null when the sender is not a player.
        public AnswerVerdict Answer(int seq, int option, string who, long now)
        {
            if (seq != Seq)
                return AnswerVerdict.OldSeq;
            if (End == QuestionEnd.Answered)
                return AnswerVerdict.Duplicate;
            if (!Open || now >= DeadlineMs)
                return AnswerVerdict.Late;
            if (who == null)
                return AnswerVerdict.NotAPlayer;
            if (option < 0 || option >= Options)
                return AnswerVerdict.Invalid;
            Finish(QuestionEnd.Answered, option, now);
            AnsweredBy = who;
            return AnswerVerdict.Applied;
        }

        // True when this tick ended the question with the default.
        public bool Tick(long now, int linkedPlayers)
        {
            if (!Open)
                return false;
            if (linkedPlayers <= 0)
                Finish(QuestionEnd.NoPlayers, RotPopupRules.DefaultOption, now);
            else if (now >= DeadlineMs)
                Finish(QuestionEnd.TimedOut, RotPopupRules.DefaultOption, now);
            return !Open;
        }

        // True when the question was still open.
        public bool SessionEnded(long now)
        {
            if (!Open)
                return false;
            Finish(QuestionEnd.SessionEnded, RotPopupRules.DefaultOption, now);
            return true;
        }

        private void Finish(QuestionEnd end, int option, long now)
        {
            End = end;
            Chosen = option;
            EndedAtMs = now;
        }
    }

    // The players' connections that count for a question: each entered the campaign with the link
    // on. A connection that dropped is pruned; a rejoin is a new connection.
    internal sealed class LinkedPlayers
    {
        private readonly List<object> _peers = new List<object>();

        public int Count => _peers.Count;

        // True when the connection is new here (a late joiner); Coop says "entered" again after
        // every battle.
        public bool Add(object peer)
        {
            if (peer == null || _peers.Any(p => ReferenceEquals(p, peer)))
                return false;
            _peers.Add(peer);
            return true;
        }

        public int Prune(Func<object, bool> stillConnected)
        {
            var before = _peers.Count;
            _peers.RemoveAll(peer =>
            {
                try { return !stillConnected(peer); }
                catch { return false; }
            });
            return before - _peers.Count;
        }

        public void Clear() => _peers.Clear();
    }

    // What the module's unpause rule answers Coop: time may run unless a question holds it, and
    // never past the window plus a grace, whatever else happens. Thread safe for one writer.
    internal sealed class PauseHold
    {
        private volatile bool _holding;
        private long _untilMs;

        public bool Holding => _holding;

        public void Begin(long now, int seconds)
        {
            System.Threading.Interlocked.Exchange(ref _untilMs, now + seconds * 1000L);
            _holding = true;
        }

        public void End() => _holding = false;

        public bool Allows(long now) => !_holding || now >= System.Threading.Interlocked.Read(ref _untilMs);
    }
}
