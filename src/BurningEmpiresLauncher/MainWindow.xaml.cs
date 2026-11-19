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
using BurningEmpires.Core;
using BurningEmpires.Ui;
using TaomCoop.Core;

namespace BurningEmpiresLauncher;

public partial class MainWindow : Window
{
    private readonly string _settingsPath = ToolData.File("be-launcher.json");
    private BeClientSettings _settings = new();
    private string? _gameRoot;
    private bool _busy;
    // When Install or repair last unpacked into Modules, so its own new files never read as a copy by hand.
    private DateTime _unpackedAtUtc = DateTime.MinValue;
    private bool _passwordVisible;
    private bool _loadingForm = true;
    private bool _connectReady;
    // Steam can start or close while this window is open, so its status is read again.
    private readonly DispatcherTimer _steamPoll = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _unblockNoted;
    private readonly HostActionLog _actions = new(() => ToolData.Folder, LogFolderOpen.ClientActionLogName);

    // Shown once at startup (compat\BurningEmpiresCoopCompat\README.txt, Known limits).
    private const string KnownLimits =
        "War Sails and the Historical campaign start are not supported in Coop. Champions and some Burning Empires tables can look stale until you rejoin.";

    // Install or repair Burning Empires unpacks the downloads here, on the game's own drive, and
    // removes the folder again.
    private const string UnpackFolder = "Coodsz-BE-unpack";

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
        Coodsz.Ui.NewBadges.Attach(GuidesButton, NewFeatures.Guides);
        ReadOwnServerFromBeData();
        SetUpModReports();
        LoadSettings();
        HostBox.Text = _settings.Host;
        PortBox.Text = string.IsNullOrWhiteSpace(_settings.Port) ? BeStack.DirectPort : _settings.Port;
        PasswordHidden.Password = _settings.Password;
        PasswordBox.Text = _settings.Password;
        SteamBox.Text = _settings.SteamName;
        SetJoinMode(_settings.UseSteam);
        UpdateSteamStatus();
        Activated += (_, _) => UpdateSteamStatus();
        _steamPoll.Tick += (_, _) => UpdateSteamStatus();
        _steamPoll.Start();
        GameSearch? search = null;
        _gameRoot = GameFolderPick.ResolveSavedPath(_settings.GamePath, () => (search = BannerlordFinder.FindGameWithReason()).Root);
        ShowGameFolder();
        if (_gameRoot == null)
            Log(search?.Reason ?? GameSearch.GameMissingReason);
        // Before the rows are read: a Coop the last run left set aside (it crashed or was killed) goes back first.
        PutBackAfterCrash();
        // The setups and the mod list fill even with no game, so a folder picked later only adds the game.
        InitSetups();
        RefreshModules();
        _loadingForm = false;
        UpdateConnectButton();
        ApplyCrashGuards();
        Log("Burning Empires set: " + BeStack.SetText + ".");
        Log(KnownLimits);
        _ = WatchForUpdateAsync(ReleaseNotice.BeRepo, ReleaseNotice.BeVersion);
        Loaded += async (_, _) =>
        {
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
    // Connect does not search again, so this is the only way a late pick reaches it.
    private void ApplyGameFound(string root)
    {
        _gameRoot = root;
        ShowGameFolder();
        if (Process.GetProcessesByName("Bannerlord").Length == 0)
            RestorePrefabsFix();
        RefreshModules();
        UpdateConnectButton();
        ApplyCrashGuards();
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

    // Until the Burning Empires release repository exists the check gets no release and stays quiet.
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
        var problems = ModuleProblems();
        var setup = _settings.ModSetups.Current();
        var message = problems.Count > 0
            ? "These modules are missing or the wrong version: " + string.Join(", ", problems)
            : setup.HasEnabledExtras
                ? "Every module on " + setup.Name + " is ready. Check mods looks in the game's Modules folder and the Steam Workshop. It does not download anything."
                : "The Burning Empires module list is ready. Check mods looks in the game's Modules folder and the Steam Workshop. It does not download anything.";
        JoinStatus.Text = message;
        Log(message);
        if (problems.Contains(BeStack.CoopModuleId, StringComparer.OrdinalIgnoreCase) && CoopInstall.ClientGetLineNow is { } getCoop)
            Log(getCoop);
        if (!string.IsNullOrWhiteSpace(_gameRoot))
            Log(BeCoopCopy.Describe(Path.Combine(_gameRoot, "Modules"), CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries())));
        if (BannerlordCoopLauncher.Core.Modules.UIExtenderExPrefabsFix.InLaunch(BeLoadOrder.LaunchModules(setup, joinPassword: false)))
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

        if (OpenFolder(modules))
            Log("Opened the Modules folder.");
    }

