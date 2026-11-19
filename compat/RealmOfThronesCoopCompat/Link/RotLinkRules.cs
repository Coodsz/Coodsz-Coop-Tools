#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RealmOfThronesCoopCompat
{
    // The plain rules of our end of the shared Coop link (RotLink): the COODSZ_ROT_LINK switch, our
    // channels and keys, the protocol number every message starts with, the start up steps and the
    // log lines. No game or link types, so the launcher tests can run them.
    internal static class RotLinkRules
    {
        public const string Variable = "COODSZ_ROT_LINK";
        public const string Log = "Link: ";
        public const string Loud = "Link: !!! ";

        // Everything of ours on the link starts with "rot.".
        public const string Prefix = "rot.";
        // S70: the server's daily world digest, key DayKey, Seq the in game day.
        public const string ProbeChannel = "rot.probe";
        public const string DayKey = "day";
        // S16: world values Coop does not send (cultures, volunteers, races, a location's scenes,
        // the clan list; RotSyncRules). SetKey right after a write,
        // SnapshotKey for a player who entered the campaign (Seq the snapshot's number).
        public const string WorldChannel = "rot.world";
        public const string SetKey = "set";
        public const string SnapshotKey = "snapshot";
        // Coop's "this player is on the campaign map now", forwarded by the link under this key. It
        // comes after the join and again after every battle (coop-bridge.verify W4).
        public const string EnteredKey = "rot.entered";
        public const string EnteredType = "Coop.Core.Server.Connections.Messages.NetworkPlayerCampaignEntered";

        // The first arg of every message. A PC that reads another number ignores the message.
        public const int Protocol = 1;
        // Link.Version 1 has every call this module makes.
        public const int MinLinkVersion = 1;

        public const int ProbeEveryMs = 5000;
        public const int SessionPollMs = 1000;
        public const int SubscribeEveryMs = 5000;
        public const int MaxSubscribeTries = 20;
        public const int DrainPerTick = 64;

        // The link refuses a message over 256 KB (Coodsz.CoopLink LinkRules.MaxMessageBytes). A part
        // of ours stays well under it, with room for the channel, key and args.
        public const int LinkMaxBytes = 256 * 1024;
        public const int PartBytes = 200 * 1024;

        // Not set or "on" means on, "off" means off, anything else off with a line saying so (as
        // COODSZ_ROT_STORY).
        public static bool On(string variable, out bool unknown) => RotStoryRules.On(variable, out unknown);

        public static string SwitchOffLine(string variable)
        {
            On(variable, out var unknown);
            return Log + "off: " + Variable + "=" + CrashTraceRules.Shorten((variable ?? "").Trim())
                + (unknown ? " is neither on nor off" : "") + ", so the drift probe, the world value sync and the story popups stay off.";
        }

        public static string Channel(string channel, string key) => channel + "/" + key;

        // The protocol number in front of a message's args.
        public static List<string> WithProtocol(IEnumerable<string> args)
        {
            var all = new List<string> { Protocol.ToString(CultureInfo.InvariantCulture) };
            if (args != null)
                all.AddRange(args);
            return all;
        }

        // False when the first arg is not a number; protocol then is 0.
        public static bool ReadProtocol(IReadOnlyList<string> args, out int protocol, out List<string> rest)
        {
            protocol = 0;
            rest = new List<string>();
            if (args == null || args.Count == 0 || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out protocol))
                return false;
            rest.AddRange(args.Skip(1));
            return true;
        }

        // The link's own size rule (channel, key and args as UTF-8 plus the blob), so a part we
        // build is checked before it is sent.
        public static int Bytes(string channel, string key, IList<string> args, byte[] blob)
        {
            long total = Utf8(channel) + Utf8(key) + (blob?.LongLength ?? 0);
            if (args != null)
                foreach (var arg in args)
                    total += Utf8(arg);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private static long Utf8(string s) => string.IsNullOrEmpty(s) ? 0 : Encoding.UTF8.GetByteCount(s);

        public static string StartedLine(int linkVersion) =>
            Log + "started (link v" + linkVersion + ", our protocol " + Protocol + "). Channels " + ProbeChannel + " (drift probe), " + WorldChannel + " (world values) and " + RotPopupRules.Channel + " (story popups).";

        public static string OffLine(string reason) =>
            Log + "off: " + reason + ". The drift probe, the world value sync and the story popups stay off (each story popup gets its default answer on the server); everything else works as without the link.";

        public static string ProbeFailedReason(IList<string> missing) =>
            "Coop lacks what the link needs (" + string.Join(", ", (missing ?? new List<string>()).Take(8)) + ((missing?.Count ?? 0) > 8 ? ", ..." : "") + ")";

        public static string NoAnswerLine() =>
            Log + "off for this session: the server never answered the link's hello (its copy of this module has no link, or the link is off there). Nothing is compared or applied here.";

        public static string ProtocolLine(string channel, bool numbered, int protocol) =>
            Log + "ignored " + Shown(channel) + ": " + (numbered ? "protocol " + protocol + ", this PC speaks " + Protocol : "no protocol number") + " (said once).";

        public static string UnknownLine(string channel, string key) =>
            Log + "nothing here handles " + ShownChannel(channel, key) + "; ignored (said once).";

        // A channel and key from the other side as a log line shows them.
        public static string ShownChannel(string channel, string key) => Channel(Shown(channel), Shown(key));

        public const int MaxShownChars = 80;

        // A name from the other side: control chars and line breaks become ? and it is cut at MaxShownChars,
        // so a modified client can't forge log lines with a name. The same rule as the link's LinkRules.Shown.
        public static string Shown(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var shown = new StringBuilder(Math.Min(text.Length, MaxShownChars + 3));
            foreach (var c in text)
            {
                if (shown.Length >= MaxShownChars)
                {
                    shown.Append("...");
                    break;
                }
                shown.Append(char.IsControl(c) || c == '\u2028' || c == '\u2029' ? '?' : c);
            }
            return shown.ToString();
        }

        // Most once per session lines RotLink keeps; a modified client can make up any number of channels.
        public const int MaxSaid = 64;
        public const string MoreSaidLine = Log + "more lines like the ones above came; no more are said this session.";

        // The line to log for a once per session line, or null: each line once, and past MaxSaid lines one
        // MoreSaidLine and nothing more is kept.
        public static string Once(HashSet<string> said, string line)
        {
            if (said.Contains(line))
                return null;
            if (said.Count < MaxSaid)
            {
                said.Add(line);
                return line;
            }
            return said.Add(MoreSaidLine) ? MoreSaidLine : null;
        }

        // The link's channel for lifecycle events (Coodsz.CoopLink LinkRules.EventChannel).
        public const string EventChannel = "link.event";

        // Where one message goes. Lifecycle events come on the link's own "link.event" channel with
        // our key; ours carry the protocol number and go from the server to players' PCs, except a
        // player's answer to a story question (rot.story "answer"), which goes the other way.
        public static LinkRoute Route(string channel, string key, bool numbered, int protocol, bool fromServer, bool onServer)
        {
            if (channel == EventChannel)
                return key == EnteredKey && onServer ? LinkRoute.Entered : LinkRoute.NotFromServer;
            if (!numbered || protocol != Protocol)
                return LinkRoute.BadProtocol;
            if (channel == RotPopupRules.Channel && key == RotPopupRules.AnswerKey)
                return !fromServer && onServer ? LinkRoute.StoryAnswer : LinkRoute.NotFromServer;
            if (!fromServer || onServer)
                return LinkRoute.NotFromServer;
            if (channel == ProbeChannel && key == DayKey)
                return LinkRoute.Drift;
            if (channel == WorldChannel && key == SetKey)
                return LinkRoute.WorldSet;
            if (channel == WorldChannel && key == SnapshotKey)
                return LinkRoute.WorldSnapshot;
            if (channel == RotPopupRules.Channel && (key == RotPopupRules.AskKey || key == RotPopupRules.NoticeKey || key == RotPopupRules.ResultKey))
                return LinkRoute.Story;
            return LinkRoute.Unknown;
        }
    }

    internal enum LinkRoute
    {
        // Coop's "entered the campaign" for one player, on the server.
        Entered,
        // A daily digest, on a player's PC.
        Drift,
        WorldSet,
        WorldSnapshot,
        // A story question, notice or result, on a player's PC.
        Story,
        // A player's answer to a story question, on the server.
        StoryAnswer,
        // Another protocol number or none: one said once line.
        BadProtocol,
        // Not from the server, this is the server, or an event that is not ours here: dropped without a line.
        NotFromServer,
        // A channel or key nothing here handles: one said once line.
        Unknown,
    }

    // The open culture writes on the server: Realm of Thrones' writers call each other, so only the
    // outermost one takes the values before and compares after. End runs from a finalizer, also
    // after a throw, and never goes below zero; Reset at a session's end clears a stuck count.
    internal sealed class WriteDepth
    {
        public int Depth { get; private set; }

        // True for the outermost write, which takes the values before.
        public bool Begin() => Depth++ == 0;

        // True when the outermost write ended, which compares and sends.
        public bool End()
        {
            if (Depth == 0)
                return false;
            Depth--;
            return Depth == 0;
        }

        public void Reset() => Depth = 0;
    }

    internal enum LinkPhase
    {
        // Waiting for Coop's assemblies (the probe asks again every few seconds).
        Waiting,
        // The probe passed; the next tick loads and starts the link.
        Loading,
        Started,
        Off,
    }

    // The link's start up steps, with the reason when it stays off. The first step that fails
    // decides: off by the switch, Coop missing a member, no DLL, a link too old or its Start failed.
    internal sealed class LinkBoot
    {
        private long _nextProbe = long.MinValue;

        public LinkPhase Phase { get; private set; } = LinkPhase.Waiting;
        public string OffReason { get; private set; }

        public void SwitchedOff(string reason)
        {
            Phase = LinkPhase.Off;
            OffReason = reason;
        }

        public bool ProbeDue(long now)
        {
            if (Phase != LinkPhase.Waiting || (_nextProbe != long.MinValue && now < _nextProbe))
                return false;
            _nextProbe = now + RotLinkRules.ProbeEveryMs;
            return true;
        }

        // LinkProbe.Check's answer. A list that starts with "Coop's Common assembly" means Coop is
        // not loaded yet (single player without Coop stays here quietly).
        public void Probed(IList<string> missing)
        {
            if (Phase != LinkPhase.Waiting)
                return;
            if (missing == null || missing.Count == 0)
                Phase = LinkPhase.Loading;
            else if (!missing[0].StartsWith("Coop's Common assembly", StringComparison.Ordinal))
                SwitchedOff(RotLinkRules.ProbeFailedReason(missing));
        }

        // missingMembers: link members this module calls that the loaded copy lacks.
        public void Loaded(bool assembly, int version, IList<string> missingMembers, bool started)
        {
            if (Phase != LinkPhase.Loading)
                return;
            if (!assembly)
                SwitchedOff("Coodsz.CoopLink.dll is not next to this module's DLL or did not load");
            else if (version < RotLinkRules.MinLinkVersion)
                SwitchedOff("the loaded Coodsz.CoopLink.dll is version " + version + ", this module needs " + RotLinkRules.MinLinkVersion + " or newer");
            else if (missingMembers != null && missingMembers.Count > 0)
                SwitchedOff("the loaded Coodsz.CoopLink.dll lacks " + string.Join(", ", missingMembers));
            else if (!started)
                SwitchedOff("Coodsz.CoopLink's Start failed (its line above says why)");
            else
                Phase = LinkPhase.Started;
        }
    }
}
