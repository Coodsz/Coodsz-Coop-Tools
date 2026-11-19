// Coodsz War of the Ring sync, client side (DedicatedServer.CoodszJoinPassword loads this assembly; see
// Shared\WarOfTheRingSync.cs). Puts the server's TAOM War of the Ring state into this game's TAOM once the
// campaign is ready, and again whenever the server sends a change.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Globalization;
using CoodszSettingsSync.Messages;

namespace CoodszSettingsSync
{
    /// <summary>
    /// The handler only queues; all TAOM work runs on the game thread from <see cref="Tick"/>. The latest
    /// snapshot is checked against TAOM every few seconds as well, so a load that put the join save's older
    /// copy back is corrected; the check costs a few reads and sets nothing when TAOM already matches.
    /// </summary>
    public static class WotrClientSync
    {
        private const float RecheckSeconds = 5f;
        private const int MaxLoggedUpdates = 3;

        private static Action<string> _log = _ => { };
        private static readonly ConcurrentQueue<Action> Work = new ConcurrentQueue<Action>();
        private static readonly WotrMirror Mirror = new WotrMirror();
        private static WotrSnapshot? _latest;
        private static TaomWotr? _game;
        private static bool _campaignReady;
        private static bool _dirty;
        private static bool _off;
        private static bool _receivedNoted;
        private static bool _meterNoted;
        private static int _updates;
        private static float _since;
        private static object? _owner;
        private static bool _meterErrorNoted;
        private static volatile bool _active;

        /// <summary>Called by ClientSync.Start once the probe passed.</summary>
        public static void Start(Action<string> log)
        {
            if (_active) return;
            _log = log ?? (_ => { });
            _active = true;
        }

        // ---- from Coop's handler (any thread) ---------------------------------------------------------

        // Each session belongs to its handler, so a late Dispose of the previous handler cannot wipe the new session.
        internal static void SessionStarted(object owner) => Work.Enqueue(() =>
        {
            _owner = owner;
            Clear();
        });

        internal static void SessionEnded(object owner) => Work.Enqueue(() =>
        {
            if (!ReferenceEquals(_owner, owner)) return;
            _owner = null;
            Clear();
        });

        internal static void CampaignReady() => Work.Enqueue(() =>
        {
            _campaignReady = true;
            _dirty = true;
        });

        internal static void Receive(NetworkWarOfTheRingSnapshot message)
        {
            if (!_active || message == null) return;
            WotrSnapshot? snapshot = WotrWire.FromParts(message.ProtocolVersion, message.Phase, message.Outcome, message.Keys, message.Values, out string? reason);
            if (snapshot == null)
            {
                _log("War of the Ring sync: the server's war state was not used because " + reason + ".");
                return;
            }
            Work.Enqueue(() =>
            {
                if (!_receivedNoted) _log("War of the Ring sync: got the server's war state (phase " + snapshot.Phase.ToString(CultureInfo.InvariantCulture)
                    + ", " + snapshot.Momentum.Count.ToString(CultureInfo.InvariantCulture) + " momentum entries)"
                    + (_campaignReady ? "." : "; it goes in once the campaign has loaded."));
                _receivedNoted = true;
                _latest = snapshot;
                _dirty = true;
            });
        }

        // ---- game thread -----------------------------------------------------------------------------

        public static void Tick(float dt)
        {
            if (!_active) return;
            while (Work.TryDequeue(out Action? work)) work();
            if (_off || !_campaignReady || _latest == null) return;
            _since += dt;
            if (!_dirty && _since < RecheckSeconds) return;
            _since = 0f;
            _dirty = false;

            try
            {
                TaomWotr? game = Game();
                if (game == null) return;
                WotrApplyResult result = Mirror.Apply(game, _latest);
                _failures = 0;
                Note(game, _latest, result);
            }
            catch (Exception e)
            {
                Exception inner = e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                // A TAOM call that keeps failing turns the sync off instead of failing every few seconds.
                _off = ++_failures >= MaxFailures;
                _log("War of the Ring sync: applying the server's war state failed: " + inner.GetType().Name + ": " + inner.Message
                    + (_off ? ". The sync is off for this game." : ""));
            }
        }

