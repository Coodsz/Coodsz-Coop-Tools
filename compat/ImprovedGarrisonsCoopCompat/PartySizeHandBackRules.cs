// Improved Garrisons party size hand back: the type names, log lines and the one decision, without the game, so
// the launcher tests can run them (tests\BannerlordCoopLauncher.Tests links this file). The engine part is
// PartySizeHandBack.cs. Both files are built into the Join Password client module, Coodsz War Sync (TAOM server)
// and Improved Garrisons Coop Compat (Coodsz Host server).
#nullable enable

namespace Coodsz.IgPartySize
{
    internal static class PartySizeHandBackRules
    {
        /// <summary>Improved Garrisons 4.2.0.7's party size model, added only when its LoadCustomPartySizeModel is on.</summary>
        public const string IgModelType = "ImprovedGarrisons.Models.GarrisonpartySizeLimitModel";

        /// <summary>The game's own model, which every mod's party size model extends.</summary>
        public const string DefaultModelType = "TaleWorlds.CampaignSystem.GameComponents.DefaultPartySizeLimitModel";

        public const string HarmonyId = "coodsz.ig-party-size";

        /// <summary>Set once per game process, so two Coodsz modules in one server never patch twice.</summary>
        public const string ProcessKey = "Coodsz.IgPartySizeHandBack";

        public const string LogPrefix = "Improved Garrisons party size: ";

        public const string NotLoadedLine = "Improved Garrisons is not loaded, party sizes are left alone.";

        public const string InstalledLine = "ready, Improved Garrisons builds on another mod's party size model when one is loaded before it.";

        public static string HandBackLine(string previousType) =>
            "Improved Garrisons now builds on " + previousType + ", so that mod's party sizes and tooltips count again. Improved Garrisons' own special parties and multipliers stay.";

        public const string GameModelLine = "no other mod's party size model is loaded before Improved Garrisons, so it is left as it is.";

        public static string MissingLine(string what) =>
            "off, " + what + " was not found. Party sizes are left as Improved Garrisons has them.";

        public static string GarrisonOffLine(string error) =>
            "the garrison limit patch failed (" + error + "), so a garrison limit asked from Improved Garrisons' model directly comes from the game's model. Other party sizes, garrison parties too, still build on the model before it.";

        public static string FailedLine(string error) =>
            "the model before Improved Garrisons failed (" + error + "), Improved Garrisons' own result is used.";

        /// <summary>
        /// True when Improved Garrisons' base result should come from the model that was active before it instead of
        /// the game's own model. Only for Improved Garrisons' model itself, and only when the model before it is
        /// another mod's (TAOM's TaomPartySizeModel): with no model before it, the game's own model (plain Coop,
        /// AD1259) or a second Improved Garrisons model, nothing changes.
        /// </summary>
        public static bool HandsBack(bool instanceIsIg, string? previousType)
        {
            if (!instanceIsIg || string.IsNullOrEmpty(previousType))
                return false;
            return previousType != DefaultModelType && previousType != IgModelType;
        }
    }
}
