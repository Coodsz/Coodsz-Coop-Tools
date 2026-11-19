// Coodsz Join Password: the TAOM enlist guard's ids, hint and on/off rule, without the game, so the launcher
// tests can run them (tests\BannerlordCoopLauncher.Tests links this file). The engine part is TaomEnlistGuard.cs.
#nullable enable
using System;
using System.Collections.Generic;

namespace Coodsz.EnlistGuard
{
    internal static class TaomEnlistGuardRules
    {
        /// <summary>TAOM's main assembly (Modules\TAOM\bin\Win64_Shipping_Client\TAOM.dll).</summary>
        public const string TaomAssembly = "TAOM";

        /// <summary>TAOM 2.0.29.5's player line on hero_main_options that starts enlisting with a lord.</summary>
        public const string OfferId = "taom_enlist_offer";

        /// <summary>TAOM 2.0.29.5's "I swear it." line, whose consequence does the enlisting on the authority only.</summary>
        public const string SwearId = "taom_enlist_oath_swear";

        /// <summary>The greyed out offer's hint.</summary>
        public const string Hint = "Enlisting is not supported in co-op yet";

        public const string ActiveLine = "TAOM enlist guard on: enlisting with a lord is greyed out here, because TAOM only enlists on the server and the client would attack the lord instead.";

        /// <summary>The line written when TAOM is loaded but its enlist lines were not found (a TAOM update renamed them).</summary>
        public static string MissingLine(string what) =>
            "TAOM enlist guard is off: " + what + " was not found, TAOM may have changed. Enlisting is left as TAOM has it.";

        /// <summary>TAOM itself is loaded. TAOM.Dependencies or TAOM.CoopCompat alone do not count.</summary>
        public static bool TaomLoaded(IEnumerable<string?> assemblyNames)
        {
            foreach (string? name in assemblyNames)
                if (string.Equals(name, TaomAssembly, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// The guard runs only in a Coop client's campaign with TAOM: never on a dedicated server or a Coop
        /// server, and never in single player, where no Coop connect was started in this game.
        /// </summary>
        public static bool ShouldGuard(bool dedicatedServer, bool coopServer, bool coopClientStarted, bool taomLoaded) =>
            !dedicatedServer && !coopServer && coopClientStarted && taomLoaded;
    }
}