    // A folder Windows can't open is a log line, not a throw that would close the tool.
    private bool OpenFolder(string folder)
    {
        if (ShellOpen.TryOpen(folder, out var problem))
            return true;
        Log("Could not open " + folder + ". " + problem);
        return false;
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
            if (OpenFolder(bin))
                Log("Opened the game log folder.");
        }
        else
        {
            var reports = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Mount and Blade II Bannerlord",
                "Coop Crash Reports");
            Directory.CreateDirectory(reports);
            if (OpenFolder(reports))
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

    // One button: Install or repair Burning Empires. Burning Empires is on Nexus Mods only, so it is
    // unpacked from the player's own downloads (the main file, then the hotfix on top) over the game's
    // Modules folder. Nothing is deleted there, and BESavedata, the Burning Empires save data, is never
    // written. Then the bundled Burning Empires Coop Compat goes in and Steam gets any missing BUTR
    // module. When the downloads cannot be unpacked it says why and asks before it opens the Nexus page.
    private async void InstallOrRepair_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginInstall())
            return;

        var gameRoot = _gameRoot!;
        string? contentProblem;
        IReadOnlyList<string> archives;
        string? setProblem;
        bool hotfixOnly;
        bool copying;
        // Files this tool unpacked a moment ago are new too, so they never count as a copy by hand.
        var unpackedJustNow = DateTime.UtcNow - _unpackedAtUtc < TimeSpan.FromMinutes(5);
        try
        {
            (contentProblem, archives, setProblem, hotfixOnly, copying) = await Task.Run(() =>
            {
                var found = FindBeArchives();
                var problem = BeArchive.ContentProblem(gameRoot);
                var stillCopying = problem != null && !unpackedJustNow && BeArchive.ChangedRecently(gameRoot, DateTime.UtcNow, TimeSpan.FromMinutes(1));
                return (problem, found, BeArchive.SetProblem(found, gameRoot), !BeArchive.NeedsMainFile(gameRoot), stillCopying);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log(ex.Message);
            _busy = false;
            return;
        }

        // A copy into Modules that is still running looks like a broken Burning Empires, so nothing
        // is asked or unpacked over it.
        if (copying)
        {
            Log(BeUiText.CopyStillRunning);
            _busy = false;
            return;
        }

        // Unpacking the 5 GB main file takes minutes, so downloads found by themselves are only
        // unpacked after the player says Yes to the files and sizes. A file picked by hand is already a yes.
        // A hotfix with nothing under it asks for the main file, and a main file with no hotfix is
        // refused before the question, since neither can make Burning Empires 2.0.2. With Burning
        // Empires already in Modules (a copy by hand) only the hotfix is asked for. Cancel in the
        // picker stops there: nothing is unpacked and no browser opens.
        var unpack = contentProblem != null;
        string? nexusReason = null;
        var nexusHotfixOnly = hotfixOnly;
        if (contentProblem != null && (archives.Count == 0 || setProblem == BeArchive.MainFileMissing))
        {
            var picked = PickBeDownload(contentProblem, archives, hotfixOnly);
            if (picked == null)
            {
                unpack = false;
                Log(BeUiText.PickCancelled(hotfixOnly, BeStack.BeVersionText));
            }
            else
            {
                archives = picked;
            }
        }
        else if (setProblem != null)
        {
            unpack = false;
            if (contentProblem != null)
            {
                Log(contentProblem);
                Log(setProblem);
                nexusReason = contentProblem + "\n" + setProblem;
                nexusHotfixOnly = hotfixOnly || setProblem == BeArchive.HotfixMissing;
            }
        }
        else if (archives.Count > 0)
        {
            var question = BeUiText.DownloadQuestion(contentProblem, BeStack.BeVersionText, BeArchive.Describe(archives));
            unpack = Coodsz.Ui.UiLanguage.Message(question, "Install or repair Burning Empires", MessageBoxButton.YesNo, MessageBoxImage.Question, this) == MessageBoxResult.Yes;
            if (!unpack && contentProblem != null)
                Log("Nothing was unpacked.");
        }

        if (contentProblem == null && !unpack)
            Log("Burning Empires " + BeStack.BeVersionText + " is ready.");

        var addOn = ClientAddOns.Payload(BeStack.CompatModuleId);
        IProgress<string> progress = new Progress<string>(Log);
        await RunAsync(() =>
        {
            if (unpack && !UnpackBe(archives, gameRoot, contentProblem, progress, out var failure))
                nexusReason = failure;
            var compatProblem = BeArchive.CompatProblem(gameRoot, addOn);
            progress.Report(compatProblem == null
                ? "Putting " + BeStack.CompatName + " in again from this launcher."
                : compatProblem + " Putting it in from this launcher.");
            progress.Report(BeArchive.InstallCompat(addOn, gameRoot));
            UnblockInstalledModules(gameRoot, progress);
        });
        await GetButrAsync();
        RefreshModules();
        if (nexusReason != null)
            AskOpenNexus(nexusReason, nexusHotfixOnly);
    }

    // False when Burning Empires could not be put in from the downloads; failure says why, for the
    // question before the Nexus page opens.
    private bool UnpackBe(IReadOnlyList<string> archives, string gameRoot, string? problem, IProgress<string> progress, out string? failure)
    {
        failure = null;
        if (archives.Count == 0)
        {
            failure = (problem == null ? "" : problem + " ") + "No Burning Empires download was found. Download the main file and the "
                + BeStack.BeVersionText + " hotfix from Nexus Mods into Downloads, then click Install or repair Burning Empires again.";
            progress.Report(failure);
            return false;
        }

        // InstallOverlay checks the free space itself, from the unpacked size, before it unpacks anything.
        progress.Report((problem == null ? "Putting Burning Empires in again" : problem + " Installing Burning Empires")
            + " from " + string.Join(" and ", archives.Select(Path.GetFileName)) + ". Your Burning Empires save data is not touched.");
        var scratch = Path.Combine(gameRoot, UnpackFolder);
        try
        {
            BeArchive.InstallOverlay(archives, gameRoot, scratch, progress);
        }
        catch (BeArchiveException ex)
        {
            // A full disk or a missing tar.exe is not fixed by a new download, so it is only logged.
            failure = ex.DownloadHelps ? ex.Message : null;
            progress.Report(ex.Message);
            return false;
        }
        finally
        {
            _unpackedAtUtc = DateTime.UtcNow;
            RemoveEmptyFolder(scratch);
        }

        RememberArchives(archives);
        var left = BeArchive.ContentProblem(gameRoot);
        if (left == null)
        {
            progress.Report("Burning Empires " + BeStack.BeVersionText + " is installed. Your Burning Empires save data was not touched.");
            return true;
        }

        // A missing hotfix is caught before the question (BeArchive.SetProblem), so only what is left is said.
        failure = left;
        progress.Report(left);
        return false;
    }

    private static void RemoveEmptyFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // The player's own Burning Empires downloads: the newest main file and hotfix in Downloads, on the
    // Desktop or in the folder they came from last time. A file picked by hand is kept even when its
    // name does not start with Burning Empires.
    private IReadOnlyList<string> FindBeArchives()
    {
        var saved = new[] { _settings.BeArchive, _settings.BeHotfixArchive }.Where(File.Exists).ToList();
        var folders = BeArchive.DefaultFolders().ToList();
        foreach (var folder in saved.Select(Path.GetDirectoryName).OfType<string>())
        {
            if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                folders.Add(folder);
        }

        var found = BeArchive.FindArchives(folders).ToList();
        foreach (var path in saved)
        {
            if (!found.Any(item => BeArchive.IsHotfix(item) == BeArchive.IsHotfix(path)))
                found.Add(path);
        }

        return found.OrderBy(BeArchive.IsHotfix).ToList();
    }

    private void RememberArchives(IReadOnlyList<string> archives)
    {
        _settings.BeArchive = archives.FirstOrDefault(path => !BeArchive.IsHotfix(path)) ?? _settings.BeArchive;
        _settings.BeHotfixArchive = archives.FirstOrDefault(BeArchive.IsHotfix) ?? _settings.BeHotfixArchive;
    }

    // No download in Downloads or on the Desktop, or only the hotfix: the player points at the main
    // file, and the hotfix next to it (or the one already found) is used too. With Burning Empires
    // already in Modules (hotfixOnly, a copy by hand) the hotfix is asked for instead. Null on
    // Cancel, so nothing is unpacked and a lone hotfix never goes in alone.
    private IReadOnlyList<string>? PickBeDownload(string problem, IReadOnlyList<string> found, bool hotfixOnly)
    {
        Log(BeUiText.PickLine(problem, hotfixOnly, found.Count > 0, BeStack.BeVersionText));
        var dialog = new OpenFileDialog
        {
            Title = BannerlordCoopLauncher.Core.Localization.Translator.T(BeUiText.PickTitle(hotfixOnly)),
            Filter = "Burning Empires download|*.7z;*.rar;*.zip"
        };
        if (dialog.ShowDialog(this) != true)
            return null;
        var picked = dialog.FileName;
        return BeArchive.FindArchives(new[] { Path.GetDirectoryName(picked)! })
            .Concat(found)
            .Where(path => BeArchive.IsHotfix(path) != BeArchive.IsHotfix(picked))
            .Take(1)
            .Prepend(picked)
            .OrderBy(BeArchive.IsHotfix)
            .ToList();
    }

    // The browser only opens after a Yes to a question that says why, so it never opens unasked.
    private void AskOpenNexus(string reason, bool hotfixOnly)
    {
        var question = BeUiText.NexusQuestion(reason, hotfixOnly, BeStack.BeVersionText);
        if (Coodsz.Ui.UiLanguage.Message(question, "Install or repair Burning Empires", MessageBoxButton.YesNo, MessageBoxImage.Question, this) == MessageBoxResult.Yes)
            OpenNexusPage(hotfixOnly);
        else
            Log(BeUiText.NexusNotOpened(BeStack.BeNexusPage, hotfixOnly, BeStack.BeVersionText));
    }

    private void OpenNexusPage(bool hotfixOnly)
    {
        try
        {
            Process.Start(new ProcessStartInfo(BeStack.BeNexusPage) { UseShellExecute = true });
            Log(BeUiText.NexusOpened(hotfixOnly, BeStack.BeVersionText));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log("Burning Empires is on Nexus Mods at " + BeStack.BeNexusPage);
        }
    }

    // Harmony, ButterLib and UIExtenderEx come from the Steam Workshop. Missing ones are subscribed and
    // downloaded through Steam; when Steam can't do it by itself their Workshop pages open, and with Steam
    // closed it only says to open Steam (SubscribeWorkshopAsync). MCM comes from Nexus Mods
    // into the game's Modules folder (its official Workshop item was removed), so it is never subscribed.
    // True when Harmony, ButterLib, UIExtenderEx and MCM are all on this PC; a missing MCM keeps it false
    // until the player puts the Nexus Mods copy in.
    private async Task<bool> GetButrAsync()
    {
        ScanWorkshopModules();
        var missing = MissingButr();
        if (missing.Count == 0)
            return true;
        var names = BeSharedList.ButrDownloads(missing);

        if (names.Count > 0)
        {
            _busy = true;
            try
            {
                await SubscribeWorkshopAsync(names);
            }
            finally
            {
                _busy = false;
            }
        }

        ScanWorkshopModules();
        var left = MissingButr();
        foreach (var mod in left.Where(mod => mod.FromNexus))
            Log(McmAdvice);
        var downloading = left.Where(mod => !mod.FromNexus).Select(mod => mod.Name).ToList();
        if (left.Count == 0)
            Log("Harmony, ButterLib, UIExtenderEx and MCM are on this PC.");
        // With Steam closed SubscribeWorkshopAsync already said to open Steam.
        else if (downloading.Count > 0 && BeSteamPage.SteamRunning())
            Log("Steam is still finishing " + string.Join(", ", downloading) + ". Wait for the download in Steam, then try again.");
        return left.Count == 0;
    }

    private static string McmAdvice =>
        "MCM is not on this PC. Get MCM " + BeStack.Mcm.TestedVersion + " or newer from Nexus Mods and put it in the game's Modules folder. " + BeServerPath.McmOnNexus;

    // Any Workshop copy of MCM counts here on purpose: the client game loads it. The server tool only takes the
    // retired official item at McmServerMinimum or newer (BeServerPath.OldWorkshopMcm), since re-uploads can crash it.
    private List<ButrModule> MissingButr() =>
        _gameRoot == null ? new List<ButrModule>() : BeStack.Butr.Where(mod => FindModuleXml(_gameRoot, mod.ModuleId) == null).ToList();

    // The tools follow the Workshop Coop's updates. This makes sure it is downloaded and moves any other
    // Coop copy out of Modules, kept, not deleted, except a newer Coop unzipped there by hand.
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

        var gameRoot = _gameRoot!;
        var marker = _setAsideMarker;
        IProgress<string> progress = new Progress<string>(Log);
        // An unreadable Workshop SubModule.xml stops here with BeCoopCopy.WorkshopUnreadable, which RunAsync logs.
        await RunAsync(() =>
        {
            var version = BeCoopCopy.WorkshopVersion(workshop);
            foreach (var path in BeCoopCopy.ParkBesideWorkshop(gameRoot, workshop, marker))
                progress.Report(CoopParking.SetAsideLine(path));
            // A newer Coop unzipped into Modules by hand stays, and the game loads it before the Workshop one.
            if (CoopInstall.InModules(gameRoot) is { } manual && CoopInstall.IsNewer(manual.Version, version))
                progress.Report("Bannerlord Coop " + manual.Version + " in the game's Modules folder is used. It is newer than the Steam Workshop Coop " + version + ", so it stays.");
            else
                progress.Report("Bannerlord Coop " + version + " from the Steam Workshop is ready.");
        });
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

    private async Task ConnectAsync()
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
        foreach (var line in CoopInstall.CheckModulesCoop(_gameRoot))
            Log(line);
        // Connect starts the saved setup; changes on screen are kept or dropped first.
        if (!ConfirmModChanges())
            return;

        // The server needs the BUTR modules and the setup's extra mods. When some are not on this PC,
        // one prompt gets them and connects; with nothing missing Connect goes on as before.
        bool ready;
        try
        {
            ready = await GetServerModsFirstAsync();
        }
        catch (Exception ex)
        {
            // Steam links and Workshop downloads can throw; Connect stops here instead of closing the tool.
            Log("Getting the mods the server needs stopped, so Connect did not go on. " + ex.Message);
            return;
        }
        if (!ready)
            return;

        var problems = ModuleProblems();
        if (problems.Count > 0)
        {
            var message = "Install the missing modules first: " + string.Join(", ", problems);
            JoinStatus.Text = message;
            Log(message);
            return;
        }

        if (!ModChecksPass(LaunchSetup))
            return;

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
            LogServerModuleNotes();
            ParkRetiredMcm();
            WarnVersionDrift();
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
            ApplyCrashGuards();
            // The Burning Empires core in its sealed order, the setup's extra mods in their places,
            // and the join password module last. The locked Burning Empires setup gives exactly the core.
            var setup = LaunchSetup;
            var modules = BeLoadOrder.LaunchModules(setup, InstallJoinPassword());
            var joinKey = JoinMemory.Key(steam, SteamBox.Text, HostBox.Text, PortBox.Text);
            var lastJoin = JoinMemory.Problem(_settings.LastJoins.GetValueOrDefault(joinKey), modules);
            if (lastJoin != null)
                ShowJoin(lastJoin);
            // Coop 0.1.6 compares every community module both ways, interface mods too (CoopJoinRules);
            // live run 2 joined fine because Connect ran the Burning Empires setup, not Setup 2.
            if (setup.HasEnabledExtras)
                Log(setup.Name + " adds " + string.Join(", ", setup.EnabledExtras.Select(mod => mod.Label)) + ". The host has to load the same mods, even interface mods, or the server turns the join down.");
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
            Log(BeCoopCopy.Describe(Path.Combine(_gameRoot, "Modules"), CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries())));
            if (NonSteamCopy.Note(_gameRoot) is { } nonSteam)
                Log(nonSteam);
            // A Common.dll or 0Harmony.dll another mod left in the game's bin folder: a warning, the game still starts.
            foreach (var leftover in GameBinLeftovers.Notes(_gameRoot))
                Log(leftover);
            // The Activity box can be seen on a stream or screenshot, so the join password is cleaned out.
            Log(TaomLaunch.Shown(arguments, CurrentPassword()));
            var started = StartWithPrefabsFix(setup, start);
            Log("Bannerlord started.");
            RestoreWhenClosed(started);
            // The header pill says Joining until the join result, then In game or Refused until the game closes.
            SetJoinState(ClientNextStep.Joining);
            var sessionExtras = SessionExtras(setup);
            try
            {
                await WatchJoinAsync(started, _gameRoot, () =>
                {
                    RememberJoin(joinKey, modules);
                    _ = RecordSessionAsync(started, sessionExtras, modules);
                });
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

    // Coop's auto-connect sends an empty password on the first try, so a password server answers
    // "incorrect password". This module fills it from the launcher without editing Coop.Core.dll.
    // It loads last; Coop skips DedicatedServer.* modules in the join check, so the server needs nothing.
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

    private async Task WatchJoinAsync(Process started, string gameRoot, Action? joined = null)
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
                    Log("Connected. The game is joining the Burning Empires server.");
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
                    await ExplainEarlyExitAsync(started);
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
        RestoreRetiredMcm();
        ScanInstalledExtras();
        ShowModuleList();
        // The Next step card reads the modules again (ShowNextStep).
        _attention = null;
        NoteWorkshopTwins();
        NoteRetiredMcm();
        if (!_loadingForm)
            UpdateConnectButton();
    }

    // One locked core row, the same look as the TAOM launcher's.
    private Border CoreRow(int number, string id)
    {
        var xml = _gameRoot == null ? null : FindModuleXml(_gameRoot, id);
        string version;
        bool found;
        var rightVersion = true;
        var butr = BeStack.Butr.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (xml == null)
        {
            version = butr == null ? "missing" : BeSharedList.ButrMissingText(butr);
            found = false;
        }
        else
        {
            var identity = ModuleIdentityReader.Read(xml);
            version = identity.Version;
            var expected = Expected(id);
            found = true;
            rightVersion = expected == null || identity.Version.Equals(expected, StringComparison.OrdinalIgnoreCase);
            // The BUTR modules follow the Workshop; another version than the tested one only gets a note.
            if (butr != null && !identity.Version.Equals(butr.TestedVersion, StringComparison.OrdinalIgnoreCase))
                version += " (tested with " + butr.TestedVersion + ")";
        }

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 0), LastChildFill = true };
        // The Ready pill in the same column as the extra rows', with the chips after it: the counts,
        // this PC's result and the Report link, without the list status (an empty column for Coop and the official modules).
        var chips = PackModChips(id, xml == null ? null : Path.GetDirectoryName(xml)) ?? BadgeColumn();
        DockPanel.SetDock(chips, Dock.Right);
        var (mark, kind) = BeModChips.CoreMark(found, rightVersion);
        var status = StatusPill(mark, kind);
        DockPanel.SetDock(status, Dock.Right);
        // Always on, the Mods page word for a module the tool keeps on and in its place (ModChips.Role).
        var locked = new Coodsz.Ui.Chip
        {
            Text = ModChips.AlwaysOn,
            Kind = Coodsz.Ui.ChipKind.Mute,
            Margin = new Thickness(0, 0, 12, 0),
            Tip = BeSetupBook.CoreRowTip
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
        row.Children.Add(chips);
        row.Children.Add(status);
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
                new TextBlock { Text = CoreName(id), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE6, 0xE1)) },
                new TextBlock { Text = id != BeStack.CoopModuleId ? version : InGameModules(xml) ? "Coop " + version + " (Modules)" : "Coop " + version + " (Workshop)", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x7D, 0xAA, 0xA4)) }
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

    private static string CoreName(string id)
    {
        if (id.Equals(BeStack.CoopModuleId, StringComparison.OrdinalIgnoreCase))
            return "Bannerlord Coop";
        if (id.Equals(BeStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return BeStack.CompatName;
        if (id.Equals(BeStack.BeModuleId, StringComparison.OrdinalIgnoreCase))
            return BeStack.BeName;
        if (id.Equals(BeStack.ElephantsModuleId, StringComparison.OrdinalIgnoreCase))
            return BeStack.ElephantsName;
        return BeStack.Butr.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
    }

    private List<string> ModuleProblems()
    {
        var problems = CoreProblems();
        if (_gameRoot == null)
            return problems;
        // Extra mods of the saved setup (the one Connect starts) that are not on this PC.
        foreach (var mod in BeSharedList.Missing(LaunchSetup, _installedExtras.Keys))
            problems.Add(mod.Label);
        return problems;
    }

    // The Burning Empires core, Elephants included, and Coop: Install or repair
    // Burning Empires puts these in, so they keep Connect off. A missing BUTR module does not, because
    // Connect gets it from the Steam Workshop or, for MCM, names its Nexus Mods page (GetServerModsFirstAsync);
    // withButr leaves it out.
    private List<string> CoreProblems(bool withButr = true)
    {
        var problems = new List<string>();
        if (_gameRoot == null)
            return problems;
        var ids = BeStack.ClientModules;
        foreach (var id in ids)
        {
            var xml = FindModuleXml(_gameRoot, id);
            if (xml == null)
            {
                if (withButr || !BeStack.IsButr(id))
                    problems.Add(id);
                continue;
            }

            var expected = Expected(id);
            if (expected == null)
                continue;
            var identity = ModuleIdentityReader.Read(xml);
            if (!identity.Version.Equals(expected, StringComparison.OrdinalIgnoreCase))
                problems.Add(id + " " + identity.Version);
        }

        return problems;
    }

    // Burning Empires, Elephants and the compat module have to match. Coop follows the Workshop and
    // the BUTR modules only warn (WarnVersionDrift), so they have no expected version here.
    private static string? Expected(string id) =>
        BeStack.ContentVersions.TryGetValue(id, out var version) ? version : null;

    // The version of a core module on this PC, or "".
    private string CoreVersion(string id)
    {
        var xml = _gameRoot == null ? null : FindModuleXml(_gameRoot, id);
        return xml == null ? "" : ModuleIdentityReader.Read(xml).Version;
    }

    private void LogServerModuleNotes()
    {
        foreach (var note in BeModNotes.For(LaunchSetup))
            Log(note);
    }

    // Every Steam Workshop item's module by id. The BUTR modules and Coop come from the Workshop, and
    // looking only in Coop's item showed Harmony, ButterLib, UIExtenderEx and MCM as Missing.
    // Read again with the mods on this PC (ScanInstalledExtras).
    private Dictionary<string, string> _workshopXml = new(StringComparer.OrdinalIgnoreCase);

    private void ScanWorkshopModules()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in Workshops())
        {
            try
            {
                if (!Directory.Exists(library.ContentDirectory))
                    continue;
                foreach (var item in Directory.EnumerateDirectories(library.ContentDirectory))
                {
                    // A module sits at the top of its item, or one folder down.
                    var xml = Path.Combine(item, "SubModule.xml");
                    var candidates = File.Exists(xml)
                        ? new[] { xml }
                        : Directory.EnumerateDirectories(item).Select(folder => Path.Combine(folder, "SubModule.xml")).Where(File.Exists).ToArray();
                    foreach (var candidate in candidates)
                    {
                        var id = ModuleIdentityReader.Read(candidate).Id;
                        if (id.Length > 0)
                            found.TryAdd(id, candidate);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        _workshopXml = found;
    }

    // True when FindModuleXml took the game's Modules folder copy, the one the game loads first (a Coop unzipped by hand).
    private bool InGameModules(string? xml) =>
        xml != null && _gameRoot != null
        && Path.GetFullPath(xml).StartsWith(Path.GetFullPath(Path.Combine(_gameRoot, "Modules")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // The game's Modules folder first, then every Workshop item.
    private string? FindModuleXml(string gameRoot, string moduleId)
    {
        var modules = Path.Combine(gameRoot, "Modules");
        if (Directory.Exists(modules))
        {
            foreach (var directory in Directory.EnumerateDirectories(modules))
            {
                var xml = Path.Combine(directory, "SubModule.xml");
                if (!File.Exists(xml))
                    continue;
                var identity = ModuleIdentityReader.Read(xml);
                if (identity.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(directory).Equals(moduleId, StringComparison.OrdinalIgnoreCase))
                    return xml;
            }
        }

        return _workshopXml.TryGetValue(moduleId, out var workshopXml) && File.Exists(workshopXml) ? workshopXml : null;
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
        // Missing extra mods and BUTR modules leave Connect on: Connect offers to get them first.
        var enabled = _gameRoot != null && CoreProblems(withButr: false).Count == 0 && joinReady && !_joinChecking;
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

    // The game loads a Coop in Modules before the Steam Workshop one, and two Coop copies crash the game.
    // Another Coop in Modules is set aside in the game folder unless it has the Workshop Coop's version
    // or a newer one (Coop 0.1.7 unzipped by hand from GitHub); with no Workshop Coop yet one Coop stays
    // and only extra copies go. They go back into Modules when this launcher closes (MainWindow.SetAside.cs),
    // and this runs again before each Connect and game launch. The tools follow the newest Coop (no
    // Coop.Core.dll pin and no TAOM 0.1.6 pin), so Coop.Core.dll is left as it is.
    private void ApplyCrashGuards()
    {
        if (string.IsNullOrWhiteSpace(_gameRoot))
            return;

        try
        {
            var workshop = CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries());
            var parked = workshop != null
                ? BeCoopCopy.ParkBesideWorkshop(_gameRoot, workshop, _setAsideMarker)
                : BeCoopCopy.ParkWithoutWorkshop(_gameRoot, _setAsideMarker);
            foreach (var folder in parked)
                Log(CoopParking.SetAsideLine(folder));
            // The rows, the Next step card and the Active Coop line were read with that copy still in Modules.
            if (parked.Count > 0 && !_loadingForm)
                RefreshModules();
        }
        catch (BeCoopCopyException ex)
        {
            Log(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("Could not move an extra Coop folder out of Modules. " + ex.Message);
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

    private async Task RunAsync(Action work)
    {
        _busy = true;
        try
        {
            await Task.Run(work);
        }
        catch (Exception ex)
        {
            Log(ex.Message);
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

    // A missing or unreadable be-launcher.json gives the defaults with the one locked Burning Empires setup.
    private void LoadSettings() => _settings = BeClientSettings.Load(_settingsPath);

    private void SaveSettings()
    {
        _settings.GamePath = _gameRoot ?? "";
        _settings.Host = HostBox.Text.Trim();
        _settings.Port = PortBox.Text.Trim();
        _settings.Password = CurrentPassword();
        _settings.SteamName = SteamBox.Text.Trim();
        _settings.Save(_settingsPath);
    }
}
