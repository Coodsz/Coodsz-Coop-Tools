using System.Collections.ObjectModel;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Nexus;
using BannerlordCoopLauncher.Core.Network;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using Microsoft.Win32;

namespace BannerlordCoopLauncher;

public partial class MainWindow : Window
{
    private readonly PhysicalFileSystem _files = new();
    private readonly ProfileClient _profiles = new();
    private readonly SteamWorkshopClient _workshop = new();
    private readonly ObservableCollection<ModuleRow> _rows = new();
    private LauncherSettings _settings = new();
    private ServerProfile? _profile;
    private ModuleResolution? _resolution;
    private string? _gameRoot;
    // True when the game folder came from Find Bannerlord (now or saved before), not from Steam.
    private bool _gamePicked;
    private CancellationTokenSource? _work;
    // True while RunBusyAsync runs an action. Only RunBusyAsync sets it; SetBusy just shows it on the buttons.
    private bool _busy;
    private bool _applyingMap;
    private bool _applyingSetup;
    private string _shownSetup = "Default";
    private string _missingNoted = "";
    private string _duplicatesNoted = "";
    private IReadOnlyList<BannerlordCoopLauncher.Core.Modules.WorkshopTwin> _twins = [];
    private readonly HostActionLog _actions = new(() => ToolData.Folder, LogFolderOpen.ClientActionLogName);
    private string _rejectedSetupName = "";
    private Point _dragStart;
    private ModuleRow? _dragRow;
    private bool _dragMoved;
    private bool _dragBlocked;
    private readonly Dictionary<string, HashSet<string>> _pendingMods = new(StringComparer.OrdinalIgnoreCase);
    private Storyboard? _savePulse;
    private ModSnapshot? _savedMods;
    private string? _savedModsSetup;
    private Window? _sharedGuide;
    private Button? _sharedGuideContinue;
    private Action? _sharedGuideAction;
    private readonly DispatcherTimer _unblockPulse = new() { Interval = TimeSpan.FromMilliseconds(450) };
    // Steam can start or close while this window is open, so its status is read again.
    private readonly DispatcherTimer _steamPoll = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _unblockBright;
    private bool _unblockNoted;

