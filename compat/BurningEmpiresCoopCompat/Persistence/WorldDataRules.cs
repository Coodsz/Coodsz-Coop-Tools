#nullable disable
using System;

namespace BurningEmpiresCoopCompat
{
    // WorldDataTransfer's plain rules, without game types, so the launcher tests can run them.
    internal static class WorldDataRules
    {
        // The full world data the server stores in a save starts with this property, so a later
        // load can tell the server's copy from Burning Empires' own small copy, which never has
        // it. Burning Empires reads the copy with Json.NET, which skips a property its world data
        // class does not have.
        public const string ServerCopyProperty = "BurningEmpiresCoopCompatServerCopy";

        // How Burning Empires keeps its "External snapshot recovery was used" text until it shows
        // it. Two warnings are joined into one translated text without this id.
        public const string RecoveryNoticeId = "{=BEExternalSaveMrsText}";

        // Puts the server copy property first in the world data JSON object. Anything that is
        // not a JSON object comes back as it is.
        public static string MarkServerCopy(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || IsServerCopy(json))
                return json;
            var open = SkipSpace(json, 0);
            if (json[open] != '{')
                return json;
            var rest = json.Substring(open + 1);
            var next = SkipSpace(rest, 0);
            var empty = next < rest.Length && rest[next] == '}';
            return json.Substring(0, open + 1) + "\"" + ServerCopyProperty + "\":true" + (empty ? "" : ",") + rest;
        }

        // True when the world data JSON is a copy the server stored (MarkServerCopy).
        public static bool IsServerCopy(string json)
        {
            if (string.IsNullOrEmpty(json))
                return false;
            var i = SkipSpace(json, 0);
            if (i >= json.Length || json[i] != '{')
                return false;
            i = SkipSpace(json, i + 1);
            var name = "\"" + ServerCopyProperty + "\"";
            if (json.Length - i < name.Length || string.CompareOrdinal(json, i, name, 0, name.Length) != 0)
                return false;
            i = SkipSpace(json, i + name.Length);
            return i < json.Length && json[i] == ':';
        }

        // True when the only warning Burning Empires waits to show is its recovery notice.
        public static bool IsOnlyRecoveryNotice(string pendingText)
        {
            return !string.IsNullOrEmpty(pendingText) && pendingText.StartsWith(RecoveryNoticeId, StringComparison.Ordinal);
        }

        private static int SkipSpace(string text, int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            return i;
        }
    }
}
