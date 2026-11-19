#nullable disable
using System;

namespace BurningEmpiresCoopCompat
{
    // DsSaveSafety's plain rules, without game types, so the launcher tests can run them.
    internal static class SaveSafetyRules
    {
        // The prefab and UIExtenderEx checks only protect the save list screen. The
        // AutoSaveCurrentGame and OverwriteSaveAux checks protect the save itself.
        public static bool IsScreenOnlyReason(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return false;
            if (reason.IndexOf("AutoSaveCurrentGame", StringComparison.Ordinal) >= 0
                || reason.IndexOf("OverwriteSaveAux", StringComparison.Ordinal) >= 0)
                return false;
            return reason.IndexOf("SavedGameGroup", StringComparison.Ordinal) >= 0
                || reason.IndexOf("UIExtenderEx", StringComparison.Ordinal) >= 0;
        }

        // The line for a failed save list check, which every server start has, because the
        // server has no SandBox GUI files. Only the reason's part before the .NET error text is
        // kept: the host tool would read that text as the cause of a later crash.
        public static string ScreenCheckNote(string reason)
        {
            var text = reason ?? string.Empty;
            var cut = text.IndexOf(": ", StringComparison.Ordinal);
            return "Save safety: Burning Empires' save list screen check failed (" + (cut > 0 ? text.Substring(0, cut) : text).Trim()
                + "). Branch autosaves stay on, the server has no save list.";
        }
    }
}
