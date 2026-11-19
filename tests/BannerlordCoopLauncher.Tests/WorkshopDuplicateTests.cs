using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;

namespace BannerlordCoopLauncher.Tests;

public class WorkshopDuplicateTests
{
    [Fact]
    public void The_launcher_never_bundles_a_mod_that_comes_from_the_workshop()
    {
        var profile = new ServerProfile();
        ServerModuleSet.Apply(profile);
        var fromWorkshop = profile.Mods
            .Where(mod => !string.IsNullOrWhiteSpace(mod.WorkshopId) && mod.WorkshopId != "0")
            .Select(mod => mod.ModuleId);
        Assert.Empty(BundledModules.ModuleIds.Intersect(fromWorkshop, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_game_folder_copy_of_a_workshop_mod_is_turned_off_and_coop_is_left_alone()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-dupes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            var workshop = Path.Combine(root, "workshop", "content", "261550");
            var lockWorkshop = Path.Combine(workshop, "3794802607");
            var lockGame = Path.Combine(game, "Modules", "ServerSettingsLock");
            var coopGame = Path.Combine(game, "Modules", "Coop");
            var nexusOnly = Path.Combine(game, "Modules", "BannerFix");
            foreach (var (folder, id) in new[] { (lockWorkshop, "ServerSettingsLock"), (lockGame, "ServerSettingsLock"), (coopGame, "Coop"), (nexusOnly, "BannerFix") })
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module><Id value=\"" + id + "\"/><Version value=\"v1.0.0\"/></Module>");
            }

            var resolution = new ModuleResolution(
                Array.Empty<ResolvedModule>(),
                new[]
                {
                    new ResolvedModule("COOP Server Settings Lock", "ServerSettingsLock", "3794802607", true, false, ModuleState.Ready, "", lockWorkshop),
                    new ResolvedModule("Bannerlord Coop", "Coop", "3770450698", true, false, ModuleState.Ready, "", Path.Combine(workshop, "3770450698")),
                    new ResolvedModule("Banner Fix", "BannerFix", "0", true, false, ModuleState.Ready, "", nexusOnly)
                },
                true);

            var parked = WorkshopDuplicates.ParkGameCopies(game, resolution, new[] { workshop });

            Assert.Single(parked);
            Assert.False(File.Exists(Path.Combine(lockGame, "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(lockGame, WorkshopDuplicates.ParkedFile)));
            Assert.True(File.Exists(Path.Combine(lockWorkshop, "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(coopGame, "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(nexusOnly, "SubModule.xml")));
            Assert.Empty(WorkshopDuplicates.ParkGameCopies(game, resolution, new[] { workshop }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // CR-J5AUW: the Nexus MCM in the game folder is used, so Connect turns off the removed Workshop item's
    // old download (same version too), and turns it back on once the game folder copy is gone.
    [Theory]
    [InlineData("v5.12.3")]
    [InlineData("v5.13.1")]
    public void The_old_workshop_mcm_is_turned_off_while_the_game_folder_mcm_is_used_and_back_on_without_it(string gameVersion)
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-dupes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            var workshop = Path.Combine(root, "workshop", "content", "261550");
            var oldMcm = Path.Combine(workshop, "2859238197");
            var nexusMcm = Path.Combine(game, "Modules", "Bannerlord.MBOptionScreen");
            foreach (var (folder, version) in new[] { (oldMcm, "v5.12.3"), (nexusMcm, gameVersion) })
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module><Id value=\"Bannerlord.MBOptionScreen\"/><Version value=\"" + version + "\"/></Module>");
            }

            var resolution = new ModuleResolution(
                Array.Empty<ResolvedModule>(),
                new[] { new ResolvedModule("Mod Configuration Menu v5", "Bannerlord.MBOptionScreen", "2859238197", true, false, ModuleState.Ready, "", nexusMcm) },
                true);
            var problems = new List<string>();

            Assert.Empty(WorkshopDuplicates.ParkGameCopies(game, resolution, new[] { workshop }, problems));
            Assert.Single(WorkshopDuplicates.ParkRetiredWorkshopCopies(resolution, new[] { workshop }, problems));
            Assert.Empty(problems);
            Assert.False(File.Exists(Path.Combine(oldMcm, "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(oldMcm, WorkshopDuplicates.ParkedFile)));
            Assert.True(File.Exists(Path.Combine(nexusMcm, "SubModule.xml")));
            // While the game folder copy is there, nothing is turned back on.
            Assert.Empty(WorkshopDuplicates.RestoreRetiredWorkshopCopies(game, new[] { workshop }));

            Directory.Delete(nexusMcm, recursive: true);
            Assert.Single(WorkshopDuplicates.RestoreRetiredWorkshopCopies(game, new[] { workshop }));
            Assert.True(File.Exists(Path.Combine(oldMcm, "SubModule.xml")));
            Assert.False(File.Exists(Path.Combine(oldMcm, WorkshopDuplicates.ParkedFile)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Other_retired_items_keep_their_workshop_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-dupes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workshop = Path.Combine(root, "workshop", "content", "261550");
            var lockWorkshop = Path.Combine(workshop, "3794802607");
            var lockGame = Path.Combine(root, "game", "Modules", "ServerSettingsLock");
            foreach (var folder in new[] { lockWorkshop, lockGame })
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module><Id value=\"ServerSettingsLock\"/><Version value=\"v1.0.3\"/></Module>");
            }

            var resolution = new ModuleResolution(
                Array.Empty<ResolvedModule>(),
                new[] { new ResolvedModule("COOP Server Settings Lock", "ServerSettingsLock", "3794802607", true, false, ModuleState.Ready, "", lockGame) },
                true);

            Assert.Empty(WorkshopDuplicates.ParkRetiredWorkshopCopies(resolution, new[] { workshop }, new List<string>()));
            Assert.True(File.Exists(Path.Combine(lockWorkshop, "SubModule.xml")));
            Assert.False(RetiredWorkshopItems.LocalCopyWins("ServerSettingsLock"));
            Assert.True(RetiredWorkshopItems.LocalCopyWins("Bannerlord.MBOptionScreen"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // The Burning Empires client names MCM by id and takes the game folder copy first (its FindModuleXml), so a
    // Nexus MCM in Modules plus the removed Workshop item's download gave the game two MCMs.
    [Fact]
    public void The_burning_empires_client_turns_off_the_old_workshop_mcm_only_while_the_game_folder_has_mcm()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-dupes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            var workshop = Path.Combine(root, "workshop", "content", "261550");
            var oldMcm = Path.Combine(workshop, "2859238197");
            var nexusMcm = Path.Combine(game, "Modules", "Bannerlord.MBOptionScreen");
            WriteMcm(oldMcm, "v5.12.3");
            WriteMcm(nexusMcm, "v5.12.3");
            var problems = new List<string>();

            // A list without MCM leaves it alone.
            Assert.Empty(WorkshopDuplicates.ParkRetiredWorkshopCopies(game, new[] { "Native" }, new[] { workshop }, problems));
            Assert.Single(WorkshopDuplicates.RetiredWorkshopCopies(game, BurningEmpires.Core.BeStack.ClientModules, new[] { workshop }));
            Assert.Equal(new[] { "Bannerlord.MBOptionScreen" }, WorkshopDuplicates.ParkRetiredWorkshopCopies(game, BurningEmpires.Core.BeStack.ClientModules, new[] { workshop }, problems));
            Assert.Empty(problems);
            Assert.False(File.Exists(Path.Combine(oldMcm, "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(oldMcm, WorkshopDuplicates.ParkedFile)));
            Assert.True(File.Exists(Path.Combine(nexusMcm, "SubModule.xml")));
            Assert.Empty(WorkshopDuplicates.RetiredWorkshopCopies(game, BurningEmpires.Core.BeStack.ClientModules, new[] { workshop }));
            Assert.Empty(WorkshopDuplicates.RestoreRetiredWorkshopCopies(game, new[] { workshop }));

            // The game folder copy is gone: the old download is MCM again.
            Directory.Delete(nexusMcm, recursive: true);
            Assert.Single(WorkshopDuplicates.RestoreRetiredWorkshopCopies(game, new[] { workshop }));
            Assert.True(File.Exists(Path.Combine(oldMcm, "SubModule.xml")));
            Assert.False(File.Exists(Path.Combine(oldMcm, WorkshopDuplicates.ParkedFile)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_burning_empires_client_keeps_an_old_workshop_mcm_that_is_the_only_mcm()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-dupes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(game, "Modules", "Native"));
            var workshop = Path.Combine(root, "workshop", "content", "261550");
            var oldMcm = Path.Combine(workshop, "2859238197");
            WriteMcm(oldMcm, "v5.12.3");

            Assert.Empty(WorkshopDuplicates.RetiredWorkshopCopies(game, BurningEmpires.Core.BeStack.ClientModules, new[] { workshop }));
            Assert.Empty(WorkshopDuplicates.ParkRetiredWorkshopCopies(game, BurningEmpires.Core.BeStack.ClientModules, new[] { workshop }, new List<string>()));
            Assert.Empty(WorkshopDuplicates.RestoreRetiredWorkshopCopies(game, new[] { workshop }));
            Assert.True(File.Exists(Path.Combine(oldMcm, "SubModule.xml")));
            Assert.Empty(Directory.GetFiles(oldMcm, WorkshopDuplicates.ParkedFile + "*"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_burning_empires_client_wires_the_old_workshop_mcm_steps()
    {
        var launch = SourceFiles.Read(Path.Combine("src", "BurningEmpiresLauncher", "MainWindow.xaml.cs"));
        var support = SourceFiles.Read(Path.Combine("src", "BurningEmpiresLauncher", "MainWindow.Support.cs"));
        // Turned off on Connect after the open game question, so No leaves the files as they were.
        Assert.Matches(@"GameAlreadyOpen\.ClearAsync\(this, ShowJoin\)\)\s+return;\s+LogServerModuleNotes\(\);\s+ParkRetiredMcm\(\);", launch);
        // Turned back on before the mods are read again.
        Assert.Matches(@"private void RefreshModules\(\)\s+\{\s+RestoreRetiredMcm\(\);\s+ScanInstalledExtras\(\);", launch);
        Assert.Contains("WorkshopDuplicates.ParkRetiredWorkshopCopies(_gameRoot, BeStack.ClientModules, ClientWorkshopFolders(), problems)", support);
        Assert.Contains("WorkshopDuplicates.RestoreRetiredWorkshopCopies(_gameRoot, ClientWorkshopFolders(), problems)", support);
    }

    // A Modules folder that cannot be read is no game copy for a park (the Workshop MCM may be the only MCM),
    // and still a game copy for a restore (nothing is turned back on).
    [Fact]
    public void An_unreadable_modules_folder_turns_nothing_off_and_nothing_back_on()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-dupes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            var workshop = Path.Combine(root, "workshop", "content", "261550");
            var oldMcm = Path.Combine(workshop, "2859238197");
            var parkedMcm = Path.Combine(root, "workshop2", "content", "261550", "2859238197");
            WriteMcm(Path.Combine(game, "Modules", "Bannerlord.MBOptionScreen"), "v5.12.3");
            WriteMcm(oldMcm, "v5.12.3");
            WriteMcm(parkedMcm, "v5.12.3");
            File.Move(Path.Combine(parkedMcm, "SubModule.xml"), Path.Combine(parkedMcm, WorkshopDuplicates.ParkedFile));
            Func<string, string[]> unreadable = _ => throw new UnauthorizedAccessException("Access denied.");
            var problems = new List<string>();

            Assert.Empty(WorkshopDuplicates.ParkRetiredWorkshopCopies(game, BurningEmpires.Core.BeStack.ClientModules, new[] { workshop }, problems, unreadable));
            Assert.True(File.Exists(Path.Combine(oldMcm, "SubModule.xml")));
            Assert.Empty(WorkshopDuplicates.RestoreRetiredWorkshopCopies(game, new[] { Path.GetDirectoryName(parkedMcm)! }, problems, unreadable));
            Assert.True(File.Exists(Path.Combine(parkedMcm, WorkshopDuplicates.ParkedFile)));
            Assert.Empty(problems);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // Steam can put the old download's SubModule.xml back; the next Connect turns it off again and keeps the first parked file.
    [Fact]
    public void Parking_the_old_workshop_mcm_twice_keeps_both_parked_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-dupes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            var workshop = Path.Combine(root, "workshop", "content", "261550");
            var oldMcm = Path.Combine(workshop, "2859238197");
            WriteMcm(Path.Combine(game, "Modules", "Bannerlord.MBOptionScreen"), "v5.12.3");
            WriteMcm(oldMcm, "v5.12.3");
            var problems = new List<string>();

            Assert.Single(WorkshopDuplicates.ParkRetiredWorkshopCopies(game, BurningEmpires.Core.BeStack.ClientModules, new[] { workshop }, problems));
            WriteMcm(oldMcm, "v5.12.3");
            Assert.Single(WorkshopDuplicates.ParkRetiredWorkshopCopies(game, BurningEmpires.Core.BeStack.ClientModules, new[] { workshop }, problems));
            Assert.Empty(problems);
            Assert.False(File.Exists(Path.Combine(oldMcm, "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(oldMcm, WorkshopDuplicates.ParkedFile)));
            Assert.Single(Directory.GetFiles(oldMcm, WorkshopDuplicates.ParkedFile + "-*"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_restore_that_cannot_rename_is_reported()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-dupes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workshop = Path.Combine(root, "workshop", "content", "261550");
            var oldMcm = Path.Combine(workshop, "2859238197");
            WriteMcm(oldMcm, "v5.12.3");
            File.Move(Path.Combine(oldMcm, "SubModule.xml"), Path.Combine(oldMcm, WorkshopDuplicates.ParkedFile));
            var problems = new List<string>();

            using (new FileStream(Path.Combine(oldMcm, WorkshopDuplicates.ParkedFile), FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Empty(WorkshopDuplicates.RestoreRetiredWorkshopCopies(Path.Combine(root, "game"), new[] { workshop }, problems));

            Assert.Contains(problems, line => line.StartsWith("Could not turn the old Workshop copy of Bannerlord.MBOptionScreen back on in ", StringComparison.Ordinal));
            Assert.True(File.Exists(Path.Combine(oldMcm, WorkshopDuplicates.ParkedFile)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // The Friends path: a used MCM that is itself under a Workshop root is not the game folder copy, so nothing is turned off.
    [Fact]
    public void The_friends_path_keeps_the_old_workshop_mcm_when_the_setup_uses_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-dupes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workshop = Path.Combine(root, "workshop", "content", "261550");
            var oldMcm = Path.Combine(workshop, "2859238197");
            WriteMcm(oldMcm, "v5.12.3");
            var resolution = new ModuleResolution(
                Array.Empty<ResolvedModule>(),
                new[] { new ResolvedModule("Mod Configuration Menu v5", "Bannerlord.MBOptionScreen", "2859238197", true, false, ModuleState.Ready, "", oldMcm) },
                true);

            Assert.Empty(WorkshopDuplicates.ParkRetiredWorkshopCopies(resolution, new[] { workshop }, new List<string>()));
            Assert.True(File.Exists(Path.Combine(oldMcm, "SubModule.xml")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteMcm(string folder, string version)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module><Id value=\"Bannerlord.MBOptionScreen\"/><Version value=\"" + version + "\"/></Module>");
    }

    [Fact]
    public void Connect_and_check_mods_wire_the_old_workshop_mcm_steps()
    {
        var launch = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "MainWindow.Launch.cs"));
        var modules = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "MainWindow.Modules.cs"));
        Assert.Contains("WorkshopDuplicates.ParkRetiredWorkshopCopies(_resolution, workshopFolders, parkProblems)", launch);
        Assert.Matches(@"RestoreRetiredCopies\(workshops\);\s+_resolution = ModuleResolver\.Resolve\(_profile, _gameRoot, workshops, remote", launch);
        Assert.Matches(@"RestoreRetiredCopies\(workshops\);\s+_resolution = ModuleResolver\.Resolve\(_profile, _gameRoot, workshops, null", modules);
    }

    [Fact]
    public void A_workshop_mod_that_is_only_in_the_game_folder_is_kept()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-dupes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            var lockGame = Path.Combine(game, "Modules", "ServerSettingsLock");
            Directory.CreateDirectory(lockGame);
            File.WriteAllText(Path.Combine(lockGame, "SubModule.xml"), "<Module><Id value=\"ServerSettingsLock\"/><Version value=\"v1.0.3\"/></Module>");
            var resolution = new ModuleResolution(
                Array.Empty<ResolvedModule>(),
                new[] { new ResolvedModule("COOP Server Settings Lock", "ServerSettingsLock", "3794802607", true, false, ModuleState.Ready, "", lockGame) },
                true);

            Assert.Empty(WorkshopDuplicates.ParkGameCopies(game, resolution, new[] { Path.Combine(root, "workshop", "content", "261550") }));
            Assert.True(File.Exists(Path.Combine(lockGame, "SubModule.xml")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
