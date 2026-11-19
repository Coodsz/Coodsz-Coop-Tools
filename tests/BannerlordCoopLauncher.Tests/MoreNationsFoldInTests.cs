using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// Coodsz and Friends became the More Nations switch of the Coop client launcher. Its players' settings
// fold into the client's own folder with the switch on, and a player who had both tools keeps both.
public sealed class MoreNationsFoldInTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-foldin-" + Guid.NewGuid().ToString("N"));
    private readonly string _client;
    private readonly string _friends;
    private readonly string _coop;

    public MoreNationsFoldInTests()
    {
        _client = Path.Combine(_root, "AppData", "Coop");
        _friends = Path.Combine(_root, "Coodsz and Friends");
        _coop = Path.Combine(_root, "Coodsz Coop and Friends");
        Directory.CreateDirectory(_friends);
        Directory.CreateDirectory(_coop);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private static LauncherSettings Settings(string active, params ClientSetup[] setups)
    {
        var settings = new LauncherSettings { ActiveSetup = active, GamePath = "" };
        settings.Setups.AddRange(setups);
        return settings;
    }

    private static ClientSetup Setup(string name, string map, string host = "", params string[] off) =>
        new() { Name = name, CampaignMap = map, SteamHostName = host, DisabledModules = off.ToList() };

    private LauncherSettings Client() => LauncherSettings.Load(Path.Combine(_client, "launcher.json"));

    private static ClientSetup Named(LauncherSettings settings, string name) =>
        settings.Setups.Single(setup => setup.Name == name);

    private string Log() => File.ReadAllText(Path.Combine(_client, ToolDataFolder.LogName));

    [Fact]
    public void A_Coodsz_and_Friends_player_gets_their_setups_and_the_switch_on()
    {
        var friends = Settings("My MN", Setup("Default", ""), Setup("My MN", "MoreNations", "Host", "RBM"));
        friends.GamePath = @"D:\Bannerlord";
        friends.Save(Path.Combine(_friends, "launcher.json"));
        File.WriteAllText(Path.Combine(_friends, "profile-cache.json"), "cached");

        MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);

        var client = Client();
        Assert.True(client.MoreNations);
        Assert.Equal("My MN", client.ActiveSetup);
        Assert.Equal(@"D:\Bannerlord", client.GamePath);
        Assert.Equal(new[] { "RBM" }, Named(client, "My MN").DisabledModules);
        Assert.Equal("Host", Named(client, "My MN").SteamHostName);
        // Its Default showed More Nations there, so it keeps that map whatever the switch says later.
        Assert.Equal("MoreNations", Named(client, "Default").CampaignMap);
        Assert.Equal("cached", File.ReadAllText(Path.Combine(_client, "profile-cache.json")));
        // The old file stays where it was, for an older version.
        Assert.True(File.Exists(Path.Combine(_friends, "launcher.json")));
        Assert.Contains("Turned the More Nations switch on", Log());
    }

    [Fact]
    public void A_Coop_player_keeps_the_switch_off()
    {
        Settings("Default", Setup("Default", "")).Save(Path.Combine(_coop, "launcher.json"));

        MoreNationsFoldIn.Run(_client, _coop, fromFriends: false, moreNationsFolder: null);

        var client = Client();
        Assert.False(client.MoreNations);
        Assert.Equal("Vanilla", Named(client, "Default").CampaignMap);
    }

    [Fact]
    public void A_player_with_both_tools_keeps_the_Coop_setups_and_gets_the_More_Nations_ones_beside_them()
    {
        // The Coop client launcher's folder came in first.
        Settings("Coop raid", Setup("Default", ""), Setup("Coop raid", "Vanilla", "Raider"), Setup("Shared", "Vanilla", "Same"))
            .Save(Path.Combine(_coop, "launcher.json"));
        MoreNationsFoldIn.Run(_client, _coop, fromFriends: false, moreNationsFolder: null);
        Settings("Default", Setup("Default", ""), Setup("Nations night", "MoreNations", "Friend"), Setup("Shared", "Vanilla", "Same"))
            .Save(Path.Combine(_friends, "launcher.json"));

        MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);

        var client = Client();
        Assert.True(client.MoreNations);
        Assert.Equal("Coop raid", client.ActiveSetup);
        Assert.Equal("Raider", Named(client, "Coop raid").SteamHostName);
        // The Coop Default meant the vanilla map, so it stays on it with the switch on.
        Assert.Equal("Vanilla", Named(client, "Default").CampaignMap);
        Assert.Equal("MoreNations", Named(client, "Default (More Nations)").CampaignMap);
        Assert.Equal("Friend", Named(client, "Nations night").SteamHostName);
        // The same setup in both comes in once.
        Assert.Single(client.Setups, setup => setup.Name.StartsWith("Shared", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(_client, "launcher.more-nations.json")));
        Assert.Contains("Added 2 setup(s)", Log());
    }

    [Fact]
    public void Coodsz_and_Friends_first_then_the_Coop_folder_keeps_both_and_the_switch_on()
    {
        Settings("Nations", Setup("Default", ""), Setup("Nations", "MoreNations")).Save(Path.Combine(_friends, "launcher.json"));
        MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);
        // ToolData's own first copy ran from the Friends folder, and must not hide the Coop folder.
        new ToolDataFolder(Path.GetDirectoryName(_client)!, "Coop").CopyOldFiles(_friends, ToolData.CoopClient.Key, ToolData.CoopClient.OldFiles);
        Settings("Default", Setup("Default", "", "CoopHost")).Save(Path.Combine(_coop, "launcher.json"));

        MoreNationsFoldIn.Run(_client, _coop, fromFriends: false, moreNationsFolder: null);

        var client = Client();
        Assert.True(client.MoreNations);
        Assert.Equal("MoreNations", Named(client, "Default").CampaignMap);
        Assert.Equal("Vanilla", Named(client, "Default (Coop)").CampaignMap);
        Assert.Equal("CoopHost", Named(client, "Default (Coop)").SteamHostName);
        Assert.True(File.Exists(Path.Combine(_client, "launcher.coop.json")));
    }

    [Fact]
    public void Both_tools_sharing_one_folder_and_one_file_fold_in_once()
    {
        Settings("Default", Setup("Default", "MoreNations"), Setup("Mine", "Vanilla")).Save(Path.Combine(_friends, "launcher.json"));

        MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);
        MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);
        MoreNationsFoldIn.Run(_client, _friends, fromFriends: false, moreNationsFolder: null);

        var client = Client();
        Assert.True(client.MoreNations);
        Assert.Single(client.Setups, setup => setup.Name.StartsWith("Mine", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_client, "launcher.more-nations.json")));
        Assert.Single(File.ReadAllLines(Path.Combine(_client, MoreNationsFoldIn.DoneName)));
    }

    [Fact]
    public void A_folder_ToolData_already_copied_from_is_not_folded_in_again()
    {
        Settings("Default", Setup("Mine", "Vanilla")).Save(Path.Combine(_coop, "launcher.json"));
        new ToolDataFolder(Path.GetDirectoryName(_client)!, "Coop").CopyOldFiles(_coop, ToolData.CoopClient.Key, ToolData.CoopClient.OldFiles);
        var client = Client();
        Named(client, "Mine").SteamHostName = "Changed since";
        client.Save(Path.Combine(_client, "launcher.json"));

        MoreNationsFoldIn.Run(_client, _coop, fromFriends: false, moreNationsFolder: null);

        Assert.DoesNotContain(Client().Setups, setup => setup.Name == "Mine (Coop)");
    }

    [Fact]
    public void The_switch_turns_on_for_a_Coodsz_and_Friends_folder_that_never_saved_settings()
    {
        Settings("Coop", Setup("Coop", "")).Save(Path.Combine(_coop, "launcher.json"));
        MoreNationsFoldIn.Run(_client, _coop, fromFriends: false, moreNationsFolder: null);

        MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);

        var client = Client();
        Assert.True(client.MoreNations);
        // Blank meant vanilla in the Coop tool, so the switch does not move it.
        Assert.Equal("Vanilla", Named(client, "Coop").CampaignMap);
    }

    [Fact]
    public void A_fresh_install_from_a_Coodsz_and_Friends_folder_starts_with_the_switch_on()
    {
        MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);

        var client = Client();
        Assert.True(client.MoreNations);
        // Its Default has no map yet, so it starts on More Nations like Coodsz and Friends did.
        Assert.Equal("", client.Current().CampaignMap);
        Assert.Equal("MoreNations", client.DefaultMap());
    }

    [Fact]
    public void An_unreadable_More_Nations_file_is_kept_aside_and_the_Coop_settings_stay()
    {
        Settings("Coop", Setup("Coop", "Vanilla", "Host")).Save(Path.Combine(_coop, "launcher.json"));
        MoreNationsFoldIn.Run(_client, _coop, fromFriends: false, moreNationsFolder: null);
        File.WriteAllText(Path.Combine(_friends, "launcher.json"), "{ not json");

        MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);

        var client = Client();
        Assert.True(client.MoreNations);
        Assert.Equal("Host", Named(client, "Coop").SteamHostName);
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(_client, "launcher.more-nations.json")));
        Assert.Contains("Could not read the More Nations launcher.json", Log());
    }

    [Fact]
    public void An_unreadable_client_file_is_kept_aside_and_the_More_Nations_settings_come_in()
    {
        Directory.CreateDirectory(_client);
        File.WriteAllText(Path.Combine(_client, "launcher.json"), "");
        Settings("Nations", Setup("Nations", "MoreNations")).Save(Path.Combine(_friends, "launcher.json"));

        MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);

        Assert.Equal("Nations", Client().ActiveSetup);
        Assert.True(File.Exists(Path.Combine(_client, "launcher.unreadable.json")));
    }

    [Fact]
    public void A_missing_or_empty_source_changes_nothing_for_a_Coop_folder()
    {
        MoreNationsFoldIn.Run(_client, _coop, fromFriends: false, moreNationsFolder: Path.Combine(_root, "AppData", "MoreNations"));

        Assert.False(File.Exists(Path.Combine(_client, "launcher.json")));
        Assert.False(File.Exists(Path.Combine(_client, MoreNationsFoldIn.DoneName)));
    }

    [Fact]
    public void The_old_More_Nations_data_folder_folds_in_too_but_only_with_a_settings_file()
    {
        var data = Path.Combine(_root, "AppData", "MoreNations");
        Directory.CreateDirectory(Path.Combine(data, "payload"));
        Settings("Coop", Setup("Coop", "Vanilla")).Save(Path.Combine(_coop, "launcher.json"));

        MoreNationsFoldIn.Run(_client, _coop, fromFriends: false, moreNationsFolder: data);
        Assert.False(Client().MoreNations);

        Settings("Data MN", Setup("Data MN", "MoreNations")).Save(Path.Combine(data, "launcher.json"));
        MoreNationsFoldIn.Run(_client, _coop, fromFriends: false, moreNationsFolder: data);

        var client = Client();
        Assert.True(client.MoreNations);
        Assert.Equal("MoreNations", Named(client, "Data MN").CampaignMap);
        Assert.True(File.Exists(Path.Combine(data, "launcher.json")));
    }

    [Fact]
    public void A_locked_setup_brings_its_server_only_when_the_client_has_none()
    {
        var target = Settings("Default", Setup("Default", "Vanilla"));
        target.EnsureSetups();
        Named(target, StableClientSetup.VanillaName).SteamHostName = "Kept";
        var incoming = Settings("Default", Setup("Default", "Vanilla"));
        incoming.EnsureSetups();
        Named(incoming, StableClientSetup.VanillaName).SteamHostName = "Other";
        Named(incoming, StableClientSetup.MoreNationsName).DirectHost = "10.0.0.5";
        Named(incoming, StableClientSetup.MoreNationsName).UseDirectConnection = true;

        var added = MoreNationsFoldIn.Merge(target, incoming, MoreNationsFoldIn.MoreNationsSuffix);

        Assert.Empty(added);
        Assert.Equal("Kept", Named(target, StableClientSetup.VanillaName).SteamHostName);
        Assert.Equal("10.0.0.5", Named(target, StableClientSetup.MoreNationsName).DirectHost);
        Assert.True(Named(target, StableClientSetup.MoreNationsName).UseDirectConnection);
        Assert.Single(target.Setups, setup => setup.Name == StableClientSetup.MoreNationsName);
    }

    [Fact]
    public void A_clashing_name_gets_a_free_suffix()
    {
        var target = Settings("A", Setup("A", "Vanilla", "one"), Setup("A (More Nations)", "Vanilla", "two"));
        var incoming = Settings("A", Setup("A", "MoreNations", "three"));

        var added = MoreNationsFoldIn.Merge(target, incoming, MoreNationsFoldIn.MoreNationsSuffix);

        Assert.Equal(new[] { "A (More Nations) 2" }, added);
        Assert.Equal("three", Named(target, "A (More Nations) 2").SteamHostName);
    }

    [Fact]
    public void It_knows_a_Coodsz_and_Friends_folder_by_the_exe_or_by_the_rename_marker()
    {
        Assert.False(MoreNationsFoldIn.CameFromFriends(_friends));
        File.WriteAllText(Path.Combine(_friends, SuccessorInstall.RenameMarker), Path.Combine(_friends, "Coodsz Coop and Friends.exe"));
        Assert.False(MoreNationsFoldIn.CameFromFriends(_friends));
        File.WriteAllText(Path.Combine(_friends, SuccessorInstall.RenameMarker), Path.Combine(_friends, "Coodsz and Friends.exe") + Environment.NewLine);
        Assert.True(MoreNationsFoldIn.CameFromFriends(_friends));
        Assert.True(File.Exists(Path.Combine(_friends, MoreNationsFoldIn.NoteName)));
        File.Delete(Path.Combine(_friends, SuccessorInstall.RenameMarker));
        File.Delete(Path.Combine(_friends, MoreNationsFoldIn.NoteName));
        File.WriteAllText(Path.Combine(_friends, "Coodsz and Friends.exe"), "old");
        Assert.True(MoreNationsFoldIn.CameFromFriends(_friends));
        Assert.False(MoreNationsFoldIn.CameFromFriends(Path.Combine(_root, "missing")));
        Assert.False(File.Exists(Path.Combine(_coop, MoreNationsFoldIn.NoteName)));
    }

    // The rename cleanup removes Coodsz and Friends.exe and the marker in the background even when the
    // fold in failed on that start. The note left on the first look keeps the folder a Friends folder.
    [Fact]
    public void A_fold_in_that_failed_once_still_turns_the_switch_on_after_the_rename_cleanup()
    {
        File.WriteAllText(Path.Combine(_friends, "Coodsz and Friends.exe"), "old");
        File.WriteAllText(Path.Combine(_friends, SuccessorInstall.RenameMarker), Path.Combine(_friends, "Coodsz and Friends.exe"));
        Settings("Default", Setup("Default", "")).Save(Path.Combine(_friends, "launcher.json"));
        // A file where the client's folder goes makes this start's fold in fail.
        Directory.CreateDirectory(Path.GetDirectoryName(_client)!);
        File.WriteAllText(_client, "in the way");

        MoreNationsFoldIn.Run(_client, _friends, MoreNationsFoldIn.CameFromFriends(_friends), moreNationsFolder: null);
        Assert.False(Directory.Exists(_client));
        File.Delete(Path.Combine(_friends, "Coodsz and Friends.exe"));
        File.Delete(Path.Combine(_friends, SuccessorInstall.RenameMarker));
        File.Delete(_client);

        MoreNationsFoldIn.Run(_client, _friends, MoreNationsFoldIn.CameFromFriends(_friends), moreNationsFolder: null);

        var client = Client();
        Assert.True(client.MoreNations);
        Assert.Equal("MoreNations", Named(client, "Default").CampaignMap);
    }

    // The settings are saved and the folder is marked done before the cache copy, so a locked
    // profile-cache.json can not make the next start merge the same file again.
    [WindowsFact]
    public void A_cache_copy_that_fails_does_not_make_the_next_start_merge_again()
    {
        Settings("Coop", Setup("Default", "Vanilla"), Setup("Coop", "Vanilla", "Raider")).Save(Path.Combine(_coop, "launcher.json"));
        MoreNationsFoldIn.Run(_client, _coop, fromFriends: false, moreNationsFolder: null);
        Settings("Default", Setup("Default", "MoreNations", "Friend")).Save(Path.Combine(_friends, "launcher.json"));
        var cache = Path.Combine(_friends, "profile-cache.json");
        File.WriteAllText(cache, "cached");

        using (new FileStream(cache, FileMode.Open, FileAccess.Read, FileShare.None))
            MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);
        MoreNationsFoldIn.Run(_client, _friends, fromFriends: true, moreNationsFolder: null);

        var client = Client();
        Assert.True(client.MoreNations);
        Assert.Single(client.Setups, setup => setup.Name.StartsWith("Default (More Nations)", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_client, "launcher.more-nations-2.json")));
        Assert.Contains("Could not copy profile-cache.json", Log());
    }

    [Fact]
    public void Merging_the_same_settings_twice_adds_nothing_the_second_time()
    {
        var target = Settings("Default", Setup("Default", "Vanilla"));
        var incoming = Settings("Default", Setup("Default", "MoreNations", "Friend"), Setup("Night", "MoreNations"));

        var first = MoreNationsFoldIn.Merge(target, incoming, MoreNationsFoldIn.MoreNationsSuffix);
        var second = MoreNationsFoldIn.Merge(target, incoming, MoreNationsFoldIn.MoreNationsSuffix);

        Assert.Equal(new[] { "Default (More Nations)", "Night" }, first);
        Assert.Empty(second);
    }

    [Fact]
    public void The_client_reads_the_Friends_exe_before_the_rename_cleanup_and_folds_in_before_ToolData()
    {
        var app = SourceFiles.Read("src/BannerlordCoopLauncher/App.xaml.cs");
        var detect = app.IndexOf("MoreNationsFoldIn.CameFromFriends(AppContext.BaseDirectory)", StringComparison.Ordinal);
        var rename = app.IndexOf("ToolRename.OnStartup()", StringComparison.Ordinal);
        var fold = app.IndexOf("MoreNationsFoldIn.Run(ToolData.FolderOf(ToolData.CoopClient), AppContext.BaseDirectory, fromFriends, ToolData.FolderOf(ToolData.MoreNationsClient));", StringComparison.Ordinal);
        var start = app.IndexOf("ToolData.Start(ToolData.CoopClient);", StringComparison.Ordinal);

        Assert.True(detect >= 0 && detect < rename);
        Assert.True(rename < fold && fold < start);
        Assert.DoesNotContain("MoreNationsClient :", app);
    }
}

