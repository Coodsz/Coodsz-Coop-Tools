// Coodsz Settings Sync: the limits of the PvP countdown messages (NetworkPvpCountdown, NetworkPvpCountdownStop),
// which both sides check.
//
// Engine free (no game or Coop types), so the launcher's tests compile this file as is.
#nullable enable

namespace CoodszSettingsSync
{
    public static class PvpCountdownWire
    {
        /// <summary>Bumped when the countdown messages change meaning. Other versions are ignored.</summary>
        public const int ProtocolVersion = 1;

        public const int MaxSessionIdLength = 128;

        /// <summary>The response timer's longest setting (PvpRulesSettings.MostSeconds).</summary>
        public const int MostSeconds = 300;

        public const int CaughtRole = 1;
        public const int AttackerRole = 2;

        public static bool IsValidSessionId(string? sessionId) =>
            !string.IsNullOrEmpty(sessionId) && sessionId!.Length <= MaxSessionIdLength;

        /// <summary>True when a start message can be shown: a known version, a session id, 1 to 300 seconds and a known role.</summary>
        public static bool IsValidStart(int version, string? sessionId, int secondsLeft, int role) =>
            version == ProtocolVersion && IsValidSessionId(sessionId) && secondsLeft >= 1 && secondsLeft <= MostSeconds &&
            (role == CaughtRole || role == AttackerRole);

        public static bool IsValidStop(int version, string? sessionId) => version == ProtocolVersion && IsValidSessionId(sessionId);
    }
}
