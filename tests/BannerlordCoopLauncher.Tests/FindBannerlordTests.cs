using System.Text.Json;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Steam;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

public class FindBannerlordTests
{
    [Theory]
    [InlineData("TaomCoopLauncher")]
    [InlineData("AD1259Launcher")]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresLauncher")]
    [InlineData("BurningEmpiresHost")]
    public void Startup_and_picker_share_the_game_found_method(string project)
    {
        var source = SourceFiles.Window(SourceFiles.Root(), project);
        var initializer = SourceFiles.Method(source, "private void ApplyGameFound(");
        var picker = SourceFiles.Method(source, "private void FindBannerlord_Click(");

        Assert.Contains("ApplyGameFound(", source);
        Assert.Contains("GameFolderPick.Use(dialog.FolderName", picker);
        Assert.Contains("ApplyGameFound(root)", picker);
        Assert.Contains("GameFolderPick.DialogStartFolder(", picker);
        Assert.True(initializer.Contains("FindBannerlordButton.Visibility = Visibility.Collapsed") || initializer.Contains("ShowGameFolder();"));
    }

    [Theory]
    [InlineData("BannerlordCoopLauncher", "if (_busy || _joinChecking)")]
    [InlineData("TaomCoopLauncher", "if (_busy || _joinChecking)")]
    [InlineData("AD1259Launcher", "if (_gettingMods || _joinChecking)")]
    [InlineData("BurningEmpiresLauncher", "if (_busy || _joinChecking)")]
    public void Client_picker_waits_while_the_launcher_is_busy(string project, string guard)
    {
        var source = SourceFiles.Window(SourceFiles.Root(), project);
        var picker = SourceFiles.Method(source, "void FindBannerlord_Click(");

        // A pick in the middle of an install, a download or a join check would swap the game folder under it.
        var wait = picker.IndexOf(guard, StringComparison.Ordinal);
        Assert.True(wait >= 0, project + " picker should start with " + guard);
        Assert.True(wait < picker.IndexOf("new OpenFolderDialog", StringComparison.Ordinal), "the busy check should come before the folder dialog");
        Assert.Contains("Log(\"Wait for the current action to finish.\");", picker);
    }

