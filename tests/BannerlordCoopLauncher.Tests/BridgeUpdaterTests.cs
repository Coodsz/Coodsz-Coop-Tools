using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// The bridge release: old exe names, an updater that already knows the two exe zips and new names.
// The new zip path (PlanInstall with SuccessorOnly, SuccessorPlan, the first start cleanup) is only
// reachable in the field once a release ships Coodsz-<Product>-Tools zips. Until then these tests are
// its only proof, and every published release still takes the old zip path.
public class BridgeUpdaterTests
{
    [Theory]
    [InlineData("CoodszHost.exe", "Coop", ToolRole.Server, "Coodsz Coop Server Tool.exe")]
    [InlineData("Coodsz Coop and Friends.exe", "Coop", ToolRole.Client, "Coodsz Coop Client Launcher.exe")]
    [InlineData("Coodsz Dedicated TAOM Server Tool.exe", "TAOM", ToolRole.Server, "Coodsz TAOM Server Tool.exe")]
    [InlineData("Coodsz TAOM Client Launcher.exe", "TAOM", ToolRole.Client, "Coodsz TAOM Client Launcher.exe")]
    [InlineData("Coodsz AD1259 Server.exe", "AD1259", ToolRole.Server, "Coodsz AD1259 Server Tool.exe")]
    [InlineData("Coodsz AD1259 Client Launcher.exe", "AD1259", ToolRole.Client, "Coodsz AD1259 Client Launcher.exe")]
    [InlineData("Coodsz Burning Empires Server.exe", "BurningEmpires", ToolRole.Server, "Coodsz Burning Empires Server Tool.exe")]
    [InlineData("Coodsz Burning Empires Client.exe", "BurningEmpires", ToolRole.Client, "Coodsz Burning Empires Client Launcher.exe")]
    public void Every_old_exe_maps_to_its_new_name(string oldExe, string product, ToolRole role, string newExe)
    {
        var tool = ToolNames.Find(oldExe);

        Assert.NotNull(tool);
        Assert.Equal(product, tool!.Product);
        Assert.Equal(role, tool.Role);
        Assert.Equal(newExe, tool.NewExeName);
        Assert.Same(tool, ToolNames.Find(newExe));
        Assert.Same(tool, ToolNames.Find(Path.GetFileNameWithoutExtension(oldExe)));
    }

