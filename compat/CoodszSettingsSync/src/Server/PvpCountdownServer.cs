// Coodsz Settings Sync, server side: sends the PvP response timer's countdown to players whose client add on
// shows it. DedicatedServer.CoodszTools decides what to send (PvpRules.cs) and calls Countdown and Stop here
// through the loader, so a server without this assembly still runs the timer.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using CoodszSettingsSync.Messages;
using Common.Network;
using LiteNetLib;

namespace CoodszSettingsSync
{
    /// <summary>
    /// Only a player whose settings request set WantsPvpCountdown is ever sent these messages, so a game without
    /// the add on, or with an older one, never gets a message type it can't read. Sends use INetwork.SendImmediate
    /// like the rest of the sync. Countdown and Stop run on the game thread (the server tools' tick); Coop's handler
    /// only adds players and the network.
    /// </summary>
    public static class PvpCountdownServer
    {
        private const int MaxPlayers = 256;

        private static readonly PvpCountdownRecipients<NetPeer> Players =
            new PvpCountdownRecipients<NetPeer>(p => p.ConnectionState == ConnectionState.Connected, MaxPlayers);
        private static volatile INetwork? _network;

        // ---- from Coop's handler (any thread) ---------------------------------------------------------

        internal static void Attach(INetwork network) => _network = network;

        internal static void Detach(INetwork network)
        {
            if (!ReferenceEquals(_network, network)) return;
            _network = null;
            Players.Clear();
        }

        internal static void Subscribe(NetPeer peer) => Players.Add(peer);

        /// <summary>Coop's PlayerDisconnected: the player leaves the list, so whoever gets this peer next starts without it.</summary>
        internal static void Disconnected(NetPeer peer) => Players.Remove(peer);

        /// <summary>True when this player's add on asked for the countdown and is still connected.</summary>
        public static bool Wants(object? peer) => peer is NetPeer netPeer && Players.Wants(netPeer);

        // ---- game thread (DedicatedServer.CoodszTools' PvP tick) -----------------------------------------

        /// <summary>
        /// Starts the countdown for one player: <paramref name="role"/> 1 for the caught player, 2 for the one who
        /// caught them. True when it went out; false for a player without the add on, or when it could not be sent.
        /// </summary>
        public static bool Countdown(object? peer, string sessionId, int secondsLeft, int role)
        {
            INetwork? network = _network;
            if (network == null || !Wants(peer) || !PvpCountdownWire.IsValidStart(PvpCountdownWire.ProtocolVersion, sessionId, secondsLeft, role)) return false;
            network.SendImmediate((NetPeer)peer!, new NetworkPvpCountdown
            {
                ProtocolVersion = PvpCountdownWire.ProtocolVersion,
                SessionId = sessionId,
                SecondsLeft = secondsLeft,
                Role = role,
            });
            return true;
        }

        /// <summary>Takes one player's countdown for this session down. False for a player without the add on.</summary>
        public static bool Stop(object? peer, string sessionId)
        {
            INetwork? network = _network;
            if (network == null || !Wants(peer) || !PvpCountdownWire.IsValidSessionId(sessionId)) return false;
            network.SendImmediate((NetPeer)peer!, new NetworkPvpCountdownStop
            {
                ProtocolVersion = PvpCountdownWire.ProtocolVersion,
                SessionId = sessionId,
            });
            return true;
        }
    }
}

namespace CoodszSettingsSync
{
    using global::Common.Messaging;

    /// <summary>
    /// Drops a player from the countdown list on Coop's PlayerDisconnected (CoopServer.OnPeerDisconnected publishes it
    /// for every player that leaves). The type is found by name and subscribed by reflection, so a Coop update that
    /// renames it only loses this drop, with one log line; the countdown and the settings sync stay on, and
    /// PvpCountdownServer.Wants still drops a player it finds not connected.
    /// </summary>
    internal sealed class PvpCountdownDisconnects
    {
        public const string TypeName = "Common.Network.Messages.PlayerDisconnected";

        private static bool _missingNoted;
        private static bool _subscribedNoted;
        private static readonly HashSet<string> FailuresNoted = new HashSet<string>(StringComparer.Ordinal);

        private readonly PropertyInfo _player;
        private MethodInfo? _unsubscribe;
        private Delegate? _handler;

        private PvpCountdownDisconnects(PropertyInfo player) => _player = player;

