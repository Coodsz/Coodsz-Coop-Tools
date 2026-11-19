using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Network;
using BannerlordCoopLauncher.Core.Steam;
using TaomCoop.Core;

namespace TaomCoopLauncher;

public partial class MainWindow : Window
{
    private readonly string _settingsPath = ToolData.File("taom-launcher.json");
    private TaomClientSettings _settings = new();
    private string? _gameRoot;
    private bool _busy;
    private bool _passwordVisible;
    private bool _loadingForm = true;
    private bool _connectReady;
    // Steam can start or close while this window is open, so its status is read again.
    private readonly DispatcherTimer _steamPoll = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _unblockNoted;
    private readonly HostActionLog _actions = new(() => ToolData.Folder, LogFolderOpen.ClientActionLogName);

    public MainWindow()
    {
        InitializeComponent();
        // A window bigger than the screen opened with its right side, and Install update, off screen.
        var fit = WindowFit.Fit(new WindowSize(Width, Height, MinWidth, MinHeight), SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        (Width, Height, MinWidth, MinHeight) = (fit.Width, fit.Height, fit.MinWidth, fit.MinHeight);
        LanguageSlot.Content = Coodsz.Ui.UiLanguage.MakeButton();
        WhatsNewSlot.Content = Coodsz.Ui.UiLanguage.MakeWhatsNewButton();
        DonateSlot.Content = Coodsz.Ui.UiLanguage.MakeDonateButton();
        StartFrame();
        Coodsz.Ui.NewBadges.Attach(GuidesButton, NewFeatures.Guides);
        SetUpModReports();
        LoadSettings();
        HostBox.Text = _settings.Host;
        PortBox.Text = string.IsNullOrWhiteSpace(_settings.Port) ? TaomStack.DirectPort : _settings.Port;
        PasswordHidden.Password = _settings.Password;
        PasswordBox.Text = _settings.Password;
        SteamBox.Text = _settings.SteamName;
        SetJoinMode(_settings.UseSteam);
        UpdateSteamStatus();
        Activated += (_, _) =>
        {
            UpdateSteamStatus();
            UpdateLowFpsButton();
        };
        _steamPoll.Tick += (_, _) => UpdateSteamStatus();
        _steamPoll.Start();
        if (string.IsNullOrWhiteSpace(_settings.TaomZip) || !File.Exists(_settings.TaomZip))
            _settings.TaomZip = FindPlaytestZip(wholePc: false) ?? NewestZip("TAOM*.zip", wholePc: false) ?? "";
        if (string.IsNullOrWhiteSpace(_settings.CompatZip) || !File.Exists(_settings.CompatZip))
            _settings.CompatZip = FindCompatZip(wholePc: false).Path ?? "";
        GameSearch? search = null;
        _gameRoot = GameFolderPick.ResolveSavedPath(_settings.GamePath, () => (search = BannerlordFinder.FindGameWithReason()).Root);
        ShowGameFolder();
        if (_gameRoot == null)
            Log(search?.Reason ?? GameSearch.GameMissingReason);
        // Before the rows are read: a Coop the last run left set aside (it crashed or was killed) goes back first.
        PutBackAfterCrash();
        // The setups and the mod list fill even with no game, so a folder picked later only adds the game.
        InitSetups();
        Closing += MainWindow_Closing;
        RefreshModules();
        _loadingForm = false;
        UpdateConnectButton();
        ApplyCrashGuards();
        Log("Playtest set: " + TaomStack.SetText + ".");
        Log("Enlistment and making a camp do not work in this playtest.");
        UpdateLowFpsButton();
        _ = WatchForUpdateAsync(ReleaseNotice.TaomRepo, ReleaseNotice.TaomVersion);
        Loaded += async (_, _) =>
        {
            if (_askSetAsideWhenLoaded)
            {
                _askSetAsideWhenLoaded = false;
                SetAsideExtraCoop();
            }
            await ShowOwnServerHintAsync(fill: UseDirect);
            await CheckMemoryAsync();
        };
    }

    private void ShowGameFolder()
    {
        GameText.Text = _gameRoot ?? GameFolderPick.NotFoundText;
        FindBannerlordButton.Visibility = _gameRoot == null ? Visibility.Visible : Visibility.Collapsed;
    }

    // A folder picked after startup: the game dependent work that had no game at startup runs again.
    private void ApplyGameFound(string root)
    {
        _gameRoot = root;
        ShowGameFolder();
        if (Process.GetProcessesByName("Bannerlord").Length == 0)
            RestoreDependencyFix();
        RefreshModules();
        UpdateConnectButton();
        ApplyCrashGuards();
        UpdateLowFpsButton();
    }

    private void FindBannerlord_Click(object sender, RoutedEventArgs e)
    {
        // A pick swaps the game folder, so it waits for an install, a download or a join check to finish.
        if (_busy || _joinChecking)
        {
            Log("Wait for the current action to finish.");
            return;
        }

        var dialog = new OpenFolderDialog { Title = BannerlordCoopLauncher.Core.Localization.Translator.T("Pick the Mount & Blade II Bannerlord folder"), InitialDirectory = GameFolderPick.DialogStartFolder(_settings.GamePath), Multiselect = false };
        if (dialog.ShowDialog(this) != true)
            return;
        var check = GameFolderPick.Use(dialog.FolderName, root =>
        {
            _gameRoot = root;
            SaveSettings();
            ApplyGameFound(root);
            Log(GameFolderPick.RememberedText);
        });
        if (check.Result != GameFolderPickResult.Ok)
            Coodsz.Ui.UiLanguage.Message(check.Message, "Find Bannerlord", MessageBoxButton.OK, MessageBoxImage.Warning, this);
    }

    private ReleaseOffer? _updateOffer;

    private void ShowUpdateNotice(ReleaseOffer offer)
    {
        _updateOffer = offer;
        UpdateNotice.Text = offer.Message;
        UpdateNoticeBar.Visibility = Visibility.Visible;
        Log(offer.Message);
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateOffer == null)
            return;
        InstallUpdateButton.IsEnabled = false;
        InstallUpdateButton.Opacity = 1;
        InstallUpdateButton.Content = "Installing...";
        ShowUpdateProgress("Installing the update. This window stays open until the download finishes.", null);
        await Task.Yield();
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                throw new InvalidOperationException("This copy cannot update itself.");
            var progress = new Progress<UpdateStep>(step => ShowUpdateProgress(step.Message, step.Fraction));
            await ToolUpdate.InstallAsync(_updateOffer, AppContext.BaseDirectory, exe, Environment.ProcessId, progress, CancellationToken.None);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateNotice.Text = ex.Message;
            UpdateProgress.Visibility = Visibility.Collapsed;
            InstallUpdateButton.Content = "Install update";
            InstallUpdateButton.ClearValue(OpacityProperty);
            InstallUpdateButton.IsEnabled = true;
        }
    }

    private void ShowUpdateProgress(string message, double? fraction)
    {
        UpdateNotice.Text = message;
        UpdateProgress.Visibility = Visibility.Visible;
        if (fraction is double value)
        {
            UpdateProgress.IsIndeterminate = false;
            UpdateProgress.Value = value;
        }
        else
        {
            UpdateProgress.IsIndeterminate = true;
        }
    }

    private async Task WatchForUpdateAsync(string repo, string version)
    {
        try
        {
            var offer = await ReleaseNotice.CheckOfferAsync(repo, version);
            if (offer != null)
                Dispatcher.Invoke(() => ShowUpdateNotice(offer));
        }
        catch
        {
        }
    }

    private async void Direct_Click(object sender, RoutedEventArgs e)
    {
        SetJoinMode(false);
        await ShowOwnServerHintAsync(fill: true);
    }

    private void Steam_Click(object sender, RoutedEventArgs e)
    {
        SetJoinMode(true);
        UpdateSteamStatus();
    }

    private void CheckMods_Click(object sender, RoutedEventArgs e)
    {
        // A Coop unzipped into Modules by hand: Windows' download marks cleared, a folder one level too deep named.
        // A misplaced Coop of another version than TAOM's gets no rename or move line, only TAOM's own line below.
        foreach (var line in CoopInstall.CheckModulesCoop(_gameRoot, TaomStack.CoopVersion))
            Log(line);
        RefreshModules();
        var problems = ModuleProblems();
        var setup = _settings.ModSetups.Current();
        var message = problems.Count > 0
            ? "These modules are missing or the wrong version: " + string.Join(", ", problems)
            : setup.HasEnabledExtras
                ? "Every module on " + setup.Name + " is ready. Check mods looks in the game's Modules folder and the Steam Workshop. It does not download anything."
                : "The sealed module list is ready. Check mods looks in the game's Modules folder. It does not download anything.";
        var coop = problems.Count > 0 ? CoopVersionProblem() : null;
        // When Coop is the only problem, the Play page shows why (Steam's Coop 0.1.7 needs the Coop 0.1.6 client from Coop's GitHub release).
        JoinStatus.Text = problems.Count == 1 && coop != null ? coop : message;
        Log(message);
        if (coop != null)
            Log(coop);
        if (!string.IsNullOrWhiteSpace(_gameRoot))
            Log(CoopStaging.DescribeCopies(Path.Combine(_gameRoot, "Modules")));
        if (BannerlordCoopLauncher.Core.Modules.UIExtenderExPrefabsFix.InLaunch(TaomLoadOrder.LaunchModules(setup, joinPassword: false)))
        {
            foreach (var line in BannerlordCoopLauncher.Core.Modules.UIExtenderExPrefabsFix.HealthProblems(ModRoots()))
                Log(line);
        }

        if (problems.Count == 0)
            ModChecksPass(setup);
    }

    private void ShowPassword_Click(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        if (_passwordVisible)
            PasswordBox.Text = PasswordHidden.Password;
        else
            PasswordHidden.Password = PasswordBox.Text;
        PasswordBox.Visibility = _passwordVisible ? Visibility.Visible : Visibility.Collapsed;
        PasswordHidden.Visibility = _passwordVisible ? Visibility.Collapsed : Visibility.Visible;
        ShowPasswordButton.Content = _passwordVisible ? "Hide" : "Show";
    }

    private void Unblock_Click(object sender, RoutedEventArgs e)
    {
        if (_gameRoot == null)
        {
            Log("Bannerlord was not found.");
            return;
        }

        var modules = Path.Combine(_gameRoot, "Modules");
        if (!Directory.Exists(modules))
        {
            Log("The Modules folder was not found.");
            return;
        }

        try
        {
            var cleared = DownloadMark.Clear(modules);
            var detail = cleared == 0
                ? "No files in Modules were blocked. Connect can start the game."
                : "Cleared the download block on " + cleared + " files in Modules. Connect can start the game.";
            _actions.Note("Unblock DLLs", detail);
            Log(detail);
            _settings.DllsUnblocked = true;
            SaveSettings();
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    // Windows marks downloaded files, and the game cannot load a marked DLL. The launcher clears
    // that mark on the Modules folder before every launch, so nobody has to remember Unblock DLLs.
    private async Task UnblockBeforeLaunchAsync()
    {
        if (_gameRoot == null)
            return;
        var modules = Path.Combine(_gameRoot, "Modules");
        if (!Directory.Exists(modules))
            return;
        try
        {
            var cleared = await Task.Run(() => DownloadMark.Clear(modules));
            if (cleared > 0)
            {
                var detail = "Cleared the Windows download block on " + cleared + " files in Modules before the launch.";
                _actions.Note("Unblock DLLs", detail);
                Log(detail);
            }

            if (!_settings.DllsUnblocked)
            {
                _settings.DllsUnblocked = true;
                SaveSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log("Could not clear the Windows download block in Modules (" + ex.Message + "). Open Settings and click Unblock DLLs if the game cannot load a DLL.");
        }
    }

    private void NoteBlockedDll(string? text)
    {
        if (_unblockNoted || !BlockedDllNotice.Matches(text))
            return;
        _unblockNoted = true;
        Log("Windows blocked a DLL. The launcher clears that block before every launch, so click Connect again. If it comes back, open Settings and click Unblock DLLs.");
    }

    // The More buttons open their menu on a normal click, under the button.
    private void OpenMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ClearShaders_Click(object sender, RoutedEventArgs e)
    {
        var programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord");
        try
        {
            var deleted = ShaderCache.Clear(programData, Process.GetProcessesByName("Bannerlord").Length > 0);
            Log(deleted == 0
                ? "There was no shader cache to delete."
                : "Deleted the shader cache. The game builds it again the next time it starts. Saves were not touched.");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private static void UnblockInstalledModules(string gameRoot, IProgress<string> progress)
    {
        var modules = Path.Combine(gameRoot, "Modules");
        if (!Directory.Exists(modules))
            return;
        var cleared = DownloadMark.Clear(modules);
        if (cleared > 0)
            progress.Report("Cleared the download block on " + cleared + " module files.");
    }

    private void ModuleFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_gameRoot == null)
        {
            Log("Bannerlord was not found.");
            return;
        }

        var modules = Path.Combine(_gameRoot, "Modules");
        if (!Directory.Exists(modules))
        {
            Log("The Modules folder was not found.");
            return;
        }

        Process.Start(new ProcessStartInfo(modules) { UseShellExecute = true });
        Log("Opened the Modules folder.");
    }

    // One button for the FPS fix: the patch shield flag file decides whether it is on now,
    // the same file Fix low FPS writes and Undo FPS fix deletes.
    private bool LowFpsFixOn => Patchshield.FlagPath(_gameRoot) != null;

    private void LowFps_Click(object sender, RoutedEventArgs e)
    {
        if (LowFpsFixOn)
            UndoFps_Click(sender, e);
        else
            FixFps_Click(sender, e);
    }

    private void UpdateLowFpsButton()
    {
        var on = LowFpsFixOn;
        LowFpsButton.Content = on ? "Low FPS fix: On" : "Low FPS fix: Off";
        if (on)
        {
            LowFpsButton.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x33, 0x28));
            LowFpsButton.Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0xE0, 0xA8));
            LowFpsButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x9B, 0xE0, 0xA8));
        }
        else
        {
            LowFpsButton.ClearValue(Control.BackgroundProperty);
            LowFpsButton.ClearValue(Control.ForegroundProperty);
            LowFpsButton.ClearValue(Control.BorderBrushProperty);
        }
    }

    private void FixFps_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gameRoot))
        {
            Log("Bannerlord was not found.");
            return;
        }

        try
        {
            var created = Patchshield.Disable(_gameRoot);
            Log(created
                ? "Turned off TAOM's patch shield. Close Bannerlord if it is open, then click Connect again. Click Low FPS fix again if this was a mistake."
                : "TAOM's patch shield is already off. Click Low FPS fix again if this was a mistake. Otherwise close Bannerlord and click Connect again.");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }

        UpdateLowFpsButton();
    }

    private void UndoFps_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gameRoot))
        {
            Log("Bannerlord was not found.");
            return;
        }

        try
        {
            var removed = Patchshield.Enable(_gameRoot);
            Log(removed
                ? "Turned TAOM's patch shield back on. Close Bannerlord if it is open, then click Connect again."
                : "TAOM's patch shield is already on.");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }

        UpdateLowFpsButton();
    }

    private void OpenClientLogs_Click(object sender, RoutedEventArgs e)
    {
        var clientLog = _gameRoot == null
            ? null
            : Path.Combine(_gameRoot, "bin", "Win64_Shipping_Client", "Coop_client.log");
        var actionLog = _actions.FilePath;
        _actions.Note("Open client logs", "Opened Coop_client.log and client-action.log.");
        if ((clientLog != null && File.Exists(clientLog)) || (actionLog != null && File.Exists(actionLog)))
        {
            LogFolderOpen.Select(new[] { clientLog, actionLog });
            Log("Opened Coop_client.log and client-action.log.");
        }
        else if (_gameRoot != null)
        {
            var bin = Path.Combine(_gameRoot, "bin", "Win64_Shipping_Client");
            Directory.CreateDirectory(bin);
            Process.Start(new ProcessStartInfo(bin) { UseShellExecute = true });
            Log("Opened the game log folder.");
        }
        else
        {
            var reports = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Mount and Blade II Bannerlord",
                "Coop Crash Reports");
            Directory.CreateDirectory(reports);
            Process.Start(new ProcessStartInfo(reports) { UseShellExecute = true });
            Log("Bannerlord was not found. Opened Coop Crash Reports.");
        }
    }

    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        var address = HostBox.Text.Trim();
        if (!ClientJoin.IsDirectAddress(address))
        {
            ShowJoin(SteamPanel.Visibility == Visibility.Visible
                ? "A Steam name cannot be checked this way. Fill the address and UDP port, then click Check server."
                : ClientJoin.IpRequired);
            return;
        }

        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            ShowJoin("Enter the UDP port the host set.");
            return;
        }

        var probe = await UdpServerProbe.ProbeAsync(address, port, UdpServerProbe.DefaultTimeout, CancellationToken.None);
        ShowJoin(ClientJoin.DescribeProbe(probe, address), probe.Reachability != ServerReachability.Online);
    }

    // One button for both: Install TAOM when TAOM or TAOM.CoopCompat is missing, the wrong
    // version, or Coop is not on TAOM's co-op list; otherwise Repair TAOM puts both in again.
    private async void InstallOrRepair_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginInstall())
            return;

        var gameRoot = _gameRoot!;
        bool install;
        try
        {
            install = await Task.Run(() => TaomArchive.NeedsInstall(gameRoot));
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            _busy = false;
            return;
        }

        // Both paths check _busy again, so it is handed back first; nothing runs in between.
        _busy = false;
        if (install)
            Install_Click(sender, e);
        else
            Repair_Click(sender, e);
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginInstall())
            return;

        var gameRoot = _gameRoot!;
        IProgress<string> progress = new Progress<string>(Log);
        // The NewTaomCompat refusal (a newer TAOM.CoopCompat kept, or its SubModule.xml locked) changed nothing, so only then
        // the module list is not read again; after any other failure it shows what is there now.
        var refused = false;
        await RunAsync(() =>
        {
            // The TAOM.CoopCompat v2 from the new TAOM download for 1.5.7 stops the whole step, before any module is replaced (CR-XXP67).
            if (NewTaomCompat.Problem(Path.Combine(gameRoot, "Modules", TaomStack.CompatModuleId)) is { } kept)
            {
                refused = true;
                throw new InvalidOperationException(kept);
            }
            var contentProblem = TaomArchive.ContentProblem(gameRoot);
            var compatProblem = TaomArchive.CompatProblem(gameRoot);
            if (contentProblem == null && compatProblem == null)
            {
                if (!TaomArchive.CoopIsListed(gameRoot))
                {
                    TaomArchive.EnsureCoopListed(TaomArchive.CoopModulesPath(gameRoot));
                    progress.Report("You already have TAOM " + TaomStack.TaomVersionText + " and TAOM.CoopCompat " + TaomStack.KitVersion + ". Coop was missing from TAOM's co-op module list, so that one line was added.");
                }
                else
                {
                    progress.Report("You already have TAOM " + TaomStack.TaomVersionText + " and TAOM.CoopCompat " + TaomStack.KitVersion + ". There is no need to install them again. Install or repair TAOM puts them in again from the zips if a module file is damaged.");
                }

                UnblockInstalledModules(gameRoot, progress);
                return;
            }

            progress.Report("Looking for the TAOM " + TaomStack.TaomVersionText + " archive and the TAOM.CoopCompat " + TaomStack.KitVersion + " client zip on this PC.");
            var taomZip = FindPlaytestZip(wholePc: true) ?? _settings.TaomZip;
            var compatPick = compatProblem != null ? FindCompatZip(wholePc: true) : CompatZipPick.Missing;
            var compatZip = compatPick.Path ?? "";
            if (contentProblem != null && !File.Exists(taomZip))
                throw new InvalidOperationException(contentProblem + " The TAOM " + TaomStack.TaomVersionText + " archive (zip) was not found on this PC. Download TAOM " + TaomStack.TaomVersionText + " and keep the zip in Downloads, or install its four modules into Bannerlord\\Modules by hand.");
            if (compatProblem != null && compatPick.From == CompatZipFrom.WriteFailed)
            {
                progress.Report("Could not write the built in " + TaomStack.ClientKitName + " to " + TaomCompatZip.Folder() + ". " + compatPick.Problem);
                throw new InvalidOperationException(compatProblem + " The built in copy of " + TaomStack.ClientKitName + " could not be written to " + TaomCompatZip.Folder() + ". Check that the drive has free space and the folder isn't blocked, then try again.");
            }
            if (compatProblem != null && !File.Exists(compatZip))
                throw new InvalidOperationException(compatProblem + " The " + TaomStack.ClientKitName + " was not found on this PC. Put it in Downloads.");

            _settings.TaomZip = taomZip;
            if (contentProblem != null)
            {
                progress.Report(contentProblem + " Replacing the TAOM modules from the zip.");
                TaomArchive.InstallContent(taomZip, gameRoot, progress);
            }

            if (compatProblem != null)
            {
                progress.Report(compatProblem + " Replacing TAOM.CoopCompat from the client zip.");
                TaomArchive.InstallCompat(compatZip, gameRoot, progress);
                _settings.CompatZip = compatZip;
            }

            UnblockInstalledModules(gameRoot, progress);
        });
        if (refused)
            return;
        RefreshModules();
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginInstall())
            return;

        var gameRoot = _gameRoot!;
        IProgress<string> progress = new Progress<string>(Log);
        // The NewTaomCompat refusal (a newer TAOM.CoopCompat kept, or its SubModule.xml locked) changed nothing, so only then
        // the module list is not read again; after any other failure it shows what is there now.
        var refused = false;
        await RunAsync(() =>
        {
            if (NewTaomCompat.Problem(Path.Combine(gameRoot, "Modules", TaomStack.CompatModuleId)) is { } kept)
            {
                refused = true;
                throw new InvalidOperationException(kept);
            }
            progress.Report("Looking for the TAOM " + TaomStack.TaomVersionText + " archive and the TAOM.CoopCompat " + TaomStack.KitVersion + " client zip on this PC.");
            var taomZip = FindPlaytestZip(wholePc: true) ?? _settings.TaomZip;
            var compatPick = FindCompatZip(wholePc: true);
            var compatZip = compatPick.Path ?? "";
            if (File.Exists(taomZip) && compatPick.From == CompatZipFrom.WriteFailed)
            {
                progress.Report("Could not write the built in " + TaomStack.ClientKitName + " to " + TaomCompatZip.Folder() + ". " + compatPick.Problem);
                throw new InvalidOperationException("The built in copy of " + TaomStack.ClientKitName + " could not be written to " + TaomCompatZip.Folder() + ". Check that the drive has free space and the folder isn't blocked, then try again.");
            }
            if (!File.Exists(taomZip) || !File.Exists(compatZip))
                throw new InvalidOperationException("The TAOM " + TaomStack.TaomVersionText + " archive or the TAOM.CoopCompat " + TaomStack.KitVersion + " client zip was not found on this PC. Put both in Downloads.");

            _settings.TaomZip = taomZip;
            _settings.CompatZip = compatZip;
            progress.Report("Replacing TAOM " + TaomStack.TaomVersionText + " and TAOM.CoopCompat " + TaomStack.KitVersion + " from the zips.");
            TaomArchive.InstallContent(taomZip, gameRoot, progress);
            TaomArchive.InstallCompat(compatZip, gameRoot, progress);
            UnblockInstalledModules(gameRoot, progress);
        });
        if (refused)
            return;
        RefreshModules();
    }

    private async void UseCoop_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginInstall())
            return;
        var workshop = CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries());
        if (workshop == null)
        {
            try
            {
                workshop = await SubscribeCoopAsync();
            }
            catch (Exception ex)
            {
                Log("Could not subscribe through Steam. " + ex.Message);
            }

            if (workshop == null)
            {
                _busy = false;
                return;
            }
        }

        // The click is the yes, so the other Coop is set aside without the question and goes back when the launcher closes.
        // It counts as this run's answer only once that worked: a newer Workshop Coop stops it before anything moves.
        var marker = _setAsideMarker;
        var used = false;
        await RunAsync(() =>
        {
            CoopStaging.UsePlaytestCoop(workshop, _gameRoot, new Progress<string>(Log), marker);
            used = true;
        });
        if (used)
            _setAsideThisRun = true;
        RefreshModules();
    }

    private async void RestoreCoop_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginInstall())
            return;
        await RunAsync(() => CoopStaging.RestoreKeptCoop(_gameRoot, new Progress<string>(Log)));
        RefreshModules();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    // A later Connect click (or Safe mode click) while one runs: TAOM CR-W4H9X started two games that way.
    public const string ConnectAlreadyRunning = "Connect is already running. Wait for the game to open and join the server.";
    public const string SafeModeNeedsPlainHost = "Safe mode only works when the host's server runs Plain TAOM too. If the host picked TAOM plus or a setup with extra mods, the server turns this join down.";

    private async Task ConnectAsync()
    {
        if (!_connect.TryBegin())
        {
            if (_connect.SearchAgain())
            {
                _actions.Note("Connect", "Clicked again while it looked for the Steam lobby, so the search started over.");
                Log("Looking for the Steam lobby again.");
            }
            else
            {
                _actions.Note("Connect", "Clicked again while a Connect was running. Nothing started.");
                Log(ConnectAlreadyRunning);
            }
            return;
        }

        try
        {
            await ConnectOnceAsync();
        }
        finally
        {
            _connect.End();
        }
    }

    // Held from the click until the game joined, was turned down or closed (OneConnect).
    private readonly OneConnect _connect = new();

    private async Task ConnectOnceAsync()
    {
        if (_busy)
        {
            Log("Wait for the current action to finish.");
            return;
        }
        // Not searched again here: the game found at startup also sets up mods and crash guards.
        if (_gameRoot == null)
        {
            Log("Bannerlord was not found.");
            return;
        }
        // A Coop unzipped into Modules by hand: Windows' download marks cleared, a folder one level too deep named.
        foreach (var line in CoopInstall.CheckModulesCoop(_gameRoot, TaomStack.CoopVersion))
            Log(line);
        // Connect starts the saved setup; changes on screen are kept or dropped first.
        if (!ConfirmModChanges())
            return;

        // The server needs the setup's extra mods. When some are not on this PC, one prompt gets them
        // and connects; with nothing missing Connect goes on as before.
        if (!await GetServerModsFirstAsync())
            return;

        var problems = ModuleProblems();
        if (problems.Count > 0)
        {
            // Coop on the wrong version is not missing: say which Coop TAOM needs (with a newer Workshop Coop, how to get the Coop 0.1.6 client).
            var coop = CoopVersionProblem();
            var others = coop == null ? problems : problems.Where(problem => !problem.StartsWith(TaomStack.CoopModuleId + " ", StringComparison.Ordinal)).ToList();
            var missing = others.Count == 0 ? null : "Install the missing modules first: " + string.Join(", ", others);
            // On lines of their own, so each stays a whole translated text.
            var message = coop == null ? missing! : BannerlordCoopLauncher.Core.Modules.CoopInstall.AddLine(coop, missing);
            JoinStatus.Text = message;
            Log(message);
            return;
        }

        if (!ModChecksPass(LaunchSetup))
            return;

        SaveSettings();
        var steam = SteamPanel.Visibility == Visibility.Visible;
        _actions.Note("Connect", steam ? "Steam lobby join." : "Direct join.");
        if (steam && !SteamNameReady())
            return;

        if (!steam && !ClientJoin.IsDirectAddress(HostBox.Text))
        {
            ShowJoin(ClientJoin.IpRequired);
            return;
        }

        if (!steam && (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535))
        {
            ShowJoin("Enter the UDP port the host set.");
            return;
        }

        // Direct: a wrong address used to start the game into a connect screen with no end, so the
        // server has to answer first. An answer goes straight on with no extra click.
        if (!steam && !await CheckDirectJoinAsync(HostBox.Text.Trim(), int.Parse(PortBox.Text.Trim())))
            return;

        try
        {
            if (!await Coodsz.Ui.GameAlreadyOpen.ClearAsync(this, ShowJoin))
                return;
            LogServerModuleNotes();
            WarnBigBattles(LaunchSetup);
            await CheckMemoryAsync();
            await UnblockBeforeLaunchAsync();
            RaiseLowTerrain();
            // Map shaders the game could not remove crash it on the map (AD1259 CR-TXY4V).
            await TerrainShaderCache.ClearAsync(names => Coodsz.Ui.UiLanguage.Message(TerrainShaderCache.Ask(names), TerrainShaderCache.AskTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, this) == MessageBoxResult.Yes, Log);
            ulong? lobbyId = null;
            if (steam)
            {
                ClearStoredAddress();
                // Keeps looking for up to 30 seconds; a second Connect click stops it and starts over, with the
                // Steam name in the box then.
                while (true)
                {
                    if (!SteamNameReady())
                        return;
                    var steamName = SteamBox.Text.Trim();
                    var search = _connect.BeginSearch();
                    try
                    {
                        lobbyId = await SteamLobbyFinder.FindWithRetryAsync(_gameRoot, steamName, Log, search);
                        // A click that came in just as the lobby was found still starts the search over.
                        search.ThrowIfCancellationRequested();
                        break;
                    }
                    // Also a search that failed just as the click stopped it: the click still starts it over.
                    catch (Exception) when (search.IsCancellationRequested && _connect.TakeSearchAgain())
                    {
                    }
                    finally
                    {
                        _connect.EndSearch();
                    }
                }
                Log("Found that Steam lobby. The game will join it. Lobby " + lobbyId + ".");
            }
            else
            {
                WriteDirectAddress(HostBox.Text.Trim());
            }
            ApplyCrashGuards();
            // The TAOM core in its sealed order, the setup's extra mods in their places, and the
            // join password module last. Plain TAOM gives exactly the sealed list.
            var setup = LaunchSetup;
            var modules = TaomLoadOrder.LaunchModules(setup, InstallJoinPassword());
            var joinKey = JoinMemory.Key(steam, SteamBox.Text, HostBox.Text, PortBox.Text);
            var lastJoin = JoinMemory.Problem(_settings.LastJoins.GetValueOrDefault(joinKey), modules);
            if (lastJoin != null)
                ShowJoin(lastJoin);
            if (setup.HasEnabledExtras)
                Log(setup.Name + " adds " + string.Join(", ", setup.EnabledExtras.Select(mod => mod.Label)) + ". The host has to load the same mods, or the server turns the join down.");
            var arguments = TaomLaunch.Arguments(connect: true, steam, CurrentPassword(), lobbyId, steam ? null : HostBox.Text.Trim() + ":" + PortBox.Text.Trim(), modules);
            var start = new ProcessStartInfo
            {
                FileName = GameRoot.ExecutablePath(_gameRoot),
                WorkingDirectory = GameRoot.WorkingDirectory(_gameRoot),
                Arguments = arguments,
                UseShellExecute = false
            };
            start.Environment[CoopPasswordPatch.VariableName] = CurrentPassword();
            if (!steam && SteamRegistry.GetActiveProcessId() <= 0)
                Log("Steam is not running. Direct connect can still start the game.");
            Log(CoopStaging.DescribeCopies(Path.Combine(_gameRoot, "Modules")));
            if (NonSteamCopy.Note(_gameRoot) is { } nonSteam)
                Log(nonSteam);
            // A Common.dll or 0Harmony.dll another mod left in the game's bin folder: a warning, the game still starts.
            foreach (var leftover in GameBinLeftovers.Notes(_gameRoot))
                Log(leftover);
            // The Activity box can be seen on a stream or screenshot, so the join password is cleaned out.
            Log(TaomLaunch.Shown(arguments, CurrentPassword()));
            ApplyDependencyFix(setup);
            var started = Process.Start(start);
            if (started == null)
                throw new InvalidOperationException("Bannerlord did not start.");
            Log("Bannerlord started.");
            _safeModeGameSetup = _safeMode.On ? setup : null;
            RestoreWhenClosed(started);
            // The header pill says Joining until the join result, then In game or Refused until the game closes.
            SetJoinState(ClientNextStep.Joining);
            var sessionExtras = SessionExtras(setup);
            try
            {
                await WatchJoinAsync(started, _gameRoot, setup, () =>
                {
                    RememberJoin(joinKey, modules);
                    _ = RecordSessionAsync(started, setup, sessionExtras, modules);
                });
            }
            finally
            {
                // After the end state, so a game that closed fast can't clear the pill before In game or Refused is set.
                ClearJoinStateWhenClosed(started);
            }
        }
        // A second click restarts the search inside the loop above, so this is only a search stopped some other way.
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            Log("Stopped looking for the Steam lobby.");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private bool SteamNameReady()
    {
        if (SteamHostName.IsValid(SteamBox.Text))
            return true;
        ShowJoin(string.IsNullOrWhiteSpace(SteamBox.Text)
            ? "Enter the host's Steam profile name, the name friends see in Steam (not the login name)."
            : "The host's Steam profile name can not contain a double quote.");
        return false;
    }

    // Coop's auto-connect sends an empty password on the first try, so a password server answers
    // "incorrect password". This module fills it from the launcher without editing Coop.Core.dll,
    // which TAOM.CoopCompat needs unchanged. It loads after TAOM.CoopCompat; Coop skips DedicatedServer.*
    // modules in the join check, so the server needs nothing.
    private bool InstallJoinPassword()
    {
        if (_gameRoot == null)
            return false;
        try
        {
            // A DLL the last Connect put there and is gone now was most likely taken by antivirus.
            if (ClientAddOns.Install(_gameRoot, ClientAddOns.JoinPasswordId, ClientAddOns.JoinPasswordName) is { } taken)
                Log(taken);
            DownloadMark.Clear(Path.Combine(_gameRoot, "Modules", ClientAddOns.JoinPasswordId));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log("Could not add " + ClientAddOns.JoinPasswordName + " (" + ex.Message + "). A password server may show \"incorrect password\" once before it lets you in.");
            return false;
        }
    }

    // setup is the one this game was started with, for the crash hint when it closes before it joins.
    private async Task WatchJoinAsync(Process started, string gameRoot, TaomSetup setup, Action? joined = null)
    {
        try
        {
            var joinLog = new ClientJoinLog(gameRoot, started.Id);
            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < deadline)
            {
                var exited = !ClientJoinLog.StillRunning(started);
                var text = joinLog.Read();
                if (JoinResultExplain.Succeeded(text))
                {
                    Log("Connected. The game is joining the TAOM server.");
                    joined?.Invoke();
                    SetJoinState(ClientNextStep.InGame);
                    return;
                }

                NoteBlockedDll(text);

                var lines = JoinResultExplain.Explain(text);
                if (lines.Count > 0)
                {
                    foreach (var line in lines)
                        Log(line);
                    SetJoinState(ClientNextStep.Refused);
                    return;
                }

                if (exited)
                {
                    Log("The game closed before it joined the server.");
                    await ExplainEarlyExitAsync(started, setup);
                    return;
                }

                await Task.Delay(2000);
            }

            // Three minutes without a result: the pill stays Joining until the game closes, or says Game open
            // when Coop wrote nothing to either client log (Coop 0.1.7 can leave it empty).
            if (joinLog.TimedOutState(ClientJoinLog.StillRunning(started)) == ClientNextStep.GameOpen)
            {
                Log(ClientJoinLog.EmptyLogNote);
                SetJoinState(ClientNextStep.GameOpen);
            }
        }
        catch (Exception ex)
        {
            Log("Could not read the join result. " + ex.Message);
        }
    }

    private void RefreshModules()
    {
        ScanInstalledExtras();
        ShowModuleList();
        // The Next step card reads the modules again (ShowNextStep).
        _attention = null;
        NoteWorkshopTwins();
        if (!_loadingForm)
            UpdateConnectButton();
    }

    // One locked core row, the same look as the sealed list had.
    private Border CoreRow(int number, string id)
    {
        var xml = _gameRoot == null ? null : FindModuleXml(_gameRoot, id);
        string version;
        string mark;
        if (xml == null)
        {
            version = "missing";
            mark = ModChips.Missing;
        }
        else
        {
            // An unreadable SubModule.xml (locked) shows as a wrong version instead of stopping the whole list.
            version = ModuleVersion(xml);
            var expected = Expected(id);
            var ready = expected == null || version.Equals(expected, StringComparison.OrdinalIgnoreCase);
            mark = ready ? ModChips.Ready : ModChips.WrongVersion;
        }

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 0), LastChildFill = true };
        var status = StatusChip(mark);
        DockPanel.SetDock(status, Dock.Right);
        // Always on, the Mods page word for a module the tool keeps on and in its place (ModChips.Role).
        var locked = new Coodsz.Ui.Chip
        {
            Text = ModChips.AlwaysOn,
            Kind = Coodsz.Ui.ChipKind.Mute,
            Margin = new Thickness(0, 0, 12, 0),
            Tip = "Part of the TAOM core. It always loads, in this place."
        };
        DockPanel.SetDock(locked, Dock.Right);
        var index = new TextBlock
        {
            Text = number.ToString(),
            Width = 28,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD2, 0xA3, 0x6A)),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(index, Dock.Left);
        // The same chips and Report link as the extra mods; an empty column keeps the rows in line.
        var chips = PackModChips(id, xml == null ? null : Path.GetDirectoryName(xml)) ?? BadgeColumn();
        DockPanel.SetDock(chips, Dock.Right);
        row.Children.Add(status);
        row.Children.Add(chips);
        // Always on goes first in the chip column, like the other chips (ModChips order), and the empty button and
        // switch slots keep this core row in line with the extra mod rows (ModRowLayout).
        locked.Margin = new Thickness(0, 1, 6, 1);
        ((Panel)chips).Children.Insert(0, locked);
        var actions = Coodsz.Ui.ModRowLayout.Slot(Coodsz.Ui.ModRowLayout.ClientActionColumn);
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        row.Children.Add(index);
        var switchSlot = Coodsz.Ui.ModRowLayout.Slot(Coodsz.Ui.ModRowLayout.SwitchColumn);
        DockPanel.SetDock(switchSlot, Dock.Left);
        row.Children.Add(switchSlot);
        row.Children.Add(new StackPanel
        {
            Margin = new Thickness(8, 0, 8, 0),
            Children =
            {
                new TextBlock { Text = id == TaomStack.CoopModuleId ? "Bannerlord Coop" : id, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE6, 0xE1)) },
                new TextBlock { Text = id != TaomStack.CoopModuleId ? version : InGameModules(xml) ? "Coop " + version + " (Modules)" : "Coop " + version + " (Workshop)", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x7D, 0xAA, 0xA4)) }
            }
        });
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x44)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
            Background = Brushes.Transparent,
            Child = row
        };
        Coodsz.Ui.ModRowLayout.FixHeight(border);
        return border;
    }

    private List<string> ModuleProblems()
    {
        var problems = CoreProblems();
        if (_gameRoot == null)
            return problems;
        // Extra mods of the saved setup (the one Connect starts) that are not on this PC.
        foreach (var mod in TaomSharedList.Missing(LaunchSetup, _installedExtras.Keys))
            problems.Add(mod.Label);
        return problems;
    }

    // Why the Coop on this PC is not the one the TAOM kit needs, else null. A copy in the game's Modules folder (Coop
    // 0.1.7 unzipped by hand) loads before the Workshop Coop, and Use Workshop Coop 0.1.6 moves it out, so that is the
    // advice; a wrong Workshop Coop gets CoopStaging.WrongWorkshopCoop. With the Workshop on a newer Coop too (0.1.7
    // since 2026-10-30) that button stops on the Workshop's version, so the Workshop's line comes first. workshopNewer
    // says the line is that newer Workshop Coop's, so Get Coop 0.1.6 shows (AttentionItems).
    private string? CoopVersionProblem() => CoopVersionProblem(out _);
    private string? CoopVersionProblem(out bool workshopNewer)
    {
        workshopNewer = false;
        var xml = _gameRoot == null ? null : FindModuleXml(_gameRoot, TaomStack.CoopModuleId);
        if (xml == null)
            return null;
        // A locked SubModule.xml gets a plain line, never the Workshop or Modules advice whose button would move a locked folder.
        if (ModuleIdentityReader.TryRead(xml) is not { } identity)
            return CoopUnreadable;
        if (CoopInstall.Compare(identity.Version, TaomStack.CoopVersion) == 0)
            return null;
        if (InGameModules(xml) && CoopStaging.NewerCoopProblem(CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries())) is { } newer)
        {
            workshopNewer = true;
            return newer;
        }
        if (InGameModules(xml))
            return CoopStaging.ModulesCoopNotPinned(identity.Version, Path.GetFileName(Path.GetDirectoryName(xml)!));
        workshopNewer = identity.Id.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) && CoopStaging.IsNewerCoop(identity.Version);
        return CoopStaging.WrongWorkshopCoop(identity.Id, identity.Version);
    }

    // True when FindModuleXml took the game's Modules folder copy, the one the game loads first.
    private bool InGameModules(string? xml) =>
        xml != null && _gameRoot != null
        && Path.GetFullPath(xml).StartsWith(Path.GetFullPath(Path.Combine(_gameRoot, "Modules")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // The TAOM core and Coop: Install or repair TAOM puts these in, so they keep Connect off.
    private List<string> CoreProblems()
    {
        var problems = new List<string>();
        if (_gameRoot == null)
            return problems;
        foreach (var id in TaomStack.ClientModules)
        {
            var xml = FindModuleXml(_gameRoot, id);
            if (xml == null)
            {
                problems.Add(id);
                continue;
            }

            var expected = Expected(id);
            if (expected == null)
                continue;
            var version = ModuleVersion(xml);
            if (!version.Equals(expected, StringComparison.OrdinalIgnoreCase))
                problems.Add(id + " " + version);
        }

        return problems;
    }

    private static string? Expected(string id)
    {
        if (TaomStack.ContentVersions.TryGetValue(id, out var version))
            return version;
        if (id == TaomStack.CoopModuleId)
            return TaomStack.CoopVersion;
        if (id == TaomStack.CompatModuleId)
            return TaomStack.CompatVersion;
        return null;
    }

    private void LogServerModuleNotes()
    {
        foreach (var note in TaomModNotes.For(LaunchSetup))
            Log(note);
        if (_gameRoot == null)
            return;
        foreach (var id in TaomStack.ClientModules)
        {
            if (ServerModuleNotes.KeepsClientCampaign(id))
                continue;
            var xmlPath = FindModuleXml(_gameRoot, id);
            if (xmlPath == null)
                continue;
            var note = ServerModuleNotes.SkippedCode(id, File.ReadAllText(xmlPath));
            if (note != null)
                Log(note);
        }
    }

    // What a module list shows for a SubModule.xml it can't read now (locked by the game or an antivirus): never a match.
    private const string UnreadableVersion = "unreadable";

    private const string CoopUnreadable = "The Coop SubModule.xml can't be read now. Close programs that may have it open, like the game or an antivirus scan, then try again.";

    // The version in a SubModule.xml, or UnreadableVersion when the file can't be read, so no check of the module list throws.
    private static string ModuleVersion(string xml) => ModuleIdentityReader.TryRead(xml)?.Version ?? UnreadableVersion;

    private static string? FindModuleXml(string gameRoot, string moduleId)
    {
        var modules = Path.Combine(gameRoot, "Modules");
        if (Directory.Exists(modules))
        {
            foreach (var directory in Directory.EnumerateDirectories(modules))
            {
                var xml = Path.Combine(directory, "SubModule.xml");
                if (!File.Exists(xml))
                    continue;
                // A locked SubModule.xml still matches by its folder name.
                var identity = ModuleIdentityReader.TryRead(xml);
                if ((identity?.Id ?? "").Equals(moduleId, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(directory).Equals(moduleId, StringComparison.OrdinalIgnoreCase))
                    return xml;
            }
        }

        var workshop = CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries());
        if (workshop == null)
            return null;
        var workshopXml = Path.Combine(workshop, "SubModule.xml");
        if (!File.Exists(workshopXml))
            return null;
        var workshopIdentity = ModuleIdentityReader.TryRead(workshopXml);
        // The Workshop folder is Coop's: a locked SubModule.xml there is still Coop's, so the locked file line can show for it.
        if (workshopIdentity == null)
            return moduleId.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) ? workshopXml : null;
        return workshopIdentity.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase) ? workshopXml : null;
    }

    private void SetJoinMode(bool steam)
    {
        DirectPanel.Visibility = steam ? Visibility.Collapsed : Visibility.Visible;
        SteamPanel.Visibility = steam ? Visibility.Visible : Visibility.Collapsed;
        _settings.UseSteam = steam;
        PaintMode(DirectButton, !steam);
        PaintMode(SteamButton, steam);
        if (steam)
            ShowSteamHint();
        else if (Coodsz.Ui.UiLanguage.English(JoinStatus) == ClientJoin.SteamNameHint)
            JoinStatus.Text = "";
        if (!_loadingForm)
            UpdateConnectButton();
        UpdateOwnServerNote();
    }

    private void JoinField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingForm)
            return;
        UpdateConnectButton();
        UpdateOwnServerNote();
    }

    private void UpdateConnectButton()
    {
        var steam = SteamPanel.Visibility == Visibility.Visible;
        var joinReady = steam
            ? SteamHostName.IsValid(SteamBox.Text)
            : ClientJoin.IsDirectAddress(HostBox.Text)
                && int.TryParse(PortBox.Text.Trim(), out var port)
                && port is >= 1 and <= 65535;
        // Missing extra mods leave Connect on: Connect offers to get them first.
        var enabled = _gameRoot != null && CoreProblems().Count == 0 && joinReady && !_joinChecking;
        ShowNextStep(enabled);
        if (_connectReady == enabled && ConnectButton.IsEnabled == enabled)
            return;

        _connectReady = enabled;
        ConnectButton.IsEnabled = enabled;
        if (enabled)
        {
            ConnectButton.Opacity = 1;
            ConnectButton.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x6B, 0x3A));
            ConnectButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x8F, 0xCB, 0x9B));
            ConnectButton.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xFB, 0xF6));
        }
        else
        {
            ConnectButton.Opacity = 0.55;
            ConnectButton.ClearValue(Control.BackgroundProperty);
            ConnectButton.ClearValue(Control.BorderBrushProperty);
            ConnectButton.ClearValue(Control.ForegroundProperty);
        }
    }

    private static void PaintMode(Button button, bool selected)
    {
        // The Keep look: the picked way has the gold edge of a selected choice, the other the plain dark button.
        button.Background = new SolidColorBrush(selected ? Color.FromRgb(0x1B, 0x28, 0x33) : Color.FromRgb(0x15, 0x1D, 0x24));
        button.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(0xE2, 0xC2, 0x7A) : Color.FromRgb(0x3A, 0x4C, 0x5C));
    }

    private string CurrentPassword() => _passwordVisible ? PasswordBox.Text : PasswordHidden.Password;

    private void UpdateSteamStatus()
    {
        var path = SteamRegistry.TryGetSteamPath();
        var running = SteamRegistry.GetActiveProcessId() > 0;
        SteamDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, running ? "Keep.Success" : "Keep.Amber");
        SteamStatusText.Text = path == null
            ? "Steam is not installed for this Windows user."
            : running ? "Steam is running." : "Steam is installed, and it is not running.";
    }

    private bool BeginInstall()
    {
        if (_busy)
        {
            Log("Wait for the current action to finish.");
            return false;
        }

        if (_gameRoot == null)
        {
            Log("Bannerlord was not found.");
            return false;
        }

        if (Process.GetProcessesByName("Bannerlord").Length > 0)
        {
            Log("Close Bannerlord first. Installing while the game is open can leave a module half replaced.");
            return false;
        }

        _busy = true;
        return true;
    }

    private void ShowJoin(string message, bool problem = true)
    {
        JoinStatus.Text = message;
        JoinStatus.Foreground = new SolidColorBrush(problem
            ? Color.FromRgb(0xD2, 0xA3, 0x6A)
            : Color.FromRgb(0x8F, 0xBF, 0x86));
        Log(message);
    }

    private void ShowSteamHint()
    {
        JoinStatus.Text = ClientJoin.SteamNameHint;
        JoinStatus.SetResourceReference(TextBlock.ForegroundProperty, "Keep.Accent");
    }

    private void ApplyCrashGuards()
    {
        if (string.IsNullOrWhiteSpace(_gameRoot))
            return;

        // Asks first, and the Coop goes back into Modules when this launcher closes (MainWindow.SetAside.cs).
        SetAsideExtraCoop();

        // Coop and Friends edits Coop.Core.dll for the join password in Modules and in the Workshop
        // folder. TAOM.CoopCompat refuses an edited file, and a Workshop only Coop loads from there.
        // A Coop of another version (the Workshop's 0.1.7) is left alone, and Connect says why.
        var folders = new List<string> { Path.Combine(_gameRoot, "Modules", "Coop") };
        if (CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries()) is { } workshop)
            folders.Add(workshop);
        foreach (var folder in folders)
        {
            var dll = Path.Combine(folder, "bin", "Win64_Shipping_Client", CoopPasswordPatch.FileName);
            if (!File.Exists(dll) || CoopStaging.IsOtherCoopVersion(folder))
                continue;
            try
            {
                if (CoopStaging.RestoreQualifiedDll(dll))
                    Log("Coop.Core.dll was put back to the released Coop 0.1.6 file. A password edit crashes TAOM.CoopCompat.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Log(ex.Message);
            }
        }
    }

    private void RaiseLowTerrain()
    {
        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
        ClientLaunchGuard.UnlockEngineConfig(documents);
        var path = Path.Combine(documents, "Configs", "engine_config.txt");
        if (!File.Exists(path))
            return;
        var updated = ClientJoin.RaiseLowTerrain(File.ReadAllText(path));
        if (updated == null)
            return;
        File.WriteAllText(path, updated);
        Log("Terrain quality was Low. It was raised to Medium so the loading screen is less likely to close.");
    }

    private void WriteDirectAddress(string address)
    {
        var path = CoopOptionsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var current = File.Exists(path) ? File.ReadAllText(path) : "{}";
        File.WriteAllText(path, ClientJoin.SetDirectAddress(current, address));
    }

    private void ClearStoredAddress()
    {
        var path = CoopOptionsPath();
        if (!File.Exists(path))
            return;
        var current = File.ReadAllText(path);
        var updated = ClientJoin.RemoveDirectAddress(current);
        if (updated == null)
            return;
        File.WriteAllText(path, updated);
        if (ClientJoin.RemoveNonIpAddress(current) != null)
            Log("Removed a saved address that was not an IP, so the game will not try to look that name up on the loading screen.");
    }

    private static string CoopOptionsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "Configs", "BannerlordCoop", "coop_options.json");

    // False when the work threw; the reason is in the log.
    private async Task<bool> RunAsync(Action work)
    {
        _busy = true;
        try
        {
            await Task.Run(work);
            return true;
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            return false;
        }
        finally
        {
            _busy = false;
            SaveSettings();
        }
    }

    private void PageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsDescendantOf(LogBox, source))
        {
            var logScroll = FindChildScrollViewer(LogBox);
            if (logScroll == null || logScroll.ScrollableHeight <= 0)
                return;
            ScrollByWheel(logScroll, e.Delta);
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is DependencyObject nested && ParentScrollViewer(nested) is ScrollViewer inner
            && !ReferenceEquals(inner, PageScroll)
            && inner.ScrollableHeight > 0)
            return;

        if (PageScroll.ScrollableHeight <= 0)
            return;
        ScrollByWheel(PageScroll, e.Delta);
        e.Handled = true;
    }

    private static void ScrollByWheel(ScrollViewer viewer, int delta)
    {
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0)
            return;
        if (lines < 0)
            lines = 3;
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - delta / 120.0 * lines * 24);
    }

    private static bool IsDescendantOf(DependencyObject parent, DependencyObject? node)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, parent))
                return true;
            node = ParentOf(node);
        }

        return false;
    }

    private static ScrollViewer? ParentScrollViewer(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is ScrollViewer viewer)
                return viewer;
            node = ParentOf(node);
        }

        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject node)
    {
        if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(node);
        return LogicalTreeHelper.GetParent(node);
    }

    private static ScrollViewer? FindChildScrollViewer(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer)
                return viewer;
            var nested = FindChildScrollViewer(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private void Log(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Log(line));
            return;
        }

        Coodsz.Ui.UiLanguage.AppendLog(LogBox, DateTime.Now.ToString("HH:mm:ss") + "  ", line, Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    // An old taom-launcher.json loads with every value kept; it gets the Plain TAOM setup.
    private void LoadSettings() => _settings = TaomClientSettings.Load(_settingsPath);

    private void SaveSettings()
    {
        _settings.GamePath = _gameRoot ?? "";
        _settings.Host = HostBox.Text.Trim();
        _settings.Port = PortBox.Text.Trim();
        _settings.Password = CurrentPassword();
        _settings.SteamName = SteamBox.Text.Trim();
        _settings.Save(_settingsPath);
    }

    // The client zip with the pinned hash on this PC, else the copy a release build carries (TaomCompatZip).
    // The quick search at startup only notes a zip on this PC and never writes out the carried copy.
    private static CompatZipPick FindCompatZip(bool wholePc) =>
        TaomCompatZip.Find(TaomCompatKit.Client, wholePc, useEmbedded: wholePc);

    private static string? FindPlaytestZip(bool wholePc)
    {
        var found = InspectPlaytestZips(PlaytestZipFinder.CommonFolders());
        if (found != null || !wholePc)
            return found;
        return InspectPlaytestZips(PlaytestZipFinder.FixedDrives());
    }

    private static string? InspectPlaytestZips(IEnumerable<string> roots)
    {
        foreach (var file in PlaytestZipFinder.Matches("*TAOM*.zip", roots))
        {
            var name = Path.GetFileName(file);
            if (name.Contains("CompatOnly", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                if (TaomArchive.Inspect(file).MatchesPlaytest)
                    return file;
            }
            catch (InvalidDataException)
            {
            }
            catch (IOException)
            {
            }
        }

        return null;
    }

    private static string? NewestZip(string pattern, bool wholePc) =>
        wholePc ? PlaytestZipFinder.NewestOnThisPc(pattern) : PlaytestZipFinder.NewestInCommonFolders(pattern);
}
