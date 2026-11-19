using System.IO;
using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Coodsz.Ui;
using CoodszHost.Keep;
using CoodszHost.Services;

namespace CoodszHost;

// The Dashboard (both editions, redesign wave 2): the server state with the one big button, the values the
// Home page had, the newest server lines, the players and the mods. Every button runs the Saves page's own code.
// The CoopKeep style Players page is wired here too (Coodsz Host only).
public partial class MainWindow
{
    private List<LivePlayer> _livePlayers = new();
    private string _lastLoad = "";
    // The last run ended in a crash and nobody started the server since (the Dashboard's Crashed state).
    private bool _crashedSinceStart;
    // Restart (by hand, the timer or Copy invite) runs: the big button says Restarting...
    private bool _restartingNow;
    // Stop runs: the big button says Saving and stopping...
    private bool _stoppingNow;

    /// <summary>The page the window opens on, in both editions.</summary>
    private const string StartPage = "Dashboard";

    /// <summary>Called once from StartKeep in Coodsz Host.</summary>
    private void StartKeepPages()
    {
        NavPlayers.Visibility = Visibility.Visible;
        NavMap.Visibility = Visibility.Visible;
        NavEncyclopedia.Visibility = Visibility.Visible;
        NavGuides.Visibility = Visibility.Visible;
        StreamerButton.Visibility = Visibility.Visible;
        PresetCard.Visibility = Visibility.Visible;
        CreateWorldButton.Visibility = Visibility.Visible;
        // The Players page has every function of the old player card; the Dashboard shows who is online.
        PlayersCard.Visibility = Visibility.Collapsed;
        OnlinePlayersCard.Visibility = Visibility.Visible;
        KeepPlayers.RefreshRequested += RefreshPlayers;
        KeepPlayers.RefreshBansRequested += RefreshBans;
        KeepPlayers.AddBansRequested += () => AddBanIds_Click(this, new RoutedEventArgs());
        KeepPlayers.BanRequested += row => KeepPlayers.Say(BanBySteamId(row.SteamId, row.Name), SteamBanFile.IsSteamId64(row.SteamId));
        KeepPlayers.UnbanRequested += id =>
        {
            UnbanId(id);
            KeepPlayers.Say("Took " + StreamerMask.SteamId64(id, Streamer) + " off the ban list.", true);
        };
        KeepPlayers.ProfileRequested += row => _ = OpenProfileFor(row.Name, row.SteamId);
        KeepPlayers.CheatsRequested += row => _ = OpenCheatsFor(row.Name, row.SteamId);
        StartPresets();
        StartModFilter();
        StartEncyclopedia();
        StartMap();
        StartNewBadges();
        StartRbmCard();
    }

    /// <summary>The NEW badges on what this version added to Coodsz Host (see NewFeatures.CoodszHost).</summary>
    private void StartNewBadges()
    {
        foreach (var (nav, id) in new (Button, string)[]
                 {
                     (NavDashboard, NewFeatures.Home), (NavPlayers, NewFeatures.Players), (NavMap, NewFeatures.Map),
                     (NavEncyclopedia, NewFeatures.Encyclopedia), (NavGuides, NewFeatures.Guides), (NavCheats, NewFeatures.Cheats),
                 })
            NewBadges.Attach(nav, id, BadgePlace.Right);
        NewBadges.Attach(StreamerButton, NewFeatures.StreamerMode, BadgePlace.Right);
        NewBadges.Attach(PresetHeading, NewFeatures.WorldPresets, BadgePlace.After);
        NewBadges.Attach(CreateWorldButton, NewFeatures.CreateWorld, BadgePlace.After);
        NewBadges.Attach(GameSpeedButton, NewFeatures.GameSpeed);
        NewBadges.Attach(GameSpeedBattleButton, NewFeatures.GameSpeed);
    }

    /// <summary>Called once from the constructor: the log card's Open full log goes to the Logs page, and the columns follow the width.</summary>
    private void StartDashboard()
    {
        DashLog.OpenFullLog += () => ShowPage("Logs");
        DashboardPage.SizeChanged += (_, e) => FitDashboard(e.NewSize.Width);
    }

