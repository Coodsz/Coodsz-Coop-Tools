#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Coodsz.CoopLink;

namespace RealmOfThronesCoopCompat
{
    // Our end of Coodsz.CoopLink, the shared link our compat modules ship to send their own data
    // between the Coop server and players' PCs (its README is the contract). Three uses, all under
    // "rot.": the daily world drift probe (S70, RotDrift), the world values Coop does not send (S16,
    // RotWorldSync) and the story popups players answer (S41, RotStoryPopups).
    //
    // The link DLL ships next to ours in both bin folders, but this DLL does not reference it (see
    // RotLinkCalls). From the application tick, on the server and on players' PCs: LinkProbe.Check
    // (compiled in as source) until Coop is loaded, then LinkProbe.Load, Bind and Start. Without the
    // DLL, with a Coop the probe refuses, a link too old, COODSZ_ROT_LINK=off, or (on a PC) a server
    // that never answers the hello, one line says why and every call here does nothing; the rest of
    // the module works as before and the join is not touched. Sends happen only on the game thread,
    // only in a Coop session; received messages reach their handler on the game thread (Drain here,
    // or Coop's game thread queue when the link found it). Nothing here throws into the caller.
    internal static class RotLink
    {
        private static readonly LinkBoot Boot = new LinkBoot();
        private static readonly RotLinkCalls Calls = new RotLinkCalls();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        // Lines said once per session, so a caller that keeps trying does not flood the log.
        private static readonly HashSet<string> Said = new HashSet<string>();

        private static bool _session;
        private static bool _noAnswerSaid;
        private static bool _entered;
        private static int _enteredTries;
        private static long _lastEnteredTry = long.MinValue;
        private static long _lastSessionPoll = long.MinValue;
        private static volatile bool _sessionEdge;
        private static MethodInfo _tryResolveNetwork;

        public static bool Started => Boot.Phase == LinkPhase.Started;

        public static bool InSession => Started && _session;

        // SubModule.OnApplicationTick, every frame, on the server and on players' PCs.
        public static void Tick()
        {
            try
            {
                TickCore();
            }
            catch (Exception ex)
            {
                // The type name only, so a message that changes each frame is still said once.
                SayOnce(RotLinkRules.Loud + "the tick failed: " + ex.GetType().Name + " (said once per session).");
            }
        }

        private static void TickCore()
        {
            var now = Clock.ElapsedMilliseconds;
            if (Boot.Phase == LinkPhase.Waiting)
            {
                if (!Boot.ProbeDue(now))
                    return;
                var variable = Environment.GetEnvironmentVariable(RotLinkRules.Variable);
                if (!RotLinkRules.On(variable, out _))
                {
                    Boot.SwitchedOff(RotLinkRules.Variable + " is off");
                    SubModule.Log(RotLinkRules.SwitchOffLine(variable));
                    return;
                }
                Boot.Probed(LinkProbe.Check());
                SayIfOff();
            }
            if (Boot.Phase == LinkPhase.Loading)
            {
                StartLink();
                SayIfOff();
                if (Boot.Phase != LinkPhase.Started)
                    return;
                Coop.SessionChanged += _ => _sessionEdge = true;
            }
            if (Boot.Phase != LinkPhase.Started)
                return;

            PollSession(now);
            Calls.Drain(RotLinkRules.DrainPerTick);
            if (!_session)
                return;
            if (Coop.OnServer)
            {
                TrySubscribeEntered(now);
                RotDrift.ServerTick();
                RotStoryPopups.ServerTick();
                return;
            }
            RotDrift.ClientTick();
            RotStoryPopups.ClientTick();
            if (!_noAnswerSaid && Calls.PeerState() == "NoAnswer")
            {
                _noAnswerSaid = true;
                SubModule.Log(RotLinkRules.NoAnswerLine());
            }
        }

        private static void StartLink()
        {
            var assembly = LinkProbe.Load(BinFolder(), line => SubModule.Log(RotLinkRules.Log + line));
            int version = 0;
            List<string> missing = null;
            var started = false;
            if (assembly != null)
            {
                missing = Calls.Bind(assembly.GetType("Coodsz.CoopLink.Link", false), assembly.GetType("Coodsz.CoopLink.LinkMessage", false));
                if (missing.Count == 0)
                {
                    version = Calls.Version();
                    started = version >= RotLinkRules.MinLinkVersion && Calls.Start(Write, Network, RotLinkRules.Prefix, OnMessage);
                }
            }
            Boot.Loaded(assembly != null, version, missing, started);
            if (Boot.Phase == LinkPhase.Started)
                SubModule.Log(RotLinkRules.StartedLine(version));
        }

