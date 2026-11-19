using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomStackTests
{
    [Fact]
    public void Client_order_keeps_coop_next_to_the_compat_module()
    {
        Assert.True(TaomStack.CoopIsAdjacent(TaomStack.ClientModules));
        Assert.True(TaomStack.CoopIsAdjacent(TaomStack.ServerModules));
        Assert.DoesNotContain("CoopNightly", TaomStack.ClientModules);
        Assert.Equal("TAOM.CoopCompat", TaomStack.ClientModules[^1]);
        Assert.DoesNotContain("DedicatedServer.Windows", TaomStack.ClientModules);
        Assert.Equal("DedicatedServer.Windows", TaomStack.ServerModules[^1]);
    }

    [Fact]
    public void Coop_identity_file_gains_Coop()
    {
        var path = Path.Combine(Path.GetTempPath(), "taom-coop-" + Guid.NewGuid().ToString("N"), "coop-modules.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // An older TAOM list only names the retired CoopNightly id.
        File.WriteAllText(path, "# list\r\n\r\n[modules]\r\nCoopNightly\r\n\r\n[harmony-owner-prefixes]\r\n");

        TaomArchive.EnsureCoopListed(path);
        var text = File.ReadAllText(path);

        var lines = text.Split("\r\n");
        Assert.Contains("Coop", lines);
        Assert.True(Array.IndexOf(lines, "Coop") < Array.IndexOf(lines, "[harmony-owner-prefixes]"));
        TaomArchive.EnsureCoopListed(path);
        Assert.Equal(text, File.ReadAllText(path));
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void Install_reports_a_missing_module_and_a_wrong_compat_version()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "TAOM.CoopCompat"));
        File.WriteAllText(
            Path.Combine(root, "Modules", "TAOM.CoopCompat", "SubModule.xml"),
            "<Module><Id value=\"TAOM.CoopCompat\"/><Version value=\"v0.3.16\"/></Module>");

        Assert.Equal("TAOM is missing.", TaomArchive.ContentProblem(root));
        Assert.Equal("TAOM.CoopCompat is v0.3.16. The playtest needs v0.3.25.", TaomArchive.CompatProblem(root));
        Directory.Delete(root, recursive: true);
    }

    // Player report CR-XXP67 (TAOM #85): the official TAOM.CoopCompat v2.0.40 from the full TAOM download for
    // Bannerlord 1.5.7, laid out like that download (no Bootstrap DLL, a Coop DLL in both bins).
    private static string NewCompat(string modules, string? version = "v2.0.40", bool coopDll = true)
    {
        var module = Path.Combine(modules, "TAOM.CoopCompat");
        foreach (var bin in new[] { "Win64_Shipping_Client", "Win64_Shipping_Server" })
        {
            Directory.CreateDirectory(Path.Combine(module, "bin", bin));
            File.WriteAllText(Path.Combine(module, "bin", bin, "TAOM.CoopCompat.dll"), "official " + bin);
            if (coopDll)
                File.WriteAllText(Path.Combine(module, "bin", bin, "TAOM.CoopCompat.Coop.dll"), "official coop " + bin);
        }

        if (version != null)
            File.WriteAllText(Path.Combine(module, "SubModule.xml"), "<Module>\r\n  <Id value=\"TAOM.CoopCompat\" />\r\n  <Version value=\"" + version + "\" />\r\n</Module>");
        File.WriteAllText(Path.Combine(module, "README.txt"), "1.5.7 instructions");
        return module;
    }

    [Fact]
    public void The_new_TAOM_download_compat_is_found_by_version_or_its_Coop_dll_and_the_kit_one_is_not()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-newcompat-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal("v2.0.40", NewTaomCompat.Found(NewCompat(Path.Combine(root, "a"))));
            Assert.Equal("v3.1", NewTaomCompat.Found(NewCompat(Path.Combine(root, "b"), "v3.1", coopDll: false)));
            Assert.Equal("", NewTaomCompat.Found(NewCompat(Path.Combine(root, "c"), version: null)));
            // The kit 0.3.25 (and older kits) has a Bootstrap DLL and no Coop DLL.
            var kit = NewCompat(Path.Combine(root, "d"), "v0.3.25", coopDll: false);
            File.WriteAllText(Path.Combine(kit, "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.Bootstrap.dll"), "kit");
            Assert.Null(NewTaomCompat.Found(kit));
            Assert.Null(NewTaomCompat.Found(NewCompat(Path.Combine(root, "e"), "v0.3.17", coopDll: false)));
            // A file of the v2.0.40 size that is not that file.
            var sized = NewCompat(Path.Combine(root, "f"), "v0.3.25", coopDll: false);
            File.WriteAllBytes(Path.Combine(sized, "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.dll"), new byte[NewTaomCompat.DllLength]);
            Assert.Null(NewTaomCompat.Found(sized));
            Assert.Null(NewTaomCompat.Found(Path.Combine(root, "none")));
            Assert.Null(NewTaomCompat.Found(null));
            Assert.Null(NewTaomCompat.Problem(kit));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_new_TAOM_download_compat_line_is_plain_and_names_both_setups()
    {
        var text = NewTaomCompat.Text("v2.0.40");
        Assert.Equal("This TAOM.CoopCompat v2.0.40 is the one in the new TAOM download for Bannerlord 1.5.7. These TAOM tools run TAOM 2.0.29.5 on Bannerlord 1.4.8 with Coop 0.1.6, so they leave it alone. Use the TAOM download's own instructions for 1.5.7.", text);
        Assert.Equal(text.Replace("TAOM.CoopCompat v2.0.40", "TAOM.CoopCompat"), NewTaomCompat.Text(""));
        Assert.Equal(NewTaomCompat.Text(""), NewTaomCompat.Text(null));
        Assert.DoesNotContain(":", text);
        Assert.DoesNotMatch(@"[a-z]-[a-z]", text);
    }

    // Only the v2.0.40 DLL hash says it is the new module: no SubModule.xml and no Coop DLL. The real file can't be
    // made in a test, so the hash is stubbed; only a file of the v2.0.40 size is hashed at all.
    [Fact]
    public void The_new_TAOM_download_compat_is_found_by_the_DLL_hash_alone()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-newhash-" + Guid.NewGuid().ToString("N"));
        try
        {
            var module = NewCompat(root, version: null, coopDll: false);
            var dll = Path.Combine(module, "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.dll");
            File.WriteAllBytes(dll, new byte[NewTaomCompat.DllLength]);
            var hashed = new List<string>();

            Assert.Equal("", NewTaomCompat.Found(module, path => { hashed.Add(path); return path == dll ? NewTaomCompat.DllSha256.ToLowerInvariant() : "00"; }));
            Assert.Equal(new[] { dll }, hashed);
            Assert.Equal(NewTaomCompat.Text(""), NewTaomCompat.Problem(module, path => path == dll ? NewTaomCompat.DllSha256 : "00"));
            Assert.Null(NewTaomCompat.Found(module, _ => "00"));
            Assert.Null(NewTaomCompat.Problem(module, _ => "00"));
            // A DLL of that size whose hash can't be read: nothing can tell, so nothing writes there.
            Assert.Null(NewTaomCompat.Found(module, _ => ""));
            Assert.Equal(NewTaomCompat.UnsureText(module), NewTaomCompat.Problem(module, _ => ""));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // CR-XXP67 review: a SubModule.xml that is there but can't be read must not make the folder look like a kit one.
    [WindowsFact]
    public void A_locked_descriptor_still_checks_the_bins_and_an_unsure_folder_is_never_written()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-lockedxml-" + Guid.NewGuid().ToString("N"));
        try
        {
            var official = NewCompat(Path.Combine(root, "official"));
            var unsure = NewCompat(Path.Combine(root, "game", "Modules"), "v0.3.25", coopDll: false);
            var kit = NewCompat(Path.Combine(root, "kit"), "v0.3.25", coopDll: false);
            File.WriteAllText(Path.Combine(kit, "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.Bootstrap.dll"), "kit");
            var zip = Path.Combine(root, "TAOM-Coop-CompatOnly-CLIENT.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("Payload/Modules/TAOM.CoopCompat/SubModule.xml").Open()))
                writer.Write("<Module><Id value=\"TAOM.CoopCompat\"/><Version value=\"v0.3.25\"/></Module>");
            var before = Directory.EnumerateFiles(unsure, "*", SearchOption.AllDirectories).Where(path => !path.EndsWith("SubModule.xml"))
                .ToDictionary(path => path, File.ReadAllText);

            using (File.Open(Path.Combine(official, "SubModule.xml"), FileMode.Open, FileAccess.Read, FileShare.None))
            using (File.Open(Path.Combine(unsure, "SubModule.xml"), FileMode.Open, FileAccess.Read, FileShare.None))
            using (File.Open(Path.Combine(kit, "SubModule.xml"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // The Coop DLL still says it is the new module.
                Assert.Equal("", NewTaomCompat.Found(official));
                Assert.Equal(NewTaomCompat.Text(""), NewTaomCompat.Problem(official));
                // No marker either way: unsure, and Install or repair TAOM stops before it writes.
                Assert.Null(NewTaomCompat.Found(unsure));
                Assert.Equal(NewTaomCompat.UnsureText(unsure), NewTaomCompat.Problem(unsure));
                var stop = Assert.Throws<InvalidOperationException>(() => TaomArchive.InstallCompat(zip, Path.Combine(root, "game"), null));
                Assert.Equal(NewTaomCompat.UnsureText(unsure), stop.Message);
                // The kit Bootstrap DLL says it is a kit TAOM.CoopCompat, so Repair may still fix its DLLs.
                Assert.Null(NewTaomCompat.Problem(kit));
            }

            Assert.Equal(before, Directory.EnumerateFiles(unsure, "*", SearchOption.AllDirectories).Where(path => !path.EndsWith("SubModule.xml"))
                .ToDictionary(path => path, File.ReadAllText));
            Assert.Equal(new[] { unsure }, Directory.GetDirectories(Path.Combine(root, "game", "Modules")));
            Assert.DoesNotContain(":", NewTaomCompat.UnsureText("Modules\\TAOM.CoopCompat"));
            Assert.DoesNotMatch(@"[a-z]-[a-z]", NewTaomCompat.UnsureText("Modules\\TAOM.CoopCompat"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Install_or_repair_TAOM_never_replaces_the_new_TAOM_download_compat()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-keepnew-" + Guid.NewGuid().ToString("N"));
        try
        {
            var modules = Path.Combine(root, "game", "Modules");
            var module = NewCompat(modules);
            var zip = Path.Combine(root, "TAOM-Coop-CompatOnly-CLIENT.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("Payload/Modules/TAOM.CoopCompat/SubModule.xml").Open()))
                writer.Write("<Module><Id value=\"TAOM.CoopCompat\"/><Version value=\"v0.3.25\"/></Module>");
            var before = Directory.EnumerateFiles(module, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
            var gameRoot = Path.Combine(root, "game");

            var stop = Assert.Throws<InvalidOperationException>(() => TaomArchive.InstallCompat(zip, gameRoot, null));

            Assert.Equal(NewTaomCompat.Text("v2.0.40"), stop.Message);
            Assert.Equal(before, Directory.EnumerateFiles(module, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText));
            Assert.Equal(new[] { module }, Directory.GetDirectories(modules));
            // The launcher's status says the same, and both Install or repair TAOM paths stop before any module is replaced.
            Assert.Equal(NewTaomCompat.Text("v2.0.40"), TaomArchive.CompatProblem(gameRoot));
            var launcher = SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher");
            foreach (var click in new[] { "private async void Install_Click(", "private async void Repair_Click(" })
            {
                var body = SourceFiles.Method(launcher, click);
                var guard = body.IndexOf("if (NewTaomCompat.Problem(Path.Combine(gameRoot, \"Modules\", TaomStack.CompatModuleId)) is { } kept)", StringComparison.Ordinal);
                Assert.True(guard >= 0 && guard < body.IndexOf("TaomArchive.InstallContent(", StringComparison.Ordinal), click);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public void Game_path_keeps_the_drive_letter_and_folder_casing()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaomCase-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "Program Files");
        Directory.CreateDirectory(nested);

        var shown = BannerlordFinder.OnDiskCasing(nested.ToLowerInvariant());

        Assert.Equal(char.ToUpperInvariant(shown[0]), shown[0]);
        Assert.EndsWith("Program Files", shown);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Use_workshop_coop_016_moves_the_old_015_playtest_copy_out()
    {
        // Needs the real released Coop 0.1.6 Coop.Core.dll (the pin is its hash).
        var pinned = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "workshop", "content", "261550", "3770450698", "bin", "Win64_Shipping_Client", "Coop.Core.dll")
        }.FirstOrDefault(path => File.Exists(path) && string.Equals(TaomStack.CoopCoreSha256, Sha256Files.Hash(path), StringComparison.OrdinalIgnoreCase));
        if (pinned == null)
            return;

        var root = Path.Combine(Path.GetTempPath(), "taom-coop-switch-" + Guid.NewGuid().ToString("N"));
        var workshop = Path.Combine(root, "workshop");
        var game = Path.Combine(root, "game");
        var workshopDll = Path.Combine(workshop, "bin", "Win64_Shipping_Client", "Coop.Core.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(workshopDll)!);
        File.Copy(pinned, workshopDll);
        File.WriteAllText(Path.Combine(workshop, "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"v0.1.6\"/></Module>");
        var old = Path.Combine(game, "Modules", "Coop");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "SubModule.xml"), CoopXml("v0.1.5"));
        var progress = new RememberedProgress();

        CoopStaging.UsePlaytestCoop(workshop, game, progress);
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(Path.Combine(game, "Coop-kept-0.1.5")));
        Assert.Equal("Bannerlord Coop 0.1.6 from the Workshop is ready.", progress.Lines[^1]);

        CoopStaging.UsePlaytestCoop(workshop, game, progress);
        Assert.Equal("Bannerlord Coop 0.1.6 from the Workshop is ready. No other Coop copy is in Modules.", progress.Lines[^1]);
        Directory.Delete(root, recursive: true);
    }

    private sealed class RememberedProgress : IProgress<string>
    {
        public List<string> Lines { get; } = new();
        public void Report(string value) => Lines.Add(value);
    }

    private static string CoopXml(string version) =>
        "<Module><Id value=\"CoopNightly\"/><Version value=\"" + version + "\"/></Module>";

    [Fact]
    public void A_second_coop_module_is_moved_out_of_modules()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-extra-coop-" + Guid.NewGuid().ToString("N"));
        var modules = Path.Combine(root, "Modules");
        Directory.CreateDirectory(Path.Combine(modules, "Coop"));
        Directory.CreateDirectory(Path.Combine(modules, "Bannerlord Coop"));
        Directory.CreateDirectory(Path.Combine(modules, "CoopMarriage"));
        File.WriteAllText(Path.Combine(modules, "Coop", "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"v0.1.6\"/></Module>");
        File.WriteAllText(Path.Combine(modules, "Bannerlord Coop", "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"v0.1.5\"/></Module>");
        File.WriteAllText(Path.Combine(modules, "CoopMarriage", "SubModule.xml"), "<Module><Id value=\"CoopMarriage\"/><Version value=\"v1.6.0\"/></Module>");

        var moved = CoopStaging.ParkExtraCoop(root);

        Assert.Equal(Path.Combine(root, "Bannerlord Coop-kept-0.1.5"), Assert.Single(moved));
        Assert.False(Directory.Exists(Path.Combine(modules, "Bannerlord Coop")));
        Assert.True(Directory.Exists(Path.Combine(modules, "Coop")));
        Assert.True(Directory.Exists(Path.Combine(modules, "CoopMarriage")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Direct_connect_arguments_carry_the_password_and_module_order()
    {
        var arguments = TaomLaunch.Arguments(connect: true, steamLobby: false, password: "secret", directEndpoint: "192.168.1.10:4200");

        Assert.Contains("/autoconnect 192.168.1.10:4200", arguments);
        Assert.True(arguments.IndexOf("/autoconnect", StringComparison.Ordinal) < arguments.IndexOf("/cooppassword", StringComparison.Ordinal));
        Assert.Contains("/cooppassword \"secret\"", arguments);
        Assert.Contains("_MODULES_*TAOM.Dependencies*Native*", arguments);
        Assert.Contains("*LOTRLOME_Armory*Coop*TAOM.CoopCompat*_MODULES_", arguments);
        Assert.DoesNotContain("/coopowner", arguments);
        var ids = arguments.Split('*');
        Assert.Equal(1, ids.Count(id => id is "Coop" or "CoopNightly"));
    }

    [Fact]
    public void Old_CoopNightly_playtest_folders_are_moved_out_of_modules()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-two-nightly-" + Guid.NewGuid().ToString("N"));
        var modules = Path.Combine(root, "Modules");
        Directory.CreateDirectory(Path.Combine(modules, "Coop"));
        Directory.CreateDirectory(Path.Combine(modules, "CoopNightly"));
        File.WriteAllText(Path.Combine(modules, "Coop", "SubModule.xml"), CoopXml("v0.1.5"));
        File.WriteAllText(Path.Combine(modules, "CoopNightly", "SubModule.xml"), CoopXml("v0.1.5"));

        var moved = CoopStaging.ParkExtraCoop(root);

        // TAOM-Coop 0.3.25 uses Workshop Coop 0.1.6, so neither 0.1.5 copy stays in Modules.
        Assert.Equal(2, moved.Count);
        Assert.False(Directory.Exists(Path.Combine(modules, "Coop")));
        Assert.False(Directory.Exists(Path.Combine(modules, "CoopNightly")));
        Assert.Equal("Bannerlord Coop was not found in this Modules folder.", CoopStaging.DescribeCopies(modules));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void A_client_only_Coop_folder_is_not_copied_onto_the_server()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-client-coop-" + Guid.NewGuid().ToString("N"));
        var serverCoop = Path.Combine(root, "server", "Coop");
        var serverDll = Path.Combine(serverCoop, "bin", "Win64_Shipping_Server", "Coop.Core.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(serverDll)!);
        File.WriteAllText(serverDll, "server-copy");
        var clientCoop = Path.Combine(root, "game", "Coop");
        Directory.CreateDirectory(Path.Combine(clientCoop, "bin", "Win64_Shipping_Client"));
        File.WriteAllText(Path.Combine(clientCoop, "bin", "Win64_Shipping_Client", "Coop.Core.dll"), "client-copy");

        var error = Assert.Throws<InvalidOperationException>(() => CoopStaging.EnsurePinnedServerBin(serverCoop, clientCoop));

        Assert.Contains("not the released Coop 0.1.6 file", error.Message);
        Assert.Equal("server-copy", File.ReadAllText(serverDll));
        Assert.False(Directory.Exists(Path.Combine(serverCoop, "bin", "Win64_Shipping_Client")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Compat_zip_is_found_outside_Downloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-zip-search-" + Guid.NewGuid().ToString("N"));
        var desktop = Path.Combine(root, "Desktop");
        var downloads = Path.Combine(root, "Downloads");
        var elsewhere = Path.Combine(root, "Games", "mods");
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(elsewhere);
        var older = Path.Combine(downloads, "TAOM-Coop-v0.3.25-CompatOnly-CLIENT-old.zip");
        var newer = Path.Combine(elsewhere, "TAOM-Coop-v0.3.25-CompatOnly-CLIENT.zip");
        File.WriteAllText(older, "old");
        File.WriteAllText(newer, "new");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var found = PlaytestZipFinder.Newest("TAOM-Coop-v0.3.25-CompatOnly-CLIENT*.zip", [desktop, downloads, root]);

        Assert.Equal(newer, found);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Host_operator_zip_is_found_outside_Downloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-host-zip-" + Guid.NewGuid().ToString("N"));
        var desktop = Path.Combine(root, "Desktop");
        Directory.CreateDirectory(desktop);
        var host = Path.Combine(desktop, "TAOM-Coop-v0.3.25-CompatOnly-HOST-OPERATOR-36ae2c5a-d8a2ad02-r1.zip");
        var client = Path.Combine(desktop, "TAOM-Coop-v0.3.25-CompatOnly-CLIENT-PLAYTEST-36ae2c5a-d8a2ad02-r1.zip");
        File.WriteAllText(host, "host");
        File.WriteAllText(client, "client");

        var found = PlaytestZipFinder.Newest(PlaytestZipFinder.HostOperatorPattern, [root]);

        Assert.Equal(host, found);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Taom_coop_025_tuple_matches_the_kit()
    {
        Assert.Equal("v0.3.25", TaomStack.CompatVersion);
        Assert.All(TaomStack.ContentVersions.Values, version => Assert.Equal("v2.0.29.5", version));
        Assert.Equal("AEEC07AA61C84F45E9E8662C294CFF68B000F041D8FCA453CF30058CB9AD14D8", TaomStack.TaomDllSha256);
        Assert.Equal("3B8A9274F254D4CC0BD2546BADB8A5111AD2BB1488DDAA4C0B4DB9C6815E6E2A", TaomStack.DependenciesDllSha256);
        // Same order as the kit's wrapper module token.
        Assert.Equal("_MODULES_*TAOM.Dependencies*Native*SandBoxCore*Sandbox*StoryMode*CustomBattle*TAOM*TAOM_Map*LOTRLOME_Armory*Coop*TAOM.CoopCompat*DedicatedServer.Windows*_MODULES_",
            "_MODULES_*" + string.Join("*", TaomStack.ServerModules) + "*_MODULES_");
        Assert.Matches(new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(TaomStack.HostKitPattern).Replace("\\*", ".*") + "$"),
            "TAOM-Coop-v0.3.25-CompatOnly-HOST-OPERATOR-1675bea7-a2ac41c2-r2.zip");
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(TaomStack.HostKitPattern).Replace("\\*", ".*") + "$"),
            "TAOM-Coop-v0.3.17-CompatOnly-HOST-OPERATOR-36ae2c5a-d8a2ad02-r1.zip");
    }

    [Fact]
    public void Hosts_on_the_0317_folders_move_to_new_0325_server_and_data_folders()
    {
        Assert.True(HostFolders.MoveToCurrentKit(@"D:\TAOM-Coop-Host\Server-v0317-Coop-v015-r1", @"D:\TAOM-Coop-Host\Data", out var server, out var data));
        Assert.EndsWith("Server-v0325-Coop-v016-r2", server);
        Assert.EndsWith("Data-v0325", data);
        Assert.False(HostFolders.MoveToCurrentKit(server, data, out _, out _));
        Assert.False(HostFolders.MoveToCurrentKit(@"E:\MyServer", @"E:\MyData", out var keptServer, out _));
        Assert.Equal(@"E:\MyServer", keptServer);
        Assert.False(HostFolders.MoveToCurrentKit("", "", out _, out _));
    }

    [Fact]
    public void Taom_server_terms_use_the_wrappers_EULA_txt()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-terms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(ServerTerms.TermsHash(root));
            File.WriteAllText(Path.Combine(root, "EULA.txt"), "TERMS\n");
            File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "LICENSE\n");
            var hash = ServerTerms.TermsHash(root);
            Assert.True(ServerTerms.NeedsAnswer(hash, null));
            Assert.False(ServerTerms.NeedsAnswer(hash, hash));
            Assert.Contains("--- LICENSE.txt ---", ServerTerms.TermsText(root));
            Assert.Equal("DS_ACCEPT_EULA", ServerTerms.EnvironmentVariable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // A locked TAOM.CoopCompat SubModule.xml (the Client Launcher's refusal case, a game or antivirus holding it) reads as
    // unknown instead of throwing out of the module list, and a refused install or repair does not read the list again.
    [Fact]
    public void A_locked_SubModule_xml_reads_as_unknown_and_a_refused_install_skips_the_module_list()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-locked-xml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xml = Path.Combine(root, "SubModule.xml");
            File.WriteAllText(xml, "<Module><Id value=\"TAOM.CoopCompat\"/><Version value=\"v0.3.25\"/></Module>");
            Assert.Equal(new ModuleIdentity("TAOM.CoopCompat", "v0.3.25"), ModuleIdentityReader.TryRead(xml));
            using (new FileStream(xml, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Null(ModuleIdentityReader.TryRead(xml));
            Assert.Null(ModuleIdentityReader.TryRead(Path.Combine(root, "missing", "SubModule.xml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        var window = SourceFiles.Read(Path.Combine("src", "TaomCoopLauncher", "MainWindow.xaml.cs"));
        foreach (var handler in new[] { "private async void Install_Click(object sender, RoutedEventArgs e)", "private async void Repair_Click(object sender, RoutedEventArgs e)" })
        {
            var body = SourceFiles.Method(window, handler);
            // Only the compat refusal skips the list; any other failure still shows what is there now.
            var refused = body.IndexOf("if (refused)", StringComparison.Ordinal);
            Assert.True(refused > 0 && refused < body.LastIndexOf("RefreshModules();", StringComparison.Ordinal), body);
            Assert.True(body.IndexOf("refused = true;", StringComparison.Ordinal) < body.IndexOf("throw new InvalidOperationException(kept);", StringComparison.Ordinal), body);
            Assert.DoesNotContain("if (!done)", body);
        }
        // No check of the module list reads a SubModule.xml with the throwing reader (Setups.VersionIn catches its own).
        foreach (var file in new[] { "MainWindow.xaml.cs", "MainWindow.Frame.cs" })
            Assert.DoesNotContain("ModuleIdentityReader.Read(", SourceFiles.Read(Path.Combine("src", "TaomCoopLauncher", file)));
        Assert.Contains("private static string ModuleVersion(string xml) => ModuleIdentityReader.TryRead(xml)?.Version ?? UnreadableVersion;", window);
    }
}
