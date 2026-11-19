using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TaleWorlds.ModuleManager;

namespace RealmOfThronesCoopCompat
{
    // Lets this module's server parts find Harmony on the dedicated server, and Realm of Thrones'
    // DLL find the libraries it needs there (rules: HarmonyLocator). BCL and TaleWorlds types only:
    // it is registered before anything that names a Harmony type is compiled, so it must not need
    // Harmony itself. It takes an assembly of the asked name that is already loaded first, logs
    // each distinct outcome once with the path and version, and never throws into the loader. Only
    // the dedicated server registers it; the game client keeps its normal path.
    internal static class ServerHarmony
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Assembly> Found = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private static readonly ServerResolveHandler Handler = new ServerResolveHandler(Find);

        public static void Register() => Handler.Register(handler => AppDomain.CurrentDomain.AssemblyResolve += handler);

        // Finds Harmony now and logs where it came from, or that it is missing (then the server
        // parts that need it stay off).
        public static bool Available() => Find(HarmonyLocator.AssemblyName, null) != null;

        private static Assembly Find(string simpleName, ResolveEventArgs request)
        {
            KeyValuePair<string, string>? picked;
            var requestedName = request?.Name;
            try
            {
                if (HarmonyLocator.IsWindowsForms(simpleName))
                {
                    LogOnce("winforms", HarmonyLocator.WindowsForms + " was asked for by " + (Name(request?.RequestingAssembly) ?? "an unknown assembly")
                        + "; the server has no Windows Forms, so it is not answered.");
                    return null;
                }
                if (!HarmonyLocator.HasRule(simpleName))
                    return null;
                lock (Sync)
                {
                    if (Found.TryGetValue(simpleName, out var known))
                        return known;
                    var loaded = Loaded(simpleName);
                    if (loaded != null)
                    {
                        Found[simpleName] = loaded;
                        LogOnce(simpleName + " loaded", Text(loaded) + " is already loaded, from " + Location(loaded) + Asked(requestedName, loaded) + ".");
                        return loaded;
                    }
                    var modules = ActiveModules();
                    picked = HarmonyLocator.Pick(simpleName, modules, File.Exists);
                    if (picked == null)
                    {
                        if (HarmonyLocator.IsNamedLibrary(simpleName))
                        {
                            var looked = HarmonyLocator.Candidates(simpleName, modules);
                            LogOnce(simpleName + " missing", simpleName + " not found (looked in "
                                + (looked.Count == 0 ? "no active module that ships it" : string.Join(", ", looked.ConvertAll(c => c.Value))) + ")"
                                + (HarmonyLocator.IsHarmonyRequest(simpleName) ? "; the server parts that need it stay off." : "."));
                        }
                        return null;
                    }
                }
                // Loaded outside the lock: the engine's and other mods' load handlers run inside it.
                var assembly = Assembly.LoadFrom(picked.Value.Value);
                lock (Sync)
                    Found[simpleName] = assembly;
                LogOnce(simpleName + " found", Text(assembly) + " loaded from module " + picked.Value.Key + ", " + picked.Value.Value + Asked(requestedName, assembly) + ".");
                return assembly;
            }
            catch (Exception ex)
            {
                LogOnce(simpleName + " failed " + ex.GetType().Name, simpleName + " could not be loaded (" + ex.GetType().Name + ": " + ex.Message + ")"
                    + (HarmonyLocator.IsHarmonyRequest(simpleName) ? "; the server parts that need it stay off." : "."));
                return null;
            }
        }

        private static Assembly Loaded(string simpleName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!assembly.IsDynamic && string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                        return assembly;
                }
                catch { }
            }
            return null;
        }

        private static List<KeyValuePair<string, string>> ActiveModules()
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (var module in ModuleHelper.GetActiveModules())
            {
                if (module == null || !module.IsActive || string.IsNullOrEmpty(module.FolderPath))
                    continue;
                string folder;
                try { folder = Path.GetFullPath(module.FolderPath); }
                catch { folder = module.FolderPath; }
                result.Add(new KeyValuePair<string, string>(module.Id, folder));
            }
            return result;
        }

        private static string Name(Assembly assembly)
        {
            try { return assembly?.GetName().Name; }
            catch { return null; }
        }

        // "MCMv5 5.12.3.0".
        private static string Text(Assembly assembly)
        {
            try
            {
                var name = assembly.GetName();
                return name.Name + " " + name.Version;
            }
            catch
            {
                return "?";
            }
        }

        // ", asked for as 5.11.4.0" when the request named another version.
        private static string Asked(string requestedName, Assembly assembly)
        {
            try
            {
                if (string.IsNullOrEmpty(requestedName) || requestedName.IndexOf(',') < 0)
                    return "";
                var asked = new AssemblyName(requestedName).Version;
                return asked == null || asked == assembly.GetName().Version ? "" : ", asked for as " + asked;
            }
            catch
            {
                return "";
            }
        }

        private static string Location(Assembly assembly)
        {
            try { return string.IsNullOrEmpty(assembly.Location) ? "memory" : assembly.Location; }
            catch { return "?"; }
        }

        // Each distinct outcome once, so an early miss (before the module list is ready) does not
        // hide a later success.
        private static void LogOnce(string outcome, string line)
        {
            lock (Logged)
            {
                if (!Logged.Add(outcome))
                    return;
            }
            SubModule.Log("Server libraries: " + line);
        }
    }
}
