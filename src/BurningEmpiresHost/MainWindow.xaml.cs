using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresHost;

public partial class MainWindow : Window
{
    private readonly string _settingsPath = ToolData.File(BeServerFolders.SettingsFile);
    private const int MaxShownReplyLines = 250;
    private const int MaxReplyReadBytes = 96_000;
    private const string ReplyCutNotice = "Showing the first 250 lines. The rest stayed in the server log so this window would not freeze.";
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _unblockPulse = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _unblockBright;
    private HostSettings _settings = new();
    private Process? _process;
    // CPU and memory of the server engine, on the overview.
    private readonly ServerLoad _load = new();
    private string? _gameRoot;
    private string? _gameSearchReason;
    private bool _gameSearchFailureLogged;
    private bool _busy;
    private bool _autosaveHeld;
    private bool _spareUntilOnMap;
    private bool _saving;
    // Counts the server's "world saved to 'x.sav'" lines, so Stop waits for the whole save.
    private int _worldSavedCount;
    private volatile string _lastWorldSavedFile = "";
    private readonly ServerLogTail _logTail = new(200_000);
    private string _cachedLogText = "";
    private string _cachedLogPath = "";
    private long _cachedLogLength = -1;
    private string _followedLogPath = "";
    private long _followedLogLength;
    private DateTime _cachedLogWrite = DateTime.MinValue;
    private bool _seenServing;
    // The fatal @DS@ state event of this start, kept after the server exits until the next start.
    private ServerState? _fatalState;
    private readonly HashSet<string> _joinAdvice = new(StringComparer.Ordinal);
    private readonly BannerlordCoopLauncher.Core.Server.SharedSteamIdWatch _sharedSteamIds = new();
    private bool _blockedDllNoted;
    // The statuses of this run's reports that do not fail, each shown once.
    private readonly HashSet<string> _infoReportsNoted = new(StringComparer.Ordinal);
    private string _servingCheckedPath = "";
    private long _servingScanOffset;
    private string _playerListSignature = "";
    private string _playerSourcePath = "";
    private string _playerSourceText = "";
    private long _playerSourceLength = -1;
    private string? _replyCommand;
    private long _replyOffset = -1;
    private string _replyCarry = "";
    private bool _replySawEcho;
    private int _replyCount;
    private DateTime _replyDeadline;
    private bool _readingCommand;
    private TextBox? _battleSizeBox;
    private List<HostPlayer> _onlinePlayers = [];
    private readonly Dictionary<string, DateTime> _banKicks = new(StringComparer.OrdinalIgnoreCase);
    private HostActionLog _actions = null!;

