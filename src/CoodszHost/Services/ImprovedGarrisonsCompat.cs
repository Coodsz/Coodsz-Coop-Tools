using System.IO;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost.Services;

/// <summary>
/// Improved Garrisons Coop Compat (DedicatedServer.IGCoopCompat, compat\ImprovedGarrisonsCoopCompat):
/// a server only patch module that keeps the players' Improved Garrisons town settings on the
/// dedicated server and lets only a town's owner change them through CoopModPatch. Shipped next to
/// CoodszHost.exe (PatchModules\DedicatedServer.IGCoopCompat). A start whose mods include
/// ImprovedGarrisons copies it into DedicatedServer\engine\Modules and adds it last, after
/// ImprovedGarrisons and CoopModPatch. Coop leaves DedicatedServer.* modules out of the client check,
/// so players do not need it, and it is never in the Modules list. The AD1259 build leaves it out.
/// </summary>
public static class ImprovedGarrisonsCompat
{
    public const string ModuleId = "DedicatedServer.IGCoopCompat";
    public const string TargetModuleId = "ImprovedGarrisons";
    public const string FolderName = "PatchModules";
    public const string Name = "Improved Garrisons Coop Compat";
    public const string Version = "v1.0.0";
    public const string AddedNote = "Added Improved Garrisons Coop Compat to the server, so players keep their Improved Garrisons town settings and only a town's owner can change them. Players do not need it.";

    public static bool Enabled => !Edition.IsAD1259;

    public static string PayloadRoot(string? baseDirectory = null) =>
        baseDirectory == null ? Path.Combine(ToolPayload.Folder(FolderName), ModuleId) : Path.Combine(baseDirectory, FolderName, ModuleId);

    /// <summary>True when this start's turned on mods include Improved Garrisons.</summary>
    public static bool Applies(IEnumerable<ModuleRow> rows) =>
        rows.Any(row => row.Enabled && row.Id.Equals(TargetModuleId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Copies the shipped module into the server when a file is missing or different. Returns a line
    /// for the server log when something was copied, or null (also when the payload is missing, so a
    /// broken install never stops a start). A file in use by a server that still runs is left for the
    /// next Start.
    /// </summary>
    public static string? Install(string serverRoot, string? payloadRoot = null)
    {
        payloadRoot ??= PayloadRoot();
        if (!File.Exists(Path.Combine(payloadRoot, "SubModule.xml")))
            return null;

        var target = Path.Combine(ModuleCatalog.ModulesRoot(serverRoot), ModuleId);
        var copied = 0;
        foreach (var source in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(payloadRoot, source));
            try
            {
                if (File.Exists(destination) && SameBytes(source, destination))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use by a server that is still running; the next Start copies it.
            }
        }

        return copied == 0 ? null : Translator.T(AddedNote);
    }

    /// <summary>True when the module is in the server's Modules folder, so a start may list it.</summary>
    public static bool Installed(string serverRoot) =>
        File.Exists(Path.Combine(ModuleCatalog.ModulesRoot(serverRoot), ModuleId, "SubModule.xml"));

    /// <summary>
    /// This start's module list with the compat module added last (after ImprovedGarrisons and
    /// CoopModPatch) when Improved Garrisons is turned on, once; without it the module is left out.
    /// </summary>
    public static List<ModuleRow> WithCompat(IEnumerable<ModuleRow> rows)
    {
        var list = rows.Where(row => !row.Id.Equals(ModuleId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (Applies(list))
            list.Add(new ModuleRow { Id = ModuleId, Name = Name, Version = Version, Enabled = true });
        return list;
    }

    private static bool SameBytes(string a, string b)
    {
        var left = new FileInfo(a);
        var right = new FileInfo(b);
        return left.Length == right.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
