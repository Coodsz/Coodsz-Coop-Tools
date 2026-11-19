using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// Tatache, AD1259 crash reports #13 to #15: the server's AD1259 copy had its SubModule.xml with the right version but
// no bin and no SceneObj\Main_map. Start only looked at the version, so it never copied it again, the start script
// found no custom map and the server made a plain Calradia world, and every join crashed on the AD1259 map.
public class AD1259BrokenCopyTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), "ad1259-broken-" + Guid.NewGuid().ToString("N"));

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void Module(string folder, string id, string version) =>
        Write(Path.Combine(folder, "SubModule.xml"), "<Module><Id value=\"" + id + "\"/><Name value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>");

    // A full AD1259 copy: the DLL, the map, data, a raw Assets file and the editor caches the server copy never keeps.
    private static void FullCopy(string folder, string version)
    {
        Module(folder, "AD1259", version);
        Write(Path.Combine(folder, "bin", "Win64_Shipping_Client", "AD1259.dll"), "dll");
        Write(Path.Combine(folder, "SceneObj", "Main_map", "scene.xscene"), "map");
        Write(Path.Combine(folder, "ModuleData", "items.xml"), "<Items/>");
        Write(Path.Combine(folder, "Assets", "big.tpac"), "assets");
        Write(Path.Combine(folder, "AssetSources", "texture.psd"), "editor");
        Write(Path.Combine(folder, "SceneEditData", "edit.bin"), "editor");
        Write(Path.Combine(folder, "RuntimeDataCache", "cache.bin"), "editor");
    }

    [Fact]
    public void Missing_files_names_what_the_server_copy_lacks_and_skips_what_the_tools_change()
    {
        var root = Temp();
        try
        {
            var source = Path.Combine(root, "game", "AD1259");
            FullCopy(source, "v1.4.6");
            var server = Path.Combine(root, "server", "AD1259");
            // The server copy: SubModule.xml rewritten (server code marks), Assets renamed by ServerRawAssets,
            // items.xml patched to another size, no bin, no map, no editor caches, and a file of its own.
            Module(server, "AD1259", "v1.4.6 server");
            Write(Path.Combine(server, ServerRawAssets.HiddenName, "big.tpac"), "assets");
            Write(Path.Combine(server, "ModuleData", "items.xml"), "<Items patched=\"1\"/>");
            Write(Path.Combine(server, "server-only.txt"), "extra");

            var missing = ServerModuleInstall.MissingFiles(source, server).Select(path => path.Replace('\\', '/')).OrderBy(path => path).ToList();
            Assert.Equal(new[] { "SceneObj/Main_map/scene.xscene", "bin/Win64_Shipping_Client/AD1259.dll" }.OrderBy(path => path), missing);

            Write(Path.Combine(server, "bin", "Win64_Shipping_Client", "AD1259.dll"), "dll");
            Write(Path.Combine(server, "SceneObj", "Main_map", "scene.xscene"), "map");
            Assert.Empty(ServerModuleInstall.MissingFiles(source, server));

            // A missing data file counts, a patched one does not.
            File.Delete(Path.Combine(server, "ModuleData", "items.xml"));
            Assert.Equal(Path.Combine("ModuleData", "items.xml"), Assert.Single(ServerModuleInstall.MissingFiles(source, server)));
            Write(Path.Combine(server, "ModuleData", "items.xml"), "<Items/>");

            // Before the first Start hides it, the raw Assets folder is still called Assets on the server.
            Directory.Move(Path.Combine(server, ServerRawAssets.HiddenName), Path.Combine(server, "Assets"));
            Assert.Empty(ServerModuleInstall.MissingFiles(source, server));

            Assert.Empty(ServerModuleInstall.MissingFiles(Path.Combine(root, "nothing"), server));
            Assert.Empty(ServerModuleInstall.MissingFiles(source, Path.Combine(root, "nothing")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // AD1259 v1.1.6 live test: after a play session the player's copies held files the game, UIExtenderEx and the
    // launchers write there, and Start copied UIExtenderEx, Expanded Armoury, Open Source Weaponry and AD1259 again.
    [Fact]
    public void Files_the_game_and_the_launchers_write_into_the_player_copy_never_count()
    {
        var root = Temp();
        try
        {
            var source = Path.Combine(root, "game", "AD1259");
            FullCopy(source, "v1.4.6");
            var server = Path.Combine(root, "server", "AD1259");
            FullCopy(server, "v1.4.6");
            Assert.Empty(ServerModuleInstall.MissingFiles(source, server));

            // A grown log.txt, a new banner, AD1259's loose config files, a parked SubModule.xml and the
            // compiled prefabs UIExtenderEx writes, all outside the folders a server loads.
            Write(Path.Combine(source, "log.txt"), "a much longer log after a play session");
            Write(Path.Combine(source, "PlayerBanners", "banner1.json"), "{}");
            Write(Path.Combine(source, "config.json.POCpro_version"), "1");
            Write(Path.Combine(source, "SubModule.xml.before-fix"), "<Module/>");
            Write(Path.Combine(source, "CompiledPrefabs", "x.bin"), "prefab");
            // Backups and generated files inside the folders a server loads.
            Write(Path.Combine(source, "ModuleData", "CompiledPrefabs.zip"), "zip");
            Write(Path.Combine(source, "ModuleData", "items.xml.coodsz-old"), "old");
            Write(Path.Combine(source, "ModuleData", "items.xml.before-fix"), "old");
            Write(Path.Combine(source, "bin", "Win64_Shipping_Client", "AD1259.dll.pre-password.bak"), "old");
            Write(Path.Combine(source, "bin", "Win64_Shipping_Client", "AD1259.dll.coodsz-password"), "old");
            Write(Path.Combine(source, "bin", "Win64_Shipping_Client", "crash.log"), "log");
            Write(Path.Combine(source, "GUI", "layout.xml.tmp"), "tmp");
            Write(Path.Combine(source, "SceneObj", "Main_map", "scene.xscene.old"), "old");
            Write(Path.Combine(source, "SceneObj", "Main_map", "scene.backup"), "old");
            Assert.Empty(ServerModuleInstall.MissingFiles(source, server));

            // Inside a server folder a real new file still counts, and a different size counts for DLLs and SceneObj only.
            Write(Path.Combine(source, "GUI", "new.xml"), "<GUI/>");
            Assert.Equal(Path.Combine("GUI", "new.xml"), Assert.Single(ServerModuleInstall.MissingFiles(source, server)));
            File.Delete(Path.Combine(source, "GUI", "new.xml"));
            Write(Path.Combine(source, "bin", "Win64_Shipping_Client", "AD1259.dll"), "a new build");
            Write(Path.Combine(source, "bin", "Win64_Shipping_Client", "AD1259.pdb"), "symbols");
            Write(Path.Combine(server, "bin", "Win64_Shipping_Client", "AD1259.pdb"), "other symbols");
            Write(Path.Combine(source, "SceneObj", "Main_map", "scene.xscene"), "a bigger map");
            Assert.Equal(
                new[] { Path.Combine("SceneObj", "Main_map", "scene.xscene"), Path.Combine("bin", "Win64_Shipping_Client", "AD1259.dll") }.OrderBy(path => path),
                ServerModuleInstall.MissingFiles(source, server).OrderBy(path => path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("items.xml.coodsz-old", true)]
    [InlineData("SubModule.xml.coodsz-off", true)]
    [InlineData("Coop.dll.coodsz-password", true)]
    [InlineData("SubModule.xml.before-fix", true)]
    [InlineData("TaleWorlds.dll.pre-password.bak", true)]
    [InlineData("PlayerSettlement.dll.pre-gettypes.bak", true)]
    [InlineData("settings.old", true)]
    [InlineData("mod.backup", true)]
    [InlineData("rgl_log.log", true)]
    [InlineData("write.tmp", true)]
    [InlineData("compiledprefabs.zip", true)]
    [InlineData("AD1259.dll", false)]
    [InlineData("items.xml", false)]
    [InlineData("Main_map.xscene", false)]
    [InlineData("logo.png", false)]
    public void Backup_and_generated_names_never_count(string name, bool skipped) =>
        Assert.Equal(skipped, ServerModuleInstall.IsBackupOrGenerated(name));

    [Fact]
    public void Only_the_folders_a_server_loads_count()
    {
        Assert.Equal(new[] { "bin", "SceneObj", "ModuleData", "GUI", "DsAssetPackages", "Prefabs", "Assets" }, ServerModuleInstall.ServerFolders);
        var root = Temp();
        try
        {
            var source = Path.Combine(root, "game", "ExpandedArmouryBL");
            var server = Path.Combine(root, "server", "ExpandedArmouryBL");
            Module(source, "ExpandedArmouryBL", "e1.5.2");
            Module(server, "ExpandedArmouryBL", "e1.5.2");
            // Folders a server never loads: client asset packages, shaders, videos, music, source code.
            foreach (var folder in new[] { "AssetPackages", "Shaders", "Videos", "music", "CSharpSourceCode", "PlayerBanners" })
                Write(Path.Combine(source, folder, "file.bin"), "client");
            Assert.Empty(ServerModuleInstall.MissingFiles(source, server));

            // The armoury's own Bin (capital B) and its server asset packages count.
            Write(Path.Combine(source, "Bin", "Win64_Shipping_Client", "Armoury.dll"), "dll");
            Write(Path.Combine(source, "DsAssetPackages", "armoury.tpac"), "ds");
            Write(Path.Combine(source, "Prefabs", "armoury.xml"), "<prefabs/>");
            Assert.Equal(3, ServerModuleInstall.MissingFiles(source, server).Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Start_copies_a_short_ad1259_copy_again_and_leaves_a_whole_one()
    {
        var root = Temp();
        try
        {
            var version = AD1259Stack.Find("AD1259")!.Version;
            var game = Path.Combine(root, "Modules");
            FullCopy(Path.Combine(game, "AD1259"), version);
            var server = Path.Combine(root, "DedicatedServer");
            var serverCopy = Path.Combine(ModuleCatalog.ModulesRoot(server), "AD1259");
            Module(serverCopy, "AD1259", version);
            var rows = new List<ModuleRow> { new() { Id = "AD1259", Version = version } };

            // The version is right, so the old check left it alone.
            Assert.Empty(AD1259Server.RefreshWrongVersions(server, rows, game, []));
            var copy = Assert.Single(AD1259Server.ShortCopies(server, rows, game, []));
            Assert.Equal("Anno Domini 1259", copy.Name);
            Assert.Equal(4, copy.Missing.Count);
            Assert.Equal("Copying Anno Domini 1259 into the server again, because the server copy is missing files. This can take a few minutes, so leave the tool open.", copy.Copying);
            // The line names the first three missing files, so a copy that keeps coming back shows in reports.
            Assert.StartsWith("Copied Anno Domini 1259 into the server again from " + Path.Combine(game, "AD1259") + ", because the server copy was missing files (for example ", copy.Copied);
            Assert.EndsWith("(for example " + ShortServerCopy.Example(copy.Missing) + ").", copy.Copied);
            Assert.Equal(3, ShortServerCopy.Example(copy.Missing).Split(", ").Length);
            Assert.Contains(copy.Missing[0], copy.Copied);

            Assert.Empty(ExtraModScan.Recopy(copy, server));
            Assert.True(File.Exists(Path.Combine(serverCopy, "SceneObj", "Main_map", "scene.xscene")));
            Assert.True(File.Exists(Path.Combine(serverCopy, "bin", "Win64_Shipping_Client", "AD1259.dll")));
            Assert.False(Directory.Exists(Path.Combine(serverCopy, "AssetSources")));

            // Hidden Assets and a whole copy: nothing to copy on the next Start.
            ServerRawAssets.Hide(ModuleCatalog.ModulesRoot(server), ["AD1259"]);
            Assert.Empty(AD1259Server.ShortCopies(server, rows, game, []));

            // A short copy with the wrong version is RefreshWrongVersions' job, and a PC copy of another version is no source.
            var oldRows = new List<ModuleRow> { new() { Id = "AD1259", Version = "v1.0.0" } };
            Directory.Delete(Path.Combine(serverCopy, "SceneObj"), recursive: true);
            Assert.Empty(AD1259Server.ShortCopies(server, oldRows, game, []));
            Module(Path.Combine(game, "AD1259"), "AD1259", "v1.0.0");
            Assert.Empty(AD1259Server.ShortCopies(server, rows, game, []));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // How a copy ends up short: Install deleted the old copy and copied file by file, top level files first, so a copy
    // cut short kept SubModule.xml. It now writes SubModule.xml last, and a short copy reads as missing.
    [WindowsFact]
    public void A_copy_cut_short_has_no_submodule_xml()
    {
        var root = Temp();
        try
        {
            var source = Path.Combine(root, "game", "AD1259");
            FullCopy(source, "v1.4.6");
            var server = Path.Combine(root, "DedicatedServer");
            using (new FileStream(Path.Combine(source, "Assets", "big.tpac"), FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsAny<IOException>(() => ServerModuleInstall.Install(source, server, "AD1259"));

            var serverCopy = Path.Combine(ModuleCatalog.ModulesRoot(server), "AD1259");
            Assert.False(File.Exists(Path.Combine(serverCopy, "SubModule.xml")));

            // The reload after a failed copy reads that folder as no mod instead of throwing (Codex, v1.1.7 review).
            Assert.DoesNotContain(ModuleCatalog.Load(server), row => row.Id == "AD1259");
            var (copied, missingIds) = ExtraModScan.CopyMissing(server, ["AD1259"], [(Path.GetDirectoryName(source)!, false)]);
            Assert.Equal(new[] { "AD1259 (from " + source + ")" }, copied);
            Assert.Empty(missingIds);
            Assert.True(File.Exists(Path.Combine(serverCopy, "SubModule.xml")));

            ServerModuleInstall.Install(source, server, "AD1259");
            Assert.True(File.Exists(Path.Combine(serverCopy, "SubModule.xml")));
            Assert.Empty(ServerModuleInstall.MissingFiles(source, serverCopy));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // A failed recopy leaves a server folder without SubModule.xml. Every reader of the server's Modules folder treats it
    // as no mod, so the reload and the locked setup copy work instead of throwing FileNotFoundException.
    [Fact]
    public void A_server_folder_without_submodule_xml_reads_as_missing()
    {
        var root = Temp();
        try
        {
            var source = Path.Combine(root, "game", "AD1259");
            FullCopy(source, "v1.4.6");
            var server = Path.Combine(root, "DedicatedServer");
            var serverCopy = Path.Combine(ModuleCatalog.ModulesRoot(server), "AD1259");
            Write(Path.Combine(serverCopy, "Assets", "half.tpac"), "half a copy");

            Assert.DoesNotContain(ModuleCatalog.Load(server), row => row.Id == "AD1259");
            Assert.Empty(ModuleCatalog.FoldersFor(ModuleCatalog.ModulesRoot(server), "AD1259"));
            var (copied, missingIds) = ExtraModScan.CopyMissing(server, ["AD1259"], [(Path.GetDirectoryName(source)!, false)]);
            Assert.Equal(new[] { "AD1259 (from " + source + ")" }, copied);
            Assert.Empty(missingIds);
            Assert.True(File.Exists(Path.Combine(serverCopy, "SceneObj", "Main_map", "scene.xscene")));
            Assert.Contains(ModuleCatalog.Load(server), row => row.Id == "AD1259");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Start while the server runs would let a recopy delete a mod the running server uses. StartServer refuses first,
    // before any module step, and every restart path stops the server (or returns) before it calls StartServer.
    [Fact]
    public void Start_refuses_a_running_server_before_any_module_change()
    {
        var source = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs"));
        var start = MethodBody(source, "private void StartServer()");
        var running = start.IndexOf("if (_session.IsRunning)", StringComparison.Ordinal);
        Assert.True(running > 0, "StartServer checks a running server.");
        Assert.Contains("throw new InvalidOperationException(ServerSession.AlreadyRunningText);", start[running..(running + 120)]);
        foreach (var step in new[] { "ParkExtraCoop();", "ReloadModules();", "ExtraModScan.UpdateFromWorkshop(", "PrepareAD1259Server();", "RecopyIncompleteMap();" })
            Assert.True(running < start.IndexOf(step, StringComparison.Ordinal), "The running check comes before " + step);
        Assert.Contains("public const string AlreadyRunningText = \"The server is already running.\";", SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ServerSession.cs")));

        var restart = MethodBody(source, "private async Task<bool> RestartServerAsync()");
        Assert.True(restart.IndexOf("_session.Stop", StringComparison.Ordinal) < restart.IndexOf("StartServer();", StringComparison.Ordinal));
        var lastGood = MethodBody(source, "private async void RestartLastGood_Click(object sender, RoutedEventArgs e)");
        Assert.True(lastGood.IndexOf("_session.Stop();", StringComparison.Ordinal) < lastGood.IndexOf("StartServer();", StringComparison.Ordinal));
        // Last good save waits for a lingering instance lock before the running check (Devin, d84366ad review).
        var wait = lastGood.IndexOf("&& _session.IsRunning", StringComparison.Ordinal);
        Assert.True(lastGood.IndexOf("_session.Stop();", StringComparison.Ordinal) < wait && wait < lastGood.IndexOf("StartServer();", StringComparison.Ordinal));
        var crash = MethodBody(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.CrashRestart.cs")), "private void RestartAfterCrash()");
        Assert.True(crash.IndexOf("_session.IsRunning", StringComparison.Ordinal) < crash.IndexOf("StartServer();", StringComparison.Ordinal));
        var retry = MethodBody(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.AutoRescue.cs")), "private async Task StartAfterLoadCrashAsync()");
        Assert.True(retry.IndexOf("_session.IsRunning", StringComparison.Ordinal) < retry.IndexOf("StartServer();", StringComparison.Ordinal));
    }

    // Exit 20 comes before the engine makes the world, so a new campaign stays new for the next Start.
    [Fact]
    public void The_map_stop_keeps_a_new_campaign_new()
    {
        var body = MethodBody(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ExitChecks.cs")), "private bool IsMapMissingStop(ServerExitInfo exit)");
        var keep = body.IndexOf("KeepNewCampaignIfNotSaved();", StringComparison.Ordinal);
        Assert.True(keep > 0 && keep < body.IndexOf("return true;", StringComparison.Ordinal));
    }

    // CR-JTQ45: a Workshop item with a module Id AD1259 but no SceneObj\Main_map beat the game's own AD1259 (any Workshop
    // copy scored higher), so the server's AD1259 came back with its DLLs and without its map.
    [Fact]
    public void The_game_copy_with_the_map_beats_a_workshop_copy_without_it()
    {
        var root = Temp();
        try
        {
            var version = AD1259Stack.Find("AD1259")!.Version;
            var game = Path.Combine(root, "Modules");
            var gameCopy = Path.Combine(game, "AD1259");
            FullCopy(gameCopy, version);
            var workshop = Path.Combine(root, "workshop");
            var mirror = Path.Combine(workshop, "123456789");
            Module(mirror, "AD1259", version);
            Write(Path.Combine(mirror, "bin", "Win64_Shipping_Client", "AD1259.dll"), "dll");
            var server = Path.Combine(root, "DedicatedServer");
            var serverCopy = Path.Combine(ModuleCatalog.ModulesRoot(server), "AD1259");

            // The locked setup copy: Workshop roots come first, yet the game copy is taken and named.
            var mapProblems = new List<MapCopyProblem>();
            var (copied, _) = ExtraModScan.CopyMissing(server, ["AD1259"], [(workshop, true), (game, false)], mapProblems);
            Assert.Equal(new[] { "AD1259 (from " + gameCopy + ")" }, copied);
            Assert.True(ServerModuleInstall.HasMapScene(serverCopy));
            Assert.Empty(mapProblems);

            // The set's own choice (RefreshWrongVersions, ShortCopies): the game copy, never the Workshop mirror.
            var sources = AD1259Server.PcCopies(game, [workshop]);
            Assert.Equal(gameCopy, AD1259Server.GoodCopy(AD1259Stack.Find("AD1259")!, sources.ById["AD1259"]).Directory);
            var rows = new List<ModuleRow> { new() { Id = "AD1259", Version = "v1.0.0" } };
            var refreshed = AD1259Server.RefreshWrongVersions(server, rows, sources, mapProblems);
            Assert.Equal(new[] { "Anno Domini 1259 " + version + " from " + gameCopy }, refreshed);
            Assert.Empty(mapProblems);

            // With the map scene only in the Workshop copy (a game copy cut short), the copy with the map wins.
            File.Delete(Path.Combine(gameCopy, "SceneObj", "Main_map", "scene.xscene"));
            Write(Path.Combine(mirror, "SceneObj", "Main_map", "scene.xscene"), "map");
            Assert.Equal(mirror, AD1259Server.GoodCopy(AD1259Stack.Find("AD1259")!, AD1259Server.PcCopies(game, [workshop]).ById["AD1259"]).Directory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // When no copy on the PC has the map scene, the copy goes ahead as today, but it counts as failed and names its source.
    [Fact]
    public void A_map_copy_without_its_scene_counts_as_failed_and_names_the_source()
    {
        var root = Temp();
        try
        {
            var version = AD1259Stack.Find("AD1259")!.Version;
            var game = Path.Combine(root, "Modules");
            var gameCopy = Path.Combine(game, "AD1259");
            Module(gameCopy, "AD1259", version);
            Write(Path.Combine(gameCopy, "bin", "Win64_Shipping_Client", "AD1259.dll"), "dll");
            var server = Path.Combine(root, "DedicatedServer");

            var mapProblems = new List<MapCopyProblem>();
            ExtraModScan.CopyMissing(server, ["AD1259"], [(game, false)], mapProblems);
            Assert.Equal(
                "The copy of AD1259 from " + gameCopy + " has no SceneObj\\Main_map\\scene.xscene, so it brings no campaign map to the server.",
                Assert.Single(mapProblems).Line);

            mapProblems.Clear();
            var rows = new List<ModuleRow> { new() { Id = "AD1259", Version = "v1.0.0" } };
            AD1259Server.RefreshWrongVersions(server, rows, AD1259Server.PcCopies(game, []), mapProblems);
            Assert.Contains(gameCopy, Assert.Single(mapProblems).Line);
            // The failed mod's own id goes to _recopyGaveUp (NoteMapProblems), not the profile's map.
            Assert.Equal("AD1259", mapProblems[0].Id);

            // A recopy of the map mod that lands without its scene throws, so RecopyShortCopies counts it as failed.
            var copy = new ShortServerCopy("AD1259", "Anno Domini 1259", gameCopy, ["SceneObj\\Main_map\\scene.xscene"]);
            var failed = Assert.Throws<InvalidOperationException>(() => ExtraModScan.Recopy(copy, server, AD1259Server.CampaignMap));
            Assert.Contains("from " + gameCopy, failed.Message);
            // Another mod of the set without a map is fine.
            Assert.Empty(ExtraModScan.Recopy(copy, server, mapId: null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The Nexus rule is AD1259 only: the Coop Server Tool keeps a newer Workshop copy (MCM) over an older game folder one.
    // The map scene rule only picks the wanted build: the pinned version, or else the same version as the other copy.
    [Fact]
    public void The_source_rules_fit_each_edition()
    {
        var root = Temp();
        try
        {
            var mcm = AD1259Stack.Find("Bannerlord.MBOptionScreen")!;
            var gameMcm = Path.Combine(root, "Modules", "Bannerlord.MBOptionScreen");
            Module(gameMcm, mcm.Id, mcm.Version);
            var workshopMcm = Path.Combine(root, "workshop", "2859238197");
            Module(workshopMcm, mcm.Id, "v5.13.0");
            Assert.True(ExtraModScan.Preference(ad1259: true, gameMcm, false, workshopMcm, true, mcm.Id));
            Assert.False(ExtraModScan.Preference(ad1259: true, workshopMcm, true, gameMcm, false, mcm.Id));
            Assert.Null(ExtraModScan.Preference(ad1259: false, gameMcm, false, workshopMcm, true, mcm.Id));

            // Coop Server Tool: a map copy of another version than the other copy is not forced; the usual score decides.
            var withMap = Path.Combine(root, "Modules", "MoreNationsRemastered");
            Module(withMap, "MoreNationsRemastered", "v1.0.0");
            Write(Path.Combine(withMap, "SceneObj", "Main_map", "scene.xscene"), "map");
            var withoutMap = Path.Combine(root, "workshop", "111");
            Module(withoutMap, "MoreNationsRemastered", "v1.1.0");
            Assert.Null(ExtraModScan.Preference(ad1259: false, withMap, false, withoutMap, true, "MoreNationsRemastered"));
            Module(withoutMap, "MoreNationsRemastered", "v1.0.0");
            Assert.True(ExtraModScan.Preference(ad1259: false, withMap, false, withoutMap, true, "MoreNationsRemastered"));

            // AD1259: the map copy wins only at the pinned version.
            var ad1259 = AD1259Stack.Find("AD1259")!;
            var mapAtPin = Path.Combine(root, "workshop", "222");
            Module(mapAtPin, "AD1259", ad1259.Version);
            Write(Path.Combine(mapAtPin, "SceneObj", "Main_map", "scene.xscene"), "map");
            var game = Path.Combine(root, "Modules", "AD1259");
            Module(game, "AD1259", ad1259.Version);
            Assert.True(ExtraModScan.Preference(ad1259: true, mapAtPin, true, game, false, "AD1259"));
            Module(mapAtPin, "AD1259", "v0.9.0");
            // An old map copy loses to the game copy at the pinned version (the Nexus rule).
            Assert.False(ExtraModScan.Preference(ad1259: true, mapAtPin, true, game, false, "AD1259"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The nested copy frame can run inside LoadHost on Open; the clock and RefreshState wait until the window is filled in.
    [Fact]
    public void The_clock_waits_while_a_copy_runs_before_the_window_is_loaded()
    {
        var window = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml.cs"));
        Assert.Contains("private bool HalfLoaded => _copyingMods && !_addressReady;", window);
        var tick = window[window.IndexOf("_clock.Tick += (_, _) =>", StringComparison.Ordinal)..];
        Assert.True(tick.IndexOf("if (HalfLoaded)", StringComparison.Ordinal) < tick.IndexOf("_session.SyncStartedAt();", StringComparison.Ordinal));
        var refresh = MethodBody(window, "private void RefreshState()");
        Assert.True(refresh.IndexOf("if (HalfLoaded)", StringComparison.Ordinal) < refresh.IndexOf("_session.IsRunning", StringComparison.Ordinal));
        var control = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs"));
        Assert.Contains("_recopyGaveUp.Add(problem.Id);", MethodBody(control, "private void NoteMapProblems(IReadOnlyList<MapCopyProblem> problems, string note)"));
        // The Nexus rule only runs in the AD1259 edition.
        Assert.Contains("Preference(Edition.IsAD1259,", SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ExtraModScan.cs")));
    }

    // Check mods, a setup switch or Set server folder reload the modules while the server may run. The reload only reads
    // the list then: a copy (Install deletes the server copy first) would pull a mod out from under the running server.
    [Fact]
    public void A_reload_never_copies_into_a_running_server()
    {
        var modules = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.Modules.cs"));
        var reload = MethodBody(modules, "private void ReloadModules(bool install = true)");
        // ServerHoldsModFiles is a running server or a running mod test; the callers that may run then pass install off.
        Assert.Contains("private bool ServerHoldsModFiles => _session.IsRunning || _modTestRunning;", modules);
        var guard = reload.IndexOf("install && !ServerHoldsModFiles", StringComparison.Ordinal);
        Assert.True(guard > 0, "ReloadModules checks a running server.");
        Assert.True(guard < reload.IndexOf("ExtraModScan.InstallLockedSetup(", StringComparison.Ordinal));
        Assert.True(guard < reload.IndexOf("InstallAD1259Compat();", StringComparison.Ordinal));
        // The list is still read again.
        Assert.True(reload.IndexOf("ModuleCatalog.Load(ServerRoot)", StringComparison.Ordinal) > reload.IndexOf("InstallAD1259Compat();", StringComparison.Ordinal));
    }

    // Tatache's copy was most likely cut short by a first copy on the UI thread (the window said Not responding). The locked
    // setup copy (Open and every reload) and RefreshWrongVersions (Start) now hand each copy to runCopy, which Coodsz Host
    // runs through WhileCopying with a line first.
    [Fact]
    public void First_copies_run_through_the_copy_hook_with_the_mod_name()
    {
        var root = Temp();
        try
        {
            var version = AD1259Stack.Find("AD1259")!.Version;
            var game = Path.Combine(root, "Modules");
            FullCopy(Path.Combine(game, "AD1259"), version);
            var server = Path.Combine(root, "DedicatedServer");
            var serverCopy = Path.Combine(ModuleCatalog.ModulesRoot(server), "AD1259");

            var ran = new List<string>();
            void Run(string name, Action copy)
            {
                // Nothing is copied before the hook runs the copy.
                Assert.False(File.Exists(Path.Combine(serverCopy, "SubModule.xml")) && ran.Count == 0);
                ran.Add(name);
                copy();
            }

            ExtraModScan.CopyMissing(server, ["AD1259"], [(game, false)], null, Run);
            Assert.Equal(new[] { "AD1259" }, ran);
            Assert.True(ServerModuleInstall.HasMapScene(serverCopy));

            var rows = new List<ModuleRow> { new() { Id = "AD1259", Version = "v1.0.0" } };
            AD1259Server.RefreshWrongVersions(server, rows, AD1259Server.PcCopies(game, []), null, Run);
            Assert.Equal(new[] { "AD1259", "Anno Domini 1259" }, ran);
            Assert.Equal("Copying Anno Domini 1259 into the server. This can take a few minutes, so leave the tool open.", ShortServerCopy.CopyingNew("Anno Domini 1259"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Every_install_of_a_first_copy_runs_inside_the_copy_hook()
    {
        var scan = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ExtraModScan.cs"));
        var copyMissing = MethodBody(scan, "public static (IReadOnlyList<string> Copied, IReadOnlyList<string> Missing) CopyMissing(");
        Assert.Contains("RunCopy(runCopy, found.Name, () => Install(found.Directory, serverRoot, found.Id));", copyMissing);
        Assert.DoesNotContain("            Install(found.Directory", copyMissing);
        Assert.Contains("CopyMissing(serverRoot, order, sources, mapProblems, runCopy);", MethodBody(scan, "public static (IReadOnlyList<string> Copied, IReadOnlyList<string> Missing) InstallLockedSetup("));

        var refresh = MethodBody(SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "AD1259Server.cs")),
            "public static IReadOnlyList<string> RefreshWrongVersions(string serverRoot, IReadOnlyList<ModuleRow> serverRows, PcModCopies sources,");
        var hook = refresh.IndexOf("ExtraModScan.RunCopy(runCopy, mod.Name, () =>", StringComparison.Ordinal);
        Assert.True(hook > 0 && hook < refresh.IndexOf("ExtraModScan.Install(good.Directory, serverRoot, mod.Id);", StringComparison.Ordinal));

        // Coodsz Host passes the hook that runs each copy through WhileCopying.
        var control = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs"));
        Assert.Contains("WhileCopying(ShortServerCopy.CopyingNew(name), () =>", MethodBody(control, "private void CopyIntoServer(string name, Action copy)"));
        Assert.Contains("AD1259Server.RefreshWrongVersions(ServerRoot, _modules.ToList(), pcCopies, mapProblems, CopyIntoServer);", control);
        Assert.Contains("ExtraModScan.InstallLockedSetup(ServerRoot, mapProblems, CopyIntoServer)",
            MethodBody(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.Modules.cs")), "private void ReloadModules(bool install = true)"));
    }

    // The Coop Server Tool has the same gap for a locked setup's map mod (More Nations), so Start copies the picked map
    // again from a Workshop or game folder copy of the same version.
    [Fact]
    public void The_coop_server_tool_copies_a_short_map_mod_again_from_a_copy_of_the_same_version()
    {
        var root = Temp();
        try
        {
            var workshop = Path.Combine(root, "workshop");
            var source = Path.Combine(workshop, "123456");
            Module(source, "MoreNationsRemastered", "v2.0.0");
            Write(Path.Combine(source, "SceneObj", "Main_map", "scene.xscene"), "map");
            var server = Path.Combine(root, "DedicatedServer");
            var serverCopy = Path.Combine(ModuleCatalog.ModulesRoot(server), "MoreNationsRemastered");
            Module(serverCopy, "MoreNationsRemastered", "v2.0.0");

            Assert.Null(ExtraModScan.ShortMapCopy(server, StableCoop.VanillaMap, [workshop]));
            var copy = ExtraModScan.ShortMapCopy(server, "MoreNationsRemastered", [workshop]);
            Assert.NotNull(copy);
            Assert.Equal("MoreNationsRemastered", copy!.Name);
            Assert.Empty(ExtraModScan.Recopy(copy, server));
            Assert.True(File.Exists(Path.Combine(serverCopy, "SceneObj", "Main_map", "scene.xscene")));
            Assert.Null(ExtraModScan.ShortMapCopy(server, "MoreNationsRemastered", [workshop]));

            // The refusal picks the Workshop line for a map mod this PC has from the Workshop.
            Assert.True(ExtraModScan.IsOnWorkshop(server, "MoreNationsRemastered", [workshop]));
            Assert.False(ExtraModScan.IsOnWorkshop(server, "MoreNationsRemastered", [Path.Combine(root, "nothing")]));

            // Another version on the PC is not the same mod build, so the server copy stays as it is.
            Directory.Delete(Path.Combine(serverCopy, "SceneObj"), recursive: true);
            Module(source, "MoreNationsRemastered", "v2.1.0");
            Assert.Null(ExtraModScan.ShortMapCopy(server, "MoreNationsRemastered", [workshop]));
            Assert.False(Directory.Exists(Path.Combine(serverCopy, "SceneObj")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Start_refuses_a_map_mod_without_its_main_map_on_the_server()
    {
        var root = Temp();
        try
        {
            var server = Path.Combine(root, "DedicatedServer");
            var modules = ModuleCatalog.ModulesRoot(server);
            Module(Path.Combine(modules, "AD1259"), "AD1259", "v1.4.6");

            Assert.Equal(Path.Combine("engine", "Modules", "AD1259", "SceneObj", "Main_map"), MissingCampaignMap.MissingFolder(server, AD1259Server.CampaignMap));
            // An empty Main_map folder brings no map either: the scene file is what counts (Codex, release head review).
            Directory.CreateDirectory(Path.Combine(modules, "AD1259", "SceneObj", "Main_map"));
            Assert.Equal(Path.Combine("engine", "Modules", "AD1259", "SceneObj", "Main_map"), MissingCampaignMap.MissingFolder(server, AD1259Server.CampaignMap));
            Write(Path.Combine(modules, "AD1259", "SceneObj", "Main_map", "scene.xscene"), "map");
            Assert.Null(MissingCampaignMap.MissingFolder(server, AD1259Server.CampaignMap));

            // The vanilla map needs no map mod.
            Assert.Null(MissingCampaignMap.MissingFolder(server, StableCoop.VanillaMap));
            Assert.Null(MissingCampaignMap.MissingFolder(server, ""));
            Assert.Null(MissingCampaignMap.MissingFolder(server, null));

            // A map mod in a folder with another name is found by its SubModule.xml id, like the start script does.
            Module(Path.Combine(modules, "More Nations"), "MoreNationsRemastered", "v2.0.0");
            Assert.Equal(Path.Combine("engine", "Modules", "More Nations", "SceneObj", "Main_map"), MissingCampaignMap.MissingFolder(server, "MoreNationsRemastered"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_refusal_says_where_to_get_the_map_again()
    {
        var folder = Path.Combine("engine", "Modules", "AD1259", "SceneObj", "Main_map");
        var ad1259 = MissingCampaignMap.Text("AD1259", folder, AD1259Stack.Find("AD1259"));
        Assert.Equal(
            "The server did not start, because the AD1259 map files are missing on the server (" + folder + "). It would start a plain Calradia world, "
            + "and every player who joins would crash. Download Anno Domini 1259 " + AD1259Stack.Find("AD1259")!.Version + " again from Nexus Mods, put its AD1259 folder "
            + "in the game's Modules folder and replace the old folder, then press Start server. The tool copies it to the server.",
            ad1259);
        // AD1259 comes from Nexus Mods, so the line never sends the host to the Steam Workshop for it.
        Assert.DoesNotContain("Workshop", ad1259);
        Assert.Contains("Unsubscribe from Realistic Battle Mod on the Steam Workshop", MissingCampaignMap.Text("RBM", folder, AD1259Stack.Find("RBM")));
        // The Coop Server Tool: the Workshop line when this PC has the map mod from the Workshop, else the download line.
        Assert.Contains("Unsubscribe from More Nations on the Steam Workshop and subscribe again", MissingCampaignMap.Text("More Nations", folder, null, onWorkshop: true));
        // The download line names the real folder, not the display name.
        var download = MissingCampaignMap.Text("More Nations", Path.Combine("engine", "Modules", "MoreNationsRemastered", "SceneObj", "Main_map"), null);
        Assert.Contains("Download the same version of More Nations again from Nexus Mods or wherever you got it, put its MoreNationsRemastered folder in the game's Modules folder and replace the old folder", download);
        Assert.DoesNotContain("Workshop", download);
    }

    // The start script only counts turned on modules, so a turned off map mod gets its own line before the launch.
    [Fact]
    public void Start_refuses_a_turned_off_map_mod()
    {
        var modules = new[] { ("Native", true), ("AD1259", false) };
        Assert.True(MissingCampaignMap.IsTurnedOff(" ad1259 ", modules));
        Assert.False(MissingCampaignMap.IsTurnedOff("AD1259", [("AD1259", true)]));
        Assert.False(MissingCampaignMap.IsTurnedOff(StableCoop.VanillaMap, [("Vanilla", false)]));
        Assert.False(MissingCampaignMap.IsTurnedOff("AD1259", [("Native", true)]));
        Assert.Equal("The server did not start, because the Anno Domini 1259 map mod is turned off in this setup. It would start a plain Calradia world, "
            + "and every player who joins would crash. On the Mods page, turn Anno Domini 1259 on, then press Start server.",
            MissingCampaignMap.TurnedOffText("Anno Domini 1259"));
    }

    [Fact]
    public void The_lines_for_a_copy_that_stays_short_name_the_files()
    {
        var copy = new ShortServerCopy("AD1259", "Anno Domini 1259", "source", ["a.xml", "b.xml"]);
        Assert.Equal("Anno Domini 1259 still lacks files on the server after a new copy (for example c.dll, d.dll, e.dll). It is not copied again until you close and open the tool.",
            copy.StillShort(["c.dll", "d.dll", "e.dll", "f.dll"]));
        Assert.Equal("Copied Anno Domini 1259 into the server again from source, because the server copy was missing files (for example a.xml, b.xml).", copy.Copied);
        // A copy that fails is not tried again in this run, and the line says so.
        Assert.Equal("Could not copy Anno Domini 1259 into the server again (Access denied). It is not tried again until you close and open the tool.", copy.Failed("Access denied."));
        Assert.Equal("Could not compare the map mod's server copy with the copy on this PC, so it was not copied again (Access denied).", ShortServerCopy.CheckFailed("Access denied. "));
    }

    // A copy that throws (a locked file) joins the given up mods, so no Start copies several GB only to fail again.
    [Fact]
    public void A_failed_copy_is_not_tried_again_and_a_failed_map_check_shows_in_reports()
    {
        var source = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs"));
        var recopy = MethodBody(source, "private bool RecopyShortCopies(IEnumerable<ShortServerCopy> copies, string note)");
        var failedAt = recopy.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var failedCatch = recopy[failedAt..recopy.IndexOf("continue;", failedAt, StringComparison.Ordinal)];
        Assert.Contains("_recopyGaveUp.Add(copy.Id);", failedCatch);
        Assert.Contains("copy.Failed(ex.Message)", failedCatch);
        // The lambda takes the server folder read before the copy, not ServerRoot from the worker thread.
        Assert.Contains("ExtraModScan.Recopy(copy, root, mapId)", recopy);

        var map = MethodBody(source, "private void RecopyIncompleteMap()");
        var mapAt = map.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var mapCatch = map[mapAt..map.IndexOf("return;", mapAt, StringComparison.Ordinal)];
        Assert.Contains("ShortServerCopy.CheckFailed(ex.Message)", mapCatch);
        Assert.Contains("Append(line);", mapCatch);
        Assert.Contains("_actions.Note(", mapCatch);

        // The Coop Server Tool refuses a turned off map mod before it could copy it again.
        var start = MethodBody(source, "private void StartServer()");
        var off = start.IndexOf("RefuseTurnedOffMap(", StringComparison.Ordinal);
        Assert.True(off > 0 && off < start.IndexOf("RecopyIncompleteMap();", StringComparison.Ordinal));
    }

    [Fact]
    public void The_start_script_is_told_the_map_and_its_refusal_is_no_crash()
    {
        Assert.Null(MissingCampaignMap.EnvironmentValue(StableCoop.VanillaMap));
        Assert.Null(MissingCampaignMap.EnvironmentValue(" "));
        Assert.Equal("AD1259", MissingCampaignMap.EnvironmentValue(" AD1259 "));

        Assert.True(MissingCampaignMap.IsScriptLine("Campaign map missing: the Coodsz tool picked the AD1259 map, but"));
        Assert.True(MissingCampaignMap.IsScriptLine("  Campaign map missing: the Coodsz tool picked the AD1259 map, but"));
        // Only a line that starts with the marker, so a mod's own output that quotes it sets nothing.
        Assert.False(MissingCampaignMap.IsScriptLine("[SomeMod] said Campaign map missing: earlier"));
        Assert.True(MissingCampaignMap.IsScriptStop(sawMarker: true, wasExpected: false));
        // Only the marker line counts: the script passes the engine's code on, so a bare code 20 could be an engine
        // crash. A Stop is never it.
        Assert.False(MissingCampaignMap.IsScriptStop(sawMarker: false, wasExpected: false));
        Assert.False(MissingCampaignMap.IsScriptStop(sawMarker: true, wasExpected: true));
    }

    // WhileCopying runs a DispatcherFrame, so timers still fire while a mod is copied into the server. Every timer driven
    // start, restart or stop, and ReloadModules, checks the copy flag first; the rest go through BeginControl, which does.
    [Theory]
    [InlineData("MainWindow.ServerControl.cs", "private void StartServer()")]
    [InlineData("MainWindow.ServerControl.cs", "private bool BeginControl(string message)")]
    [InlineData("MainWindow.ServerControl.cs", "private void HoldBrokenAutosave()")]
    [InlineData("MainWindow.CrashRestart.cs", "private void ScheduleCrashRestart(TimeSpan delay)")]
    [InlineData("MainWindow.AutoRescue.cs", "private bool RetryAfterLoadCrash(ServerExitInfo exit, LoadCrashStep step, string? crashLine)")]
    [InlineData("MainWindow.AutoRestart.cs", "private void CheckAutoRestart()")]
    [InlineData("MainWindow.Modules.cs", "private void ReloadModules(bool install = true)")]
    public void Timer_driven_server_actions_wait_while_a_mod_is_copied(string file, string signature)
    {
        var body = MethodBody(SourceFiles.Read(Path.Combine("src", "CoodszHost", file)), signature);
        Assert.Contains("_copyingMods", body);
    }

    [Theory]
    [InlineData("MainWindow.CrashRestart.cs", "private void RestartAfterCrash()")]
    [InlineData("MainWindow.AutoRescue.cs", "private async Task StartAfterLoadCrashAsync()")]
    [InlineData("MainWindow.ServerControl.cs", "private async Task<bool> RestartServerAsync()")]
    public void Timer_driven_starts_go_through_begin_control_first(string file, string signature)
    {
        var body = MethodBody(SourceFiles.Read(Path.Combine("src", "CoodszHost", file)), signature);
        var begin = body.IndexOf("BeginControl(", StringComparison.Ordinal);
        var start = body.IndexOf("StartServer()", StringComparison.Ordinal);
        Assert.True(begin > 0 && begin < start, signature + " calls BeginControl before StartServer.");
    }

    [Fact]
    public void The_copy_flag_covers_the_whole_copy()
    {
        var body = MethodBody(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs")), "private T WhileCopying<T>(string line, Func<T> work)");
        var set = body.IndexOf("_copyingMods = true;", StringComparison.Ordinal);
        var frame = body.IndexOf("Dispatcher.PushFrame(frame);", StringComparison.Ordinal);
        var clear = body.IndexOf("_copyingMods = false;", StringComparison.Ordinal);
        var cleanup = body.IndexOf("finally", StringComparison.Ordinal);
        Assert.True(set > 0 && set < frame && cleanup < clear, "The flag is set before the frame runs and cleared in finally.");
    }

    // The method from its signature to its matching closing brace.
    private static string MethodBody(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature + " is in the source.");
        var open = source.IndexOf('{', at);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[at..(i + 1)];
        }

        return source[at..];
    }

    [Fact]
    public void Start_script_refuses_a_missing_map_before_the_engine_starts()
    {
        var script = SourceFiles.Read(Path.Combine("src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        var found = script.IndexOf("$customMapModuleIds = @(", StringComparison.Ordinal);
        var check = script.IndexOf("$campaignMapProblem = Get-MissingCampaignMapProblem", StringComparison.Ordinal);
        var engine = script.IndexOf("Write-Host 'Starting the native engine.", StringComparison.Ordinal);
        Assert.True(found > 0 && found < check && check < engine, "The map check runs after the map mods are found and before the engine starts.");
        Assert.Contains("-ExpectedModuleId $env:" + MissingCampaignMap.EnvironmentVariable, script);
        Assert.Contains("    exit " + MissingCampaignMap.ScriptExitCode + "\r\n", script.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.Contains("return (\"" + MissingCampaignMap.ScriptMarker, script);
        Assert.True(script.All(ch => ch < 128), "Start-ModdedServer.ps1 stays ASCII");
    }

    [WindowsFact]
    public void The_script_check_passes_a_present_map_and_refuses_a_missing_one()
    {
        var script = SourceFiles.Read(Path.Combine("src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        var function = System.Text.RegularExpressions.Regex.Match(script, @"(?ms)^function\s+Get-MissingCampaignMapProblem\s*\{.*?^\}").Value;
        var provider = System.Text.RegularExpressions.Regex.Match(script, @"(?ms)^function\s+Test-CampaignMapProvider\s*\{.*?^\}").Value;
        Assert.NotEmpty(function);
        Assert.NotEmpty(provider);
        Assert.Contains("(Test-CampaignMapProvider -ModuleRoot $installedModules[$_])", script);

        var root = Temp();
        Directory.CreateDirectory(root);
        try
        {
            // An existing Main_map folder without scene.xscene is no map provider, so the check stops (Codex, release head review).
            var empty = Path.Combine(root, "empty");
            Directory.CreateDirectory(Path.Combine(empty, "SceneObj", "Main_map"));
            var whole = Path.Combine(root, "whole");
            Write(Path.Combine(whole, "SceneObj", "Main_map", "scene.xscene"), "map");
            var output = Path.Combine(root, "out.txt");
            var run = function + "\r\n" + provider + "\r\n" +
                      "function Show($value) { if ($null -eq $value) { 'ok' } else { $value } }\r\n" +
                      "function Providers($root) { @(if (Test-CampaignMapProvider -ModuleRoot $root) { 'AD1259' }) }\r\n" +
                      "$lines = @(" +
                      "(Show (Get-MissingCampaignMapProblem -ExpectedModuleId 'AD1259' -CustomMapModuleIds (Providers '" + empty + "')))," +
                      "(Show (Get-MissingCampaignMapProblem -ExpectedModuleId 'AD1259' -CustomMapModuleIds (Providers '" + whole + "')))," +
                      "(Show (Get-MissingCampaignMapProblem -ExpectedModuleId 'AD1259' -CustomMapModuleIds @()))," +
                      "(Show (Get-MissingCampaignMapProblem -ExpectedModuleId 'AD1259' -CustomMapModuleIds @('AD1259')))," +
                      "(Show (Get-MissingCampaignMapProblem -ExpectedModuleId ' ad1259 ' -CustomMapModuleIds @('AD1259')))," +
                      "(Show (Get-MissingCampaignMapProblem -ExpectedModuleId 'AD1259' -CustomMapModuleIds @('MoreNationsRemastered')))," +
                      "(Show (Get-MissingCampaignMapProblem -ExpectedModuleId '' -CustomMapModuleIds @()))," +
                      "(Show (Get-MissingCampaignMapProblem -ExpectedModuleId $null -CustomMapModuleIds @())))\r\n" +
                      "[System.IO.File]::WriteAllLines('" + output + "', [string[]]$lines)\r\n";
            File.WriteAllText(Path.Combine(root, "run.ps1"), run);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "run.ps1") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(process);
            Assert.True(process!.WaitForExit(120000), "The map check did not finish.");
            var lines = File.ReadAllLines(output);
            Assert.Equal(8, lines.Length);
            Assert.StartsWith(MissingCampaignMap.ScriptMarker, lines[0]);
            Assert.Equal("ok", lines[1]);
            lines = lines[2..];
            Assert.Equal(
                "Campaign map missing: the Coodsz tool picked the AD1259 map, but no active module has SceneObj\\Main_map\\scene.xscene on this server "
                + "(its files are missing or the module is turned off), so it would start a plain Calradia world. "
                + "Copy AD1259 into engine\\Modules again or turn it on, then start the server.",
                lines[0]);
            Assert.Equal("ok", lines[1]);
            Assert.Equal("ok", lines[2]);
            Assert.StartsWith(MissingCampaignMap.ScriptMarker, lines[3]);
            Assert.Equal("ok", lines[4]);
            Assert.Equal("ok", lines[5]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