    public MainWindow()
    {
        InitializeComponent();
        // A window bigger than the screen (1366x768 at 150 percent) opened with its right side and the Start server card off screen.
        var fit = WindowFit.Fit(new WindowSize(Width, Height, MinWidth, MinHeight), SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        (Width, Height, MinWidth, MinHeight) = (fit.Width, fit.Height, fit.MinWidth, fit.MinHeight);
        // Under 1000 px the sidebar shows icons only.
        Coodsz.Ui.NavRail.Fit(this, NavColumn, changed: ShowCompactSidebar);
        StartDashboard();
        StartModsPage();
        foreach (var box in new[] { LogFileBox, FastForwardBox, BattleSpeedLimitBox })
            ShowOnOff(box);
        LanguageSlot.Content = Coodsz.Ui.UiLanguage.MakeButton();
        WhatsNewSlot.Content = Coodsz.Ui.UiLanguage.MakeWhatsNewButton();
        DonateSlot.Content = Coodsz.Ui.UiLanguage.MakeDonateButton();
        LicenseSlot.Content = Coodsz.Ui.UiLanguage.MakeLicenseButton();
        SetUpCrashReports();
        Coodsz.Ui.NewBadges.Attach(NavGuides, NewFeatures.Guides, Coodsz.Ui.BadgePlace.Right);
        Coodsz.Ui.NewBadges.Attach(GameSpeedButton, NewFeatures.GameSpeed);
        Coodsz.Ui.NewBadges.Attach(GameSpeedBattleButton, NewFeatures.GameSpeed);
        _actions = new HostActionLog(() =>
            string.IsNullOrWhiteSpace(_settings.DataRoot) ? null : Path.Combine(_settings.DataRoot, "logs"));
        _unblockPulse.Tick += (_, _) => PulseUnblockButton();
        LoadSettings();
        FindGameRoot();
        LoadCompatList();
        ShowModSetups();
        SetUpModReports();
        Closing += HostModSetups_Closing;
        // Start-ModdedServer.ps1 always keeps server-config.json, Game Saves and the logs in
        // Documents\Mount and Blade II Bannerlord\CoopData\DedicatedServer, so that is the Data folder.
        _settings.DataRoot = BeHostFolders.ScriptDataRoot();

        LoadConfigIntoFields();
        LoadTimePermissions();
        RefreshGameSpeed();
        ReloadSaves();
        ShowBannedPlayers();
        ServerAddressBox.Text = _settings.ServerAddress ?? LocalAddress();
        ShowServerPath();
        BuildGameplaySettings();
        StartCheats();
        StartConsole();
        _timer.Tick += (_, _) => RefreshState();
        _timer.Start();
        SeedLogs();
        StartJoining();
        RefreshState();
        ShowPage(StartPage);
        NoteLog("Burning Empires host: " + BeStack.SetText + ".");
        NoteGameSearchFailure();
        // Until the first release is published the check finds no release and stays quiet.
        _ = WatchForUpdateAsync(ReleaseNotice.BeRepo, ReleaseNotice.BeVersion);
    }

    // The saved pick first, then the Steam libraries. Prepare server and Install mods ask again, so a
    // game installed after the tool opened is still found.
    private string? FindGameRoot()
    {
        if (_gameRoot != null)
            return _gameRoot;
        var saved = GameFolderPick.CheckSaved(_settings.GamePath);
        if (saved.Result == GameFolderPickResult.Ok)
        {
            ApplyGameFound(saved.Root!);
            return _gameRoot;
        }

        var search = BannerlordFinder.FindGameWithReason();
        if (search.Root != null)
        {
            ApplyGameFound(search.Root);
            return _gameRoot;
        }

        FindBannerlordButton.Visibility = Visibility.Visible;
        ShowServerPath();
        _gameSearchReason = search.Reason ?? GameSearch.GameMissingReason;
        // At startup the reason waits for SeedLogs, which replaces the log text.
        if (IsLoaded)
            NoteGameSearchFailure();
        return null;
    }

    private void NoteGameSearchFailure()
    {
        if (_gameSearchFailureLogged || _gameSearchReason == null)
            return;
        NoteLog(_gameSearchReason);
        _gameSearchFailureLogged = true;
    }

    private void ShowServerPath() =>
        PathText.Text = FindBannerlordButton.Visibility == Visibility.Visible
            ? GameFolderPick.NotFoundText
            : "Server folder: " + _settings.ServerRoot;

    // Only a folder the player picked is saved as GamePath; a Steam found one is looked up again next time.
    private void ApplyGameFound(string root)
    {
        // The mod catalogue reads the game's Modules folder, so one scanned before this game folder scans again.
        if (!string.Equals(_gameRoot, root, StringComparison.OrdinalIgnoreCase))
            _hostMods = null;
        _gameRoot = root;
        FindBannerlordButton.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(_settings.ServerRoot))
            _settings.ServerRoot = BeHostFolders.DefaultServerRoot(Path.GetPathRoot(root) ?? "C:\\");
        ShowServerPath();
    }

    private void FindBannerlord_Click(object sender, RoutedEventArgs e)
    {
        // A pick can set the server folder, so it waits like every other server folder action.
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }

