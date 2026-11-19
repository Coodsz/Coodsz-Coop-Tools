#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace RealmOfThronesCoopCompat
{
    // Realm of Thrones' campaign map as the dedicated server needs it: the module whose Main_map
    // to read, the border (the playable fence, from the scene's border_min and border_max
    // entities) and the terrain size (node dimension x node size, the physical extent the weather
    // grid indexes by). Read from ModuleData\rot_map.txt, which Stage-RotServerMap.ps1 writes from
    // the scene, so a new Realm of Thrones map needs a new file and no new build. No game types,
    // so the launcher tests can run it.
    internal sealed class MapData
    {
        public const string FileName = "rot_map.txt";
        public const string SceneFileName = "scene.xscene";
        public const string NavalPointsFileName = "naval_map_points.txt";
        public const string WindMapFileName = "flowmap.dds";

        private static readonly string[] FloatKeys =
        {
            "border_min_x", "border_min_y", "border_max_x", "border_max_y", "border_max_height", "terrain_size",
        };

        public string Module { get; private set; }
        public float BorderMinX { get; private set; }
        public float BorderMinY { get; private set; }
        public float BorderMaxX { get; private set; }
        public float BorderMaxY { get; private set; }
        public float BorderMaxHeight { get; private set; }
        public float TerrainSize { get; private set; }

        // In the order of the server's extents record: border min x, min y, max x, max y, max
        // height, terrain size.
        public object[] ExtentsValues => new object[] { BorderMinX, BorderMinY, BorderMaxX, BorderMaxY, BorderMaxHeight, TerrainSize };

        public override string ToString() =>
            Module + " border " + Text(BorderMinX) + "," + Text(BorderMinY) + " to " + Text(BorderMaxX) + "," + Text(BorderMaxY)
            + ", max height " + Text(BorderMaxHeight) + ", terrain " + Text(TerrainSize);

        // "key=value" lines, '#' starts a comment. Null with the reason when a value is missing or
        // makes no sense, so a bad file leaves the server as it is instead of half redirected.
        public static MapData Parse(string text, out string error)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                var equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    error = "line '" + line + "' is not key=value";
                    return null;
                }
                values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }

            if (!values.TryGetValue("module", out var module) || module.Length == 0)
            {
                error = "no module";
                return null;
            }
            var numbers = new float[FloatKeys.Length];
            for (var i = 0; i < FloatKeys.Length; i++)
            {
                if (!values.TryGetValue(FloatKeys[i], out var value)
                    || !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i])
                    || float.IsNaN(numbers[i]) || float.IsInfinity(numbers[i]))
                {
                    error = "no number for " + FloatKeys[i];
                    return null;
                }
            }

            var data = new MapData
            {
                Module = module,
                BorderMinX = numbers[0], BorderMinY = numbers[1], BorderMaxX = numbers[2], BorderMaxY = numbers[3],
                BorderMaxHeight = numbers[4], TerrainSize = numbers[5],
            };
            if (data.BorderMinX >= data.BorderMaxX || data.BorderMinY >= data.BorderMaxY)
            {
                error = "the border min is not below its max";
                return null;
            }
            // The game's weather grid takes (int)(position / (terrain size / 32)) with no clamp
            // (DefaultMapWeatherModel.GetNodePositionForWeather). A position at the terrain size
            // gives 32, one past the grid, and a position below zero gives a negative index, so the
            // border has to sit at or above zero and strictly inside the terrain.
            if (data.BorderMinX < 0 || data.BorderMinY < 0)
            {
                error = "the border min is below zero";
                return null;
            }
            if (data.TerrainSize <= 0 || data.BorderMaxX >= data.TerrainSize || data.BorderMaxY >= data.TerrainSize)
            {
                error = "the terrain size " + Text(data.TerrainSize) + " does not cover the border";
                return null;
            }
            error = null;
            return data;
        }

        // The distance caches the server registers: Default, plus Naval and All with War Sails.
        public static string[] CacheNames(bool warSails) => warSails
            ? new[] { CachePrefix + "Default.bin", CachePrefix + "Naval.bin", CachePrefix + "All.bin" }
            : new[] { CachePrefix + "Default.bin" };

        // What the server would trip over in Realm of Thrones' module. The scene must be the
        // stripped copy (the server crashes reading terrain or entities). With War Sails the naval
        // installer reads the points and the wind map from the same folder and stops the load
        // without them. And every cache the server registers must be Realm of Thrones' own, since
        // one from another module would put Calradia's settlements on this map.
        public static List<string> MapFolderProblems(string moduleFolder, bool warSails, Func<string, bool> exists, Func<string, bool> sceneIsStripped)
        {
            var problems = new List<string>();
            var mapFolder = Path.Combine(moduleFolder, "SceneObj", "Main_map");
            foreach (var name in CacheNames(warSails))
            {
                var cache = Path.Combine(moduleFolder, "ModuleData", "DistanceCaches", name);
                if (!exists(cache))
                    problems.Add("no " + cache);
            }
            var scene = Path.Combine(mapFolder, SceneFileName);
            if (!exists(scene))
                problems.Add("no " + scene);
            else if (!sceneIsStripped(scene))
                problems.Add(scene + " still has terrain or entities (run Stage-RotServerMap.ps1)");
            if (warSails)
            {
                foreach (var name in new[] { NavalPointsFileName, WindMapFileName })
                {
                    var path = Path.Combine(mapFolder, name);
                    if (!exists(path))
                        problems.Add("no " + path);
                }
            }
            return problems;
        }

        // A stripped scene has no terrain node and no entities. Reads up to the first one, so the
        // full 23 MB scene is turned away early.
        public static bool SceneIsStripped(TextReader scene)
        {
            using (var reader = XmlReader.Create(scene, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && (reader.Name == "terrain" || reader.Name == "game_entity"))
                        return false;
                }
            }
            return true;
        }

        private const string CachePrefix = "settlements_distance_cache_";

        // The settlement distance cache to read instead, or null. With War Sails off the server
        // takes the first module that ships a cache, which is SandBox, and SandBox's only knows
        // Calradia. Realm of Thrones ships its own for every navigation type, so any cache file
        // the server picks from another module is swapped for Realm of Thrones' file of the same
        // name, when it has one.
        public static string CacheRedirect(string path, string rotModuleFolder, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(rotModuleFolder))
                return null;
            var name = Path.GetFileName(path);
            if (!name.StartsWith(CachePrefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                return null;
            var rot = Path.Combine(rotModuleFolder, "ModuleData", "DistanceCaches", name);
            if (SamePath(path, rot) || !exists(rot))
                return null;
            return rot;
        }

        // The distance cache prefix's decision, with its log lines once per cache name. It swaps
        // nothing until the map scene is patched: Realm of Thrones' caches on War Sails' map are
        // exactly the native crash this module is here to stop.
        internal sealed class CachePick
        {
            private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public string Pick(bool mapPatched, string path, string rotModuleFolder, Func<string, bool> exists, Action<string> log)
            {
                if (!mapPatched)
                    return null;
                var picked = CacheRedirect(path, rotModuleFolder, exists);
                var name = Path.GetFileName(path ?? "");
                if (picked != null)
                {
                    LogOnce(name, "Map redirect: the server picked distance cache " + path + ", reading " + picked + " instead.", log);
                    return picked;
                }
                if (IsOtherModuleCache(path, rotModuleFolder))
                    LogOnce(name, "Map redirect: Realm of Thrones has no " + name + ", so the server reads " + path
                        + ". Its settlements may not match Realm of Thrones' map.", log);
                return null;
            }

            private void LogOnce(string name, string line, Action<string> log)
            {
                lock (_logged)
                {
                    if (!_logged.Add(name))
                        return;
                }
                log(line);
            }
        }

        // A settlement distance cache file from any module but Realm of Thrones'.
        public static bool IsOtherModuleCache(string path, string rotModuleFolder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(rotModuleFolder))
                return false;
            var name = Path.GetFileName(path);
            return name.StartsWith(CachePrefix, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                && !SamePath(path, Path.Combine(rotModuleFolder, "ModuleData", "DistanceCaches", name));
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        public static string Text(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
