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
using BurningEmpires.Core;
using BurningEmpires.Ui;
using TaomCoop.Core;

namespace BurningEmpiresHost;

public partial class MainWindow
{
    // The Coop port players join on (BeStack.DirectPort).
    private static readonly int DirectPort = int.Parse(BeStack.DirectPort, System.Globalization.CultureInfo.InvariantCulture);

    // Counts the server starts; the console handlers of a run only pass lines on while it is the newest.
    private int _consoleRunId;

    private bool IsCurrentRun(int runId) => Volatile.Read(ref _consoleRunId) == runId;

    // Prepare server: a whitelist copy of the Workshop Coop dedicated server into the server folder
    // (BeServerPrepare). It deletes nothing, so it can run again after a Coop update, and the Burning
    // Empires core goes in on every Start server.
    private async void Prepare_Click(object sender, RoutedEventArgs e)
    {
        // Start keeps _busy false while it starts, so _startingServer counts as busy too.
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

        await PrepareServerAsync();
    }

    // A picked Coop server folder that is gone: say so once and forget it, so the line doesn't repeat on every start
    // and the folder doesn't come back by itself if something with that name turns up there later.
    private void ForgetGonePick(Action<string> note)
    {
        note(BeServerPrepare.PickedGoneNote);
        _settings.CoopServerFolder = "";
        SaveSettings();
    }

    // Where Prepare server and Start server copy the Coop server from: the folder picked with Choose Coop server,
    // or the Steam Workshop server (BeServerPrepare.Source).
    private BeServerSource? CoopServerSource(IEnumerable<string> libraries) =>
        BeServerPrepare.Source(_settings.CoopServerFolder, BeServerPrepare.FindWorkshopServer(libraries));

