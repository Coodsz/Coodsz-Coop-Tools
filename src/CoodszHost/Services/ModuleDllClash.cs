using System.IO;
using System.Security.Cryptography;
using System.Xml;

namespace CoodszHost.Services;

public static class ModuleDllClash
{
    public static IReadOnlyList<string> Warnings(string modulesRoot, IEnumerable<string> enabledIds)
    {
        var enabled = new HashSet<string>(enabledIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        if (enabled.Count == 0 || !Directory.Exists(modulesRoot))
            return Array.Empty<string>();

        var folders = ChosenFolders(modulesRoot);
        var copies = new Dictionary<string, List<(string Module, string Relative, string Hash)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in folders)
        {
            if (!enabled.Contains(pair.Key))
                continue;
            // Coop ships its own runtime (0Harmony, Mono.Cecil, ...) and the
            // server checks that module. Never tell anyone to move files out of it.
            if (pair.Key.Equals("Coop", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var dll in Dlls(pair.Value))
            {
                if (!copies.TryGetValue(dll.Name, out var list))
                {
                    list = new List<(string, string, string)>();
                    copies[dll.Name] = list;
                }

                if (list.Any(item => item.Module.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add((pair.Key, dll.Relative, dll.Hash));
            }
        }

        var lines = new List<string>();
        string? ownedFile = null;
        string? ownedModule = null;
        string? extraModule = null;
        foreach (var pair in copies.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var different = pair.Value
                .GroupBy(item => item.Hash, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (different.Count < 2)
                continue;
            var first = different[0].First();
            var second = different[1].First();
            lines.Add("Different copies of " + pair.Key + ". " + first.Module + ": " + first.Relative + ". " + second.Module + ": " + second.Relative + ".");
            if (ownedFile == null && KnownOwner(pair.Key) is string owner)
            {
                var extra = first.Module.Equals(owner, StringComparison.OrdinalIgnoreCase) ? second.Module
                    : second.Module.Equals(owner, StringComparison.OrdinalIgnoreCase) ? first.Module
                    : null;
                if (extra != null)
                {
                    ownedFile = pair.Key;
                    ownedModule = owner;
                    extraModule = extra;
                }
            }

            if (lines.Count == 4)
                break;
        }

        if (lines.Count == 0)
            return lines;
        lines.Add(ownedFile == null
            ? "What to do: Keep one copy of each file named above. Move the extra file out of one mod's bin folder and leave the other copy where it is. Then press Start server again."
            : "What to do: Keep " + ownedFile + " in " + ownedModule + ". Move the extra copy out of " + extraModule + "'s bin folder. Then press Start server again.");
        lines.Add("What not to do: Do not delete the mod folder. Do not turn the locked mods off. The server still starts with both copies, and the game may load the wrong one.");
        return lines;
    }

    private static string? KnownOwner(string fileName)
    {
        return fileName.ToLowerInvariant() switch
        {
            "0harmony.dll" => "Bannerlord.Harmony",
            "mcmv5.dll" => "Bannerlord.MBOptionScreen",
            "butterlib.dll" => "Bannerlord.ButterLib",
            "uiextenderex.dll" => "Bannerlord.UIExtenderEx",
            _ => null
        };
    }

    private static Dictionary<string, string> ChosenFolders(string modulesRoot)
    {
        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Directory.EnumerateDirectories(modulesRoot))
        {
            var id = ReadId(Path.Combine(folder, "SubModule.xml"));
            if (id == null || !ModuleCatalog.IsSafeModuleId(id))
                continue;
            if (!chosen.TryGetValue(id, out var current) || Better(folder, current, id))
                chosen[id] = folder;
        }

        return chosen;
    }

    private static bool Better(string candidate, string current, string moduleId)
    {
        static int Score(string folder, string id)
        {
            var name = Path.GetFileName(folder);
            if (name.Equals(id, StringComparison.OrdinalIgnoreCase))
                return 2;
            if (name.Contains("playtest", StringComparison.OrdinalIgnoreCase)
                || name.Contains("kept", StringComparison.OrdinalIgnoreCase)
                || name.Contains("backup", StringComparison.OrdinalIgnoreCase)
                || name.Contains("-old", StringComparison.OrdinalIgnoreCase))
                return 0;
            return 1;
        }

        return Score(candidate, moduleId) > Score(current, moduleId);
    }

    private static IEnumerable<(string Name, string Relative, string Hash)> Dlls(string moduleFolder)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bin in new[] { "Win64_Shipping_Server", "Win64_Shipping_Client" })
        {
            var directory = Path.Combine(moduleFolder, "bin", bin);
            if (!Directory.Exists(directory))
                continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.dll"))
            {
                var name = Path.GetFileName(path);
                if (!seen.Add(name))
                    continue;
                yield return (name, "bin\\" + bin + "\\" + name, Hash(path));
            }
        }
    }

    private static string Hash(string path) => BannerlordCoopLauncher.Core.IO.FileHash.Sha256(path);

    private static string? ReadId(string manifest)
    {
        if (!File.Exists(manifest))
            return null;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true
            };
            using var reader = XmlReader.Create(manifest, settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "Id")
                    return reader.GetAttribute("value")?.Trim();
            }
        }
        catch (XmlException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
