using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

public class DedicatedServerFinderTests
{
    [Fact]
    public void Each_server_location_resolves_to_that_exe()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-paths-" + Guid.NewGuid().ToString("N"));
        var cDrive = Path.Combine(root, "Program Files (x86)", "Steam");
        var dDrive = Path.Combine(root, "SteamLibrary");
        var game = DedicatedServerFinder.GameExecutable(dDrive);
        var workshop = DedicatedServerFinder.WorkshopExecutable(dDrive, DedicatedServerFinder.CoopWorkshopId);
        var otherWorkshop = DedicatedServerFinder.WorkshopExecutable(dDrive, "1111111111");
        var picked = Path.Combine(root, "custom", "DedicatedServer", "BannerlordCoopServer.exe");
        var missingC = DedicatedServerFinder.GameExecutable(cDrive);
        try
        {
            WriteExe(workshop);
            Assert.Equal(workshop, DedicatedServerFinder.Find(new[] { cDrive, dDrive }));
            Assert.False(File.Exists(missingC));

            WriteExe(game);
            Assert.Equal(game, DedicatedServerFinder.Find(new[] { dDrive }));

            File.Delete(game);
            File.Delete(workshop);
            WriteExe(otherWorkshop);
            Assert.Equal(otherWorkshop, DedicatedServerFinder.Find(new[] { "", dDrive }));

            WriteExe(picked);
            Assert.Equal(picked, DedicatedServerFinder.Resolve(picked, new[] { dDrive }));
            Assert.Equal(otherWorkshop, DedicatedServerFinder.Resolve(missingC, new[] { dDrive }));
            Assert.Null(DedicatedServerFinder.Find(new[] { cDrive }));
            Assert.Null(DedicatedServerFinder.ExeInFolder("  "));
            Assert.Equal(picked, DedicatedServerFinder.ExeInFolder(Path.GetDirectoryName(picked)));
            Assert.Equal(picked, DedicatedServerFinder.ExeInFolder(Path.GetDirectoryName(Path.GetDirectoryName(picked))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_library_vdf_on_another_drive_finds_the_workshop_server()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-vdf-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(root, "SteamLibrary");
        var workshop = DedicatedServerFinder.WorkshopExecutable(library, DedicatedServerFinder.CoopWorkshopId);
        WriteExe(workshop);
        try
        {
            var steam = Path.Combine(root, "Program Files (x86)", "Steam");
            var vdf = "\"path\" \"" + library.Replace(@"\", @"\\") + "\"";
            var libraries = DedicatedServerFinder.ReadLibraries(steam, vdf);
            Assert.Contains(library, libraries);
            Assert.Equal(workshop, DedicatedServerFinder.Find(libraries));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_c_drive_server_folder_is_not_created()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-missing-" + Guid.NewGuid().ToString("N"));
        var missing = DedicatedServerFinder.GameExecutable(Path.Combine(root, "Program Files (x86)", "Steam"));
        var folder = Path.GetDirectoryName(missing)!;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => ModuleCatalog.Write(folder, new List<ModuleRow>()));
            Assert.Equal(DedicatedServerFinder.MissingMessage, error.Message);
            Assert.False(Directory.Exists(folder));
            Assert.False(File.Exists(Path.Combine(folder, "modded-server-modules.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteExe(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A real server folder has its engine folder beside the exe (CoopInstall.IsServerFolder).
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "engine"));
        File.WriteAllText(path, "server");
    }
}