        var dialog = new OpenFolderDialog { Title = BannerlordCoopLauncher.Core.Localization.Translator.T("Pick the Mount & Blade II Bannerlord folder"), InitialDirectory = GameFolderPick.DialogStartFolder(_settings.GamePath), Multiselect = false };
        if (dialog.ShowDialog(this) != true)
            return;
        var check = GameFolderPick.Use(dialog.FolderName, root =>
        {
            var hadServerRoot = !string.IsNullOrWhiteSpace(_settings.ServerRoot);
            _settings.GamePath = root;
            ApplyGameFound(root);
            SaveSettings();
            // The mod lists read the game's Modules folder, so they load again on every pick.
            ShowModSetups();
            // The default server folder was just made, so the server copies of the saves show now.
            if (!hadServerRoot)
                ReloadSaves();
            NoteLog(GameFolderPick.RememberedText);
        });
        if (check.Result != GameFolderPickResult.Ok)
            Coodsz.Ui.UiLanguage.Message(check.Message, "Find Bannerlord", MessageBoxButton.OK, MessageBoxImage.Warning, this);
    }

    private void Nav_Click(object sender, RoutedEventArgs e) => ShowPage((sender as Button)?.Tag as string ?? StartPage);

    /// <summary>The page the window opens on.</summary>
    private const string StartPage = "Dashboard";

    private string _page = StartPage;

    // Every page of the window by its nav Tag. A new page is one line here plus its nav button in NavButtons.
    private IReadOnlyList<(string Name, FrameworkElement Page)> Pages => new (string, FrameworkElement)[]
    {
        ("Dashboard", DashboardPage),
        // The Saves page keeps the key Server, so every ShowPage("Server") still opens it.
        ("Server", ServerPage),
        ("Realm", RealmPage),
        ("Modules", ModulesPage),
        ("Settings", SettingsPage),
        ("Cheats", CoopCheats),
        ("Console", ConsolePage),
        ("Logs", LogsPage),
        ("Guides", GuidesPage),
    };

    private Button[] NavButtons => new[] { NavDashboard, NavServer, NavModules, NavSettings, NavCheats, NavConsole, NavLogs, NavGuides };

    private void ShowPage(string page)
    {
        _page = page;
        foreach (var (name, element) in Pages)
            element.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
        // The selected look (Keep.Nav) and the header title follow the page.
        Coodsz.Ui.NavRail.Select(NavButtons, page);
        PageTitle.Text = PageTitleOf(page);
        if (page == "Dashboard")
            RefreshDashboard();
        if (page == "Settings")
            LoadDifficulty(onlyGreyed: true);
        if (page == "Guides" && GuidesPage.Content == null)
            GuidesPage.Content = new Coodsz.Ui.TaomGuidesPage(BannerlordCoopLauncher.Core.Keep.GuideLibrary.For(BannerlordCoopLauncher.Core.Keep.GuideSet.BeServer),
                "Short help for hosting Burning Empires with this tool. Pick a guide, or search.");

        if (page == "Logs")
            LogBox.ScrollToEnd();
        if (page == "Cheats")
        {
            var names = CurrentPlayerNames();
            CoopCheats.Shown(names);
        }
        if (page == "Console")
            ConsoleHint.Text = CommandPipeReady() ? "Commands go to the running server." : CheatBlockReason();
    }

    // The header title is the page button's word; Realm has no page button, only the footer link and the Dashboard card.
    private static string PageTitleOf(string page) => page switch
    {
        "Modules" => "Mods",
        "Server" => "Saves",
        "Realm" => "Join my server",
        _ => page,
    };

    private ReleaseOffer? _updateOffer;

    // The Keep text colours of the Settings rows the code builds (KeepColors.xaml: TextMain, Accent, TextDim).
    private static readonly SolidColorBrush Ivory = new(Color.FromRgb(0xD5, 0xDE, 0xE6));
    private static readonly SolidColorBrush SettingHeader = new(Color.FromRgb(0x8E, 0xB4, 0xD4));
    private static readonly SolidColorBrush SettingName = new(Color.FromRgb(0xD5, 0xDE, 0xE6));
    private static readonly SolidColorBrush SettingHint = new(Color.FromRgb(0x8E, 0xA0, 0xAE));

    private sealed class SealedModuleRow
    {
        public SealedModuleRow(int? number, string name, string note, Brush noteBrush)
        {
            Number = number?.ToString() ?? "";
            Name = name;
            Note = note;
            NoteBrush = noteBrush;
        }

        public string Number { get; }
        public string Name { get; }
        // Note stays English; this is what the row shows.
        public string Note { get; }
        public string NoteText => BannerlordCoopLauncher.Core.Localization.Translator.T(Note);
        public Brush NoteBrush { get; }

        // The list status chip of Coodsz Coop and Friends' mods page (an extra mod only), from the Burning Empires list.
        public BeStatusChip? Status { get; init; }
        public string BadgeText => Status == null ? "" : BannerlordCoopLauncher.Core.Localization.Translator.T(Status.Text);
        public string? BadgeNote => Status == null ? null : Translated(Status.Text + "\n" + Status.Tip);
        public Visibility BadgeVisibility => Status == null ? Visibility.Collapsed : Visibility.Visible;
        public Brush BadgeInk => ChipInk(Status?.Kind);
        public Brush BadgeBack => ChipBack(Status?.Kind);

        // What players reported (ModderLords' verdict, the count chips) and the Report link (MainWindow.ReportChips),
        // after this PC's result, like the TAOM server tool.
        public FrameworkElement? Reports { get; init; }

        // Coodsz Coop and Friends' chip colors in the Burning Empires palette (the Report brushes in MainWindow.xaml).
        private static Brush ChipInk(string? kind) => kind switch
        {
            BeModChips.Works => Chip(0x8F, 0xBF, 0x86),
            BeModChips.Broken => Chip(0xE0, 0x7A, 0x6E),
            BeModChips.Risky => Chip(0xE2, 0xC2, 0x7A),
            BeModChips.Reported => Chip(0x8E, 0xB4, 0xD4),
            _ => Chip(0x8E, 0xA0, 0xAE)
        };

        private static Brush ChipBack(string? kind) => kind switch
        {
            BeModChips.Works => Chip(0x1A, 0x33, 0x28),
            BeModChips.Broken => Chip(0x3A, 0x24, 0x22),
            BeModChips.Risky => Chip(0x3A, 0x34, 0x20),
            BeModChips.Reported => Chip(0x1C, 0x2A, 0x3A),
            _ => Chip(0x24, 0x30, 0x3A)
        };

        private static SolidColorBrush Chip(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

        private static string Translated(string text) =>
            string.Join("\n", text.Split('\n').Select(BannerlordCoopLauncher.Core.Localization.Translator.T));

        // This PC's own result (mod-results.json): Passed on your server, Worked for you, and so on.
        public BannerlordCoopLauncher.Core.Modules.ModResultPill? Mine { get; init; }
        public Visibility MineVisibility => Mine == null ? Visibility.Collapsed : Visibility.Visible;
        public Brush MineInk => new SolidColorBrush(Mine?.Good == false ? Color.FromRgb(0xE0, 0x8B, 0x84) : Color.FromRgb(0x8F, 0xCB, 0x9B));
        public Brush MineBackground => new SolidColorBrush(Mine?.Good == false ? Color.FromRgb(0x3A, 0x24, 0x22) : Color.FromRgb(0x1E, 0x33, 0x26));
        public double MineOpacity => Mine?.Older == true ? 0.5 : 1;

        // An extra mod row of an unlocked setup: the on and off switch and the move and remove buttons.
        public string? ModuleId { get; init; }
        public bool Editable { get; init; }
        public bool IsOn { get; init; }
        public Visibility EditVisibility => Editable ? Visibility.Visible : Visibility.Collapsed;
        // False while the server runs or starts (ModListBlock): the switch and the move and remove buttons stay off.
        public bool Blocked { get; init; }
        public bool CanEdit => Editable && !Blocked;

        // The hover text of the whole row (a Burning Empires core row says it always loads in this order).
        public string? Tip { get; init; }
        // The full mod name comes first, since a long name is cut off in its column; the name has no tooltip of its own,
        // which would hide this one.
        public string RowTip => Tip == null ? Name : Name + "\n" + BannerlordCoopLauncher.Core.Localization.Translator.T(Tip);
    }
}