    public MainWindow()
    {
        InitializeComponent();
        // A window bigger than the screen opened with its right side, and Install update, off screen.
        var fit = WindowFit.Fit(new WindowSize(Width, Height, MinWidth, MinHeight), SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        (Width, Height, MinWidth, MinHeight) = (fit.Width, fit.Height, fit.MinWidth, fit.MinHeight);
        LanguageSlot.Content = Coodsz.Ui.UiLanguage.MakeButton();
        WhatsNewSlot.Content = Coodsz.Ui.UiLanguage.MakeWhatsNewButton();
        DonateSlot.Content = Coodsz.Ui.UiLanguage.MakeDonateButton();
        LicenseSlot.Content = Coodsz.Ui.UiLanguage.MakeLicenseButton();
        Coodsz.Ui.NewBadges.Attach(JoinHelpButton, NewFeatures.JoinHelp);
        Coodsz.Ui.NewBadges.Attach(SendLogsButton, NewFeatures.SaveLogs);
        Coodsz.Ui.NewBadges.Attach(RbmCoopFriendlyBox, NewFeatures.RbmCoopFriendly, Coodsz.Ui.BadgePlace.After);
        Title = LauncherBrand.Name;
        ModuleList.ItemsSource = _rows;
        _rows.CollectionChanged += (_, _) => RefreshMoveEdges();
        StartFrame();
        WatchCommunityCompat();
        _unblockPulse.Tick += (_, _) => PulseUnblockButton();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private string SettingsPath => ToolData.File("launcher.json");
    private string CachePath => ToolData.File("profile-cache.json");

    private async Task InitializeAsync()
    {
        try
        {
            _settings = LauncherSettings.Load(SettingsPath);
        }
        catch (Exception ex)
        {
            _settings = new LauncherSettings();
            Log(ex.Message);
        }

        _settings.EnsureSetups();
        GamePathBox.Text = _settings.GamePath;
        NexusKeyBox.Password = _settings.NexusApiKey;
        ShowMoreNationsSwitch();
        FillSetupList();
        ApplySetupToForm(_settings.Current());
        ProfileUrlBox.Text = string.IsNullOrWhiteSpace(_settings.ProfileUrl)
            || _settings.ProfileUrl == ProfileJson.DefaultProfileUrl
            ? ""
            : _settings.ProfileUrl;
        UpdateSteamStatus();
        Activated += (_, _) => UpdateSteamStatus();
        _steamPoll.Tick += (_, _) => UpdateSteamStatus();
        _steamPoll.Start();
        _ = WatchForUpdateAsync(ReleaseNotice.FriendsRepo, ReleaseNotice.FriendsVersion);
        await DetectGamePath(logWhenMissing: true);
        RememberSavedMods();
        // A UIExtenderEx SubModule.xml left changed by an earlier launch is put back.
        if (Process.GetProcessesByName("Bannerlord").Length == 0)
            RestoreUIExtenderExFix();
        await ShowOwnServerHintAsync(fill: UseDirect);
    }

    private ReleaseOffer? _updateOffer;

    private bool UseDirect => DirectPanel.Visibility == Visibility.Visible;

    private bool MoreNationsMapSelected =>
        MapBox.SelectedItem is ComboBoxItem item
        && item.Tag is string id
        && id.Equals("MoreNations", StringComparison.OrdinalIgnoreCase);

    private bool IncludeMoreNations
    {
        get
        {
            if (MapBox.SelectedItem is ComboBoxItem item && item.Tag is string id)
                return id.Equals("MoreNations", StringComparison.OrdinalIgnoreCase);
            return _settings.MoreNations;
        }
    }

    private const string OrderHintAfterCrash =
        "A crash was found. Press Auto dependency order to see if that order can fix it. Bannerlord Coop and the working list stay put. Then press Save mods and Test to main menu again.";

    private async Task RunBusyAsync(Func<CancellationToken, Task> action)
    {
        _work?.Cancel();
        var current = new CancellationTokenSource();
        _work = current;
        _busy = true;
        SetBusy(true);
        try
        {
            await action(current.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Launcher action stopped. Steam may continue existing downloads.");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_work, current))
            {
                _busy = false;
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        InstallButton.IsEnabled = !busy;
        CheckModsButton.IsEnabled = !busy;
        PlayCheckModsButton.IsEnabled = !busy;
        CheckServerButton.IsEnabled = !busy;
        var modulesReady = _profile != null
            && _gameRoot != null
            && _resolution != null
            && ModuleResolver.ConnectBlockReason(_resolution) == null;
        UpdateConnectButton();
        DiagnosticButton.IsEnabled = !busy && modulesReady;
        BlockReasonText.Text = busy || _resolution == null
            ? BlockReasonText.Text
            : _gameRoot == null
                ? "Install Bannerlord in Steam, then open this again."
                : BannerlordCoopLauncher.Core.Joining.ServerModNeeds.PromptCovers(_resolution)
                    ? "Some mods this setup needs are missing. Connect gets them first."
                    : ModuleResolver.ConnectBlockReason(_resolution) ?? "";
    }

    private bool _connectReady;

    private void UpdateConnectButton()
    {
        var joinReady = UseDirect ? TryReadDirect(out _, out _) : SteamHostName.IsValid(SteamNameBox.Text);
        var modulesReady = _profile != null
            && _gameRoot != null
            && _resolution != null
            && (ModuleResolver.ConnectBlockReason(_resolution) == null || BannerlordCoopLauncher.Core.Joining.ServerModNeeds.PromptCovers(_resolution));
        // Off while Connect checks that the server answers, so a second click does not start a second check.
        var enabled = modulesReady && joinReady && !_joinChecking;
        // The Next step card and the header pill follow every change, also when Connect itself stays as it was.
        ShowNextStep(enabled);
        if (_connectReady == enabled && ConnectButton.IsEnabled == enabled)
            return;

        _connectReady = enabled;
        ConnectButton.IsEnabled = enabled;
        if (enabled)
        {
            ConnectButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x6B, 0x3A));
            ConnectButton.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8F, 0xCB, 0x9B));
            ConnectButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0xFB, 0xF6));
        }
        else
        {
            ConnectButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            ConnectButton.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
            ConnectButton.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
        }
    }

    private bool _passwordVisible;

    private void ShowPassword_Click(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        if (_passwordVisible)
        {
            ServerPasswordVisibleBox.Text = ServerPasswordBox.Password;
            ServerPasswordBox.Visibility = Visibility.Collapsed;
            ServerPasswordVisibleBox.Visibility = Visibility.Visible;
            ShowPasswordButton.Content = "Hide";
            ServerPasswordVisibleBox.Focus();
        }
        else
        {
            ServerPasswordBox.Password = ServerPasswordVisibleBox.Text;
            ServerPasswordVisibleBox.Visibility = Visibility.Collapsed;
            ServerPasswordBox.Visibility = Visibility.Visible;
            ShowPasswordButton.Content = "Show";
            ServerPasswordBox.Focus();
        }
    }

    private string CurrentPassword() =>
        _passwordVisible ? ServerPasswordVisibleBox.Text : ServerPasswordBox.Password;

    private void Log(string message)
    {
        Coodsz.Ui.UiLanguage.AppendLog(LogBox, DateTime.Now.ToString("HH:mm:ss") + "  ", message, Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    private void NoteAction(string action, string detail)
    {
        _actions.Note(action, detail);
    }
}

internal enum ModChangeChoice { Stay, Save, Discard }

internal sealed class ModSnapshot
{
    private readonly List<CustomMod> _customMods;
    private readonly List<string> _order;
    private readonly List<string> _removed;
    private readonly List<string> _disabled;

    private ModSnapshot(ClientSetup setup)
    {
        _customMods = setup.CustomMods.Select(mod => new CustomMod { Name = mod.Name, ModuleId = mod.ModuleId, WorkshopId = mod.WorkshopId }).ToList();
        _order = setup.ModuleOrder.ToList();
        _removed = setup.RemovedModules.ToList();
        _disabled = setup.DisabledModules.ToList();
    }

    public static ModSnapshot Capture(ClientSetup setup) => new(setup);

    public void RememberDisabled(ClientSetup setup)
    {
        _disabled.Clear();
        _disabled.AddRange(setup.DisabledModules);
    }

    public bool Matches(ClientSetup setup) =>
        Same(_order, setup.ModuleOrder)
        && Same(_removed, setup.RemovedModules)
        && Same(_disabled, setup.DisabledModules)
        && _customMods.Select(mod => mod.ModuleId).SequenceEqual(setup.CustomMods.Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);

    public void Apply(ClientSetup setup)
    {
        setup.CustomMods.Clear();
        setup.CustomMods.AddRange(_customMods.Select(mod => new CustomMod { Name = mod.Name, ModuleId = mod.ModuleId, WorkshopId = mod.WorkshopId }));
        setup.ModuleOrder.Clear();
        setup.ModuleOrder.AddRange(_order);
        setup.RemovedModules.Clear();
        setup.RemovedModules.AddRange(_removed);
        setup.DisabledModules.Clear();
        setup.DisabledModules.AddRange(_disabled);
    }

    private static bool Same(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
}

public sealed class ModuleRow : INotifyPropertyChanged
{
    private int _order;
    public int Order
    {
        get => _order;
        set
        {
            if (_order == value)
                return;
            _order = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Order)));
        }
    }

    public bool IsPending { get; set; }

    private bool _isDragging;
    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging == value)
                return;
            _isDragging = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDragging)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string DisplayId { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusTip { get; set; } = "";
    // The chip colour of the English status word (ModChips.StatusKind), since Status is in the picked language.
    public string StatusKind { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool IsOn { get; set; }
    public bool CanToggle { get; set; }
    public bool CanRemove { get; set; }
    // Move up, Move down and dragging: off on a locked setup and for the mods that keep their spot.
    public bool CanMove { get; set; }

    // The arrows: the first movable row has no Move up and the last one no Move down (MainWindow.RefreshMoveEdges),
    // so an arrow that would do nothing is off instead.
    private bool _canMoveUp;
    public bool CanMoveUp
    {
        get => _canMoveUp;
        set
        {
            if (_canMoveUp == value)
                return;
            _canMoveUp = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanMoveUp)));
        }
    }

    private bool _canMoveDown;
    public bool CanMoveDown
    {
        get => _canMoveDown;
        set
        {
            if (_canMoveDown == value)
                return;
            _canMoveDown = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanMoveDown)));
        }
    }
    public string ToggleTip { get; set; } = "";

    // The community list badge (compat-list/mods.json); null for the official modules and Coop.
    private BannerlordCoopLauncher.Core.Modules.ModCompatBadge? _compat;
    public BannerlordCoopLauncher.Core.Modules.ModCompatBadge? Compat
    {
        get => _compat;
        set
        {
            if (Equals(_compat, value))
                return;
            _compat = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Compat)));
        }
    }

    // This PC's own result for the mod (mod-results.json): Worked for you or Crashed for you.
    private BannerlordCoopLauncher.Core.Modules.ModResultPill? _mine;
    public BannerlordCoopLauncher.Core.Modules.ModResultPill? Mine
    {
        get => _mine;
        set
        {
            if (Equals(_mine, value))
                return;
            _mine = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mine)));
        }
    }
}
