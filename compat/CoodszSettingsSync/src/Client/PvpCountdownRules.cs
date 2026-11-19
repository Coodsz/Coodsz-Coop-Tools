// Coodsz Settings Sync, client side: the PvP countdown's text and clock, without the game or Coop, so the
// launcher's tests compile this file as is. PvpCountdownClient.cs shows it in the game.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CoodszSettingsSync
{
    /// <summary>
    /// The countdown's two lines in the tools' languages. The game's language picks one; any other language gets
    /// English. The client add on can't use the tools' Translator, so the lines live here.
    /// </summary>
    public static class PvpCountdownText
    {
        // {0} is the whole seconds left.
        private static readonly Dictionary<string, (string Caught, string Attacker)> Lines =
            new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["en"] = ("Answer in {0} s", "They have {0} s to answer"),
                ["ar"] = ("أجب خلال {0} ث", "لدى اللاعب الآخر {0} ث للرد"),
                ["de"] = ("Antworte in {0} s", "Der andere Spieler hat {0} s zum Antworten"),
                ["fr"] = ("Répondez dans {0} s", "L'autre joueur a {0} s pour répondre"),
                ["pt"] = ("Responda em {0} s", "O outro jogador tem {0} s para responder"),
                ["ru"] = ("Ответьте за {0} с", "У другого игрока {0} с на ответ"),
                ["tr"] = ("{0} sn içinde cevap verin", "Diğer oyuncunun cevap vermek için {0} sn süresi var"),
            };

        public static IEnumerable<string> Languages => Lines.Keys;

        /// <summary>
        /// The tools' language code for the game's language id (MBTextManager.ActiveTextLanguage, the id in each
        /// language_data.xml): Deutsch, Français, Português (BR), Русский, Türkçe, and an Arabic translation mod's id.
        /// Anything else is English.
        /// </summary>
        public static string LanguageFor(string? gameLanguage)
        {
            string id = (gameLanguage ?? "").Trim();
            if (id.Length == 0) return "en";
            if (id.StartsWith("Deutsch", StringComparison.OrdinalIgnoreCase) || id.Equals("German", StringComparison.OrdinalIgnoreCase)) return "de";
            if (id.StartsWith("Français", StringComparison.OrdinalIgnoreCase) || id.Equals("French", StringComparison.OrdinalIgnoreCase)) return "fr";
            if (id.StartsWith("Português", StringComparison.OrdinalIgnoreCase) || id.StartsWith("Portuguese", StringComparison.OrdinalIgnoreCase)) return "pt";
            if (id.StartsWith("Русский", StringComparison.OrdinalIgnoreCase) || id.Equals("Russian", StringComparison.OrdinalIgnoreCase)) return "ru";
            if (id.StartsWith("Türkçe", StringComparison.OrdinalIgnoreCase) || id.Equals("Turkish", StringComparison.OrdinalIgnoreCase)) return "tr";
            // No Arabic ships with the game; translation mods name it in Arabic or English.
            if (id.IndexOf("عرب", StringComparison.Ordinal) >= 0 || id.IndexOf("Arab", StringComparison.OrdinalIgnoreCase) >= 0) return "ar";
            return "en";
        }

        /// <summary>The line for a role (PvpCountdownWire.CaughtRole or AttackerRole) in a tools' language code.</summary>
        public static string For(string language, int role, int seconds)
        {
            if (!Lines.TryGetValue(language ?? "", out var lines)) lines = Lines["en"];
            string format = role == PvpCountdownWire.AttackerRole ? lines.Attacker : lines.Caught;
            return string.Format(CultureInfo.InvariantCulture, format, Math.Max(0, seconds));
        }
    }

    /// <summary>
    /// The one countdown this player sees. A start message sets it; it counts down by itself from the game's tick
    /// and goes away at 0, on a stop for its session, when the session ends, or when Coop's meeting menu has been
    /// closed for a moment (the start can come just before Coop's own menu, so a short wait comes first).
    /// </summary>
    public sealed class PvpCountdownClock
    {
        /// <summary>How long Coop's menu may show another session or none before the countdown goes.</summary>
        public const float MenuGoneSeconds = 2f;

        private string? _session;
        private int _role;
        private double _left;
        private float _menuGone;

        public bool Showing => _session != null;
        public string? SessionId => _session;
        public int Role => _role;

        /// <summary>Whole seconds shown, rounded up.</summary>
        public int Seconds => _session == null ? 0 : (int)Math.Ceiling(Math.Max(0, _left));

        /// <summary>A start message; a newer one replaces the countdown. False when the message is not valid.</summary>
        public bool Start(int version, string? sessionId, int secondsLeft, int role)
        {
            if (!PvpCountdownWire.IsValidStart(version, sessionId, secondsLeft, role)) return false;
            _session = sessionId;
            _role = role;
            _left = secondsLeft;
            _menuGone = 0f;
            return true;
        }

        /// <summary>A stop message: only the countdown of that session goes away.</summary>
        public bool Stop(int version, string? sessionId)
        {
            if (!PvpCountdownWire.IsValidStop(version, sessionId) || !string.Equals(sessionId, _session, StringComparison.Ordinal)) return false;
            Clear();
            return true;
        }

        private object? _owner;

        /// <summary>A Coop session started with this handler as its owner; any countdown from before goes.</summary>
        public void SessionStarted(object owner)
        {
            _owner = owner;
            Clear();
        }

        /// <summary>
        /// The owner's session ended. A late end from an earlier handler is ignored, so it can't clear the new session's
        /// countdown. False when it was not the owner.
        /// </summary>
        public bool SessionEnded(object owner)
        {
            if (!ReferenceEquals(_owner, owner)) return false;
            _owner = null;
            Clear();
            return true;
        }

        public void Clear()
        {
            _session = null;
            _role = 0;
            _left = 0;
            _menuGone = 0f;
        }

        /// <summary>
        /// One game tick. <paramref name="menuSession"/> is the session Coop's meeting menu shows now (null when none);
        /// <paramref name="menuKnown"/> is false when Coop's menu state could not be read, so only the clock and the
        /// stop take the countdown down.
        /// </summary>
        public void Tick(float dt, string? menuSession, bool menuKnown)
        {
            if (_session == null) return;
            _left -= Math.Max(0f, dt);
            if (_left <= 0)
            {
                Clear();
                return;
            }
            if (!menuKnown || string.Equals(menuSession, _session, StringComparison.Ordinal))
            {
                _menuGone = 0f;
                return;
            }
            _menuGone += Math.Max(0f, dt);
            if (_menuGone >= MenuGoneSeconds) Clear();
        }

        /// <summary>The line to show now, or null when nothing shows.</summary>
        public string? Text(string language) => _session == null ? null : PvpCountdownText.For(language, _role, Seconds);
    }
}
