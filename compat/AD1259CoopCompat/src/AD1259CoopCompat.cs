// AD1259 <-> Bannerlord Coop dedicated-server compatibility submodule.
//
// Loaded from the standalone DedicatedServer.AD1259CoopCompat module. The original
// DedicatedServer.Windows submodule registration remains supported during migration.
// Only the dedicated-server engine enables these hooks; a client that enables the module
// installs no patches. It does five things:
//
//  1. New campaign on a custom map (FreshCampaign.cs, HeadlessCharacterCreation.cs). When the server
//     is about to host its copy of Coop's vanilla default_new_game template on a custom campaign map
//     such as AD1259, a brand-new campaign is created on that map instead, with character creation
//     completed on the server. Coodsz Host's "new campaign" therefore works without a save made in
//     single-player.
//
//  2. Distance-cache redirect. The Coop dedicated server always reads the settlement distance
//     cache from Modules/SandBox/ModuleData/DistanceCaches. A custom campaign map (AD1259) ships
//     its own cache, so the server read a cache that does not match the loaded Main_map. The
//     prefix on NavigationCache<Settlement>.Deserialize points the read at the active map
//     provider's own cache, so SandBox's vanilla file can stay vanilla for the other profiles.
//
//  3. World-consistency guard. A total-conversion map cannot run on Coop's default_new_game
//     template (a vanilla Calradia world). Previously that ended in a native access violation
//     (exit -1073741819) during map/cache initialisation. Point 1 normally replaces the template;
//     for any vanilla world that still reaches the map (fresh-campaign support disabled, or a
//     vanilla save selected) the guard compares the loaded world with the map provider's own XML
//     before the cache is read and stops with a clear message (exit code 2, the server's
//     documented "load failure" code) instead of crashing.
//
//  4. Fording parity. AD1259 marks navmesh faces with id 9 as TerrainType.Fording by patching
//     SandBox.MapScene. The dedicated server uses its own IMapScene implementation, so that rule
//     silently did not apply on the authoritative server. The same rule is applied to the
//     server's map scene so server and clients agree on terrain.
//
//  5. Settlements the map does not define (DistanceGuard.cs). Coop v0.1.6 spawns lords for its own
//     minor faction, whose XML home is a vanilla town; on AD1259 that is a placeholder object, and a
//     distance query for it made the engine's path search crash the server while a new world was
//     built. Such clans get a real town of the map as home, and a cache miss that involves anything
//     but a real settlement on the navigation mesh is answered as unreachable instead of crashing.
//
// Escape hatches (environment variables, value "1"):
//   AD1259COOP_DISABLE_FRESH_CAMPAIGN - never replace the template with a new campaign (see FreshCampaign.cs)
//   AD1259COOP_SKIP_WORLD_CHECK       - log the world check but never stop the server
//   AD1259COOP_DISABLE_CACHE_REDIRECT - keep the server's original distance-cache path
//   AD1259COOP_DISABLE_FORDING_PARITY - do not patch the server map scene
//   AD1259COOP_DISABLE_DISTANCE_GUARD - leave distance-cache misses to the game (see DistanceGuard.cs)
//   AD1259COOP_DISABLE_HOME_FIX       - leave clans' homes as the XML set them

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;
using Path = System.IO.Path;

