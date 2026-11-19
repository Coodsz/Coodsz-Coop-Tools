// Coodsz War Sync: when the small War of the Ring sender may run. Engine free; compiled into the module and
// into the launcher's tests.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoodszWarSync
{
    public static class WarSyncRules
    {
        /// <summary>Coodsz Host's full server module, which already sends the War of the Ring state.</summary>
        public const string FullToolsAssembly = "DedicatedServer.CoodszTools";

        public const string LogPrefix = "[Coodsz War Sync] ";

        public const string StepAsideLine =
            "Coodsz Server Tools is loaded and sends the War of the Ring state itself, so Coodsz War Sync stays off.";

        /// <summary>
        /// Runs only on a dedicated server without Coodsz Server Tools. Any version counts: Coodsz Host and the TAOM server tool (which adds it to DedicatedServer.Windows next to War Sync) both ship one that sends the war state. Both load the same CoodszSettingsSync.Coop
        /// assembly, and two copies would give Coop two handlers and two message types for every request.
        /// </summary>
        public static bool ShouldRun(bool dedicatedServer, IEnumerable<string?> loadedAssemblies) =>
            dedicatedServer && !loadedAssemblies.Any(n => string.Equals(n, FullToolsAssembly, StringComparison.OrdinalIgnoreCase));
    }
}