    // Coodsz and Friends became the client launcher's More Nations switch, so both old client names
    // move to the one client launcher, by the two exe zip or by the legacy zip.
    [Theory]
    [InlineData("Coodsz Coop and Friends.exe")]
    [InlineData("Coodsz and Friends.exe")]
    public void Both_old_client_names_have_the_client_launcher_as_their_successor(string oldExe)
    {
        var tool = ToolNames.Find(oldExe);

        Assert.NotNull(tool);
        Assert.Equal("Coodsz Coop Client Launcher.exe", tool!.NewExeName);
        Assert.True(ToolNames.IsOldName(oldExe));
        Assert.Contains(@"C:\T\" + oldExe, ToolRename.OldExePaths(@"C:\T", "Coodsz Coop Client Launcher.exe"));
        Assert.Equal(new InstallPlan("Coodsz-Coop-Tools-v1.3.0.zip", "Coodsz Coop Client Launcher.exe", true),
            ToolUpdate.PlanInstall(oldExe, ["Coodsz-Coop-Tools-v1.3.0.zip", "Coodsz-Coop-and-Friends-v1.2.79.zip"]));
        Assert.Equal(new InstallPlan("Coodsz-Coop-and-Friends-v1.2.79.zip", oldExe, false),
            ToolUpdate.PlanInstall(oldExe, ["Coodsz-Coop-and-Friends-v1.2.79.zip"]));
        // The client build the legacy zip ships under the old name renames itself on its first start.
        Assert.Equal(@"C:\T\Coodsz Coop Client Launcher.exe", ToolRename.SelfRenameTarget(@"C:\T\" + oldExe, "Coodsz Coop Client Launcher"));
        // Only the client launcher owns it. The server tool's update never removes it.
        Assert.True(ToolUpdate.IsOwnFile(oldExe, "Coodsz Coop Client Launcher.exe"));
        Assert.False(ToolUpdate.IsOwnFile(oldExe, "Coodsz Coop Server Tool.exe"));
        Assert.Contains(Path.GetFileNameWithoutExtension(oldExe), ToolUpdate.SiblingBaseNames("Coodsz Coop Server Tool.exe"));
    }

    [Fact]
    public void Every_exe_name_belongs_to_one_tool_only()
    {
        var names = ToolNames.KnownExeNames.ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(ToolNames.IsNewName("Coodsz Coop Server Tool.exe"));
        Assert.True(ToolNames.IsOldName("CoodszHost.exe"));
        Assert.False(ToolNames.IsOldName("Coodsz TAOM Client Launcher.exe"));
    }

    [Theory]
    [InlineData("Coodsz Coop Server Tool (1).exe")]
    [InlineData("My Coodsz TAOM Server Tool.exe")]
    [InlineData("Coodsz Coop Server Tool.exe.exe")]
    public void A_name_that_only_looks_like_a_tool_is_not_one(string exe)
    {
        Assert.Null(ToolNames.Find(exe));
    }

    [Fact]
    public void Coop_and_TAOM_names_never_cross()
    {
        var both = new[] { "Coodsz-Coop-Tools-v1.3.0.zip", "Coodsz-TAOM-Tools-v1.2.0.zip", "Coodsz-AD1259-Tools-v1.1.0.zip" };
        var taomOld = new[] { "Coodsz-Dedicated-TAOM-Server-Tool-v1.1.55.zip", "Coodsz-TAOM-Client-Launcher-v1.1.55.zip" };
        var coopOld = new[] { "Coodsz-Coop-and-Friends-v1.2.79.zip" };

        Assert.Equal("Coop", ToolNames.Find("Coodsz Coop Server Tool.exe")!.Product);
        Assert.Equal("TAOM", ToolNames.Find("Coodsz TAOM Server Tool.exe")!.Product);
        Assert.Equal("Coodsz-Coop-Tools-v1.3.0.zip", ToolUpdate.PickZip("Coodsz Coop Server Tool.exe", both));
        Assert.Equal("Coodsz-TAOM-Tools-v1.2.0.zip", ToolUpdate.PickZip("Coodsz TAOM Server Tool.exe", both));
        Assert.Equal("Coodsz-AD1259-Tools-v1.1.0.zip", ToolUpdate.PickZip("Coodsz AD1259 Server Tool.exe", both));
        Assert.Equal("Coodsz-TAOM-Tools-v1.2.0.zip", ToolUpdate.PickZip("Coodsz Dedicated TAOM Server Tool.exe", both));
        Assert.Equal("Coodsz-Coop-Tools-v1.3.0.zip", ToolUpdate.PickZip("CoodszHost.exe", both));
        // "Coodsz Coop Server Tool" has Server and Tool in it, but it never takes a TAOM zip.
        Assert.Null(ToolUpdate.PickZip("Coodsz Coop Server Tool.exe", taomOld));
        Assert.Null(ToolUpdate.PickZip("Coodsz Coop Client Launcher.exe", taomOld));
        Assert.Null(ToolUpdate.PickZip("Coodsz TAOM Server Tool.exe", coopOld));
        Assert.Null(ToolUpdate.PickZip("Coodsz AD1259 Server Tool.exe", taomOld));
    }

    [Theory]
    [InlineData("CoodszHost.exe", "Coodsz-Coop-Tools-v1.3.0.zip", "Coodsz-Coop-and-Friends-v1.2.79.zip", "Coodsz Coop Server Tool.exe")]
    [InlineData("Coodsz Coop and Friends.exe", "Coodsz-Coop-Tools-v1.3.0.zip", "Coodsz-Coop-and-Friends-v1.2.79.zip", "Coodsz Coop Client Launcher.exe")]
    [InlineData("Coodsz Dedicated TAOM Server Tool.exe", "Coodsz-TAOM-Tools-v1.2.0.zip", "Coodsz-Dedicated-TAOM-Server-Tool-v1.1.55.zip", "Coodsz TAOM Server Tool.exe")]
    [InlineData("Coodsz TAOM Client Launcher.exe", "Coodsz-TAOM-Tools-v1.2.0.zip", "Coodsz-TAOM-Client-Launcher-v1.1.55.zip", "Coodsz TAOM Client Launcher.exe")]
    [InlineData("Coodsz AD1259 Server.exe", "Coodsz-AD1259-Tools-v1.1.0.zip", "Coodsz-AD1259-Server-v1.0.34.zip", "Coodsz AD1259 Server Tool.exe")]
    [InlineData("Coodsz AD1259 Client Launcher.exe", "Coodsz-AD1259-Tools-v1.1.0.zip", "Coodsz-AD1259-Client-Launcher-v1.0.34.zip", "Coodsz AD1259 Client Launcher.exe")]
    public void PickZip_prefers_the_new_zip_and_falls_back_to_the_old_one(string exe, string newZip, string oldZip, string successor)
    {
        var both = new[] { "SHA256SUMS.txt", oldZip, newZip, "LICENSE.txt" };

        Assert.Equal(newZip, ToolUpdate.PickZip(exe, [newZip]));
        Assert.Equal(oldZip, ToolUpdate.PickZip(exe, [oldZip]));
        Assert.Equal(newZip, ToolUpdate.PickZip(exe, both));
        Assert.Equal(new InstallPlan(newZip, successor, true), ToolUpdate.PlanInstall(exe, both));
        // An old zip still installs the old way, with the program under its own name.
        Assert.Equal(new InstallPlan(oldZip, exe, false), ToolUpdate.PlanInstall(exe, [oldZip]));
    }

    [Fact]
    public void A_renamed_exe_still_uses_the_old_rules()
    {
        Assert.Equal("Coodsz-Coop-and-Friends-v1.2.79.zip", ToolUpdate.PickZip("Coodsz Coop and Friends (2).exe", ["Coodsz-Coop-and-Friends-v1.2.79.zip"]));
    }

    [Fact]
    public void A_successor_install_copies_the_new_exe_and_sidecars_but_never_the_sibling()
    {
        var root = TempFolder("coodsz-plan-");
        var payload = Path.Combine(root, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(Path.Combine(payload, "DedicatedServerRepair", "bin"));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(payload, "Coodsz TAOM Server Tool.exe"), "new server");
        File.WriteAllText(Path.Combine(payload, "Coodsz TAOM Client Launcher.exe"), "new client");
        File.WriteAllText(Path.Combine(payload, "LICENSE.txt"), "license");
        File.WriteAllText(Path.Combine(payload, "taom-server.json"), "default settings");
        File.WriteAllText(Path.Combine(payload, "DedicatedServerRepair", "bin", "fix.dll"), "fix");
        File.WriteAllText(Path.Combine(payload, "DedicatedServerRepair", "server-module-policy.json"), "default policy");
        var oldExe = Path.Combine(dest, "Coodsz Dedicated TAOM Server Tool.exe");
        File.WriteAllText(oldExe, "old server");
        File.WriteAllText(Path.Combine(dest, "Coodsz Dedicated TAOM Server Tool.dll"), "old");
        File.WriteAllText(Path.Combine(dest, "Coodsz Dedicated TAOM Server Tool.deps.json"), "old");
        File.WriteAllText(Path.Combine(dest, "TaomCoop.Core.dll"), "old");
        File.WriteAllText(Path.Combine(dest, "taom-server.json"), "saved");
        try
        {
            var install = SuccessorInstall.Prepare(payload, "Coodsz TAOM Server Tool.exe", dest, oldExe);

            Assert.Equal(Path.Combine(payload, "Coodsz TAOM Server Tool.exe"), install.SourceExe);
            Assert.Equal(Path.Combine(dest, "Coodsz TAOM Server Tool.exe"), install.NewExePath);
            Assert.Equal(SuccessorInstall.Hash(install.SourceExe), install.SourceSha256);
            Assert.True(install.Renames);
            Assert.Equal(
                new[] { Path.Combine("DedicatedServerRepair", "bin", "fix.dll"), "LICENSE.txt" },
                install.OtherFiles.Select(file => Path.GetRelativePath(dest, file.To)).OrderBy(name => name, StringComparer.Ordinal).ToArray());
            var removed = install.RemoveAfterCopy.Select(path => Path.GetFileName(path)).OrderBy(name => name).ToList();
            Assert.Equal(new[] { "Coodsz Dedicated TAOM Server Tool.deps.json", "Coodsz Dedicated TAOM Server Tool.dll", "TaomCoop.Core.dll" }, removed);
            var plan = UpdateApply.Serialize(ToolUpdate.SuccessorPlan(install, 1, Path.Combine(root, "stage")));
            Assert.DoesNotContain("Coodsz TAOM Client Launcher", plan);
            Assert.DoesNotContain("taom-server.json", plan);

            // An old exe kept for a shortcut keeps every loose file it loads, so the shortcut still starts it.
            var kept = SuccessorInstall.Prepare(payload, "Coodsz TAOM Server Tool.exe", dest, oldExe, keepOldExe: true);
            Assert.Empty(kept.RemoveAfterCopy);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // A shortcut that could not be moved keeps the old exe, and the first start of the new exe must not
    // take the files that old exe loads, or the shortcut starts a broken program.
    [Fact]
    public void The_first_start_leaves_the_old_exes_files_while_the_old_exe_is_still_there()
    {
        var root = TempFolder("coodsz-first-start-");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.exe"), "new server");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.exe.old"), "older copy");
        File.WriteAllText(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.exe"), "old server");
        File.WriteAllText(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.dll"), "old server");
        File.WriteAllText(Path.Combine(root, "TaomCoop.Core.dll"), "old loose library");
        try
        {
            Assert.Equal(new[] { "Coodsz TAOM Server Tool.exe.old" },
                ToolUpdate.FirstStartFiles(root, "Coodsz TAOM Server Tool.exe", singleFile: true).Select(path => Path.GetFileName(path)).ToArray());

            // The startup removal every App.xaml.cs runs goes through the same rule.
            ToolUpdate.RemovePreviousVersion(root, Path.Combine(root, "Coodsz TAOM Server Tool.exe"), singleFile: true);
            Assert.True(File.Exists(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.dll")));
            Assert.True(File.Exists(Path.Combine(root, "TaomCoop.Core.dll")));
            Assert.False(File.Exists(Path.Combine(root, "Coodsz TAOM Server Tool.exe.old")));

            File.Delete(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.exe"));
            var later = ToolUpdate.FirstStartFiles(root, "Coodsz TAOM Server Tool.exe", singleFile: true).Select(path => Path.GetFileName(path)).ToList();
            Assert.Contains("Coodsz Dedicated TAOM Server Tool.dll", later);
            Assert.Contains("TaomCoop.Core.dll", later);
            ToolUpdate.RemovePreviousVersion(root, Path.Combine(root, "Coodsz TAOM Server Tool.exe"), singleFile: true);
            Assert.False(File.Exists(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.dll")));
            Assert.False(File.Exists(Path.Combine(root, "TaomCoop.Core.dll")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // A start while an update helper holds the folder must not take the helper's .new between its copy and
    // the swap, so the leftovers wait for a start after the helper is done.
    [Fact]
    public void A_start_during_an_update_leaves_the_folder_to_the_update()
    {
        var root = TempFolder("coodsz-start-during-update-");
        var exe = Path.Combine(root, "Coodsz TAOM Server Tool.exe");
        var leftover = Path.Combine(root, "Coodsz TAOM Server Tool.exe.old");
        File.WriteAllText(exe, "new server");
        File.WriteAllText(leftover, "older copy");
        ToolUpdate.RemoveWait = TimeSpan.FromMilliseconds(200);
        try
        {
            // The helper holds the folder on another thread, the way its own process would.
            using var held = new ManualResetEventSlim();
            using var done = new ManualResetEventSlim();
            var holder = Task.Run(() =>
            {
                using var gate = UpdateApply.FolderLock.Take(root, TimeSpan.FromSeconds(5));
                held.Set();
                done.Wait(TimeSpan.FromSeconds(10));
            });
            Assert.True(held.Wait(TimeSpan.FromSeconds(10)), "The holder never took the folder.");
            ToolUpdate.RemovePreviousVersion(root, exe, singleFile: true);
            Assert.True(File.Exists(leftover));

            done.Set();
            Assert.True(holder.Wait(TimeSpan.FromSeconds(10)), "The holder never let the folder go.");
            ToolUpdate.RemovePreviousVersion(root, exe, singleFile: true);
            Assert.False(File.Exists(leftover));
        }
        finally
        {
            ToolUpdate.RemoveWait = TimeSpan.FromSeconds(3);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_loose_sibling_in_the_same_folder_keeps_its_shared_files()
    {
        var root = TempFolder("coodsz-sibling-");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Client Launcher.exe"), "new client");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Client Launcher.dll"), "old client");
        File.WriteAllText(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.exe"), "loose server");
        File.WriteAllText(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.dll"), "loose server");
        File.WriteAllText(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.pdb"), "loose server");
        File.WriteAllText(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.exe.old"), "running server");
        File.WriteAllText(Path.Combine(root, "TaomCoop.Core.dll"), "shared");
        try
        {
            ToolUpdate.RemovePreviousVersion(root, Path.Combine(root, "Coodsz TAOM Client Launcher.exe"), singleFile: true);

            Assert.False(File.Exists(Path.Combine(root, "Coodsz TAOM Client Launcher.dll")));
            Assert.True(File.Exists(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.dll")));
            Assert.True(File.Exists(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.pdb")));
            Assert.True(File.Exists(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.exe.old")));
            Assert.True(File.Exists(Path.Combine(root, "TaomCoop.Core.dll")));
            Assert.True(File.Exists(Path.Combine(root, "Coodsz Dedicated TAOM Server Tool.exe")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // A loose Burning Empires test build runs on the same BannerlordCoopLauncher.Core.dll and TaomCoop.Core.dll
    // a loose TAOM tool shipped, so a single file TAOM tool's first start must leave them for it.
    [Theory]
    [InlineData("Coodsz Burning Empires Server")]
    [InlineData("Coodsz Burning Empires Client")]
    [InlineData("Coodsz Burning Empires Server Tool")]
    [InlineData("Coodsz Burning Empires Client Launcher")]
    public void A_TAOM_cleanup_keeps_the_shared_dlls_of_a_loose_Burning_Empires_tool_in_the_same_folder(string beTool)
    {
        var root = TempFolder("coodsz-be-sibling-");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.exe"), "single file server");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.dll"), "old server");
        File.WriteAllText(Path.Combine(root, beTool + ".exe"), "loose BE tool");
        File.WriteAllText(Path.Combine(root, beTool + ".dll"), "loose BE tool");
        File.WriteAllText(Path.Combine(root, "BurningEmpires.Core.dll"), "BE core");
        File.WriteAllText(Path.Combine(root, "BannerlordCoopLauncher.Core.dll"), "shared");
        File.WriteAllText(Path.Combine(root, "TaomCoop.Core.dll"), "shared");
        try
        {
            var listed = ToolUpdate.PreviousVersionFiles(root, "Coodsz TAOM Server Tool.exe", singleFile: true).Select(Path.GetFileName).ToList();
            ToolUpdate.RemovePreviousVersion(root, Path.Combine(root, "Coodsz TAOM Server Tool.exe"), singleFile: true);

            Assert.DoesNotContain("BannerlordCoopLauncher.Core.dll", listed);
            Assert.DoesNotContain("TaomCoop.Core.dll", listed);
            Assert.True(File.Exists(Path.Combine(root, "BannerlordCoopLauncher.Core.dll")));
            Assert.True(File.Exists(Path.Combine(root, "TaomCoop.Core.dll")));
            Assert.True(File.Exists(Path.Combine(root, "BurningEmpires.Core.dll")));
            Assert.True(File.Exists(Path.Combine(root, beTool + ".exe")));
            Assert.True(File.Exists(Path.Combine(root, beTool + ".dll")));
            Assert.False(File.Exists(Path.Combine(root, "Coodsz TAOM Server Tool.dll")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_single_file_Burning_Empires_tool_removes_its_own_loose_test_build_files()
    {
        var root = TempFolder("coodsz-be-own-");
        File.WriteAllText(Path.Combine(root, "Coodsz Burning Empires Server Tool.exe"), "single file server");
        File.WriteAllText(Path.Combine(root, "Coodsz Burning Empires Server Tool.dll"), "old server");
        File.WriteAllText(Path.Combine(root, "BurningEmpires.Core.dll"), "BE core");
        File.WriteAllText(Path.Combine(root, "TaomCoop.Core.dll"), "shared");
        File.WriteAllText(Path.Combine(root, "be-server.json"), "{}");
        try
        {
            ToolUpdate.RemovePreviousVersion(root, Path.Combine(root, "Coodsz Burning Empires Server Tool.exe"), singleFile: true);

            Assert.False(File.Exists(Path.Combine(root, "Coodsz Burning Empires Server Tool.dll")));
            Assert.False(File.Exists(Path.Combine(root, "BurningEmpires.Core.dll")));
            Assert.False(File.Exists(Path.Combine(root, "TaomCoop.Core.dll")));
            Assert.True(File.Exists(Path.Combine(root, "be-server.json")));
            Assert.True(File.Exists(Path.Combine(root, "Coodsz Burning Empires Server Tool.exe")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_legacy_update_of_one_TAOM_tool_never_removes_the_other_one_in_the_same_folder()
    {
        var root = TempFolder("coodsz-legacy-");
        var source = Path.Combine(root, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(source, "Coodsz TAOM Client Launcher.exe"), "new client");
        File.WriteAllText(Path.Combine(source, "Coodsz TAOM Client Launcher.dll"), "new client");
        File.WriteAllText(Path.Combine(source, "TaomCoop.Core.dll"), "new shared");
        foreach (var name in new[]
        {
            "Coodsz TAOM Client Launcher.exe", "Coodsz TAOM Client Launcher.dll", "Coodsz TAOM Client Launcher.pdb",
            "Coodsz Dedicated TAOM Server Tool.exe", "Coodsz Dedicated TAOM Server Tool.dll", "Coodsz Dedicated TAOM Server Tool.exe.old",
            "Coodsz and Friends.exe", "ServerOnly.dll"
        })
            File.WriteAllText(Path.Combine(dest, name), "old");
        try
        {
            var stale = ToolUpdate.LegacyStaleFiles(source, dest, "Coodsz TAOM Client Launcher.exe").Select(path => Path.GetFileName(path)).ToList();
            var plan = ToolUpdate.LegacyPlan(source, dest, Path.Combine(dest, "Coodsz TAOM Client Launcher.exe"), 1, Path.Combine(root, "stage"));
            var text = UpdateApply.Serialize(plan);

            // Only the client's own leftover goes. The server tool is loose, so its dlls and the shared ones stay.
            Assert.Equal(new[] { "Coodsz TAOM Client Launcher.pdb" }, stale);
            Assert.Equal(new[] { Path.Combine(dest, "Coodsz TAOM Client Launcher.pdb") }, plan.StaleFiles);
            Assert.DoesNotContain("Coodsz Dedicated TAOM Server Tool.exe\"", text);
            Assert.DoesNotContain("ServerOnly.dll", text);
            Assert.DoesNotContain("Coodsz and Friends.exe", text);
            // The closing sweep of .old files skips the server tool's, which may still be running.
            Assert.Contains("Coodsz Dedicated TAOM Server Tool.", plan.SiblingPrefixes!);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_legacy_update_without_a_sibling_removes_only_its_own_stale_program_files()
    {
        var root = TempFolder("coodsz-legacy-alone-");
        var source = Path.Combine(root, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(source, "CoodszHost.exe"), "new");
        File.WriteAllText(Path.Combine(dest, "CoodszHost.exe"), "old");
        File.WriteAllText(Path.Combine(dest, "CoodszHost.dll"), "old");
        File.WriteAllText(Path.Combine(dest, "CoodszHost.exe.old"), "old");
        File.WriteAllText(Path.Combine(dest, "BannerlordCoopLauncher.Core.dll"), "old");
        File.WriteAllText(Path.Combine(dest, "Old.dll"), "player");
        File.WriteAllText(Path.Combine(dest, "Retired.exe"), "player");
        File.WriteAllText(Path.Combine(dest, "Coodsz and Friends.exe"), "brand");
        File.WriteAllText(Path.Combine(dest, "host.json"), "saved");
        try
        {
            var stale = ToolUpdate.LegacyStaleFiles(source, dest, "CoodszHost.exe").Select(path => Path.GetFileName(path)).OrderBy(name => name, StringComparer.Ordinal).ToList();

            // An exe or dll the tool never shipped is the player's, so it stays.
            Assert.Equal(new[] { "BannerlordCoopLauncher.Core.dll", "CoodszHost.dll", "CoodszHost.exe.old" }, stale);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_plan_keeps_every_path_and_list_through_the_plan_file()
    {
        var folder = @"C:\Coodsz" + (char)0x2019 + "s tools";
        var install = new SuccessorInstall(folder + @"\a.exe", folder, folder + @"\b.exe", folder + @"\c.exe", [folder + @"\old.dll"],
            new string('A', 64), [new UpdateFile(@"C:\stage\x.dll", folder + @"\x.dll")], KeepOldExe: true);
        var root = TempFolder("coodsz-plan-file-");
        try
        {
            var path = Path.Combine(root, UpdateApply.PlanFileName);
            UpdateApply.WritePlanAsync(path, ToolUpdate.SuccessorPlan(install, 42, @"C:\stage")).GetAwaiter().GetResult();
            var back = System.Text.Json.JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(path))!;

            Assert.Equal(42, back.ProcessId);
            Assert.Equal(folder + @"\update-install.log", back.LogPath);
            Assert.Equal(install.SourceExe, back.Successor!.SourceExe);
            Assert.Equal(install.OldExePath, back.Successor.OldExePath);
            Assert.Equal(install.NewExePath, back.Successor.NewExePath);
            Assert.Equal(install.RemoveAfterCopy, back.Successor.RemoveAfterCopy);
            Assert.Equal(install.SourceSha256, back.Successor.SourceSha256);
            Assert.Equal(install.OtherFiles, back.Successor.OtherFiles);
            Assert.True(back.Successor.KeepOldExe);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Only_the_update_argument_runs_the_helper()
    {
        Assert.False(UpdateApply.OnStartup([]));
        Assert.False(UpdateApply.OnStartup(["--something-else", "x"]));
        Assert.False(UpdateApply.OnStartup([UpdateApply.ApplyArgument]));
        // A missing plan file is a failed helper run, and the helper still exits.
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.True(UpdateApply.OnStartup([UpdateApply.ApplyArgument, Path.Combine(missing, UpdateApply.PlanFileName)], Path.Combine(missing, UpdateApply.HelperFolder, "tool.exe")));
    }

    // A junction planted in the update folder (security review) is removed itself, never followed, so the
    // files it points to stay.
    [WindowsFact]
    public void Removing_an_update_folder_never_follows_a_junction_inside_it()
    {
        var root = TempFolder("coodsz-junction-");
        var outside = Path.Combine(root, "outside");
        var stage = Path.Combine(root, "stage");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(stage, "plan.json"), "{}");
        var link = Path.Combine(stage, "link");
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", link, outside }, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
            mklink.WaitForExit();
        Assert.True(Directory.Exists(link));
        try
        {
            UpdateApply.TryDeleteFolder(stage);
            Assert.False(Directory.Exists(stage));
            Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(root, true);
        }
    }

    // A trusted plan that fails before its install writes update-error.log in its update folder. When
    // update-install.log can't be written (antivirus holds it, say), that file is the only record of the
    // failure, so the folder stays. With the log written, the folder goes as before.
    [WindowsFact]
    public void A_failed_update_keeps_its_error_log_when_the_install_log_is_locked()
    {
        var root = TempFolder("coodsz-locked-log-");
        var stage = Path.Combine(root, "stage");
        var payload = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(payload, "tool.cmd"), "new program");
        var exe = Path.Combine(dest, "tool.cmd");
        File.WriteAllText(exe, "@echo off\r\necho old> \"%~dp0started.txt\"\r\n");
        var plan = ToolUpdate.SuccessorPlan(SuccessorInstall.Prepare(payload, "tool.cmd", dest, exe), 2147483000, stage);
        var errorLog = Path.Combine(stage, "update-error.log");
        File.WriteAllText(errorLog, "The update did not run. The plan broke.");
        File.WriteAllText(plan.LogPath, "");
        try
        {
            using (new FileStream(plan.LogPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                UpdateApply.NotInstalled(plan, new InvalidOperationException("The plan broke."));
            Assert.True(WaitFor(Path.Combine(dest, "started.txt")));
            Assert.Equal("", File.ReadAllText(plan.LogPath));
            Assert.True(File.Exists(errorLog));

            // A tool that won't start again is noted next to the first error, not only in the locked log.
            File.Move(exe, exe + ".away");
            using (new FileStream(plan.LogPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                UpdateApply.NotInstalled(plan, new InvalidOperationException("The plan broke."));
            Assert.Contains("The plan broke.", File.ReadAllText(errorLog));
            Assert.Contains("The tool could not be started again.", File.ReadAllText(errorLog));

            // The log took the first line but not the restart one: the folder holding that note stays.
            File.Delete(errorLog);
            UpdateApply.NotInstalled(plan, new InvalidOperationException("The plan broke."), (path, text, append) =>
            {
                if (append && path == plan.LogPath)
                    return false;
                if (append)
                    File.AppendAllText(path, text);
                else
                    File.WriteAllText(path, text);
                return true;
            });
            Assert.StartsWith("Not installed ", File.ReadAllText(plan.LogPath));
            Assert.True(Directory.Exists(stage));
            Assert.Contains("The tool could not be started again.", File.ReadAllText(errorLog));
            File.Move(exe + ".away", exe);

            File.Delete(Path.Combine(dest, "started.txt"));
            UpdateApply.NotInstalled(plan, new InvalidOperationException("The plan broke."));
            Assert.True(WaitFor(Path.Combine(dest, "started.txt")));
            Assert.StartsWith("Not installed ", File.ReadAllText(plan.LogPath));
            Assert.False(Directory.Exists(stage));
        }
        finally
        {
            // The started tool.cmd can still be closing for a moment.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Delete(root, true);
                    break;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(250);
                }
            }
        }
    }

    // The normal two exe update keeps the exe name, so the copy replaces the only exe the player has.
    [WindowsFact]
    public void A_swap_that_fails_puts_the_old_exe_back_and_starts_it()
    {
        var root = TempFolder("coodsz-swap-");
        var stage = Path.Combine(root, "stage");
        var payload = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(payload, "tool.cmd"), "@echo off\r\necho new> \"%~dp0started.txt\"\r\n");
        var oldProgram = "@echo off\r\necho old> \"%~dp0started.txt\"\r\n";
        var exe = Path.Combine(dest, "tool.cmd");
        File.WriteAllText(exe, oldProgram);
        var install = SuccessorInstall.Prepare(payload, "tool.cmd", dest, exe);
        // Replace is not there (another volume), then the fallback fails after the old exe moved aside.
        UpdateApply.FaultForTest = step => throw (step == "replace" ? (Exception)new IOException("No replace here.") : new InvalidOperationException("The disk is full."));
        try
        {
            RunPlan(stage, ToolUpdate.SuccessorPlan(install, 2147483000, stage));
            var started = WaitFor(Path.Combine(dest, "started.txt"));

            Assert.False(install.Renames);
            Assert.Equal(oldProgram, File.ReadAllText(exe));
            Assert.False(File.Exists(exe + ".old"));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(exe)!, "*.new"));
            var log = File.ReadAllText(Path.Combine(dest, "update-install.log"));
            Assert.StartsWith("Not installed ", log);
            Assert.Contains("The copy failed. The disk is full.", log);
            Assert.True(started);
            Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "started.txt")).Trim());
        }
        finally
        {
            UpdateApply.FaultForTest = null;
            Directory.Delete(root, true);
        }
    }

    // File.Replace swaps the exe in one step and keeps the old one, so a kill mid update never leaves the folder without it.
    [WindowsFact]
    public void The_exe_swap_keeps_the_old_exe_as_a_backup_in_one_step()
    {
        var root = TempFolder("coodsz-replace-");
        var from = Path.Combine(root, "download", "tool.exe");
        var to = Path.Combine(root, "dest", "tool.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(from)!);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.WriteAllText(from, "new program");
        File.WriteAllText(to, "old program");
        File.WriteAllText(to + ".old", "an older backup");
        var steps = new List<string>();
        UpdateApply.FaultForTest = steps.Add;
        try
        {
            UpdateApply.ReplaceExe(from, to, SuccessorInstall.Hash(from));

            Assert.Equal("new program", File.ReadAllText(to));
            Assert.Equal("old program", File.ReadAllText(to + ".old"));
            Assert.False(File.Exists(to + ".new"));
            // Replace did it, so the two move fallback never ran.
            Assert.Equal(new[] { "replace" }, steps);
        }
        finally
        {
            UpdateApply.FaultForTest = null;
            Directory.Delete(root, true);
        }
    }

    // A locked file moves aside to .old so the new copy can take its name. When that last move failed the
    // file stayed at .old only, so a sidecar or a legacy payload file went missing.
    [WindowsFact]
    public void A_locked_file_copy_that_fails_after_moving_it_aside_puts_it_back()
    {
        var root = TempFolder("coodsz-copy-back-");
        var from = Path.Combine(root, "download", "tool.dll");
        var to = Path.Combine(root, "dest", "tool.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(from)!);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.WriteAllText(from, "new file");
        File.WriteAllText(to, "old file");
        UpdateApply.FaultForTest = step =>
        {
            if (step == "copy move")
                throw new IOException("The disk is full.");
        };
        try
        {
            // Held the way Windows holds a running exe: it can be renamed, not overwritten.
            using (new FileStream(to, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                var error = Assert.Throws<IOException>(() => UpdateApply.CopyUpdateFile(from, to));
                Assert.Equal("The disk is full.", error.Message);
            }

            Assert.Equal("old file", File.ReadAllText(to));
            Assert.False(File.Exists(to + ".new"));
            Assert.False(File.Exists(to + ".old"));
        }
        finally
        {
            UpdateApply.FaultForTest = null;
            Directory.Delete(root, true);
        }
    }

    // When the swap fails and the old exe cannot go back either, the checked new copy is the only working
    // program left, so it stays and the error names it instead of it being removed with no exe in place.
    [WindowsFact]
    public void A_swap_whose_old_exe_cannot_go_back_keeps_the_new_copy_and_names_it()
    {
        var root = TempFolder("coodsz-keep-new-");
        var from = Path.Combine(root, "download", "tool.exe");
        var to = Path.Combine(root, "dest", "tool.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(from)!);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.WriteAllText(from, "new program");
        File.WriteAllText(to, "old program");
        UpdateApply.FaultForTest = step =>
        {
            if (step == "replace")
                throw new IOException("No replace here.");
            if (step == "move")
            {
                // Something takes the exe's name, so the old exe cannot move back.
                Directory.CreateDirectory(to);
                throw new InvalidOperationException("The disk is full.");
            }
        };
        try
        {
            var error = Assert.Throws<IOException>(() => UpdateApply.ReplaceExe(from, to, SuccessorInstall.Hash(from), "coodsz-update-7"));

            var kept = to + ".coodsz-update-7.new";
            Assert.Equal("new program", File.ReadAllText(kept));
            Assert.Equal("old program", File.ReadAllText(to + ".old"));
            Assert.Contains(kept, error.Message);
            Assert.Contains(to + ".old", error.Message);
        }
        finally
        {
            UpdateApply.FaultForTest = null;
            Directory.Delete(root, true);
        }
    }

    // The legacy install copied the exe over itself in place, so a kill mid copy left half an exe. It now
    // goes through the same checked .new copy and swap as the two exe update.
    [WindowsFact]
    public void A_legacy_update_swaps_the_exe_through_a_checked_copy_and_keeps_the_old_one_when_that_fails()
    {
        var root = TempFolder("coodsz-legacy-swap-");
        var stage = Path.Combine(root, "stage");
        var source = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(source, "tool.cmd"), "@echo off\r\necho new> \"%~dp0started.txt\"\r\n");
        var oldProgram = "@echo off\r\necho old> \"%~dp0started.txt\"\r\n";
        var exe = Path.Combine(dest, "tool.cmd");
        File.WriteAllText(exe, oldProgram);
        var plan = ToolUpdate.LegacyPlan(source, dest, exe, 2147483000, stage);
        // Hashed by the tool before it closes, like the two exe plan.
        Assert.Equal(SuccessorInstall.Hash(Path.Combine(source, "tool.cmd")), plan.ExeSha256);
        var steps = new List<string>();
        UpdateApply.FaultForTest = step =>
        {
            steps.Add(step);
            throw step == "replace" ? new IOException("No replace here.") : new InvalidOperationException("The disk is full.");
        };
        try
        {
            RunPlan(stage, plan);
            var started = WaitFor(Path.Combine(dest, "started.txt"));

            Assert.Equal(new[] { "replace", "move" }, steps);
            Assert.Equal(oldProgram, File.ReadAllText(exe));
            Assert.Empty(Directory.GetFiles(dest, "*.new"));
            Assert.False(File.Exists(exe + ".old"));
            var log = File.ReadAllText(Path.Combine(dest, "update-install.log"));
            Assert.StartsWith("Not installed ", log);
            Assert.Contains("The disk is full.", log);
            Assert.True(started);
            Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "started.txt")).Trim());
        }
        finally
        {
            UpdateApply.FaultForTest = null;
            Directory.Delete(root, true);
        }
    }

    // The exe the tool hashed before it closed is the only one that gets in: one changed or cut short in
    // the update folder after that is refused and the old exe stays.
    [WindowsFact]
    public void A_legacy_update_refuses_an_exe_that_changed_after_the_plan_was_made()
    {
        var root = TempFolder("coodsz-legacy-hash-");
        var stage = Path.Combine(root, "stage");
        var source = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(source, "tool.cmd"), "@echo off\r\necho new> \"%~dp0started.txt\"\r\n");
        var oldProgram = "@echo off\r\necho old> \"%~dp0started.txt\"\r\n";
        var exe = Path.Combine(dest, "tool.cmd");
        File.WriteAllText(exe, oldProgram);
        var plan = ToolUpdate.LegacyPlan(source, dest, exe, 2147483000, stage);
        File.WriteAllText(Path.Combine(source, "tool.cmd"), "@echo off\r\necho cut");
        try
        {
            RunPlan(stage, plan);
            var started = WaitFor(Path.Combine(dest, "started.txt"));

            Assert.Equal(oldProgram, File.ReadAllText(exe));
            Assert.Empty(Directory.GetFiles(dest, "*.new"));
            Assert.False(File.Exists(exe + ".old"));
            var log = File.ReadAllText(Path.Combine(dest, "update-install.log"));
            Assert.StartsWith("Not installed ", log);
            Assert.Contains("is not complete", log);
            Assert.True(started);
            Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "started.txt")).Trim());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // When a copy could not put a file back, its .old and .new are the only copies of it, so the next start
    // must not remove them as leftovers.
    [Fact]
    public void A_backup_whose_file_is_missing_is_never_removed_as_a_leftover()
    {
        var root = TempFolder("coodsz-only-copy-");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.exe"), "server");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.exe.old"), "previous server");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.dll.old"), "the only old dll");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.dll.new"), "the only new dll");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.dll.coodsz-update-5.new"), "the only new dll");
        try
        {
            var listed = ToolUpdate.PreviousVersionFiles(root, "Coodsz TAOM Server Tool.exe", singleFile: false).Select(Path.GetFileName).ToList();

            Assert.Equal(new[] { "Coodsz TAOM Server Tool.exe.old" }, listed);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_killed_helpers_tagged_new_copy_is_removed_on_the_next_start()
    {
        var root = TempFolder("coodsz-tagged-new-");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.exe"), "server");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Server Tool.exe.coodsz-update-1234.new"), "half a copy");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Client Launcher.exe"), "sibling");
        File.WriteAllText(Path.Combine(root, "Coodsz TAOM Client Launcher.exe.coodsz-update-99.new"), "sibling's copy");
        try
        {
            var listed = ToolUpdate.PreviousVersionFiles(root, "Coodsz TAOM Server Tool.exe", singleFile: true).Select(Path.GetFileName).ToList();

            Assert.Contains("Coodsz TAOM Server Tool.exe.coodsz-update-1234.new", listed);
            // A sibling's update may still be running.
            Assert.DoesNotContain("Coodsz TAOM Client Launcher.exe.coodsz-update-99.new", listed);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public void The_same_name_update_swaps_the_exe_and_leaves_no_old_or_new_copy()
    {
        var root = TempFolder("coodsz-swap-ok-");
        var stage = Path.Combine(root, "stage");
        var payload = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(dest);
        var newProgram = "@echo off\r\necho new> \"%~dp0started.txt\"\r\n";
        File.WriteAllText(Path.Combine(payload, "tool.cmd"), newProgram);
        var exe = Path.Combine(dest, "tool.cmd");
        File.WriteAllText(exe, "old program");
        var install = SuccessorInstall.Prepare(payload, "tool.cmd", dest, exe);
        try
        {
            RunPlan(stage, ToolUpdate.SuccessorPlan(install, 2147483000, stage));
            var started = WaitFor(Path.Combine(dest, "started.txt"));

            Assert.Equal(newProgram, File.ReadAllText(exe));
            Assert.False(File.Exists(exe + ".old"));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(exe)!, "*.new"));
            Assert.StartsWith("Installed ", File.ReadAllText(Path.Combine(dest, "update-install.log")));
            Assert.True(started);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // Two helpers for the same exe (an update clicked twice) once shared .new and .old and could remove each
    // other's backup, leaving no exe. Each now has its own .new and they take turns per install folder.
    [WindowsFact]
    public void Two_helpers_for_the_same_exe_never_leave_it_missing()
    {
        var root = TempFolder("coodsz-two-helpers-");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(dest);
        var exe = Path.Combine(dest, "tool.cmd");
        File.WriteAllText(exe, "old program");
        var newProgram = "@echo off\r\necho new>> \"%~dp0started.txt\"\r\n";
        (string Stage, UpdatePlan Plan) Helper(string name)
        {
            var stage = Path.Combine(root, name);
            var payload = Path.Combine(stage, "files");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, "tool.cmd"), newProgram);
            return (stage, ToolUpdate.SuccessorPlan(SuccessorInstall.Prepare(payload, "tool.cmd", dest, exe), 2147483000, stage));
        }

        try
        {
            // One after the other.
            var first = Helper("coodsz-update-1");
            var second = Helper("coodsz-update-2");
            RunPlan(first.Stage, first.Plan);
            Assert.Equal(newProgram, File.ReadAllText(exe));
            RunPlan(second.Stage, second.Plan);
            Assert.Equal(newProgram, File.ReadAllText(exe));

            // Overlapping, a few times over.
            for (var round = 0; round < 5; round++)
            {
                var a = Helper("coodsz-update-a" + round);
                var b = Helper("coodsz-update-b" + round);
                using var go = new ManualResetEventSlim();
                var runs = new[] { a, b }.Select(helper => Task.Run(() => { go.Wait(); RunPlan(helper.Stage, helper.Plan); })).ToArray();
                go.Set();
                Task.WaitAll(runs);

                Assert.Equal(newProgram, File.ReadAllText(exe));
                Assert.StartsWith("Installed ", File.ReadAllText(Path.Combine(dest, "update-install.log")));
                Assert.Empty(Directory.GetFiles(dest, "*.new"));
                Assert.False(File.Exists(exe + ".old"));
            }
        }
        finally
        {
            // Each helper started the tool once; let those small programs finish before the folder goes.
            var started = Path.Combine(dest, "started.txt");
            var until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until && StartedLines() < 12)
                Thread.Sleep(100);
            // A tool still writing its line holds the file for a moment; that read counts as not there yet.
            int StartedLines()
            {
                try { return File.Exists(started) ? File.ReadAllLines(started).Length : 0; }
                catch (IOException) { return 0; }
            }
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Delete(root, true);
                    break;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(250);
                }
            }
        }
    }

    [WindowsFact]
    public void A_helper_that_cannot_get_the_folder_changes_nothing_and_starts_the_tool()
    {
        var root = TempFolder("coodsz-lock-held-");
        var stage = Path.Combine(root, "stage");
        var payload = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(payload, "tool.cmd"), "new program");
        var oldProgram = "@echo off\r\necho old> \"%~dp0started.txt\"\r\n";
        var exe = Path.Combine(dest, "tool.cmd");
        File.WriteAllText(exe, oldProgram);
        var plan = ToolUpdate.SuccessorPlan(SuccessorInstall.Prepare(payload, "tool.cmd", dest, exe), 2147483000, stage);
        UpdateApply.LockWait = TimeSpan.FromMilliseconds(200);
        try
        {
            // Another update holds the folder, on another thread the way another helper process would.
            using var held = new ManualResetEventSlim();
            using var done = new ManualResetEventSlim();
            var holder = Task.Run(() =>
            {
                using var gate = UpdateApply.FolderLock.Take(dest, TimeSpan.FromSeconds(5));
                held.Set();
                done.Wait();
            });
            held.Wait();
            // The update holding the folder writes update-install.log when it ends, so this one's line goes
            // to its own log and never changes that one.
            File.WriteAllText(Path.Combine(dest, "update-install.log"), "Installed by the other update");
            RunPlan(stage, plan);
            done.Set();
            holder.Wait();
            var started = WaitFor(Path.Combine(dest, "started.txt"));

            Assert.Equal(oldProgram, File.ReadAllText(exe));
            Assert.Contains("Another update of this folder is still running", File.ReadAllText(Path.Combine(dest, UpdateApply.SkippedLogName)));
            Assert.Equal("Installed by the other update", File.ReadAllText(Path.Combine(dest, "update-install.log")));
            // Its download does not wait a day in the temp folder.
            Assert.False(Directory.Exists(stage));
            Assert.True(started);
            Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "started.txt")).Trim());
        }
        finally
        {
            UpdateApply.LockWait = TimeSpan.FromMinutes(1);
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public void A_plan_that_cannot_run_still_starts_the_tool_and_says_why()
    {
        var root = TempFolder("coodsz-bad-plan-");
        var stage = Path.Combine(root, "stage");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(dest);
        var exe = Path.Combine(dest, "tool.cmd");
        File.WriteAllText(exe, "@echo off\r\necho opened> \"%~dp0reopened.txt\"\r\n");
        try
        {
            // No source folder, the way a damaged plan reads.
            RunPlan(stage, new UpdatePlan(2147483000, stage, Path.Combine(dest, "update-install.log"), exe));
            var opened = WaitFor(Path.Combine(dest, "reopened.txt"));

            Assert.True(opened);
            Assert.StartsWith("Not installed ", File.ReadAllText(Path.Combine(dest, "update-install.log")));
            // The reason is in update-install.log, so the plan's own update folder goes with its download.
            Assert.False(Directory.Exists(stage));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public void A_plan_that_cannot_run_and_cannot_restart_the_tool_logs_both()
    {
        var root = TempFolder("coodsz-bad-restart-");
        var stage = Path.Combine(root, "stage");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(dest);
        // The exe is gone, so the restart fails too.
        var exe = Path.Combine(dest, "tool.cmd");
        try
        {
            RunPlan(stage, new UpdatePlan(2147483000, stage, Path.Combine(dest, "update-install.log"), exe));

            var log = File.ReadAllText(Path.Combine(dest, "update-install.log"));
            Assert.StartsWith("Not installed ", log);
            Assert.Contains("The tool could not be started again. ", log);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public void A_plan_that_is_not_the_helpers_own_is_refused_and_starts_nothing()
    {
        var root = TempFolder("coodsz-foreign-plan-");
        var stage = Path.Combine(root, "stage");
        var elsewhere = Path.Combine(root, "elsewhere");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(dest);
        var exe = Path.Combine(dest, "tool.cmd");
        File.WriteAllText(exe, "@echo off\r\necho opened> \"%~dp0reopened.txt\"\r\n");
        var helper = Path.Combine(stage, UpdateApply.HelperFolder, "tool.exe");
        try
        {
            var plan = new UpdatePlan(2147483000, elsewhere, Path.Combine(dest, "update-install.log"), exe, SourceDirectory: elsewhere, InstallDirectory: dest);
            var path = Path.Combine(elsewhere, UpdateApply.PlanFileName);
            UpdateApply.WritePlanAsync(path, plan).GetAwaiter().GetResult();

            Assert.True(UpdateApply.OnStartup([UpdateApply.ApplyArgument, path], helper));
            Assert.False(WaitFor(Path.Combine(dest, "reopened.txt")));
            Assert.False(File.Exists(Path.Combine(dest, "update-install.log")));
            Assert.Contains("The plan does not belong to this helper.", File.ReadAllText(Path.Combine(stage, "update-error.log")));

            // A plan whose own folder is not its update folder, or under another name, is refused too.
            Assert.Equal("The plan is not in its own update folder.", UpdateApply.Refusal(plan with { StageDirectory = stage }, path, helper));
            Assert.NotNull(UpdateApply.Refusal(plan with { StageDirectory = stage }, Path.Combine(stage, "other.json"), helper));
            Assert.Null(UpdateApply.Refusal(plan with { StageDirectory = stage }, Path.Combine(stage, UpdateApply.PlanFileName), helper));
            // A loose build is its own helper, so its plan must start that same exe again.
            Assert.Null(UpdateApply.Refusal(plan, path, exe));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Update_folders_stay_for_a_day_so_a_slow_download_is_never_removed()
    {
        var source = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Updates", "UpdateApply.cs"));
        Assert.Contains("RemoveOldStages(Path.GetTempPath(), TimeSpan.FromHours(24))", source);
    }

    // Antivirus tools flag a program that starts a hidden PowerShell with the execution policy turned off.
    // The updater starts a copy of the tool itself instead.
    [Fact]
    public void The_updater_never_starts_PowerShell()
    {
        foreach (var file in new[] { "ToolUpdate.cs", "UpdateApply.cs", "SuccessorInstall.cs", "ToolRename.cs" })
        {
            var source = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Updates", file));
            Assert.DoesNotContain("powershell", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ExecutionPolicy", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [WindowsFact]
    public void A_single_file_tool_starts_a_copy_of_itself_with_the_plan()
    {
        var root = TempFolder("coodsz-helper-");
        var tool = Path.Combine(root, "tool", "tool.cmd");
        var stage = Path.Combine(root, "stage");
        Directory.CreateDirectory(Path.GetDirectoryName(tool)!);
        Directory.CreateDirectory(stage);
        var seen = Path.Combine(root, "args.txt");
        File.WriteAllText(tool, "@echo off\r\necho %~f0 %*> \"" + seen + "\"\r\n");
        var plan = Path.Combine(stage, UpdateApply.PlanFileName);
        try
        {
            UpdateApply.StartHelper(tool, stage, plan, singleFile: true);
            var started = WaitFor(seen);

            Assert.True(started);
            var line = File.ReadAllText(seen).Trim();
            Assert.StartsWith(Path.Combine(stage, UpdateApply.HelperFolder, "tool.cmd"), line);
            Assert.Contains(UpdateApply.ApplyArgument, line);
            Assert.Contains(plan, line);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Update_folders_go_on_a_later_start_but_a_fresh_one_stays()
    {
        var temp = TempFolder("coodsz-temp-");
        var old = Directory.CreateDirectory(Path.Combine(temp, "coodsz-update-1"));
        var fresh = Directory.CreateDirectory(Path.Combine(temp, "coodsz-update-2"));
        var other = Directory.CreateDirectory(Path.Combine(temp, "something-else"));
        File.WriteAllText(Path.Combine(old.FullName, "helper.exe"), "left behind");
        Directory.SetCreationTimeUtc(old.FullName, DateTime.UtcNow.AddHours(-2));
        try
        {
            UpdateApply.RemoveOldStages(temp, TimeSpan.FromHours(1));

            Assert.False(Directory.Exists(old.FullName));
            Assert.True(Directory.Exists(fresh.FullName));
            Assert.True(Directory.Exists(other.FullName));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [WindowsFact]
    public void The_successor_install_copies_sidecars_removes_the_old_exe_and_starts_the_new_exe()
    {
        var root = TempFolder("coodsz-successor-");
        var stage = Path.Combine(root, "stage");
        var payload = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(Path.Combine(payload, "ClientAddOns", "Mod"));
        Directory.CreateDirectory(Path.Combine(dest, "ClientAddOns", "Mod"));
        var newProgram = "@echo off\r\necho new> \"%~dp0started.txt\"\r\n";
        File.WriteAllText(Path.Combine(payload, "new.cmd"), newProgram);
        File.WriteAllText(Path.Combine(payload, "Coodsz TAOM Client Launcher.exe"), "new sibling");
        File.WriteAllText(Path.Combine(payload, "launcher.json"), "default settings");
        File.WriteAllText(Path.Combine(payload, "ClientAddOns", "Mod", "SubModule.xml"), "new module");
        var oldExe = Path.Combine(dest, "old.cmd");
        File.WriteAllText(oldExe, "@echo off\r\necho old> \"%~dp0started.txt\"\r\n");
        File.WriteAllText(Path.Combine(dest, "old.dll"), "old loose file");
        File.WriteAllText(Path.Combine(dest, "Coodsz TAOM Client Launcher.exe"), "old sibling");
        File.WriteAllText(Path.Combine(dest, "Coodsz and Friends.exe"), "brand");
        File.WriteAllText(Path.Combine(dest, "launcher.json"), "saved settings");
        File.WriteAllText(Path.Combine(dest, "ClientAddOns", "Mod", "SubModule.xml"), "old module");
        var install = SuccessorInstall.Prepare(payload, "new.cmd", dest, oldExe);
        try
        {
            RunPlan(stage, ToolUpdate.SuccessorPlan(install, 2147483000, stage));
            var started = WaitFor(Path.Combine(dest, "started.txt"));

            Assert.Equal(newProgram, File.ReadAllText(Path.Combine(dest, "new.cmd")));
            Assert.Equal("new module", File.ReadAllText(Path.Combine(dest, "ClientAddOns", "Mod", "SubModule.xml")));
            Assert.False(File.Exists(oldExe));
            Assert.False(File.Exists(Path.Combine(dest, "old.dll")));
            Assert.Equal("old sibling", File.ReadAllText(Path.Combine(dest, "Coodsz TAOM Client Launcher.exe")));
            Assert.Equal("brand", File.ReadAllText(Path.Combine(dest, "Coodsz and Friends.exe")));
            Assert.Equal("saved settings", File.ReadAllText(Path.Combine(dest, "launcher.json")));
            Assert.Equal(oldExe, File.ReadAllText(Path.Combine(dest, SuccessorInstall.RenameMarker)).Trim());
            var log = File.ReadAllText(Path.Combine(dest, "update-install.log"));
            Assert.StartsWith("Installed ", log);
            Assert.Contains("Copied 1 other files", log);
            Assert.Contains("Copied " + Path.Combine(dest, "new.cmd"), log);
            Assert.Contains("Removed " + oldExe, log);
            Assert.True(started);
            Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "started.txt")).Trim());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public void A_folder_with_a_curly_quote_still_installs_and_restarts()
    {
        var root = TempFolder("Coodsz" + (char)0x2019 + "s tools-");
        var stage = Path.Combine(root, "stage");
        var payload = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(payload, "new.cmd"), "@echo off\r\necho new> \"%~dp0started.txt\"\r\n");
        var oldExe = Path.Combine(dest, "old.cmd");
        File.WriteAllText(oldExe, "@echo off\r\necho old> \"%~dp0started.txt\"\r\n");
        var install = SuccessorInstall.Prepare(payload, "new.cmd", dest, oldExe);
        try
        {
            RunPlan(stage, ToolUpdate.SuccessorPlan(install, 2147483000, stage));
            var started = WaitFor(Path.Combine(dest, "started.txt"));

            Assert.StartsWith("Installed ", File.ReadAllText(Path.Combine(dest, "update-install.log")));
            Assert.False(File.Exists(oldExe));
            Assert.True(started);
            Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "started.txt")).Trim());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public void A_failed_copy_keeps_the_old_exe_and_starts_it_again()
    {
        var root = TempFolder("coodsz-rollback-");
        var stage = Path.Combine(root, "stage");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(dest);
        var oldProgram = "@echo off\r\necho old> \"%~dp0started.txt\"\r\n";
        var oldExe = Path.Combine(dest, "old.cmd");
        File.WriteAllText(oldExe, oldProgram);
        File.WriteAllText(Path.Combine(dest, "old.dll"), "old loose file");
        // The payload is missing, so the check before the copy fails.
        var install = new SuccessorInstall(Path.Combine(stage, "files", "new.cmd"), dest, oldExe, Path.Combine(dest, "new.cmd"), [Path.Combine(dest, "old.dll")], new string('0', 64));
        try
        {
            RunPlan(stage, ToolUpdate.SuccessorPlan(install, 2147483000, stage));
            var started = WaitFor(Path.Combine(dest, "started.txt"));

            Assert.Equal(oldProgram, File.ReadAllText(oldExe));
            Assert.True(File.Exists(Path.Combine(dest, "old.dll")));
            Assert.False(File.Exists(Path.Combine(dest, "new.cmd")));
            Assert.False(File.Exists(Path.Combine(dest, SuccessorInstall.RenameMarker)));
            var log = File.ReadAllText(Path.Combine(dest, "update-install.log"));
            Assert.StartsWith("Not installed ", log);
            Assert.Contains("The copy failed.", log);
            Assert.Contains("Kept " + oldExe, log);
            Assert.True(started);
            Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "started.txt")).Trim());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public void A_download_that_does_not_match_its_hash_is_not_copied_and_an_existing_new_exe_stays()
    {
        var root = TempFolder("coodsz-hash-");
        var stage = Path.Combine(root, "stage");
        var payload = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(payload, "new.cmd"), "swapped after the check");
        var oldExe = Path.Combine(dest, "old.cmd");
        File.WriteAllText(oldExe, "@echo off\r\necho old> \"%~dp0started.txt\"\r\n");
        File.WriteAllText(Path.Combine(dest, "new.cmd"), "a new exe the player already had");
        var install = new SuccessorInstall(Path.Combine(payload, "new.cmd"), dest, oldExe, Path.Combine(dest, "new.cmd"), [], new string('A', 64));
        try
        {
            RunPlan(stage, ToolUpdate.SuccessorPlan(install, 2147483000, stage));
            var started = WaitFor(Path.Combine(dest, "started.txt"));

            Assert.Equal("a new exe the player already had", File.ReadAllText(Path.Combine(dest, "new.cmd")));
            Assert.True(File.Exists(oldExe));
            Assert.Contains("does not match the release", File.ReadAllText(Path.Combine(dest, "update-install.log")));
            Assert.True(started);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public void The_old_exe_stays_when_the_marker_cannot_be_written_or_a_shortcut_points_at_it()
    {
        foreach (var keepForShortcut in new[] { false, true })
        {
            var root = TempFolder("coodsz-keep-");
            var stage = Path.Combine(root, "stage");
            var payload = Path.Combine(stage, "files");
            var dest = Path.Combine(root, "dest");
            Directory.CreateDirectory(payload);
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(payload, "new.cmd"), "@echo off\r\necho new> \"%~dp0started.txt\"\r\n");
            var oldExe = Path.Combine(dest, "old.cmd");
            File.WriteAllText(oldExe, "old program");
            // A folder in the marker's place makes the marker write fail.
            if (!keepForShortcut)
                Directory.CreateDirectory(Path.Combine(dest, SuccessorInstall.RenameMarker));
            var install = SuccessorInstall.Prepare(payload, "new.cmd", dest, oldExe, keepOldExe: keepForShortcut);
            try
            {
                RunPlan(stage, ToolUpdate.SuccessorPlan(install, 2147483000, stage));
                var started = WaitFor(Path.Combine(dest, "started.txt"));

                Assert.Equal("old program", File.ReadAllText(oldExe));
                var log = File.ReadAllText(Path.Combine(dest, "update-install.log"));
                Assert.StartsWith("Installed ", log);
                Assert.Contains(keepForShortcut ? "because a shortcut points at it" : "Could not write " + SuccessorInstall.RenameMarker, log);
                if (keepForShortcut)
                    Assert.True(File.Exists(Path.Combine(dest, SuccessorInstall.RenameMarker)));
                Assert.True(started);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Only_shortcuts_to_the_old_exe_in_this_folder_are_repointed()
    {
        var store = new FakeShortcuts(new()
        {
            [@"C:\Users\p\Desktop\Server.lnk"] = @"C:\Games\Coop\CoodszHost.exe",
            [@"C:\Users\p\Desktop\Other copy.lnk"] = @"D:\Old\CoodszHost.exe",
            [@"C:\Users\p\Desktop\Steam.lnk"] = @"C:\Steam\steam.exe",
            [@"C:\Pinned\TaskBar\Server.lnk"] = @"C:\GAMES\coop\coodszhost.exe",
            [@"C:\Pinned\TaskBar\Client.lnk"] = @"C:\Games\Coop\Coodsz Coop and Friends.exe"
        });
        var old = ToolRename.OldExePaths(@"C:\Games\Coop", "Coodsz Coop Server Tool.exe");

        var result = ToolRename.RetargetShortcuts(store, old, @"C:\Games\Coop\Coodsz Coop Server Tool.exe");

        Assert.Equal(new ShortcutResult(2, 0), result);
        Assert.Equal(@"C:\Games\Coop\Coodsz Coop Server Tool.exe", store.Targets[@"C:\Users\p\Desktop\Server.lnk"]);
        Assert.Equal(@"C:\Games\Coop\Coodsz Coop Server Tool.exe", store.Targets[@"C:\Pinned\TaskBar\Server.lnk"]);
        Assert.Equal(@"D:\Old\CoodszHost.exe", store.Targets[@"C:\Users\p\Desktop\Other copy.lnk"]);
        Assert.Equal(@"C:\Steam\steam.exe", store.Targets[@"C:\Users\p\Desktop\Steam.lnk"]);
        // The client launcher's shortcut is another role, so the server tool leaves it.
        Assert.Equal(@"C:\Games\Coop\Coodsz Coop and Friends.exe", store.Targets[@"C:\Pinned\TaskBar\Client.lnk"]);
    }

    [Fact]
    public void The_new_exe_waits_for_the_old_one_then_removes_it_and_the_marker()
    {
        var host = new FakeHost();
        const string folder = @"C:\Games\Coop";
        var old = Path.Combine(folder, "Coodsz Coop and Friends.exe");
        host.Files.Add(Path.Combine(folder, "Coodsz Coop Client Launcher.exe"));
        host.Files.Add(old);
        host.Files.Add(Path.Combine(folder, "Coodsz and Friends.exe"));
        host.Files.Add(Path.Combine(folder, "CoodszHost.exe"));
        host.Files.Add(Path.Combine(folder, SuccessorInstall.RenameMarker));
        host.RunningChecksLeft[old] = 2;
        var shortcuts = new FakeShortcuts(new()
        {
            [@"C:\Desktop\Client.lnk"] = old,
            [@"C:\Desktop\More Nations.lnk"] = Path.Combine(folder, "Coodsz and Friends.exe")
        });

        Assert.True(ToolRename.NeedsCleanup(host, folder, "Coodsz Coop Client Launcher.exe"));
        ToolRename.Cleanup(host, shortcuts, folder, "Coodsz Coop Client Launcher.exe", TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(old, host.Files);
        Assert.DoesNotContain(Path.Combine(folder, SuccessorInstall.RenameMarker), host.Files);
        // The server tool's old exe is another role. Both old client exes end in the one client launcher.
        Assert.Contains(Path.Combine(folder, "CoodszHost.exe"), host.Files);
        Assert.DoesNotContain(Path.Combine(folder, "Coodsz and Friends.exe"), host.Files);
        Assert.Contains(Path.Combine(folder, "Coodsz Coop Client Launcher.exe"), host.Files);
        Assert.Equal(Path.Combine(folder, "Coodsz Coop Client Launcher.exe"), shortcuts.Targets[@"C:\Desktop\Client.lnk"]);
        Assert.Equal(Path.Combine(folder, "Coodsz Coop Client Launcher.exe"), shortcuts.Targets[@"C:\Desktop\More Nations.lnk"]);
        Assert.Empty(host.LogLines);
    }

    [Fact]
    public void A_shortcut_to_an_old_exe_the_install_already_removed_still_moves()
    {
        var host = new FakeHost();
        const string folder = @"C:\T";
        host.Files.Add(Path.Combine(folder, SuccessorInstall.RenameMarker));
        var shortcuts = new FakeShortcuts(new() { [@"C:\Desktop\Server.lnk"] = Path.Combine(folder, "Coodsz Dedicated TAOM Server Tool.exe") });

        Assert.True(ToolRename.NeedsCleanup(host, folder, "Coodsz TAOM Server Tool.exe"));
        ToolRename.Cleanup(host, shortcuts, folder, "Coodsz TAOM Server Tool.exe", TimeSpan.FromSeconds(1));

        Assert.Equal(Path.Combine(folder, "Coodsz TAOM Server Tool.exe"), shortcuts.Targets[@"C:\Desktop\Server.lnk"]);
        Assert.DoesNotContain(Path.Combine(folder, SuccessorInstall.RenameMarker), host.Files);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_shortcut_that_cannot_be_moved_keeps_the_old_exe_and_the_marker(bool unreadable)
    {
        var host = new FakeHost();
        const string folder = @"C:\T";
        var old = Path.Combine(folder, "Coodsz Dedicated TAOM Server Tool.exe");
        host.Files.Add(old);
        host.Files.Add(Path.Combine(folder, SuccessorInstall.RenameMarker));
        var shortcuts = new FakeShortcuts(new() { [@"C:\Users\Public\Desktop\Server.lnk"] = old });
        if (unreadable)
        {
            // Unreadable, but its bytes hold the full old exe path.
            shortcuts.Unreadable.Add(@"C:\Users\Public\Desktop\Server.lnk");
            shortcuts.Mentions.Add(@"C:\Users\Public\Desktop\Server.lnk");
        }
        else
            shortcuts.ReadOnly.Add(@"C:\Users\Public\Desktop\Server.lnk");

        ToolRename.Cleanup(host, shortcuts, folder, "Coodsz TAOM Server Tool.exe", TimeSpan.FromSeconds(1));

        Assert.Contains(old, host.Files);
        Assert.Contains(Path.Combine(folder, SuccessorInstall.RenameMarker), host.Files);
        Assert.Contains("could not be moved", Assert.Single(host.LogLines));
        Assert.True(ToolRename.ShortcutsMayPointAt(shortcuts, old));
    }

    [Fact]
    public void The_install_only_keeps_the_old_exe_when_a_shortcut_may_point_at_it()
    {
        var shortcuts = new FakeShortcuts(new() { [@"C:\Desktop\Other.lnk"] = @"D:\Elsewhere\CoodszHost.exe" });

        Assert.False(ToolRename.ShortcutsMayPointAt(shortcuts, @"C:\T\CoodszHost.exe"));
        shortcuts.Targets[@"C:\Desktop\Server.lnk"] = @"C:\T\CoodszHost.exe";
        Assert.True(ToolRename.ShortcutsMayPointAt(shortcuts, @"C:\T\CoodszHost.exe"));
    }

    [Fact]
    public void An_old_exe_that_keeps_running_stays_for_the_next_start()
    {
        var host = new FakeHost();
        const string folder = @"C:\T";
        var old = Path.Combine(folder, "Coodsz Dedicated TAOM Server Tool.exe");
        host.Files.Add(old);
        host.RunningChecksLeft[old] = int.MaxValue;

        ToolRename.Cleanup(host, new FakeShortcuts(new()), folder, "Coodsz TAOM Server Tool.exe", TimeSpan.FromMilliseconds(300));

        Assert.Contains(old, host.Files);
    }

    [Fact]
    public void Nothing_is_cleaned_under_an_old_name_or_without_leftovers()
    {
        var host = new FakeHost();
        host.Files.Add(@"C:\T\CoodszHost.exe");

        // The bridge release itself runs under the old names.
        Assert.False(ToolRename.NeedsCleanup(host, @"C:\T", "CoodszHost.exe"));
        Assert.False(ToolRename.NeedsCleanup(host, @"C:\T", "Coodsz TAOM Server Tool.exe"));
        Assert.False(ToolRename.NeedsCleanup(host, @"C:\T", "Coodsz TAOM Client Launcher.exe"));
        Assert.Empty(ToolRename.OldExePaths(@"C:\T", "CoodszHost.exe"));
    }

    [Fact]
    public void A_new_build_under_an_old_name_copies_itself_to_the_new_name_and_starts_it()
    {
        var host = new FakeHost();
        const string old = @"C:\T\Coodsz Dedicated TAOM Server Tool.exe";
        host.Files.Add(old);

        Assert.Equal(@"C:\T\Coodsz TAOM Server Tool.exe", ToolRename.SelfRenameTarget(old, "Coodsz TAOM Server Tool"));
        Assert.True(ToolRename.TrySelfRename(host, old, "Coodsz TAOM Server Tool", ["--flag"]));
        Assert.Equal((old, @"C:\T\Coodsz TAOM Server Tool.exe"), host.Copies.Single());
        Assert.Equal((@"C:\T\Coodsz TAOM Server Tool.exe", "--flag"), host.Starts.Single());
    }

    [Fact]
    public void A_running_new_exe_is_never_started_twice_or_overwritten()
    {
        var host = new FakeHost();
        const string old = @"C:\T\CoodszHost.exe";
        const string target = @"C:\T\Coodsz Coop Server Tool.exe";
        host.Files.Add(old);
        host.Files.Add(target);
        host.RunningChecksLeft[target] = int.MaxValue;
        host.Versions[old] = new Version(1, 3, 1, 0);
        host.Versions[target] = new Version(1, 3, 0, 0);

        Assert.True(ToolRename.TrySelfRename(host, old, "Coodsz Coop Server Tool", []));
        Assert.Empty(host.Copies);
        Assert.Empty(host.Starts);
    }

    [Theory]
    [InlineData("1.3.1.0", "1.3.0.0", true)]
    [InlineData("1.3.0.0", "1.3.0.0", false)]
    [InlineData("1.3.0.0", "1.3.1.0", false)]
    [InlineData("1.3.0.0", null, false)]
    public void An_existing_new_exe_is_only_overwritten_by_a_newer_build(string mine, string? theirs, bool overwrite)
    {
        var host = new FakeHost();
        const string old = @"C:\T\CoodszHost.exe";
        const string target = @"C:\T\Coodsz Coop Server Tool.exe";
        host.Files.Add(old);
        host.Files.Add(target);
        host.Versions[old] = Version.Parse(mine);
        if (theirs != null)
            host.Versions[target] = Version.Parse(theirs);

        Assert.True(ToolRename.TrySelfRename(host, old, "Coodsz Coop Server Tool", []));
        Assert.Equal(overwrite ? 1 : 0, host.Copies.Count);
        Assert.Equal(target, host.Starts.Single().Exe);
    }

    [Theory]
    // The bridge build: its identity is its old name, so it stays where it is.
    [InlineData(@"C:\T\CoodszHost.exe", "CoodszHost")]
    [InlineData(@"C:\T\Coodsz Dedicated TAOM Server Tool.exe", "Coodsz Dedicated TAOM Server Tool")]
    // Already under the new name.
    [InlineData(@"C:\T\Coodsz TAOM Server Tool.exe", "Coodsz TAOM Server Tool")]
    // Another tool's build in this exe: never rename across tools or products.
    [InlineData(@"C:\T\Coodsz Dedicated TAOM Server Tool.exe", "Coodsz Coop Server Tool")]
    [InlineData(@"C:\T\CoodszHost.exe", "Coodsz Coop Client Launcher")]
    [InlineData(@"C:\T\Coodsz TAOM Client Launcher.exe", "Coodsz TAOM Client Launcher")]
    [InlineData(@"C:\T\Coodsz and Friends.exe", "Coodsz Coop Server Tool")]
    public void No_self_rename_unless_the_build_is_this_tool_under_its_new_name(string processPath, string identity)
    {
        var host = new FakeHost();

        Assert.Null(ToolRename.SelfRenameTarget(processPath, identity));
        Assert.False(ToolRename.TrySelfRename(host, processPath, identity, []));
        Assert.Empty(host.Copies);
        Assert.Empty(host.Starts);
    }

    [Fact]
    public void A_failed_self_copy_keeps_running_under_the_old_name()
    {
        var host = new FakeHost { CopyWorks = false };

        Assert.False(ToolRename.TrySelfRename(host, @"C:\T\CoodszHost.exe", "Coodsz Coop Server Tool", []));
        Assert.Empty(host.Starts);
    }

    [WindowsFact]
    public void The_windows_shortcut_store_reads_and_repoints_a_real_shortcut()
    {
        var root = TempFolder("coodsz-lnk-");
        var oldExe = Path.Combine(root, "CoodszHost.exe");
        var newExe = Path.Combine(root, "Coodsz Coop Server Tool.exe");
        File.WriteAllText(oldExe, "old");
        File.WriteAllText(newExe, "new");
        var lnk = Path.Combine(root, "Server.lnk");
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            Assert.NotNull(shellType);
            var shell = Activator.CreateInstance(shellType!)!;
            var link = shellType!.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [lnk])!;
            link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, [oldExe]);
            link.GetType().InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, link, [oldExe + ",0"]);
            link.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
            var store = new WindowsShortcutStore();

            Assert.True(store.TryTarget(lnk, out var before));
            Assert.True(ToolRename.SamePath(oldExe, before));
            Assert.False(store.TryRetarget(lnk, Path.Combine(root, "Other.exe"), newExe));
            Assert.True(store.TryRetarget(lnk, oldExe, newExe));
            Assert.True(store.TryTarget(lnk, out var after));
            Assert.True(ToolRename.SamePath(newExe, after));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void An_unreadable_shortcut_that_does_not_hold_the_old_path_is_ignored()
    {
        var host = new FakeHost();
        const string folder = @"C:\T";
        var old = Path.Combine(folder, "Coodsz Dedicated TAOM Server Tool.exe");
        host.Files.Add(old);
        var shortcuts = new FakeShortcuts(new() { [@"C:\Desktop\Broken.lnk"] = @"D:\Other\Coodsz Dedicated TAOM Server Tool.exe" });
        shortcuts.Unreadable.Add(@"C:\Desktop\Broken.lnk");

        Assert.Equal(new ShortcutResult(0, 0), ToolRename.RetargetShortcuts(shortcuts, [old], Path.Combine(folder, "Coodsz TAOM Server Tool.exe")));
        Assert.False(ToolRename.ShortcutsMayPointAt(shortcuts, old));
        ToolRename.Cleanup(host, shortcuts, folder, "Coodsz TAOM Server Tool.exe", TimeSpan.FromSeconds(1));

        Assert.DoesNotContain(old, host.Files);
        Assert.Empty(host.LogLines);
    }

    [Fact]
    public void Without_the_Windows_Shell_the_old_exe_stays_and_the_log_says_so_once()
    {
        var host = new FakeHost();
        const string folder = @"C:\T";
        var old = Path.Combine(folder, "Coodsz Dedicated TAOM Server Tool.exe");
        host.Files.Add(old);
        var shortcuts = new FakeShortcuts(new()) { ShellAvailable = false };

        for (var start = 0; start < 3; start++)
        {
            host.Now = host.Now.AddDays(1);
            ToolRename.Cleanup(host, shortcuts, folder, "Coodsz TAOM Server Tool.exe", TimeSpan.FromSeconds(1));
        }

        Assert.Contains(old, host.Files);
        Assert.Contains("WScript.Shell", Assert.Single(host.LogLines));
        Assert.True(ToolRename.ShortcutsMayPointAt(shortcuts, old));
    }

    [Fact]
    public void A_stuck_shortcut_adds_at_most_one_log_line_a_day()
    {
        var host = new FakeHost();
        const string folder = @"C:\T";
        var old = Path.Combine(folder, "Coodsz Dedicated TAOM Server Tool.exe");
        host.Files.Add(old);
        var shortcuts = new FakeShortcuts(new() { [@"C:\Users\Public\Desktop\Server.lnk"] = old });
        shortcuts.ReadOnly.Add(@"C:\Users\Public\Desktop\Server.lnk");

        for (var start = 0; start < 5; start++)
        {
            host.Now = host.Now.AddHours(1);
            ToolRename.Cleanup(host, shortcuts, folder, "Coodsz TAOM Server Tool.exe", TimeSpan.FromSeconds(1));
        }
        host.Now = host.Now.AddDays(1);
        ToolRename.Cleanup(host, shortcuts, folder, "Coodsz TAOM Server Tool.exe", TimeSpan.FromSeconds(1));

        Assert.Equal(2, host.LogLines.Count);
        Assert.Contains(old, host.Files);
    }

    [Fact]
    public void An_old_exe_that_exits_for_a_running_new_exe_leaves_one_log_line()
    {
        var host = new FakeHost();
        const string old = @"C:\T\CoodszHost.exe";
        const string target = @"C:\T\Coodsz Coop Server Tool.exe";
        host.Files.Add(old);
        host.Files.Add(target);
        host.RunningChecksLeft[target] = int.MaxValue;

        Assert.True(ToolRename.TrySelfRename(host, old, "Coodsz Coop Server Tool", []));
        Assert.True(ToolRename.TrySelfRename(host, old, "Coodsz Coop Server Tool", []));

        Assert.Contains("CoodszHost.exe closed because Coodsz Coop Server Tool.exe is already running.", Assert.Single(host.LogLines));
    }

    [WindowsFact]
    public void The_legacy_install_works_in_a_folder_with_a_curly_quote()
    {
        var root = TempFolder("Coodsz" + (char)0x2019 + "s legacy-");
        var stage = Path.Combine(root, "stage");
        var source = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(Path.Combine(source, "DedicatedServerRepair"));
        Directory.CreateDirectory(dest);
        var program = "@echo off\r\necho opened> \"%~dp0reopened.txt\"\r\n";
        File.WriteAllText(Path.Combine(source, "tool.cmd"), program);
        File.WriteAllText(Path.Combine(source, "TaomCoop.Core.dll"), "new shared");
        File.WriteAllText(Path.Combine(source, "launcher.json"), "default settings");
        File.WriteAllText(Path.Combine(source, "DedicatedServerRepair", "fix.txt"), "new fix");
        File.WriteAllText(Path.Combine(dest, "tool.cmd"), "old program");
        File.WriteAllText(Path.Combine(dest, "tool.dll"), "stale");
        File.WriteAllText(Path.Combine(dest, "TaomCoop.Core.dll"), "old shared");
        File.WriteAllText(Path.Combine(dest, "launcher.json"), "saved settings");
        // A loose sibling tool unzipped into the same folder.
        File.WriteAllText(Path.Combine(dest, "Coodsz Dedicated TAOM Server Tool.exe"), "sibling");
        File.WriteAllText(Path.Combine(dest, "Coodsz Dedicated TAOM Server Tool.dll"), "sibling");
        File.WriteAllText(Path.Combine(dest, "ServerOnly.dll"), "sibling");
        try
        {
            RunPlan(stage, ToolUpdate.LegacyPlan(source, dest, Path.Combine(dest, "tool.cmd"), 2147483000, stage));
            var opened = WaitFor(Path.Combine(dest, "reopened.txt"));

            Assert.Equal(program, File.ReadAllText(Path.Combine(dest, "tool.cmd")));
            Assert.Equal("new shared", File.ReadAllText(Path.Combine(dest, "TaomCoop.Core.dll")));
            Assert.Equal("new fix", File.ReadAllText(Path.Combine(dest, "DedicatedServerRepair", "fix.txt")));
            Assert.False(File.Exists(Path.Combine(dest, "tool.dll")));
            Assert.Equal("saved settings", File.ReadAllText(Path.Combine(dest, "launcher.json")));
            Assert.Equal("sibling", File.ReadAllText(Path.Combine(dest, "Coodsz Dedicated TAOM Server Tool.exe")));
            Assert.Equal("sibling", File.ReadAllText(Path.Combine(dest, "Coodsz Dedicated TAOM Server Tool.dll")));
            Assert.Equal("sibling", File.ReadAllText(Path.Combine(dest, "ServerOnly.dll")));
            Assert.StartsWith("Installed ", File.ReadAllText(Path.Combine(dest, "update-install.log")));
            Assert.True(opened);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // Every Coodsz tool a player might unzip into one folder, under the names they ship with today,
    // the TAOM tools as loose files, plus an exe and a dll the player put there.
    private static readonly string[] SharedFolderFiles =
    [
        "CoodszHost.exe", "Coodsz Coop and Friends.exe", "Coodsz and Friends.exe",
        "Coodsz Dedicated TAOM Server Tool.exe", "Coodsz Dedicated TAOM Server Tool.dll",
        "Coodsz Dedicated TAOM Server Tool.deps.json", "Coodsz Dedicated TAOM Server Tool.runtimeconfig.json",
        "Coodsz TAOM Client Launcher.exe", "Coodsz TAOM Client Launcher.dll",
        "Coodsz TAOM Client Launcher.deps.json", "Coodsz TAOM Client Launcher.runtimeconfig.json",
        "BannerlordCoopLauncher.Core.dll", "TaomCoop.Core.dll", "LiteNetLib.dll", "Mono.Cecil.dll",
        "Coodsz AD1259 Server.exe", "Coodsz AD1259 Client Launcher.exe",
        "PlayerTool.exe", "PlayerHelper.dll",
        "launcher.json", "host.json", "taom-launcher.json", "taom-server.json"
    ];

    [WindowsTheory]
    [InlineData("CoodszHost.exe")]
    [InlineData("Coodsz Coop and Friends.exe")]
    [InlineData("Coodsz and Friends.exe")]
    [InlineData("Coodsz Dedicated TAOM Server Tool.exe")]
    [InlineData("Coodsz TAOM Client Launcher.exe")]
    [InlineData("Coodsz AD1259 Server.exe")]
    [InlineData("Coodsz AD1259 Client Launcher.exe")]
    public void A_legacy_update_in_a_shared_folder_keeps_every_other_tool_and_the_players_files(string exe)
    {
        var root = TempFolder("coodsz-shared-");
        var stage = Path.Combine(root, "stage");
        var source = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        foreach (var name in SharedFolderFiles)
            File.WriteAllText(Path.Combine(dest, name), "before");
        var tool = Path.GetFileNameWithoutExtension(exe);
        // Its own leftover from an older version, the only file this update may remove.
        File.WriteAllText(Path.Combine(dest, tool + ".pdb"), "stale");
        // The payload holds only this tool and its own loose files, the worst case: no zip carries the others.
        File.WriteAllText(Path.Combine(source, exe), "updated");
        foreach (var name in SharedFolderFiles.Where(name => ToolUpdate.IsOwnFile(name, exe) && !name.Equals(exe, StringComparison.OrdinalIgnoreCase)))
            File.WriteAllText(Path.Combine(source, name), "updated");
        try
        {
            RunPlan(stage, ToolUpdate.LegacyPlan(source, dest, Path.Combine(dest, exe), 2147483000, stage));

            Assert.StartsWith("Installed ", File.ReadAllText(Path.Combine(dest, "update-install.log")));
            Assert.Equal("updated", File.ReadAllText(Path.Combine(dest, exe)));
            Assert.False(File.Exists(Path.Combine(dest, tool + ".pdb")));
            foreach (var name in SharedFolderFiles.Where(name => !name.Equals(exe, StringComparison.OrdinalIgnoreCase)))
                Assert.True(File.Exists(Path.Combine(dest, name)), name + " was removed by the update of " + exe);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("CoodszHost.exe", "Coodsz Coop Server Tool.exe")]
    [InlineData("Coodsz Coop and Friends.exe", "Coodsz Coop Client Launcher.exe")]
    [InlineData("Coodsz and Friends.exe", "Coodsz Coop Client Launcher.exe")]
    [InlineData("Coodsz Dedicated TAOM Server Tool.exe", "Coodsz TAOM Server Tool.exe")]
    [InlineData("Coodsz TAOM Client Launcher.exe", "Coodsz TAOM Client Launcher.exe")]
    [InlineData("Coodsz AD1259 Server.exe", "Coodsz AD1259 Server Tool.exe")]
    public void The_two_exe_install_and_the_first_start_never_list_another_tools_file(string oldExe, string newExe)
    {
        var root = TempFolder("coodsz-shared-new-");
        var payload = Path.Combine(root, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(dest);
        foreach (var name in SharedFolderFiles)
            File.WriteAllText(Path.Combine(dest, name), "before");
        File.WriteAllText(Path.Combine(payload, newExe), "new");
        try
        {
            var install = SuccessorInstall.Prepare(payload, newExe, dest, Path.Combine(dest, oldExe));
            var firstStart = ToolUpdate.PreviousVersionFiles(dest, newExe, singleFile: true);

            foreach (var path in install.RemoveAfterCopy.Concat(firstStart))
            {
                var name = Path.GetFileName(path);
                Assert.True(ToolUpdate.IsOwnFile(name, newExe) || ToolUpdate.IsOwnFile(name, oldExe), name + " is not " + newExe + "'s own file.");
                Assert.DoesNotContain(name, new[] { "PlayerTool.exe", "PlayerHelper.dll" });
                Assert.False(ToolNames.IsKnownExe(name) && ToolNames.Find(name) != ToolNames.Find(newExe), name);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("PlayerTool.exe", false)]
    [InlineData("PlayerHelper.dll", false)]
    [InlineData("Coodsz and Friends.exe", false)]
    [InlineData("Coodsz TAOM Client Launcher.dll", false)]
    [InlineData("Coodsz Dedicated TAOM Server Tool.dll", true)]
    [InlineData("Coodsz Dedicated TAOM Server Tool.runtimeconfig.json", true)]
    [InlineData("Coodsz TAOM Server Tool.exe.old", true)]
    [InlineData("TaomCoop.Core.dll", true)]
    [InlineData("TaomCoop.Core.pdb", true)]
    [InlineData("TaomCoop.Core.exe", false)]
    [InlineData("taom-server.json", false)]
    public void Only_files_named_after_the_tool_or_its_old_loose_libraries_are_its_own(string fileName, bool own)
    {
        Assert.Equal(own, ToolUpdate.IsOwnFile(fileName, "Coodsz Dedicated TAOM Server Tool.exe"));
    }

    [WindowsFact]
    public void A_sibling_the_zip_carries_is_only_copied_into_a_folder_that_already_has_it()
    {
        var root = TempFolder("coodsz-carry-");
        var stage = Path.Combine(root, "stage");
        var source = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(Path.Combine(source, "ClientAddOns"));
        Directory.CreateDirectory(dest);
        foreach (var name in new[] { "Coodsz Coop and Friends.exe", "CoodszHost.exe", "Coodsz and Friends.exe", "LICENSE.txt", Path.Combine("ClientAddOns", "a.dll") })
            File.WriteAllText(Path.Combine(source, name), "new");
        File.WriteAllText(Path.Combine(dest, "Coodsz and Friends.exe"), "brand");
        try
        {
            Assert.Equal(new[] { "CoodszHost.exe" }, ToolUpdate.AbsentSiblingFiles(source, dest, "Coodsz Coop and Friends.exe"));
            RunPlan(stage, ToolUpdate.LegacyPlan(source, dest, Path.Combine(dest, "Coodsz Coop and Friends.exe"), 2147483000, stage));

            Assert.StartsWith("Installed ", File.ReadAllText(Path.Combine(dest, "update-install.log")));
            Assert.False(File.Exists(Path.Combine(dest, "CoodszHost.exe")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "Coodsz and Friends.exe")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "LICENSE.txt")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "ClientAddOns", "a.dll")));
            // The fake exe is only recorded, never opened (Windows showed "Unsupported 16-Bit Application" for it).
            Assert.Contains(Path.Combine(dest, "Coodsz Coop and Friends.exe"), SafeToolStart.Started);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Every_tool_runs_the_rename_step_before_anything_else()
    {
        foreach (var project in new[] { "BannerlordCoopLauncher", "CoodszHost", "TaomCoopHost", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresHost", "BurningEmpiresLauncher" })
        {
            var source = SourceFiles.Read(Path.Combine("src", project, "App.xaml.cs"));
            var rename = source.IndexOf("ToolRename.OnStartup()", StringComparison.Ordinal);
            Assert.True(rename > 0, project + " does not call ToolRename.OnStartup.");
            // The update helper runs before anything else, so it never reads or changes the tool's folder.
            var helper = source.IndexOf("UpdateApply.OnStartup(", StringComparison.Ordinal);
            Assert.True(helper > 0 && helper < rename, project + " does not run the update helper first.");
            var foldIn = source.IndexOf("MoreNationsFoldIn.CameFromFriends", StringComparison.Ordinal);
            Assert.True(foldIn < 0 || helper < foldIn, project);
            Assert.True(rename < source.IndexOf("ToolUpdate.RemovePreviousVersion", StringComparison.Ordinal), project);
            Assert.True(rename < source.IndexOf("UiLanguage.Start()", StringComparison.Ordinal), project);
        }
    }

    private static string TempFolder(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    // Writes the plan the way InstallAsync does and runs it the way the helper exe does, through the
    // update argument, in this process. The pid in every test plan is not running, so nothing waits.
    internal static void RunPlan(string stage, UpdatePlan plan)
    {
        Directory.CreateDirectory(stage);
        var path = Path.Combine(stage, UpdateApply.PlanFileName);
        UpdateApply.WritePlanAsync(path, plan).GetAwaiter().GetResult();
        Assert.True(UpdateApply.OnStartup([UpdateApply.ApplyArgument, path], Path.Combine(stage, UpdateApply.HelperFolder, "tool.exe")));
    }

    private static bool WaitFor(string path)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!File.Exists(path) && DateTime.UtcNow < until)
            Thread.Sleep(100);
        // Give the started program a moment to finish writing.
        Thread.Sleep(200);
        return File.Exists(path);
    }

    private sealed class FakeShortcuts(Dictionary<string, string> targets) : IShortcutStore
    {
        public Dictionary<string, string> Targets { get; } = targets;
        public HashSet<string> Unreadable { get; } = [];
        public HashSet<string> ReadOnly { get; } = [];
        public HashSet<string> Mentions { get; } = [];
        public bool ShellAvailable { get; set; } = true;

        public bool MentionsPath(string shortcutPath, string path) => Mentions.Contains(shortcutPath);

        public IEnumerable<string> Candidates(IReadOnlyCollection<string> fileNames) => Targets.Keys.ToList();

        public bool TryTarget(string shortcutPath, out string target)
        {
            target = Unreadable.Contains(shortcutPath) ? "" : Targets.GetValueOrDefault(shortcutPath) ?? "";
            return !Unreadable.Contains(shortcutPath);
        }

        public bool TryRetarget(string shortcutPath, string oldTarget, string newTarget)
        {
            if (ReadOnly.Contains(shortcutPath) || !ToolRename.SamePath(Targets[shortcutPath], oldTarget))
                return false;
            Targets[shortcutPath] = newTarget;
            return true;
        }
    }

    private sealed class FakeHost : IRenameHost
    {
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> RunningChecksLeft { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Version> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string From, string To)> Copies { get; } = [];
        public List<(string Exe, string Arguments)> Starts { get; } = [];
        public List<string> LogLines { get; } = [];
        public bool CopyWorks { get; init; } = true;
        public DateTime Now { get; set; } = new(2026, 10, 27, 9, 0, 0);

        public bool FileExists(string path) => Files.Contains(path);

        public bool TryCopy(string from, string to)
        {
            if (!CopyWorks)
                return false;
            Copies.Add((from, to));
            Files.Add(to);
            return true;
        }

        public bool TryDelete(string path)
        {
            if (RunningChecksLeft.GetValueOrDefault(path) > 0)
                return false;
            Files.Remove(path);
            return true;
        }

        public bool IsRunning(string exePath)
        {
            var left = RunningChecksLeft.GetValueOrDefault(exePath);
            if (left <= 0)
                return false;
            if (left != int.MaxValue)
                RunningChecksLeft[exePath] = left - 1;
            return true;
        }

        public bool TryStart(string exePath, IReadOnlyList<string> arguments)
        {
            Starts.Add((exePath, string.Join(" ", arguments)));
            return true;
        }

        public Version? FileVersion(string path) => Versions.GetValueOrDefault(path);

        public void Log(string folder, string key, string line, bool once)
        {
            if (PhysicalRenameHost.ShouldLog(LogLines, key, once, Now))
                LogLines.Add(PhysicalRenameHost.LogLine(key, line, Now));
        }
    }
}
