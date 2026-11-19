// Coodsz Coop Link: lets our compat modules send their own data between the Coop server and the players'
// PCs, over Coop's own network and message broker.
//
// THE PUBLIC API IS ADDITIVE FROM VERSION 1 ON. Never change or remove a public member, only add. Several
// mods ship this DLL and the first copy loaded wins, so a mod built against a newer copy checks Link.Version
// before it calls anything new. The assembly version stays 1.0.0.0 for the same reason.
//
// Load it only after LinkProbe.Check() returned an empty list, from a game tick (never inside an AssemblyLoad
// callback or Coop's container build). Every public member catches what it calls: a failure is a false or a
// default plus one line through Log, never an exception into the mod.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Common;
using Common.Messaging;
using Common.Network;
using LiteNetLib;

namespace Coodsz.CoopLink
{
    public static class Link
    {
        private static readonly object StartGate = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly LinkHub Hub = new LinkHub(Write, SafeIsServer, SendWelcome, SendHello);
        private static readonly SubscribeMemory Subscribed = new SubscribeMemory();
        private const string HelloTriggerName = "Coop.Core.Server.Connections.Messages.NetworkModuleVersionsValidated";

        // The one subscriber, kept here for good: Coop's broker only holds it weakly.
        private static LinkSubscriber? _subscriber;
        private static volatile bool _started;
        private static volatile bool _startFailed;
        private static volatile bool _helloTriggerFound;
        private static long _lastTriggerTry;
        private static MethodInfo? _runSafe;
        private static object?[]? _runSafeArgs;

        /// <summary>The API level of the copy that is loaded. 1 for this one.</summary>
        public static int Version => 1;

        /// <summary>Where the link writes its one line per failure. Add yours with +=; a mod's handler that throws is ignored.</summary>
        public static Action<string>? Log { get; set; }

        /// <summary>
        /// Gives the link Coop's INetwork for the current session whenever it asks, or null outside a session.
        /// Only the built in handshake uses it. Resolve it fresh on every call (ContainerProvider.TryResolve), never cache it.
        /// </summary>
        public static Func<object?>? NetworkProvider { get; set; }

        /// <summary>
        /// True (the default): received messages are also handed to Coop's game thread queue when Coop has
        /// GameThread.RunSafe, so a mod's callback runs in order with Coop's own game thread work. Drain still
        /// picks up whatever that queue has not run. False: only Drain hands messages over.
        /// </summary>
        public static bool UseCoopGameThread { get; set; } = true;

        public static LinkState State
        {
            get
            {
                if (_startFailed) return LinkState.StartFailed;
                if (!_started) return LinkState.NotStarted;
                if (Hub.PeerState == PeerLinkState.Answered) return LinkState.PeerAnswered;
                return Hub.InSession ? LinkState.InSession : LinkState.Started;
            }
        }

        /// <summary>On a player's PC: whether the server answered the hello. On the server: Answered once any player said hello.</summary>
        public static PeerLinkState PeerState => Hub.PeerState;

        /// <summary>On a player's PC: the server's link version, 0 until it answered.</summary>
        public static int ServerLinkVersion => Hub.ServerVersion;

        /// <summary>On the server: the link version of this player's NetPeer, 0 until it said hello or sent anything.</summary>
        public static int GetPeerLinkVersion(object? peer) => Hub.PeerVersion(peer);

        /// <summary>Messages that reached no callback because no prefix matched.</summary>
        public static int UnknownChannelCount => Hub.UnknownChannelCount;

        /// <summary>Raised with true or false when a mod reports a session start or end, on that mod's thread.</summary>
        public static event Action<bool>? SessionChanged;

        /// <summary>Subscribes the link on Coop's message broker. Safe to call from several mods; later calls do nothing.</summary>
        public static bool Start()
        {
            lock (StartGate)
            {
                if (_started) return true;
                try
                {
                    StartCore();
                    _started = true;
                    _startFailed = false;
                    Write("Started, link version " + Version + (_runSafe != null ? ", with Coop's game thread queue." : ", without Coop's game thread queue (Drain only)."));
                    return true;
                }
                catch (Exception e)
                {
                    _startFailed = true;
                    Write("Start failed: " + Describe(e));
                    return false;
                }
            }
        }