        private static void SayIfOff()
        {
            if (Boot.Phase == LinkPhase.Off)
                SubModule.Log(RotLinkRules.OffLine(Boot.OffReason));
        }

        // Coop's edges when they are hooked, and a slow poll for when they are not.
        private static void PollSession(long now)
        {
            if (!_sessionEdge && _lastSessionPoll != long.MinValue && now - _lastSessionPoll < RotLinkRules.SessionPollMs)
                return;
            _sessionEdge = false;
            _lastSessionPoll = now;
            var on = Coop.InSession;
            if (on == _session)
                return;
            _session = on;
            Said.Clear();
            if (on)
            {
                Calls.SessionStarted();
                return;
            }
            Calls.SessionEnded();
            _noAnswerSaid = false;
            RotWorldSync.SessionEnded();
            RotDrift.SessionEnded();
            RotStoryPopups.SessionEnded();
        }

        // The server's snapshot for a player waits for Coop's "entered the campaign". The broker
        // keeps the subscription across sessions; the link says no until Coop.Core is loaded.
        private static void TrySubscribeEntered(long now)
        {
            if (_entered || _enteredTries >= RotLinkRules.MaxSubscribeTries || (_lastEnteredTry != long.MinValue && now - _lastEnteredTry < RotLinkRules.SubscribeEveryMs))
                return;
            _lastEnteredTry = now;
            _enteredTries++;
            _entered = Calls.Subscribe(RotLinkRules.EnteredType, RotLinkRules.EnteredKey);
            if (!_entered && _enteredTries >= RotLinkRules.MaxSubscribeTries)
                SubModule.Log(RotLinkRules.Loud + "Coop's " + RotLinkRules.EnteredType + " was never found, so a player who joins gets no culture snapshot.");
        }

        // The server to every player, through Coop's join gate. Only in a session, and only once
        // some player's PC answered the hello (a PC without the link cannot read our messages).
        // reason says why not, for the caller's own line.
        public static bool ToAll(string channel, string key, int seq, IList<string> args, byte[] blob, out string reason)
        {
            reason = "no Coop session";
            if (!InSession || !Coop.OnServer)
                return false;
            try
            {
                var all = RotLinkRules.WithProtocol(args);
                if (!Fits(channel, key, all, blob, out reason))
                    return Refused(channel, key, reason);
                if (Calls.PeerState() != "Answered")
                    return Refused(channel, key, reason = "no player's PC has answered the link yet");
                var network = Network();
                if (network == null)
                    return Refused(channel, key, reason = "Coop's network is not there");
                if (Calls.ToAll(network, channel, key, seq, all, blob))
                    return true;
                reason = "the link refused it (its own line says why)";
                return false;
            }
            catch (Exception ex)
            {
                return Refused(channel, key, reason = ex.GetType().Name + ": " + ex.Message);
            }
        }

        // The server to one player, through Coop's join gate (dropped before that player has the
        // join save, held while loading, passed after). Only to a PC that said hello.
        public static bool ToPlayer(object peer, string channel, string key, int seq, IList<string> args, byte[] blob, out string reason)
        {
            reason = "no Coop session";
            if (!InSession || !Coop.OnServer || peer == null)
                return false;
            try
            {
                var all = RotLinkRules.WithProtocol(args);
                if (!Fits(channel, key, all, blob, out reason))
                    return Refused(channel, key, reason);
                if (Calls.PeerVersion(peer) <= 0)
                    return Refused(channel, key, reason = "that player's PC has not answered the link (no link there?)");
                var network = Network();
                if (network == null)
                    return Refused(channel, key, reason = "Coop's network is not there");
                if (Calls.ToPlayer(network, peer, false, channel, key, seq, all, blob))
                    return true;
                reason = "the link refused it (its own line says why)";
                return false;
            }
            catch (Exception ex)
            {
                return Refused(channel, key, reason = ex.GetType().Name + ": " + ex.Message);
            }
        }