        private const int MaxFailures = 3;
        private static int _failures;

        private static void Note(TaomWotr game, WotrSnapshot snapshot, WotrApplyResult result)
        {
            if (result.UnknownPhase != null)
                _log("War of the Ring sync: this TAOM has no phase " + result.UnknownPhase.Value.ToString(CultureInfo.InvariantCulture)
                    + "; the server may run another TAOM version.");
            if (result.UnknownOutcome != null)
                _log("War of the Ring sync: this TAOM has no outcome " + result.UnknownOutcome.Value.ToString(CultureInfo.InvariantCulture)
                    + "; the server may run another TAOM version.");
            if (result.MeterError != null && !_meterErrorNoted)
            {
                _meterErrorNoted = true;
                _log("War of the Ring sync: TAOM's map meter could not be refreshed yet (" + result.MeterError + "); it is tried again in a few seconds.");
            }
            if (!result.Any) return;
            if (result.PhaseChanged || result.OutcomeChanged)
                _log("War of the Ring sync: the war is now " + game.PhaseName(snapshot.Phase) + " like on the server.");
            else if (++_updates <= MaxLoggedUpdates)
                _log("War of the Ring sync: momentum updated from the server" + (_updates == MaxLoggedUpdates ? ". Further updates are not logged." : "."));
            if (!result.MeterRefreshed && result.MeterError == null && !_meterNoted)
            {
                _meterNoted = true;
                _log("War of the Ring sync: TAOM's map meter could not be refreshed here; it shows on TAOM's next daily tick.");
            }
        }

        private static TaomWotr? Game()
        {
            object? campaign = TaomWotr.CurrentCampaign();
            if (_game != null && ReferenceEquals(_game.Campaign, campaign)) return _game;
            _game = null;
            Mirror.Reset();
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
                    return _game = game;
                case WotrBindStatus.TaomNotLoaded:
                    _off = true;
                    _log("War of the Ring sync is off: TAOM is not loaded in this game.");
                    return null;
                case WotrBindStatus.Missing:
                    _off = true;
                    _log("War of the Ring sync is off: TAOM may have changed, " + missing + " was not found.");
                    return null;
                default:
                    return null;
            }
        }

        private static void Clear()
        {
            _latest = null;
            _game = null;
            _campaignReady = false;
            _dirty = false;
            _receivedNoted = false;
            _updates = 0;
            _since = 0f;
            Mirror.Reset();
            _off = false;
            _failures = 0;
        }
    }
}

namespace Coop.Core.Client.Services.CoodszSettingsSync
{
    using global::Common.Messaging;
    using global::CoodszSettingsSync.Messages;
    using global::GameInterface.Services.GameState.Messages;

    // Coop builds every IHandler under Coop.Core.Client.* when the client starts a session.
    public sealed class CoodszWarOfTheRingClientHandler : IHandler
    {
        private readonly IMessageBroker _broker;

        public CoodszWarOfTheRingClientHandler(IMessageBroker broker)
        {
            _broker = broker;
            global::CoodszSettingsSync.WotrClientSync.SessionStarted(this);
            broker.Subscribe<NetworkWarOfTheRingSnapshot>(HandleSnapshot);
            broker.Subscribe<CampaignReady>(HandleCampaignReady);
        }

        private void HandleSnapshot(MessagePayload<NetworkWarOfTheRingSnapshot> payload)
        {
            // The war state only ever flows from the server to players.
            if (global::Common.ModInformation.IsServer || payload.What == null) return;
            global::CoodszSettingsSync.WotrClientSync.Receive(payload.What);
        }

        private void HandleCampaignReady(MessagePayload<CampaignReady> payload) => global::CoodszSettingsSync.WotrClientSync.CampaignReady();

        public void Dispose()
        {
            _broker.Unsubscribe<NetworkWarOfTheRingSnapshot>(HandleSnapshot);
            _broker.Unsubscribe<CampaignReady>(HandleCampaignReady);
            global::CoodszSettingsSync.WotrClientSync.SessionEnded(this);
        }
    }
}
