using System.Diagnostics;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// Round 5 of the Coop 0.1.7 work: safe download mark sweeps (junctions, broad folders, locked marks), the ModDB
// package layouts, and the v0.1.7 live test findings (password and Steam at Start, the antivirus line, the Coop
// server note, the Active Coop source, the movement limit boxes).
public sealed class Coop017Round5Tests
{
    private static string Marked(string path)
    {
        Write(path, "x");
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");
        return path;
    }

    private static bool HasMark(string path) => File.Exists(path + ":Zone.Identifier");

    [WindowsFact]
    public void The_sweep_skips_junctions_and_file_links_and_goes_past_a_locked_mark()
    {
        var root = NewRoot("motw-links");
        try
        {
            var outside = Marked(Path.Combine(root, "outside", "target.dll"));
            var swept = Path.Combine(root, "swept");
            var own = Marked(Path.Combine(swept, "own.dll"));
            var locked = Marked(Path.Combine(swept, "locked.dll"));
            var junction = Path.Combine(swept, "junction");
            using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + junction + "\" \"" + Path.Combine(root, "outside") + "\"")
                   { CreateNoWindow = true, UseShellExecute = false })!)
                mklink.WaitForExit();
            Assert.True(Directory.Exists(junction), "the junction was made");
            var fileLink = Path.Combine(swept, "link.dll");
            var hasFileLink = true;
            try
            {
                File.CreateSymbolicLink(fileLink, outside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file symbolic link needs Developer Mode or admin rights; the junction part still runs.
                hasFileLink = false;
            }

            int cleared;
            using (new FileStream(locked + ":Zone.Identifier", FileMode.Open, FileAccess.Read, FileShare.None))
                cleared = DownloadMark.Clear(swept);

            Assert.Equal(1, cleared);
            Assert.False(HasMark(own));
            Assert.True(HasMark(locked));
            Assert.True(HasMark(outside), "nothing behind a junction or a file link is touched");
            _ = hasFileLink;
            Assert.Equal(1, DownloadMark.Clear(swept));
        }
        finally
        {
            foreach (var link in new[] { Path.Combine(root, "swept", "junction"), Path.Combine(root, "swept", "link.dll") })
            {
                try
                {
                    if (Directory.Exists(link))
                        Directory.Delete(link);
                    else if (File.Exists(link))
                        File.Delete(link);
                }
                catch (IOException)
                {
                }
            }

            Delete(root);
        }
    }

    // The ModDB DedicatedServer.11.7z layout: the exe and the Electron manager at the top, engine and server-data.
    private static string ModDbServer(string folder)
    {
        Write(Path.Combine(folder, CoopInstall.ServerExeName), "exe");
        Write(Path.Combine(folder, "BannerlordCoopServerManager.exe"), "manager");
        Module(Path.Combine(folder, "engine", "Modules"), "Coop", "Coop", "v0.1.7");
        Directory.CreateDirectory(Path.Combine(folder, "server-data", "Game Saves"));
        return folder;
    }

    [WindowsFact]
    public void Only_a_real_server_folder_is_swept_and_broad_folders_never_are()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.True(CoopInstall.IsBroadFolder(Path.GetPathRoot(profile)));
        Assert.True(CoopInstall.IsBroadFolder(profile));
        Assert.True(CoopInstall.IsBroadFolder(Path.GetDirectoryName(profile)));
        Assert.True(CoopInstall.IsBroadFolder(Path.Combine(profile, "Downloads")));
        Assert.True(CoopInstall.IsBroadFolder(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
        Assert.True(CoopInstall.IsBroadFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
        Assert.True(CoopInstall.IsBroadFolder(""));

        var root = NewRoot("server-guard");
        try
        {
            Assert.False(CoopInstall.IsBroadFolder(root));
            var extract = ModDbServer(Path.Combine(root, "DedicatedServer.11"));
            Assert.True(CoopInstall.IsServerFolder(extract));
            Assert.Equal(extract, CoopInstall.ServerFolderIn(extract));
            Assert.Equal(extract, CoopInstall.ServerFolderIn(root));
            Assert.Equal(Path.Combine(extract, CoopInstall.ServerExeName), DedicatedServerFinder.ExeInFolder(root));
            var mark = Marked(Path.Combine(extract, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Coop.Core.dll"));
            Assert.Equal(1, CoopInstall.ClearServerMarks(extract));
            Assert.False(HasMark(mark));

            // No engine folder: not a server folder, so nothing is swept.
            var lone = Path.Combine(root, "lone");
            Write(Path.Combine(lone, CoopInstall.ServerExeName), "exe");
            var loneMark = Marked(Path.Combine(lone, "x.dll"));
            Assert.False(CoopInstall.IsServerFolder(lone));
            Assert.Equal(0, CoopInstall.ClearServerMarks(lone));
            Assert.True(HasMark(loneMark));

            // A bad saved path never escapes.
            Assert.Equal(0, CoopInstall.ClearServerMarks("bad|path"));
            Assert.Equal(0, CoopInstall.ClearModulesCoopMarks("bad|path"));
        }
        finally
        {
            Delete(root);
        }

        var host = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private void SetServerFolder_Click(");
        Assert.Contains("CoopInstall.IsServerFolder(Path.GetDirectoryName(dialog.FileName))", host);
    }

    [Fact]
    public void The_moddb_and_github_layouts_each_get_one_fix_line()
    {
        var root = NewRoot("coop-layouts");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            Module(modules, "Native", "Native", "v1.4.8");

            // a) "Extract to Coop.10\": a folder with another name, read by its Id.
            Module(modules, "Coop.10", "Coop", "v0.1.7");
            Assert.Equal([CoopInstall.MisnamedLine("Coop.10")], CoopInstall.LayoutProblems(game));
            Assert.Equal("Coop is in Modules\\Coop.10. Rename that folder to Coop.", CoopInstall.MisnamedLine("Coop.10"));

            // Beside a real Modules\Coop it is a second copy.
            Module(modules, "Coop", "Coop", "v0.1.7");
            Assert.Equal([CoopInstall.DuplicateLine("Coop.10")], CoopInstall.LayoutProblems(game));
            Directory.Delete(Path.Combine(modules, "Coop.10"), recursive: true);
            Assert.Empty(CoopInstall.LayoutProblems(game));
            Directory.Delete(Path.Combine(modules, "Coop"), recursive: true);

            // b) "Extract here": loose files straight in Modules, by SubModule.xml or by the DLL alone.
            var looseXml = Marked(Path.Combine(modules, "SubModule.xml"));
            File.WriteAllText(looseXml, "<Module><Id value=\"Coop\"/><Version value=\"v0.1.7\"/></Module>");
            File.WriteAllText(looseXml + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");
            Assert.Equal([CoopInstall.LooseLine], CoopInstall.CheckModulesCoop(game));
            Assert.True(HasMark(looseXml), "Modules itself is never swept");
            File.Delete(looseXml);
            Write(Path.Combine(modules, "bin", "Win64_Shipping_Client", "Coop.dll"), "dll");
            Assert.Equal([CoopInstall.LooseLine], CoopInstall.LayoutProblems(game));
            Directory.Delete(Path.Combine(modules, "bin"), recursive: true);

            // c) One folder too deep, also with an extra folder level of another name.
            Module(Path.Combine(modules, "Coop", "BannerlordCoop-v0.1.7"), "Coop", "Coop", "v0.1.7");
            Assert.Equal([CoopInstall.NestedLine], CoopInstall.LayoutProblems(game));
            Directory.Delete(Path.Combine(modules, "Coop"), recursive: true);

            // Coop wrapped in a folder with another name.
            Module(Path.Combine(modules, "BannerlordCoop-v0.1.7"), "Coop", "Coop", "v0.1.7");
            Assert.Equal([CoopInstall.WrappedLine("BannerlordCoop-v0.1.7")], CoopInstall.LayoutProblems(game));

            // CoopNightly is not this Coop.
            Directory.Delete(Path.Combine(modules, "BannerlordCoop-v0.1.7"), recursive: true);
            Module(modules, "Nightly", "CoopNightly", "v0.1.5");
            Assert.Empty(CoopInstall.LayoutProblems(game));

            // The folder renamed to Coop is swept on the next check: the path is built each time.
            var renamed = Marked(Path.Combine(modules, "Coop", "bin", "Win64_Shipping_Client", "Coop.dll"));
            Module(modules, "Coop", "Coop", "v0.1.7");
            Assert.Contains(CoopInstall.UnblockedLine(1)!, CoopInstall.CheckModulesCoop(game));
            Assert.False(HasMark(renamed));
        }
        finally
        {
            Delete(root);
        }

        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var table = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language);
            foreach (var key in new[] { "Coop is in Modules\\{0}. Rename that folder to Coop.", "Modules\\{0} is a second copy of Coop. Move it out of the Modules folder, so only Modules\\Coop is left.",
                         "Coop is in Modules\\{0}\\Coop. Move that Coop folder into Modules, so it becomes Modules\\Coop.", CoopInstall.LooseLine })
                Assert.True(table.ContainsKey(key), language + ": " + key);
        }
    }

    [Fact]
    public void Start_writes_the_password_and_the_steam_switch_and_they_read_back()
    {
        var root = NewRoot("server-config-start");
        try
        {
            var path = Write(Path.Combine(root, "server-config.json"), "{\n  \"password\": \"123\",\n  \"steam\": true,\n  \"port\": 4200\n}\n");

            // The host cleared the password and turned Steam off on a page that loaded the file.
            JsoncEditor.Write(path, ServerConfigFlags.ApplyAtStart(JsoncEditor.Read(path), "", steamBox: false, loadedFromFile: true));
            var back = JsoncEditor.Read(path);
            Assert.Equal("", JsoncEditor.GetString(back, "password"));
            Assert.Equal("false", JsoncEditor.GetRaw(back, "steam"));
            Assert.Equal("4200", JsoncEditor.GetRaw(back, "port"));

            // A password the host never set here (null) leaves the file's own.
            JsoncEditor.Write(path, JsoncEditor.SetString(back, "password", "abc"));
            JsoncEditor.Write(path, ServerConfigFlags.ApplyAtStart(JsoncEditor.Read(path), null, steamBox: true, loadedFromFile: true));
            Assert.Equal("abc", JsoncEditor.GetString(JsoncEditor.Read(path), "password"));
            Assert.Equal("true", JsoncEditor.GetRaw(JsoncEditor.Read(path), "steam"));
        }
        finally
        {
            Delete(root);
        }

        var start = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.Contains("ServerConfigFlags.ApplyAtStart(serverConfig, profile.Password, SteamBox.IsChecked == true, _serverFlagsLoaded)", start);
        Assert.DoesNotContain("profile.Password.Trim().Length > 0)\r\n            WriteServerPassword", start);
    }

    [Fact]
    public void A_client_add_on_never_installed_is_not_blamed_on_antivirus()
    {
        var root = NewRoot("addon-first-install");
        try
        {
            var payload = Path.Combine(root, "Payload");
            Write(Path.Combine(payload, "SubModule.xml"), "<Module/>");
            Write(Path.Combine(payload, "bin", "Win64_Shipping_Client", "CoodszSettingsSync.Coop.dll"), "dll");
            var game = Path.Combine(root, "Game");
            var watch = new AntivirusWatch(Path.Combine(root, "watch.json"));
            Assert.Null(ClientAddOns.Install(game, "DedicatedServer.Test", "Test module", payload, watch));

            // The whole module folder went (a restore without it): no antivirus line.
            Directory.Delete(Path.Combine(game, "Modules", "DedicatedServer.Test"), recursive: true);
            Assert.Null(ClientAddOns.Install(game, "DedicatedServer.Test", "Test module", payload, watch));

            // A DLL gone from a module that is still there is still named.
            File.Delete(Path.Combine(game, "Modules", "DedicatedServer.Test", "bin", "Win64_Shipping_Client", "CoodszSettingsSync.Coop.dll"));
            Assert.NotNull(ClientAddOns.Install(game, "DedicatedServer.Test", "Test module", payload, watch));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Coop_gets_no_server_skips_its_code_line()
    {
        const string coop = "<Module><Id value=\"Coop\"/><SubModules><SubModule><Tags><Tag key=\"DedicatedServerType\" value=\"none\"/></Tags></SubModule></SubModules></Module>";
        Assert.Null(ServerModuleNotes.SkippedCode("Bannerlord Coop", coop));
        Assert.NotNull(ServerModuleNotes.SkippedCode("Other", coop.Replace("\"Coop\"", "\"Other\"")));
    }

    [Fact]
    public void Active_coop_names_where_the_server_comes_from()
    {
        Assert.Equal("Active Coop: Bannerlord Co-op v0.1.7, from the server folder you chose.", CoopInstall.ActiveServerLine("Bannerlord Co-op v0.1.7", @"C:\CoopServer017"));
        Assert.Equal("Active Coop: Bannerlord Co-op v0.1.6, from the Steam Workshop server.",
            CoopInstall.ActiveServerLine("Bannerlord Co-op v0.1.6", @"C:\Steam\steamapps\workshop\content\261550\3770450698\DedicatedServer"));
        Assert.Equal("Active Coop: Bannerlord Co-op, from the game's Modules folder.",
            CoopInstall.ActiveServerLine("Bannerlord Co-op", @"C:\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Bannerlord Coop\DedicatedServer"));
    }

    // A host unzipped client Coop 0.1.7 into Modules while the tool still found the Workshop's 0.1.6 server.
    [Fact]
    public void A_newer_game_coop_than_the_server_coop_points_to_set_server_folder()
    {
        Assert.Equal("The server runs Coop v0.1.6, older than Coop v0.1.7 in the game's Modules folder. Click Download Coop server for the matching Coop dedicated server download, linked at the bottom of Coop's release notes, not under Assets. Unzip it to a short folder, then click Set server folder.",
            CoopInstall.ServerBehindLine("v0.1.7", "v0.1.6"));
        Assert.NotNull(CoopInstall.ServerBehindLine("0.1.7.1", "v0.1.7"));
        Assert.Null(CoopInstall.ServerBehindLine("v0.1.7", "v0.1.7"));
        Assert.Null(CoopInstall.ServerBehindLine("v0.1.7", "0.1.7.0"));
        Assert.Null(CoopInstall.ServerBehindLine("v0.1.6", "v0.1.7"));
        // No Coop in Modules, or a version that can't be read, says nothing.
        Assert.Null(CoopInstall.ServerBehindLine(null, "v0.1.6"));
        Assert.Null(CoopInstall.ServerBehindLine("v0.1.7", ""));
        Assert.Null(CoopInstall.ServerBehindLine("garbled", "v0.1.6"));
    }

    [Fact]
    public void The_server_behind_line_sits_with_active_coop_and_opens_set_server_folder()
    {
        var xaml = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml"));
        var line = xaml[xaml.IndexOf("x:Name=\"CoopBehindLine\"", StringComparison.Ordinal)..];
        line = line[..line.IndexOf("</DockPanel>", StringComparison.Ordinal)];
        Assert.Contains("Content=\"Set server folder\" Click=\"SetServerFolder_Click\"", line);
        Assert.Contains("x:Name=\"CoopBehindText\"", line);
        Assert.True(xaml.IndexOf("x:Name=\"ActiveCoop\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"CoopBehindLine\"", StringComparison.Ordinal));
    }

    // After Steam updated Coop, a host kept the older ModDB server he had picked and could not see how to go back (Vanquish).
    [Fact]
    public void A_picked_server_older_than_the_steam_server_offers_use_the_steam_server()
    {
        const string picked = @"C:\CoopServer016";
        Assert.Equal("Your picked server folder has Coop v0.1.6. Steam has Coop v0.1.7.",
            CoopInstall.ChosenServerOlderLine(picked, "v0.1.6", "v0.1.7"));
        Assert.NotNull(CoopInstall.ChosenServerOlderLine(picked, "0.1.7", "v0.1.7.1"));
        // Same or newer, no Workshop server, or a version that can't be read says nothing.
        Assert.Null(CoopInstall.ChosenServerOlderLine(picked, "v0.1.7", "v0.1.7"));
        Assert.Null(CoopInstall.ChosenServerOlderLine(picked, "v0.1.7", "0.1.7.0"));
        Assert.Null(CoopInstall.ChosenServerOlderLine(picked, "v0.1.8", "v0.1.7"));
        Assert.Null(CoopInstall.ChosenServerOlderLine(picked, "v0.1.6", null));
        Assert.Null(CoopInstall.ChosenServerOlderLine(picked, "", "v0.1.7"));
        // No picked folder: the Workshop server itself, the game's Modules\Bannerlord Coop server, or none at all.
        Assert.Null(CoopInstall.ChosenServerOlderLine(@"C:\Steam\steamapps\workshop\content\261550\3770450698\DedicatedServer", "v0.1.6", "v0.1.7"));
        Assert.Null(CoopInstall.ChosenServerOlderLine(@"C:\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Bannerlord Coop\DedicatedServer", "v0.1.6", "v0.1.7"));
        Assert.Null(CoopInstall.ChosenServerOlderLine("", "v0.1.6", "v0.1.7"));
        Assert.Null(CoopInstall.ChosenServerOlderLine(null, "v0.1.6", "v0.1.7"));
        // The Workshop server in another Steam library, and paths with forward slashes.
        Assert.Null(CoopInstall.ChosenServerOlderLine(@"D:\SteamLibrary\steamapps\workshop\content\261550\3770450698\DedicatedServer", "v0.1.6", "v0.1.7"));
        Assert.Null(CoopInstall.ChosenServerOlderLine("D:/SteamLibrary/steamapps/workshop/content/261550/3770450698/DedicatedServer", "v0.1.6", "v0.1.7"));
        Assert.Null(CoopInstall.ChosenServerOlderLine("C:/Steam/steamapps/common/Mount & Blade II Bannerlord/Modules/Bannerlord Coop/DedicatedServer", "v0.1.6", "v0.1.7"));
        Assert.NotNull(CoopInstall.ChosenServerOlderLine("C:/CoopServer016", "v0.1.6", "v0.1.7"));
        // Another Workshop item is a picked folder too.
        Assert.NotNull(CoopInstall.ChosenServerOlderLine(@"D:\SteamLibrary\steamapps\workshop\content\261550\1234567890\DedicatedServer", "v0.1.6", "v0.1.7"));
    }

    // AD1259 pins Coop v0.1.7 and accepts v0.1.6 (AD1259Stack), so a newer Steam Coop never offers to move its host off the pin.
    [Fact]
    public void The_ad1259_tool_offers_the_steam_server_only_up_to_its_pinned_coop()
    {
        const string picked = @"C:\CoopServer016";
        Assert.NotNull(CoopInstall.ChosenServerOlderLine(picked, "v0.1.6", "v0.1.7", ad1259: true));
        Assert.NotNull(CoopInstall.ChosenServerOlderLine(picked, "v0.1.6", "0.1.7.0", ad1259: true));
        Assert.Null(CoopInstall.ChosenServerOlderLine(picked, "v0.1.7", "v0.1.8", ad1259: true));
        Assert.Null(CoopInstall.ChosenServerOlderLine(picked, "v0.1.6", "v0.1.7.1", ad1259: true));
        // The Coop tool has no pin.
        Assert.NotNull(CoopInstall.ChosenServerOlderLine(picked, "v0.1.7", "v0.1.8"));
    }

    // The click reads the Workshop Coop again before it saves (Codex final review): Steam may have moved past AD1259's pin since
    // the line was shown.
    [Fact]
    public void Use_the_steam_server_checks_the_fresh_workshop_coop_against_the_ad1259_pin()
    {
        Assert.Null(CoopInstall.UseSteamServerProblem("v0.1.7", ad1259: true));
        Assert.Null(CoopInstall.UseSteamServerProblem("0.1.7.0", ad1259: true));
        Assert.Null(CoopInstall.UseSteamServerProblem("v0.1.6", ad1259: true));
        Assert.Equal("The Steam Workshop server now has Coop v0.1.8, newer than the Coop v0.1.7 AD1259 needs, so the server folder stays as it is.",
            CoopInstall.UseSteamServerProblem("v0.1.8", ad1259: true));
        Assert.StartsWith("The Coop version of the Steam Workshop server could not be read", CoopInstall.UseSteamServerProblem(null, ad1259: true));
        Assert.StartsWith("The Coop version of the Steam Workshop server could not be read", CoopInstall.UseSteamServerProblem(" ", ad1259: true));
        // The Coop tool has no pin, so any version (or none) is fine there.
        Assert.Null(CoopInstall.UseSteamServerProblem("v0.1.8", ad1259: false));
        Assert.Null(CoopInstall.UseSteamServerProblem(null, ad1259: false));

        // The click reads the version again after its Steam updating check and before the save.
        var click = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost").Replace("\r", ""), "private void UseSteamServer_Click(");
        var updating = click.IndexOf("AntivirusFiles.SteamUpdating(", StringComparison.Ordinal);
        var fresh = click.IndexOf("_steamServerCoop = SteamServerCoop();", StringComparison.Ordinal);
        var pin = click.IndexOf("CoopInstall.UseSteamServerProblem(_steamServerCoop, Edition.IsAD1259)", StringComparison.Ordinal);
        var save = click.IndexOf("HostSettings.Save(SettingsPath, _settings);", StringComparison.Ordinal);
        Assert.True(updating >= 0 && updating < fresh && fresh < pin && pin < save);
    }

    [Fact]
    public void Use_the_steam_server_sets_the_workshop_server_and_refreshes_only_when_stopped()
    {
        var xaml = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml"));
        var line = xaml[xaml.IndexOf("x:Name=\"SteamServerLine\"", StringComparison.Ordinal)..];
        line = line[..line.IndexOf("</DockPanel>", StringComparison.Ordinal)];
        Assert.Contains("Content=\"" + CoopInstall.UseSteamServerButton + "\" Click=\"UseSteamServer_Click\"", line);
        Assert.Contains("x:Name=\"SteamServerText\"", line);
        Assert.True(xaml.IndexOf("x:Name=\"ActiveCoop\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"SteamServerLine\"", StringComparison.Ordinal));

        var host = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        var click = SourceFiles.Method(host, "private void UseSteamServer_Click(");
        // The running guard comes before the setting changes, and nothing on disk is moved or deleted.
        var guard = click.IndexOf("if (ModListRunning || _copyingMods)", StringComparison.Ordinal);
        var set = click.IndexOf("_settings.ServerExecutable = steam;", StringComparison.Ordinal);
        Assert.True(guard >= 0 && set > guard);
        Assert.Contains("SetControl(UseSteamServerRunning, failed: true);", click);
        Assert.Contains("private const string UseSteamServerRunning = \"Stop the server first, then click Use the Steam server.\";", host);
        // The button is off while the server runs, with a tip that says why, since the status line is on the Dashboard.
        var block = SourceFiles.Method(host, "private void ApplyModListBlock(");
        Assert.Contains("UseSteamServerButton.IsEnabled = !running;", block);
        Assert.Contains("ShowBlockedTip(UseSteamServerButton, running ? UseSteamServerRunning : null);", block);
        Assert.Contains("x:Name=\"UseSteamServerButton\"", line);
        // While Steam updates the Workshop Coop the setting stays as it is.
        var pending = click.IndexOf("AntivirusFiles.SteamUpdating(", StringComparison.Ordinal);
        Assert.True(pending > guard && pending < set);
        Assert.True(click.IndexOf("HostSettings.Save(SettingsPath, _settings);", StringComparison.Ordinal) > set);
        Assert.Contains("_actions.Note(CoopInstall.UseSteamServerButton", click);
        Assert.True(click.IndexOf("ReloadModules();", StringComparison.Ordinal) > set);
        // The fresh AD1259 pin refusal refreshes too (the line may go), so the last refresh is the one after the setting.
        Assert.True(click.LastIndexOf("RefreshState();", StringComparison.Ordinal) > set);
        Assert.DoesNotContain("Delete", click);
        Assert.DoesNotContain("Move", click);
        // The line reads the Workshop server's Coop on every module reload and shows from RefreshState.
        Assert.Contains("_steamServerCoop = SteamServerCoop();", SourceFiles.Method(host, "private void ReloadModules("));
        Assert.Contains("var steamNewer = SteamServerState().Line;", SourceFiles.Method(host, "private void RefreshState("));
        Assert.Contains("CoopInstall.ChosenServerOlderLine(ServerRoot, coop?.Version, _steamServerCoop, Edition.IsAD1259, nightly)", SourceFiles.Method(host, "private (bool Nightly, string? Line) SteamServerState("));
    }

    [Fact]
    public void The_movement_limit_boxes_are_gone_and_their_keys_are_never_written()
    {
        var xaml = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml"));
        Assert.DoesNotContain("OutgoingBox", xaml);
        Assert.DoesNotContain("IncomingBox", xaml);
        var host = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.DoesNotContain("movementOutgoingMiBPerSecond", host);
        Assert.DoesNotContain("movementIncomingMiBPerSecond", host);
    }
}
