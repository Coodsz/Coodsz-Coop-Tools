using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Map.DistanceCache;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ModuleManager;

namespace RealmOfThronesCoopCompat
{
    // Makes the dedicated server read Realm of Thrones' campaign map.
    // The server's map scene reads Main_map from NavalDLC when War Sails is on (from its own
    // module when off) and sets hard coded border and terrain extents for that map. The War Sails
    // naval installer reads the naval map points and the wind map from NavalDLC's Main_map folder
    // too. With Realm of Thrones, its settlements and distance caches then meet War Sails' navmesh
    // and the server dies in native path code right after loading the cache.
    // On the dedicated server with Realm of Thrones' map only, three patches:
    // - the map scene's Load: the module and extents calls get our values (MapRedirectRules);
    // - the naval installer's Install: its module call gets our module;
    // - NavigationCache.Deserialize: a distance cache picked from another module is read from
    //   Realm of Thrones' module instead (War Sails off picks SandBox's).
    // All three or none. Every call site is found and dry run first, the patches go in together,
    // and the cache swap only acts once the map patches are in, because Realm of Thrones' caches
    // on War Sails' map are the crash itself. The values come from ModuleData\rot_map.txt.
    internal static class MapRedirect
    {
        private const string ServerAssemblyName = "DedicatedServer.Core";
        private const string InstallerTypeName = "DedicatedServer.NavalMapSceneInstaller";
        private const string OwnModuleId = "RealmOfThrones.CoopCompat";
        internal const string HarmonyId = "coodsz.realmofthrones.coopcompat";

        private static readonly object Gate = new object();
        private static readonly MapData.CachePick Caches = new MapData.CachePick();
        private static Harmony _harmony;
        private static bool _warSails;
        private static bool _tried;
        private static readonly RedirectHooks Hooks = new RedirectHooks(SubModule.Log);
        private static Type _extentsType;

        // The staged folder check asks for the naval files only with NavalDLC active. That matches
        // what the server reads: it refuses to boot when its War Sails switch and NavalDLC in the
        // module list disagree (WarSailsBoot.ApplyFlag), and its installer returns early when off.
        public static void Install(bool warSails)
        {
            _harmony = new Harmony(HarmonyId);
            _warSails = warSails;
            var data = LoadData();
            if (data == null)
                return;
            var moduleFolder = ModuleFolder(data.Module);
            if (moduleFolder == null)
            {
                SubModule.Log("Map redirect: module " + data.Module + " is not active; the server keeps its own map.");
                return;
            }
            var problems = MapData.MapFolderProblems(moduleFolder, warSails, File.Exists, IsStripped);
            if (problems.Count > 0)
            {
                SubModule.Log("Map redirect: the server keeps its own map, because Realm of Thrones' module is not ready for it: "
                    + string.Join("; ", problems) + ".");
                return;
            }
            Hooks.Data = data;
            SubModule.Log("Map redirect: using " + data + " from " + MapData.FileName + ".");

            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryPatch(e.LoadedAssembly);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(assembly);
        }

        private static MapData LoadData()
        {
            try
            {
                var folder = ModuleHelper.GetModuleInfo(OwnModuleId)?.FolderPath;
                if (string.IsNullOrEmpty(folder))
                {
                    SubModule.Log("Map redirect: could not find this module's own folder; the server keeps its own map.");
                    return null;
                }
                var path = Path.Combine(folder, "ModuleData", MapData.FileName);
                if (!File.Exists(path))
                {
                    SubModule.Log("Map redirect: no " + path + "; the server keeps its own map.");
                    return null;
                }
                var data = MapData.Parse(File.ReadAllText(path), out var error);
                if (data == null)
                    SubModule.Log("Map redirect: " + path + " is not usable (" + error + "); the server keeps its own map.");
                return data;
            }
            catch (Exception ex)
            {
                SubModule.Log("Map redirect: could not read " + MapData.FileName + "; the server keeps its own map: " + ex.Message);
                return null;
            }
        }

        private static string ModuleFolder(string moduleId)
        {
            var info = ModuleHelper.GetModuleInfo(moduleId);
            if (info == null || !info.IsActive || string.IsNullOrEmpty(info.FolderPath))
                return null;
            return info.FolderPath;
        }

        private static bool IsStripped(string scene)
        {
            using (var reader = new StreamReader(scene))
                return MapData.SceneIsStripped(reader);
        }

        private static void TryPatch(Assembly assembly)
        {
            if (_tried || Hooks.Data == null || assembly == null)
                return;
            string name;
            try { name = assembly.GetName().Name; }
            catch { return; }
            if (name != ServerAssemblyName)
                return;
            lock (Gate)
            {
                if (_tried)
                    return;
                _tried = true;
            }

            try
            {
                PatchAll(assembly);
            }
            catch (Exception ex)
            {
                SubModule.Log("Map redirect: could not check the server's map scene; the server keeps its own map: " + ex);
            }
        }

