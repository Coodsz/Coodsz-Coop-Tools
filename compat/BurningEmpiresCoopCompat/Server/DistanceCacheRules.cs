#nullable disable
using System;
using System.Collections.Generic;
using System.IO;

namespace BurningEmpiresCoopCompat
{
    // DistanceCacheRedirect's plain rules, without game types, so Coodsz Host's tests can run them.
    internal static class DistanceCacheRules
    {
        public static readonly string CacheFolder = Path.Combine("ModuleData", "DistanceCaches");

        // True when the file sits in that module's ModuleData\DistanceCaches folder.
        public static bool IsInModuleCaches(string path, string moduleFolder)
        {
            var folder = FullPath(string.IsNullOrEmpty(moduleFolder) ? null : Path.Combine(moduleFolder, CacheFolder));
            var file = FullPath(path);
            return folder != null && file != null && SamePath(Path.GetDirectoryName(file), folder);
        }

        // The cache file the game itself would read: the last active module, in load order, that
        // has ModuleData\DistanceCaches\<same file name>. Null when no module has it.
        public static string PickLast(string fileName, IEnumerable<string> activeModuleFolders, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(fileName) || activeModuleFolders == null)
                return null;
            exists = exists ?? File.Exists;
            string picked = null;
            foreach (var folder in activeModuleFolders)
            {
                if (string.IsNullOrEmpty(folder))
                    continue;
                var candidate = FullPath(Path.Combine(folder, CacheFolder, fileName));
                if (candidate != null && exists(candidate))
                    picked = candidate;
            }
            return picked;
        }

        // The path the server should read instead of the one it asked for, or null to keep it:
        // only a file in SandBox's own cache folder is redirected, and only to another module's.
        public static string Redirect(string requestedPath, string sandBoxFolder, IEnumerable<string> activeModuleFolders, Func<string, bool> exists)
        {
            if (!IsInModuleCaches(requestedPath, sandBoxFolder))
                return null;
            var picked = PickLast(Path.GetFileName(requestedPath), activeModuleFolders, exists);
            return picked == null || SamePath(picked, FullPath(requestedPath)) ? null : picked;
        }

        public static bool SamePath(string first, string second) =>
            first != null && second != null
            && string.Equals(Trim(first), Trim(second), StringComparison.OrdinalIgnoreCase);

        private static string Trim(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string FullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            try { return Path.GetFullPath(path); }
            catch { return null; }
        }
    }
}
