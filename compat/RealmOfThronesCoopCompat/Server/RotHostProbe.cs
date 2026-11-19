using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using TaleWorlds.ModuleManager;

namespace RealmOfThronesCoopCompat
{
    // A probe for the next live boot, off unless the tester sets COODSZ_ROT_HOST=probe. Once every
    // module is loaded it loads ROT.dll from ROT-Core's client bin and logs its version, how many
    // of its types load, each type that does not with the assembly it misses (the server's
    // libraries come from ServerHarmony), and what the static getter ROT.ROTSettings.Duels reads.
    // Nothing else of Realm of Thrones is called. Loading ROT.dll puts it in the server's
    // AppDomain, where the game's own type scans can see it from then on, so it stays off by
    // default. BCL and TaleWorlds types only.
    internal static class RotHostProbe
    {
        private const int MaxTypeLines = 60;

        private static int _ran;

        public static void Run(string hook)
        {
            try
            {
                if (!RotRules.IsProbeOn(Environment.GetEnvironmentVariable(RotRules.HostVariable)) || Interlocked.Exchange(ref _ran, 1) == 1)
                    return;
                RunNow(hook);
            }
            catch (Exception ex)
            {
                SubModule.Log("Host probe: failed: " + ex);
            }
        }

        private static void RunNow(string hook)
        {
            SubModule.Log("Host probe: on (" + RotRules.HostVariable + "=probe), run from " + hook + ".");
            var path = RotDllPath();
            var rot = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => !a.IsDynamic && Name(a) == "ROT");
            if (rot == null)
            {
                if (path == null || !File.Exists(path))
                {
                    SubModule.Log("Host probe: ROT.dll not found (" + (path ?? "ROT-Core is not an active module") + ").");
                    return;
                }
                rot = Assembly.LoadFrom(path);
            }
            SubModule.Log("Host probe: ROT.dll " + rot.GetName().Version + " loaded from " + rot.Location + ".");

            // Every type on its own by its metadata token, so a type that does not load names the
            // assembly it misses. A failing type cannot tell its own name, so the names come from
            // the file's metadata.
            Type[] listed;
            try { listed = rot.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { listed = ex.Types; }
            var names = TypeNames(rot.Location);
            var failures = new List<string>();
            var missing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            // Row 1 is <Module>, which GetTypes leaves out.
            for (var row = 2; row <= listed.Length + 1; row++)
            {
                try
                {
                    rot.ManifestModule.ResolveType(0x02000000 | row);
                }
                catch (Exception ex)
                {
                    var what = Missing(ex);
                    missing[what] = missing.TryGetValue(what, out var count) ? count + 1 : 1;
                    failures.Add((names.TryGetValue(row, out var name) ? name : "type row " + row) + ": " + what);
                }
            }
            SubModule.Log("Host probe: " + (listed.Length - failures.Count) + " of " + listed.Length + " ROT.dll types load"
                + (failures.Count == 0 ? "." : "; missing " + string.Join(", ", missing.Select(m => m.Key + " (" + m.Value + " types)")) + "."));
            foreach (var failure in failures.Take(MaxTypeLines))
                SubModule.Log("Host probe: type " + failure);
            if (failures.Count > MaxTypeLines)
                SubModule.Log("Host probe: " + (failures.Count - MaxTypeLines) + " more types failed.");

            ReadDuels(rot);
        }

        // ROT.ROTSettings reads its MCM settings, or its defaults when MCM has none.
        private static void ReadDuels(Assembly rot)
        {
            try
            {
                var duels = rot.GetType("ROT.ROTSettings", false)?.GetProperty("Duels", BindingFlags.Public | BindingFlags.Static);
                if (duels == null)
                {
                    SubModule.Log("Host probe: ROT.ROTSettings.Duels not found.");
                    return;
                }
                SubModule.Log("Host probe: ROT.ROTSettings.Duels reads " + (duels.GetValue(null, null) ?? "null") + ".");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                SubModule.Log("Host probe: reading ROT.ROTSettings.Duels threw " + inner.GetType().Name + ": " + inner.Message);
            }
        }

        private static string Missing(Exception ex)
        {
            if (ex is FileNotFoundException notFound && !string.IsNullOrEmpty(notFound.FileName))
                return notFound.FileName;
            if (ex is FileLoadException notLoaded && !string.IsNullOrEmpty(notLoaded.FileName))
                return notLoaded.FileName;
            return ex.GetType().Name + ": " + CrashTraceRules.Shorten(ex.Message);
        }

        private static string RotDllPath()
        {
            var modules = new List<KeyValuePair<string, string>>();
            foreach (var module in ModuleHelper.GetActiveModules())
            {
                if (module != null && module.IsActive && !string.IsNullOrEmpty(module.FolderPath))
                    modules.Add(new KeyValuePair<string, string>(module.Id, module.FolderPath));
            }
            var path = HarmonyLocator.ModuleFile(modules, RotRules.CoreModuleId, HarmonyLocator.ClientBin, "ROT.dll");
            try { return path == null ? null : Path.GetFullPath(path); }
            catch { return path; }
        }

        private static string Name(Assembly assembly)
        {
            try { return assembly.GetName().Name; }
            catch { return null; }
        }

        // Type names by metadata row ("ROT.ROTSettingsMCM", "ROT.ROTSettingsMCM+<>c"), read with
        // System.Reflection.Metadata, which the server's .NET has. Empty when it cannot be read.
        private static Dictionary<int, string> TypeNames(string path)
        {
            var names = new Dictionary<int, string>();
            try
            {
                var metadata = Assembly.Load("System.Reflection.Metadata");
                var peReaderType = metadata.GetType("System.Reflection.PortableExecutable.PEReader", true);
                using (var stream = File.OpenRead(path))
                using (var pe = (IDisposable)Activator.CreateInstance(peReaderType, new object[] { stream }))
                {
                    var reader = metadata.GetType("System.Reflection.Metadata.PEReaderExtensions", true)
                        .GetMethod("GetMetadataReader", new[] { peReaderType }).Invoke(null, new object[] { pe });
                    var readerType = reader.GetType();
                    string Text(object definition, string property)
                    {
                        var handle = definition.GetType().GetProperty(property).GetValue(definition, null);
                        return (string)readerType.GetMethod("GetString", new[] { handle.GetType() }).Invoke(reader, new[] { handle });
                    }
                    var rows = new Dictionary<object, int>();
                    var parts = new List<(string Name, object Declaring)>();
                    foreach (var handle in (IEnumerable)readerType.GetProperty("TypeDefinitions").GetValue(reader, null))
                    {
                        var definition = readerType.GetMethod("GetTypeDefinition", new[] { handle.GetType() }).Invoke(reader, new[] { handle });
                        var space = Text(definition, "Namespace");
                        var declaring = definition.GetType().GetMethod("GetDeclaringType", Type.EmptyTypes).Invoke(definition, null);
                        var nil = (bool)declaring.GetType().GetProperty("IsNil").GetValue(declaring, null);
                        parts.Add(((string.IsNullOrEmpty(space) ? "" : space + ".") + Text(definition, "Name"), nil ? null : declaring));
                        rows[handle] = parts.Count;
                    }
                    string Full(int row, int depth)
                    {
                        var part = parts[row - 1];
                        return part.Declaring != null && depth < 10 && rows.TryGetValue(part.Declaring, out var outer) ? Full(outer, depth + 1) + "+" + part.Name : part.Name;
                    }
                    for (var row = 1; row <= parts.Count; row++)
                        names[row] = Full(row, 0);
                }
            }
            catch
            {
                // Rows without a name are logged by number.
            }
            return names;
        }
    }
}
