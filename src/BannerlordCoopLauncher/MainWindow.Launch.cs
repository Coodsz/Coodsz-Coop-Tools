using System.Collections.ObjectModel;
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

public partial class MainWindow
{
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select your Bannerlord installation folder." };
        if (dialog.ShowDialog(this) != true)
            return;

        var root = GameRoot.Normalize(dialog.FolderName, _files);
        if (root == null)
        {
            Log("Install Mount & Blade II: Bannerlord in Steam, then open this again.");
            return;
        }

        _gamePicked = true;
        GamePathBox.Text = root;
        _ = RefreshAsync();
    }

    private async void Detect_Click(object sender, RoutedEventArgs e)
    {
        await DetectGamePath(logWhenMissing: true);
    }

    private async Task RefreshAsync()
    {
        await RunBusyAsync(async cancellationToken =>
        {
            await LoadPackAsync(cancellationToken);
        });
    }

    private async Task LoadPackAsync(CancellationToken cancellationToken)
    {
            SaveSettingsFromForm();
            _gameRoot = GameRoot.Normalize(GamePathBox.Text, _files);
            if (!string.IsNullOrWhiteSpace(GamePathBox.Text) && _gameRoot == null)
                Log("Install Mount & Blade II: Bannerlord in Steam, then open this again.");
            else if (_gameRoot != null)
            {
                GamePathBox.Text = _gameRoot;
                GameFoundText.Text = GameFoundMessage;
            }

            // This PC's Bannerlord is newer than 1.4.8 (NewerGame): the line under Connect on Play. Only a warning.
            var newerGame = NewerGame.Notice(_gameRoot);
            NewerGameText.Text = newerGame ?? "";
            NewerGameText.Visibility = newerGame == null ? Visibility.Collapsed : Visibility.Visible;

            RemoveStaleBattleSizeAdapter();
            ApplyPlayerSettlementPatch();
            await InstallBundledModulesAsync();
            DisableSlowMotion();

            _profile = await LoadProfileAsync(cancellationToken);
            PackText.Text = "";
            PackBadge.Visibility = Visibility.Collapsed;
            UpdateSteamStatus();
            if (string.IsNullOrWhiteSpace(DirectPortBox.Text) && _profile.Port > 0)
                DirectPortBox.Text = _profile.Port.ToString();

            IReadOnlyDictionary<string, PublishedFile>? remote = null;
            if (ModuleResolver.RequiresSteamMetadata(_profile))
            {
                try
                {
                    remote = await _workshop.GetDetailsAsync(
                        _profile.EnabledMods.Where(mod => !mod.Pinned).Select(mod => mod.WorkshopId),
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log("Cannot verify the latest Steam release. Open Steam, then click Check mods to try again.");
                    NoteAction("Steam release check failed", ex.Message);
                }
            }

            _remote = remote;
            var workshops = ReadWorkshops();
            RestoreRetiredCopies(workshops);
            _resolution = ModuleResolver.Resolve(_profile, _gameRoot, workshops, remote, _files, _steamKeptLocal);
            BindModules(_resolution);
            await ShowJoinStatusAsync(cancellationToken);
    }

    private void SteamMode_Click(object sender, RoutedEventArgs e) => SetJoinMode(direct: false);

    private async void DirectMode_Click(object sender, RoutedEventArgs e)
    {
        SetJoinMode(direct: true);
        await ShowOwnServerHintAsync(fill: true);
        await RunBusyAsync(async cancellationToken =>
        {
            SaveSettingsFromForm();
            await ShowJoinStatusAsync(cancellationToken);
        });
    }

    private async Task LaunchAsync(bool diagnostic)
    {
        if (_profile == null || _resolution == null || _gameRoot == null)
        {
            Log("Click Check mods before launching.");
            return;
        }

        // Before the game starts: Windows' download marks on a hand unzipped Modules\Coop cleared, a nested one named.
        foreach (var line in CoopInstall.CheckModulesCoop(_gameRoot))
            Log(line);

        // Direct: a wrong address used to start the game into a connect screen with no end, so the
        // server has to answer first. An answer goes straight on with no extra click.
        if (!diagnostic && UseDirect && TryReadDirect(out var checkHost, out var checkPort) && !await CheckDirectJoinAsync(checkHost, checkPort))
            return;

        // The server needs this setup's mods. When some are missing or out of date, one prompt gets
        // them and connects; with nothing missing Connect goes on as before.
        if (!diagnostic && !await GetServerModsFirstAsync())
            return;
        if (_profile == null || _resolution == null || _gameRoot == null)
            return;

        var block = ModuleResolver.ConnectBlockReason(_resolution);
        if (block != null)
        {
            foreach (var line in ModuleResolver.ConnectProblemLines(_resolution))
                Log(line);
            return;
        }

        if (!diagnostic)
            WarnBigBattles();

        await RunBusyAsync(async cancellationToken =>
        {
            await EnsureSteamAsync(_profile, cancellationToken);
            string? steamName = null;
            if (!diagnostic && !UseDirect)
            {
                steamName = SteamNameBox.Text.Trim();
                if (!SteamHostName.IsValid(steamName))
                    throw new InvalidOperationException("Enter the host's Steam profile name, the name friends see in Steam (not the login name). It can not contain a double quote.");
            }
            else if (!diagnostic && !JoinGuidance.IsDirectAddress(DirectHostBox.Text))
            {
                throw new InvalidOperationException(JoinGuidance.IpRequired);
            }
            else if (!diagnostic && !TryReadDirect(out _, out _))
            {
                throw new InvalidOperationException("Enter the UDP port the host set.");
            }

            // Asked before anything below changes the game's files, so No leaves them as they were.
            if (!await Coodsz.Ui.GameAlreadyOpen.ClearAsync(this, (message, _) => Log(message)))
                return;
            LogServerModuleNotes();
            var workshopFolders = ReadWorkshops().Select(library => library.ContentDirectory).ToList();
            var parkProblems = new List<string>();
            foreach (var parked in WorkshopDuplicates.ParkGameCopies(_gameRoot, _resolution, workshopFolders, parkProblems))
                Log("Turned off the copy of " + parked + " in the game's Modules folder. The game uses the Workshop copy, so it no longer loads two copies of that mod.");
            foreach (var parked in WorkshopDuplicates.ParkRetiredWorkshopCopies(_resolution, workshopFolders, parkProblems))
                Log("Turned off the old Workshop copy of " + parked + ". The game uses the copy in its Modules folder, so it no longer loads two copies of that mod.");
            foreach (var problem in parkProblems)
                Log(problem);
            // A TAOM launch changes a mod's SubModule.xml only while TAOM runs; put back any that
            // were left changed, so this setup loads the mods as they come.
            var restoreProblems = new List<string>();
            foreach (var restored in TaomDependencyFix.RestoreAll(workshopFolders.Append(Path.Combine(_gameRoot, "Modules")), restoreProblems))
                Log("Put back the original SubModule.xml of " + Path.GetFileName(restored) + ". The TAOM launcher had changed it for TAOM.");
            foreach (var problem in restoreProblems)
                Log(problem);
            // A UIExtenderEx copy missing a DLL its Compiled Prefabs need gets them turned off
            // for this game only and put back when it closes (see UIExtenderExPrefabsFix).
            RestoreUIExtenderExFix();
            foreach (var line in UIExtenderExPrefabsFix.ApplyForLaunch(_resolution.All.Select(module => module.ModuleId), UIExtenderExRoots()))
                Log(line);
            ApplyPlayerSettlementPatch();
            ApplyCoopPasswordPatch();
            DisableSlowMotion();
            ApplyRbmCoopFriendly();
            if (ClientLaunchGuard.UnlockEngineConfig(SlowMotionGuard.BannerlordDocuments()))
                Log("Unlocked engine_config.txt so the game can write it.");
            ulong? lobbyId = null;
            string? directEndpoint = null;
            if (!diagnostic && UseDirect && TryReadDirect(out var directHost, out var directPort))
            {
                WriteDirectAddress(directHost);
                directEndpoint = directHost + ":" + directPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (!diagnostic && !UseDirect)
            {
                ClearStoredAddress();
                // Keeps looking for up to 30 seconds: the lobby shows up a little after the server starts.
                lobbyId = await SteamLobbyFinder.FindWithRetryAsync(_gameRoot, steamName!, Log, cancellationToken);
                Log("Found that Steam lobby. The game will join it.");
            }
            var launchIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in _resolution.Modules)
            {
                if (!string.IsNullOrWhiteSpace(module.LaunchId))
                    launchIds[module.ModuleId] = module.LaunchId;
            }

            // The Coodsz client module: fills the Coop password on the first auto-connect and keeps a village
            // or town menu from freezing on a party Coop synced without its data (Coodsz Menu Guard).
            var addOn = BannerlordCoopLauncher.Core.Launch.ClientAddOns.TryInstall(_gameRoot, BannerlordCoopLauncher.Core.Launch.ClientAddOns.JoinPasswordId, BannerlordCoopLauncher.Core.Launch.ClientAddOns.JoinPasswordName, out var addOnError, Log);
            if (!addOn && addOnError != null)
                Log("Could not add " + BannerlordCoopLauncher.Core.Launch.ClientAddOns.JoinPasswordName + " (" + addOnError + "). The game still starts without it.");
            var plan = LaunchArgumentBuilder.Create(_gameRoot, _profile, diagnostic, steamName, launchIds, lobbyId, directEndpoint,
                addOn ? new[] { BannerlordCoopLauncher.Core.Launch.ClientAddOns.JoinPasswordId } : null);
            if (diagnostic)
                Log("Main menu: starts the game without joining the server.");
            else if (UseDirect && TryReadDirect(out var host, out var port))
                Log("Connect opens the game for " + host + ":" + port + ".");

            foreach (var move in ModuleShaderCache.SetAside(OldShaderModules()))
            {
                Log(move.Error == null
                    ? move.Name + " had an old shader file. It was set aside. The game builds a new one."
                    : "Could not set aside the old shader file in " + move.Name + ". " + move.Error);
            }

            // Map shaders the game could not remove crash it on the map (AD1259 CR-TXY4V).
            await TerrainShaderCache.ClearAsync(names => Coodsz.Ui.UiLanguage.Message(TerrainShaderCache.Ask(names), TerrainShaderCache.AskTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, this) == MessageBoxResult.Yes, Log);

            SaveSettingsFromForm();
            if (NonSteamCopy.Note(_gameRoot) is { } nonSteam)
                Log(nonSteam);
            // A Common.dll or 0Harmony.dll another mod left in the game's bin folder: a warning, the game still starts.
            foreach (var leftover in GameBinLeftovers.Notes(_gameRoot))
                Log(leftover);
            Log(plan.Executable);
            Log(plan.Arguments);
            var start = new ProcessStartInfo
            {
                FileName = plan.Executable,
                Arguments = plan.Arguments,
                WorkingDirectory = plan.WorkingDirectory,
                UseShellExecute = false
            };
            if (!diagnostic)
                start.Environment[CoopPasswordPatch.VariableName] = CurrentPassword();
            start.Environment["COODSZ_CAMPAIGN_MAP"] = IncludeMoreNations ? "MoreNations" : "Vanilla";
            var started = Process.Start(start);
            if (started == null)
                throw new InvalidOperationException("Bannerlord did not start.");
            ClientLaunchGuard.KeepEngineConfigWritable(SlowMotionGuard.BannerlordDocuments());
            RestoreUIExtenderExFixWhenClosed(started);
            Log("Bannerlord started.");
            if (!diagnostic)
            {
                var problems = ModuleResolver.ConnectProblemLines(_resolution);
                _ = WatchJoinAsync(started, _gameRoot, problems, SessionExtras());
            }
            if (diagnostic)
            {
                var extras = ExtraMods();
                var working = ServerModuleSet.WorkingOrder(IncludeMoreNations);
                LogExtraOrderCheck();
                Log("Watching this test. If the game closes while starting, this log will say which extra mod to turn off.");
                _ = WatchStartupAsync(started, SlowMotionGuard.BannerlordDocuments(), _gameRoot, extras, working);
            }
        });
    }

    private List<string> UIExtenderExRoots()
    {
        var roots = ReadWorkshops().Select(library => library.ContentDirectory).ToList();
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

    private IEnumerable<(string Name, string Directory)> OldShaderModules()
    {
        if (_resolution == null)
            yield break;
        foreach (var module in _resolution.All)
        {
            if (!module.Enabled || module.IsBaseModule || string.IsNullOrWhiteSpace(module.Directory))
                continue;
            var name = string.IsNullOrWhiteSpace(module.Name) ? module.ModuleId : module.Name;
            yield return (name, module.Directory);
        }
    }

    private void LogServerModuleNotes()
    {
        if (_resolution == null)
            return;
        foreach (var module in _resolution.All)
        {
            if (!module.Enabled || ServerModuleNotes.KeepsClientCampaign(module.ModuleId) || string.IsNullOrWhiteSpace(module.Directory))
                continue;
            var xmlPath = Path.Combine(module.Directory, "SubModule.xml");
            if (!File.Exists(xmlPath))
                continue;
            var note = ServerModuleNotes.SkippedCodeNote(module.ModuleId, module.Name, File.ReadAllText(xmlPath));
            if (note != null)
                Log(note);
        }

        var diplomacy = BannerlordCoopLauncher.Core.Profile.DiplomacyCompatRule.WarningFor(_resolution.All.Where(module => module.Enabled).Select(module => module.ModuleId));
        if (diplomacy != null)
            Log(diplomacy);
    }

    private async Task WatchJoinAsync(Process started, string gameRoot, IReadOnlyList<string> localProblems, IReadOnlyList<(string Id, string? Version)>? extras = null)
    {
        // The header pill: Joining now, then In game until the game closes, or Refused when the log says why not.
        SetJoinState(BannerlordCoopLauncher.Core.Launch.ClientNextStep.Joining);
        string? endState = null;
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
                    endState = BannerlordCoopLauncher.Core.Launch.ClientNextStep.InGame;
                    if (extras != null)
                        _ = RecordSessionAsync(started, extras);
                    return;
                }
                NoteBlockedDll(text);
                var lines = JoinResultExplain.Explain(text, localProblems);
                if (lines.Count > 0)
                {
                    endState = BannerlordCoopLauncher.Core.Launch.ClientNextStep.Refused;
                    foreach (var line in lines)
                        LogLater(line);
                    return;
                }

                if (exited)
                {
                    LogLater("The game closed before it joined the server.");
                    var twins = TwinsAfterCrash(null);
                    if (twins.Count > 0)
                    {
                        LogLater(WorkshopTwins.CrashLead);
                        foreach (var twin in twins)
                            LogLater(WorkshopTwins.Describe(twin));
                    }
                    return;
                }

                await Task.Delay(2000);
            }

            // Three minutes without a result while the game still runs: the pill stays Joining until the game closes,
            // or says Game open when Coop wrote nothing to either client log (Coop 0.1.7 can leave it empty).
            endState = joinLog.TimedOutState(ClientJoinLog.StillRunning(started));
            if (endState == BannerlordCoopLauncher.Core.Launch.ClientNextStep.GameOpen)
                LogLater(ClientJoinLog.EmptyLogNote);
        }
        catch (Exception ex)
        {
            LogLater("Could not read the join result. " + ex.Message);
        }
        finally
        {
            SetJoinState(endState);
            // In game, Refused and a join that timed out stay on the pill until the game closes, then it follows Connect again.
            if (endState != null)
                ClearJoinStateWhenClosed(started);
        }
    }

    private List<StartupCrashExplain.ModName> ExtraMods()
    {
        var working = new HashSet<string>(ServerModuleSet.WorkingOrder(IncludeMoreNations), StringComparer.OrdinalIgnoreCase);
        return _rows
            .Where(row => row.IsOn && !working.Contains(row.ModuleId))
            .Select(row => new StartupCrashExplain.ModName(row.ModuleId, string.IsNullOrWhiteSpace(row.Name) ? row.ModuleId : row.Name))
            .ToList();
    }

    private async Task WatchStartupAsync(
        Process started,
        string documentsRoot,
        string gameRoot,
        IReadOnlyList<StartupCrashExplain.ModName> extras,
        IReadOnlyCollection<string> working)
    {
        try
        {
            var exited = await Task.Run(() => started.WaitForExit(90_000));
            if (!exited)
            {
                LogLater("The game is still open. This test did not see a startup crash. If it closes on its own, this log will say what to change.");
                LogLoadedExtras(gameRoot, extras);
                await Task.Run(() => started.WaitForExit());
            }

            var capture = await Task.Run(() => WaitForCrashCapture(started, documentsRoot));
            NoteBlockedDll(capture.ReportText + "\n" + capture.GameLog);
            var explained = StartupCrashExplain.Explain(
                false,
                capture.ExitCode,
                capture.ReportText,
                capture.GameLog,
                extras,
                working,
                _profile?.EnabledMods.Any(mod => SharedModList.IsWorkshopId(mod.WorkshopId)) ?? true);
            var lines = WorkshopTwins.PointCrashAt(explained, TwinsAfterCrash(capture.GameLog));
            foreach (var line in lines)
                LogLater(line);
            if (StartupCrashExplain.SawStartupCrash(explained) && !StartupCrashExplain.NamesShaderCache(explained))
            {
                LogLater(OrderHintAfterCrash);
                // The crash bar, with the mod the crash names marked as suggested Broken.
                var named = StartupCrashExplain.NamedMods(capture.ReportText, capture.GameLog, extras);
                _ = Dispatcher.BeginInvoke(() => _reportBar?.Crash(ReportRequest(extras.Select(mod => (mod.Id, (string?)VersionOf(mod.Id))), afterGame: false, crashed: true).AsCrash(named, CrashLine(capture))));
            }
        }
        catch (Exception ex)
        {
            LogLater("Could not read the crash log. " + ex.Message);
        }
    }

    private static StartupCrashExplain.Capture WaitForCrashCapture(Process started, string documentsRoot)
    {
        int? code = null;
        try { code = started.HasExited ? started.ExitCode : null; }
        catch { }

        var wait = code is 0 ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(55);
        var deadline = DateTime.UtcNow + wait;
        StartupCrashExplain.Capture capture = default;
            while (true)
            {
                capture = StartupCrashExplain.Read(documentsRoot, started.Id);
                var gameLog = capture.GameLog ?? "";
                var explained = gameLog.Contains("could not be loaded correctly", StringComparison.OrdinalIgnoreCase)
                    || gameLog.Contains("Final_cleanup", StringComparison.OrdinalIgnoreCase)
                    || gameLog.Contains("Deleting Game", StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(capture.ReportText) || explained || DateTime.UtcNow >= deadline)
                    break;
                Thread.Sleep(2000);
            }

        return capture with { ExitCode = code ?? capture.ExitCode };
    }

    private void LogLater(string message)
    {
        if (Dispatcher.HasShutdownStarted)
            return;
        Dispatcher.BeginInvoke(() => Log(message));
    }

    private async Task EnsureSteamAsync(ServerProfile? profile, CancellationToken cancellationToken)
    {
        if (SteamRegistry.GetActiveProcessId() > 0)
        {
            UpdateSteamStatus();
            return;
        }

        var startAutomatically = profile?.StartSteamAutomatically ?? _settings.StartSteamAutomatically;
        var timeout = profile?.SteamStartupTimeoutSeconds ?? _settings.SteamStartupTimeoutSeconds;
        if (!startAutomatically)
            throw new InvalidOperationException("Open Steam and sign in, or enable StartSteamAutomatically in launcher.json.");

        var steamPath = SteamRegistry.TryGetSteamPath()
            ?? throw new InvalidOperationException("Could not open Steam: Steam is not installed for this user.");
        var steamExe = Path.Combine(steamPath, "steam.exe");
        if (!File.Exists(steamExe))
            throw new InvalidOperationException("Could not open Steam: " + steamExe);

        Log("Waiting for Steam. Sign in if prompted; the game will launch automatically.");
        Process.Start(new ProcessStartInfo(steamExe)
        {
            Arguments = "-silent",
            UseShellExecute = true
        });

        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, timeout));
        while (SteamRegistry.GetActiveProcessId() <= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Could not open Steam: timed out waiting for Steam to start.");
            await Task.Delay(500, cancellationToken);
        }

        UpdateSteamStatus();
    }

    private void RemoveStaleBattleSizeAdapter()
    {
        var roots = new List<string>();
        if (_gameRoot != null)
            roots.Add(Path.Combine(_gameRoot, "Modules"));
        foreach (var library in ReadWorkshops())
            roots.Add(library.ContentDirectory);

        var removed = CoopFpsPatch.RemoveStaleBattleSizeAdapter(roots);
        if (removed.Count > 0)
            Log("Removed Adapter.Native.dll. It was rewriting the battle-size limit every frame and dropping menu FPS.");
    }

    private void ApplyPlayerSettlementPatch()
    {
        var roots = new List<string>();
        if (_gameRoot != null)
            roots.Add(Path.Combine(_gameRoot, "Modules"));
        foreach (var library in ReadWorkshops())
            roots.Add(library.ContentDirectory);

        var batch = PlayerSettlementCompatPatch.Apply(roots);
        foreach (var file in batch.Patched)
            Log("Patched Player Settlement so a broken mod cannot close the game.");
        foreach (var error in batch.Errors)
            Log("Could not patch Player Settlement: " + error);
    }

    private void ApplyCoopPasswordPatch()
    {
        var roots = new List<string>();
        if (_gameRoot != null)
            roots.Add(Path.Combine(_gameRoot, "Modules"));
        foreach (var library in ReadWorkshops())
            roots.Add(library.ContentDirectory);

        var batch = CoopPasswordPatch.Apply(roots);
        foreach (var file in batch.Patched)
            Log("Patched Coop so Connect can send the server password.");
        foreach (var error in batch.Errors)
            Log("Could not patch Coop: " + error);
    }

    private void DisableSlowMotion()
    {
        var roots = new List<string>();
        if (_gameRoot != null)
            roots.Add(Path.Combine(_gameRoot, "Modules"));
        foreach (var library in ReadWorkshops())
            roots.Add(library.ContentDirectory);

        var result = SlowMotionGuard.Apply(roots, SlowMotionGuard.BannerlordDocuments());
        if (result.Dismemberment)
            Log("Turned off Dismemberment Plus slow motion.");
        if (result.RtsCamera)
            Log("Turned off RTS Camera slow motion.");
    }

    private async Task InstallBundledModulesAsync()
    {
        if (_gameRoot == null)
            return;

        // Written out on a worker thread the first time, so the window never waits on it.
        var bundled = await ToolPayload.FolderAsync(ToolPayload.BundledMods);
        foreach (var moduleId in BundledModules.InstallMissing(bundled, _gameRoot))
            Log("Installed " + moduleId + ", which comes with the launcher.");
    }

    private void ApplySetupModules(ServerProfile profile)
    {
        var setup = _settings.Current();
        if (!setup.Locked)
        {
            DefaultClientSet.Apply(profile);
            CustomModCatalog.Apply(profile, setup.CustomMods, setup.Locked);
            ClientLoadOrder.Apply(profile, setup.ModuleOrder);
            profile.ModuleLoadOrder = WorkingLoadOrder.PlaceExtrasBelow(profile.ModuleLoadOrder, DefaultClientSet.Fixed);
            DefaultClientSet.ApplyToggles(profile, setup.DisabledModules);
            RemovedModLaunch.Drop(profile, setup.RemovedModules);
            if (IncludeMoreNations)
                CampaignMapMods.Apply(profile, true);
            return;
        }

        if (StableClientSetup.IsPlain(setup.Name))
        {
            // Plain Coop (no mods): Coop alone, to match a plain Coop server.
            ServerModuleSet.ApplyPlain(profile);
            ClientCampaignModes.KeepOnClient(profile);
            return;
        }

        ServerModuleSet.Apply(profile, IncludeMoreNations);
        CustomModCatalog.Apply(profile, setup.CustomMods, locked: true);
        ClientModToggles.Apply(profile, setup.DisabledModules);
        RemovedModLaunch.Drop(profile, setup.RemovedModules);
        CampaignMapMods.Apply(profile, IncludeMoreNations);
        ClientCampaignModes.KeepOnClient(profile);
    }

    // The working order a locked setup keeps: Coop alone for Plain Coop, else the stable mods.
    private IReadOnlyList<string> LockedWorkingOrder() =>
        StableClientSetup.IsPlain(_settings.Current().Name)
            ? ServerModuleSet.PlainOrder
            : ServerModuleSet.WorkingOrder(IncludeMoreNations);

    private bool CanToggleModule(string moduleId)
    {
        var setup = _settings.Current();
        if (setup.Locked)
            return false;
        return !DefaultClientSet.IsFixed(moduleId);
    }

    private void ApplyModuleToggles(ServerProfile profile, IEnumerable<string> disabled)
    {
        if (_settings.Current().Locked)
            ClientModToggles.Apply(profile, disabled);
        else
            DefaultClientSet.ApplyToggles(profile, disabled);
    }

    private static void BlankSavedProfile(ServerProfile profile)
    {
        profile.Name = "";
        profile.Host = "";
    }

    private async Task<ServerProfile> LoadProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var profileUrl = string.IsNullOrWhiteSpace(ProfileUrlBox.Text)
                ? ProfileJson.DefaultProfileUrl
                : ProfileUrlBox.Text.Trim();
            var (profile, _) = await _profiles.DownloadAsync(profileUrl, cancellationToken);
            ApplySetupModules(profile);
            BlankSavedProfile(profile);
            File.WriteAllText(CachePath, ProfileJson.Serialize(profile));
            Log("Online settings override local server and Steam settings.");
            return profile;
        }
        catch (ProfileException ex) when (File.Exists(CachePath))
        {
            Log(ex.Message + " Using the saved profile.");
            var saved = ProfileJson.Parse(File.ReadAllText(CachePath));
            ApplySetupModules(saved);
            BlankSavedProfile(saved);
            return saved;
        }
        catch (ProfileException ex)
        {
            // No saved copy yet (a new install): carry on with the built-in profile instead of an empty list.
            Log(ex.Message + " Using the built-in profile.");
            var builtIn = ProfileJson.BuiltIn();
            ApplySetupModules(builtIn);
            return builtIn;
        }
    }

    // An old Workshop copy Connect turned off (the removed MCM item) comes back when the game's
    // Modules folder lost its own copy, so the mod is not shown as missing.
    private void RestoreRetiredCopies(IReadOnlyList<WorkshopLibrary> workshops)
    {
        foreach (var moduleId in WorkshopDuplicates.RestoreRetiredWorkshopCopies(_gameRoot, workshops.Select(library => library.ContentDirectory).ToList()))
            Log("Turned the old Workshop copy of " + moduleId + " back on, because the game's Modules folder no longer has that mod.");
    }

    private IReadOnlyList<WorkshopLibrary> ReadWorkshops()
    {
        var steamPath = SteamRegistry.TryGetSteamPath();
        if (steamPath == null)
            return Array.Empty<WorkshopLibrary>();

        try
        {
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            var vdf = _files.ReadAllText(vdfPath);
            IReadOnlyList<string> libraries = new[] { steamPath };
            // A damaged library list still leaves the Steam folder's own Workshop mods.
            if (vdf != null)
            {
                try
                {
                    libraries = SteamInstallFinder.ReadLibraryPaths(steamPath, vdf);
                }
                catch (BannerlordCoopLauncher.Core.Parsing.VdfException ex)
                {
                    Log("Unknown Steam manifest format. " + ex.Message);
                }
            }
            return SteamInstallFinder.ReadWorkshopLibraries(libraries, _files);
        }
        catch (Exception ex)
        {
            Log("Unknown Steam manifest format. " + ex.Message);
            return Array.Empty<WorkshopLibrary>();
        }
    }
}
