#nullable disable
using System;
using System.Collections.Generic;
using System.IO;

namespace BurningEmpiresCoopCompat
{
    // FreshCampaign's plain rules, without game types, so Coodsz Host's tests can run them.
    internal static class FreshCampaignRules
    {
        public const string BurningEmpiresModuleId = "BurningEmpires";
        public const string TemplateFileName = "default_new_game.sav";

        // The Coop dedicated server runs from bin\Win64_Shipping_Server, players' PCs from
        // bin\Win64_Shipping_Client.
        public static bool IsServerBinFolder(string folder) =>
            !string.IsNullOrEmpty(folder)
            && folder.IndexOf("Win64_Shipping_Server", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool HasModule(IEnumerable<string> modules, string id)
        {
            if (modules == null)
                return false;
            foreach (var module in modules)
            {
                if (string.Equals(module, id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // Where the dedicated server keeps default_new_game.sav: next to its exe, in its Game
        // Saves folder and next to the save itself. The save's own path is left out, so a save
        // named default_new_game that already holds a campaign never counts as its own template.
        public static List<string> TemplateCandidates(string savePath, params string[] folders)
        {
            var result = new List<string>();
            var saveFull = FullPath(savePath);
            var all = new List<string>(folders ?? new string[0]);
            if (!string.IsNullOrEmpty(savePath))
                all.Add(Path.GetDirectoryName(savePath));
            foreach (var folder in all)
            {
                if (string.IsNullOrEmpty(folder))
                    continue;
                var candidate = FullPath(Path.Combine(folder, TemplateFileName));
                if (candidate == null || string.Equals(candidate, saveFull, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!result.Exists(c => string.Equals(c, candidate, StringComparison.OrdinalIgnoreCase)))
                    result.Add(candidate);
            }
            return result;
        }

        // A save is the server's untouched copy of the template when it has no Burning Empires
        // module in its metadata and its bytes match one of the template files. Once the server
        // has saved a Burning Empires campaign under that name, both tests fail and it loads.
        public static bool IsTemplateCopy(IEnumerable<string> saveModules, string savePath, IEnumerable<string> templates)
        {
            if (HasModule(saveModules, BurningEmpiresModuleId) || string.IsNullOrEmpty(savePath) || !File.Exists(savePath))
                return false;
            foreach (var template in templates ?? new string[0])
            {
                if (SameBytes(savePath, template))
                    return true;
            }
            return false;
        }

        public static bool SameBytes(string first, string second)
        {
            try
            {
                if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second) || !File.Exists(first) || !File.Exists(second))
                    return false;
                if (new FileInfo(first).Length != new FileInfo(second).Length)
                    return false;
                using (var a = File.OpenRead(first))
                using (var b = File.OpenRead(second))
                {
                    var bufferA = new byte[81920];
                    var bufferB = new byte[81920];
                    while (true)
                    {
                        var readA = ReadFull(a, bufferA);
                        var readB = ReadFull(b, bufferB);
                        if (readA != readB)
                            return false;
                        if (readA == 0)
                            return true;
                        for (var i = 0; i < readA; i++)
                        {
                            if (bufferA[i] != bufferB[i])
                                return false;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static int ReadFull(Stream stream, byte[] buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                    break;
                total += read;
            }
            return total;
        }

        private static string FullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            try { return Path.GetFullPath(path); }
            catch { return null; }
        }
    }
}