    // The side column (online players, mods, Join my server) sits beside the main one while both fit, and under it on a
    // narrow window, so "Saves every 5 min" and the other status values are never squeezed.
    private void FitDashboard(double width)
    {
        var stack = Coodsz.Ui.FrameRules.SideColumnWraps(width, DashSideWidth);
        Grid.SetColumnSpan(DashMain, stack ? 2 : 1);
        DashMain.Margin = new Thickness(0, 0, stack ? 0 : 12, 0);
        Grid.SetRow(DashSide, stack ? 1 : 0);
        Grid.SetColumn(DashSide, stack ? 0 : 1);
        Grid.SetColumnSpan(DashSide, stack ? 2 : 1);
        DashSideColumn.Width = new GridLength(stack ? 0 : DashSideWidth);
    }

    private const double DashSideWidth = 300;

    private void OnKeepPageShown(string page)
    {
        if (Edition.IsAD1259)
            return;
        if (page == "Players")
        {
            ShowKeepPlayers();
            RefreshBans();
        }
        if (page == "Modules")
            UpdateModFilterCounts();
        if (page == "Encyclopedia")
            KeepEncyclopedia.Shown();
        if (page == "Map")
            KeepMap.Shown();
    }

    private void BackupNow_Click(object sender, RoutedEventArgs e) => _ = BackupNowAsync();

    private void CreateWorld_Click(object sender, RoutedEventArgs e) => OpenNewWorld();

    // Stop auto restart: the crash restart (or the save retry) that waits is cancelled; the server stays stopped.
    private void StopAutoRestart_Click(object sender, RoutedEventArgs e)
    {
        if (!CrashRestartWaits)
            return;
        CancelCrashRestart();
        CancelRescueRetry();
        const string line = "The restart after the crash was cancelled. Press Start server when you are ready.";
        Append(line);
        SetControl(line);
        _actions.Note("Crash restart", "Cancelled by hand");
        RefreshDashboard();
    }

    /// <summary>The save that runs now, or the one Start server loads.</summary>
    private string HomeSaveName() =>
        _session.IsRunning && _launchedSave.Length > 0 ? _launchedSave : _settings.Current().SaveName ?? "";

    // A crash restart or a save retry waits for its turn.
    private bool CrashRestartWaits => _crashRestartTimer != null || _rescueRetryTimer != null;

    private HomeFacts DashboardFacts()
    {
        var running = _session.IsRunning;
        var save = HomeSaveName();
        var savePath = save.Length > 0 && SaveSet.IsValidSaveName(save) ? Path.Combine(ServerSession.SavesPath, save + ".sav") : "";
        return new HomeFacts(
            running,
            _serving,
            _engineExited,
            _session.Phase.IsShuttingDown() || _stoppingNow,
            save,
            running && _session.StartedAt is DateTime started ? DateTime.Now - started : null,
            _livePlayers.Count,
            ChosenAutosaveMinutes(),
            savePath.Length > 0 && File.Exists(savePath) ? File.GetLastWriteTime(savePath) : null,
            LastBackup(save),
            Crashed: _crashedSinceStart,
            Restarting: _restartingNow || _restartingAfterCrash || (!running && CrashRestartWaits));
    }