    [Fact]
    public void Coop_client_busy_flag_belongs_to_the_running_action()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher");
        var run = SourceFiles.Method(source, "private async Task RunBusyAsync(");
        // Set before the action and cleared only when the newest action ends.
        Assert.True(run.IndexOf("_busy = true;", StringComparison.Ordinal) is >= 0 and var on
            && on < run.IndexOf("await action(", StringComparison.Ordinal));
        Assert.True(run.IndexOf("_busy = false;", StringComparison.Ordinal) > run.IndexOf("ReferenceEquals(_work, current)", StringComparison.Ordinal));
        // Anything else that refreshes the buttons (a mod toggle during an install) can't clear it, so Find Bannerlord still waits.
        Assert.DoesNotContain("_busy = ", SourceFiles.Method(source, "private void SetBusy(bool busy)"));
        Assert.Equal(2, source.Split("_busy = ").Length - 1);
        Assert.DoesNotContain("SetBusy(false);", SourceFiles.Method(source, "private void ModuleToggle_Click("));
        Assert.Contains("SetBusy(_busy);", SourceFiles.Method(source, "private void ModuleToggle_Click("));
    }

    [Fact]
    public void Taom_client_startup_runs_the_game_independent_setup_with_no_game()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher");
        var startup = SourceFiles.Method(source, "public MainWindow()");
        var found = SourceFiles.Method(source, "private void ApplyGameFound(");
        var setups = SourceFiles.Method(source, "private void InitSetups(");

        // The not found branch must not skip the setups, the mod list and the crash guards.
        Assert.DoesNotContain("ApplyGameFound(", startup);
        var search = startup.IndexOf("GameFolderPick.ResolveSavedPath(", StringComparison.Ordinal);
        foreach (var call in new[] { "InitSetups();", "Closing += MainWindow_Closing;", "RefreshModules();", "ApplyCrashGuards();" })
            Assert.True(startup.IndexOf(call, StringComparison.Ordinal) > search, call + " should run at every startup, after the game search");
        Assert.DoesNotContain("InitSetups();", found);
        Assert.DoesNotContain("Closing +=", setups);
    }

    [Fact]
    public void BE_client_startup_runs_the_game_independent_setup_and_a_late_pick_reaches_connect()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresLauncher");
        var startup = SourceFiles.Method(source, "public MainWindow()");
        var found = SourceFiles.Method(source, "private void ApplyGameFound(");

        Assert.DoesNotContain("ApplyGameFound(", startup);
        var search = startup.IndexOf("GameFolderPick.ResolveSavedPath(", StringComparison.Ordinal);
        Assert.True(search >= 0);
        foreach (var call in new[] { "InitSetups();", "RefreshModules();", "UpdateConnectButton();", "ApplyCrashGuards();" })
            Assert.True(startup.IndexOf(call, StringComparison.Ordinal) > search, call + " should run at every startup, after the game search");
        // Connect does not search again, so a late pick runs the game dependent startup work itself.
        foreach (var call in new[] { "RestorePrefabsFix();", "RefreshModules();", "UpdateConnectButton();", "ApplyCrashGuards();" })
            Assert.Contains(call, found);
        // InitSetups hooks Closing, so it runs once.
        Assert.DoesNotContain("InitSetups();", found);
        Assert.DoesNotContain("BannerlordFinder.FindGame()", source);
    }

    [Fact]
    public void BE_host_uses_the_saved_pick_everywhere_and_a_late_pick_loads_again()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost");
        var finder = SourceFiles.Method(source, "private string? FindGameRoot(");
        var picker = SourceFiles.Method(source, "private void FindBannerlord_Click(");
        var found = SourceFiles.Method(source, "private void ApplyGameFound(");
        var startup = SourceFiles.Method(source, "public MainWindow()");

        // Prepare server, Install mods and Install or repair Burning Empires all go through the saved pick.
        Assert.DoesNotContain("BannerlordFinder.FindGame()", source);
        Assert.True(finder.IndexOf("GameFolderPick.CheckSaved(_settings.GamePath)", StringComparison.Ordinal) >= 0
            && finder.IndexOf("CheckSaved", StringComparison.Ordinal) < finder.IndexOf("FindGameWithReason()", StringComparison.Ordinal));
        Assert.Contains("if (_busy || _startingServer)", picker);
        var apply = picker.IndexOf("ApplyGameFound(root);", StringComparison.Ordinal);
        var save = picker.IndexOf("SaveSettings();", StringComparison.Ordinal);
        Assert.True(apply >= 0 && save > apply, "the default server folder is made before it is saved");
        Assert.True(picker.IndexOf("ShowModSetups();", StringComparison.Ordinal) > save, "the mod lists load again with the picked Modules folder");
        Assert.Contains("_settings.GamePath = root;", picker);
        Assert.DoesNotContain("_settings.GamePath", found);
        // A host opened without the game cached a mod catalogue with no Modules folder; a pick drops it,
        // so the setup list and the Start server plan see the game's mods.
        Assert.True(found.IndexOf("_hostMods = null;", StringComparison.Ordinal) is >= 0 and var drop
            && drop < found.IndexOf("_gameRoot = root;", StringComparison.Ordinal), "a new game folder scans the mods again");
        Assert.Contains("if (_hostMods != null && !rescan)", SourceFiles.Method(source, "private IReadOnlyList<FoundMod> HostMods("));
        Assert.DoesNotContain("_settings.GamePath", SourceFiles.Method(source, "private void SaveSettings("));
        Assert.True(startup.IndexOf("NoteGameSearchFailure();", StringComparison.Ordinal) > startup.IndexOf("SeedLogs();", StringComparison.Ordinal),
            "the not found reason is logged after SeedLogs, which replaces the log");
        Assert.Contains("public string GamePath { get; set; }", source.Replace("\r", "", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("BurningEmpiresLauncher")]
    [InlineData("BurningEmpiresHost")]
    public void BE_find_button_starts_hidden_and_reuses_the_shared_text(string project)
    {
        var xaml = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
        var start = xaml.IndexOf("x:Name=\"FindBannerlordButton\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var button = xaml[start..xaml.IndexOf("/>", start, StringComparison.Ordinal)];

        Assert.Contains("Content=\"Find Bannerlord\"", button);
        Assert.Contains("ToolTip=\"Pick the Mount &amp; Blade II Bannerlord folder. The tool remembers it.\"", button);
        Assert.Contains("Visibility=\"Collapsed\"", button);
        Assert.Contains("GameFolderPick.NotFoundText", SourceFiles.Window(SourceFiles.Root(), project));
    }

    [Fact]
    public void AD1259_client_startup_runs_the_game_independent_work_with_no_game()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "AD1259Launcher");
        var startup = SourceFiles.Method(source, "public MainWindow()");

        Assert.DoesNotContain("ApplyGameFound(", startup);
        var search = startup.IndexOf("GameFolderPick.ResolveSavedPath(", StringComparison.Ordinal);
        var restore = startup.IndexOf("RestoreUIExtenderExFix();", StringComparison.Ordinal);
        var modules = startup.IndexOf("RefreshModules();", StringComparison.Ordinal);
        var set = startup.IndexOf("Log(\"AD1259 set: \"", StringComparison.Ordinal);
        var problems = startup.IndexOf("LogModProblems();", StringComparison.Ordinal);
        var unblock = startup.IndexOf("ShowUnblockFirst();", StringComparison.Ordinal);
        Assert.True(search >= 0 && restore > search && modules > restore && set > modules && problems > set && unblock > problems,
            "game search, restore, mod list, AD1259 set line, mod problems, unblock, in the old order");
    }

    [Fact]
    public void Coop_client_runs_startup_work_with_no_game_and_does_not_save_a_steam_root()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher");
        var startup = SourceFiles.Method(source, "private async Task InitializeAsync()");
        var found = SourceFiles.Method(source, "private async Task ApplyGameFoundAsync(");
        var save = SourceFiles.Method(source, "private void SaveSettingsFromForm(");

        var detect = startup.IndexOf("await DetectGamePath(", StringComparison.Ordinal);
        Assert.True(detect >= 0 && startup.IndexOf("RememberSavedMods();", StringComparison.Ordinal) > detect);
        Assert.True(startup.IndexOf("RestoreUIExtenderExFix();", StringComparison.Ordinal) > detect);
        Assert.DoesNotContain("RememberSavedMods", found);
        Assert.DoesNotContain("_settings.GamePath", found);
        Assert.Contains("_settings.GamePath = _gamePicked ?", save);
        Assert.Contains("\"Found in Steam. You do not pick a folder.\"", source);
    }

    [Fact]
    public void Taom_host_late_pick_saves_the_default_folders_then_loads_again()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        var picker = SourceFiles.Method(source, "private void FindBannerlord_Click(");
        var found = SourceFiles.Method(source, "private void ApplyGameFound(");
        var startup = SourceFiles.Method(source, "public MainWindow()");

        var apply = picker.IndexOf("ApplyGameFound(root);", StringComparison.Ordinal);
        var save = picker.IndexOf("SaveSettings();", StringComparison.Ordinal);
        Assert.True(apply >= 0 && save > apply, "the default server folders are made before they are saved");
        foreach (var loader in new[] { "ShowModSetups();", "LoadConfigIntoFields();", "ReloadSaves();", "LoadGameplaySettings();" })
            Assert.True(picker.IndexOf(loader, StringComparison.Ordinal) > save, loader + " should run again after the save");
        Assert.DoesNotContain("_settings.GamePath", found);
        Assert.DoesNotContain("_settings.GamePath", SourceFiles.Method(source, "private void SaveSettings("));
        Assert.True(startup.IndexOf("NoteGameSearchFailure();", StringComparison.Ordinal) > startup.IndexOf("SeedLogs();", StringComparison.Ordinal),
            "the not found reason is logged after SeedLogs, which replaces the log");
    }

    [Fact]
    public void Coop_client_startup_and_picker_share_the_game_found_method()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher");
        var detect = SourceFiles.Method(source, "private async Task DetectGamePath(");
        var picker = SourceFiles.Method(source, "private async void FindBannerlord_Click(");

        Assert.Contains("ApplyGameFoundAsync(", detect);
        Assert.Contains("ApplyGameFoundAsync(", picker);
    }

    [Fact]
    public void Shared_host_picture_finder_prefers_the_saved_path()
    {
        var source = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "PictureBuilder.cs"));
        var finder = SourceFiles.Method(source, "public static string? FindGame()");

        Assert.Contains("FindGame(SavedGamePath?.Invoke(), SearchGame)", finder);
        Assert.Contains("GameFolderPick.ResolveSavedPath", source);
    }

    [Fact]
    public void Shared_host_startup_and_picker_use_the_same_game_initializer()
    {
        var source = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Keep", "CheatsPage.xaml.cs"));
        var startup = SourceFiles.Method(source, "public void UpdateGameFolderButton()");
        var picker = SourceFiles.Method(source, "private void FindBannerlord_Click(");

        Assert.Contains("ApplyGameFound(game)", startup);
        Assert.Contains("ApplyGameFound(root)", picker);
        Assert.DoesNotContain("MessageBox.Show", picker);
    }

    [Fact]
    public void Shared_host_find_button_is_not_inside_the_picture_panel()
    {
        // The AD1259 host collapses PicturePanel, so a button in it could never show there.
        var xaml = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Keep", "CheatsPage.xaml"));
        var panel = xaml.IndexOf("x:Name=\"PicturePanel\"", StringComparison.Ordinal);
        var panelEnd = xaml.IndexOf("</StackPanel>", xaml.IndexOf("x:Name=\"BuildPicturesButton\"", StringComparison.Ordinal), StringComparison.Ordinal);
        panelEnd = xaml.IndexOf("</StackPanel>", panelEnd + 1, StringComparison.Ordinal);
        var button = xaml.IndexOf("x:Name=\"FindBannerlordButton\"", StringComparison.Ordinal);

        Assert.True(panel >= 0 && button >= 0);
        Assert.False(button > panel && button < panelEnd, "Find Bannerlord sits inside PicturePanel");
    }

    [Fact]
    public void Shared_host_module_scans_use_the_saved_game_folder_first()
    {
        var source = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ExtraModScan.cs"));
        var modules = SourceFiles.Method(source, "internal static string? GameModulesDirectory()");

        Assert.True(modules.IndexOf("PictureBuilder.SavedGamePath", StringComparison.Ordinal) < modules.IndexOf("LibraryPaths()", StringComparison.Ordinal));
    }

    [Fact]
    public void Picture_builder_uses_a_valid_saved_path_before_search()
    {
        var files = new MemoryFileSystem();
        const string root = @"C:\Games\Bannerlord";
        GameFolderPickTests.AddGame(files, root);
        var searched = false;

        var selected = PictureBuilder.FindGame(root, () => { searched = true; return null; }, files);

        Assert.Equal(Path.GetFullPath(root), selected);
        Assert.False(searched);
    }

    [Fact]
    public void Picture_builder_ignores_an_invalid_saved_path_and_searches()
    {
        var searched = false;

        var selected = PictureBuilder.FindGame(@"C:\OldGame", () => { searched = true; return @"D:\Steam\Bannerlord"; }, new MemoryFileSystem());

        Assert.Equal(@"D:\Steam\Bannerlord", selected);
        Assert.True(searched);
    }

    [Fact]
    public void Picker_strings_exist_in_every_language()
    {
        var root = Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages");
        var keys = new[]
        {
            "Find Bannerlord",
            "Pick the Mount & Blade II Bannerlord folder. The tool remembers it.",
            "Pick the Mount & Blade II Bannerlord folder",
            "Found. The tool remembers this folder.",
            BannerlordCoopLauncher.Core.Updates.WhatsNew.FindBannerlordPick
        };

        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            using var json = JsonDocument.Parse(SourceFiles.Read(Path.Combine(root, language + ".json")));
            foreach (var key in keys)
                Assert.True(json.RootElement.TryGetProperty(key, out _), language + " is missing " + key);
        }
    }
}
