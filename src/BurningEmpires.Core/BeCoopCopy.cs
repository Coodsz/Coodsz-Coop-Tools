using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>
/// The Coop line Check mods and Connect write to the log. Coop from the Steam Workshop is the
/// normal setup, so no Coop in the game's Modules folder is fine then; live run 2 logged "was not
/// found" while the module list showed Coop v0.1.6 (Workshop).
/// </summary>
public static class BeCoopCopy
{
    public const string FromWorkshop = "Bannerlord Coop is used from the Steam Workshop. It does not need a copy in the game's Modules folder.";

    public static string Describe(string modulesDirectory, string? workshopCoop)
    {
        var line = CoopStaging.DescribeCopies(modulesDirectory);
        return line == CoopStaging.NotInModules && workshopCoop != null ? FromWorkshop : line;
    }

    /// <summary>The marker in the launcher's data folder that lists the Coop folders it set aside (CoopParking, Tool BE).</summary>
    public const string SetAsideMarkerFileName = "be-coop-set-aside.json";

    /// <summary>
    /// With a Workshop Coop, sets every Coop and CoopNightly in the game's Modules folder aside in
    /// the game folder (Name-kept-version, where Restore parked Coop finds them), except one Coop
    /// with the Workshop copy's version or a newer one. They go back into Modules when the launcher
    /// closes (CoopParking.PutBack). CoopStaging.ParkExtraCoop keeps a Coop 0.1.6
    /// because TAOM pins it. Burning Empires follows the newest Coop, and the game loads the Modules
    /// copy first, so a stale 0.1.6 there would win once the Workshop Coop updates, while a newer one
    /// (Coop 0.1.7 unzipped by hand from GitHub before it reached the Workshop) is the one to run.
    /// </summary>
    public static IReadOnlyList<string> ParkBesideWorkshop(string gameRoot, string workshopModule, string markerPath) =>
        CoopParking.SetAside(gameRoot, markerPath, Extra(gameRoot, WorkshopVersion(workshopModule)), CoopParking.BeTool);

    /// <summary>
    /// With no Workshop Coop, the Coop in Modules is the only one, so it stays: one Coop is kept (the newest
    /// with id Coop, else a CoopNightly) and only extra copies are set aside, the same way.
    /// CoopStaging.ParkDuplicateCoop kept a Coop 0.1.6 over a newer one because TAOM pins 0.1.6.
    /// </summary>
    public static IReadOnlyList<string> ParkWithoutWorkshop(string gameRoot, string markerPath) =>
        CoopParking.SetAside(gameRoot, markerPath, Extra(gameRoot, workshopVersion: null), CoopParking.BeTool);

    // The Coop folders the two Park methods set aside.
    private static IReadOnlyList<CoopStaging.CoopFolder> Extra(string gameRoot, string? workshopVersion)
    {
        var modules = Path.Combine(gameRoot, "Modules");
        if (!Directory.Exists(modules))
            return [];
        var found = Directory.EnumerateDirectories(modules)
            .Select(directory => (Directory: directory, Name: Path.GetFileName(directory), Identity: ModulesIdentity(directory)))
            .Where(folder => folder.Identity.Id.Equals(BeStack.CoopModuleId, StringComparison.OrdinalIgnoreCase)
                || folder.Identity.Id.Equals(BeStack.LegacyCoopModuleId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var coop = found
            .Where(folder => folder.Identity.Id.Equals(BeStack.CoopModuleId, StringComparison.OrdinalIgnoreCase)
                && (workshopVersion == null
                    || folder.Identity.Version.Equals(workshopVersion, StringComparison.OrdinalIgnoreCase)
                    || CoopInstall.IsNewer(folder.Identity.Version, workshopVersion)))
            .ToList();
        var keep = (coop.Count > 0 || workshopVersion != null ? coop : found)
            .OrderByDescending(folder => folder.Identity.Version, Comparer<string>.Create(CoopInstall.Compare))
            .ThenBy(folder => folder.Name.Equals("Coop", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .Select(folder => folder.Directory)
            .FirstOrDefault();

        return found
            .Where(folder => !folder.Directory.Equals(keep, StringComparison.OrdinalIgnoreCase))
            .Select(folder => new CoopStaging.CoopFolder(folder.Directory, folder.Name, folder.Identity.Id, folder.Identity.Version))
            .ToList();
    }

    /// <summary>
    /// The Workshop Coop's version. Without it every Modules Coop would look stale and be moved, a good
    /// one too, so a Workshop SubModule.xml that is missing, locked or has no version stops here.
    /// </summary>
    public static string WorkshopVersion(string workshopModule)
    {
        string version;
        try
        {
            version = Identity(workshopModule).Version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BeCoopCopyException(WorkshopUnreadable);
        }

        return version.Length > 0 ? version : throw new BeCoopCopyException(WorkshopUnreadable);
    }

    public const string WorkshopUnreadable =
        "The Steam Workshop Coop's SubModule.xml could not be read, so its version is not known. No Coop folder was moved out of Modules. Let Steam finish the Coop download, or unsubscribe from Workshop item 3770450698 and subscribe again, then try again.";

    // A Modules folder whose SubModule.xml cannot be read is not taken for a Coop and stays where it is.
    private static ModuleIdentity ModulesIdentity(string module)
    {
        try
        {
            return Identity(module);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ModuleIdentity("", "");
        }
    }

    private static ModuleIdentity Identity(string module)
    {
        var xml = Path.Combine(module, "SubModule.xml");
        return File.Exists(xml) ? ModuleIdentityReader.Read(xml) : new ModuleIdentity("", "");
    }
}

public sealed class BeCoopCopyException(string message) : Exception(message);
