using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Documents;
using TaomCoop.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using Microsoft.Win32;

namespace CoodszHost;

public partial class MainWindow : Window
{
    private readonly ServerSession _session = new();
    private readonly HostActionLog _actions = new(() => ServerSession.LogsPath);
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    // CPU and memory of the server's process tree, on the overview.
    private readonly ServerLoad _load = new();
    private readonly ObservableCollection<ModuleRow> _modules = new();
    private HostSettings _settings = new();
    private bool _loading = true;
    private bool _serving;
    // True once this run reached serving, kept after the exit so the crash report knows.
    private bool _servedThisRun;
    // The Steam lobby as this run's log tells it (Home shows it), reset on every start.
    private SteamLobbyState _steamLobby;
    private bool _engineExited;
    private bool _userStop;
    private bool _watchExits;
    private readonly Queue<string> _recentServerLines = new();
    private readonly HashSet<string> _joinAdvice = new(StringComparer.Ordinal);
    private bool _exitExplainQueued;
    private bool _blockedDllNoted;
    private string _launchedSave = "";
    private bool _controlBusy;
    private bool _autosaveHeld;
    private bool _spareUntilOnMap;
    private string _worldLogText = "";
    private string _worldLogPath = "";
    private long _worldLogLength = -1;
    private string? _announceSaveOnServing;
    private Point _dragStart;
    private ModuleRow? _dragRow;
    private bool _dragMoved;
    private bool _dragBlocked;
    private readonly DispatcherTimer _refreshPulse = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private Storyboard? _settingsPulse;
    private string _savedSettings = "";
    private bool _watchSettings;
    private readonly DispatcherTimer _logAlertPulse = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _logAlertBright;
    private readonly DispatcherTimer _unblockPulse = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _unblockBright;
    private bool _unblockExample;
    private string? _logAlertDefault;
    private List<McmSettingFile> _mcmFiles = new();
    private Window? _sharedGuide;
    private Button? _sharedGuideContinue;
    private Action? _sharedGuideAction;

    private string SettingsPath => ToolData.File("host.json");
    private string Executable => File.Exists(_settings.ServerExecutable) ? _settings.ServerExecutable : "";
    private string ServerRoot => Path.GetDirectoryName(Executable) ?? "";