    // Choose Coop server: a Coop dedicated server the host unzipped himself (Coop 0.1.7's server download linked from
    // Coop's release, before the Workshop has it). With one picked already, the host can go back to the Workshop.
    private void PickCoopServer_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            SetControl("Choose Coop server waits until the tool finishes what it is doing now. Click it again in a moment.", failed: true);
            return;
        }

        // The source only changes between runs, so a running server keeps the files it started with.
        if (ServerIsRunning())
        {
            SetControl("Stop the server before you choose another Coop server.", failed: true);
            return;
        }

        // Once the Workshop's Coop is newer than the picked one, Prepare server already takes the Workshop server.
        var workshopNewer = CoopServerSource(BannerlordFinder.SteamLibraries()) is { Picked: false };
        if (BeServerPrepare.PickedServer(_settings.CoopServerFolder) is { } current
            && ConfirmWindow.Ask(this, BeServerPrepare.PickButton, workshopNewer
                ? "The Steam Workshop Coop server is newer than the one in " + current + ", so Prepare server copies the Workshop one now. Stop using the folder you picked?"
                : "Prepare server copies the Coop server in " + current + ". Use the Steam Workshop Coop server again instead?", null, "Use the Workshop server"))
        {
            _settings.CoopServerFolder = "";
            SaveSettings();
            SetControl("Prepare server copies the Steam Workshop Coop server again. Click Prepare server.");
            return;
        }

        var dialog = new OpenFolderDialog { Title = BannerlordCoopLauncher.Core.Localization.Translator.T("Pick the folder with BannerlordCoopServer.exe"), Multiselect = false };
        if (dialog.ShowDialog(this) != true)
            return;
        var server = BeServerPrepare.PickedServer(dialog.FolderName);
        if (server == null)
        {
            SetControl(BeServerPrepare.PickedNotServer, failed: true);
            return;
        }

        _settings.CoopServerFolder = server;
        SaveSettings();
        _actions.Note("Choose Coop server", "Coop server folder set");
        var version = BeServerPrepare.ServerCoopVersion(server);
        // Source takes the Workshop server when its Coop is newer than the picked one, so the status says which one is used.
        if (CoopServerSource(BannerlordFinder.SteamLibraries()) is { Picked: false } workshopSource)
            SetControl(BeServerPrepare.PickedButWorkshopNewer(server, workshopSource.Folder));
        else SetControl(version.Length > 0
            ? "Prepare server now copies the Coop server in " + server + " (Coop " + version + "). Click Prepare server."
            : "Prepare server now copies the Coop server in " + server + ". Click Prepare server.");
    }

    // Download Coop server: the browser opens the server .7z linked from Coop's release (CoopServerDownload), or the
    // release page. This tool never downloads it itself.
    private bool _openingCoopServer;

    private async void DownloadCoopServer_Click(object sender, RoutedEventArgs e)
    {
        // The manifest takes up to a few seconds; a second click meanwhile shows the same line again.
        SetControl(CoopServerDownload.LookingUpLine);
        if (_openingCoopServer)
            return;
        _openingCoopServer = true;
        try
        {
            var (line, failed) = await CoopServerDownload.OpenAsync("CoodszBurningEmpiresServerTool", BeServerPrepare.PickButton);
            SetControl(line, failed: failed);
            // The status line is on the Dashboard, so a browser that did not open shows its link where the host clicked.
            if (failed)
                ConfirmWindow.Show(this, "Download Coop server", line);
        }
        finally
        {
            _openingCoopServer = false;
        }
    }

    // The Prepare server work, also run by Start server when the server folder is not ready yet
    // (StartServerCoreAsync, automatic). True when the server folder was prepared; otherwise the reason is shown.
    private async Task<bool> PrepareServerAsync(bool automatic = false)
    {
        if (FindGameRoot() == null)
        {
            SetControl(GameFolderPick.NotFoundText, failed: true);
            return false;
        }

        if (ServerIsRunning())
        {
            SetControl("Stop the server before it is prepared again.", failed: true);
            return false;
        }

        var libraries = BannerlordFinder.SteamLibraries().ToList();
        var source = CoopServerSource(libraries);
        var picked = source?.Picked == true;
        var serverSource = source?.Folder;
        if (BeServerPrepare.PickedGone(_settings.CoopServerFolder))
            ForgetGonePick(NoteLog);
        // A Workshop Coop without its server program or engine files says antivirus only when the watch saw that
        // file before (CR-88BP7). Otherwise a download still running (CR-P75R3) or an incomplete one, not subscribe.
        // It reads Steam's workshop list and the watch, so it runs in RunAsync, which also keeps a second click out.
        // A Coop server the host picked (Choose Coop server) is checked by Prepare itself.
        string? workshopProblem = null;
        if (!picked)
            await RunAsync(() => workshopProblem = BeServerPrepare.WorkshopServerProblem(libraries, GoneWorkshopFiles(libraries)));
        if (serverSource == null || workshopProblem != null)
        {
            // With no Workshop Coop at all, the host can unzip Coop 0.1.7's server package and pick it.
            var noCoop = CoopStaging.FindWorkshopModule(libraries) == null;
            // The server package line goes on a line of its own, so each line stays a whole translated text.
            SetControl(CoopInstall.AddLine(workshopProblem ?? BeServerPrepare.NoWorkshopServer,
                noCoop ? CoopInstall.ServerGetLineNow(BeServerPrepare.PickButton) : null), failed: true);
            return false;
        }

        if (picked)
            NoteLog("Prepare server copies the Coop server in " + serverSource + ", the folder picked with " + BeServerPrepare.PickButton + ".");

        if (!UseShortServerFolder())
            return false;

        SaveSettings();
        var serverRoot = _settings.ServerRoot;
        var dataRoot = _settings.DataRoot;
        // The source's Coop is older than the server's (the Workshop's after a picked folder is gone, or an older picked
        // folder): Start server never puts the older one back by itself, and a Prepare server click asks first.
        if (BeServerPrepare.ServerIsNewerThan(serverRoot, serverSource))
        {
            if (automatic)
            {
                SetControl(BeServerPrepare.AutoPrepareKeptNewer(serverRoot, serverSource, picked), failed: true);
                return false;
            }

            if (!ConfirmWindow.Ask(this, BeServerPrepare.PrepareButton, BeServerPrepare.DowngradeQuestion(serverRoot, serverSource), null, "Continue"))
            {
                SetControl("Prepare server did not run. The server keeps its Coop.");
                return false;
            }
        }

        var sources = CoreSources();
        var prepared = false;
        await RunAsync(() =>
        {
            // A Coop server unzipped by hand: Windows' download marks in the picked folder are cleared too. The server that runs
            // is the copy in the server folder, so this mainly keeps the picked folder itself clean.
            if (picked && CoopInstall.UnblockedLine(CoopInstall.ClearServerMarks(serverSource)) is { } unblocked)
                NoteLog(unblocked);
            // Coodsz Host's own start script and launcher submodules go over the Workshop copy (ServerFolderRepair).
            var result = BeServerPrepare.Prepare(serverSource, serverRoot, new Progress<string>(NoteLog), ServerFolderRepair.DefaultPayload, picked);
            foreach (var line in result.Lines)
                NoteLog(line);
            // Prepare copies every missing file, so a Workshop module DLL still missing was taken again
            // at once, most likely by antivirus (BeServerModuleRepair).
            var missingModules = BeServerModuleRepair.BlockingModuleFiles(serverRoot, BeServerModuleRepair.WorkshopModuleFiles(serverSource));
            if (missingModules.Count > 0)
                throw new InvalidOperationException(BeServerModuleRepair.MissingText(missingModules));
            var framework = ServerFramework.Ensure(serverRoot, createDotnetHome: true);
            if (!framework.Ready)
                throw new InvalidOperationException(framework.Detail);
            if (framework.Detail.Length > 0)
                NoteLog(framework.Detail);
            foreach (var line in BeServerPrepare.EnsureData(serverRoot, dataRoot, BeHostFolders.ScriptCoopDataRoot()))
                NoteLog(line);
            BeServerPrepare.WritePolicy(serverRoot);
            Dispatcher.Invoke(() => WriteConfig());
            // Burning Empires, Elephants and the BUTR mods go in on Start server; say now what is missing.
            // Only the first problem shows under Start server. The rest are in the log.
            var problems = BeServerPath.CoreProblems(sources);
            foreach (var problem in problems)
                NoteLog(problem);
            SetControl(problems.Count == 0
                ? "Server prepared. Pick a Burning Empires save or make a new campaign, then click Start server."
                : "Server prepared, but Start server cannot run yet. " + BeUiText.FirstProblem(problems, BeLine(sources.GameRoot)), failed: problems.Count > 0);
            _actions.Note("Prepare", "Prepared server. Copied=" + result.Copied);
            RememberServerFiles(serverRoot);
            // Start server stops here with the first problem when Burning Empires or a helper mod is missing.
            prepared = problems.Count == 0;
        });
        RefreshState();
        return prepared;
    }

    // A server folder deeper than BeServerPrepare.MaxServerFolderLength makes the engine stop at init,
    // because its deepest files pass Windows' 260 character path limit. Prepare server offers the short
    // default folder on the game's drive instead. False when the host says no.
    private bool UseShortServerFolder()
    {
        var tooLong = BeServerPrepare.FolderTooLong(_settings.ServerRoot);
        if (tooLong == null)
            return true;
        var shortFolder = BeHostFolders.DefaultServerRoot(Path.GetPathRoot(_gameRoot ?? "") is { Length: > 0 } drive ? drive : "C:\\");
        if (!ConfirmWindow.Ask(this, "Server folder path too long", tooLong + " Use " + shortFolder + " for the server instead?", null, "Use the short folder"))
        {
            SetControl(tooLong + " Use a short folder like " + shortFolder + ".", failed: true);
            return false;
        }

        _actions.Note("Prepare", "Server folder was " + _settings.ServerRoot.Length + " characters long. Now using " + shortFolder);
        _settings.ServerRoot = shortFolder;
        PathText.Text = "Server folder: " + _settings.ServerRoot;
        NoteLog("The server folder is now " + shortFolder + ".");
        return true;
    }

    // Start server, before anything goes into the server folder: after a Coop update on the Workshop
    // the server files and Coop are copied again together (the server checks the SHA of its own Coop at
    // boot), then Coodsz Host's own start script and launcher submodules are laid over the folder, the
    // way Coodsz Host does on every start. False when the copy failed; the reason is shown.
    private async Task<bool> RefreshServerFolderAsync(string server)
    {
        var libraries = BannerlordFinder.SteamLibraries().ToList();
        var source = CoopServerSource(libraries);
        var workshop = source is { Picked: false } ? source.Folder : null;
        // Start's own notes, so the line also reaches this run's console log.
        if (BeServerPrepare.PickedGone(_settings.CoopServerFolder))
            ForgetGonePick(NoteStart);
        IReadOnlyList<string> lines;
        try
        {
            lines = await Task.Run(() =>
            {
                var done = new List<string>();
                // A Coop server the host picked that has another Coop now (a newer package unzipped over it) is copied
                // again the same way. It is no Steam download, so there is no download to wait for.
                if (source is { Picked: true } && BeServerPrepare.CoopChanged(source.Folder, server))
                {
                    // A picked folder older than the server's Coop is never copied over it by itself.
                    if (BeServerPrepare.ServerIsNewerThan(server, source.Folder))
                        throw new InvalidOperationException(BeServerPrepare.AutoPrepareKeptNewer(server, source.Folder, picked: true));
                    if (CoopInstall.UnblockedLine(CoopInstall.ClearServerMarks(source.Folder)) is { } unblocked)
                        done.Add(unblocked);
                    var prepared = BeServerPrepare.Prepare(source.Folder, server, null, ServerFolderRepair.DefaultPayload, picked: true);
                    done.Add(BeServerPrepare.PickedChangedNote);
                    if (prepared.Repaired != null)
                        done.Add(prepared.Repaired);
                }
                else if (workshop != null && BeServerPrepare.CoopChanged(workshop, server))
                {
                    // The server runs a newer Coop than the Workshop's (copied from a picked folder that is gone now).
                    // Copying the Workshop server over it would put the older Coop back, so it stays.
                    if (BeServerPrepare.ServerIsNewerThan(server, workshop))
                        done.Add(BeServerPrepare.KeptNewerNote(server, workshop));
                    // A Coop update Steam is still downloading leaves the ready server folder as it is, also
                    // when it already has the engine files, so a half download is never copied over it.
                    // A Workshop Coop missing a server file gets the same line as Prepare server (antivirus
                    // when the watch saw it, else an incomplete download) instead of waiting forever.
                    else if (AntivirusFiles.SteamUpdating(Path.GetDirectoryName(workshop)!))
                        done.Add(BeServerPrepare.WorkshopUnfinishedNote);
                    else if (BeServerPrepare.WorkshopServerProblem(libraries, GoneWorkshopFiles(libraries)) is { } problem)
                        throw new InvalidOperationException(problem);
                    else
                    {
                        var prepared = BeServerPrepare.Prepare(workshop, server, null, ServerFolderRepair.DefaultPayload);
                        done.Add(BeServerPrepare.CoopChangedNote);
                        if (prepared.Repaired != null)
                            done.Add(prepared.Repaired);
                    }
                }

                if (BeServerPrepare.LayOverRepair(server) is { } repaired)
                    done.Add(repaired);
                return done;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetControl(ex.Message, failed: true);
            return false;
        }

        foreach (var line in lines)
            NoteStart(line);
        return true;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        // Restart and Last good save set this before they call here: the tool's own start never asks the older save question.
        var automatic = _startFromTool;
        _startFromTool = false;
        // A Start click while Prepare server (or another action) still works would start or prepare the same
        // folder a second time. Restart and Last good save call this only after they cleared _busy.
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
        ClearStaleRescueNotice();
        await StartServerAsync(prepareIfNeeded: true, automatic);
    }

    private bool _startingServer;
    private bool _startFromTool;

    // prepareIfNeeded: only a Start server click prepares a server folder that is not ready. The automatic
    // restarts (after a load crash, Rescue save, start again) keep the Click Prepare server line, so
    // nothing big is copied and no window opens while nobody is watching.
    // automatic: the tool started this run itself (load retry, Restart, Last good save), so no question opens.
    private async Task StartServerAsync(bool prepareIfNeeded = false, bool automatic = false)
    {
        if (_startingServer)
            return;
        _startingServer = true;
        _automaticRun = automatic;
        try
        {
            await StartServerCoreAsync(prepareIfNeeded);
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

    private async Task StartServerCoreAsync(bool prepareIfNeeded)
    {
        if (ServerIsRunning())
        {
            SetControl("A Burning Empires server is already running. It was left up.");
            RefreshState();
            return;
        }

        var server = _settings.ServerRoot;
        var missingPiece = string.IsNullOrWhiteSpace(server) ? "The server folder is not set. Click Prepare server." : BeServerPrepare.MissingPiece(server);
        // A server folder that was never prepared, or lost an engine file, is prepared here the same way
        // Prepare server does it, instead of sending the host to click Prepare server first.
        if (missingPiece != null && prepareIfNeeded && !string.IsNullOrWhiteSpace(server))
        {
            NoteLog("The server folder is not ready yet, so Start server prepares it first.");
            if (!await PrepareServerAsync(automatic: true))
                return;
            server = _settings.ServerRoot;
            missingPiece = BeServerPrepare.MissingPiece(server);
        }

        if (missingPiece != null)
        {
            SetControl(missingPiece, failed: true);
            return;
        }

        // The Workshop copy after a Coop update and the tool's own server files come first, so Coop
        // Save Rescue's entry in DedicatedServer.Windows is not copied over and the new campaign name
        // below is checked against the start script that really runs.
        _startNotes.Clear();
        if (!await RefreshServerFolderAsync(server))
            return;

        // Coop Save Rescue on every start (auto mode); players can join while it is on.
        if (!InstallRescue(server))
            return;

        // A new campaign (a name, no .sav yet) or a Burning Empires save already in the server's Game Saves.
        var newCampaign = PendingNewCampaign();
        string save;
        if (newCampaign != null)
        {
            var problem = BeServerLaunch.NewCampaignProblem(newCampaign, ServerSavesDirectory(), BeServerLaunch.ReadServerScript(server));
            if (problem != null)
            {
                SetControl(problem, failed: true);
                return;
            }

            save = Path.Combine(ServerSavesDirectory(), newCampaign + ".sav");
        }
        else
        {
            save = SelectedSavePath();
            if (BeServerLaunch.IsTemplate(save))
            {
                SetControl(BeServerLaunch.TemplateReason, failed: true);
                return;
            }

            if (!SaveSlot.CanHost(save, ServerSavesDirectory()))
            {
                SetControl("Start server only loads a save already in the server Game Saves folder. Pick the one marked Server copy, or click Name server copy first. Or type a name and click New campaign.", failed: true);
                return;
            }

            if (!BeSaveVersion.IsBeSave(save))
            {
                SetControl(Path.GetFileNameWithoutExtension(save) + " was not made with Burning Empires. Pick a Burning Empires save, or click New campaign.", failed: true);
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

            if (!ConfirmSaveBeVersion(save))
            {
                SetControl("The server was not started. Pick a campaign made on Burning Empires " + BeStack.BeVersionText + ", or click New campaign.", failed: true);
                return;
            }
        }

        var saveName = Path.GetFileNameWithoutExtension(save);
        if (UdpPortBusy(DirectPort))
        {
            SetControl("UDP " + DirectPort + " is already in use. Stop the other dedicated server first. This one was not started.", failed: true);
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
                NoteLog("A second Coop folder was moved out of the server Modules folder: " + Path.GetFileName(parked) + ". Two Coop copies crash the server.");
            NoteLog(CoopStaging.DescribeCopies(modules));
            RepairModConfig();
            // After the repair, so a True written with a capital counts as the server will read it.
            if (BannerlordCoopLauncher.Core.Server.WarSailsCheck.Refusal(CoopModConfig.UserPath(), BeLoadOrder.ServerOrder(HostSetup)) is { } warSails)
                throw new InvalidOperationException(warSails);
            RepairServerConfig();
            var brokenAt = newCampaign == null ? BrokenWorldTime() : null;
            var holdAutosave = newCampaign == null && BrokenWorldGuard.HoldAutosave(SaveHeader.ReadCreated(save), brokenAt);
            WriteConfig(holdAutosave ? 0 : null, saveName);
            _steamAtStart = SteamBox.IsChecked == true;
            _autosaveHeld = holdAutosave;
            _spareUntilOnMap = brokenAt != null && !holdAutosave;
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
            return;
        }

        var framework = ServerFramework.Ensure(server, createDotnetHome: true);
        if (!framework.Ready)
        {
            SetControl(framework.Detail, failed: true);
            return;
        }

        if (framework.Detail.Length > 0)
            NoteLog(framework.Detail);
        RememberServerModuleNotes(server);
        _allowAddedModules = false;
        if (newCampaign == null && !ConfirmSaveMatchesSetup(save))
        {
            if (_startAgain)
            {
                // The save mods box picked the setup the save was made with: start again with it.
                _startAgain = false;
                SetControl("Switched to the setup this save was made with. Starting the server again.");
                _keepSaveSwitchAgreed = true;
                var automatic = _automaticRun;
                _ = Dispatcher.BeginInvoke(new Action(() => _ = StartServerAsync(automatic: automatic)));
                return;
            }

            SetControl("The server was not started. Pick the setup the save was made with on the Mods page.", failed: true);
            return;
        }

        var acceptTerms = AskServerTerms(server);
        if (acceptTerms == null)
        {
            SetControl(BeServerTerms.Refused, failed: true);
            return;
        }

        // Every setup: the Burning Empires core and the setup's extras staged, the order written,
        // then the server folder's own Start-ModdedServer.ps1 (BeServerLaunch).
        var engine = await PrepareBeServerAsync(server, new BeStartOptions
        {
            SaveName = saveName,
            NewCampaign = newCampaign != null,
            BattleSize = StartBattleSize(),
            AcceptTerms = acceptTerms == true,
            AllowAddedModules = _allowAddedModules
        });
        if (engine == null)
            return;
        if (ServerIsRunning())
        {
            SetControl("A Burning Empires server is already running. It was left up.");
            RefreshState();
            return;
        }

        // The module DLLs this run was staged with, for its exit check.
        var moduleFiles = _runModuleFiles;
        var start = new ProcessStartInfo
        {
            FileName = engine.FileName,
            Arguments = engine.Arguments,
            WorkingDirectory = engine.WorkingDirectory,
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
        foreach (var (key, value) in engine.Environment)
            start.Environment[key] = value;
        start.Environment[AutoRescue.EnvironmentVariable] = AutoRescue.ModeValue(_rescueNextMode);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        // One log per start, so a restart cannot close the next run's file.
        var consoleLog = new ConsoleLogFile();
        // Each run's lines carry its run id. A late line from a stopped run still goes to that run's own
        // log file, but never changes the state of the run that started after it.
        var runId = Interlocked.Increment(ref _consoleRunId);
        process.OutputDataReceived += (_, args) =>
        {
            consoleLog.Write(args.Data);
            if (IsCurrentRun(runId))
                CaptureLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            consoleLog.Write(args.Data);
            if (IsCurrentRun(runId))
                CaptureLine(args.Data);
        };
        process.Exited += (_, _) =>
        {
            // Let the last output lines arrive before the file is closed.
            try
            {
                // The timed wait does not wait for the output handlers; the second one does, so a
                // refusal on the last line is seen before the crash bar is decided.
                if (process.WaitForExit(5000))
                    process.WaitForExit();
            }
            catch (InvalidOperationException)
            {
            }

            int? exitCode;
            try { exitCode = process.ExitCode; }
            catch (InvalidOperationException) { exitCode = null; }
            consoleLog.Write(exitCode is { } loggedCode ? BeHostReport.ExitLine(loggedCode) : "[BE host] Server exited.");
            consoleLog.Close();
            Dispatcher.Invoke(() =>
            {
                // Only this tool's own run that is still the current one (a stop already let go of it).
                // An older run that exits after Restart started the next one never shows the crash bar.
                // The exit report is the same: after Stop it would put "The server stopped without an
                // error in its log" over the Stop note.
                if (ReferenceEquals(_process, process))
                {
                    _playHold.Stop();
                    // The exit reports read the exit code from the run's text, and nothing on the
                    // script route prints it. A script refusal keeps its own status instead.
                    if (exitCode is { } code && !_scriptRefused)
                        _logTail.Add(BeHostReport.ExitLine(code));
                    NoteHostReport(_logTail.ToString(), exited: true);
                    if (!IsScriptRefusedStop())
                    {
                        // A module DLL gone from the server folder is no load crash: no load mode retry
                        // and no Coop Save Rescue.
                        var missingFiles = IsMissingModuleFilesStop(exitCode, server, moduleFiles);
                        // Decided once, since Decide uses up the one retry. No bar before the load mode
                        // retry: its own exit shows it if it crashes too.
                        var loadStep = missingFiles ? LoadCrashStep.NotALoadCrash : DecideLoadCrash(exitCode);
                        if (LoadCrashRetry.ShowsCrashBar(loadStep))
                            OfferServerCrashReport(process);
                        if (!missingFiles)
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
        _runCreatesCampaign = newCampaign != null;
        // This run reads only its own logs and its own console lines, never the last run's SERVING.
        _runStartedUtc = DateTime.UtcNow.AddSeconds(-2);
        // Nor the last run's players: a stale list would let a ban kick go by id into this run.
        ForgetPlayers();
        // The Cheats page reads the players and lists of the new run.
        CoopCheats.ServerStarting();
        _logTail.Clear();
        _seenServing = false;
        // A new run: its exit is a crash unless Stop, Restart or this tool ends it.
        _stopAsked = false;
        _servingCheckedPath = "";
        _servingScanOffset = 0;
        // The program that starts is PowerShell with the server folder's Start-ModdedServer.ps1,
        // which starts the server in the same console.
        consoleLog.Open(Path.Combine(_settings.DataRoot, "logs"), "[BE host] Start " + start.FileName + " " + start.Arguments);
        // What the tool did to the server folder before this start, kept with the run's log.
        foreach (var note in _startNotes)
            consoleLog.Write("[BE host] " + note);
        _startNotes.Clear();
        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception ex)
        {
            consoleLog.Close();
            SetControl("The server did not start. " + ex.Message, failed: true);
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
        lock (_infoReportsNoted)
            _infoReportsNoted.Clear();
        _joinAdvice.Clear();
        _sharedSteamIds.Reset();
        _process = process;
        // The Start question compares against this save until a later Start's server runs.
        _settings.LastHostedSave = Path.GetFileNameWithoutExtension(save);
        // The server already runs, so a settings file that cannot be written only keeps the name until the next save.
        try { SaveSettings(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        RememberServerFiles(_settings.ServerRoot);
        // The last run's crash bar goes once the server runs again, unless this start is the retry
        // with the deeper save fixes, the tool's own answer to that crash.
        if (_rescueNextMode != RescueMode.Load)
            _crashBar?.ServerStarted();
        _speedAtStart = SavedGameSpeed();
        // The first start from this server folder brings the Windows Firewall prompt for .NET Host.
        if (BeFirewallHint.Shows(_settings.FirewallHintDoneFor, _settings.ServerRoot))
            NoteLog(BeFirewallHint.Text);
        _actions.NotePassword(!string.IsNullOrWhiteSpace(PasswordBox.Text));
        _actions.Note("Start", "Save=" + saveName + ". NewCampaign=" + (newCampaign != null)
            + ". ServerRoot set=" + !string.IsNullOrWhiteSpace(_settings.ServerRoot)
            + ". AutosaveHeld=" + _autosaveHeld);
        if (newCampaign != null)
        {
            // The server writes the campaign under this name; later starts load it like any save.
            _settings.SaveFile = save;
            SaveSettings();
        }

        SetControl(_autosaveHeld
            ? BrokenWorldGuard.AutosaveHeldText
            : newCampaign != null
                ? "Server is starting a new Burning Empires campaign named " + newCampaign + ". It is ready when the log says SERVING. The first start builds the world and the map cache and can take up to an hour; the status at the top shows how far it is."
                : "Server is starting. It is ready when the log says SERVING. The first start of a campaign builds the map cache and can take up to an hour; the status at the top shows how far it is.", failed: _autosaveHeld);
        RefreshState();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => _ = ShowWorldSave();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        // Busy first, so no question is asked for a stop that would be turned down anyway (SaveThenStop checks again).
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        if (!ConfirmWindow.Ask(this, "Stop server", "Stop the Burning Empires server? It saves the campaign first, then players are disconnected."))
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
        // Restart brings the same world back for the players. A new campaign typed during the run would win at
        // the next start and build a fresh world instead, so Restart lets it go, and the question says so.
        var waiting = PendingNewCampaign();
        var body = waiting == null
            ? "Restart the Burning Empires server? It saves the campaign first. Players are disconnected, then the same save loads again."
            : "Restart the Burning Empires server? It saves the campaign first. Players are disconnected, then the same save loads again. The new campaign "
              + waiting + " is not made. Type it again after the restart to make it.";
        if (!ConfirmWindow.Ask(this, "Restart server", body))
            return;
        if (waiting != null)
        {
            ClearNewCampaign();
            ShowSaveDetail();
        }
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
            WriteConfig();
            SaveTimePermissions();
            // The save already worked; a file that cannot be read again only leaves a box blank.
            try { RefillEmptySettings(); } catch (Exception) { }
            _actions.NotePassword(!string.IsNullOrWhiteSpace(PasswordBox.Text));
            _actions.Note("Settings", "Saved host settings. Save=" + Path.GetFileNameWithoutExtension(SelectedSavePath() ?? ""));
            if (_difficultySkipped > 0)
                _actions.Note("Settings", "Skipped " + _difficultySkipped + " greyed out difficulty settings (not in mod-config.json yet)");
            SetControl(_difficultySkipped > 0 && fromSettingsPage
                ? CoopDifficulty.SkippedOnSave
                : "Saved settings. Restart the server to use them. Fast forward and the battle size stay as they are until that restart.");
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
        // A server still loading (it never said SERVING in this run) is stopped right away.
        RefreshState();
        var loading = refusal == null && !BeServerStop.WaitsForSave(_seenServing || _servedThisRun);
        if (loading)
            _actions.Note("Stop", "The server was still loading, so it was stopped without a save");
        _busy = true;
        var saved = false;
        var startAgain = false;
        _stopAsked = true;
        try
        {
            if (refusal == null && !loading)
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
            SetControl(refusal ?? (loading
                ? BeServerStop.StoppedWhileLoading
                : saved
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

    private async void RestartLastGood_Click(object sender, RoutedEventArgs e)
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

        await KeepLastGoodCopyAsync();
        // The copy can take seconds on a big save; another action may have started meanwhile.
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        var live = Path.Combine(ServerSavesDirectory(), name + ".sav");
        var pick = LastGoodSave.Pick(SaveCopies(name), brokenAt);
        if (string.IsNullOrEmpty(pick) || pick.Equals(live, StringComparison.OrdinalIgnoreCase))
        {
            SetControl("No save from before the break is on disk. Later autosaves replaced those copies. This world was not saved.", failed: true);
            return;
        }

        // Burning Empires keeps part of the campaign in BESavedata; while the log says it could not
        // restore a save's data, no older save is put over the slot. A stopped run's lines do not count.
        if (BeSaveData.RestoreBlocked(RestoreBlockLog()))
        {
            SetControl(BeSaveData.RestoreBlockedReason, failed: true);
            return;
        }

        var created = SaveHeader.ReadCreated(pick);
        var when = created?.ToString("h:mm tt") ?? "an earlier time";
        if (!ConfirmWindow.Ask(this, "Last good save", "Restart on the save from " + when + "? The world running now is not written. Players disconnect, then that earlier save loads."))
            return;

        _busy = true;
        var loaded = false;
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

    private int _keepingLastGood;
    private Task _keepLastGoodTask = Task.CompletedTask;

    // From the window refresh: the copy of a whole save runs on a worker, one at a time, so a big
    // Burning Empires save never freezes the window while it copies.
    private void KeepLastGoodCopy()
    {
        if (Interlocked.Exchange(ref _keepingLastGood, 1) == 1)
            return;
        _keepLastGoodTask = Task.Run(() =>
        {
            try
            {
                KeepLastGoodCopyNow();
            }
            catch (Exception)
            {
                // Best effort: the next refresh tries again.
            }
            finally
            {
                Volatile.Write(ref _keepingLastGood, 0);
            }
        });
    }

    // Last good save waits for a copy that is still writing the kept file, then keeps the newest one.
    private async Task KeepLastGoodCopyAsync()
    {
        await _keepLastGoodTask;
        KeepLastGoodCopy();
        await _keepLastGoodTask;
    }

    private void KeepLastGoodCopyNow()
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
            _process!.StandardInput.WriteLine("save");
            _process.StandardInput.Flush();
        }
        else if (!AskQueuedSave())
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

    private void WriteConfig(int? autosaveOverride = null, string? saveName = null)
    {
        // An empty box keeps the minutes already in server-config.json; only bad text stops the save.
        var autosave = HostConfigEditor.ParseAutosave(AutosaveBox.Text);
        // While the server runs it keeps its own save, so Restart loads the same save again. The box pick goes in at the next Start.
        var hosted = ServerIsRunning() ? ConfiguredSaveName() : "";
        saveName ??= hosted.Length > 0 ? hosted : CurrentSaveName();
        // Before a save or a new campaign is picked, the password and the rest go in at the first start.
        if (saveName.Length > 0 && !BeServerLaunch.IsTemplate(saveName))
            WriteConfigFile(Path.Combine(_settings.DataRoot, "server-config.json"), saveName, PasswordBox.Text, autosaveOverride ?? autosave, SteamBox.IsChecked == true, LogFileBox.IsChecked != false);
        SaveSettings();
    }

    // The campaign the next start hosts: the new campaign name, the picked save, or the one in server-config.json.
    private string CurrentSaveName()
    {
        if (_settings.NewCampaignName.Trim().Length > 0)
            return _settings.NewCampaignName.Trim();
        var picked = SelectedSavePath();
        return picked.Length > 0 ? Path.GetFileNameWithoutExtension(picked) : ConfiguredSaveName();
    }

    private static void WriteConfigFile(string path, string saveName, string password, int? autosave, bool steam, bool logFile)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException("server-config.json was not found. Click Prepare server first.");
        if (string.IsNullOrWhiteSpace(saveName) || BeServerLaunch.IsTemplate(saveName))
            throw new InvalidOperationException("Pick a Burning Empires save or make a new campaign first. " + BeServerLaunch.TemplateReason);
        // The server does not know battleSize in this file; Start server passes the Settings value as -BattleSize.
        var updated = BeServerLaunch.WithoutBattleSize(HostConfigEditor.Apply(File.ReadAllText(path), saveName, password, autosave, steam, logFile));
        File.WriteAllText(path, updated);
    }

    // The person hosting reads the Coop server terms and decides; the tool never
    // accepts on its own. Returns null when they were not accepted, true when the
    // server should be told they were, false when the server has no terms to accept.
    private bool? AskServerTerms(string serverFolder)
    {
        var hash = BeServerTerms.TermsHash(serverFolder);
        if (hash == null)
            return false;
        if (!BeServerTerms.NeedsAnswer(hash, _settings.AcceptedEulaSha256))
            return true;
        if (!ConfirmWindow.Ask(this, BeServerTerms.Title, BeServerTerms.Body, BeServerTerms.TermsText(serverFolder), BeServerTerms.AcceptButton))
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

    // App.xaml.cs sends an error no window handler caught here, so the tool says so and keeps running (like TAOM).
    internal void NoteUnexpected(Exception ex)
    {
        SetControl("Something went wrong, and the tool kept running: " + ex.Message, failed: true);
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

    // Prepare server sits on the Server page, away from the note under Start server, so a player there saw nothing
    // when it stopped (a missing Workshop Coop said so only on the Dashboard). The note shows there too. It scrolls
    // into view only for a problem or when it first shows.
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
        foreach (var note in BeModNotes.For(HostSetup))
            NoteLog(note);
        var modules = Path.Combine(serverRoot, "engine", "Modules");
        foreach (var id in BeStack.ServerModules)
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