public sealed class SaveChoice
{
    public SaveChoice(string fullPath, string place)
    {
        FullPath = fullPath;
        Place = place;
    }

    public string FullPath { get; }
    public string Name => Path.GetFileNameWithoutExtension(FullPath);
    public string Place { get; }
    // Place stays English for sorting; this is what the dropdown shows.
    public string PlaceText => BannerlordCoopLauncher.Core.Localization.Translator.T(Place);
    public override string ToString() => Name + "   " + Place;
    public int PlaceRank => Place == "Server copy" ? 0 : Place == "Single player copy" ? 1 : 2;
    // The save's campaign day from its header, so two saves with the same name can be told apart. Read once, when the
    // box first shows the row; empty when the header has no day.
    public string DayText => _dayText ??= BannerlordCoopLauncher.Core.Localization.Translator.T(
        TaomCoop.Core.SaveSwitchWarning.BoxDay(BannerlordCoopLauncher.Core.Saves.CampaignSaveInfoReader.Read(FullPath)?.Day));
    private string? _dayText;
}

public sealed class HostSettings
{
    public string ServerRoot { get; set; } = "";
    // A Coop dedicated server the host unzipped himself (Choose Coop server), the source Prepare server copies
    // from instead of the Workshop one while it holds a server and the Workshop's Coop is not newer. Empty: the Workshop.
    public string CoopServerFolder { get; set; } = "";
    public string DataRoot { get; set; } = "";
    // The Bannerlord folder picked with Find Bannerlord; empty when Steam finds it.
    public string GamePath { get; set; } = "";
    public string SaveFile { get; set; } = "";
    // The save the server last ran from a Start. Save settings only writes the box pick into server-config.json, so the
    // older save question at Start compares against this one.
    public string LastHostedSave { get; set; } = "";
    public string Password { get; set; } = "";
    public string? ServerAddress { get; set; }
    public int AutosaveMinutes { get; set; } = 15;
    public bool SteamRelay { get; set; }
    public string? AcceptedEulaSha256 { get; set; }
    // The mod setup players should join with: the locked Burning Empires setup
    // or one of the host's own.
    public BeSetupBook ModSetups { get; set; } = new();
    // A new campaign under this name on the next start (no .sav needed). Empty starts the picked save.
    public string NewCampaignName { get; set; } = "";
    // The Burning Empires downloads Install or repair Burning Empires used last, so their folder is searched again.
    public string BeArchive { get; set; } = "";
    public string BeHotfixArchive { get; set; } = "";
    // Open the server's UDP port on the router (UPnP or NAT PMP) while the server runs, like Coodsz Host.
    public bool OpenRouterPort { get; set; } = true;
    // Advertise through Steam was turned on once for a shared address (CGNAT); after that the host's own choice stays.
    public bool SteamLobbyAutoOn { get; set; }
    // The starred cheats of the Cheats page (their ids) and the last commands sent from the Console page.
    public List<string> FavouriteCheats { get; set; } = new();
    public List<string> RecentCommands { get; set; } = new();
    // The server folder whose server once reached SERVING, so Windows has asked about .NET Host
    // for it. Until then the Start server card shows BeFirewallHint.
    public string FirewallHintDoneFor { get; set; } = "";
}
