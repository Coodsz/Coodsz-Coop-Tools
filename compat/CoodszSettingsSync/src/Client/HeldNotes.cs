// Coodsz Settings Sync, client side: the line a player sees when the host's value of a synced mod is put back over a
// change of theirs, once per mod per session, in the game's language. Without the game or Coop, so the launcher's
// tests compile this file as is. ClientSync.cs shows it in the game.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CoodszSettingsSync
{
    /// <summary>Which held changes were already told this session: one log line, and one line on screen per mod.</summary>
    public sealed class HeldNotes
    {
        // {0} is the mod's name. The game's language picks one (PvpCountdownText.LanguageFor); any other gets English.
        private static readonly Dictionary<string, string> Lines = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["en"] = "The host sets this ({0}). Your own value comes back when you leave the server.",
            ["ar"] = "المضيف يحدد هذا ({0}). تعود قيمتك الخاصة عندما تغادر الخادم.",
            ["de"] = "Der Host legt das fest ({0}). Dein eigener Wert kommt zurück, wenn du den Server verlässt.",
            ["fr"] = "L'hôte règle ceci ({0}). Votre propre valeur revient quand vous quittez le serveur.",
            ["pt"] = "O host define isto ({0}). Seu próprio valor volta quando você sair do servidor.",
            ["ru"] = "Это задаёт хост ({0}). Ваше значение вернётся, когда вы покинете сервер.",
            ["tr"] = "Bunu sunucu sahibi belirliyor ({0}). Sunucudan ayrıldığında kendi değerin geri gelir.",
        };

        private readonly object _gate = new object();
        private readonly HashSet<string> _shown = new HashSet<string>(StringComparer.Ordinal);
        private bool _logged;

        public static IEnumerable<string> Languages => Lines.Keys;

        /// <summary>A new Coop session: every mod may be told again.</summary>
        public void NewSession()
        {
            lock (_gate)
            {
                _shown.Clear();
                _logged = false;
            }
        }

        /// <summary>
        /// A held change of the settings id. True the first time this session (the log line);
        /// <paramref name="show"/> is true the first time for this id (the line on screen).
        /// </summary>
        public bool Held(string? settingsId, out bool show)
        {
            lock (_gate)
            {
                show = _shown.Add(settingsId ?? "");
                bool first = !_logged;
                _logged = true;
                return first;
            }
        }

        /// <summary>The mod's name as MCM shows it, without a leading localization tag ({=id}Name), else the settings id.</summary>
        public static string Name(string? displayName, string? settingsId)
        {
            string name = (displayName ?? "").Trim();
            if (name.StartsWith("{=", StringComparison.Ordinal) && name.IndexOf('}') > 0) name = name.Substring(name.IndexOf('}') + 1).Trim();
            return name.Length > 0 ? name : (settingsId ?? "").Trim();
        }

        /// <summary>The line in a tools' language code, with the mod's name, or without it when there is none.</summary>
        public static string Text(string language, string? name)
        {
            if (!Lines.TryGetValue(language ?? "", out string? format)) format = Lines["en"];
            return string.IsNullOrEmpty(name) ? format.Replace(" ({0})", "") : string.Format(CultureInfo.InvariantCulture, format, name);
        }
    }
}