        /// <summary>
        /// The game thread callback for every channel that starts with <paramref name="channelPrefix"/> (for
        /// example "rot."). The longest matching prefix wins. Registering a prefix again replaces its callback.
        /// Lifecycle events are routed by their key instead of their channel.
        /// </summary>
        public static bool Register(string channelPrefix, Action<LinkMessage> onMessage) => Hub.Register(channelPrefix, onMessage);

        /// <summary>
        /// A callback that runs on Coop's network thread, inside the receive, before the message is queued.
        /// Return true to take the message (it is not queued). Only for work that must keep its place in the
        /// network stream: store an immutable object or answer with ToPlayer(immediate: true). Never touch the game here.
        /// </summary>
        public static bool RegisterOnNetworkThread(string channelPrefix, Func<LinkMessage, bool> onMessage) => Hub.RegisterNetworkThread(channelPrefix, onMessage);

        /// <summary>
        /// Hands queued messages to their callbacks, oldest first. Call it from the mod's game thread tick
        /// (OnApplicationTick) every frame. Returns how many were handed over.
        /// </summary>
        public static int Drain(int maxItems)
        {
            try
            {
                TryFindHelloTrigger();
            }
            catch (Exception e)
            {
                Write("Hello trigger lookup failed: " + Describe(e));
            }
            return Hub.Drain(maxItems);
        }

        /// <summary>The mod's own session detection saw a Coop session start. Idempotent.</summary>
        public static void SessionStarted()
        {
            if (Hub.InSession) return;
            Hub.SessionStarted();
            RaiseSession(true);
        }

        /// <summary>The mod's own session detection saw the session end. Drops queued messages and the handshake state. Idempotent.</summary>
        public static void SessionEnded()
        {
            if (!Hub.InSession) return;
            Hub.SessionEnded();
            RaiseSession(false);
        }

        /// <summary>A player's PC to the server (Coop's INetwork.SendAll on a client). <paramref name="network"/> is Coop's INetwork of this session.</summary>
        public static bool ToServer(object network, string channel, string key, int seq, IList<string>? args, byte[]? blob) =>
            Send(Target.Server, network, null, false, channel, key, seq, args, blob, true);

        /// <summary>The server to every connected player (INetwork.SendAll, through Coop's join gate).</summary>
        public static bool ToAll(object network, string channel, string key, int seq, IList<string>? args, byte[]? blob) =>
            Send(Target.All, network, null, false, channel, key, seq, args, blob, true);

        /// <summary>
        /// The server to one player's NetPeer. immediate false is INetwork.Send, through Coop's join gate
        /// (dropped before that player has the join save, held and replayed while loading, passed after).
        /// immediate true is INetwork.SendImmediate, which skips the gate.
        /// </summary>
        public static bool ToPlayer(object network, object peer, bool immediate, string channel, string key, int seq, IList<string>? args, byte[]? blob) =>
            Send(Target.Player, network, peer, immediate, channel, key, seq, args, blob, true);

        /// <summary>
        /// Forwards a Coop message, found by its full type name, into the queue on channel "link.event" with
        /// this <paramref name="key"/> and the message's source as Sender. The key must start with a prefix the
        /// mod registered. False when the type is not loaded yet (call again later) or is not a Coop message.
        /// Calling it again is safe: a type not found and a failed subscribe are logged once per name, a type is
        /// subscribed once and a key added once.
        /// </summary>
        public static bool TrySubscribeByName(string fullTypeName, string key)
        {
            try
            {
                if (!LinkRules.IsModChannel(key) || string.IsNullOrEmpty(fullTypeName))
                {
                    Write("TrySubscribeByName refused: the type name must not be empty and the key must not be empty or start with \"link.\".");
                    return false;
                }
                if (!_started)
                {
                    Write("TrySubscribeByName(" + fullTypeName + "): call Start first.");
                    return false;
                }
                return SubscribeByNameCore(fullTypeName, key);
            }
            catch (Exception e)
            {
                // Called again on later ticks, so a failure is said once per type name.
                if (Subscribed.FirstFailure(fullTypeName ?? ""))
                    Write("TrySubscribeByName(" + fullTypeName + ") failed: " + Describe(e) + " (said once).");
                return false;
            }
        }