namespace AD1259CoopCompat
{
    public sealed class CompatSubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "ad1259.coop.compat";
        internal static Harmony HarmonyInstance;
        private static bool _serverPatchesInitialized;
        private static bool _freshCampaignInstallAttempted;
        private bool _serverRuntime;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            // This property is available before network startup. In the dedicated-server
            // game assembly its getter is a constant true; the client engine returns false.
            // Never install the headless creation or process-exit guards in a player's game.
            if (!GameNetwork.IsDedicatedServer) return;
            _serverRuntime = true;
            InitializeServerPatches();
        }

        private static void InitializeServerPatches()
        {
            // A migrated install can briefly have both the old injected entry and the
            // standalone module. Harmony prefixes must be registered once per process.
            if (_serverPatchesInitialized) return;
            _serverPatchesInitialized = true;
            try
            {
                HarmonyInstance = new Harmony(HarmonyId);
                RbmCampaignPolicy.TryInstall(HarmonyInstance);
                InstallMapGuards();
                Type openCache = typeof(Settlement).Assembly.GetType(
                    "TaleWorlds.CampaignSystem.Map.DistanceCache.NavigationCache`1", false);
                MethodInfo deserialize = openCache == null
                    ? null
                    : AccessTools.Method(openCache.MakeGenericType(typeof(Settlement)), "Deserialize",
                        new[] { typeof(string) });
                if (deserialize == null)
                {
                    Log.Warn("NavigationCache<Settlement>.Deserialize(string) was not found in this game build; " +
                             "distance-cache redirect and world check are disabled.");
                    return;
                }

                HarmonyInstance.Patch(deserialize,
                    prefix: new HarmonyMethod(typeof(DistanceCachePatch), nameof(DistanceCachePatch.Prefix)));
                Log.Info("loaded (distance-cache redirect, world check, fording parity, distance-miss guard, placeholder " +
                         "homes; new-campaign support follows once every module is loaded).");
            }
            catch (Exception e)
            {
                Log.Error("failed to install patches: " + e);
            }
        }

        // The legacy injected entry can load before SandBox; the standalone module loads
        // after it. Both install the new-campaign hooks once SandBox is available.
        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_serverRuntime)
            {
                RbmCampaignPolicy.TryInstall(HarmonyInstance);
                InstallFreshCampaign();
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if (_serverRuntime) RbmCampaignPolicy.TryInstall(HarmonyInstance);
            if (_serverRuntime && !_freshCampaignInstallAttempted) InstallFreshCampaign();
            if (_serverRuntime) RbmEconomyDiagnostics.TryReport();
        }

        public override void OnAfterGameInitializationFinished(Game game, object starterObject)
        {
            base.OnAfterGameInitializationFinished(game, starterObject);
            if (_serverRuntime)
            {
                FordingParity.TryInstall();
                RbmEconomyDiagnostics.TryReport();
            }
        }

        // All live in TaleWorlds.CampaignSystem, which is loaded before any module's submodules.
        private static void InstallMapGuards()
        {
            try
            {
                DistanceMissGuard.Install(HarmonyInstance);
            }
            catch (Exception e)
            {
                Log.Error("failed to install the distance-cache miss guard: " + e);
            }
            try
            {
                PlaceholderHomes.Install(HarmonyInstance);
            }
            catch (Exception e)
            {
                Log.Error("failed to install the placeholder-home fix: " + e);
            }
            try
            {
                CompanionTemplateGuard.Install(HarmonyInstance);
            }
            catch (Exception e)
            {
                Log.Error("failed to install the companion template guard: " + e);
            }
        }

        private static void InstallFreshCampaign()
        {
            if (_freshCampaignInstallAttempted) return;
            if (HarmonyInstance == null || !IsAssemblyLoaded("SandBox")) return;
            _freshCampaignInstallAttempted = true;
            try
            {
                FreshCampaign.Install(HarmonyInstance);
            }
            catch (Exception e)
            {
                Log.Error("failed to install new-campaign support (the world check stays in charge): " + e);
            }
        }

        private static bool IsAssemblyLoaded(string simpleName)
        {
            try
            {
                return AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>The active module that provides SceneObj/Main_map (for example AD1259).</summary>
    internal sealed class MapProvider
    {
        private static readonly HashSet<string> VanillaMapHosts = new HashSet<string>(
            new[] { "Native", "SandBoxCore", "Sandbox", "SandBox", "StoryMode", "CustomBattle", "DedicatedServer.Windows" },
            StringComparer.OrdinalIgnoreCase);

        public string ModuleId;
        public string ModulePath;   // engine-style module path, e.g. ..\..\Modules\AD1259/
        public string FullPath;     // absolute directory

        public static MapProvider Resolve()
        {
            string[] active;
            try { active = Utilities.GetModulesNames() ?? new string[0]; }
            catch { active = new string[0]; }

            // The Coodsz/HexTool launcher already tells the engine which modules provide the map.
            string fromLauncher = Environment.GetEnvironmentVariable("BANNERLORD_DS_CUSTOM_MAP_MODULES");
            IEnumerable<string> candidates = string.IsNullOrWhiteSpace(fromLauncher)
                ? active.Where(id => !VanillaMapHosts.Contains(id))
                : fromLauncher.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());

            MapProvider found = null;
            foreach (string id in candidates)
            {
                string modulePath;
                try { modulePath = ModuleHelper.GetModuleFullPath(id); }
                catch { continue; }
                if (string.IsNullOrEmpty(modulePath)) continue;

                string full = Path.GetFullPath(modulePath);
                if (File.Exists(Path.Combine(full, "SceneObj", "Main_map", "scene.xscene")))
                {
                    // Later modules override Main_map, so the last match is the one that is loaded.
                    found = new MapProvider { ModuleId = id, ModulePath = modulePath, FullPath = full };
                }
            }
            return found;
        }

        public static bool IsModuleActive(string moduleId)
        {
            try
            {
                return (Utilities.GetModulesNames() ?? new string[0])
                    .Any(m => string.Equals(m, moduleId, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
    }

    internal static class DistanceCachePatch
    {
        public static void Prefix(ref string path)
        {
            try
            {
                MapProvider provider = MapProvider.Resolve();
                if (provider == null)
                {
                    VanillaCacheSanity.Check(path);
                    return;
                }

                WorldGuard.Verify(provider);   // stops the server on an incompatible world

                if (Env.IsSet("AD1259COOP_DISABLE_CACHE_REDIRECT"))
                {
                    Log.Info("distance-cache redirect disabled by AD1259COOP_DISABLE_CACHE_REDIRECT.");
                }
                else
                {
                    string redirected = CompatLogic.ComputeCacheRedirect(path, provider.ModulePath);
                    if (redirected != null)
                    {
                        Log.Info("distance cache: " + path + " -> " + redirected + " (map provider " + provider.ModuleId + ")");
                        path = redirected;
                    }
                    else if (CompatLogic.IsSandBoxDistanceCachePath(path))
                    {
                        Log.Warn(provider.ModuleId + " provides Main_map but has no ModuleData/DistanceCaches/" +
                                 Path.GetFileName(path) + "; the vanilla SandBox cache will be used and may not match the map.");
                    }
                }

                FordingParity.TryInstall();
            }
            catch (Exception e)
            {
                Log.Error("distance-cache prefix failed (original path kept): " + e);
            }
        }
    }

    internal static class WorldGuard
    {
        public static void Verify(MapProvider provider)
        {
            bool createdByPatch = FreshCampaign.Active || FreshCampaign.Completed;
            ICollection<string> providerSettlements =
                CompatLogic.ReadModuleXmlIds(provider.FullPath, "Settlements", "Settlement");
            ICollection<string> providerKingdoms =
                CompatLogic.ReadModuleXmlIds(provider.FullPath, "Kingdoms", "Kingdom");

            var worldSettlements = new List<string>();
            int broken = 0;
            foreach (Settlement s in Settlement.All)
            {
                if (s == null) continue;
                worldSettlements.Add(s.StringId);
                if (s.SettlementComponent == null) broken++;
            }
            var worldKingdoms = Kingdom.All.Where(k => k != null).Select(k => k.StringId).ToList();

            WorldCheckResult r = CompatLogic.EvaluateWorld(
                worldSettlements, broken, worldKingdoms, providerSettlements, providerKingdoms, provider.ModuleId);
            Log.Info(r.Summary);

            if (createdByPatch)
            {
                // This world is being built from the map provider's own XML, so it cannot be the vanilla template;
                // the numbers above are only logged.
                if (r.Verdict != WorldVerdict.Ok)
                {
                    Log.Warn("this campaign is being created on " + provider.ModuleId + "'s map by the patch; the check " +
                             "above is informational only: " + (r.Message ?? r.Verdict.ToString()));
                }
                return;
            }

            if (r.Verdict == WorldVerdict.Warn)
            {
                Log.Warn(r.Message);
                return;
            }
            if (r.Verdict != WorldVerdict.Fatal) return;

            if (Env.IsSet("AD1259COOP_SKIP_WORLD_CHECK"))
            {
                Log.Warn("world check failed but AD1259COOP_SKIP_WORLD_CHECK=1, continuing: " + r.Message);
                return;
            }

            Log.Error(r.Message);
            Log.Error("Stopping the server with exit code 2 instead of letting the engine crash (0xC0000005).");
            Log.Flush();
            Environment.Exit(2);
        }
    }

    /// <summary>Warns when SandBox's vanilla cache was overwritten by a custom map's cache.</summary>
    internal static class VanillaCacheSanity
    {
        public static void Check(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (!File.Exists(full) || !CompatLogic.IsSandBoxDistanceCachePath(full)) return;

                string modulesRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(full), "..", "..", ".."));
                long length = new FileInfo(full).Length;
                string hash = null;
                foreach (string dir in Directory.GetDirectories(modulesRoot))
                {
                    if (string.Equals(Path.GetFileName(dir), "SandBox", StringComparison.OrdinalIgnoreCase)) continue;
                    string other = Path.Combine(dir, "ModuleData", "DistanceCaches", Path.GetFileName(full));
                    if (!File.Exists(other) || new FileInfo(other).Length != length) continue;

                    hash = hash ?? CompatLogic.Sha256(full);
                    if (hash == CompatLogic.Sha256(other))
                    {
                        Log.Error("SandBox/ModuleData/DistanceCaches/" + Path.GetFileName(full) + " is a copy of " +
                                  Path.GetFileName(dir) + "'s cache. The vanilla map will not match it. Restore the " +
                                  "original SandBox file (re-validate the server files) before hosting a vanilla-map profile.");
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("vanilla cache sanity check skipped: " + e.Message);
            }
        }
    }

    /// <summary>Applies AD1259's "navmesh face id 9 = Fording" rule to the server's own map scene.</summary>
    internal static class FordingParity
    {
        private const int FordingFaceId = 9;
        private static bool _done;
        private static object _lastInstance;
        private static Scene _lastScene;

        public static void TryInstall()
        {
            if (_done) return;
            try
            {
                if (Env.IsSet("AD1259COOP_DISABLE_FORDING_PARITY")) { _done = true; return; }
                if (!MapProvider.IsModuleActive("AD1259")) { _done = true; return; }

                IMapScene wrapper = Campaign.Current == null ? null : Campaign.Current.MapSceneWrapper;
                if (wrapper == null) return;   // not created yet, retried later
                _done = true;

                Type type = wrapper.GetType();
                for (Type t = type; t != null; t = t.BaseType)
                {
                    if (t.FullName == "SandBox.MapScene")
                    {
                        Log.Info("map scene is SandBox.MapScene; AD1259's own fording patch already applies.");
                        return;
                    }
                }

                InterfaceMapping map = type.GetInterfaceMap(typeof(IMapScene));
                MethodInfo target = null;
                for (int i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    if (map.InterfaceMethods[i].Name == "GetFaceTerrainType") target = map.TargetMethods[i];
                }
                if (target == null)
                {
                    Log.Warn("server map scene " + type.FullName + " has no GetFaceTerrainType; fording parity skipped.");
                    return;
                }
                if (FindScene(wrapper) == null)
                {
                    Log.Warn("server map scene " + type.FullName + " exposes no Scene; fording parity skipped.");
                    return;
                }

                CompatSubModule.HarmonyInstance.Patch(target,
                    postfix: new HarmonyMethod(typeof(FordingParity), nameof(Postfix)));
                Log.Info("fording parity applied to server map scene " + type.FullName + ".");
            }
            catch (Exception e)
            {
                _done = true;
                Log.Warn("fording parity not applied: " + e.Message);
            }
        }

        public static void Postfix(object __instance, PathFaceRecord __0, ref TerrainType __result)
        {
            if (__result == TerrainType.Fording || !__0.IsValid()) return;
            Scene scene;
            if (ReferenceEquals(__instance, _lastInstance))
            {
                scene = _lastScene;
            }
            else
            {
                scene = FindScene(__instance);
                _lastInstance = __instance;
                _lastScene = scene;
            }
            if (scene != null && scene.GetIdOfNavMeshFace(__0.FaceIndex) == FordingFaceId)
            {
                __result = TerrainType.Fording;
            }
        }

        private static Scene FindScene(object instance)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type t = instance.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (PropertyInfo p in t.GetProperties(flags))
                {
                    if (!typeof(Scene).IsAssignableFrom(p.PropertyType) || p.GetIndexParameters().Length != 0) continue;
                    Scene s = p.GetValue(instance, null) as Scene;
                    if (s != null) return s;
                }
                foreach (FieldInfo f in t.GetFields(flags))
                {
                    if (!typeof(Scene).IsAssignableFrom(f.FieldType)) continue;
                    Scene s = f.GetValue(instance) as Scene;
                    if (s != null) return s;
                }
            }
            return null;
        }
    }

    public enum WorldVerdict { Ok, Warn, Fatal }

    public sealed class WorldCheckResult
    {
        public WorldVerdict Verdict;
        public double SettlementCoverage;
        public double KingdomCoverage;
        public double ForeignSettlementRatio;
        public int BrokenSettlements;
        public string Summary;
        public string Message;
    }

    /// <summary>Engine-independent logic, kept separate so it can be tested outside the game.</summary>
    public static class CompatLogic
    {
        public static bool IsSandBoxDistanceCachePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string normalized = path.Replace('/', '\\');
            return normalized.IndexOf(@"\Modules\SandBox\ModuleData\DistanceCaches\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns the provider's cache path when the server asked for SandBox's cache and the provider
        /// ships a file with the same name; otherwise null (keep the original path).
        /// </summary>
        public static string ComputeCacheRedirect(string originalPath, string providerModulePath)
        {
            if (!IsSandBoxDistanceCachePath(originalPath) || string.IsNullOrEmpty(providerModulePath)) return null;
            string fileName = Path.GetFileName(originalPath.Replace('/', '\\'));
            string basePath = providerModulePath.EndsWith("/") || providerModulePath.EndsWith("\\")
                ? providerModulePath
                : providerModulePath + "/";
            string candidate = basePath + "ModuleData/DistanceCaches/" + fileName;
            return File.Exists(Path.GetFullPath(candidate)) ? candidate : null;
        }

        /// <summary>
        /// Collects the ids of <paramref name="elementName"/> elements from every XML file a module registers
        /// under &lt;XmlName id="<paramref name="xmlNodeId"/>"&gt; (file or folder form).
        /// </summary>
        public static ICollection<string> ReadModuleXmlIds(string moduleFullPath, string xmlNodeId, string elementName)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string subModule = Path.Combine(moduleFullPath, "SubModule.xml");
            if (!File.Exists(subModule)) return ids;

            var paths = new List<string>();
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(subModule, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
            {
                doc.Load(reader);
            }
            foreach (XmlNode node in doc.SelectNodes("/Module/Xmls/XmlNode/XmlName"))
            {
                XmlAttribute id = node.Attributes == null ? null : node.Attributes["id"];
                XmlAttribute path = node.Attributes == null ? null : node.Attributes["path"];
                if (id != null && path != null && string.Equals(id.Value, xmlNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(path.Value);
                }
            }

            foreach (string relative in paths)
            {
                string baseName = Path.Combine(moduleFullPath, "ModuleData", relative.Replace('/', '\\'));
                var files = new List<string>();
                if (File.Exists(baseName + ".xml")) files.Add(baseName + ".xml");
                if (Directory.Exists(baseName)) files.AddRange(Directory.GetFiles(baseName, "*.xml"));
                foreach (string file in files) CollectIds(file, elementName, ids);
            }
            return ids;
        }

        private static void CollectIds(string file, string elementName, HashSet<string> ids)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true };
            using (XmlReader reader = XmlReader.Create(file, settings))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != elementName) continue;
                    string id = reader.GetAttribute("id");
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }
            }
        }

        public static WorldCheckResult EvaluateWorld(
            ICollection<string> worldSettlementIds, int brokenSettlements, ICollection<string> worldKingdomIds,
            ICollection<string> providerSettlementIds, ICollection<string> providerKingdomIds, string providerId)
        {
            var world = new HashSet<string>(worldSettlementIds ?? new string[0], StringComparer.Ordinal);
            var kingdoms = new HashSet<string>(worldKingdomIds ?? new string[0], StringComparer.Ordinal);
            var r = new WorldCheckResult { BrokenSettlements = brokenSettlements, Verdict = WorldVerdict.Ok };

            int providerCount = providerSettlementIds == null ? 0 : providerSettlementIds.Count;
            int covered = providerCount == 0 ? 0 : providerSettlementIds.Count(world.Contains);
            int foreign = providerCount == 0 ? 0 : world.Count(id => !providerSettlementIds.Contains(id));
            int kingdomCount = providerKingdomIds == null ? 0 : providerKingdomIds.Count;
            int kingdomsCovered = kingdomCount == 0 ? 0 : providerKingdomIds.Count(kingdoms.Contains);

            r.SettlementCoverage = providerCount == 0 ? 1.0 : covered / (double)providerCount;
            r.KingdomCoverage = kingdomCount == 0 ? 1.0 : kingdomsCovered / (double)kingdomCount;
            r.ForeignSettlementRatio = world.Count == 0 ? 0.0 : foreign / (double)world.Count;
            double brokenRatio = world.Count == 0 ? 0.0 : brokenSettlements / (double)world.Count;

            r.Summary = string.Format(CultureInfo.InvariantCulture,
                "world check vs {0}: save has {1} settlements ({2} without a settlement component) and {3} kingdoms; " +
                "{4}/{5} of {0}'s settlements and {6}/{7} of its kingdoms are present.",
                providerId, world.Count, brokenSettlements, kingdoms.Count, covered, providerCount, kingdomsCovered, kingdomCount);

            if (world.Count == 0 || providerCount == 0) return r;

            if (r.SettlementCoverage < 0.5 || r.KingdomCoverage < 0.5 || brokenRatio > 0.05)
            {
                r.Verdict = WorldVerdict.Fatal;
                r.Message =
                    "The loaded campaign was not created with " + providerId + ". It looks like Coop's default_new_game " +
                    "template (a vanilla Calradia world), which cannot run on " + providerId + "'s map: its settlements " +
                    "and kingdoms are not in the save and the engine would crash while building the map caches. " +
                    "Fix: in Coodsz Host, start a new campaign for this profile. The compat patch then builds a fresh " +
                    providerId + " world on the server; no single-player save is needed. Otherwise select a save that " +
                    "was created with " + providerId + ".";
            }
            else if (r.ForeignSettlementRatio > 0.10)
            {
                r.Verdict = WorldVerdict.Warn;
                r.Message = string.Format(CultureInfo.InvariantCulture,
                    "{0:P0} of the save's settlements are not defined by {1}. If the server crashes while loading the " +
                    "map, the save probably mixes worlds from different map mods.", r.ForeignSettlementRatio, providerId);
            }
            return r;
        }

        public static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(file))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }

    internal static class Env
    {
        public static bool IsSet(string name)
        {
            return string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);
        }
    }

    internal static class Log
    {
        private const string Prefix = "[AD1259CoopCompat] ";
        private static readonly object Sync = new object();
        private static string _file;

        public static void Info(string message) { Write(message); }
        public static void Warn(string message) { Write("WARNING: " + message); }
        public static void Error(string message) { Write("ERROR: " + message); }

        public static void Flush()
        {
            try { Console.Out.Flush(); } catch { }
        }

        private static void Write(string message)
        {
            string line = Prefix + message;
            try { Console.WriteLine(line); } catch { }
            try { Debug.Print(line); } catch { }
            try
            {
                lock (Sync)
                {
                    if (_file == null)
                    {
                        string root = Environment.GetEnvironmentVariable("BANNERLORD_USER_DIR");
                        string dir = string.IsNullOrEmpty(root) ? AppDomain.CurrentDomain.BaseDirectory : Path.Combine(root, "logs");
                        Directory.CreateDirectory(dir);
                        _file = Path.Combine(dir, "AD1259CoopCompat.log");
                    }
                    File.AppendAllText(_file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                                              "  " + line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