        private static void PatchAll(Assembly server)
        {
            var plan = MapRedirectRules.MakePlan(LoadableTypes(server), typeof(SandBox.MapScene), server.GetType(InstallerTypeName, false),
                typeof(Game), _warSails, ModuleHookMethod, NavalHookMethod, ExtentsHookMethod);
            if (plan.Problem != null)
            {
                SubModule.Log("Map redirect: " + plan.Problem + " in " + ServerAssemblyName + "; nothing patched, the server keeps its own map.");
                return;
            }
            var cache = AccessTools.Method(typeof(NavigationCache<Settlement>), "Deserialize", new[] { typeof(string) });
            if (cache == null)
            {
                SubModule.Log("Map redirect: NavigationCache.Deserialize not found; nothing patched, the server keeps its own map.");
                return;
            }

            _extentsType = plan.ExtentsType;
            Hooks.ExtentsConstructor = plan.ExtentsConstructor;
            var id = _harmony.Id;
            var steps = new List<(string Name, Action Apply, Action Undo)>
            {
                (plan.Load.DeclaringType.FullName + ".Load",
                    () => _harmony.Patch(plan.Load, transpiler: new HarmonyMethod(typeof(MapRedirect), nameof(LoadTranspiler))),
                    () => _harmony.Unpatch(plan.Load, HarmonyPatchType.Transpiler, id)),
            };
            if (plan.Install != null)
            {
                steps.Add((InstallerTypeName + ".Install",
                    () => _harmony.Patch(plan.Install, transpiler: new HarmonyMethod(typeof(MapRedirect), nameof(InstallTranspiler))),
                    () => _harmony.Unpatch(plan.Install, HarmonyPatchType.Transpiler, id)));
            }
            steps.Add(("NavigationCache.Deserialize",
                () => _harmony.Patch(cache, prefix: new HarmonyMethod(typeof(MapRedirect), nameof(DeserializePrefix))),
                () => _harmony.Unpatch(cache, HarmonyPatchType.Prefix, id)));

            var failure = Hooks.ApplyAndActivate(steps);
            if (failure != null)
            {
                SubModule.Log("Map redirect: " + failure + ".");
                return;
            }
            SubModule.Log("Map redirect: patched " + plan.Load.DeclaringType.FullName + ".Load (" + plan.LoadModuleCalls + " module call, "
                + plan.LoadExtentsCalls + " extents call on " + plan.ExtentsType.FullName + "), "
                + (plan.Install == null ? (_warSails ? "no naval installer in this server" : "the naval installer left alone (War Sails off)") : InstallerTypeName + ".Install (" + plan.InstallModuleCalls + " module call)")
                + " and the distance cache pick.");
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(type => type != null); }
        }

        private static MethodInfo ModuleHookMethod => AccessTools.Method(typeof(MapRedirect), nameof(MapModule));
        private static MethodInfo NavalHookMethod => AccessTools.Method(typeof(MapRedirect), nameof(NavalModule));
        private static MethodInfo ExtentsHookMethod => AccessTools.Method(typeof(MapRedirect), nameof(MapExtents));

        private static IEnumerable<CodeInstruction> LoadTranspiler(IEnumerable<CodeInstruction> instructions) =>
            MapRedirectRules.Rewrite(instructions, _extentsType, ModuleHookMethod, ExtentsHookMethod, out _, out _);

        private static IEnumerable<CodeInstruction> InstallTranspiler(IEnumerable<CodeInstruction> instructions) =>
            MapRedirectRules.Rewrite(instructions, _extentsType, NavalHookMethod, null, out _, out _);

        // The rewritten calls land here; RedirectHooks keeps the server's own values until every
        // patch is in.
        public static string MapModule(string original) => Hooks.MapModule(original);

        public static string NavalModule(string original) => Hooks.NavalModule(original);

        public static object MapExtents(object original) => Hooks.MapExtents(original);

        private static void DeserializePrefix(ref string __0)
        {
            try
            {
                var rot = ModuleHelper.GetModuleInfo(Hooks.Data?.Module ?? "")?.FolderPath;
                var picked = Caches.Pick(Hooks.Active, __0, rot, File.Exists, SubModule.Log);
                if (picked != null)
                    __0 = picked;
            }
            catch (Exception ex)
            {
                SubModule.Log("Map redirect: could not check the server's distance cache pick, so it is read as it is: " + ex.Message);
            }
            if (Hooks.Active)
                CheckWorld(__0);
        }

        private static readonly HashSet<string> CheckedCaches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A world that lacks the cache's settlements makes the game's reader fail with a bare
        // NullReferenceException, so the reason goes in the log first, once per file. The original
        // still runs: there is no public, clean way to stop the server here (throwing or exiting
        // ourselves would skip the server's own error path), and it fails the same way as before.
        private static void CheckWorld(string path)
        {
            try
            {
                lock (CheckedCaches)
                {
                    if (!CheckedCaches.Add(path ?? ""))
                        return;
                }
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return;
                HashSet<string> ids;
                string error;
                using (var stream = File.OpenRead(path))
                    ids = CacheIds.Read(stream, CacheIds.HasLandRatio(path), out error);
                if (ids == null)
                {
                    SubModule.Log("Map redirect: could not read the settlement ids in " + path + " to check this world (" + error + "); skipped.");
                    return;
                }
                var line = CacheIds.MissingLine(path, ids, id => Settlement.Find(id) != null);
                if (line != null)
                    SubModule.Log(line);
            }
            catch (Exception ex)
            {
                SubModule.Log("Map redirect: could not check this world against " + path + "; skipped: " + ex.Message);
            }
        }
    }
}