        private enum Target { Server, All, Player }

        private static bool Send(Target target, object network, object? peer, bool immediate, string channel, string key, int seq, IList<string>? args, byte[]? blob, bool fromMod)
        {
            string what = target + " " + (channel ?? "null") + "/" + (key ?? "");
            try
            {
                if (fromMod && !LinkRules.IsModChannel(channel))
                {
                    Write("Send refused (" + what + "): the channel must not be empty or start with \"link.\".");
                    return false;
                }
                if (!LinkRules.CheckSize(channel, key, args, blob, out _, out string reason))
                {
                    Write("Send refused (" + what + "): " + reason + ".");
                    return false;
                }
                if (network == null)
                {
                    Write("Send refused (" + what + "): no network (no Coop session?).");
                    return false;
                }
                return SendCore(target, network, peer, immediate, channel!, key, seq, args, blob, what);
            }
            catch (Exception e)
            {
                Write("Send failed (" + what + "): " + Describe(e));
                return false;
            }
        }

        // Everything that names a Coop type sits in its own method, so a Coop member that went missing fails
        // inside the caller's try instead of when the caller itself is compiled.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool SendCore(Target target, object network, object? peer, bool immediate, string channel, string? key, int seq, IList<string>? args, byte[]? blob, string what)
        {
            if (!(network is INetwork net))
            {
                Write("Send refused (" + what + "): the network object is a " + network.GetType().FullName + ", not Coop's INetwork.");
                return false;
            }
            bool isServer = ModInformation.IsServer;
            List<string>? list = args == null || args.Count == 0 ? null : new List<string>(args);
            if (target == Target.Server)
            {
                if (isServer)
                {
                    Write("Send refused (" + what + "): ToServer is for a player's PC and this is the server.");
                    return false;
                }
                net.SendAll(new ToServer { Version = Version, Channel = channel, Key = key, Seq = seq, Args = list, Blob = blob });
                return true;
            }
            if (!isServer)
            {
                Write("Send refused (" + what + "): ToAll and ToPlayer are for the server and this is a player's PC.");
                return false;
            }
            var message = new ToClient { Version = Version, Channel = channel, Key = key, Seq = seq, Args = list, Blob = blob };
            if (target == Target.All)
            {
                net.SendAll(message);
                return true;
            }
            if (!(peer is NetPeer netPeer))
            {
                Write("Send refused (" + what + "): the peer is " + (peer == null ? "null" : "a " + peer.GetType().FullName) + ", not a LiteNetLib NetPeer.");
                return false;
            }
            if (immediate) net.SendImmediate(netPeer, message);
            else net.Send(netPeer, message);
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StartCore()
        {
            var subscriber = new LinkSubscriber();
            MessageBroker.Instance.Subscribe<ToClient>(subscriber.OnToClient);
            MessageBroker.Instance.Subscribe<ToServer>(subscriber.OnToServer);
            _subscriber = subscriber;
            FindRunSafe();
            TryFindHelloTrigger();
        }

        // Common.GameThread.RunSafe(Action, bool blocking, string context, ...caller info), found by name so a
        // change there only loses the ordering with Coop's queue.
        private static void FindRunSafe()
        {
            try
            {
                Type? gameThread = typeof(MessageBroker).Assembly.GetType("Common.GameThread", false);
                if (gameThread == null) return;
                foreach (MethodInfo method in gameThread.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (method.Name != "RunSafe" || method.IsGenericMethodDefinition || parameters.Length == 0 || parameters[0].ParameterType != typeof(Action))
                        continue;
                    var values = new object?[parameters.Length];
                    values[0] = null;
                    bool contextSet = false;
                    bool usable = true;
                    for (int i = 1; i < parameters.Length; i++)
                    {
                        Type type = parameters[i].ParameterType;
                        if (type == typeof(bool)) values[i] = false;
                        else if (type == typeof(string)) { values[i] = contextSet ? null : "Coodsz.CoopLink"; contextSet = true; }
                        else if (parameters[i].IsOptional) values[i] = parameters[i].DefaultValue;
                        else usable = false;
                    }
                    if (!usable) continue;
                    _runSafeArgs = values;
                    _runSafe = method;
                    return;
                }
            }
            catch (Exception e)
            {
                Write("Coop's GameThread.RunSafe lookup failed, Drain only: " + Describe(e));
            }
        }

        // The client says hello the moment Coop's module check answer arrives. Coop.Core can load after Start,
        // so Drain asks again every few seconds until the type is found.
        private static void TryFindHelloTrigger()
        {
            if (_helloTriggerFound || _subscriber == null) return;
            long now = Clock.ElapsedMilliseconds + 5000;
            if (now - System.Threading.Interlocked.Read(ref _lastTriggerTry) < 5000) return;
            System.Threading.Interlocked.Exchange(ref _lastTriggerTry, now);
            Type? type = FindLoadedType(HelloTriggerName);
            if (type == null) return;
            SubscribeEvent(type, null);
            _helloTriggerFound = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool SubscribeByNameCore(string fullTypeName, string key)
        {
            Type? type = FindLoadedType(fullTypeName);
            if (type == null)
            {
                // A mod asks again on later ticks until Coop.Core loads, so this is said once per name.
                if (Subscribed.FirstMiss(fullTypeName))
                    Write("TrySubscribeByName: " + fullTypeName + " is not loaded (yet); call again later (said once).");
                return false;
            }
            if (!typeof(IMessage).IsAssignableFrom(type) || type.IsGenericTypeDefinition)
            {
                Write("TrySubscribeByName: " + fullTypeName + " is not a Coop message.");
                return false;
            }
            SubscribeEvent(type, key);
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SubscribeEvent(Type type, string? key)
        {
            if (Subscribed.Claim(type))
            {
                try
                {
                    // A real instance method on the rooted subscriber, closed over this message type: the broker's
                    // weak delegate needs a live target and a method it can invoke.
                    MethodInfo onEvent = typeof(LinkSubscriber).GetMethod(nameof(LinkSubscriber.OnEvent))!.MakeGenericMethod(type);
                    Type actionType = typeof(Action<>).MakeGenericType(typeof(MessagePayload<>).MakeGenericType(type));
                    Delegate handler = Delegate.CreateDelegate(actionType, _subscriber, onEvent);
                    MethodInfo subscribe = typeof(MessageBroker).GetMethod(nameof(MessageBroker.Subscribe))!.MakeGenericMethod(type);
                    subscribe.Invoke(MessageBroker.Instance, new object[] { handler });
                }
                catch
                {
                    // Not subscribed after all, so a later call may try again.
                    Subscribed.Unclaim(type);
                    throw;
                }
            }
            // A second call with the same key changes nothing and says nothing.
            if (key != null && Subscribed.AddKey(type, key))
                Write("Forwarding " + type.FullName + " as \"" + key + "\".");
        }

        private static Type? FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                try
                {
                    Type? type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch (Exception)
                {
                    // An assembly whose dependencies are not loaded yet; asking again later can work.
                }
            }
            return null;
        }

        // Network thread. Only the direction check and the queue (plus the handshake) run here.
        internal static void ReceiveWire(bool toClient, int version, string? channel, string? key, int seq, List<string>? args, byte[]? blob, object? sender)
        {
            if (!LinkRules.Accepts(toClient, SafeIsServer())) return;
            PostToGameThread(Hub.Receive(new LinkMessage(version, channel, key, seq, args, blob, sender, toClient)));
        }

        internal static void ReceiveEvent(Type type, object? sender)
        {
            if (_helloTriggerFound && type.FullName == HelloTriggerName && !SafeIsServer())
                Hub.SayHelloNow();
            if (!Subscribed.TryGetKeys(type, out string[] keys)) return;
            long last = 0;
            foreach (string key in keys)
                last = Math.Max(last, Hub.Receive(new LinkMessage(0, LinkRules.EventChannel, key, 0, null, null, sender, false)));
            PostToGameThread(last);
        }

        // Puts "hand over everything up to this message" on Coop's game thread queue, behind the game thread
        // work Coop queued for the messages before it.
        private static void PostToGameThread(long number)
        {
            MethodInfo? runSafe = _runSafe;
            if (number == 0 || !UseCoopGameThread || runSafe == null || _runSafeArgs == null) return;
            try
            {
                var values = (object?[])_runSafeArgs.Clone();
                values[0] = (Action)(() => Hub.DrainThrough(number));
                runSafe.Invoke(null, values);
            }
            catch (Exception e)
            {
                // The message stays queued for the mod's own Drain.
                Write("Coop's game thread queue refused a hand over, Drain will pick it up: " + Describe(e));
            }
        }

        private static void SendWelcome(object? peer)
        {
            object? network = SafeNetwork();
            if (network == null || peer == null)
            {
                Write("Could not answer a hello: " + (network == null ? "no network (set Link.NetworkProvider)." : "no peer."));
                return;
            }
            Send(Target.Player, network, peer, true, LinkRules.HelloChannel, LinkRules.WelcomeKey, 0, new[] { Version.ToString(CultureInfo.InvariantCulture) }, null, false);
        }

        private static bool SendHello()
        {
            object? network = SafeNetwork();
            if (network == null) return false;
            return Send(Target.Server, network, null, false, LinkRules.HelloChannel, LinkRules.HelloKey, 0, new[] { Version.ToString(CultureInfo.InvariantCulture) }, null, false);
        }

        private static object? SafeNetwork()
        {
            try { return NetworkProvider?.Invoke(); }
            catch (Exception e)
            {
                Write("Link.NetworkProvider failed: " + Describe(e));
                return null;
            }
        }

        private static bool SafeIsServer()
        {
            try { return IsServerCore(); }
            catch (Exception) { return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsServerCore() => ModInformation.IsServer;

        private static void RaiseSession(bool started)
        {
            Action<bool>? handlers = SessionChanged;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<bool>)handler)(started); }
                catch (Exception e) { Write("A SessionChanged handler failed: " + Describe(e)); }
            }
        }

