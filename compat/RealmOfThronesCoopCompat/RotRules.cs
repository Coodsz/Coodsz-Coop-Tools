#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace RealmOfThronesCoopCompat
{
    // Plain rules for when the module runs and what its start line says, without game types, so
    // the launcher tests can run them.
    internal static class RotRules
    {
        public const string MapModuleId = "ROT_Map";
        public const string CoreModuleId = "ROT-Core";
        public const string WarSailsModuleId = "NavalDLC";
        public static readonly string[] CoopModuleIds = { "CoopNightly", "Coop" };

        // Realm of Thrones' own DLLs. Whether the dedicated server ever loads them is the open
        // question, so the log says so at start, after game start and the moment one loads.
        public static readonly string[] WatchedAssemblies = { "ROT", "BannerColorPersistence" };

        // The dedicated server runs from bin\Win64_Shipping_Server; the game never does.
        public static bool IsServerBinFolder(string folder) =>
            !string.IsNullOrEmpty(folder)
            && folder.IndexOf("Win64_Shipping_Server", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool HasModule(IEnumerable<string> moduleIds, string id) =>
            moduleIds != null && moduleIds.Any(module => string.Equals(module, id, StringComparison.OrdinalIgnoreCase));

        // Nothing at all happens without Realm of Thrones' map. On a player's PC the module also
        // stays quiet without Coop, so single player is never touched.
        public static bool ShouldRun(bool server, IEnumerable<string> moduleIds)
        {
            var ids = moduleIds?.ToList() ?? new List<string>();
            if (!HasModule(ids, MapModuleId))
                return false;
            return server || CoopModuleIds.Any(id => HasModule(ids, id));
        }

        // The first Coop module in the list, as "id version", or "none".
        public static string CoopText(IEnumerable<KeyValuePair<string, string>> modules)
        {
            foreach (var module in modules ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (CoopModuleIds.Any(id => string.Equals(id, module.Key, StringComparison.OrdinalIgnoreCase)))
                    return module.Key + " " + module.Value;
            }
            return "none";
        }

        public static string StartLine(string version, bool server, IList<KeyValuePair<string, string>> modules, IEnumerable<string> loadedAssemblies)
        {
            var ids = (modules ?? new List<KeyValuePair<string, string>>()).Select(module => module.Key).ToList();
            return "Realm of Thrones Coop Compat " + version
                + " on the " + (server ? "dedicated server" : "game client")
                + ". Coop module " + CoopText(modules)
                + ". War Sails " + (HasModule(ids, WarSailsModuleId) ? "on" : "off")
                + ". Modules " + string.Join(", ", (modules ?? new List<KeyValuePair<string, string>>()).Select(module => module.Key + " " + module.Value))
                + ". " + AssemblyText(loadedAssemblies);
        }

        // "ROT.dll loaded yes, BannerColorPersistence.dll loaded no" for the assemblies in the AppDomain.
        public static string AssemblyText(IEnumerable<string> loadedAssemblies)
        {
            var loaded = new HashSet<string>(loadedAssemblies ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", WatchedAssemblies.Select(name => name + ".dll loaded " + (loaded.Contains(name) ? "yes" : "no")));
        }

        // The host probe for the next live boot runs only when the tester sets COODSZ_ROT_HOST=probe.
        public const string HostVariable = "COODSZ_ROT_HOST";

        public static bool IsProbeOn(string hostVariable) =>
            string.Equals(hostVariable?.Trim(), "probe", StringComparison.OrdinalIgnoreCase);

        public static bool IsWatched(string assemblyName) =>
            WatchedAssemblies.Any(name => string.Equals(name, assemblyName, StringComparison.OrdinalIgnoreCase));

        // Coop builds a weapon it reads off the network with no item (WeaponComponentDataSurrogate),
        // and Realm of Thrones' prefix on that WeaponComponentData constructor reads the item's id
        // without a null check, so the message carrying the weapon was dropped (battle rewards,
        // score hits, routed damage). WeaponItemGate puts SkipWithoutItem on RoT's prefix on both
        // sides: with no item RoT's flags for its giant club and ice spear have nothing to match.
        public const string WeaponPatchType = "ROT.HarmonyPatches.Core.ROTWeaponComponetData";
        public const string WeaponPatchMethod = "patch1";

        // Set by WeaponItemGate to log the first skip; asked again after Coop's session changes.
        internal static Action OnWeaponSkipped = () => { };
        private static int _weaponSkipLogged;

        public static void ResetWeaponSkipLog() => System.Threading.Interlocked.Exchange(ref _weaponSkipLogged, 0);

        // The Harmony prefix. Returning false skips RoT's patch1 only, never the constructor.
        public static bool SkipWithoutItem(object item)
        {
            if (item != null)
                return true;
            if (System.Threading.Interlocked.Exchange(ref _weaponSkipLogged, 1) == 0)
            {
                try { OnWeaponSkipped(); }
                catch { }
            }
            return false;
        }
    }
}
