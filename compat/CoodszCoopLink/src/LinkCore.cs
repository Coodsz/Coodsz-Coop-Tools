// Coodsz Coop Link: the parts of the link that need no Coop type. The message a mod gets, the size and
// direction rules, Coop's wire id rule, and the hub that keeps the channel callbacks, the receive queue and
// the handshake state. Compiled into Coodsz.CoopLink.dll and into the launcher's tests.
//
// The public API is additive from version 1 on: never change or remove a public member here, only add.
// Two mods can ship this DLL and the first copy loaded wins, so a mod built against a newer copy checks
// Link.Version before it calls anything new.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Coodsz.CoopLink
{
    /// <summary>One message from the other side, or a Coop lifecycle event on channel "link.event".</summary>
    public sealed class LinkMessage
    {
        private static readonly string[] NoArgs = new string[0];
        private static readonly byte[] NoBlob = new byte[0];

        public LinkMessage(int version, string? channel, string? key, int seq, IList<string>? args, byte[]? blob, object? sender, bool fromServer)
        {
            Version = version;
            Channel = channel ?? "";
            Key = key ?? "";
            Seq = seq;
            Args = args == null || args.Count == 0 ? NoArgs : new List<string>(args).AsReadOnly();
            Blob = blob ?? NoBlob;
            Sender = sender;
            FromServer = fromServer;
        }

        /// <summary>The sender's link version, 0 for a lifecycle event.</summary>
        public int Version { get; }

        public string Channel { get; }

        public string Key { get; }

        public int Seq { get; }

        /// <summary>Never null.</summary>
        public IReadOnlyList<string> Args { get; }

        /// <summary>Never null.</summary>
        public byte[] Blob { get; }

        /// <summary>
        /// Who sent it, as Coop hands it over. On the server the player's LiteNetLib NetPeer (map it to a player
        /// with Coop's IPlayerManager); on a player's PC the server's NetPeer; for a lifecycle event the event's
        /// own source (the peer for NetworkPlayerCampaignEntered).
        /// </summary>
        public object? Sender { get; }

        /// <summary>True for a ToClient message, false for ToServer and lifecycle events.</summary>
        public bool FromServer { get; }
    }

    /// <summary>What the link is doing. A failed probe is not in here: the link DLL is never loaded then, see LinkProbe.</summary>
    public enum LinkState
    {
        NotStarted = 0,
        StartFailed = 1,
        Started = 2,
        InSession = 3,
        PeerAnswered = 4,
    }

    /// <summary>Whether the other side speaks the link, learned from the "link.hello" handshake.</summary>
    public enum PeerLinkState
    {
        NoSession = 0,
        Waiting = 1,
        Answered = 2,
        NoAnswer = 3,
    }

    public static class LinkRules
    {
        /// <summary>
        /// Largest message the link sends: channel, key and every arg as UTF-8 plus the blob. Coop sends a
        /// message too big for one datagram bare on LiteNetLib's reliable ordered channel 0, which splits it
        /// into at most 65535 fragments (about 63 MB at the smallest MTU), so 256 KB is far from the hard
        /// limit. It is about 260 fragments in a 64 packet reliable window, so it holds the world stream back
        /// for a few windows only, well under Coop's 10000 queued packet overload pause.
        /// </summary>
        public const int MaxMessageBytes = 256 * 1024;

        public const int MaxArgs = 4096;

        /// <summary>
        /// Most routes without a callback the link logs by name. A modified client can make up any number,
        /// so after these one line says more came and nothing more is kept.
        /// </summary>
        internal const int MaxUnknownLogged = 64;

        /// <summary>Longest route or key a log line shows.</summary>
        internal const int MaxShownChars = 80;

        public const string ReservedPrefix = "link.";
        public const string HelloChannel = "link.hello";
        public const string EventChannel = "link.event";
        public const string HelloKey = "hello";
        public const string WelcomeKey = "welcome";

        /// <summary>ToClient counts only on a player's PC, ToServer only on the server.</summary>
        public static bool Accepts(bool toClient, bool isServer) => toClient != isServer;

        /// <summary>Counts the message's bytes and says why it is refused, or returns true.</summary>
        public static bool CheckSize(string? channel, string? key, IList<string>? args, byte[]? blob, out int bytes, out string reason)
        {
            reason = "";
            bytes = 0;
            int count = args?.Count ?? 0;
            if (count > MaxArgs)
            {
                reason = "too many args (" + count + ", the limit is " + MaxArgs + ")";
                return false;
            }
            long total = Utf8(channel) + Utf8(key) + (blob?.LongLength ?? 0);
            for (int i = 0; i < count && total <= MaxMessageBytes; i++)
                total += Utf8(args![i]);
            bytes = total > int.MaxValue ? int.MaxValue : (int)total;
            if (total > MaxMessageBytes)
            {
                reason = "message too large (over " + MaxMessageBytes + " bytes); split it into several messages or send less";
                return false;
            }
            return true;
        }

        /// <summary>A channel a mod may send on or register: not empty and not one of the link's own.</summary>
        public static bool IsModChannel(string? channel) =>
            !string.IsNullOrEmpty(channel) && !channel!.StartsWith(ReservedPrefix, StringComparison.Ordinal);

        /// <summary>
        /// The id Coop puts on the wire for a message type: 32 bit FNV-1a over the full name, each char
        /// hashed as its low byte then its high byte, then masked to 31 bits. Our own code of the rule in
        /// Coop's Common\Serialization\SerializableTypeMapper.cs.
        /// </summary>
        public static int WireId(string fullName)
        {
            uint hash = 2166136261;
            foreach (char c in fullName)
            {
                hash = unchecked((hash ^ (byte)(c & 0xFF)) * 16777619);
                hash = unchecked((hash ^ (byte)(c >> 8)) * 16777619);
            }
            return (int)(hash & 0x7FFFFFFF);
        }

        /// <summary>
        /// A route or key from the other side as a log line shows it: control chars and line breaks become ?
        /// and it is cut at <see cref="MaxShownChars"/>, so a name can't forge a log line of its own.
        /// </summary>
        internal static string Shown(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var shown = new StringBuilder(Math.Min(text!.Length, MaxShownChars + 3));
            foreach (char c in text)
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

        private static long Utf8(string? s) => string.IsNullOrEmpty(s) ? 0 : Encoding.UTF8.GetByteCount(s);
    }

    /// <summary>
    /// What TrySubscribeByName remembers, so a mod may call it again and again until Coop's type loads: each
    /// type name it could not find, and each one whose subscribe failed, is logged once, each Coop message
    /// type is subscribed once, and each key is added to a type once (and logged once).
    /// </summary>
    internal sealed class SubscribeMemory
    {
        private readonly ConcurrentDictionary<string, byte> _missing = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _failed = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<Type, byte> _claimed = new ConcurrentDictionary<Type, byte>();
        private readonly ConcurrentDictionary<Type, string[]> _keys = new ConcurrentDictionary<Type, string[]>();

        /// <summary>True the first time this type name was not found, false for every later miss.</summary>
        public bool FirstMiss(string typeName) => _missing.TryAdd(typeName, 0);

        /// <summary>True the first time a call for this type name failed, false for every later failure.</summary>
        public bool FirstFailure(string typeName) => _failed.TryAdd(typeName, 0);

        /// <summary>
        /// True only for the one caller that should subscribe this type on Coop's broker. When that
        /// subscribe fails, Unclaim lets a later call try again. Claims and keys are kept apart, so an
        /// Unclaim never drops a key another caller added meanwhile.
        /// </summary>
        public bool Claim(Type type) => _claimed.TryAdd(type, 0);

        public void Unclaim(Type type) => _claimed.TryRemove(type, out _);

        /// <summary>Adds the key to the type's keys. False when it was there already.</summary>
        public bool AddKey(Type type, string key)
        {
            bool added = false;
            _keys.AddOrUpdate(type, _ => { added = true; return new[] { key }; }, (_, keys) =>
            {
                if (Array.IndexOf(keys, key) >= 0)
                {
                    added = false;
                    return keys;
                }
                added = true;
                var result = new string[keys.Length + 1];
                keys.CopyTo(result, 0);
                result[keys.Length] = key;
                return result;
            });
            return added;
        }

        /// <summary>The keys a type's events are forwarded with; false when the type has none.</summary>
        public bool TryGetKeys(Type type, out string[] keys) => _keys.TryGetValue(type, out keys!);
    }

    /// <summary>
    /// The channel callbacks, the receive queue and the handshake state. Receive runs on Coop's network
    /// thread; Drain runs on the game thread. Nothing in here throws to its caller.
    /// </summary>
    internal sealed class LinkHub
    {
        public const int MaxQueued = 20000;
        public const int HelloIntervalMs = 3000;
        public const int MaxHelloTries = 20;

        private readonly object _gate = new object();
        private readonly Dictionary<string, Action<LinkMessage>> _game = new Dictionary<string, Action<LinkMessage>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<LinkMessage, bool>> _network = new Dictionary<string, Func<LinkMessage, bool>>(StringComparer.Ordinal);
        private readonly HashSet<string> _unknownLogged = new HashSet<string>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<Entry> _queue = new ConcurrentQueue<Entry>();
        private readonly object _queueGate = new object();
        private readonly ConcurrentDictionary<object, int> _peerVersions = new ConcurrentDictionary<object, int>(new SameObject());
        private readonly Action<string> _log;
        private readonly Func<bool> _isServer;
        private readonly Action<object?> _sendWelcome;
        private readonly Func<bool> _sendHello;
        private readonly Func<long> _nowMs;

        private int _queued;
        private int _draining;
        private int _unknownCount;
        private int _overflowLogged;
        private int _tooBigLogged;
        private bool _unknownFullLogged;
        private volatile bool _inSession;
        private int _serverVersion;
        private int _helloTries;
        private long _lastHelloMs;
        private long _lastNumber;

        /// <param name="sendWelcome">Server: answers a hello to this peer.</param>
        /// <param name="sendHello">Client: says hello to the server, true when it was handed to Coop.</param>
        public LinkHub(Action<string> log, Func<bool> isServer, Action<object?> sendWelcome, Func<bool> sendHello, Func<long>? nowMs = null)
        {
            _log = log;
            _isServer = isServer;
            _sendWelcome = sendWelcome;
            _sendHello = sendHello;
            Stopwatch clock = Stopwatch.StartNew();
            _nowMs = nowMs ?? (() => clock.ElapsedMilliseconds);
        }

        public int UnknownChannelCount => Volatile.Read(ref _unknownCount);

        public int Queued => Volatile.Read(ref _queued);

        public bool InSession => _inSession;

        public int ServerVersion => Volatile.Read(ref _serverVersion);

        public int PeerVersion(object? peer) => peer != null && _peerVersions.TryGetValue(peer, out int v) ? v : 0;

        public PeerLinkState PeerState
        {
            get
            {
                if (SafeIsServer())
                    return !_peerVersions.IsEmpty ? PeerLinkState.Answered : _inSession ? PeerLinkState.Waiting : PeerLinkState.NoSession;
                if (ServerVersion > 0) return PeerLinkState.Answered;
                if (!_inSession) return PeerLinkState.NoSession;
                return Volatile.Read(ref _helloTries) >= MaxHelloTries ? PeerLinkState.NoAnswer : PeerLinkState.Waiting;
            }
        }

        public bool Register(string? prefix, Action<LinkMessage>? onMessage)
        {
            if (!LinkRules.IsModChannel(prefix) || onMessage == null)
            {
                _log("Register refused: the prefix must not be empty or start with \"link.\" and the callback must not be null (" + (prefix ?? "null") + ").");
                return false;
            }
            lock (_gate)
            {
                if (_game.ContainsKey(prefix!)) _log("Register: the callback for \"" + prefix + "\" was replaced.");
                _game[prefix!] = onMessage;
            }
            return true;
        }

        public bool RegisterNetworkThread(string? prefix, Func<LinkMessage, bool>? onMessage)
        {
            if (!LinkRules.IsModChannel(prefix) || onMessage == null)
            {
                _log("RegisterOnNetworkThread refused: the prefix must not be empty or start with \"link.\" and the callback must not be null (" + (prefix ?? "null") + ").");
                return false;
            }
            lock (_gate)
            {
                if (_network.ContainsKey(prefix!)) _log("RegisterOnNetworkThread: the callback for \"" + prefix + "\" was replaced.");
                _network[prefix!] = onMessage;
            }
            return true;
        }

        /// <summary>
        /// Takes one message that passed the direction rule, on the network thread. Handshake messages are
        /// handled here; a network thread callback may take the message; everything else is queued for Drain.
        /// Returns the message's queue number, or 0 when it was not queued.
        /// </summary>
        public long Receive(LinkMessage message)
        {
            try
            {
                // The send side's limits hold on receive too, so a modified client can't queue more. Args is
                // a read only list or an empty array, both an IList.
                if (!LinkRules.CheckSize(message.Channel, message.Key, message.Args as IList<string>, message.Blob, out _, out string reason))
                {
                    if (Interlocked.Exchange(ref _tooBigLogged, 1) == 0)
                        _log("A message on \"" + LinkRules.Shown(RouteOf(message)) + "\" was dropped: " + reason + " (logged once).");
                    return 0;
                }

                if (!string.Equals(message.Channel, LinkRules.EventChannel, StringComparison.Ordinal) && message.Version > 0)
                    NotePeer(message);

                if (string.Equals(message.Channel, LinkRules.HelloChannel, StringComparison.Ordinal))
                {
                    if (!message.FromServer && string.Equals(message.Key, LinkRules.HelloKey, StringComparison.Ordinal))
                        _sendWelcome(message.Sender);
                    return 0;
                }

                Func<LinkMessage, bool>? onNetwork;
                lock (_gate) onNetwork = Find(_network, RouteOf(message));
                if (onNetwork != null)
                {
                    try
                    {
                        if (onNetwork(message)) return 0;
                    }
                    catch (Exception e)
                    {
                        _log("A network thread callback for \"" + LinkRules.Shown(RouteOf(message)) + "\" failed, the message goes to the queue: " + e.GetType().Name + ": " + e.Message);
                    }
                }

                if (Interlocked.Increment(ref _queued) > MaxQueued)
                {
                    Interlocked.Decrement(ref _queued);
                    if (Interlocked.Exchange(ref _overflowLogged, 1) == 0)
                        _log("The receive queue is full (" + MaxQueued + " messages); new ones are dropped until Drain runs. Call Link.Drain from the game thread tick.");
                    return 0;
                }
                lock (_queueGate)
                {
                    long number = ++_lastNumber;
                    _queue.Enqueue(new Entry(message, number));
                    return number;
                }
            }
            catch (Exception e)
            {
                _log("Receive failed: " + e.GetType().Name + ": " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// Hands up to <paramref name="maxItems"/> queued messages to their callbacks, oldest first, on the
        /// calling (game) thread. A drain already running (a callback that calls Drain) makes this return 0.
        /// </summary>
        public int Drain(int maxItems) => DrainCore(maxItems, long.MaxValue, true);

        /// <summary>
        /// Hands over every queued message up to and including number <paramref name="through"/>, oldest
        /// first. Coop's game thread queue runs this once per message, so each message's callback runs at
        /// the place in Coop's own game thread work where the message arrived. Messages the mod's Drain
        /// already handed over are not handed over again.
        /// </summary>
        public int DrainThrough(long through) => DrainCore(int.MaxValue, through, false);

        private int DrainCore(int maxItems, long through, bool hello)
        {
            if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0) return 0;
            int done = 0;
            try
            {
                if (hello) MaybeSayHello();
                while (done < maxItems && _queue.TryPeek(out Entry? next) && next.Number <= through && _queue.TryDequeue(out Entry? entry))
                {
                    Interlocked.Decrement(ref _queued);
                    Volatile.Write(ref _overflowLogged, 0);
                    done++;
                    Dispatch(entry.Message);
                }
            }
            catch (Exception e)
            {
                _log("Drain failed: " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                Volatile.Write(ref _draining, 0);
            }
            return done;
        }

        /// <summary>The mod's session detection fired. The handshake state is kept: a welcome can come first.</summary>
        public void SessionStarted() => _inSession = true;

        /// <summary>The session is over: queued messages and the handshake state go.</summary>
        public void SessionEnded()
        {
            _inSession = false;
            int dropped = 0;
            while (_queue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _queued);
                dropped++;
            }
            _peerVersions.Clear();
            Volatile.Write(ref _serverVersion, 0);
            Volatile.Write(ref _helloTries, 0);
            Interlocked.Exchange(ref _lastHelloMs, 0);
            if (dropped > 0) _log("Session ended: " + dropped + " queued messages were dropped.");
        }

        /// <summary>Client: says hello now (Coop's module check answer just arrived), unless the server already answered.</summary>
        public void SayHelloNow()
        {
            if (SafeIsServer() || ServerVersion > 0) return;
            SendHello();
        }

        private void MaybeSayHello()
        {
            if (!_inSession || ServerVersion > 0 || SafeIsServer()) return;
            if (Volatile.Read(ref _helloTries) >= MaxHelloTries) return;
            long now = _nowMs();
            if (Volatile.Read(ref _helloTries) > 0 && now - Interlocked.Read(ref _lastHelloMs) < HelloIntervalMs) return;
            SendHello();
        }

        private void SendHello()
        {
            int tries = Interlocked.Increment(ref _helloTries);
            Interlocked.Exchange(ref _lastHelloMs, _nowMs());
            bool sent = false;
            try { sent = _sendHello(); }
            catch (Exception e) { _log("Hello failed: " + e.GetType().Name + ": " + e.Message); }
            if (tries == MaxHelloTries && ServerVersion == 0)
                _log("The server has not answered " + MaxHelloTries + " hellos; it may not run the link." + (sent ? "" : " The last hello was not sent."));
        }

        private void NotePeer(LinkMessage message)
        {
            if (message.FromServer)
            {
                if (Interlocked.Exchange(ref _serverVersion, message.Version) == 0)
                    _log("The server speaks link version " + message.Version + ".");
            }
            else if (message.Sender != null)
            {
                _peerVersions[message.Sender] = message.Version;
            }
        }

        private void Dispatch(LinkMessage message)
        {
            string route = RouteOf(message);
            Action<LinkMessage>? onMessage;
            lock (_gate) onMessage = Find(_game, route);
            if (onMessage == null)
            {
                Interlocked.Increment(ref _unknownCount);
                bool first = false, full = false;
                lock (_gate)
                {
                    if (_unknownLogged.Count < LinkRules.MaxUnknownLogged) first = _unknownLogged.Add(route);
                    else if (!_unknownFullLogged && !_unknownLogged.Contains(route)) full = _unknownFullLogged = true;
                }
                if (first) _log("No callback is registered for \"" + LinkRules.Shown(route) + "\"; its messages are dropped (logged once).");
                if (full) _log("More unknown routes came; their messages are dropped too, and no more are logged.");
                return;
            }
            try
            {
                onMessage(message);
            }
            catch (Exception e)
            {
                _log("The callback for \"" + LinkRules.Shown(route) + "\" failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Lifecycle events are routed by their key, which the mod chose with its own prefix, so two mods can
        // each forward the same Coop event to themselves.
        private static string RouteOf(LinkMessage message) =>
            string.Equals(message.Channel, LinkRules.EventChannel, StringComparison.Ordinal) ? message.Key : message.Channel;

        private static T? Find<T>(Dictionary<string, T> callbacks, string route) where T : class
        {
            T? best = null;
            int bestLength = -1;
            foreach (KeyValuePair<string, T> entry in callbacks)
            {
                if (entry.Key.Length > bestLength && route.StartsWith(entry.Key, StringComparison.Ordinal))
                {
                    best = entry.Value;
                    bestLength = entry.Key.Length;
                }
            }
            return best;
        }

        private bool SafeIsServer()
        {
            try { return _isServer(); }
            catch (Exception) { return false; }
        }

        private sealed class Entry
        {
            public Entry(LinkMessage message, long number)
            {
                Message = message;
                Number = number;
            }

            public LinkMessage Message { get; }

            public long Number { get; }
        }

        // NetPeer is keyed by the object itself, whatever Equals it may have.
        private sealed class SameObject : IEqualityComparer<object>
        {
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
