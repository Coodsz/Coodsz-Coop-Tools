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
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using TaomCoop.Core;

namespace TaomCoopHost;

public partial class MainWindow
{
    private async void Prepare_Click(object sender, RoutedEventArgs e)
    {
        // Start server may be repairing or preparing the same folder, so Prepare waits for it too.
        if (_busy || _startingServer)
        {
            SetControl("Prepare server waits until the tool finishes what it is doing now. Click it again in a moment.", failed: true);
            return;
        }

        // Prepare server copies into the folder the running server loads from.
        if (ServerIsRunning())
        {
            SetControl("Stop the server before Prepare server.", failed: true);
            return;
        }

        if (FindGameRoot() == null)
        {
            FindBannerlordButton.Visibility = Visibility.Visible;
            SetControl(GameFolderPick.NotFoundText, failed: true);
            return;
        }

        var save = SelectedSavePath();
        if (!File.Exists(save))
        {
            SetControl("Choose an existing TAOM .sav file. A vanilla new game is refused.", failed: true);
            return;
        }
        if (!TaomServerPath.IsTaomCampaign(save))
        {
            SetControl(TaomServerPath.NotTaomCampaign, failed: true);
            return;
        }

        var libraries = BannerlordFinder.SteamLibraries();
        // A Workshop server with Coop 0.1.6 stays the source. Steam has Coop 0.1.7 since 2026-10-30, so otherwise the Coop 0.1.6
        // server folder the host chose is (KitCoopServer.Pick). Hashing it runs in RunAsync, off the window's thread.
        var workshopServer = CoopStaging.FindDedicatedServer(libraries);
        var chosenFolder = _settings.Coop016Server;
        KitCoopServerPick? pick = null;
        await RunAsync(() => pick = KitCoopServer.Pick(workshopServer, chosenFolder));
        if (pick == null)
            return;
        if (pick.Problem != null)
        {
            SetControl(pick.Problem, failed: true);
            return;
        }

        var serverSource = pick.Server;
        var chosen = pick.Chosen;
        if (serverSource == null)
        {
            // A Workshop Coop the game loads but without its server program says antivirus only when the watch saw
            // that file before (CR-88BP7). Otherwise a download still running or an incomplete one. Finding the
            // libraries and the server folder runs above as before. Reading Steam's workshop list and the watch
            // runs in RunAsync, which also keeps a second click out while it does.
            await RunAsync(() =>
            {
                var workshopCoop = AntivirusFiles.WorkshopCoop(libraries);
                throw new InvalidOperationException(AntivirusFiles.WorkshopServerProblem(workshopCoop, "Prepare server", AntivirusWatch.ForTool().Gone([workshopCoop]), libraries) ?? "The Workshop Coop dedicated server was not found.");
            });
            return;
        }

        // A Workshop Coop Steam is still updating can be mid swap, so nothing is copied from it until Steam is done.
        // A chosen Coop 0.1.6 server is not Steam's, so Steam's update doesn't hold it up.
        var steamUpdating = false;
        if (!chosen)
        {
            await RunAsync(() =>
            {
                var workshopCoop = AntivirusFiles.WorkshopCoop(libraries);
                steamUpdating = workshopCoop != null && AntivirusFiles.SteamUpdating(workshopCoop);
            });
        }
        if (steamUpdating)
        {
            SetControl(AntivirusText.WorkshopDownloading("Prepare server"), failed: true);
            return;
        }

        if (Directory.Exists(_settings.ServerRoot) && Directory.EnumerateFileSystemEntries(_settings.ServerRoot).Any())
        {
            SetControl("This server folder is already prepared, so Prepare server is refused to keep your campaign safe. Click Start server. It puts back missing server files by itself.", failed: true);
            return;
        }

        SaveSettings();
        var password = PasswordBox.Text;
        var autosaveText = AutosaveBox.Text.Trim();
        var steamRelay = SteamBox.IsChecked == true;
        var logFile = LogFileBox.IsChecked == true;
        var serverRoot = _settings.ServerRoot;
        var dataRoot = _settings.DataRoot;
        var modules = Path.Combine(_gameRoot!, "Modules");
        await RunAsync(() =>
        {
            if (chosen)
            {
                // The chosen folder is only read: no password restore writes into it, and Pick already checked its hashes.
                // The host kit takes only a Workshop shaped path, so it reads the folder through a junction and still
                // checks the exact Coop 0.1.6 DLLs itself. A server or Data folder that overlaps the chosen one is refused first.
                NoteLog(KitCoopServer.Chosen(serverSource));
                KitCoopServer.PrepareFromChosen(serverSource, ChosenServerLinkFolder, serverRoot, dataRoot,
                    link => RunPrepare(EnsureHostKit(NoteLog), link, modules, serverRoot, dataRoot, save, password, autosaveText, steamRelay, logFile), NoteLog);
            }
            else
            {
                var dll = Path.Combine(serverSource, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Coop.Core.dll");
                CoopStaging.RestoreQualifiedDll(dll);
                NoteLog("Workshop Coop.Core.dll matches the released Coop 0.1.6 the " + TaomStack.KitVersion + " kit needs.");
                RunPrepare(EnsureHostKit(NoteLog), serverSource, modules, serverRoot, dataRoot, save, password, autosaveText, steamRelay, logFile);
            }
            InstallServerTools(serverRoot);
            RememberServerFiles(serverRoot);
            _actions.Note("Prepare", "Prepared server. Save=" + Path.GetFileNameWithoutExtension(save));
        });
        RefreshState();
    }

    // Where the junction to a chosen Coop 0.1.6 server lives while Prepare server runs (KitCoopServer.KitLinkPath).
    private static string ChosenServerLinkFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TAOM-Coop", "Coop016Server");

    // The chosen folder is saved only when it holds the official Coop 0.1.6 server, so Prepare and Start never meet a wrong one.
    private async void ChooseCoop016Server_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            ShowChooseResult("Wait for the current action to finish.", failed: true);
            return;
        }

        var dialog = new OpenFolderDialog { Title = BannerlordCoopLauncher.Core.Localization.Translator.T("Pick the folder you unpacked the Coop 0.1.6 dedicated server into"), Multiselect = false };
        if (Directory.Exists(_settings.Coop016Server))
            dialog.InitialDirectory = _settings.Coop016Server;
        if (dialog.ShowDialog(this) != true)
            return;
        var folder = dialog.FolderName;
        // Problem and WorkshopHasKitCoop catch a file they can't read; RunAsync shows anything else instead of closing the tool.
        string? problem = null;
        string? server = null;
        var workshopFirst = false;
        var checkedFolder = false;
        var serverRoot = _settings.ServerRoot;
        var dataRoot = _settings.DataRoot;
        await RunAsync(() =>
        {
            problem = KitCoopServer.ChooseProblem(folder, serverRoot, dataRoot);
            server = KitCoopServer.Resolve(folder);
            workshopFirst = KitCoopServer.WorkshopHasKitCoop(CoopStaging.FindDedicatedServer(BannerlordFinder.SteamLibraries()));
            checkedFolder = true;
        });
        // RunAsync already showed why when the check threw.
        if (!checkedFolder)
            return;
        // Never quiet: a pick with no server program always says so (TAOM Server Tool report, 2026-11-07).
        var (result, failed) = KitCoopServer.ChooseResult(problem, server, workshopFirst);
        if (failed)
        {
            ShowChooseResult(result, failed: true);
            return;
        }