    /// <summary>Redraws the Dashboard from the one second clock (both editions). The buttons follow the state on every page.</summary>
    private void RefreshDashboard()
    {
        if (!_addressReady)
            return;
        var facts = DashboardFacts();
        ShowServerActions(facts);
        if (_page != "Dashboard")
            return;
        DashUptime.Text = HomeDashboard.Uptime(facts.Uptime);
        DashLastSave.Text = HomeDashboard.Ago(facts.LastSave, DateTime.Now);
        DashLastBackup.Text = HomeDashboard.Ago(facts.LastBackup, DateTime.Now);
        DashAutosave.Text = HomeDashboard.Autosave(facts.AutosaveMinutes);
        var hasPassword = !string.IsNullOrWhiteSpace(PasswordBox.Text);
        PasswordChip.Kind = hasPassword ? ChipKind.Gold : ChipKind.Mute;
        PasswordChip.Text = hasPassword ? "Password Set" : "Password Open";
        var steam = SteamBox.IsChecked == true;
        SteamChip.Kind = steam ? ChipKind.Ok : ChipKind.Mute;
        SteamChip.Text = steam ? "Steam lobby On" : "Steam lobby Off";
        var chips = Edition.IsAD1259 || facts.SaveName.Length == 0 ? Array.Empty<string>() : HomeChips(facts.SaveName);
        if (!chips.SequenceEqual(DashChips.ItemsSource as IEnumerable<string> ?? Array.Empty<string>()))
            DashChips.ItemsSource = chips;
        DashChips.Visibility = chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowDashboardPlayers();
        ShowModsSummary();
        ShowFirstRunSteps();
    }

    // The big button and the smaller ones beside it (ux-plan 1.3). The pending states can't be pressed, and a running
    // server action (_controlBusy) turns every one off until it ends.
    private void ShowServerActions(HomeFacts facts)
    {
        var state = HomeDashboard.State(facts);
        var primary = HomeDashboard.Primary(state);
        var main = primary.Stops ? StopButton : StartButton;
        var other = primary.Stops ? StartButton : StopButton;
        other.Visibility = Visibility.Collapsed;
        main.Visibility = Visibility.Visible;
        // The label shows translated, so the English behind it is what gets compared (else it is set again every second).
        if (Coodsz.Ui.UiLanguage.English(main, ContentControl.ContentProperty) != primary.Label)
            main.Content = primary.Label;
        main.IsEnabled = primary.Enabled && !_controlBusy;
        var actions = HomeDashboard.Secondary(state, CrashRestartWaits);
        foreach (var (button, action) in new (Button, DashboardActions)[]
                 {
                     (StopStartingButton, DashboardActions.StopStarting), (SaveWorldButton, DashboardActions.SaveGame),
                     (RestartButton, DashboardActions.Restart), (BackupNowButton, DashboardActions.BackupNow),
                     (LastGoodButton, DashboardActions.LastGoodSave), (StopAutoRestartButton, DashboardActions.StopAutoRestart),
                 })
        {
            button.Visibility = actions.HasFlag(action) ? Visibility.Visible : Visibility.Collapsed;
            // Stop auto restart is the way out of a restart that waits, so busy never turns it off.
            button.IsEnabled = action == DashboardActions.StopAutoRestart || !_controlBusy;
        }
    }