    public MainWindow()
    {
        InitializeComponent();
        // A window bigger than the screen opens inside the work area; under 1000 px the sidebar shows icons only.
        var fit = WindowFit.Fit(new WindowSize(Width, Height, MinWidth, MinHeight), SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        (Width, Height, MinWidth, MinHeight) = (fit.Width, fit.Height, fit.MinWidth, fit.MinHeight);
        LanguageSlot.Content = Coodsz.Ui.UiLanguage.MakeButton();
        WhatsNewSlot.Content = Coodsz.Ui.UiLanguage.MakeWhatsNewButton();
        DonateSlot.Content = Coodsz.Ui.UiLanguage.MakeDonateButton();
        LicenseSlot.Content = Coodsz.Ui.UiLanguage.MakeLicenseButton();
        SetUpCrashReports();
        StartSaveModsCard();
        StartDashboard();
        // The release number players quote in bug reports. It is the same number the
        // update check compares against GitHub.
        VersionText.Text = Edition.Version;
        // The window title is the tool's own name, the one the guides, README and release notes use.
        Title = Edition.Name;
        // Names the client tool of this edition, never Coop and Friends in the AD1259 server tool.
        MapSaveNote.Text = "An existing save keeps the map it was created with. Friends need that same map selected in " + ClientToolName + ".";
        if (Edition.IsAD1259)
        {
            EditionCredit.Visibility = Visibility.Visible;
            // The AD1259 client launcher has no map choice.
            MapSaveNote.Visibility = Visibility.Collapsed;
            ToolHeading.Text = Edition.Brand;
            // New setup starts on the AD1259 map here (StableCoop.CreateFresh).
            NewSetupButton.ToolTip = "Starts a setup on the default mod order, with the AD1259 map and an empty save.";
            NewSetupHintRun.Text = " starts on the default mod order, with the AD1259 map and an empty save. ";
        }
        Coodsz.Ui.NavRail.Fit(this, NavColumn, changed: ShowCompactSidebar);
        FillDifficulties();
        FillRegions();
        WatchSettingsPage();
        ShowGameSpeedGroup();
        StartClanCap();
        StartServerRules();
        StartPvpRules();
        ModuleList.ItemsSource = _modules;
        WatchCommunityCompat();
        ShowModTestButton();
        StartModsPage();
        StartKeep();
        StartConsole();
        _refreshPulse.Tick += (_, _) =>
        {
            _refreshPulse.Stop();
            RefreshModulesButton.Content = "Refresh";
            RefreshModulesButton.ClearValue(BackgroundProperty);
            RefreshModulesButton.ClearValue(ForegroundProperty);
            RefreshModulesButton.ClearValue(BorderBrushProperty);
            RefreshModulesButton.ClearValue(BorderThicknessProperty);
        };
        StartLineDrain();
        // Server lines are queued and drawn in batches every 150 ms (StartLineDrain). A blocking Invoke per line held the output reader
        // on the window, the pipe filled and the server's game thread stalled on its own console writes
        // (a modded character's thousands of log lines timed out Coop's character transfer).
        _session.ServerLine += line =>
        {
            var phase = _session.Phase;
            var replaying = _session.ReplayingLog;
            // Checked when the line arrives too: a queued line waits up to 150 ms, and the load test may start or end meanwhile.
            var testing = _modTestRunning;
            PostToWindow(draw =>
            {
                // A server load test runs its own server; its lines are not this server's.
                if (testing || _modTestRunning)
                    return;
                OnServerLine(line, phase, replaying, draw);
            });
        };
        // Play hold reads Coop's time lines once, from this window's own server output.
        _session.ProcessLine += line =>
        {
            if (!line.Contains("[Coop] ", StringComparison.Ordinal))
                return;
            var testing = _modTestRunning;
            PostToWindow(_ =>
            {
                if (!testing && !_modTestRunning)
                    NotePlayHold(line);
            });
        };
        void OnServerLine(string line, ServerPhase phase, bool replaying, bool draw)
        {
            NoteServerLine(line);
            NoteBlockedDll(line);
            OnRescueLine(line, replaying);
            if (!replaying)
            {
                _modPatchSetup.Observe(line);
                _saveRefused |= ServerCrashExplain.IsSaveRefusedLine(line);
                if (ServerCrashExplain.IsFatalLine(line))
                    _fatalLine = line;
                _mapMissing |= MissingCampaignMap.IsScriptLine(line);
                if (_joinPause.HostPauseUndone(line))
                    KeepHostPause(line);
            }
            NoteSavedWorld(line);
            if (SteamLobbyLine.Read(line) is { } lobby)
                _steamLobby = lobby;
            // The session reads the phase from the server's @DS@ state events (or the
            // older SERVING line) before this handler runs.
            if (phase == ServerPhase.Serving)
            {
                var becameServing = !_serving;
                if (becameServing)
                    _joinPause.Reset();
                _serving = true;
                _servedThisRun = true;
                _engineExited = false;
                if (becameServing)
                {
                    // The server serves again, also after the tool's own restart: the crash bar of the last run goes.
                    if (!replaying)
                        _crashBar?.ServerStarted();
                    if (!_unblockExample)
                        StopUnblockAlert();
                    if (_announceSaveOnServing != null)
                    {
                        var saved = _announceSaveOnServing;
                        _announceSaveOnServing = null;
                        Append(saved);
                        if (!_controlBusy)
                        {
                            var savedOk = saved.StartsWith("SAVE: Saved ", StringComparison.Ordinal);
                            SetControl("Server is serving. " + saved, done: savedOk, failed: !savedOk);
                        }
                    }
                    else if (!_controlBusy || Coodsz.Ui.UiLanguage.English(ControlStatus).StartsWith("The server cannot load a DLL", StringComparison.Ordinal))
                        SetControl("Server is serving.", done: true);
                }
            }
            else if (line.Contains("engine exited", StringComparison.OrdinalIgnoreCase))
            {
                var wasServing = _serving || _servedThisRun;
                _engineExited = true;
                _serving = false;
                if (!_controlBusy)
                    SetControl("The game engine exited. Click Stop server to clear it.", failed: true);
                QueueExitReport(line, wasServing);
            }

            // Every line is handled; only the ones the log box keeps are drawn.
            if (draw)
                Append(line);
            // A replayed line already happened, so it is not checked again.
            if (!replaying && line.Contains(DsEventParser.Marker, StringComparison.Ordinal)
                && DsEventParser.TryParse(line, out var evt) && evt is DsEvent.Players players)
                {
                    NoteSharedSteamIds(players.List);
                    NoteJoinPause(players.List);
                }
            if (draw)
                MirrorServerLine(line);
        }
        // Status lines and the exit go through the same queue, so they stay in order with the server lines.
        _session.StatusLine += line =>
        {
            var testing = _modTestRunning;
            PostToWindow(_ =>
            {
                if (testing || _modTestRunning)
                    return;
                NoteServerLine(line);
                NoteBlockedDll(line);
                Append(line);
                if (line.Contains("Server process exited", StringComparison.OrdinalIgnoreCase))
                    QueueExitReport(line, _serving || _servedThisRun);
            });
        };
        _session.StateChanged += () => Dispatcher.Invoke(RefreshState);
        _session.Exited += exit => PostToWindow(_ => OnServerExited(exit));
        _clock.Tick += (_, _) =>
        {
            // A first copy on Open runs its frame inside LoadHost (CopyIntoServer), before the window is filled in.
            if (HalfLoaded)
                return;
            _session.SyncStartedAt();
            TickJoinPause();
            RefreshState();
        };
        _clock.Start();
        _logAlertPulse.Tick += (_, _) => PulseLogAlert();
        _unblockPulse.Tick += (_, _) => PulseUnblockButton();
        Loaded += (_, _) =>
        {
            LoadHost();
            _ = WatchForUpdateAsync(Edition.Repo, Edition.Version);
        };
        Closed += (_, _) => _session.Dispose();
    }

    private void LoadHost()
    {
        _loading = true;
        _settings = HostSettings.Load(SettingsPath);
        ShowRecentCommands();
        EnsureMcmSnapshots();
        LocateDedicatedServer();
        _session.UseServer(Executable);
        ProfileBox.ItemsSource = _settings.Profiles.Select(profile => profile.Name).ToList();
        ProfileBox.SelectedItem = _settings.Current().Name;
        ApplyLaunchDefaults(_settings.Current());
        ApplyProfileToFields(_settings.Current());
        ReloadModules();
        ReloadSaves();
        LoadDifficulty();
        RefreshBans();
        _loading = false;
        RememberSettingsSnapshot();
        _addressReady = true;
        ShowPage(StartPage);
        _session.AttachLogs();
        _watchExits = true;
        if (_session.IsRunning && _session.StartedAt is DateTime started && DateTime.Now - started > TimeSpan.FromSeconds(20))
        {
            _engineExited = false;
            _serving = true;
        }
        StartJoining();
        StartAutoRestartTimer();
        RefreshState();
        ApplyStreamerMode();
        var current = _settings.Current();
        _actions.Note("Open", "Coodsz Coop Server Tool opened. Profile=" + current.Name
            + ". Save=" + (string.IsNullOrWhiteSpace(current.SaveName) ? "(empty)" : current.SaveName)
            + ". Serving=" + _serving);
        if (!File.Exists(Executable))
            ShowServerMissingOnOpen();
        if (Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "--show-log-samples", StringComparison.OrdinalIgnoreCase)))
            ShowLogSamples();
        if (Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "--show-unblock-example", StringComparison.OrdinalIgnoreCase)))
        {
            _unblockExample = true;
            Dispatcher.BeginInvoke(() =>
            {
                _blockedDllNoted = false;
                NoteBlockedDll("Cannot load: ..\\Modules\\TradeBoundFix\\bin\\Win64_Shipping_Server\\TradeBoundFix.dll");
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    // The window opened with the server program missing. Right after a resubscribe that is a Workshop Coop
    // Steam is still downloading (CR-P75R3), so the usual line shows at once and the same check Start uses
    // runs off the window's thread. Its text replaces the line only when it says more and nothing else was
    // shown meanwhile.
    private async void ShowServerMissingOnOpen()
    {
        SetControl(DedicatedServerFinder.MissingMessage, failed: true);
        if (!AntivirusFiles.UsesWorkshopServer(_settings.ServerExecutable))
            return;
        var shown = ControlStatus.Text;
        string text;
        try
        {
            text = await Task.Run(() => ServerMissingText(GoneSinceLastStart()));
        }
        catch (Exception)
        {
            // Only a nicer wording: the usual line is up already, so nothing may fail the window here.
            return;
        }

        if (!string.Equals(text, DedicatedServerFinder.MissingMessage, StringComparison.Ordinal) && !File.Exists(Executable)
            && string.Equals(ControlStatus.Text, shown, StringComparison.Ordinal))
            SetControl(text, failed: true);
    }

    private void LocateDedicatedServer()
    {
        var found = DedicatedServerFinder.ResolveInstalled(_settings.ServerExecutable);
        if (string.IsNullOrWhiteSpace(found) || found.Equals(_settings.ServerExecutable, StringComparison.OrdinalIgnoreCase))
            return;
        NoteReplacedServer(_settings.ServerExecutable, found);
        _settings.ServerExecutable = found;
        HostSettings.Save(SettingsPath, _settings);
    }

    private void ShowLogSamples()
    {
        Append("These lines are examples so you can check the font. A real stop writes only one answer.");
        foreach (var sample in ServerCrashExplain.Examples())
        {
            Append(sample.Title);
            foreach (var line in sample.Lines)
                Append(line);
        }

        ShowPage("Logs");
        StartLogAlert();
    }

    private void LogAlert_Click(object sender, MouseButtonEventArgs e) => ShowPage("Logs");

    private void LogAlertConfirm_Click(object sender, RoutedEventArgs e) => StopLogAlert();

    private void StartLogAlert()
    {
        // One red bar at a time: while the crash bar shows, it is the one bar, this banner stays hidden
        // and only the Logs tab pulses. The blocked DLL fix, which has its own action, takes over from it.
        if (_crashBar?.ShowsCrash == true && _unblockPulse.IsEnabled)
            _crashBar.Hide();
        LogAlert.Visibility = _crashBar?.ShowsCrash == true ? Visibility.Collapsed : Visibility.Visible;
        if (!_logAlertPulse.IsEnabled)
            _logAlertPulse.Start();
        PulseLogAlert();
    }

    // The crash bar showed: the banner goes, the Logs tab keeps pulsing until the alert is stopped.
    private void HideLogAlertBanner() => LogAlert.Visibility = Visibility.Collapsed;

    private void StopLogAlert()
    {
        _logAlertPulse.Stop();
        LogAlert.Visibility = Visibility.Collapsed;
        buttonForeground(_page);
    }

    private void PulseLogAlert()
    {
        _logAlertBright = !_logAlertBright;
        var color = _logAlertBright
            ? Color.FromRgb(226, 194, 122)
            : Color.FromRgb(226, 120, 108);
        var brush = new SolidColorBrush(color);
        LogAlertText.Foreground = brush;
        NavLogs.Foreground = brush;
    }

    private static void ApplyLaunchDefaults(HostProfile profile)
    {
        profile.EnginePort = 7210;
        profile.CoopPort = 4200;
    }

    private static readonly string[] Regions = ["EU", "NA", "AS", "OC", "SA", "AF"];

    private void FillRegions()
    {
        RegionBox.Items.Clear();
        foreach (var region in Regions)
            RegionBox.Items.Add(region);
    }

    private void SelectRegion(string? region)
    {
        var value = string.IsNullOrWhiteSpace(region) ? "EU" : region.Trim().ToUpperInvariant();
        if (!RegionBox.Items.Contains(value))
            RegionBox.Items.Add(value);
        RegionBox.SelectedItem = value;
    }

    private void Region_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RegionBox.SelectedItem is not string region)
            return;
        var profile = _settings.Current();
        if (profile.Region.Equals(region, StringComparison.OrdinalIgnoreCase))
            return;
        profile.Region = region;
        HostSettings.Save(SettingsPath, _settings);
    }

    // A button that is off because the setup is locked says why on hover; unlocked, it gets its own tooltip back.
    private readonly Dictionary<Control, object?> _unlockedTips = new();

    private void ShowLockedTip(Control control, bool locked) =>
        ShowBlockedTip(control, locked ? BannerlordCoopLauncher.Core.Profile.LockedSetupNote.ButtonTip : null);

    // The tip says why a control is off (a locked setup, a running server); null gives it its own tip back.
    private void ShowBlockedTip(Control control, string? why)
    {
        if (!_unlockedTips.ContainsKey(control))
            _unlockedTips[control] = control.ToolTip;
        control.ToolTip = why ?? _unlockedTips[control];
        // Hover tips show on buttons that are off in every tool (UiLanguage.HoverTips).
        control.ClearValue(ToolTipService.ShowOnDisabledProperty);
    }

    private void ApplyProfileToFields(HostProfile profile)
    {
        ProfileNameBox.Text = profile.Name;
        ProfileNameBox.IsReadOnly = profile.Locked;
        MapBox.IsEnabled = !profile.Locked;
        CopySetupButton.IsEnabled = !profile.Locked;
        SaveSetupButton.IsEnabled = !profile.Locked;
        DeleteSetupButton.IsEnabled = !profile.Locked;
        AddModsButton.IsEnabled = !profile.Locked;
        RefreshModulesButton.IsEnabled = !profile.Locked;
        AutoOrderButton.IsEnabled = !profile.Locked;
        SaveModulesButton.IsEnabled = !profile.Locked;
        // The AD1259 edition has one fixed set and no mod features, so it shows no Locked tag or line.
        var showLocked = profile.Locked && !Edition.IsAD1259;
        LockedSetupTag.Visibility = showLocked ? Visibility.Visible : Visibility.Collapsed;
        LockedSetupText.Visibility = showLocked ? Visibility.Visible : Visibility.Collapsed;
        LockedSetupText.Text = BannerlordCoopLauncher.Core.Profile.LockedSetupNote.Line(profile.Name);
        foreach (var button in new Control[] { CopySetupButton, SaveSetupButton, DeleteSetupButton, AddModsButton, RefreshModulesButton, AutoOrderButton, SaveModulesButton })
            ShowLockedTip(button, profile.Locked);
        EnginePortBox.Text = profile.EnginePort.ToString();
        SettingsEnginePort.Text = profile.EnginePort.ToString();
        CoopPortBox.Text = profile.CoopPort.ToString();
        ServerUdpBox.Text = profile.CoopPort.ToString();
        SelectRegion(profile.Region);
        ServerAddressBox.Text = profile.ServerAddress;
        RefreshRbmCard(force: true);
        ShowCrashRestart(profile);
        OverviewProfile.Text = profile.Name;
        if (!string.IsNullOrWhiteSpace(profile.SaveName))
            OverviewSave.Text = profile.SaveName;
        // After the locked setup lines above, so a running server still keeps the mod buttons off.
        ApplyModListBlock(force: true);
    }

    private bool _addressReady;

    private void ServerAddress_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || !_addressReady)
            return;
        var profile = _settings.Current();
        var address = ServerAddressBox.Text.Trim();
        if (profile.ServerAddress == address)
            return;
        profile.ServerAddress = address;
        HostSettings.Save(SettingsPath, _settings);
    }

    private void CaptureProfile(HostProfile profile)
    {
        profile.Name = string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? profile.Name : ProfileNameBox.Text.Trim();
        profile.SaveName = SaveBox.SelectedItem as string ?? profile.SaveName;
        ApplyLaunchDefaults(profile);
        if (RegionBox.SelectedItem is string region)
            profile.Region = region;
        profile.ServerAddress = ServerAddressBox.Text.Trim();
        RememberPassword(profile);
        profile.ModuleOrder = _modules.Select(row => row.Id).ToList();
        profile.DisabledModules = _modules.Where(row => !row.Required && !row.Enabled).Select(row => row.Id).ToList();
    }

    private void Nav_Click(object sender, RoutedEventArgs e) => ShowPage((sender as Button)?.Tag as string ?? "Dashboard");

    private string _page = "Dashboard";

    // Every page of the window by its nav Tag. A new page is one line here plus its nav button in
    // NavButtons; page specific work when it opens goes in OnPageShown.
    private IReadOnlyList<(string Name, FrameworkElement Page)> Pages => new (string, FrameworkElement)[]
    {
        // The Dashboard opens first in both editions. Players, Map, Encyclopedia and Guides are CoopKeep style pages
        // of Coodsz Host; the AD1259 build has none of them.
        ("Dashboard", DashboardPage),
        ("Players", KeepPlayers),
        ("Map", KeepMap),
        ("Encyclopedia", KeepEncyclopedia),
        ("Guides", KeepGuides),
        // The Saves page keeps the key Server: SaveMods, ServerLog and AutoRescue call ShowPage("Server").
        ("Server", ServerPage),
        ("Realm", RealmPage),
        ("Modules", ModulesPage),
        ("Settings", SettingsPage),
        ("Logs", LogsPage),
        ("Console", ConsolePage),
        ("Cheats", KeepCheats),
    };

    private Button[] NavButtons => new[] { NavDashboard, NavServer, NavModules, NavPlayers, NavMap, NavEncyclopedia, NavSettings, NavLogs, NavCheats, NavConsole, NavGuides };

    private void ShowPage(string page)
    {
        _page = page;
        foreach (var (name, element) in Pages)
            element.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
        // The selected look (Keep.Nav) and the header title follow the page.
        Coodsz.Ui.NavRail.Select(NavButtons, page);
        PageTitle.Text = PageTitleOf(page);
        buttonForeground(page);
        OnPageShown(page);
    }

    private void OnPageShown(string page)
    {
        OnKeepPageShown(page);
        if (page == "Dashboard")
            RefreshDashboard();
        if (page == "Modules")
            DependencyNote.Text = ModuleCatalog.Validate(_modules.ToList());
        if (page == "Settings")
            RefreshRbmCard(reload: true);
        if (page == "Settings" && _mcmFiles.Count == 0)
            RefreshMcm_Click(this, new RoutedEventArgs());
        if (page == "Cheats")
        {
            KeepCheats.Shown();
        }
    }

    private void buttonForeground(string page)
    {
        // Keep.Nav colours the words; only the Logs button's crash pulse sets its own.
        foreach (var button in NavButtons)
            button.ClearValue(ForegroundProperty);
        if (_logAlertPulse.IsEnabled)
            NavLogs.Foreground = LogAlertText.Foreground;
    }

    // The header title is the page button's word; Realm has no page button, only the footer link.
    private static string PageTitleOf(string page) => page switch
    {
        "Modules" => "Mods",
        "Server" => "Saves",
        "Realm" => "Join my server",
        _ => page,
    };

    // The 64 px sidebar keeps the brand mark and the icons; the tool name and the AD1259 credit come back when it widens.
    // Send my logs and the footer buttons don't fit in 64 px, so an icon button and a More menu stand in for them.
    private void ShowCompactSidebar(bool compact)
    {
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        EditionCredit.Visibility = !compact && Edition.IsAD1259 ? Visibility.Visible : Visibility.Collapsed;
        SendLogsSlot.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SendLogsCompact.Visibility = compact && SendLogsSlot.Content != null ? Visibility.Visible : Visibility.Collapsed;
        FooterFit.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactMore.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        // The two icon buttons have no words, so screen readers get their names in the picked language.
        System.Windows.Automation.AutomationProperties.SetName(SendLogsCompact, BannerlordCoopLauncher.Core.Localization.Translator.T("Send my logs"));
        System.Windows.Automation.AutomationProperties.SetName(CompactMore, BannerlordCoopLauncher.Core.Localization.Translator.T("More"));
    }

    // The compact sidebar's More menu clicks the same buttons the wide footer shows.
    private void CompactMore_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = CompactMore, Placement = System.Windows.Controls.Primitives.PlacementMode.Right };
        void Add(string header, string tip, Action run)
        {
            var item = new MenuItem { Header = header, ToolTip = tip };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }
        void Press(object? button) => (button as Button)?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        // My reports sits under Send my logs in the wide sidebar (ReportRepliesUi.Attach), which the 64 px one hides.
        if (FindMyReports() is { } reports)
        {
            var unread = UnreadReports();
            Add(BannerlordCoopLauncher.Core.Localization.Translator.T(ReportReplyText.MyReports) + (unread > 0 ? " (" + unread + ")" : ""),
                ReportReplyText.MyReportsTip, () => Press(reports));
        }
        Add("Language", "Picks the language of this window.", () => Press(LanguageSlot.Content));
        Add("What's new", "Shows what changed in this version.", () => Press(WhatsNewSlot.Content));
        Add("Donate", "Opens Coodsz's donation page in your browser.", () => Press(DonateSlot.Content));
        Add("License", "Shows the license of the tools and the licenses of code from other projects.", () => Press(LicenseSlot.Content));
        Add("Join my server", "The Realm Bannerlord, my custom modded server", () => ShowPage("Realm"));
        if (StreamerButton.Visibility == Visibility.Visible)
            Add(StreamerText.Text, StreamerButton.ToolTip as string ?? "", () => StreamerMode_Click(StreamerButton, new RoutedEventArgs()));
        menu.IsOpen = true;
    }

    // The My reports button in the Send my logs slot, once it shows (a report was sent or answered).
    private Button? FindMyReports()
    {
        static Button? Find(DependencyObject node)
        {
            if (node is Button button && System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "MyReportsButton")
                return button;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                if (Find(child) is { } found)
                    return found;
            return null;
        }

        if (SendLogsSlot.Content is not DependencyObject content || Find(content) is not { } reports)
            return null;
        // The button and its ring stay collapsed until there is a report; the slot itself is hidden in the 64 px sidebar.
        for (DependencyObject? node = reports; node != null && !ReferenceEquals(node, SendLogsSlot); node = LogicalTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: Visibility.Collapsed })
                return null;
        return reports;
    }

    // The reports with an answer not read yet, for the More menu's My reports line.
    private static int UnreadReports()
    {
        try
        {
            var rows = ReportReplyStore.ForThisTool()?.Rows() ?? Array.Empty<MyReportRow>();
            return ReportReplies.UnreadReports(rows.Where(row => row.Answer != null).Select(row => row.Answer!));
        }
        catch (Exception)
        {
            // An unreadable store only leaves the count off the menu line.
            return 0;
        }
    }

    // The header pill: the state word in English (the language walk translates it) and its colour.
    private void ShowStatePill(bool running)
    {
        if (_engineExited && running)
            StatePill.Show(Coodsz.Ui.StatusKind.Crashed, "Engine exited");
        // The Dashboard's Restarting and Crashed states (HomeDashboard.State) show in the pill too.
        else if (_restartingNow || _restartingAfterCrash || (!running && CrashRestartWaits))
            StatePill.Show(Coodsz.Ui.StatusKind.Starting, "Restarting");
        else if (!running && _crashedSinceStart)
            StatePill.Show(Coodsz.Ui.StatusKind.Crashed, "Crashed");
        else if (!running)
            StatePill.Show(Coodsz.Ui.StatusKind.Stopped, "Stopped");
        else if (_session.Phase.IsShuttingDown())
            StatePill.Show(Coodsz.Ui.StatusKind.Stopping, "Stopping");
        else if (_serving)
            StatePill.Show(Coodsz.Ui.StatusKind.Online, "Server online");
        else
            StatePill.Show(Coodsz.Ui.StatusKind.Starting, "Starting");
    }

    private static string FormatUptime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return (int)elapsed.TotalHours + elapsed.ToString(@"\:mm\:ss");
    }

    // True while a mod copy runs inside LoadHost, before the profile, modules and address are read.
    private bool HalfLoaded => _copyingMods && !_addressReady;

    private void RefreshState()
    {
        if (HalfLoaded)
            return;
        var running = _session.IsRunning;
        if (_engineExited && running)
        {
            StateText.Text = "EXITED";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(158, 72, 68));
            StateDetail.Text = "Engine exited. Click Stop server to finish.";
        }
        else
        {
            StateText.Text = running ? (_session.Phase.IsShuttingDown() ? "STOPPING" : _serving ? "SERVER ONLINE" : "STARTING") : "STOPPED";
            StatusDot.Fill = running
                ? new SolidColorBrush(_serving ? Color.FromRgb(90, 138, 96) : Color.FromRgb(176, 132, 72))
                : new SolidColorBrush(Color.FromRgb(140, 90, 90));
            StateDetail.Text = running
                ? "Uptime " + (_session.StartedAt is DateTime started ? FormatUptime(DateTime.Now - started) : "-")
                : "Server is stopped.";
        }
        ShowStatePill(running);
        // The pill already says Stopped; the line beside it shows the uptime or the exit.
        StateDetail.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        var alive = running && !_engineExited;
        var load = _load.Line(alive, alive ? Path.GetDirectoryName(_session.ServerDataRoots().FirstOrDefault() ?? "") : null, _session.StartedProcessId);
        ServerLoadText.Text = load;
        _lastLoad = load;
        ServerLoadText.Visibility = load.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        GameTimeStatus.Text = alive ? (_playHold.Held ? PlayHeldText : "Server is running.") : running ? "Engine exited." : "Server is not running.";
        ShowGameSpeedRestart();
        RefreshRbmCard();
        ConsoleState.Text = _session.CanSendCommands
            ? "Server is running. Commands go to the running server."
            : _session.CommandBlockReason();
        RefreshPlayers();
        KeepLastGoodCopy();
        HoldBrokenAutosave();
        var profile = _settings.Current();
        OverviewProfile.Text = profile.Name;
        OverviewSave.Text = string.IsNullOrWhiteSpace(profile.SaveName) ? "-" : profile.SaveName;
        UpdateModuleSummary();
        OverviewPorts.Text = profile.EnginePort + " / " + profile.CoopPort;
        var coop = _modules.FirstOrDefault(row => row.Id.Equals("Coop", StringComparison.OrdinalIgnoreCase));
        ActiveCoop.Text = coop == null ? "" : BannerlordCoopLauncher.Core.Modules.CoopInstall.ActiveServerLine(coop.Version.Length > 0 ? coop.Name + " " + coop.Version : coop.Name, ServerRoot);
        // Both editions: the AD1259 set takes a 0.1.6 server Coop too, yet Coop still refuses 0.1.7 players on it.
        var behind = coop == null ? null : BannerlordCoopLauncher.Core.Modules.CoopInstall.ServerBehindLine(_gameCoop, coop.Version);
        // A chosen server folder older than the Workshop server: Use the Steam server is the first step, so only that line
        // shows. The behind line comes back afterwards if the game's Coop is newer than the Steam server too.
        var steamNewer = SteamServerState().Line;
        SteamServerText.Text = steamNewer ?? "";
        SteamServerLine.Visibility = steamNewer == null ? Visibility.Collapsed : Visibility.Visible;
        if (steamNewer != null)
            behind = null;
        CoopBehindText.Text = behind ?? "";
        CoopBehindLine.Visibility = behind == null ? Visibility.Collapsed : Visibility.Visible;
        // A server load test server stays off the router.
        KeepRouterPort(running && !_modTestRunning);
        RefreshKeepPages();
        ApplyModListBlock();
    }

    private const string VanillaMapId = "Vanilla";
    private const string VanillaMap = "Vanilla campaign Map";

    private bool _repairedWorkingOrder;

    private bool _enabledOnly;

    private static readonly string[] NeverOnServer =
    {
        "CoopNightly", "FamilyAppearanceEditor", "CoopMarriage", "CoopSycn"
    };

    private ReleaseOffer? _updateOffer;

    private string BanPath => Path.Combine(ServerRoot, "server-data", "steam-bans.json");

    private sealed class LivePlayer
    {
        public string Name { get; init; } = "";
        public string SteamId { get; init; } = "";
        public string Label => string.IsNullOrWhiteSpace(SteamId) ? Name : Name + "  " + SteamId;
    }
}
