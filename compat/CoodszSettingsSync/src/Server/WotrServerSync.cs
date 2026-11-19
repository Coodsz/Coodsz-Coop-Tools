// Coodsz War of the Ring sync, server side (DedicatedServer.CoodszTools loads this assembly; see
// Shared\WarOfTheRingSync.cs). Sends TAOM's War of the Ring state to players whose settings request set
// WantsWarOfTheRing: once when they join, then whenever it changes. Never turns on anything in TAOM.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CoodszSettingsSync.Messages;
using Common.Network;
using LiteNetLib;

namespace CoodszSettingsSync
{
    /// <summary>
    /// Every TAOM read and every send runs on the game thread from <see cref="Tick"/>; Coop's handler only
    /// queues. Sends use INetwork.SendImmediate like the settings snapshots: a joining player is still in
    /// Coop's Dropping phase, where CoopServer.Send drops every packet.
    /// </summary>
    public static class WotrServerSync
    {
        private const float PollSeconds = 2f;
        private const int MaxPlayers = 64;
        private const int MaxLoggedChanges = 5;
        private const int MaxFailures = 3;
        private static int _failures;

        private static Action<string> _log = _ => { };
        private static INetwork? _network;
        private static readonly ConcurrentQueue<Action> Pending = new ConcurrentQueue<Action>();
        private static readonly ConcurrentQueue<NetPeer> Joining = new ConcurrentQueue<NetPeer>();
        private static readonly WotrRecipients<NetPeer> Players = new WotrRecipients<NetPeer>(MaxPlayers);
        private static readonly WotrChangeGate Gate = new WotrChangeGate();
        private static TaomWotr? _game;
        private static string? _tooBigNoted;
        private static float _since = PollSeconds;
        private static int _changes;
        private static int _joinsLogged;
        private static int _sendErrors;
        private static bool _off;
        private static bool _readyNoted;
        private static volatile bool _active;

        public static bool Active => _active && !_off;

        /// <summary>Called by ServerSync.Start once the probe passed.</summary>
        public static void Start(Action<string> log)
        {
            if (_active) return;
            _log = log ?? (_ => { });
            _active = true;
        }

        // ---- from Coop's handler (any thread) ---------------------------------------------------------

        internal static void Attach(INetwork network) => Pending.Enqueue(() => _network = network);

        internal static void Detach(INetwork network) => Pending.Enqueue(() =>
        {
            if (!ReferenceEquals(_network, network)) return;
            _network = null;
            Players.Clear();
            while (Joining.TryDequeue(out _)) { }
            Gate.Reset();
        });

        internal static void Subscribe(NetPeer peer)
        {
            if (!Active || Joining.Count >= MaxPlayers) return;
            Joining.Enqueue(peer);
        }

        // ---- game thread -----------------------------------------------------------------------------

        public static void Tick(float dt)
        {
            if (!_active) return;
            while (Pending.TryDequeue(out Action? action)) action();
            if (_off)
            {
                while (Joining.TryDequeue(out _)) { }
                return;
            }

            _since += dt;
            if (Joining.IsEmpty && _since < PollSeconds) return;
            _since = 0f;

            while (Joining.TryDequeue(out NetPeer? peer)) Players.Join(peer);
            Players.Prune(p => p.ConnectionState == ConnectionState.Connected);
            if (Players.Count == 0) return;

            // Players stay waiting until TAOM's campaign is up and a send to them worked.
            INetwork? network = _network;
            TaomWotr? game = network == null ? null : Game();
            if (network == null || game == null) return;

            WotrSnapshot snapshot;
            try
            {
                snapshot = game.Capture();
                _failures = 0;
            }
            catch (Exception e)
            {
                Exception inner = e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                // A TAOM read that keeps failing turns the sync off instead of failing every poll.
                _off = ++_failures >= MaxFailures;
                _log("War of the Ring sync: reading TAOM's war state failed: " + inner.GetType().Name + ": " + inner.Message
                    + (_off ? ". The sync is off for this run." : ""));
                return;
            }
            string? tooBig = WotrWire.CheckOutgoing(snapshot);
            if (tooBig != null)
            {
                if (_tooBigNoted != tooBig) _log("War of the Ring sync: the state was not sent because " + tooBig + ".");
                _tooBigNoted = tooBig;
                return;
            }

            bool changed = Gate.Changed(snapshot);
            List<NetPeer> waiting = Players.Targets(false);
            List<NetPeer> targets = Players.Targets(changed);
            if (targets.Count == 0) return;
            NetworkWarOfTheRingSnapshot message = ToMessage(snapshot);
            int sent = 0;
            foreach (NetPeer peer in targets)
            {
                bool first = waiting.Contains(peer);
                bool worked = Send(network, peer, message);
                Players.Sent(peer, worked);
                if (!worked) continue;
                sent++;
                if (first && ++_joinsLogged <= MaxLoggedChanges * 4)
                    _log("War of the Ring sync: sent the war state (" + game.PhaseName(snapshot.Phase) + ") to player " + Count(peer.Id) + ".");
            }
            if (changed && ++_changes <= MaxLoggedChanges)
                _log("War of the Ring sync: the war state is now " + game.PhaseName(snapshot.Phase) + " on the server; sent to "
                    + Count(sent) + " player" + (sent == 1 ? "" : "s") + (_changes == MaxLoggedChanges ? ". Further changes are not logged." : "."));
        }