    private void ShowDashboardPlayers()
    {
        if (Edition.IsAD1259)
            return;
        // Streamer mode: Player 1, Player 2, numbered like the other pages.
        var names = _livePlayers.Select(player => player.Name).Distinct().Select(name => Streamer ? Translator.T(StreamerNames.Alias(name)) : name).ToList();
        if (!names.SequenceEqual(DashPlayers.ItemsSource as IEnumerable<string> ?? Array.Empty<string>()))
            DashPlayers.ItemsSource = names;
        DashPlayerCount.Text = names.Count.ToString();
        DashNobody.Visibility = names.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowModsSummary()
    {
        var database = CommunityCompat.Current;
        ModsSummaryCard.Show(ModTally.Count(_modules.Select(row =>
            new TallyRow(row.Id, row.Enabled, row.Required, CommunityCompat.Works(row.Id, database)))));
        if (DashActiveCoop.Text != ActiveCoop.Text)
            DashActiveCoop.Text = ActiveCoop.Text;
        DashActiveCoop.Visibility = ActiveCoop.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowMadeFor();
    }

    // The Made for card: the AD1259 build checks the server's game, AD1259, RBM and Coop against its pack (AD1259Stack);
    // the Coop build pins nothing, so it shows the game and the Coop the server has under Versions. Both show the newer game
    // line of this PC's game (_newerGame) there and under Start server.
    private void ShowMadeFor()
    {
        var coop = ModuleVersion("Coop") is { Length: > 0 } found ? found : ModuleVersion("CoopNightly");
        var game = ModuleVersion("Native", "DedicatedServer.Windows");
        NewerGameText.Text = _newerGame ?? "";
        NewerGameText.Visibility = _newerGame == null ? Visibility.Collapsed : Visibility.Visible;
        if (!Edition.IsAD1259)
        {
            MadeForCard.Title = "Versions";
            MadeForCard.Show(new[]
            {
                new VersionPin("Bannerlord", PinIcon.Game, null, game, BaseGame: true),
                new VersionPin("Coop", PinIcon.Coop, null, coop),
            }, new[] { _newerGame ?? "" });
            return;
        }

        VersionPin Pack(string id, string name, PinIcon icon, string? have) =>
            AD1259Stack.Find(id) is { } mod ? new VersionPin(name, icon, mod.Version, have, AlsoAccepted: mod.AlsoAccepted) : new VersionPin(name, icon, null, have);
        MadeForCard.Show(new[]
        {
            new VersionPin("Bannerlord", PinIcon.Game, AD1259Stack.GameVersion, game, BaseGame: true),
            Pack("AD1259", "AD1259", PinIcon.Mod, ModuleVersion("AD1259")),
            Pack("RBM", "RBM", PinIcon.Mod, ModuleVersion("RBM")),
            Pack("Coop", "Coop", PinIcon.Coop, coop),
        }, new[] { _newerGame ?? "" });
    }

    private string _stepsShown = "";

    // First run: the strip shows until the server folder is found and a save is picked or set to be made.
    private void ShowFirstRunSteps()
    {
        var hasServer = File.Exists(Executable);
        var profile = _settings.Current();
        var save = profile.SaveName ?? "";
        var hasSave = save.Length > 0 && (profile.NewCampaign || File.Exists(Path.Combine(ServerSession.SavesPath, save + ".sav")));
        var show = !hasServer || !hasSave;
        FirstRunCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        var key = hasServer + "|" + hasSave + "|" + Translator.Current;
        if (!show || key == _stepsShown)
            return;
        _stepsShown = key;
        var saveStep = Edition.IsAD1259
            ? new FrameStep(Translator.T("Pick or make a save"), hasSave, Translator.T("Open Saves"), Translator.T("Opens the Saves page with the save list and + New save."), () => ShowPage("Server"))
            : new FrameStep(Translator.T("Pick or make a save"), hasSave, Translator.T("Create a world"), Translator.T("A new campaign in a few steps: name, difficulty, rules and password."), OpenNewWorld);
        FirstRunSteps.Show(new[]
        {
            new FrameStep(Translator.T("Set server folder"), hasServer, Translator.T("Set server folder"),
                Translator.T("Chooses which BannerlordCoopServer.exe this tool starts."), () => SetServerFolder_Click(this, new RoutedEventArgs())),
            saveStep,
            new FrameStep(Translator.T("Start server"), false),
        });
    }

    // A server line for the Recent server output card: its time, the level word and the line, coloured by tone.
    private void PushDashboardLine(string kind, string message, string tone) =>
        DashLog.Push(DateTime.Now, kind, message, tone switch
        {
            "bad" => ChipKind.Bad,
            "warn" => ChipKind.Warn,
            "good" => ChipKind.Ok,
            _ => ChipKind.Mute,
        });

    // A control line that only makes sense while the server runs.
    private static bool IsRunLine(string english) =>
        english.StartsWith("Server is serving", StringComparison.Ordinal)
        || english.StartsWith("Server is starting", StringComparison.Ordinal)
        || english.StartsWith("Server is ready", StringComparison.Ordinal);

    private string _chipsKey = "";
    private IReadOnlyList<string> _chips = Array.Empty<string>();

    // The preset tags of the world, read again when the save or the language changes, or every 10 seconds.
    private IReadOnlyList<string> HomeChips(string save)
    {
        var key = save + "|" + Translator.Current + "|" + DateTime.Now.Ticks / TimeSpan.FromSeconds(10).Ticks;
        if (key == _chipsKey)
            return _chips;
        _chipsKey = key;
        _chips = WorldPresets.Chips(CoopSettingsFor(save.Length > 0 ? save : null, out _)).Select(Translator.T).ToList();
        return _chips;
    }

    private DateTime? _lastBackupRead;
    private string _lastBackupSave = "";
    private DateTime? _lastBackup;

    // The newest zip in Save Backups for this save, read again at most every 10 seconds.
    private DateTime? LastBackup(string save)
    {
        if (save.Length == 0 || !SaveSet.IsValidSaveName(save))
            return null;
        if (save == _lastBackupSave && _lastBackupRead is DateTime read && DateTime.Now - read < TimeSpan.FromSeconds(10))
            return _lastBackup;
        _lastBackupSave = save;
        _lastBackupRead = DateTime.Now;
        try
        {
            _lastBackup = SaveBackups.List(save).Select(entry => (DateTime?)entry.CreatedLocal).Max();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _lastBackup = null;
        }

        return _lastBackup;
    }

    /// <summary>Backup now on the Dashboard and Saves: a zip of the save in Save Backups, the same zip the autosave makes.</summary>
    private async Task BackupNowAsync()
    {
        var save = HomeSaveName();
        if (save.Length == 0 || !SaveSet.IsValidSaveName(save) || !File.Exists(Path.Combine(ServerSession.SavesPath, save + ".sav")))
        {
            SetControl("There is no save file to back up yet. Start the world once, or pick a save on the Saves page.", failed: true);
            return;
        }

        var backups = SaveBackups;
        try
        {
            await Task.Run(() => backups.Create(save, BackupReason.Manual));
            _lastBackupRead = null;
            var note = "Backed up " + save + ". Load backup puts it back.";
            Append(note);
            SetControl(note, done: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _actions.Note("Backup", "Backup now failed for " + save + ": " + ex.Message);
            SetControl("Could not back up " + save + ". " + ex.Message, failed: true);
        }

        RefreshDashboard();
    }

    private void ShowKeepPlayers()
    {
        if (Edition.IsAD1259)
            return;
        var line = !_session.IsRunning
            ? "The server is stopped. Start it to see connected players."
            : _serving
                ? "The server is running. Players show up here when they join."
                : "The server is starting. Players can join once it says online.";
        KeepPlayers.ShowPlayers(_livePlayers.Select(player => (player.Name, player.SteamId)).ToList(), Streamer, line);
    }

    private void UnbanId(string id)
    {
        var ids = ReadBanIds();
        ids.RemoveAll(item => item == id);
        WriteBanIds(ids);
        RefreshBans();
    }

    /// <summary>The hero of this player for the profile and the Cheats page (a bare one before the server answers).</summary>
    private static async Task<KeepPlayer> FindKeepPlayer(KeepContext keep, string name, string steamId)
    {
        var players = await keep.LoadPlayersAsync();
        return players.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? players.FirstOrDefault(p => string.Equals(p.SteamName, name, StringComparison.OrdinalIgnoreCase))
               ?? (steamId.Length > 0 ? players.FirstOrDefault(p => p.SteamId == steamId) : null)
               ?? new KeepPlayer(name, "", steamId, true, null);
    }

    private async Task OpenProfileFor(string name, string steamId)
    {
        if (KeepContext.Current is not { } keep)
            return;
        new PlayerProfileWindow(keep, new KeepTarget(await FindKeepPlayer(keep, name, steamId))).Show();
    }

    private async Task OpenCheatsFor(string name, string steamId)
    {
        if (KeepContext.Current is not { } keep)
            return;
        var target = new KeepTarget(await FindKeepPlayer(keep, name, steamId));
        ShowPage("Cheats");
        KeepCheats.Pick(target);
    }

    /// <summary>The player box line (no SteamID64 in streamer mode).</summary>
    private string PlayerLine(LivePlayer player) => StreamerMask.PlayerLine(player.Name, player.SteamId, Streamer);
}