        /// <summary>Subscribes to PlayerDisconnected on this broker; null (one log line) when Coop has no such message.</summary>
        public static PvpCountdownDisconnects? Subscribe(IMessageBroker broker)
        {
            try
            {
                Type? type = CoopProbe.FindLoaded("Common", TypeName);
                PropertyInfo? player = type?.GetProperty("PlayerId", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo? subscribe = typeof(IMessageBroker).GetMethod(nameof(IMessageBroker.Subscribe));
                MethodInfo? unsubscribe = typeof(IMessageBroker).GetMethod(nameof(IMessageBroker.Unsubscribe));
                if (type == null || player == null || subscribe == null || unsubscribe == null)
                {
                    NoteMissing("it was not found");
                    return null;
                }
                var disconnects = new PvpCountdownDisconnects(player);
                Type action = typeof(Action<>).MakeGenericType(typeof(MessagePayload<>).MakeGenericType(type));
                MethodInfo handle = typeof(PvpCountdownDisconnects).GetMethod(nameof(Handle), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(type);
                disconnects._handler = Delegate.CreateDelegate(action, disconnects, handle);
                subscribe.MakeGenericMethod(type).Invoke(broker, new object[] { disconnects._handler });
                disconnects._unsubscribe = unsubscribe.MakeGenericMethod(type);
                if (!_subscribedNoted)
                {
                    _subscribedNoted = true;
                    ServerSync.Log("PvP countdown: subscribed to Coop's " + TypeName + ", so a player who leaves is dropped from the countdown right away.");
                }
                return disconnects;
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                NoteMissing(inner.GetType().Name + ": " + inner.Message);
                return null;
            }
        }

        public void Unsubscribe(IMessageBroker broker)
        {
            try { _unsubscribe?.Invoke(broker, new object?[] { _handler }); }
            catch (Exception e) { NoteFailure("unsubscribing from", e); }
        }

        private void Handle<T>(MessagePayload<T> payload) where T : IMessage
        {
            try
            {
                object? what = payload == null ? null : (object?)payload.What;
                if (what != null && _player.GetValue(what, null) is LiteNetLib.NetPeer peer) PvpCountdownServer.Disconnected(peer);
            }
            catch (Exception e) { NoteFailure("handling", e); }
        }

        // One line per kind of failure, so a broken drop shows in the log without filling it.
        private static void NoteFailure(string what, Exception e)
        {
            Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
            lock (FailuresNoted)
                if (!FailuresNoted.Add(what)) return;
            ServerSync.Log("PvP countdown: " + what + " Coop's " + TypeName + " failed: " + inner.GetType().Name + ": " + inner.Message);
        }

        private static void NoteMissing(string why)
        {
            if (_missingNoted) return;
            _missingNoted = true;
            ServerSync.Log("PvP countdown: Coop's " + TypeName + " can't be used (" + why
                + "), so a player who leaves is dropped from the countdown only when the server next finds them not connected.");
        }
    }
}

namespace Coop.Core.Server.Services.CoodszSettingsSync
{
    using global::Common.Messaging;
    using global::CoodszSettingsSync.Messages;
    using global::LiteNetLib;

    // Coop builds every IHandler under Coop.Core.Server.* when a server session starts. A player asks for the
    // countdown inside the settings request, so only a new enough add on is ever sent its messages.
    public sealed class CoodszPvpCountdownServerHandler : IHandler
    {
        private readonly IMessageBroker _broker;
        private readonly global::Common.Network.INetwork _network;

        public CoodszPvpCountdownServerHandler(IMessageBroker broker, global::Common.Network.INetwork network)
        {
            _broker = broker;
            _network = network;
            global::CoodszSettingsSync.PvpCountdownServer.Attach(network);
            broker.Subscribe<NetworkModSettingsRequest>(HandleRequest);
            _disconnects = global::CoodszSettingsSync.PvpCountdownDisconnects.Subscribe(broker);
        }

        private readonly global::CoodszSettingsSync.PvpCountdownDisconnects? _disconnects;

        private void HandleRequest(MessagePayload<NetworkModSettingsRequest> payload)
        {
            if (!global::CoodszSettingsSync.ServerSync.Active) return;
            if (!global::Common.ModInformation.IsServer || !(payload.Who is NetPeer peer)) return;
            if (payload.What == null || !payload.What.WantsPvpCountdown
                || !global::CoodszSettingsSync.SettingsWire.IsKnownProtocol(payload.What.ProtocolVersion)) return;
            global::CoodszSettingsSync.PvpCountdownServer.Subscribe(peer);
        }

        public void Dispose()
        {
            _broker.Unsubscribe<NetworkModSettingsRequest>(HandleRequest);
            _disconnects?.Unsubscribe(_broker);
            global::CoodszSettingsSync.PvpCountdownServer.Detach(_network);
        }
    }
}