        // A locked or read only settings file would close the tool from this handler, so the old folder stays and the host reads why.
        var previous = _settings.Coop016Server;
        _settings.Coop016Server = server!;
        try
        {
            SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _settings.Coop016Server = previous;
            ShowChooseResult("The Coop 0.1.6 server folder could not be saved in the settings. " + ex.Message, failed: true);
            return;
        }

        _actions.Note(KitCoopServer.ChooseButton, "Chosen " + server);
        ShowChooseResult(result, failed: false);
    }

    // The button is on the Saves page and the note under Start server on the Dashboard, so a pick shows on both (CR-VCTXE).
    private void ShowChooseResult(string text, bool failed)
    {
        SetControl(text, failed);
        ShowCoop016Status(text, failed);
    }

    // The line under Choose Coop 0.1.6 server. Startup shows the chosen folder, or that it is gone (KitCoopServer.StartupLine).
    private void ShowCoop016Status(string text, bool failed)
    {
        Coop016Status.Text = text;
        Coop016Status.Foreground = new SolidColorBrush(failed ? Color.FromRgb(158, 72, 68) : Color.FromRgb(0x7D, 0xAA, 0xA4));
        Coop016Status.Visibility = Visibility.Visible;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        // Restart and Last good save set this before they call here: the tool's own start never asks the older save question.
        var automatic = _startFromTool;
        _startFromTool = false;
        // A Start click while Prepare server (or another action) still works would start or prepare the same
        // folder a second time, and a click while a Start runs would ask about mod changes again. Restart and
        // Last good save call this only after they cleared _busy.
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }

        // Start server uses the saved setup, so mod changes not saved yet are saved or dropped first.
        if (!ConfirmHostModChanges())
            return;
        _loadRetry.ManualStart();
        CancelRescueRetry();
        await StartServerAsync(automatic);
    }

    private bool _startingServer;
    private bool _startFromTool;

    // automatic: the tool started this run itself (load retry, Restart, Last good save), so no question opens.
    private async Task StartServerAsync(bool automatic = false)
    {
        if (_startingServer)
            return;
        _startingServer = true;
        _automaticRun = automatic;
        try
        {
            await StartServerCoreAsync();
        }
        finally
        {
            _startingServer = false;
            _automaticRun = false;
            // A yes to the older save question holds only for the start again after a setup switch.
            if (!_keepSaveSwitchAgreed)
                _saveSwitchAgreed = null;
            _keepSaveSwitchAgreed = false;
        }
        // A Start that failed or ended opens the Mods page again now, not at the next refresh.
        ApplyModListBlock();
    }

    private async Task StartServerCoreAsync()
    {
        if (ServerIsRunning())
        {
            SetControl("A TAOM server is already running. It was left up.");
            RefreshState();
            return;
        }

        var server = _settings.ServerRoot;
        // Read before Start puts anything back. It only changes the wording of a problem (antivirus, CR-88BP7).
        var (goneFiles, goneWorkshop) = await Task.Run(() => GoneServerFiles(server));
        if (!await EnsureServerProgramAsync(server))
            return;
        var exe = Path.Combine(server, ServerProgramRepair.ExeName);

        // Coop Save Rescue on every start (auto mode); players can join while it is on.
        if (!InstallRescue(server))
            return;
        // Coodsz War Sync on every start: players' TAOM war bar follows the server.
        InstallWarSync(server);
        // Coodsz Server Tools on every start: the server rules and the mod settings for players.
        InstallServerTools(server);
        NoteTakenFiles(server, goneWorkshop, goneFiles);

        var save = SelectedSavePath();
        if (!SaveSlot.CanHost(save, ServerSavesDirectory()))
        {
            SetControl("Start server only loads a save already in the server Game Saves folder. Pick the one marked Server copy, or click Name server copy first. A single-player save001 is not the hosted campaign.", failed: true);
            return;
        }

        // A campaign made without TAOM can't load with it, so it is refused before any config or save check.
        if (!TaomServerPath.IsTaomCampaign(save))
        {
            SetControl(TaomServerPath.NotTaomCampaign, failed: true);
            return;
        }

        // TAOM report #44: an earlier save picked in the box would quietly replace the campaign the server ran.
        if (!ConfirmSaveSwitch(save))
        {
            SetControl(SaveSwitchWarning.NotStarted(LastRanSaveName()), failed: true);
            return;
        }

        // A server stopped in the middle of a save leaves this marker. The server puts back the
        // save from before that save when it loads, which is the .sav the header shows now.
        if (SaveSet.MarkerExists(WorldSavedLine.TransactionMarker(ServerSavesDirectory(), Path.GetFileNameWithoutExtension(save))))
            NoteLog("The last save of " + Path.GetFileNameWithoutExtension(save) + " did not finish. The server loads the save from before it.");

        if (!ConfirmSaveTaomVersion(save))
        {
            SetControl("The server was not started. Start a new campaign in Bannerlord on this TAOM version and pick that save.", failed: true);
            return;
        }

        if (UdpPortBusy(4200))
        {
            SetControl("UDP 4200 is already in use. Stop the other dedicated server first. This one was not started.", failed: true);
            return;
        }

        // Low memory does not stop the start; the host reads why the server or game may freeze.
        var memoryWarning = await Task.Run(() => LowMemory.ServerWarning(LowMemory.Read(), LowMemory.GameRunning()));
        if (memoryWarning != null)
        {
            NoteLog(memoryWarning);
            _actions.Note("Low memory", memoryWarning);
        }

        try
        {
            var modules = Path.Combine(server, "engine", "Modules");
            foreach (var parked in CoopStaging.ParkDuplicateCoop(modules, Path.Combine(server, "engine")))
                NoteLog("A second Coop folder was moved out of the server Modules folder: " + Path.GetFileName(parked) + ". Two Coop copies crash TAOM.CoopCompat.");
            // The chosen Coop 0.1.6 server when it is set and pinned, else the Workshop server, as before.
            var steamServer = CoopStaging.FindDedicatedServer(BannerlordFinder.SteamLibraries());
            var workshop = KitCoopServer.RepairServer(steamServer, _settings.Coop016Server);
            var workshopCoop = workshop == null ? null : Path.Combine(workshop, "engine", "Modules", "Coop");
            NoteLog(CoopStaging.EnsurePinnedServerBin(Path.Combine(modules, "Coop"), workshopCoop, chosenServer: workshop != null && !string.Equals(workshop, steamServer, StringComparison.OrdinalIgnoreCase)));
            NoteLog(CoopStaging.DescribeCopies(modules));
            RepairModConfig();
            ApplyKingdomProtection();
            ShowKingdomProtection();
            // After the repair, so a True written with a capital counts as the server will read it.
            if (BannerlordCoopLauncher.Core.Server.WarSailsCheck.Refusal(CoopModConfig.UserPath(), TaomLoadOrder.ServerOrder(HostSetup)) is { } warSails)
                throw new InvalidOperationException(warSails);
            RepairServerConfig();
            var brokenAt = BrokenWorldTime();
            var holdAutosave = BrokenWorldGuard.HoldAutosave(SaveHeader.ReadCreated(save), brokenAt);
            WriteConfig(holdAutosave ? 0 : null);
            _steamAtStart = SteamBox.IsChecked == true;
            _autosaveHeld = holdAutosave;
            _spareUntilOnMap = brokenAt != null && !holdAutosave;
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
            return;
        }

        var framework = ServerFramework.Ensure(server);
        if (!framework.Ready)
        {
            SetControl(framework.Detail, failed: true);
            return;
        }

        if (framework.Detail.Length > 0)
            NoteLog(framework.Detail);
        RememberServerModuleNotes(server);
        if (!ConfirmSaveMatchesSetup(save))
        {
            if (_startAgain)
            {
                // The save mods box picked the setup the save was made with: start again with it.
                _startAgain = false;
                SetControl("Switched to the setup this save was made with. Starting the server again.");
                _keepSaveSwitchAgreed = true;
                var automatic = _automaticRun;
                _ = Dispatcher.BeginInvoke(new Action(() => _ = StartServerAsync(automatic)));
                return;
            }

            SetControl("The server was not started. Pick the setup the save was made with on the Mods page.", failed: true);
            return;
        }

        var acceptTerms = AskServerTerms(server);
        if (acceptTerms == null)
        {
            SetControl(ServerTerms.Refused, failed: true);
            return;
        }

        // Plain TAOM: the sealed TAOM-Coop server, as before. A setup with extra mods: the
        // Coodsz TAOM server, the same server folder's engine started with the setup's list.
        EngineStart? engine = null;
        _startNotes.Clear();
        if (TaomServerPath.Needed(HostSetup))
        {
            engine = await PrepareCoodszServerAsync(server);
            if (engine == null)
                return;
            if (ServerIsRunning())
            {
                SetControl("A TAOM server is already running. It was left up.");
                RefreshState();
                return;
            }
        }

        var start = new ProcessStartInfo
        {
            FileName = engine?.FileName ?? exe,
            Arguments = engine?.Arguments ?? "--data-dir \"" + _settings.DataRoot + "\"",
            WorkingDirectory = engine?.WorkingDirectory ?? server,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // The server writes the OEM code page and reads its commands in it; read as ANSI, a Turkish save
            // name came in bent, and a kick or say with such a letter would reach it bent.
            StandardInputEncoding = ServerConsoleEncoding.Oem(),
            StandardOutputEncoding = ServerConsoleEncoding.Oem(),
            StandardErrorEncoding = ServerConsoleEncoding.Oem(),
            CreateNoWindow = true
        };
        if (engine != null)
        {
            foreach (var (key, value) in engine.Environment)
                start.Environment[key] = value;
        }

        if (acceptTerms == true)
            start.Environment[ServerTerms.EnvironmentVariable] = "true";
        start.Environment[AutoRescue.EnvironmentVariable] = AutoRescue.ModeValue(_rescueNextMode);
        // The server's data folder for Coodsz Server Tools (TaomServerTools): its rules file and its own copy of the MCM files.
        foreach (var (key, value) in TaomServerTools.StartVariables(_settings.DataRoot))
            start.Environment[key] = value;
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        // One log per start, so a restart cannot close the next run's file.
        var consoleLog = new ConsoleLogFile();
        process.OutputDataReceived += (_, args) =>
        {
            consoleLog.Write(args.Data);
            CaptureLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            consoleLog.Write(args.Data);
            CaptureLine(args.Data);
        };
        process.Exited += (_, _) =>
        {
            // Let the last output lines arrive before the file is closed.
            try
            {
                process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
            }

            int? exitCode;
            try { exitCode = process.ExitCode; }
            catch (InvalidOperationException) { exitCode = null; }
            consoleLog.Write("[TAOM host] Server exited.");
            consoleLog.Close();
            Dispatcher.Invoke(() =>
            {
                NoteHostReport(_logTail.ToString(), exited: true);
                // Only this tool's own run that is still the current one (a stop already let go of it).
                // An older run that exits after Restart started the next one never shows the crash bar.
                if (ReferenceEquals(_process, process))
                {
                    _playHold.Stop();
                    // Server files missing from the server folder that started and TAOM.CoopCompat's own
                    // startup check are no load crash: no load mode retry and no Coop Save Rescue.
                    if (IsMissingServerFilesStop(process, exitCode, server))
                        _startCheckStopsInARow = 0;
                    else if (IsStartupCheckStop(exitCode))
                    {
                        if (StartupCheckStopShowsBar)
                            OfferServerCrashReport(process);
                    }
                    else
                    {
                        // Decided once, since Decide uses up the one retry. No bar before the load mode
                        // retry: its own exit shows it if it crashes too.
                        var loadStep = DecideLoadCrash(exitCode);
                        if (LoadCrashRetry.ShowsCrashBar(loadStep))
                            OfferServerCrashReport(process);
                        OnRescueExit(process, exitCode, loadStep);
                    }
                }
                RefreshState();
                ShowSaveDetail();
            });
        };
        // A new start follows only its own state events.
        _fatalState = null;
        _playHold.Stop();
        BeginRescueRun();
        // This run reads only its own logs and its own console lines, never the last run's SERVING.
        _runStartedUtc = DateTime.UtcNow.AddSeconds(-2);
        // Nor the last run's players: a stale list would let a ban kick or a queued kick go by id into this run.
        ForgetPlayers();
        // The Cheats page reads the players and lists of the new run.
        CoopCheats.ServerStarting();
        _logTail.Clear();
        _seenServing = false;
        // A new run: its exit is a crash unless Stop, Restart or this tool ends it.
        _stopAsked = false;
        _servingCheckedPath = "";
        _servingScanOffset = 0;
        // The program that really runs: BannerlordCoopServer.exe for plain TAOM, the engine's
        // dotnet.exe for the Coodsz TAOM server.
        consoleLog.Open(Path.Combine(_settings.DataRoot, "logs"), "[TAOM host] Start " + start.FileName + " " + start.Arguments);
        // What the tool did to the server folder before this start, kept with the run's log.
        foreach (var note in _startNotes)
            consoleLog.Write("[TAOM host] " + note);
        _startNotes.Clear();
        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception ex)
        {
            consoleLog.Close();
            // A dotnet.exe or exe that was there at the last start that worked is worded as antivirus.
            SetControl(AntivirusWording.Reword("The server did not start. " + ex.Message, start.FileName, goneFiles, server, goneWorkshop, "Start server"), failed: true);
            return;
        }

        if (!started)
        {
            consoleLog.Close();
            SetControl("The server did not start.", failed: true);
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _blockedDllNoted = false;
        _joinAdvice.Clear();
        _sharedSteamIds.Reset();
        _process = process;
        // The Start question compares against this save until a later Start's server runs.
        _settings.LastHostedSave = Path.GetFileNameWithoutExtension(save);
        // The server already runs, so a settings file that cannot be written only keeps the name until the next save.
        try { SaveSettings(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        RememberServerFiles(server);
        // The last run's crash bar goes once the server runs again, unless this start is the retry
        // with the deeper save fixes, the tool's own answer to that crash.
        if (_rescueNextMode != RescueMode.Load)
            _crashBar?.ServerStarted();
        _speedAtStart = SavedGameSpeed();
        _actions.NotePassword(!string.IsNullOrWhiteSpace(PasswordBox.Text));
        _actions.Note("Start", "Save=" + Path.GetFileNameWithoutExtension(save)
            + ". ServerRoot set=" + !string.IsNullOrWhiteSpace(_settings.ServerRoot)
            + ". AutosaveHeld=" + _autosaveHeld);
        SetControl(_autosaveHeld
            ? BrokenWorldGuard.AutosaveHeldText
            : "Server is starting. It is ready when the log says SERVING. The first start of a new campaign builds the map cache and can take up to an hour; the status at the top shows how far it is.", failed: _autosaveHeld);
        RefreshState();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => _ = ShowWorldSave();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        if (!ConfirmWindow.Ask(this, "Stop server", "Stop the TAOM server? It saves the campaign first, then players are disconnected."))
            return;
        _actions.Note("Stop", "Requested stop");
        _ = SaveThenStop(restart: false);
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        if (!ConfirmWindow.Ask(this, "Restart server", "Restart the TAOM server? It saves the campaign first. Players are disconnected, then the same save loads again."))
            return;
        _actions.Note("Restart", "Requested restart");
        _ = SaveThenStop(restart: true);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e) => SaveSettingsPage();

    // The Settings page's one save. The Game speed buttons on the Dashboard use it too.
    // fromSettingsPage false: the Dashboard game speed buttons, which restart the server right after, so the
    // greyed difficulty line (start the server once, save again) belongs only to Save settings itself.
    private bool SaveSettingsPage(bool fromSettingsPage = true)
    {
        try
        {
            // Read before mod-config.json changes: an untouched kingdom dropdown keeps following Coop's option.
            var serverRules = ServerRulesFromPage();
            // The rules first: they live in their own file, so a host who has not started the server yet (no server-config.json
            // or mod-config.json, which the saves below need) still keeps them. Coop's kingdom option follows below.
            SaveServerRules(serverRules);
            WriteConfig();
            SaveGameplaySettings();
            SaveTimePermissions();
            // The save already worked; a file that cannot be read again only leaves a box blank.
            try { RefillEmptySettings(); } catch (Exception) { }
            _actions.NotePassword(!string.IsNullOrWhiteSpace(PasswordBox.Text));
            _actions.Note("Settings", "Saved host settings. Save=" + Path.GetFileNameWithoutExtension(SelectedSavePath() ?? ""));
            if (_difficultySkipped > 0)
                _actions.Note("Settings", "Skipped " + _difficultySkipped + " greyed out difficulty settings (not in mod-config.json yet)");
            SetControl(_difficultySkipped > 0 && fromSettingsPage
                ? CoopDifficulty.SkippedOnSave
                : "Saved settings. Restart the server to use them. Fast forward stays as it is until that restart.");
            RefreshGameSpeed();
            return true;
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
            return false;
        }
    }

    private void StopServer()
    {
        _stopAsked = true;
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            NoteLog(ex.Message);
        }

        foreach (var process in Process.GetProcessesByName("BannerlordCoopServer"))
        {
            try
            {
                string? path = null;
                try { path = process.MainModule?.FileName; }
                catch (Exception) { }
                if (!string.IsNullOrWhiteSpace(_settings.ServerRoot) && path != null && !DedicatedServerWatch.IsUnder(path, _settings.ServerRoot))
                    continue;
                process.Kill(entireProcessTree: true);
                NoteLog("Stopped server process " + process.Id + ".");
            }
            catch (Exception ex)
            {
                NoteLog(ex.Message);
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (var process in Process.GetProcessesByName("dotnet"))
        {
            try
            {
                string? path = null;
                try { path = process.MainModule?.FileName; }
                catch (Exception) { }
                if (!DedicatedServerWatch.IsEngine(path, _settings.ServerRoot))
                    continue;
                process.Kill(entireProcessTree: true);
                NoteLog("Stopped server engine " + process.Id + ".");
            }
            catch (Exception ex)
            {
                NoteLog(ex.Message);
            }
            finally
            {
                process.Dispose();
            }
        }

        _process = null;
        ForgetPlayers();
    }

    private async Task ShowWorldSave()
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        if (!ServerIsRunning())
        {
            SetControl("The server is stopped, so there is nothing to save.");
            RefreshState();
            return;
        }

        var refusal = SaveRefusal();
        if (refusal != null)
        {
            SetControl(refusal, failed: true);
            return;
        }

        _busy = true;
        ShowSaving();
        try
        {
            var saved = await Task.Run(WaitForWorldSave);
            SetControl(saved ? "Saved " + ConfiguredSaveName() + "." : "The server did not finish saving.", failed: !saved);
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            _saving = false;
            _busy = false;
            RefreshState();
            // The chips and the save check read the .sav header, which a save just rewrote.
            ShowSaveDetail();
        }
    }

    private async Task SaveThenStop(bool restart)
    {
        // Start only holds _startingServer while it prepares; a server that is loading can still be stopped.
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        if (!ServerIsRunning())
        {
            SetControl("The server is already stopped.");
            RefreshState();
            return;
        }

        // A campaign that did not load is never saved or sent stop, like a broken world.
        var refusal = SaveRefusal(stopping: true);
        _busy = true;
        var saved = false;
        var startAgain = false;
        _stopAsked = true;
        try
        {
            if (refusal == null)
            {
                if (restart)
                    await WarnPlayersOfRestart();
                ShowSaving();
                SetControl("Saving the world, then the server will stop.");
                saved = await Task.Run(WaitForWorldSave);
                // A broken world is never sent stop: stop writes the save on stop.
                await Task.Run(AskServerToStop);
            }

            StopServer();
            _saving = false;
            RefreshState();
            ShowSaveDetail();
            SetControl(refusal ?? (saved
                ? "Saved " + ConfiguredSaveName() + ", then stopped."
                : "Stopped. No new save was written."), failed: refusal != null);
            if (restart)
                startAgain = true;
            else if (refusal == null)
                ScanAfterStop();
        }
        catch (Exception ex)
        {
            _saving = false;
            SetControl(ex.Message, failed: true);
            RefreshState();
        }
        finally
        {
            _busy = false;
        }

        // After _busy is cleared, so Start server's own wait check lets the restart through and its Prepare
        // keeps _busy for itself. The restart hosts the save the server ran, not a later pick in the box.
        if (startAgain)
        {
            SelectHostedSave(LastRanSaveName());
            _startFromTool = true;
            Start_Click(this, new RoutedEventArgs());
        }
    }

    private void ShowSaving()
    {
        _saving = true;
        StateText.Text = "SAVING";
        StateDetail.Text = "Writing the campaign.";
        UptimeText.Text = "Saving.";
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(176, 132, 72));
    }

    private void RestartLastGood_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        var name = ConfiguredSaveName();
        if (name.Length == 0)
        {
            SetControl("Prepare the server before loading a save.", failed: true);
            return;
        }

        var brokenAt = BrokenWorldTime();
        if (brokenAt == null)
        {
            SetControl("The hero can rejoin this world. Last good save is kept for when they cannot.");
            return;
        }

        KeepLastGoodCopy();
        var live = Path.Combine(ServerSavesDirectory(), name + ".sav");
        var pick = LastGoodSave.Pick(SaveCopies(name), brokenAt);
        if (string.IsNullOrEmpty(pick) || pick.Equals(live, StringComparison.OrdinalIgnoreCase))
        {
            SetControl("No save from before the break is on disk. Later autosaves replaced those copies. This world was not saved.", failed: true);
            return;
        }

        var created = SaveHeader.ReadCreated(pick);
        var when = created?.ToString("h:mm tt") ?? "an earlier time";
        if (!ConfirmWindow.Ask(this, "Last good save", "Restart on the save from " + when + "? The world running now is not written. Players disconnect, then that earlier save loads."))
            return;

        var loaded = false;
        _busy = true;
        try
        {
            StopServer();
            LastGoodSave.CopyOver(pick, live);
            var jsonSource = Path.Combine(ServerSavesDirectory(), Path.GetFileNameWithoutExtension(pick) + ".json");
            var jsonDestination = Path.Combine(ServerSavesDirectory(), name + ".json");
            if (File.Exists(jsonSource))
                LastGoodSave.CopyOver(jsonSource, jsonDestination);
            _actions.Note("Last good save", "Restored " + name + " from " + Path.GetFileName(pick));
            SetControl("Loaded the save from " + when + ". Starting the server.");
            loaded = true;
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            _busy = false;
        }

        // After _busy is cleared, the same as the restart after Save and stop. The restored save is the one the server ran, so
        // it loads with no question.
        if (loaded)
        {
            SelectHostedSave(name);
            _startFromTool = true;
            Start_Click(this, new RoutedEventArgs());
        }
    }

    private void KeepLastGoodCopy()
    {
        try
        {
            var name = ConfiguredSaveName();
            if (name.Length == 0)
                return;
            var directory = ServerSavesDirectory();
            var destination = Path.Combine(directory, name + LastGoodSave.Suffix + ".sav");
            var pick = LastGoodSave.Pick(SaveCopies(name), BrokenWorldTime());
            if (string.IsNullOrEmpty(pick) || pick.Equals(destination, StringComparison.OrdinalIgnoreCase))
                return;
            var pickedAt = SaveHeader.ReadCreated(pick);
            var keptAt = File.Exists(destination) ? SaveHeader.ReadCreated(destination) : null;
            if (pickedAt != null && keptAt != null && keptAt >= pickedAt)
                return;
            LastGoodSave.CopyOver(pick, destination);
            var jsonSource = Path.Combine(directory, Path.GetFileNameWithoutExtension(pick) + ".json");
            var jsonDestination = Path.Combine(directory, name + LastGoodSave.Suffix + ".json");
            if (File.Exists(jsonSource))
                LastGoodSave.CopyOver(jsonSource, jsonDestination);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private List<SaveCopy> SaveCopies(string name)
    {
        var directory = ServerSavesDirectory();
        var copies = new List<SaveCopy>();
        foreach (var fileName in new[] { name + ".sav", name + ".backup1.sav", name + ".backup2.sav", name + LastGoodSave.Suffix + ".sav" })
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
                continue;
            copies.Add(new SaveCopy(path, SaveHeader.ReadCreated(path)));
        }

        return copies;
    }

    private string? BrokenWorldRefusal() =>
        BrokenWorldGuard.Refusal(RecentWorldLog(out _));

    private bool CampaignLoadFailed() => BrokenWorldGuard.LoadFailed(_fatalState, ReadNewestLog());

    // Every path that saves or backs up asks this: a failed load first, then the broken world.
    private string? SaveRefusal(bool stopping = false) =>
        CampaignLoadFailed() ? (stopping ? BrokenWorldGuard.FailedLoadStopText : BrokenWorldGuard.FailedLoadSaveText) : BrokenWorldRefusal();

    private DateTime? BrokenWorldTime()
    {
        var time = BrokenWorldGuard.BrokenTime(RecentWorldLog(out var written));
        if (time == null || written == null)
            return null;
        var clock = new DateTime(written.Value.Year, written.Value.Month, written.Value.Day, written.Value.Hour, written.Value.Minute, written.Value.Second, DateTimeKind.Unspecified);
        var stamp = new DateTime(clock.Year, clock.Month, clock.Day, time.Value.Hours, time.Value.Minutes, time.Value.Seconds, DateTimeKind.Unspecified);
        if (stamp > clock.AddMinutes(2))
            stamp = stamp.AddDays(-1);
        return stamp;
    }

    private string RecentWorldLog(out DateTime? writtenAt)
    {
        writtenAt = null;
        var logs = Path.Combine(_settings.DataRoot, "logs");
        if (!Directory.Exists(logs))
            return "";
        var text = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(logs, "*.log", SearchOption.AllDirectories)
                     .Select(path => new FileInfo(path))
                     .Where(file => file.Length > 0 && (file.Name.StartsWith("Coop_server", StringComparison.OrdinalIgnoreCase) || file.Name.StartsWith("coop-server", StringComparison.OrdinalIgnoreCase)))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Take(4))
        {
            var tail = HostLogFile.ReadTail(file.FullName, 400_000);
            if (!tail.Contains(BrokenWorldGuard.Marker, StringComparison.OrdinalIgnoreCase))
            {
                var older = HostLogFile.ReadTailContaining(file.FullName, BrokenWorldGuard.Marker, 400_000);
                if (older.Contains(BrokenWorldGuard.Marker, StringComparison.OrdinalIgnoreCase))
                    tail = older;
            }
            text.AppendLine(tail);
            if (writtenAt == null || file.LastWriteTime > writtenAt)
                writtenAt = file.LastWriteTime;
        }

        return text.ToString();
    }

    private bool WaitForWorldSave()
    {
        var name = ConfiguredSaveName();
        var directory = Path.Combine(_settings.DataRoot, "Game Saves");
        var sav = Path.Combine(directory, name + ".sav");
        var marker = WorldSavedLine.TransactionMarker(directory, name);
        var beforeSav = File.Exists(sav) ? File.GetLastWriteTimeUtc(sav) : DateTime.MinValue;
        var beforeCount = Volatile.Read(ref _worldSavedCount);
        var owned = _process is { HasExited: false };
        if (owned)
        {
            // In its turn of the command queue; this runs off the window thread, so it may wait for that turn here.
            var process = _process!;
            if (!WriteQueued(() => WriteLine(process, "save")).GetAwaiter().GetResult())
                return false;
        }
        // A server this window did not start gets the save through its console queue, in its turn of the command queue too.
        else if (!WriteQueued(AskQueuedSave).GetAwaiter().GetResult())
        {
            return false;
        }

        // The .json and the .autosave-transaction marker are written when the save starts and
        // the .sav last, so only the server's "world saved" line, or a newer .sav with the marker
        // gone, means the save is on disk. Stopping before that loses the save: the server puts
        // the old one back on its next start.
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            if (owned && Volatile.Read(ref _worldSavedCount) != beforeCount && WorldSavedLine.SameSave(_lastWorldSavedFile, name))
                return true;
            if (SaveFileFinished(sav, marker, beforeSav))
                return true;
            if (owned && _process is not { HasExited: false })
                return false;
            Thread.Sleep(250);
        }

        return false;
    }

    private static bool SaveFileFinished(string sav, string marker, DateTime beforeSav)
    {
        if (!File.Exists(sav) || File.GetLastWriteTimeUtc(sav) <= beforeSav || SaveSet.MarkerExists(marker))
            return false;
        // The .sav stops growing once it is written.
        var size = new FileInfo(sav).Length;
        Thread.Sleep(1000);
        return !SaveSet.MarkerExists(marker) && new FileInfo(sav).Length == size;
    }

    private void RunPrepare(
        string kit,
        string serverSource,
        string modules,
        string serverRoot,
        string dataRoot,
        string save,
        string password,
        string autosaveText,
        bool steamRelay,
        bool logFile)
    {
        var script = Path.Combine(kit, "Tools", "Prepare-Host.ps1");
        if (!File.Exists(script))
            throw new InvalidOperationException("Prepare-Host.ps1 is missing from the host kit.");

        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Arg(script)
                + " -NonInteractive"
                + " -ServerSource " + Arg(serverSource)
                + " -GameModules " + Arg(modules)
                + " -TaomModules " + Arg(modules)
                + " -TargetRoot " + Arg(serverRoot)
                + " -DataRoot " + Arg(dataRoot)
                + " -SaveFile " + Arg(save),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start.");
        process.ErrorDataReceived += (_, args) => CaptureLine(args.Data);
        process.BeginErrorReadLine();
        while (process.StandardOutput.ReadLine() is { } line)
            NoteLog(line);
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Host preparation stopped. Exit code " + process.ExitCode + ".");
        var framework = ServerFramework.Ensure(serverRoot);
        if (!framework.Ready)
            throw new InvalidOperationException(framework.Detail);
        if (framework.Detail.Length > 0)
            NoteLog(framework.Detail);
        if (!int.TryParse(autosaveText, out var autosave))
            autosave = 15;
        WriteConfigFile(Path.Combine(dataRoot, "server-config.json"), Path.GetFileNameWithoutExtension(save), password, autosave, steamRelay, logFile);
        SetControl("Server prepared.");
    }

    private void WriteConfig(int? autosaveOverride = null)
    {
        // An empty box keeps the minutes already in server-config.json; only bad text stops the save.
        var autosave = HostConfigEditor.ParseAutosave(AutosaveBox.Text);
        // While the server runs it keeps its own save, so Restart loads the same save again. The box pick goes in at the next Start.
        var hosted = ServerIsRunning() ? ConfiguredSaveName() : "";
        var saveName = hosted.Length > 0 ? hosted : Path.GetFileNameWithoutExtension(SelectedSavePath());
        WriteConfigFile(Path.Combine(_settings.DataRoot, "server-config.json"), saveName, PasswordBox.Text, autosaveOverride ?? autosave, SteamBox.IsChecked == true, LogFileBox.IsChecked != false);
        SaveSettings();
    }

    private static void WriteConfigFile(string path, string saveName, string password, int? autosave, bool steam, bool logFile)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException("server-config.json was not found. Prepare the server first.");
        var updated = HostConfigEditor.Apply(File.ReadAllText(path), saveName, password, autosave, steam, logFile);
        File.WriteAllText(path, updated);
    }

    // The person hosting reads the Coop server terms and decides; the tool never
    // accepts on its own. Returns null when they were not accepted, true when the
    // server should be told they were, false when the server has no terms to accept.
    private bool? AskServerTerms(string serverFolder)
    {
        var hash = ServerTerms.TermsHash(serverFolder);
        if (hash == null)
            return false;
        if (!ServerTerms.NeedsAnswer(hash, _settings.AcceptedEulaSha256))
            return true;
        if (!ConfirmWindow.Ask(this, ServerTerms.Title, ServerTerms.Body, ServerTerms.TermsText(serverFolder), ServerTerms.AcceptButton))
        {
            _actions.Note("Server terms", "Not accepted");
            return null;
        }

        _settings.AcceptedEulaSha256 = hash;
        SaveSettings();
        _actions.Note("Server terms", "Accepted " + hash[..12]);
        NoteLog("You accepted the Coop server terms. The server saves that in the Data folder's eula.txt.");
        return true;
    }

    private static string HostKitFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TAOM-Coop", "HostKit-" + TaomStack.KitVersion);

    private static string EnsureHostKit(Action<string> log)
    {
        var kit = HostKitFolder;
        var script = Path.Combine(kit, "Tools", "Prepare-Host.ps1");
        if (File.Exists(script))
            return kit;

        var zip = FindHostKitZip(log);
        if (zip.From == CompatZipFrom.WriteFailed)
            throw new InvalidOperationException("The built in copy of " + TaomStack.HostKitName + " could not be written to " + TaomCompatZip.Folder() + ". Check that the drive has free space and the folder isn't blocked, then try again.");
        return ExtractHostKit(zip)
            ?? throw new InvalidOperationException("The TAOM-Coop " + TaomStack.KitVersion + " CompatOnly-HOST-OPERATOR zip was not found on this PC. Put it in Downloads.");
    }

    // The HOST-OPERATOR zip with the pinned hash on this PC, else the copy a release build carries (TaomCompatZip).
    // When that copy could not be written out, the log says why.
    private static CompatZipPick FindHostKitZip(Action<string> log)
    {
        var pick = TaomCompatZip.Find(TaomCompatKit.Host, wholePc: true);
        if (pick.From == CompatZipFrom.WriteFailed)
            log("Could not write the built in " + TaomStack.HostKitName + " to " + TaomCompatZip.Folder() + ". " + pick.Problem);
        return pick;
    }

    private static string? ExtractHostKit(CompatZipPick pick)
    {
        var zip = pick.Path;
        if (zip == null)
            return null;
        var kit = HostKitFolder;
        Directory.CreateDirectory(kit);
        ZipFile.ExtractToDirectory(zip, kit, overwriteFiles: true);
        return kit;
    }

    // A prepared folder that lost BannerlordCoopServer.exe (antivirus, most likely) cannot be prepared
    // again without risking the campaign, so Start puts back only the server program files from the
    // host kit, the engine bin DLLs from the Workshop server and the module DLLs from the game's Modules
    // folder (ServerProgramRepair). It only adds missing files. The Data folder, saves and configs are not touched.
    private async Task<bool> EnsureServerProgramAsync(string server)
    {
        // The engine bin DLLs come back only from the Workshop Coop dedicated server folder, or the chosen Coop 0.1.6
        // server when it is set and pinned, checked against pinned hashes. Its exe is not needed, antivirus may have taken that one too.
        var workshop = KitCoopServer.RepairServer(WorkshopServerFolder(), _settings.Coop016Server);
        // The module DLLs come back only from the game's Modules folder, checked against pinned hashes.
        var gameModules = GameModulesFolder();
        var running = ServerIsRunning();
        var state = await Task.Run(() => ServerProgramRepair.Check(server, running, workshop, gameModules, compatByHash: true));
        if (state == ServerProgramState.NotPrepared)
        {
            SetControl("Prepare the server before starting it.", failed: true);
            return false;
        }

        // The TAOM.CoopCompat v2 of the new TAOM download for 1.5.7 in the server folder fails the kit preflight, and
        // these tools never replace it (CR-XXP67), so the start stops here with why.
        if (await Task.Run(() => NewTaomCompat.Problem(Path.Combine(server, ServerProgramRepair.CompatFolder))) is { } kept)
        {
            SetControl(kept, failed: true);
            return false;
        }

        if (state != ServerProgramState.NeedsRepair)
        {
            NoteUnrestorableServerFiles(server);
            return true;
        }

        var dataRoot = _settings.DataRoot;
        ServerProgramRepairResult? result = null;
        try
        {
            result = await Task.Run(() => RepairServerProgram(server, dataRoot, workshop, gameModules, NoteLog));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            NoteLog(ex.Message);
        }

        if (result is { Restored.Count: > 0 })
            NoteLog("Put back: " + string.Join(", ", result.Restored));
        if (result is { StillMissing.Count: > 0 })
            NoteLog("Still missing in the server folder: " + string.Join(", ", result.StillMissing));
        _actions.Note("Server files", "Restored=" + (result == null ? "none" : string.Join(",", result.Restored)) + " Missing=" + (result == null ? "?" : string.Join(",", result.StillMissing)));

        if (!File.Exists(Path.Combine(server, ServerProgramRepair.ExeName)))
        {
            SetControl("BannerlordCoopServer.exe is missing from the server folder and could not be put back. Antivirus may have removed it. Restore it from your antivirus quarantine, add the server folder as an exclusion, then click Start server again. Your campaign was not touched.", failed: true);
            return false;
        }

        // Without 0Harmony.dll the server dies 2 seconds in, so it is not started. The same for a DLL the
        // Workshop server has a checked copy of that could not be put back.
        var missingBin = await Task.Run(() => ServerProgramRepair.BlockingEngineBinFiles(server, workshop));
        if (missingBin.Count > 0)
        {
            SetControl(MissingServerFilesText(missingBin), failed: true);
            return false;
        }

        // Without a module DLL the game's Modules folder has, TAOM's types fail to load 3 seconds in
        // (CR-KFRG6), so one that could not be put back stops the start. Others only go to the action log.
        var missingModules = await Task.Run(() => ServerProgramRepair.BlockingModuleFiles(server, gameModules));
        if (missingModules.Count > 0)
        {
            SetControl(MissingModuleFilesText(missingModules), failed: true);
            return false;
        }

        // Without a TAOM.CoopCompat DLL its preflight stops every start (CR-HZYX5), and with one that is not the
        // pinned file too (CR-XXP67), so one that could not be put back from the host kit stops the start here.
        var missingCompat = await Task.Run(() => ServerProgramRepair.MissingCompatFiles(server, byHash: true));
        if (missingCompat.Count > 0)
        {
            var changed = missingCompat.Where(path => File.Exists(Path.Combine(server, path))).ToList();
            SetControl(changed.Count > 0 ? ChangedCompatFilesText(changed) : MissingCompatFilesText(missingCompat), failed: true);
            return false;
        }

        NoteUnrestorableServerFiles(server);
        if (result is { Restored.Count: > 0 })
        {
            SetControl(result.Restored.All(ServerProgramRepair.IsKitFile)
                ? "Start server put back missing server program files from the TAOM host kit. Antivirus may have removed them. Your campaign and Data folder were not touched."
                : "Start server put back missing server files, each checked against its known hash. Antivirus may have removed them. Your campaign and Data folder were not touched.");
        }
        return true;
    }

    private static string MissingServerFilesText(IEnumerable<string> names) =>
        "The server folder is missing " + string.Join(", ", names) + " in engine\\bin\\Win64_Shipping_Server, so the server cannot start. Antivirus most likely removed them. Restore them from your antivirus quarantine and add the server folder as an exclusion, or in Steam unsubscribe from Workshop item 3770450698 and subscribe again so it downloads again, then click Start server again. Your campaign was not touched.";

    // Each path is relative to the server folder, so the player sees which folder each file belongs in.
    private static string MissingModuleFilesText(IEnumerable<string> paths) =>
        "The server folder is missing " + string.Join(", ", paths) + ", so the server cannot start. Antivirus most likely removed them. Restore them from your antivirus quarantine and add the server folder as an exclusion, then click Start server again. If they are not in quarantine, verify Bannerlord in Steam and check that TAOM is installed, and Start server puts them back from the game's Modules folder. Your campaign was not touched.";

    // Start server takes these from the TAOM host kit, so they are only still missing when the kit copy is gone too.
    private static string MissingCompatFilesText(IEnumerable<string> paths) =>
        "The server folder is missing " + string.Join(", ", paths) + ", so the server cannot start, and the copy in the TAOM host kit could not be used. Antivirus most likely removed them. Click Fix antivirus, restore the files from your antivirus quarantine, then click Start server again. Your campaign was not touched.";

    // A TAOM.CoopCompat file that is there but not the pinned one, which the kit copy could not replace.
    private static string ChangedCompatFilesText(IEnumerable<string> paths) =>
        "In the server folder, " + string.Join(", ", paths) + " changed and the right file from the TAOM host kit could not be put back, so the server cannot start. Antivirus may have quarantined or changed it. Click Fix antivirus, then click Start server again. Your campaign was not touched.";

    // A missing engine bin DLL with no checked copy to put back does not stop the start, since a healthy
    // server can lack one (the staging moves only what the main bin lacks). It goes to the action log, and
    // a run that really dies on it is caught by the exit check. The same for a module DLL the game's
    // Modules folder has no source for (an older host kit or another TAOM version may not stage it); a
    // module DLL with a source never gets here, since it stops the start.
    private void NoteUnrestorableServerFiles(string server)
    {
        var missing = ServerProgramRepair.MissingEngineBinFiles(server);
        if (missing.Count > 0)
            _actions.Note("Server files", "Missing in engine bin, no checked copy to put back, starting anyway: " + string.Join(",", missing));
        var modules = ServerProgramRepair.MissingModuleFiles(server);
        if (modules.Count > 0)
            _actions.Note("Server files", "Module files missing with no source in the game's Modules folder, starting anyway: " + string.Join(",", modules));
    }

    private string? GameModulesFolder() => _gameRoot == null ? null : Path.Combine(_gameRoot, "Modules");

    private static string? WorkshopServerFolder()
    {
        try
        {
            var module = CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries());
            return module == null ? null : Path.Combine(module, "DedicatedServer");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static ServerProgramRepairResult RepairServerProgram(string server, string dataRoot, string? workshop, string? gameModules, Action<string> log)
    {
        // The host kit is needed only for a missing wrapper file or a missing or changed TAOM.CoopCompat file, so a PC
        // without its zip still gets the other DLLs back.
        var needsKit = ServerProgramRepair.Files.Any(file => !File.Exists(Path.Combine(server, file.Target)))
            || ServerProgramRepair.MissingCompatFiles(server, byHash: true).Count > 0;
        var hostKit = needsKit ? EnsureHostKit(log) : null;
        var result = ServerProgramRepair.Repair(server, dataRoot, hostKit == null ? null : ServerProgramRepair.FindWrapperArtifact(hostKit), workshop,
            gameModules: gameModules, compatModule: TaomServerFiles.KitCompatModule(hostKit), log: log);
        if (!result.StillMissing.Any(ServerProgramRepair.IsKitFile))
            return result;
        // The host kit's own copy may be gone too (antivirus takes the kit's TAOM.CoopCompat DLLs as well):
        // unpack the host kit zip again once and retry.
        var kit = ExtractHostKit(FindHostKitZip(log));
        if (kit == null)
            return result;
        var again = ServerProgramRepair.Repair(server, dataRoot, ServerProgramRepair.FindWrapperArtifact(kit), workshop, gameModules: gameModules,
            compatModule: TaomServerFiles.KitCompatModule(kit), log: log);
        return new ServerProgramRepairResult(result.Restored.Concat(again.Restored).ToList(), again.StillMissing);
    }

    private static string Arg(string value) => "\"" + value.Replace("\"", "") + "\"";

    private static string LocalAddress()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
                continue;
            foreach (var address in adapter.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                    return address.Address.ToString();
            }
        }

        return "127.0.0.1";
    }

    private async Task RunAsync(Action work)
    {
        _busy = true;
        try
        {
            await Task.Run(work);
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetControl(string message, bool failed = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetControl(message, failed));
            return;
        }

        _controlNote = message;
        ControlStatus.Text = message;
        ControlStatus.Foreground = new SolidColorBrush(failed ? Color.FromRgb(158, 72, 68) : Color.FromRgb(0x7D, 0xAA, 0xA4));
        ShowOnServerPage(message, failed);
        NoteLog(message);
    }

    // Prepare server sits on the Saves page (the "Server" page in the code, NavServer), away from the note under Start server, so a player there saw nothing
    // when it stopped (the reason showed only on the Dashboard). The note shows there too. It scrolls into view only
    // for a problem or when it first shows.
    private void ShowOnServerPage(string message, bool failed)
    {
        var first = ServerPageStatusCard.Visibility != Visibility.Visible;
        ServerPageStatus.Text = message;
        ServerPageStatus.Foreground = ControlStatus.Foreground;
        ServerPageStatusCard.Visibility = Visibility.Visible;
        if ((failed || first) && ServerPage.Visibility == Visibility.Visible)
            ServerPageStatusCard.BringIntoView();
    }

    // The English text of the note under Start server, as SetControl last wrote it.
    private string _controlNote = "";

    private static bool UdpPortBusy(int port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(end => end.Port == port);

    private static string? FindServerModuleXml(string modules, string moduleId)
    {
        if (!Directory.Exists(modules))
            return null;
        foreach (var directory in Directory.EnumerateDirectories(modules))
        {
            var xml = Path.Combine(directory, "SubModule.xml");
            if (!File.Exists(xml))
                continue;
            var identity = ModuleIdentityReader.Read(xml);
            if (identity.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase)
                || new DirectoryInfo(directory).Name.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
                return xml;
        }

        return null;
    }

    private void RememberServerModuleNotes(string serverRoot)
    {
        foreach (var note in TaomModNotes.For(HostSetup))
            NoteLog(note);
        var modules = Path.Combine(serverRoot, "engine", "Modules");
        foreach (var id in TaomStack.ServerModules)
        {
            if (ServerModuleNotes.KeepsClientCampaign(id))
                continue;
            var xmlPath = FindServerModuleXml(modules, id);
            if (xmlPath == null)
                continue;
            var note = ServerModuleNotes.SkippedCode(id, File.ReadAllText(xmlPath));
            if (note != null)
                NoteLog(note);
        }
    }
}
