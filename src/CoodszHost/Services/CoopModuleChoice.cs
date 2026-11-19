using System.IO;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Modules;

namespace CoodszHost.Services;

public static class CoopModuleChoice
{
    public const string ParkedFile = "SubModule.xml.coodsz-off";

    public static IReadOnlyList<string> KeepOne(string modulesRoot)
    {
        if (!Directory.Exists(modulesRoot))
            return [];

        var folders = new List<(string Directory, string Id)>();
        foreach (var directory in Directory.EnumerateDirectories(modulesRoot))
        {
            var id = ReadLiveOrParkedId(directory);
            if (BannerlordCoopModules.IsEither(id))
                folders.Add((directory, id!));
        }

        if (folders.Count == 0)
            return [];

        var keep = BannerlordCoopModules.Keep(folders.Select(folder => folder.Id));
        var parked = new List<string>();
        foreach (var folder in folders)
        {
            var xml = Path.Combine(folder.Directory, "SubModule.xml");
            var off = Path.Combine(folder.Directory, ParkedFile);
            if (folder.Id.Equals(keep, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(xml) && File.Exists(off))
                    File.Move(off, xml);
                continue;
            }

            if (!File.Exists(xml))
                continue;
            if (File.Exists(off))
                File.Delete(off);
            File.Move(xml, off);
            parked.Add(folder.Id);
        }

        return parked;
    }

    private static string? ReadLiveOrParkedId(string directory)
    {
        var xml = Path.Combine(directory, "SubModule.xml");
        if (!File.Exists(xml))
            xml = Path.Combine(directory, ParkedFile);
        if (!File.Exists(xml))
            return null;
        var match = Regex.Match(File.ReadAllText(xml), "<Id\\s+value=\"(?<id>[^\"]+)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["id"].Value : null;
    }
}
