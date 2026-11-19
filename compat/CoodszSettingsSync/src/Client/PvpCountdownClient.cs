// Coodsz Settings Sync, client side: shows the PvP response timer's countdown that the Coodsz Server Tools
// send (DedicatedServer.CoodszJoinPassword loads this assembly). The text and clock are in PvpCountdownRules.cs.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Reflection;
using CoodszSettingsSync.Messages;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace CoodszSettingsSync
{
    /// <summary>
    /// The handler only queues; the clock and the screen run on the game thread from <see cref="Tick"/>
    /// (ClientSync.Tick). The countdown is a global layer like Coop's own trade overlay, so it shows over the
    /// map, Coop's meeting conversation and its waiting overlay alike.
    /// </summary>
    public static class PvpCountdownClient
    {
        private const string DialogStateType = "GameInterface.Services.MapEvents.PlayerPartyInteractions.PlayerPartyInteractionDialogState";

        private static Action<string> _log = _ => { };
        private static readonly ConcurrentQueue<Action> Work = new ConcurrentQueue<Action>();
        private static readonly PvpCountdownClock Clock = new PvpCountdownClock();
        private static PvpCountdownOverlay? _overlay;
        private static PropertyInfo? _menuSession;
        private static bool _menuLooked;
        private static bool _shownNoted;
        private static bool _broken;

        internal static void Start(Action<string> log) => _log = log ?? (_ => { });

        // ---- from Coop's handler (any thread) ---------------------------------------------------------

        internal static void Receive(NetworkPvpCountdown message) => Work.Enqueue(() =>
        {
            if (!Clock.Start(message.ProtocolVersion, message.SessionId, message.SecondsLeft, message.Role))
                _log("PvP countdown: a countdown from the server was not valid, so it is not shown.");
        });

        internal static void Receive(NetworkPvpCountdownStop message) => Work.Enqueue(() => Clock.Stop(message.ProtocolVersion, message.SessionId));

        // Each session belongs to its handler, so a late Dispose of the previous handler cannot clear the new session's countdown.
        internal static void SessionStarted(object owner) => Work.Enqueue(() => Clock.SessionStarted(owner));

        internal static void SessionEnded(object owner) => Work.Enqueue(() => Clock.SessionEnded(owner));

        // ---- game thread -----------------------------------------------------------------------------

        public static void Tick(float dt)
        {
            while (Work.TryDequeue(out Action? work)) work();
            if (!Clock.Showing && _overlay == null) return;
            bool known = MenuSession(out string? menu);
            Clock.Tick(dt, menu, known);
            string? text = Clock.Text(PvpCountdownText.LanguageFor(GameLanguage()));
            if (text == null)
            {
                Hide();
                return;
            }
            Show(Localized(text));
        }

        private static void Show(string text)
        {
            if (_broken) return;
            try
            {
                if (_overlay == null)
                {
                    _overlay = new PvpCountdownOverlay();
                    if (!_shownNoted) _log("PvP countdown: shown for the host's response timer.");
                    _shownNoted = true;
                }
                _overlay.SetText(text);
            }
            catch (Exception e)
            {
                // The screen failed (a missing prefab, a game change): the timer still runs on the server.
                _broken = true;
                _log("PvP countdown: can't be shown here: " + e.GetType().Name + ": " + e.Message);
                Hide();
            }
        }

        private static void Hide()
        {
            PvpCountdownOverlay? overlay = _overlay;
            _overlay = null;
            try { overlay?.Close(); }
            catch (Exception e) { _log("PvP countdown: closing failed: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Coop's meeting menu session (PlayerPartyInteractionDialogState.SessionId); false when it can't be read.</summary>
        private static bool MenuSession(out string? session)
        {
            session = null;
            try
            {
                if (!_menuLooked)
                {
                    _menuLooked = true;
                    _menuSession = CoopProbe.FindLoaded("GameInterface", DialogStateType)?.GetProperty("SessionId", BindingFlags.Public | BindingFlags.Static);
                    if (_menuSession == null) _log("PvP countdown: Coop's meeting menu state was not found, so the countdown goes only at 0 or when the server stops it.");
                }
                if (_menuSession == null) return false;
                session = _menuSession.GetValue(null, null) as string;
                return true;
            }
            catch (Exception) { return false; }
        }

        // Also the held note's language (ClientSync.NoteHeld).
        internal static string GameLanguage()
        {
            try { return MBTextManager.ActiveTextLanguage ?? ""; }
            catch (Exception) { return ""; }
        }

        // The game's own text processing for its language (letter forms, for one), as any other game text gets.
        private static string Localized(string text)
        {
            try { return new TextObject(text).ToString() ?? text; }
            catch (Exception) { return text; }
        }
    }

    /// <summary>A small gold line at the bottom center of the screen (GUI\Prefabs\CoodszPvpCountdown.xml in the module).</summary>
    internal sealed class PvpCountdownOverlay : GlobalLayer
    {
        public const string MovieName = "CoodszPvpCountdown";

        private readonly PvpCountdownViewModel _data = new PvpCountdownViewModel();

        private readonly GauntletLayer _layer;
        private readonly GauntletMovieIdentifier _movie;

        public PvpCountdownOverlay()
        {
            _layer = new GauntletLayer(MovieName, 1001, false);
            _movie = _layer.LoadMovie(MovieName, _data);
            Layer = _layer;
            ScreenManager.AddGlobalLayer(this, false);
        }

        public void SetText(string text) => _data.Text = text;

        // Off the screen first; then the movie and the view model are let go, each on its own so one failing
        // can't keep the line on the screen.
        public void Close()
        {
            ScreenManager.RemoveGlobalLayer(this);
            try { if (_movie != null) _layer.ReleaseMovie(_movie); }
            catch (Exception) { }
            try { _data.OnFinalize(); }
            catch (Exception) { }
        }
    }

    internal sealed class PvpCountdownViewModel : ViewModel
    {
        private string _text = "";

        [DataSourceProperty]
        public string Text
        {
            get => _text;
            set
            {
                if (value == _text) return;
                _text = value;
                OnPropertyChangedWithValue(value, nameof(Text));
            }
        }
    }
}

namespace Coop.Core.Client.Services.CoodszSettingsSync
{
    using global::Common.Messaging;
    using global::CoodszSettingsSync.Messages;

    // Coop builds every IHandler under Coop.Core.Client.* when the client starts a session. The server only
    // sends these to a player whose settings request set WantsPvpCountdown.
    public sealed class CoodszPvpCountdownClientHandler : IHandler
    {
        private readonly IMessageBroker _broker;

        public CoodszPvpCountdownClientHandler(IMessageBroker broker)
        {
            _broker = broker;
            global::CoodszSettingsSync.PvpCountdownClient.SessionStarted(this);
            broker.Subscribe<NetworkPvpCountdown>(HandleStart);
            broker.Subscribe<NetworkPvpCountdownStop>(HandleStop);
        }

        private void HandleStart(MessagePayload<NetworkPvpCountdown> payload)
        {
            // The countdown only ever flows from the server to players.
            if (global::Common.ModInformation.IsServer || payload.What == null) return;
            global::CoodszSettingsSync.PvpCountdownClient.Receive(payload.What);
        }

        private void HandleStop(MessagePayload<NetworkPvpCountdownStop> payload)
        {
            if (global::Common.ModInformation.IsServer || payload.What == null) return;
            global::CoodszSettingsSync.PvpCountdownClient.Receive(payload.What);
        }

        public void Dispose()
        {
            _broker.Unsubscribe<NetworkPvpCountdown>(HandleStart);
            _broker.Unsubscribe<NetworkPvpCountdownStop>(HandleStop);
            global::CoodszSettingsSync.PvpCountdownClient.SessionEnded(this);
        }
    }
}
