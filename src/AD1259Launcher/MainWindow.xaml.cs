using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Network;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using CoodszHost.Services;
using Microsoft.Win32;
using TaomCoop.Core;

namespace AD1259Launcher;

public partial class MainWindow : Window
{
    public const string LauncherName = "Coodsz AD1259 Client Launcher";
    private const string DirectPort = "4200";

    private readonly string _settingsPath = ToolData.File("ad1259-launcher.json");
    private LauncherSettings _settings = new();
    private string? _gameRoot;
    private IReadOnlyList<AD1259ModStatus> _mods = [];
    private bool _passwordVisible;
    private bool _loadingForm = true;
    private bool _connectReady;
    private readonly DispatcherTimer _unblockRed = new() { Interval = TimeSpan.FromMilliseconds(450) };
    // Steam can start or close while this window is open, so its status is read again.
    private readonly DispatcherTimer _steamPoll = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _unblockRedBright;
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
        LicenseSlot.Content = Coodsz.Ui.UiLanguage.MakeLicenseButton();
        StartFrame();
        SetUpModReports();
        _unblockRed.Tick += (_, _) => PulseUnblockRed();
        LoadSettings();
        HostBox.Text = _settings.Host;
        PortBox.Text = string.IsNullOrWhiteSpace(_settings.Port) ? DirectPort : _settings.Port;
        PasswordHidden.Password = _settings.Password;
        PasswordBox.Text = _settings.Password;
        SteamBox.Text = _settings.SteamName;
        var rbm = _settings.Rbm ?? new RbmSettings();
        RbmCombatBox.IsChecked = rbm.Combat;
        RbmAiBox.IsChecked = rbm.Ai;
        RbmCampaignBox.IsChecked = rbm.Campaign;
        RbmTournamentBox.IsChecked = rbm.Tournament;
        RbmCoopFriendlyBox.IsChecked = _settings.KeepRbmCoopFriendly;
        Coodsz.Ui.NewBadges.Attach(RbmCoopFriendlyBox, NewFeatures.RbmCoopFriendly, Coodsz.Ui.BadgePlace.After);
        SetJoinMode(_settings.UseSteam);
        UpdateSteamStatus();
        Activated += (_, _) => UpdateSteamStatus();
        _steamPoll.Tick += (_, _) => UpdateSteamStatus();
        _steamPoll.Start();
        SetText.Text = "For " + AD1259Stack.SetText + ". Every mod comes from the Steam Workshop or Nexus Mods; this launcher checks them and starts the game.";
        GameSearch? search = null;
        _gameRoot = GameFolderPick.ResolveSavedPath(_settings.GamePath, () => (search = BannerlordFinder.FindGameWithReason()).Root);
        ShowGameFolder();
        if (_gameRoot == null)
            Log(search?.Reason ?? GameSearch.GameMissingReason);
        // A UIExtenderEx SubModule.xml left changed by an earlier launch is put back, in the Workshop even with no game.
        if (Process.GetProcessesByName("Bannerlord").Length == 0)
            RestoreUIExtenderExFix();
        RefreshModules();
        _loadingForm = false;
        UpdateConnectButton();
        Log("AD1259 set: " + AD1259Stack.SetText + ".");
        LogModProblems();
        ShowUnblockFirst();
        _ = WatchForUpdateAsync(ReleaseNotice.AD1259Repo, ReleaseNotice.AD1259Version);
        Loaded += async (_, _) => await ShowOwnServerHintAsync(fill: !_settings.UseSteam);
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
            RestoreUIExtenderExFix();
        RefreshModules();
        UpdateConnectButton();
        LogModProblems();
    }

    private void FindBannerlord_Click(object sender, RoutedEventArgs e)
    {
        // A pick swaps the game folder, so it waits for the Workshop download or a join check to finish.
        if (_gettingMods || _joinChecking)
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
        foreach (var line in CoopInstall.CheckModulesCoop(_gameRoot))
            Log(line);
        RefreshModules();
        var problems = ModProblems();
        var message = problems.Count == 0
            ? "Every AD1259 mod is installed with the right version. Check mods looks in the game's Modules folder and the Workshop folders. It does not download anything."
            : problems.Count + " mod" + (problems.Count == 1 ? " needs" : "s need") + " fixing before Connect. See the list on the right.";
        ShowJoin(message, problems.Count > 0);
        LogModProblems();
        foreach (var line in UIExtenderExPrefabsFix.HealthProblems(UIExtenderExRoots()))
            Log(line);
    }

    private bool _gettingMods;

    // Get all missing: Steam subscribes and downloads the Workshop mods of the pack by itself, and the
    // Nexus mods open their page, since only the player can download those.
    private async void GetMissing_Click(object sender, RoutedEventArgs e)
    {
        if (_gettingMods)
        {
            Log("Steam is still getting the mods. Wait for it to finish.");
            return;
        }

        RefreshModules();
        var needs = BannerlordCoopLauncher.Core.Joining.ServerModNeeds.FromAD1259(_mods);
        var links = needs.Where(need => !need.Automatic && need.Link != null).Select(need => need.Link!).Distinct().ToList();
        var workshop = new Dictionary<ulong, string>();
        foreach (var need in needs.Where(need => need.Automatic))
        {
            var mod = AD1259Stack.ClientMods.FirstOrDefault(item => item.Id.Equals(need.Id, StringComparison.OrdinalIgnoreCase));
            if (mod?.WorkshopId != null && ulong.TryParse(mod.WorkshopId, out var id))
                workshop.TryAdd(id, need.Name);
        }

        if (links.Count == 0 && workshop.Count == 0)
        {
            Log(_mods.All(mod => mod.IsReady)
                ? "Nothing is missing."
                : "The mods that need fixing have no page to open. See the list on the right.");
            return;
        }

        foreach (var link in links)
            OpenLink(link);
        // A Workshop mod with the wrong version opens its page too: Steam only has its newest version.
        var nexus = links.Count(link => !link.Contains("steamcommunity.com", StringComparison.OrdinalIgnoreCase));
        if (nexus > 0)
            Log(nexus == 1
                ? "Opened the Nexus page. Download the exact version shown, unzip it into the game's Modules folder, then click Unblock DLLs and Check mods."
                : "Opened " + nexus + " Nexus pages. Download the exact version shown, unzip them into the game's Modules folder, then click Unblock DLLs and Check mods.");
        if (links.Count > nexus)
            Log("Opened the Workshop page of the mods with another version than the pack. Steam only has the newest version, so check the version on the right and get that one by hand.");
        if (workshop.Count == 0 || _gameRoot == null)
            return;

        _gettingMods = true;
        GetMissingButton.IsEnabled = false;
        try
        {
            Log("Asking Steam to subscribe and download " + string.Join(", ", workshop.Values) + ".");
            var download = await WorkshopDownload.RunAsync(_gameRoot, workshop, line => Dispatcher.Invoke(() => Log(line)), CancellationToken.None);
            if (download.SteamProblem != null)
                Log("Could not subscribe through Steam by itself. " + download.SteamProblem);
            var open = download.OpenByHand(workshop.Keys.ToList());
            if (open.Count > 0)
            {
                Log("Opening the Workshop pages. Subscribe there and wait for the download in Steam.");
                foreach (var id in open)
                {
                    OpenLink(ShellOpen.SteamWorkshopPage(id.ToString()));
                    await Task.Delay(400);
                }
            }
        }
        catch (Exception ex)
        {
            Log("Could not get the Workshop mods. " + ex.Message);
        }
        finally
        {
            _gettingMods = false;
            GetMissingButton.IsEnabled = true;
        }

        RefreshModules();
        var left = ModProblems();
        ShowJoin(left.Count == 0
            ? "Every AD1259 mod is installed with the right version."
            : left.Count == 1
                ? "One mod still needs fixing before Connect. See the list on the right."
                : left.Count + " mods still need fixing before Connect. See the list on the right.", left.Count > 0);
    }

    // The Full AD1259 mod list link on the mods card: MODS.md in the Coodsz-AD1259 repository.
    private void FullModList_Click(object sender, RoutedEventArgs e) => OpenLink(AD1259Stack.ModsPageUrl);

    private void OpenLink(string link)
    {
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("Could not open " + link + ". " + ex.Message);
        }
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
            ShowUnblockFirst();
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private void ShowUnblockFirst()
    {
        var show = _settings.DllsUnblocked ? Visibility.Collapsed : Visibility.Visible;
        UnblockFirstJoin.Visibility = show;
        UnblockFirstSetup.Visibility = show;
        if (_settings.DllsUnblocked)
        {
            StopBlockedDllAlert();
            return;
        }

        // A steady gold button (UnblockHighlight), like the update banner: no pulse.
        Log("Click Unblock DLLs before Connect. Windows blocks downloaded DLLs, and the game cannot load them until that block is cleared.");
    }

    private void NoteBlockedDll(string? text)
    {
        if (_unblockNoted || !BlockedDllNotice.Matches(text))
            return;
        _unblockNoted = true;
        Log("Windows blocked a DLL. Click the red Unblock DLLs button, then connect again.");
        if (!_unblockRed.IsEnabled)
            _unblockRed.Start();
        PulseUnblockRed();
    }

    private void StopBlockedDllAlert()
    {
        _unblockRed.Stop();
        UnblockButton.ClearValue(Control.BackgroundProperty);
        UnblockButton.ClearValue(Control.ForegroundProperty);
        UnblockButton.ClearValue(Control.BorderBrushProperty);
        UnblockButton.ClearValue(Control.BorderThicknessProperty);
        UnblockButton.ClearValue(Control.FontSizeProperty);
        UnblockButton.FontWeight = FontWeights.SemiBold;
    }

    private void PulseUnblockRed()
    {
        _unblockRedBright = !_unblockRedBright;
        var color = _unblockRedBright ? Color.FromRgb(176, 42, 36) : Color.FromRgb(92, 22, 20);
        UnblockButton.Background = new SolidColorBrush(color);
        UnblockButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 236, 232));
        UnblockButton.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 120, 108));
        UnblockButton.BorderThickness = new Thickness(3);
        UnblockButton.FontSize = 15;
        UnblockButton.FontWeight = FontWeights.Bold;
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

    // The small arrow buttons open their menu on a normal click, under the button.
    private void OpenMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
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

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_gameRoot == null)
            return;
        if (!_settings.DllsUnblocked)
        {
            ShowJoin("Click Unblock DLLs before the game starts. Windows blocks downloaded DLLs, and the game cannot load them until that block is cleared.");
            return;
        }

        // A Coop unzipped into Modules by hand: Windows' download marks cleared, a folder one level too deep named.
        foreach (var line in CoopInstall.CheckModulesCoop(_gameRoot))
            Log(line);
        RefreshModules();
        var problems = ModProblems();
        if (problems.Count > 0)
        {
            ShowJoin("Fix the mods first: " + string.Join(" ", problems));
            return;
        }

        SaveSettings();
        var steam = SteamPanel.Visibility == Visibility.Visible;
        _actions.Note("Connect", steam ? "Steam lobby join." : "Direct join.");
        if (steam && !SteamHostName.IsValid(SteamBox.Text))
        {
            ShowJoin(string.IsNullOrWhiteSpace(SteamBox.Text)
                ? "Enter the host's Steam profile name, the name friends see in Steam (not the login name)."
                : "The host's Steam profile name can not contain a double quote.");
            return;
        }

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
            RaiseLowTerrain();
            ApplyRbm();
            SetAsideOldShaderCaches();
            // Map shaders the game could not remove crash it on the AD1259 map (CR-TXY4V).
            await TerrainShaderCache.ClearAsync(names => Coodsz.Ui.UiLanguage.Message(TerrainShaderCache.Ask(names), TerrainShaderCache.AskTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, this) == MessageBoxResult.Yes, Log);
            var clientFix = InstallClientFix();
            var joinPassword = InstallJoinPassword();
            ulong? lobbyId = null;
            if (steam)
            {
                ClearStoredAddress();
                var steamName = SteamBox.Text.Trim();
                // Keeps looking for up to 30 seconds; a second Connect click stops it and starts over.
                _lobbySearch?.Cancel();
                _lobbySearch = new CancellationTokenSource();
                lobbyId = await SteamLobbyFinder.FindWithRetryAsync(_gameRoot, steamName, Log, _lobbySearch.Token);
                Log("Found that Steam lobby. The game will join it. Lobby " + lobbyId + ".");
            }
            else
            {
                WriteDirectAddress(HostBox.Text.Trim());
            }

            var arguments = TaomLaunch.Arguments(connect: true, steam, CurrentPassword(), lobbyId,
                steam ? null : HostBox.Text.Trim() + ":" + PortBox.Text.Trim(), AD1259Stack.LaunchModules(clientFix, joinPassword));
            var start = new ProcessStartInfo
            {
                FileName = GameRoot.ExecutablePath(_gameRoot),
                WorkingDirectory = GameRoot.WorkingDirectory(_gameRoot),
                Arguments = arguments,
                UseShellExecute = false
            };
            start.Environment[CoopPasswordPatch.VariableName] = CurrentPassword();
            if (SteamRegistry.GetActiveProcessId() <= 0)
                Log("Steam is not running. Start Steam and sign in first: the Workshop mods only load with Steam running.");
            if (NonSteamCopy.Note(_gameRoot) is { } nonSteam)
                Log(nonSteam);
            // A Common.dll or 0Harmony.dll another mod left in the game's bin folder: a warning, the game still starts.
            foreach (var leftover in GameBinLeftovers.Notes(_gameRoot))
                Log(leftover);
            // The Activity box can be seen on a stream or screenshot, so the join password is cleaned out.
            Log(TaomLaunch.Shown(arguments, CurrentPassword()));
            // A UIExtenderEx copy missing a DLL its Compiled Prefabs need gets them turned off
            // for this game only and put back when it closes (see UIExtenderExPrefabsFix).
            RestoreUIExtenderExFix();
            foreach (var line in UIExtenderExPrefabsFix.ApplyForLaunch(AD1259Stack.LaunchModules(clientFix, joinPassword), UIExtenderExRoots()))
                Log(line);
            var started = Process.Start(start);
            if (started == null)
                throw new InvalidOperationException("Bannerlord did not start.");
            RestoreUIExtenderExFixWhenClosed(started);
            Log("Bannerlord started. The first join with AD1259 can take up to 20 minutes.");
            // The header pill says Joining until the join result, then In game or Refused until the game closes.
            SetJoinState(ClientNextStep.Joining);
            try
            {
                // Something marks engine_config.txt read-only while the game runs; then every change in
                // the game's graphics settings fails with "Cannot create config data".
                ClientLaunchGuard.KeepEngineConfigWritable(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord"));
                await WatchJoinAsync(started, _gameRoot);
            }
            finally
            {
                // After the end state, so a game that closed fast can't clear the pill before In game or Refused is set.
                ClearJoinStateWhenClosed(started);
            }
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            Log("Stopped looking for the Steam lobby.");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private CancellationTokenSource? _lobbySearch;

    // A mod shader cache built for an older Bannerlord can crash the game at start. Steam brings the
    // Workshop file back on update, so this runs on every Connect. Nothing is deleted.
    private void SetAsideOldShaderCaches()
    {
        var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord", "logs");
        var modules = AD1259ShaderCaches.ToSetAside(_mods, AD1259ShaderCaches.Reported(logs));
        foreach (var move in ModuleShaderCache.SetAside(modules))
        {
            Log(move.Error == null
                ? "Set aside " + move.Name + "'s old shader cache (renamed to .coodsz-old). It was built for an older Bannerlord and can crash the game at start; the game builds its own."
                : "Could not set aside " + move.Name + "'s old shader cache: " + move.Error);
        }
    }

    // Copies SkyApolo's client module into Modules on every Connect, so an update of the launcher also
    // updates the module. Without it the game still joins; only the map-sound crash guard is missing.
    private bool InstallClientFix()
    {
        if (_gameRoot == null)
            return false;
        try
        {
            if (AD1259ClientFix.Install(_gameRoot) is { } taken)
                Log(taken);
            DownloadMark.Clear(Path.Combine(_gameRoot, "Modules", AD1259Stack.ClientFixModuleId));
            Log("Added " + AD1259Stack.ClientFixName + " " + AD1259Stack.ClientFixVersion + " after Coop. " + AD1259Stack.ClientFixCredit);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log("Could not add " + AD1259Stack.ClientFixName + " (" + ex.Message + "). The game starts without it.");
            return false;
        }
    }

    // Coop's auto-connect sends an empty password on the first try, so a password server answers
    // "incorrect password". This module fills it from the launcher; without it the join still works
    // on Coop's retry. It also carries the settings sync client (CoodszSettingsSync.Coop.dll): while connected the game
    // uses the host's gameplay MCM values in memory, and the player's own files are never changed.
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

    // RBM reads its own config.xml, not MCM, in this PC's game (the Coop server never runs RBM's code).
    // Write the four parts and, with Keep RBM coop friendly on, turn off Lords buy equipment before the
    // game starts. Coop and Friends takes the same path (RbmSwitches.ApplyForPlayer).
    private void ApplyRbm()
    {
        _settings.Rbm = CurrentRbm();
        SaveSettings();
        try
        {
            var line = RbmSwitches.ApplyForPlayer(AD1259Stack.ClientModules, _settings.KeepRbmCoopFriendly, _settings.Rbm);
            Log(line ?? "RBM: " + RbmSwitches.Describe(_settings.Rbm) + ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException or InvalidDataException)
        {
            Log("Could not write RBM's settings (" + ex.Message + "). RBM uses whatever its config.xml already says.");
        }
    }

    private RbmSettings CurrentRbm() => new()
    {
        Combat = RbmCombatBox.IsChecked == true,
        Ai = RbmAiBox.IsChecked == true,
        Campaign = RbmCampaignBox.IsChecked == true,
        Tournament = RbmTournamentBox.IsChecked == true
    };

    private async Task WatchJoinAsync(Process started, string gameRoot)
    {
        try
        {
            var joinLog = new ClientJoinLog(gameRoot, started.Id);
            var engineLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Mount and Blade II Bannerlord", "logs", "rgl_log_" + started.Id + ".txt");
            var engineOffset = 0L;
            var engineRead = new StringBuilder();
            var screens = new StringBuilder();
            var watch = new LoadingWatch();
            var clock = Stopwatch.StartNew();
            var stalledNoted = false;
            var connectedNoted = false;
            // The first AD1259 join builds shaders and downloads a large world.
            var deadline = DateTime.UtcNow.AddMinutes(45);
            LoadingPanel.Visibility = Visibility.Visible;
            LoadingPanel.BringIntoView();
            while (DateTime.UtcNow < deadline)
            {
                var exited = !ClientJoinLog.StillRunning(started);
                var text = joinLog.Read();
                engineRead.Clear();
                AppendNewLog(engineLog, ref engineOffset, engineRead);
                var screenLines = LoadingWatch.ScreenLines(engineRead.ToString());
                if (screenLines.Length > 0)
                    screens.Append(screenLines).Append('\n');
                // Coop's own log stays empty on some PCs; the engine log's screen names still show the stage.
                var progress = text + "\n" + screens;
                // Connecting comes long before the world arrives, so keep watching until the game is in.
                if (!connectedNoted && JoinResultExplain.Succeeded(text))
                {
                    connectedNoted = true;
                    Log("Connected. The server is sending its world; the first time this can take a while.");
                }

                if (LoadingWatch.InGame(progress))
                {
                    ShowLoadingDone("In the game · " + LoadingWatch.Clock(clock.Elapsed), "Bannerlord finished loading and joined the AD1259 server.");
                    Log("The game finished loading and joined the AD1259 server.");
                    SetJoinState(ClientNextStep.InGame);
                    _ = WatchAfterGameAsync(started);
                    return;
                }

                NoteBlockedDll(text);

                var lines = JoinResultExplain.Explain(text);
                if (lines.Count > 0)
                {
                    ShowLoadingDone("The join stopped", lines[0], problem: true);
                    foreach (var line in lines)
                        Log(line);
                    SetJoinState(ClientNextStep.Refused);
                    return;
                }

                if (exited && !screens.ToString().Contains("TopScreen:", StringComparison.Ordinal))
                {
                    // Closed before the main menu: usually a one-off engine crash while loading, which a
                    // second start gets past.
                    ShowLoadingDone("The game closed while starting",
                        "Bannerlord closed before it reached the main menu. This sometimes happens while it loads; click Connect again. If it keeps happening, click Send my logs and post the code in Discord.",
                        problem: true);
                    Log("The game closed while starting. Click Connect again; a second start usually gets past this.");
                    await ExplainEarlyExitAsync(started);
                    return;
                }

                if (exited)
                {
                    ShowLoadingDone("The game closed", "Bannerlord closed before it joined the server. The activity log on the Logs and help page says more.", problem: true);
                    Log("The game closed before it joined the server.");
                    await ExplainEarlyExitAsync(started);
                    return;
                }

                var report = watch.Update(new LoadingSample(clock.Elapsed, CpuTime(started), FileLength(engineLog), joinLog.Bytes), progress.Trim());
                ShowLoading(report);
                if (report.Stalled && !stalledNoted)
                {
                    stalledNoted = true;
                    Log(report.Title + ". " + report.Detail);
                }
                else if (!report.Stalled)
                {
                    stalledNoted = false;
                }

                await Task.Delay(2000);
            }

            ShowLoadingDone("Stopped watching", "The game is still open after 45 minutes. If it is stuck, close it and click Send my logs.", problem: true);
            // The pill stays Joining until the game closes, or says Game open when Coop wrote nothing to either client log.
            if (joinLog.TimedOutState(ClientJoinLog.StillRunning(started)) == ClientNextStep.GameOpen)
            {
                Log(ClientJoinLog.EmptyLogNote);
                SetJoinState(ClientNextStep.GameOpen);
            }
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            Log("Could not read the join result. " + ex.Message);
        }
    }

    private void ShowLoading(LoadingReport report)
    {
        PaintLoading(report.Stalled);
        LoadingTitle.Text = report.Title;
        LoadingDetail.Text = report.Detail;
        LoadingBar.IsIndeterminate = true;
        LoadingBar.Visibility = Visibility.Visible;
        LoadingAdvice.Visibility = report.Stalled ? Visibility.Collapsed : Visibility.Visible;
    }

    // The Keep colours of the loading box: green while it works, amber when it stalls or ends with a problem.
    private void PaintLoading(bool problem)
    {
        LoadingPanel.SetResourceReference(Border.BorderBrushProperty, problem ? "Keep.Amber" : "Keep.Success");
        LoadingPanel.SetResourceReference(Border.BackgroundProperty, problem ? "Keep.AmberSoft" : "Keep.SuccessSoft");
        LoadingTitle.SetResourceReference(TextBlock.ForegroundProperty, problem ? "Keep.Amber" : "Keep.Success");
    }

    private void ShowLoadingDone(string title, string detail, bool problem = false)
    {
        PaintLoading(problem);
        LoadingTitle.Text = title;
        LoadingDetail.Text = detail;
        LoadingBar.IsIndeterminate = false;
        LoadingBar.Visibility = Visibility.Collapsed;
        LoadingAdvice.Visibility = Visibility.Collapsed;
    }

    private static TimeSpan CpuTime(Process process)
    {
        try
        {
            process.Refresh();
            return process.TotalProcessorTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return TimeSpan.Zero;
        }
    }

    private static long FileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static void AppendNewLog(string path, ref long offset, StringBuilder collected)
    {
        if (!File.Exists(path))
            return;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length < offset)
            offset = 0;
        stream.Position = offset;
        using var reader = new StreamReader(stream);
        var added = reader.ReadToEnd();
        offset = stream.Position;
        if (added.Length > 0)
            collected.Append(added);
    }

    private void RefreshModules()
    {
        ModulePanel.Children.Clear();
        if (_gameRoot == null)
        {
            _mods = [];
            ShowMadeFor();
            return;
        }

        _mods = AD1259ModScan.Check(Path.Combine(_gameRoot, "Modules"), WorkshopRoots());
        ShowMadeFor();
        var number = 1;
        foreach (var status in _mods)
        {
            ModulePanel.Children.Add(ModuleRow(number++, status));
            if (status.Mod.Id == "ExpandedArmouryBL" && AD1259ModScan.HasArmouryFix(status) == false)
                ModulePanel.Children.Add(Note("Expanded Armoury is missing its crash fix (" + AD1259Stack.ArmouryFixDll
                    + "). Download the file named FIX from the mod's Nexus page (Files tab) and copy it over Modules\\ExpandedArmouryBL.", status.Mod.Link));
            if (status.Mod.Id == UIExtenderExPrefabsFix.ModuleId && status.Directories.Count > 0 && UIExtenderExPrefabsFix.HealthProblem(status.Directories[0]) is { } incomplete)
                ModulePanel.Children.Add(Note(incomplete, status.Mod.Link));
            // Which Open Source set this PC has (2.0 or 1.x, or a mix), under the last of the three: the server needs the same.
            // The missing textures line is the log's, once per run, so a refresh doesn't repeat it here.
            if (status.Mod.Id == OpenSourceSet.Ids[^1] && OpenSourceSet.PcLine(_mods, withTextures: false) is { } openSource)
                ModulePanel.Children.Add(Note(openSource, null));
        }

        NoteWorkshopTwins();
        if (!_loadingForm)
            UpdateConnectButton();
    }

    private Border ModuleRow(int number, AD1259ModStatus status)
    {
        var (mark, kind) = RowStatus(status);
        var row = new DockPanel { LastChildFill = true };
        // Fixed slots (ModRowLayout): Get it, the chip column and the status keep their width on every row, so a row
        // without Get it or without chips lines up with the others.
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Button? get = null;
        if (!status.IsReady && status.Mod.Link != null)
        {
            var link = status.Mod.Link;
            get = new Button { Content = "Get it", Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 10, 0), ToolTip = link };
            get.Click += (_, _) => OpenLink(link);
        }

        right.Children.Add(Coodsz.Ui.ModRowLayout.Slot(GetItColumn, get));
        var chipColumn = new WrapPanel { Width = Coodsz.Ui.ModRowLayout.ClientChipColumn, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        // A TaleWorlds module of the pack says so; the community list doesn't rate those.
        if (status.Mod.BaseGame)
            chipColumn.Children.Add(new Coodsz.Ui.Chip { Text = ModChips.Official, Kind = Coodsz.Ui.ChipKind.Mute, Tip = ModChips.OfficialTip, Margin = new Thickness(0, 1, 6, 1) });

        // What players of the Coodsz tools reported for this pack mod, as green and red count chips.
        if (CommunityCounts(status.Mod) is { } chips)
        {
            chips.Margin = new Thickness(0, 1, 6, 1);
            chipColumn.Children.Add(chips);
        }

        // The small Report link: the report window for this pack mod only.
        if (ModReports.HasEndpoint() && ModReports.Rates(status.Mod.Id, status.Mod.BaseGame))
        {
            var id = status.Mod.Id;
            var report = Coodsz.Ui.ModReportUi.RowLink(this, () => Coodsz.Ui.ModReportWindow.Show(this, ReportRequest(afterGame: false, crashed: false).Only(id)));
            report.Margin = new Thickness(0, 1, 6, 1);
            chipColumn.Children.Add(report);
        }

        right.Children.Add(chipColumn);
        right.Children.Add(new Border
        {
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Coodsz.Ui.Chip { Text = mark, Kind = kind }
        });
        DockPanel.SetDock(right, Dock.Right);
        var index = new TextBlock
        {
            Text = number.ToString(),
            Width = 28,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD2, 0xA3, 0x6A)),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(index, Dock.Left);
        var detail = status.RowDetail;
        row.Children.Add(right);
        row.Children.Add(index);
        row.Children.Add(new StackPanel
        {
            Margin = new Thickness(8, 0, 8, 0),
            Children =
            {
                new TextBlock { Text = status.Mod.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE6, 0xE1)) },
                new TextBlock { Text = detail, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x7D, 0xAA, 0xA4)) }
            }
        });
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x44)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
            Child = row
        };
        Coodsz.Ui.ModRowLayout.FixHeight(border);
        return border;
    }

    // The Get it slot, kept empty on a row that is ready, so the chips and the status line up.
    private const double GetItColumn = 110;

    private Border Note(string text, string? link)
    {
        var panel = new DockPanel { LastChildFill = true };
        if (link != null)
        {
            var get = new Button { Content = "Get it", Style = (Style)FindResource("Ghost"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = link };
            get.Click += (_, _) => OpenLink(link);
            DockPanel.SetDock(get, Dock.Right);
            panel.Children.Add(get);
        }

        panel.Children.Add(new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xD2, 0xA3, 0x6A)) });
        return new Border { Padding = new Thickness(36, 4, 0, 8), Child = panel };
    }

    // Distinct: a newer game gives its five base game modules the same NewerGame line.
    private List<string> ModProblems() =>
        _gameRoot == null
            ? ["Bannerlord was not found."]
            : _mods.Where(mod => !mod.IsReady).Select(mod => mod.Problem!).Distinct().ToList();

    private void LogModProblems()
    {
        foreach (var problem in ModProblems())
            Log(problem);
        // Not a problem, since a server with the same versions plays, but this launcher can't see the server's set. The log
        // gets it once per check (the Mods list note is not logged); the missing textures line for 2.0 once per run.
        if (_gameRoot != null && OpenSourceSet.PcLine(_mods, withTextures: !_openSourceTexturesLogged) is { } openSource)
        {
            _openSourceTexturesLogged |= openSource.Contains(OpenSourceSet.MissingTextures, StringComparison.Ordinal);
            foreach (var line in openSource.Split('\n'))
                Log(line);
        }
    }

    private bool _openSourceTexturesLogged;

    private List<string> UIExtenderExRoots()
    {
        var roots = WorkshopRoots().ToList();
        if (_gameRoot != null)
            roots.Add(Path.Combine(_gameRoot, "Modules"));
        return roots;
    }

    // Puts back every UIExtenderEx SubModule.xml the Compiled Prefabs fix changed.
    private void RestoreUIExtenderExFix()
    {
        var problems = new List<string>();
        UIExtenderExPrefabsFix.RestoreAll(UIExtenderExRoots(), problems);
        foreach (var problem in problems)
            Log(problem);
    }

    private void RestoreUIExtenderExFixWhenClosed(Process game)
    {
        try
        {
            game.EnableRaisingEvents = true;
            game.Exited += (_, _) => Dispatcher.Invoke(RestoreUIExtenderExFix);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static IReadOnlyList<string> WorkshopRoots() =>
        BannerlordFinder.SteamLibraries()
            .Select(library => Path.Combine(library, "steamapps", "workshop", "content", "261550"))
            .Where(Directory.Exists)
            .ToList();

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
        var enabled = _gameRoot != null && ModProblems().Count == 0 && joinReady && !_joinChecking;
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
        SteamDot.Fill = new SolidColorBrush(running ? Color.FromRgb(0x5A, 0x8A, 0x60) : Color.FromRgb(0xB0, 0x84, 0x48));
        SteamStatusText.Text = path == null
            ? "Steam is not installed for this Windows user."
            : running ? "Steam is running." : "Steam is installed, and it is not running.";
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
        JoinStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x7D, 0xAA, 0xA4));
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

    private void LoadSettings()
    {
        if (!File.Exists(_settingsPath))
            return;
        try
        {
            _settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_settingsPath)) ?? new LauncherSettings();
        }
        catch (JsonException)
        {
            _settings = new LauncherSettings();
        }
    }

    private void SaveSettings()
    {
        _settings.GamePath = _gameRoot ?? "";
        _settings.Host = HostBox.Text.Trim();
        _settings.Port = PortBox.Text.Trim();
        _settings.Password = CurrentPassword();
        _settings.SteamName = SteamBox.Text.Trim();
        _settings.Rbm = CurrentRbm();
        _settings.KeepRbmCoopFriendly = RbmCoopFriendlyBox.IsChecked == true;
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed class LauncherSettings
{
    public string GamePath { get; set; } = "";
    public bool UseSteam { get; set; }
    public string SteamName { get; set; } = "";
    public string Host { get; set; } = "";
    public string Port { get; set; } = "4200";
    public string Password { get; set; } = "";
    public bool DllsUnblocked { get; set; }
    // Null until saved; the defaults are everything on except Campaign, like the server.
    public RbmSettings? Rbm { get; set; }
    // Connect also turns off RBM's Lords buy equipment in this PC's RBM settings.
    public bool KeepRbmCoopFriendly { get; set; } = true;
}