// The More Nations switch drives what LauncherBrand.IncludeMoreNations drove in the two builds: off is
// today's Coop client launcher, on is the Coodsz and Friends build.
public class MoreNationsSwitchTests
{
    [Fact]
    public void The_switch_is_off_for_a_new_player_and_picks_the_map_of_a_setup_with_none()
    {
        var settings = new LauncherSettings();
        Assert.False(settings.MoreNations);
        Assert.Equal(StableClientSetup.VanillaMap, settings.DefaultMap());

        settings.MoreNations = true;
        Assert.Equal(StableClientSetup.MoreNationsMap, settings.DefaultMap());
    }

    [Fact]
    public void The_switch_is_saved_and_an_older_file_without_it_reads_as_off()
    {
        var path = Path.Combine(Path.GetTempPath(), "coodsz-switch-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new LauncherSettings { MoreNations = true }.Save(path);
            Assert.Contains("\"MoreNations\": true", File.ReadAllText(path));
            Assert.True(LauncherSettings.Load(path).MoreNations);
            Assert.DoesNotContain("DefaultMap", File.ReadAllText(path));

            File.WriteAllText(path, "{ \"GamePath\": \"Game\", \"KeepRbmCoopFriendly\": false }");
            Assert.False(LauncherSettings.Load(path).MoreNations);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_launcher_reads_the_switch_where_it_read_the_build()
    {
        var window = SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher");
        // A setup with no map saved shows the switch's map, and with no map picked the mods follow the switch.
        Assert.Contains("? _settings.DefaultMap()", SourceFiles.Method(window, "private void SelectCampaignMap(string campaignMap)"));
        Assert.Contains("return _settings.MoreNations;", window);
        Assert.Contains("MoreNationsBox.IsChecked = _settings.MoreNations;", window);
        Assert.Contains("_settings.MoreNations = MoreNationsBox.IsChecked == true;", window);
        Assert.Contains("_settings.Save(SettingsPath);", SourceFiles.Method(window, "private async void MoreNations_Changed(object sender, RoutedEventArgs e)"));
        Assert.True(window.IndexOf("ShowMoreNationsSwitch();", StringComparison.Ordinal) < window.IndexOf("ApplySetupToForm(_settings.Current());", StringComparison.Ordinal));
        var xaml = SourceFiles.Read("src/BannerlordCoopLauncher/MainWindow.xaml");
        Assert.Contains("x:Name=\"MoreNationsBox\" Content=\"More Nations\"", xaml);
        Assert.Contains("Checked=\"MoreNations_Changed\" Unchecked=\"MoreNations_Changed\"", xaml);
        Assert.Contains("Title=\"Coodsz Coop Client Launcher\"", xaml);
        Assert.Contains("public const string Name = \"Coodsz Coop Client Launcher\";", SourceFiles.Read("src/BannerlordCoopLauncher/LauncherBrand.cs"));
    }
}