        // A player's PC to the server, only once the server answered the hello.
        public static bool ToServer(string channel, string key, int seq, IList<string> args, out string reason)
        {
            reason = "no Coop session";
            if (!InSession || Coop.OnServerProcess)
                return false;
            try
            {
                var all = RotLinkRules.WithProtocol(args);
                if (!Fits(channel, key, all, null, out reason))
                    return Refused(channel, key, reason);
                if (Calls.PeerState() != "Answered")
                    return Refused(channel, key, reason = "the server has not answered the link");
                var network = Network();
                if (network == null)
                    return Refused(channel, key, reason = "Coop's network is not there");
                if (Calls.ToServer(network, channel, key, seq, all, null))
                    return true;
                reason = "the link refused it (its own line says why)";
                return false;
            }
            catch (Exception ex)
            {
                return Refused(channel, key, reason = ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Server: that player's PC said hello on the link.
        public static bool PeerHasLink(object peer)
        {
            try { return InSession && peer != null && Calls.PeerVersion(peer) > 0; }
            catch { return false; }
        }

        // Coop's service for this session by its interface's full name, resolved fresh each call
        // (ContainerProvider.TryResolve<T>), or null.
        public static object Resolve(string interfaceName)
        {
            try
            {
                var container = Coop.FindType("GameInterface.ContainerProvider");
                var type = Coop.FindType(interfaceName);
                var method = container?.GetMethod("TryResolve", BindingFlags.Public | BindingFlags.Static);
                if (method == null || type == null || !method.IsGenericMethodDefinition)
                    return null;
                var args = new object[] { null };
                return (bool)method.MakeGenericMethod(type).Invoke(null, args) ? args[0] : null;
            }
            catch
            {
                return null;
            }
        }

        // The link's own byte limit, checked before the link is asked.
        private static bool Fits(string channel, string key, IList<string> args, byte[] blob, out string reason)
        {
            var bytes = RotLinkRules.Bytes(channel, key, args, blob);
            reason = bytes > RotLinkRules.LinkMaxBytes ? bytes + " bytes, over the link's limit of " + RotLinkRules.LinkMaxBytes : null;
            return reason == null;
        }

        private static bool Refused(string channel, string key, string reason)
        {
            SayOnce(RotLinkRules.Log + "not sent " + RotLinkRules.Channel(channel, key) + ": " + reason + " (said once per session).");
            return false;
        }

        private static void SayOnce(string line)
        {
            var said = RotLinkRules.Once(Said, line);
            if (said != null)
                SubModule.Log(said);
        }

        // The link's callback for every message under "rot.", on the game thread. Where each goes is
        // RotLinkRules.Route.
        private static void OnMessage(object linkMessage)
        {
            RotLinkMessage message = null;
            try
            {
                message = Calls.Read(linkMessage);
                var numbered = RotLinkRules.ReadProtocol(message.Args, out var protocol, out var rest);
                switch (RotLinkRules.Route(message.Channel, message.Key, numbered, protocol, message.FromServer, Coop.OnServerProcess))
                {
                    case LinkRoute.Entered:
                        RotWorldSync.Entered(message.Sender);
                        RotStoryPopups.Entered(message.Sender);
                        return;
                    case LinkRoute.Story:
                        RotStoryPopups.Received(message.Key, message.Seq, rest);
                        return;
                    case LinkRoute.StoryAnswer:
                        RotStoryPopups.Answered(message.Sender, message.Seq, rest);
                        return;
                    case LinkRoute.Drift:
                        RotDrift.Received(message.Seq, message.Blob);
                        return;
                    case LinkRoute.WorldSet:
                    case LinkRoute.WorldSnapshot:
                        RotWorldSync.Received(message.Key, message.Seq, rest, message.Blob);
                        return;
                    case LinkRoute.BadProtocol:
                        SayOnce(RotLinkRules.ProtocolLine(message.Channel, numbered, protocol));
                        return;
                    case LinkRoute.Unknown:
                        SayOnce(RotLinkRules.UnknownLine(message.Channel, message.Key));
                        return;
                }
            }
            catch (Exception ex)
            {
                SubModule.Log(RotLinkRules.Loud + "handling " + (message == null ? "a message" : RotLinkRules.ShownChannel(message.Channel, message.Key)) + " failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Write(string line) => SubModule.Log(RotLinkRules.Log + "(shared) " + line);

        private static string BinFolder()
        {
            try { return Path.GetDirectoryName(typeof(RotLink).Assembly.Location) ?? ""; }
            catch { return ""; }
        }

        // Coop's INetwork for this session, resolved fresh on every call (the link's README: never
        // keep it across sessions). Also the link's NetworkProvider, which the server's hello answer
        // calls on Coop's network thread, so it only reads.
        private static object Network()
        {
            try
            {
                var tryResolve = _tryResolveNetwork;
                if (tryResolve == null)
                {
                    var container = Coop.FindType("GameInterface.ContainerProvider");
                    var network = Coop.FindType("Common.Network.INetwork");
                    var method = container?.GetMethod("TryResolve", BindingFlags.Public | BindingFlags.Static);
                    if (method == null || network == null || !method.IsGenericMethodDefinition)
                        return null;
                    tryResolve = _tryResolveNetwork = method.MakeGenericMethod(network);
                }
                var args = new object[] { null };
                return (bool)tryResolve.Invoke(null, args) ? args[0] : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