        private static void Write(string line)
        {
            Action<string>? log = Log;
            if (log == null) return;
            foreach (Delegate handler in log.GetInvocationList())
            {
                try { ((Action<string>)handler)("Coop link: " + line); }
                catch (Exception) { }
            }
        }

        private static string Describe(Exception e)
        {
            Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
            return inner.GetType().Name + ": " + inner.Message;
        }
    }

    /// <summary>The rooted object whose instance methods Coop's broker calls. Kept alive by Link.</summary>
    internal sealed class LinkSubscriber
    {
        public void OnToClient(MessagePayload<ToClient> payload)
        {
            try
            {
                ToClient? m = payload?.What;
                if (m != null) Link.ReceiveWire(true, m.Version, m.Channel, m.Key, m.Seq, m.Args, m.Blob, payload!.Who);
            }
            catch (Exception) { }
        }

        public void OnToServer(MessagePayload<ToServer> payload)
        {
            try
            {
                ToServer? m = payload?.What;
                if (m != null) Link.ReceiveWire(false, m.Version, m.Channel, m.Key, m.Seq, m.Args, m.Blob, payload!.Who);
            }
            catch (Exception) { }
        }

        public void OnEvent<T>(MessagePayload<T> payload) where T : IMessage
        {
            try
            {
                Link.ReceiveEvent(typeof(T), payload?.Who);
            }
            catch (Exception) { }
        }
    }
}
