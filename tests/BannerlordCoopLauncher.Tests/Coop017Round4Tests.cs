using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Steam;
using BurningEmpires.Core;
using CoodszHost.Services;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// Round 4 of the Coop 0.1.7 work: the live test of a manual Modules\Coop next to the Workshop Coop, Coop #60
// (CR-F86VX), Devin's round 3 nits, the Windows download mark and a Coop unzipped one folder too deep.
public sealed class Coop017Round4Tests
{
    private const string Game = @"C:\game";
    private const string WorkshopRoot = @"C:\steam\steamapps\workshop\content\261550";

    private static string Xml(string id, string version) =>
        "<Module><Id value=\"" + id + "\"/><Name value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>";

    [WindowsTheory]
    [InlineData(true, "Coop v0.1.7 from the game's Modules folder. Pinned, so Steam's version check is skipped.")]
    [InlineData(false, "Coop v0.1.6 from the Steam Workshop. Pinned, so Steam's version check is skipped.")]
    public void The_coop_row_names_the_loaded_coop_and_where_it_comes_from(bool manual, string detail)
    {
        var files = new MemoryFileSystem();
        files.AddFile(Path.Combine(Game, "bin", "Win64_Shipping_Client", "Bannerlord.exe"), "");
        files.AddFile(Path.Combine(Game, "Modules", "Native", "SubModule.xml"), Xml("Native", "v1.4.8"));
        files.AddFile(Path.Combine(WorkshopRoot, CoopInstall.WorkshopId, "SubModule.xml"), Xml("Coop", "v0.1.6"));
        if (manual)
            files.AddFile(Path.Combine(Game, "Modules", "Coop", "SubModule.xml"), Xml("Coop", "v0.1.7"));
        var profile = ProfileJson.Parse("""
            { "Name": "C", "Port": 4200, "BaseModules": ["Native"],
              "Mods": [{ "Name": "Bannerlord Coop", "WorkshopId": "3770450698", "ModuleId": "Coop", "Enabled": true, "Pinned": true }],
              "ModuleLoadOrder": ["Native", "Coop"] }
            """);
        WorkshopLibrary[] workshops = [new(WorkshopRoot, new Dictionary<string, long> { [CoopInstall.WorkshopId] = 10 }, ManifestParsed: true)];

        var coop = Assert.Single(ModuleResolver.Resolve(profile, Game, workshops, null, files).Modules);

        Assert.Equal(detail, coop.Detail);
        Assert.Equal("Bannerlord Coop v0.1.7 from the game's Modules folder is the one the game loads.", CoopInstall.LoadedLine("v0.1.7", inModules: true));
        var check = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher"), "private async void CheckMods_Click(");
        Assert.Contains("Log(CoopInstall.LoadedLine(coopVersion, CoopInstall.IsInModules(coop.Directory, _gameRoot)));", check);
    }

    [Fact]
    public void The_ad1259_row_says_modules_for_a_copy_there()
    {
        var coop = AD1259Stack.Find("Coop")!;
        Assert.Equal("v0.1.7 \u00b7 Modules", new AD1259ModStatus(coop, AD1259ModState.Ready, "v0.1.7", ["Modules\\Coop"], [@"C:\g\Modules\Coop"]).RowDetail);
        Assert.Equal("v0.1.6 \u00b7 Steam Workshop", new AD1259ModStatus(coop, AD1259ModState.Ready, "v0.1.6", ["Workshop 3770450698"], [@"C:\w\3770450698"]).RowDetail);
        var native = AD1259Stack.ClientMods.First(mod => mod.BaseGame);
        Assert.EndsWith("\u00b7 Bannerlord", new AD1259ModStatus(native, AD1259ModState.Ready, native.Version, ["Modules\\" + native.Id], [@"C:\g"]).RowDetail);
    }

    [Fact]
    public void The_be_set_text_names_no_coop_version()
    {
        Assert.DoesNotContain("0.1.6", BeStack.SetText);
        Assert.EndsWith(", Bannerlord Coop", BeStack.SetText);
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
            Assert.True(BannerlordCoopLauncher.Core.Localization.Translator.Entries(language).ContainsKey(BeStack.SetText), language);
    }

    [Fact]
    public void A_fresh_setup_never_saves_switches_the_page_never_loaded()
    {
        Assert.True(ServerConfigFlags.Defaults["steam"]);
        Assert.True(ServerConfigFlags.Defaults["logFile"]);
        Assert.False(ServerConfigFlags.Defaults["autoRestart"]);
        const string file = "{ \"steam\": true, \"logFile\": false }";
        // Never loaded and left at its default: the file's value stays.
        Assert.True(ServerConfigFlags.ToWrite("steam", true, loadedFromFile: false, file));
        Assert.False(ServerConfigFlags.ToWrite("logFile", true, loadedFromFile: false, file));
        // The host changed it from its default, or the page loaded it from the file: the box wins.
        Assert.False(ServerConfigFlags.ToWrite("steam", false, loadedFromFile: false, file));
        Assert.True(ServerConfigFlags.ToWrite("logFile", true, loadedFromFile: true, file));
        // A key the file doesn't have takes the box; automatic restart always takes the box.
        Assert.True(ServerConfigFlags.ToWrite("allowClientTimeControls", true, loadedFromFile: false, file));
        Assert.False(ServerConfigFlags.ToWrite("autoRestart", false, loadedFromFile: false, "{ \"autoRestart\": true }"));

        var host = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        var load = SourceFiles.Method(host, "private void LoadDifficulty()");
        Assert.True(load.IndexOf("SteamBox.IsChecked = ServerConfigFlags.Defaults[\"steam\"];", StringComparison.Ordinal)
            < load.IndexOf("if (!File.Exists(ServerSession.ModConfigPath))", StringComparison.Ordinal), "defaults before the no mod-config return");
        Assert.Contains("ServerConfigFlags.ToWrite(\"steam\", SteamBox.IsChecked == true, _serverFlagsLoaded, server)", host);
        Assert.Contains("ServerConfigFlags.ToWrite(\"logFile\", LogFileBox.IsChecked == true, _serverFlagsLoaded, server)", host);
    }

    [Fact]
    public void Be_gone_pick_reaches_the_start_notes_and_an_older_pick_never_replaces_quietly()
    {
        var host = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost");
        Assert.Contains("ForgetGonePick(NoteStart);", SourceFiles.Method(host, "private async Task<bool> RefreshServerFolderAsync("));
        var prepare = SourceFiles.Method(host, "private async Task<bool> PrepareServerAsync(bool automatic = false)");
        Assert.Contains("if (BeServerPrepare.ServerIsNewerThan(serverRoot, serverSource))", prepare);
        Assert.DoesNotContain("!picked && BeServerPrepare.ServerIsNewerThan", prepare);
        var refresh = SourceFiles.Method(host, "private async Task<bool> RefreshServerFolderAsync(");
        Assert.True(refresh.IndexOf("BeServerPrepare.ServerIsNewerThan(server, source.Folder)", StringComparison.Ordinal)
            < refresh.IndexOf("BeServerPrepare.Prepare(source.Folder, server", StringComparison.Ordinal));

        var root = NewRoot("be-older-pick");
        try
        {
            var picked = WorkshopServer(Path.Combine(root, "pick"));
            var server = WorkshopServer(Path.Combine(root, "server"));
            Module(Path.Combine(server, "engine", "Modules"), "Coop", "Coop", "v0.1.7");
            Assert.True(BeServerPrepare.ServerIsNewerThan(server, picked));
            Assert.StartsWith("The server runs Coop v0.1.7, which is newer than Coop v0.1.6 in the Coop server folder you picked",
                BeServerPrepare.AutoPrepareKeptNewer(server, picked, picked: true));
        }
        finally
        {
            Delete(root);
        }
    }

    // A real Zone.Identifier stream, the way Windows marks a file unzipped from a download.
    private static string Marked(string path)
    {
        Write(path, "x");
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");
        return path;
    }

    [WindowsFact]
    public void Only_modules_coop_and_the_server_folder_lose_their_download_marks()
    {
        var root = NewRoot("coop-motw");
        try
        {
            var game = Path.Combine(root, "game");
            var coopDll = Marked(Path.Combine(game, "Modules", "Coop", "bin", "Win64_Shipping_Client", "Coop.Core.dll"));
            Marked(Path.Combine(game, "Modules", "Coop", "SubModule.xml"));
            var otherMod = Marked(Path.Combine(game, "Modules", "SomeMod", "bin", "Win64_Shipping_Client", "SomeMod.dll"));
            var gameExe = Marked(Path.Combine(game, "bin", "Win64_Shipping_Client", "Bannerlord.exe"));

            var lines = CoopInstall.CheckModulesCoop(game);

            Assert.Equal(["Unblocked 2 Coop files that Windows had marked as downloaded from the internet."], lines);
            Assert.False(File.Exists(coopDll + ":Zone.Identifier"));
            Assert.True(File.Exists(otherMod + ":Zone.Identifier"));
            Assert.True(File.Exists(gameExe + ":Zone.Identifier"));
            Assert.Empty(CoopInstall.CheckModulesCoop(game));

            var server = Path.Combine(root, "CoopServer");
            var serverExe = Marked(Path.Combine(server, CoopInstall.ServerExeName));
            Marked(Path.Combine(server, "engine", "bin", "Win64_Shipping_Server", "TaleWorlds.Starter.DotNetCore.dll"));
            var beside = Marked(Path.Combine(root, "beside.dll"));
            Assert.Equal(2, CoopInstall.ClearServerMarks(server));
            Assert.False(File.Exists(serverExe + ":Zone.Identifier"));
            Assert.True(File.Exists(beside + ":Zone.Identifier"));
            Assert.Equal("Unblocked 1 Coop file that Windows had marked as downloaded from the internet.", CoopInstall.UnblockedLine(1));
            Assert.Null(CoopInstall.UnblockedLine(0));
            Assert.Equal(0, CoopInstall.ClearModulesCoopMarks(Path.Combine(root, "no game")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Every_client_launcher_checks_modules_coop_on_check_mods_and_before_the_game_starts()
    {
        var friends = SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher");
        Assert.Contains("CoopInstall.CheckModulesCoop(_gameRoot)", SourceFiles.Method(friends, "private async void CheckMods_Click("));
        Assert.Contains("CoopInstall.CheckModulesCoop(_gameRoot)", SourceFiles.Method(friends, "private async Task LaunchAsync("));
        var ad1259 = SourceFiles.Window(SourceFiles.Root(), "AD1259Launcher");
        Assert.Contains("CoopInstall.CheckModulesCoop(_gameRoot)", SourceFiles.Method(ad1259, "private void CheckMods_Click("));
        Assert.Contains("CoopInstall.CheckModulesCoop(_gameRoot)", SourceFiles.Method(ad1259, "private async void Connect_Click("));
        foreach (var project in new[] { "BurningEmpiresLauncher", "TaomCoopLauncher" })
        {
            var window = SourceFiles.Window(SourceFiles.Root(), project);
            Assert.Contains("CoopInstall.CheckModulesCoop(_gameRoot", SourceFiles.Method(window, "private void CheckMods_Click("));
            // The TAOM Connect body sits behind its one Connect at a time gate (TAOM CR-W4H9X).
            Assert.Contains("CoopInstall.CheckModulesCoop(_gameRoot", SourceFiles.Method(window,
                project == "TaomCoopLauncher" ? "private async Task ConnectOnceAsync()" : "private async Task ConnectAsync()"));
        }

        Assert.Contains("CoopInstall.ClearServerMarks(ServerRoot)", SourceFiles.Window(SourceFiles.Root(), "CoodszHost"));
        var be = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost");
        Assert.Contains("CoopInstall.ClearServerMarks(serverSource)", SourceFiles.Method(be, "private async Task<bool> PrepareServerAsync("));
        Assert.Contains("CoopInstall.ClearServerMarks(source.Folder)", SourceFiles.Method(be, "private async Task<bool> RefreshServerFolderAsync("));
    }

    [Fact]
    public void A_coop_unzipped_one_folder_too_deep_is_named_and_a_server_one_folder_too_deep_is_found()
    {
        var root = NewRoot("coop-nested");
        try
        {
            var game = Path.Combine(root, "game");
            Module(Path.Combine(game, "Modules", "Coop"), "Coop", "Coop", "v0.1.7");
            Assert.True(CoopInstall.IsNested(game));
            Assert.Contains(CoopInstall.NestedLine, CoopInstall.CheckModulesCoop(game));

            Module(Path.Combine(game, "Modules"), "Coop", "Coop", "v0.1.7");
            Assert.False(CoopInstall.IsNested(game));

            // C:\CoopServer picked, with the .7z's own folder inside it.
            var picked = Path.Combine(root, "CoopServer");
            var inner = Path.Combine(picked, "BannerlordCoop-DedicatedServer-Win64-v0.1.7");
            Write(Path.Combine(inner, CoopInstall.ServerExeName), "exe");
            Directory.CreateDirectory(Path.Combine(inner, "engine"));
            Assert.Equal(inner, CoopInstall.ServerFolderIn(picked));
            Assert.Equal(Path.Combine(inner, CoopInstall.ServerExeName), DedicatedServerFinder.ExeInFolder(picked));
            Assert.Equal(inner, BeServerPrepare.PickedServer(picked));

            // Two servers inside: no guess.
            Write(Path.Combine(picked, "second", CoopInstall.ServerExeName), "exe");
            Directory.CreateDirectory(Path.Combine(picked, "second", "engine"));
            Assert.Null(CoopInstall.ServerFolderIn(picked));
        }
        finally
        {
            Delete(root);
        }

        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var table = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language);
            Assert.True(table.ContainsKey(CoopInstall.NestedLine), language);
        }
    }
}