        private static bool Send(INetwork network, NetPeer peer, NetworkWarOfTheRingSnapshot message)
        {
            try
            {
                network.SendImmediate(peer, message);
                return true;
            }
            catch (Exception e)
            {
                if (++_sendErrors <= MaxLoggedChanges)
                    _log("War of the Ring sync: sending to player " + Count(peer.Id) + " failed: " + e.GetType().Name + ": " + e.Message
                        + (_sendErrors == MaxLoggedChanges ? ". Further send errors are not logged." : ""));
                return false;
            }
        }

        /// <summary>TAOM's objects in the current campaign, bound again when a campaign loads; null until then.</summary>
        private static TaomWotr? Game()
        {
            object? campaign = TaomWotr.CurrentCampaign();
            if (_game != null && ReferenceEquals(_game.Campaign, campaign)) return _game;
            _game = null;
            Gate.Reset();
            WotrBindStatus status;
            TaomWotr? game;
            string missing;
            try { status = TaomWotr.BindLive(out game, out missing); }
            catch (Exception e)
            {
                status = WotrBindStatus.Missing;
                game = null;
                missing = "a working campaign behavior (" + e.GetType().Name + ": " + e.Message + ")";
            }
            switch (status)
            {
                case WotrBindStatus.Ready:
                    _game = game;
                    if (!_readyNoted) _log("War of the Ring sync is on: players with the Coodsz client add on get TAOM's war state live.");
                    _readyNoted = true;
                    return _game;
                case WotrBindStatus.TaomNotLoaded:
                    _off = true;
                    _log("War of the Ring sync is off: TAOM is not loaded on this server.");
                    return null;
                case WotrBindStatus.Missing:
                    _off = true;
                    _log("War of the Ring sync is off: TAOM may have changed, " + missing + " was not found.");
                    return null;
                default:
                    return null;
            }
        }

        private static NetworkWarOfTheRingSnapshot ToMessage(WotrSnapshot snapshot)
        {
            snapshot.ToLists(out List<string> keys, out List<string> values);
            return new NetworkWarOfTheRingSnapshot
            {
                ProtocolVersion = WotrWire.ProtocolVersion,
                Phase = snapshot.Phase,
                Outcome = snapshot.Outcome,
                Keys = keys,
                Values = values,
            };
        }

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

namespace Coop.Core.Server.Services.CoodszSettingsSync
{
    using global::Common.Messaging;
    using global::CoodszSettingsSync.Messages;
    using global::LiteNetLib;

    // Coop builds every IHandler under Coop.Core.Server.* when a server session starts. A player asks for
    // the war state inside the settings request, so only a new enough add on is ever sent this message.
    public sealed class CoodszWarOfTheRingServerHandler : IHandler
    {
        private readonly IMessageBroker _broker;
        private readonly global::Common.Network.INetwork _network;

        public CoodszWarOfTheRingServerHandler(IMessageBroker broker, global::Common.Network.INetwork network)
        {
            _broker = broker;
            _network = network;
            global::CoodszSettingsSync.WotrServerSync.Attach(network);
            broker.Subscribe<NetworkModSettingsRequest>(HandleRequest);
        }

        private void HandleRequest(MessagePayload<NetworkModSettingsRequest> payload)
        {
            if (!global::CoodszSettingsSync.WotrServerSync.Active) return;
            if (!global::Common.ModInformation.IsServer || !(payload.Who is NetPeer peer)) return;
            if (payload.What == null || !payload.What.WantsWarOfTheRing
                || !global::CoodszSettingsSync.SettingsWire.IsKnownProtocol(payload.What.ProtocolVersion)) return;
            global::CoodszSettingsSync.WotrServerSync.Subscribe(peer);
        }

        public void Dispose()
        {
            _broker.Unsubscribe<NetworkModSettingsRequest>(HandleRequest);
            global::CoodszSettingsSync.WotrServerSync.Detach(_network);
        }
    }
}
