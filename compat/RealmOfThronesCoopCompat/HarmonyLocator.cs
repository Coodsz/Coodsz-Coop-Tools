#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RealmOfThronesCoopCompat
{
    // Where the dedicated server can find Harmony and the other libraries this module and Realm
    // of Thrones' DLL need, without game types, so the launcher tests can run it. The nightly
    // server loads no 0Harmony.dll before the mods: the Coop module has no SubModule of its own,
    // Bannerlord.Harmony ships no Win64_Shipping_Server bin, and DedicatedServer.Windows only adds
    // Coop's assembly resolver in its own OnSubModuleLoad, after ours, because it loads last.
    // Burning Empires Coop Compat does without this because the older server builds it ran on had
    // Harmony loaded before any mod. By simple name:
    // - MCMv5 and Bannerlord.UIExtenderEx come from the active module that ships them, its client
    //   bin. Those modules have no server bin and their SubModules never run on the server; ROT.dll
    //   only needs their types (the offline dry run loaded all 348 of its types that way).
    // - 0Harmony and the view and UI assemblies in CoopBinLibraries only from the hosted Coop
    //   module's server bin, the first of CoopNightly and Coop whose bin has the file. That is the
    //   Harmony the server itself runs Coop on; Bannerlord.Harmony's file is the game's .NET
    //   Framework build, so it is never used, and we never ship our own copy, so a nightly that
    //   updates Harmony is picked up. The view and UI assemblies are the ones ROT.dll,
    //   BannerColorPersistence.dll, MCMv5 and UIExtenderEx name (and what those name in turn).
    // - No other name, so Coop's own assemblies (Common, GameInterface, Coop.Core and the rest)
    //   and SandBox stay with the server's own resolver and engine, whose integrity check (exit 4)
    //   wants them from its own folder at its own time.
    // - System.Windows.Forms is never answered, but a request for it is logged once (a tripwire:
    //   Realm of Thrones only uses it in its banned module message box).
    internal static class HarmonyLocator
    {
        public const string AssemblyName = "0Harmony";
        public const string FileName = "0Harmony.dll";
        public const string ClientBin = "Win64_Shipping_Client";
        public const string ServerBin = "Win64_Shipping_Server";

        // Libraries taken from another module's client bin, as simple name and module id.
        public static readonly KeyValuePair<string, string>[] ClientLibraries =
        {
            new KeyValuePair<string, string>("MCMv5", "Bannerlord.MBOptionScreen"),
            new KeyValuePair<string, string>("Bannerlord.UIExtenderEx", "Bannerlord.UIExtenderEx"),
        };

        // What the hosted Coop module's server bin may answer.
        public static readonly string[] CoopBinLibraries =
        {
            AssemblyName, "SandBox.View", "SandBox.GauntletUI", "SandBox.ViewModelCollection", "TaleWorlds.MountAndBlade.View",
            "TaleWorlds.MountAndBlade.GauntletUI", "TaleWorlds.MountAndBlade.GauntletUI.Widgets", "TaleWorlds.MountAndBlade.ViewModelCollection",
            "TaleWorlds.CampaignSystem.ViewModelCollection", "TaleWorlds.Core.ViewModelCollection", "TaleWorlds.Engine.GauntletUI", "TaleWorlds.GauntletUI",
            "TaleWorlds.GauntletUI.Data", "TaleWorlds.GauntletUI.ExtraWidgets", "TaleWorlds.GauntletUI.PrefabSystem", "TaleWorlds.TwoDimension",
        };

        public const string WindowsForms = "System.Windows.Forms";
        private const int MaxNameLength = 128;

        // The simple name of a request by full or simple name. Null for an empty name and for one
        // that could point outside a folder once it is a file name.
        public static string SimpleName(string requestedName)
        {
            if (string.IsNullOrEmpty(requestedName))
                return null;
            var comma = requestedName.IndexOf(',');
            var simple = (comma < 0 ? requestedName : requestedName.Substring(0, comma)).Trim();
            if (simple.Length == 0 || simple.Length > MaxNameLength || simple[0] == '.' || simple.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || simple.Contains(".."))
                return null;
            return simple;
        }

        // True for a request for 0Harmony by its full or simple name.
        public static bool IsHarmonyRequest(string requestedName) =>
            string.Equals(SimpleName(requestedName), AssemblyName, StringComparison.OrdinalIgnoreCase);

        // Harmony and the client libraries are always expected, so a miss of one is logged. A view
        // or UI assembly is only asked for when something needs it, so its miss is left to the
        // engine's own error.
        public static bool IsNamedLibrary(string simpleName) =>
            string.Equals(simpleName, AssemblyName, StringComparison.OrdinalIgnoreCase)
            || ClientLibraries.Any(library => string.Equals(library.Key, simpleName, StringComparison.OrdinalIgnoreCase));

        // Whether a rule covers the name at all. Without one the resolver does not even hand out
        // an assembly of that name that is already loaded.
        public static bool HasRule(string simpleName) =>
            IsNamedLibrary(simpleName) || CoopBinLibraries.Contains(simpleName, StringComparer.OrdinalIgnoreCase);

        public static bool IsWindowsForms(string simpleName) =>
            string.Equals(simpleName, WindowsForms, StringComparison.OrdinalIgnoreCase);

        // Harmony's candidates.
        public static List<KeyValuePair<string, string>> Candidates(IEnumerable<KeyValuePair<string, string>> activeModules) =>
            Candidates(AssemblyName, activeModules);

        // The files that may answer a simple name, as module id and path, in the order they are
        // tried. Modules come as id and folder from the game's module list, so no folder name is
        // assumed. Empty when no rule fits.
        public static List<KeyValuePair<string, string>> Candidates(string simpleName, IEnumerable<KeyValuePair<string, string>> activeModules)
        {
            var result = new List<KeyValuePair<string, string>>();
            var simple = SimpleName(simpleName);
            if (simple == null)
                return result;
            var modules = (activeModules ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .Where(module => !string.IsNullOrWhiteSpace(module.Key) && !string.IsNullOrWhiteSpace(module.Value))
                .ToList();
            var library = ClientLibraries.FirstOrDefault(l => string.Equals(l.Key, simple, StringComparison.OrdinalIgnoreCase));
            if (library.Key != null)
            {
                foreach (var module in modules.Where(m => string.Equals(m.Key, library.Value, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new KeyValuePair<string, string>(module.Key, Path.Combine(module.Value, "bin", ClientBin, library.Key + ".dll")));
                return result;
            }
            if (!CoopBinLibraries.Contains(simple, StringComparer.OrdinalIgnoreCase))
                return result;
            foreach (var id in RotRules.CoopModuleIds)
            {
                foreach (var module in modules.Where(m => string.Equals(m.Key, id, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new KeyValuePair<string, string>(module.Key, Path.Combine(module.Value, "bin", ServerBin, simple + ".dll")));
            }
            return result;
        }

        // A file in an active module's bin folder, or null when the module is not active.
        public static string ModuleFile(IEnumerable<KeyValuePair<string, string>> activeModules, string moduleId, string binFolder, string fileName)
        {
            foreach (var module in activeModules ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.Equals(module.Key, moduleId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(module.Value))
                    return Path.Combine(module.Value, "bin", binFolder, fileName);
            }
            return null;
        }

        // Whether the SubModule constructor registers the resolver: only in the dedicated server's
        // process and only with Realm of Thrones' map active. The game client never does.
        public static bool ShouldRegister(string baseDirectory, IEnumerable<string> activeModuleIds) =>
            RotRules.IsServerBinFolder(baseDirectory) && RotRules.ShouldRun(true, activeModuleIds);

        // Harmony's pick.
        public static KeyValuePair<string, string>? Pick(IEnumerable<KeyValuePair<string, string>> activeModules, Func<string, bool> exists) =>
            Pick(AssemblyName, activeModules, exists);

        // The first candidate whose file exists, or null when none has it.
        public static KeyValuePair<string, string>? Pick(string simpleName, IEnumerable<KeyValuePair<string, string>> activeModules, Func<string, bool> exists)
        {
            foreach (var candidate in Candidates(simpleName, activeModules))
            {
                if (exists(candidate.Value))
                    return candidate;
            }
            return null;
        }
    }

    // The resolve handler's own rules: it subscribes once however often it is asked, hands the
    // finder only names HarmonyLocator has a rule for (and the Windows Forms tripwire), never asks
    // again for a name it is already resolving on this thread, and never throws into the loader.
    // How a library is found is passed in, so the launcher tests can run this without the game.
    internal sealed class ServerResolveHandler
    {
        [ThreadStatic] private static HashSet<string> _resolving;

        private readonly Func<string, ResolveEventArgs, System.Reflection.Assembly> _find;
        private int _registered;

        // The finder gets the simple name and the request (its full name and who asked).
        public ServerResolveHandler(Func<string, ResolveEventArgs, System.Reflection.Assembly> find)
        {
            _find = find;
        }

        // True the first time, when it subscribed.
        public bool Register(Action<ResolveEventHandler> subscribe)
        {
            if (System.Threading.Interlocked.Exchange(ref _registered, 1) == 1)
                return false;
            subscribe(Resolve);
            return true;
        }

        public System.Reflection.Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var simple = HarmonyLocator.SimpleName(args?.Name);
                if (simple == null || !(HarmonyLocator.HasRule(simple) || HarmonyLocator.IsWindowsForms(simple)))
                    return null;
                var resolving = _resolving ?? (_resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (!resolving.Add(simple))
                    return null;
                try
                {
                    return _find(simple, args);
                }
                finally
                {
                    resolving.Remove(simple);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
