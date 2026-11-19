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
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using Microsoft.Win32;

namespace CoodszHost;

public partial class MainWindow
{
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        _crashPolicy.Reset();
        if (!BeginControl("Starting the server..."))
            return;
        try
        {
            _loadRetry.ManualStart();
            _startClicked = true;
            StartServer();
            SetControl(_autosaveHeld
                ? BrokenWorldGuard.AutosaveHeldText
                : "Server is starting. The pill at the top says Server online when it is ready.", failed: _autosaveHeld);
            // Start stays on the Dashboard: its Recent server output card shows the newest lines (crash paths still open Logs).
        }
        catch (Exception ex)
        {
            Append(ex.Message);
            SetControl(ex.Message, failed: true);
            _actions.Note("Start failed", ex.Message);
        }
        finally
        {
            _startClicked = false;
            EndControl();
        }
    }

    // True only while a click (Start server, Rescue save, Last good save) runs StartServer; crash
    // restarts and retries start without a person there.
    private bool _startClicked;

    // SandBox with its own campaign map next to a map mod: the start script would stop on
    // "Multiple active modules provide SceneObj\Main_map". A Start click asks to move it aside
    // first; an automatic restart moves it by itself, since nobody is there to answer.
    private void MoveExtraBaseMapAside()
    {
        var other = ExtraBaseMap.OtherMap(ServerRoot, _modules.Where(row => row.Enabled).Select(row => row.Id));
        if (other == null)
            return;
        var row = _modules.First(item => item.Id.Equals(other, StringComparison.OrdinalIgnoreCase));
        if (_startClicked && !ConfirmWindow.Ask(this, ExtraBaseMap.Title, ExtraBaseMap.Ask(MapLabel(row)), ExtraBaseMap.Folder(ServerRoot), ExtraBaseMap.OkText))
            throw new InvalidOperationException(ExtraBaseMap.Refused);
        var moved = ExtraBaseMap.MoveAside(ServerRoot);
        var line = ExtraBaseMap.Moved(ServerRoot, moved);
        Append(line);
        _actions.Note("Extra map", line);
    }

    // Coop Server Tool: the picked map mod copied again when its server copy lacks files (the AD1259 edition does
    // this for its whole set in PrepareAD1259Server).
    private void RecopyIncompleteMap()
    {
        ShortServerCopy? copy;
        try
        {
            copy = ExtraModScan.ShortMapCopy(ServerRoot, _settings.Current().CampaignMap);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var line = ShortServerCopy.CheckFailed(ex.Message);
            Append(line);
            _actions.Note("Map mod", line);
            return;
        }

        if (copy != null && RecopyShortCopies([copy], "Map mod"))
        {
            ReloadModules();
            EnforceSafeModuleList();
        }
    }

    // A copy of the map mod landed without its map scene: the line names the source, and that mod is not copied again
    // in this run (the Start refusal then says the map files are missing).
    private void NoteMapProblems(IReadOnlyList<MapCopyProblem> problems, string note)
    {
        foreach (var problem in problems)
        {
            Append(problem.Line);
            _actions.Note(note, problem.Line);
            _recopyGaveUp.Add(problem.Id);
        }
    }

    // Mods whose new copy still lacked files in this run of the tool (a mod that rewrites its own files on the
    // server, say). They are not copied again until the tool opens again, so no Start copies several GB each time.
    private readonly HashSet<string> _recopyGaveUp = new(StringComparer.OrdinalIgnoreCase);

    // Copies each short server copy again, with a line before each copy. True when a server folder changed.
    private bool RecopyShortCopies(IEnumerable<ShortServerCopy> copies, string note)
    {
        var changed = false;
        foreach (var copy in copies)
        {
            if (_recopyGaveUp.Contains(copy.Id))
                continue;
            IReadOnlyList<string> still;
            var root = ServerRoot;
            var mapId = MissingCampaignMap.EnvironmentValue(_settings.Current().CampaignMap);
            try
            {
                still = WhileCopying(copy.Copying, () => ExtraModScan.Recopy(copy, root, mapId));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // Install takes the old copy away first, so the folder may have changed; the set check after this says
                // what is missing, and the map check refuses a map without its Main_map. A copy that fails (a locked
                // file, say) is not tried again in this run either, so no Start copies several GB only to fail again.
                _recopyGaveUp.Add(copy.Id);
                var failed = copy.Failed(ex.Message);
                Append(failed);
                _actions.Note(note, failed);
                changed = true;
                continue;
            }

            changed = true;
            Append(copy.Copied);
            _actions.Note(note, copy.Copied);
            if (still.Count == 0)
                continue;
            _recopyGaveUp.Add(copy.Id);
            var stuck = copy.StillShort(still);
            Append(stuck);
            _actions.Note(note, stuck);
        }

        return changed;
    }

    // A first copy of a mod (the locked setup's CopyMissing on Open and every reload, RefreshWrongVersions on Start)
    // goes through WhileCopying too: a whole AD1259 copy on the UI thread made the window say Not responding, and a
    // tool closed then left the copy cut short. On Open it runs from Loaded, and the frame paints the line first.
    private void CopyIntoServer(string name, Action copy) =>
        WhileCopying(ShortServerCopy.CopyingNew(name), () =>
        {
            copy();
            return true;
        });

    // True for the whole WhileCopying call. Its DispatcherFrame still runs timers, so every timer driven start, restart
    // or stop, and ReloadModules (it copies into engine\Modules), checks it first and waits or skips.
    private bool _copyingMods;

    public const string CopyingBusyText = "A mod is still being copied into the server. Wait until it finishes, then press Start server.";

    // A copy of AD1259 is several GB. It runs off the UI thread while the window keeps painting with its input off,
    // so the window never says Not responding and is not closed halfway (that cut Tatache's first copy short).
    private T WhileCopying<T>(string line, Func<T> work)
    {
        Append(line);
        SetControl(line);
        var wasEnabled = IsEnabled;
        IsEnabled = false;
        void KeepOpen(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            SetControl(line);
        }

        Closing += KeepOpen;
        try
        {
            _copyingMods = true;
            var task = Task.Run(work);
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            return task.GetAwaiter().GetResult();
        }
        finally
        {
            Closing -= KeepOpen;
            IsEnabled = wasEnabled;
            _copyingMods = false;
        }
    }

    // The picked map mod turned off in this setup. The Coop Server Tool checks it before RecopyIncompleteMap, so a turned
    // off map mod is never copied again first; the AD1259 edition turns its locked set back on (EnableLockedSet) instead.
    private void RefuseTurnedOffMap(HostProfile profile)
    {
        if (!MissingCampaignMap.IsTurnedOff(profile.CampaignMap, _modules.Select(item => (item.Id, item.Enabled))))
            return;
        var off = _modules.First(item => item.Id.Equals(profile.CampaignMap!.Trim(), StringComparison.OrdinalIgnoreCase));
        _actions.Note("Map turned off", off.Id);
        throw new InvalidOperationException(MissingCampaignMap.TurnedOffText(string.IsNullOrWhiteSpace(off.Name) ? off.Id : off.Name.Trim()));
    }

    // A map mod turned off, or without its SceneObj\Main_map on the server, would start a plain Calradia world (MissingCampaignMap).
    private void RefuseMissingMap(HostProfile profile)
    {
        RefuseTurnedOffMap(profile);
        var folder = MissingCampaignMap.MissingFolder(ServerRoot, profile.CampaignMap);
        if (folder == null)
            return;
        var id = profile.CampaignMap!.Trim();
        var row = _modules.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var mod = Edition.IsAD1259 ? BannerlordCoopLauncher.Core.AD1259.AD1259Stack.Find(id) : null;
        var name = mod != null ? id : row == null || string.IsNullOrWhiteSpace(row.Name) ? id : row.Name.Trim();
        bool onWorkshop;
        try
        {
            onWorkshop = mod == null && ExtraModScan.IsOnWorkshop(ServerRoot, id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            onWorkshop = false;
        }

        _actions.Note("Map missing", folder);
        throw new InvalidOperationException(MissingCampaignMap.Text(name, folder, mod, onWorkshop));
    }

    private void ParkExtraCoop()
    {
        var parked = CoopModuleChoice.KeepOne(ModuleCatalog.ModulesRoot(ServerRoot));
        if (parked.Count == 0)
            return;
        foreach (var id in parked)
        {
            var row = _modules.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (row != null)
                _modules.Remove(row);
        }

        Append("Turned off the extra Bannerlord Coop copy so the server loads one: " + string.Join(", ", parked) + ".");
    }

    private void RepairServerConfigFile()
    {
        var path = ServerSession.ServerConfigPath;
        if (!File.Exists(path))
            return;
        var json = JsoncEditor.Read(path);
        var repaired = JsoncEditor.Repair(json);
        if (repaired.Equals(json, StringComparison.Ordinal))
            return;
        JsoncEditor.Write(path, repaired);
        Append("Repaired server-config.json. A saved number had been written as text the server cannot read, so it was ignoring the save name and password.");
    }

    // Repairs the safe mistakes (True written with a capital and the like) and returns a
    // warning when the server still could not read the file. The server drops the whole
    // file on one mistake and runs on defaults, battle size 1000 included.
    private string RepairModConfigFile()
    {
        var path = ServerSession.ModConfigPath;
        if (!File.Exists(path))
            return "";
        var health = CoopModConfig.Check(JsoncEditor.Read(path));
        if (health.Changed)
        {
            JsoncEditor.Write(path, health.Text);
            foreach (var fix in health.BoolFixes)
                Append(CoopModConfig.BoolFixNote(fix));
            foreach (var stray in health.Strays)
                Append(CoopModConfig.StrayFixNote(stray));
            if (health.OtherRepairs)
                Append("Repaired mod-config.json. A broken mod options block was stopping the difficulty settings from loading.");
        }

        var warning = CoopModConfig.BadLineWarning(health, path);
        if (warning.Length > 0)
        {
            Append(warning);
            StartLogAlert();
        }

        return warning;
    }

    private void EnforceSafeModuleList()
    {
        var installedIds = _modules.Select(row => row.Id).ToList();
        foreach (var row in _modules)
        {
            if (NeverOnServer.Contains(row.Id, StringComparer.OrdinalIgnoreCase)
                && !StableCoop.ShouldEnable(_settings.Current(), row.Id, installedIds))
                row.Enabled = false;
        }

        var bridge = _modules.FirstOrDefault(row => row.Id.Equals(BannerlordCoopLauncher.Core.Profile.SettlementBridgeRule.Bridge, StringComparison.OrdinalIgnoreCase));
        if (bridge is { Enabled: true }
            && BannerlordCoopLauncher.Core.Profile.SettlementBridgeRule.BridgeWithoutSettlement(_modules.Where(row => row.Enabled).Select(row => row.Id)))
        {
            bridge.Enabled = false;
            Append(BannerlordCoopLauncher.Core.Profile.SettlementBridgeRule.TurnedOffNote);
        }

        if (!_settings.Current().Locked)
            RestoreWorkingOrder();

        var bannerFix = _modules.FirstOrDefault(row => row.Id.Equals("BannerFix", StringComparison.OrdinalIgnoreCase));
        var raiseBanner = _modules.FirstOrDefault(row => row.Id.Equals("RaiseYourBanner", StringComparison.OrdinalIgnoreCase));
        if (bannerFix != null && raiseBanner != null && _modules.IndexOf(bannerFix) > _modules.IndexOf(raiseBanner))
        {
            _modules.Remove(bannerFix);
            _modules.Insert(_modules.IndexOf(raiseBanner), bannerFix);
        }

        if (!File.Exists(Executable))
            throw new InvalidOperationException(DedicatedServerFinder.MissingMessage);
        var profile = _settings.Current();
        profile.ModuleOrder = _modules.Select(row => row.Id).ToList();
        profile.DisabledModules = _modules.Where(row => !row.Required && !row.Enabled).Select(row => row.Id).ToList();
        ModuleCatalog.Write(ServerRoot, _modules.ToList());
    }

    // The start script in the server folder, or null when it cannot be read (then the old A to Z rule is assumed).
    private string? ReadServerScript()
    {
        try
        {
            return File.ReadAllText(Path.Combine(ServerRoot, "Start-ModdedServer.ps1"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A running server is refused before any module change: a recopy (Install deletes the server copy first) would pull
    // a mod out from under it. Restart, the crash restart and the load crash retry stop it first; ServerSession.Start
    // makes the same check, but only after the module steps.
    private void StartServer()
    {
        if (_session.IsRunning)
            throw new InvalidOperationException(ServerSession.AlreadyRunningText);
        if (_copyingMods)
            throw new InvalidOperationException(CopyingBusyText);
        _playHold.Stop();
        // A new start ends the Dashboard's Crashed state.
        _crashedSinceStart = false;
        _runLines.Clear();
        _joinAdvice.Clear();
        _sharedSteamIds.Reset();
        // Every start checks the save again, so an old card never stays up.
        HideSaveModCard();
        // Files gone since the last start that worked only change the wording (antivirus, CR-88BP7).
        var antivirusGone = GoneSinceLastStart();
        if (!File.Exists(Executable))
            throw new InvalidOperationException(ServerMissingText(antivirusGone));
        NoteGoneFiles(antivirusGone);
        // AD1259 never runs on the Coop nightly, so that refusal comes before any mod is copied. A newer game on this PC stays
        // the warning under Start server (NewerGame): the server itself is the Workshop's 1.4.8 one. It names Use the Steam
        // server only when that line shows (SteamServerState), with the Workshop server read again, since Steam may have
        // updated it since the last module reload.
        if (Edition.IsAD1259)
            _steamServerCoop = SteamServerCoop();
        if (Edition.IsAD1259 && AD1259Server.NightlyServer(_modules.ToList(), CoopBuildId.FromServer(ServerRoot), ServerRoot, SteamServerState().Line != null) is { } nightly)
            throw new InvalidOperationException(nightly);
        var profile = _settings.Current();
        var savePath = Path.Combine(ServerSession.SavesPath, (profile.SaveName ?? "") + ".sav");
        var saveError = LaunchOptions.MissingSaveReason(profile.SaveName, File.Exists(savePath), profile.NewCampaign);
        if (saveError != null)
            throw new InvalidOperationException(saveError);
        var acceptEula = AskServerTerms();

        ParkExtraCoop();
        RepairModConfigFile();
        RepairServerConfigFile();
        ReloadModules();
        EnforceSafeModuleList();
        var enabledIds = _modules.Where(row => row.Enabled).Select(row => row.Id).ToList();
        // After the repair above, so a True written with a capital counts as the server will read it.
        if (BannerlordCoopLauncher.Core.Server.WarSailsCheck.Refusal(ServerSession.ModConfigPath, enabledIds) is { } warSails)
            throw new InvalidOperationException(warSails);
        // AD1259 players run the pack's exact MCM, so only that version is taken there.
        // The whole AD1259 set is pinned, so a Workshop copy of another version (a re-upload with Id AD1259, say) never
        // replaces a server copy; RefreshWrongVersions takes the pinned version from this PC. The Open Source mods skip it
        // too (WorkshopRefreshIds): the Workshop's 2.0 would replace the 1.x set a host plays from the game's Modules folder.
        // So does MCM: RefreshWrongVersions gives the server the accepted MCM the host's game loads (5.13.1 or 5.12.3).
        var pins = Edition.IsAD1259
            ? BannerlordCoopLauncher.Core.AD1259.AD1259Stack.ClientMods.ToDictionary(mod => mod.Id, mod => mod.Version, StringComparer.OrdinalIgnoreCase)
            : null;
        var updatedMods = ExtraModScan.UpdateFromWorkshop(ServerRoot, AD1259Server.RefreshIds(Edition.IsAD1259, enabledIds), pins);
        if (updatedMods.Count > 0)
        {
            Append("Steam updated these mods, so the server copy was updated to match players: " + string.Join(", ", updatedMods) + ". The old copies are in DedicatedServer\\" + ModuleRemoval.RemovedFolderName + ".");
            // Logs sent from the tool then show when the server copies changed.
            _actions.Note("Mods updated", string.Join(", ", updatedMods));
        }
        // A newer MCM from Nexus in the game's Modules folder, also in a locked setup (crash reports #32 and #34).
        var newerFromGame = ExtraModScan.UpdateNewerOnlyFromGame(ServerRoot, enabledIds, pins);
        var replaced = newerFromGame.Where(line => !line.Contains(" could not be updated (", StringComparison.Ordinal)).ToList();
        if (replaced.Count > 0)
        {
            Append("A newer copy in the game's Modules folder replaced the server copy: " + string.Join(", ", replaced) + ". The old copies are in DedicatedServer\\" + ModuleRemoval.RemovedFolderName + ".");
            _actions.Note("Mods updated", string.Join(", ", replaced));
        }
        foreach (var failed in newerFromGame.Except(replaced))
            Append("The server copy was not replaced by the newer one in the game's Modules folder: " + failed + ".");
        if (newerFromGame.Count > replaced.Count)
            _actions.Note("Mods not updated", string.Join(", ", newerFromGame.Except(replaced)));
        if (updatedMods.Count > 0 || newerFromGame.Count > 0)
        {
            ReloadModules();
            EnforceSafeModuleList();
        }
        if (Edition.IsAD1259)
            PrepareAD1259Server();
        else
        {
            RefuseTurnedOffMap(_settings.Current());
            RecopyIncompleteMap();
        }
        HideServerRawAssets();

        PersistProfile(allowLockedStart: true);
        ServerModuleGuard.RejectClientXorberax(ServerRoot);
        // A server package unzipped by hand (Coop 0.1.7's .7z): Windows' download marks cleared before the server runs.
        if (CoopInstall.UnblockedLine(CoopInstall.ClearServerMarks(ServerRoot)) is { } unblocked)
            Append(unblocked);
        var repaired = DedicatedServerRepair.Ensure(ServerRoot);
        if (repaired != null)
            Append(repaired);
        // One log line per repair note, each translated on its own.
        foreach (var note in (CoopServerBin.Ensure(ServerRoot) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Append(note);
        profile = _settings.Current();
        if (string.IsNullOrWhiteSpace(profile.SaveName))
            throw new InvalidOperationException("Make a new campaign save first. The save field is empty.");
        if (string.IsNullOrWhiteSpace(profile.CampaignMap))
            throw new InvalidOperationException("Pick a campaign map first.");
        MoveExtraBaseMapAside();
        RefuseMissingMap(profile);
        // The password (an empty one clears it) and the Steam switch reach server-config.json on every Start.
        if (File.Exists(ServerSession.ServerConfigPath))
        {
            var serverConfig = JsoncEditor.Read(ServerSession.ServerConfigPath);
            if (ServerConfigFlags.PasswordChangeLine(serverConfig, profile.Password) is { } passwordLine)
                Append(passwordLine);
            JsoncEditor.Write(ServerSession.ServerConfigPath, ServerConfigFlags.ApplyAtStart(serverConfig, profile.Password, SteamBox.IsChecked == true, _serverFlagsLoaded));
        }
        _steamAtStart = File.Exists(ServerSession.ServerConfigPath) ? Flag(JsoncEditor.Read(ServerSession.ServerConfigPath), "steam", true) : null;
        var framework = TaomCoop.Core.ServerFramework.Ensure(ServerRoot, createDotnetHome: true);
        if (!framework.Ready)
            throw new InvalidOperationException(framework.Detail);
        if (framework.Detail.Length > 0)
            Append(framework.Detail);
        var keepLive = SeedSettingsLockDefaults(profile);
        if (keepLive != null)
            McmCatalog.ApplyToLive(EnsureMcmFolder(profile), keepLive);
        else
            Append("The server keeps its own MCM settings for this start.");
        savePath = Path.Combine(ServerSession.SavesPath, profile.SaveName + ".sav");
        var createNew = profile.NewCampaign && !File.Exists(savePath);
        // DedicatedServerRepair.Ensure above writes this tool's start script, but a locked file keeps an older copy
        // that only takes A to Z in a new campaign name; that one would refuse the name only after the start.
        if (createNew && SaveSet.NewSaveNameProblemFor(profile.SaveName, ReadServerScript()) is { } nameProblem)
            throw new InvalidOperationException(nameProblem);
        AgreeToMapMod(profile, createNew);
        var allowAddedModules = !createNew && AgreeToAddedModules(profile.SaveName, savePath);
        AppendServerModuleNotes(createNew);
        _serving = false;
        _servedThisRun = false;
        _runCreatesCampaign = createNew;
        _steamLobby = SteamLobbyState.Unknown;
        _engineExited = false;
        _userStop = false;
        _modPatchSetup.Reset();
        _saveRefused = false;
        _fatalLine = null;
        // The exit text reads these lines too, so an earlier run's FATAL line is never quoted for this one.
        _recentServerLines.Clear();
        _mapMissing = false;
        _exitExplainQueued = false;
        _blockedDllNoted = false;
        StopLogAlert();
        StopUnblockAlert();
        ApplyAutosaveForStart(savePath);
        ApplySaveProfile(profile.SaveName);
        ApplyBattleSizeLimit();
        ApplyKingdomProtection();
        _speedAtStart = SavedGameSpeed();
        // Coop Save Rescue on every start (auto mode), last before the tools module. Players can join.
        _rescueRun.Begin(_rescueNextMode);
        var startModules = _modules.ToList();
        var rescue = InstallRescue();
        if (rescue)
            startModules = SaveRescue.WithRescue(startModules);
        if (_rescueRun.Mode == RescueMode.Full)
        {
            Append("Rescue save: Coop Save Rescue runs every save fix on this start. After the Report, press Stop server, then Start server to play normally.");
            _actions.Note("Rescue save", "Started " + profile.SaveName + " with " + SaveRescue.ModuleId + " in full mode");
        }
        else if (_rescueRun.Mode == RescueMode.Load)
            _actions.Note("Save fixes", "Started " + profile.SaveName + " with " + SaveRescue.ModuleId + " in load mode");
        // Coodsz Server Tools (both builds): the server rules, the settings sync and the Cheats page and player profiles.
        // It is never in the Modules list; every start adds it last, after Coop.
        var toolsNote = CoodszServerTools.Install(ServerRoot);
        if (toolsNote != null)
            Append(toolsNote);
        // Settings sync: which mods players keep their own settings for, and the host overrides.
        StageModSettings(profile);
        startModules = CoodszServerTools.WithTools(startModules);
        // The Encyclopedia and Map pages are never attached in the AD1259 build (StartKeep).
        if (!Edition.IsAD1259)
        {
            KeepEncyclopedia.ServerStarting();
            KeepMap.ServerStarting();
        }
        // Improved Garrisons Coop Compat: only when ImprovedGarrisons is on, added last so it loads
        // after ImprovedGarrisons and CoopModPatch. Server only, players do not need it.
        var igCompat = false;
        if (ImprovedGarrisonsCompat.Enabled && ImprovedGarrisonsCompat.Applies(startModules))
        {
            var igNote = ImprovedGarrisonsCompat.Install(ServerRoot);
            if (igNote != null)
                Append(igNote);
            igCompat = ImprovedGarrisonsCompat.Installed(ServerRoot);
            if (igCompat)
                startModules = ImprovedGarrisonsCompat.WithCompat(startModules);
        }
        // The Cheats page reads the players and lists of the new run (both builds).
        KeepCheats.ServerStarting();
        KeepClientTimeControls();
        // The tools module is always added, so this start's list is always written.
        ModuleCatalog.Write(ServerRoot, startModules);
        var on = _modules.Where(row => row.Enabled).Select(row => row.Id).ToList();
        _actions.NotePassword(!string.IsNullOrWhiteSpace(profile.Password));
        _actions.Note("Start", profile.Name
            + ". Save=" + profile.SaveName
            + ". NewCampaign=" + createNew
            + ". Map=" + profile.CampaignMap
            + ". EnginePort=" + profile.EnginePort
            + ". Region=" + profile.Region
            + ". Mods=" + on.Count + " [" + string.Join(", ", on) + "]");
        _session.Start(Executable, new LaunchOptions
        {
            EnginePort = profile.EnginePort,
            Region = profile.Region,
            SaveName = profile.SaveName,
            CreateNew = createNew,
            AcceptEula = acceptEula,
            AllowAddedModules = allowAddedModules,
            Rescue = _rescueRun.Mode,
            CampaignMap = profile.CampaignMap
        });
        // The last run's crash bar goes once the server runs again, unless this start is the tool's own
        // answer to that crash (Restart if it crashes, the retry with the deeper save fixes).
        if (!_restartingAfterCrash && _rescueNextMode != RescueMode.Load)
            _crashBar?.ServerStarted();
        RememberServerFiles();
        _launchedSave = profile.SaveName;
        if (createNew)
        {
            profile.NewCampaign = false;
            HostSettings.Save(SettingsPath, _settings);
        }
    }

    // Setups and maps the map mod question was asked for in this run (MapModWarning).
    private readonly HashSet<string> _mapModAsked = new(StringComparer.OrdinalIgnoreCase);

    // A new campaign on a map mod crashes the Coop server a few minutes in (crash report #48). A Start
    // click asks once; the host can still start. Cancel stops this start with a short status line.
    private void AgreeToMapMod(HostProfile profile, bool createNew)
    {
        var key = MapModWarning.Key(profile.Name, profile.CampaignMap);
        if (!MapModWarning.ShouldAsk(Edition.IsAD1259, _startClicked, createNew, profile.CampaignMap, _modules.Where(row => row.Enabled).Select(row => row.Id), _mapModAsked, key))
            return;
        _mapModAsked.Add(key);
        var row = _modules.FirstOrDefault(item => item.Id.Equals(profile.CampaignMap, StringComparison.OrdinalIgnoreCase));
        var mapName = row == null ? profile.CampaignMap : MapLabel(row);
        if (ConfirmWindow.Ask(this, MapModWarning.Title, MapModWarning.Ask(mapName), null, MapModWarning.OkText))
        {
            _actions.Note("Map mod warning", "Start anyway on " + profile.CampaignMap);
            return;
        }
        _actions.Note("Map mod warning", "Cancel on " + profile.CampaignMap);
        throw new InvalidOperationException(MapModWarning.Declined);
    }

    // Saves the host already agreed to add mods to this run, so a restart does not ask again.
    private readonly HashSet<string> _addedModulesAgreed = new(StringComparer.OrdinalIgnoreCase);

    // Bannerlord loads a save with mods added, but the server script hides that save unless
    // told it may. Asks once, keeps a backup, then lets the script take the save. A save
    // missing a mod it was made with is refused here with a plain reason.
    private bool AgreeToAddedModules(string saveName, string savePath)
    {
        var info = CampaignSaveInfoReader.Read(savePath);
        if (info == null)
            return false;
        var change = SaveModChange.Check(info.Modules, _modules.Where(row => row.Enabled).Select(row => row.Id));
        if (change.Action == SaveModAction.Refuse)
        {
            // The full list stays in the log and the action log for support; the window shows the card.
            Append(change.RefuseText);
            _actions.Note("Save mods", change.RefuseText);
            ShowSaveModCard(BuildSaveModCard(saveName, info, change));
            throw new InvalidOperationException(SaveModCard.StatusLine);
        }
        var map = _settings.Current().CampaignMap;
        if (MapModWarning.RefusesAdding(change, map))
        {
            var mapRow = _modules.FirstOrDefault(row => row.Id.Equals(map!.Trim(), StringComparison.OrdinalIgnoreCase));
            var mapName = string.IsNullOrWhiteSpace(mapRow?.Name) ? map!.Trim() : mapRow.Name.Trim();
            // The action log also names the save, which the refusal itself does not.
            _actions.Note("Save mods", "Refused adding the map mod " + mapName + " to the save " + saveName + ", a campaign made on another map.");
            // English, like the other refusals: the Start click writes it to the log and the action log once,
            // and the status box shows it in the player's language.
            throw new InvalidOperationException(SaveModChange.MapRefuseText(mapName));
        }
        if (change.Action != SaveModAction.AskToAdd)
            return false;
        var key = saveName + "|" + string.Join(",", change.Added);
        if (_addedModulesAgreed.Contains(key))
            return true;
        var rows = change.AddedRows(id => _modules.FirstOrDefault(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name);
        if (!ConfirmWindow.AskList(this, SaveModChange.AskTitle, change.AskQuestion, rows, SaveModChange.AskBackupNote, change.AskWarning,
                SaveModChange.AskButton, SaveModChange.AskButtonTip))
        {
            _actions.Note("Add mods to campaign", "No for " + saveName);
            Append(change.DeclinedText);
            // The status box shows it in three lines (SetControl): what happened, the mods by name, what to do.
            throw new InvalidOperationException(string.Join('\n', change.DeclinedLines(id => _modules.FirstOrDefault(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name)));
        }

        try
        {
            var backup = SaveBackups.Create(saveName, BackupReason.Manual);
            Append("Kept a backup of " + saveName + " before adding " + string.Join(", ", change.Added) + ". Load backup puts it back. " + backup.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException("Could not make a backup of " + saveName + ", so the server was not started. " + ex.Message);
        }

        _addedModulesAgreed.Add(key);
        _actions.Note("Add mods to campaign", saveName + ": " + string.Join(", ", change.Added));
        return true;
    }

    // AD1259 edition: copy the shipped compatibility module into the server.
    private void InstallAD1259Compat()
    {
        try
        {
            AD1259Server.InstallCompat(ServerRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Append("Could not add " + BannerlordCoopLauncher.Core.AD1259.AD1259Stack.CompatName + " to the server: " + ex.Message);
        }
    }

    // Every start (both builds): the raw Assets folders the headless server crashes on are
    // renamed to Assets.off in the server copy only (ServerRawAssets). Players keep theirs.
    private void HideServerRawAssets()
    {
        var hidden = ServerRawAssets.Note(ServerRawAssets.Hide(ModuleCatalog.ModulesRoot(ServerRoot), ServerRawAssets.Ids(CommunityCompat.Current)));
        if (hidden != null)
            Append(hidden);
    }

    // The missing textures line for an Open Source 2.0 server shows once per run (PrepareAD1259Server).
    private bool _openSourceTexturesShown;

    // AD1259 edition: bring Nexus mods up to the set's versions and refuse a start with a wrong
    // or missing mod.
    private void PrepareAD1259Server()
    {
        // The module reload adds the compat module only for a locked setup; New setup makes an unlocked one (AD1259 crash report #20).
        if (AD1259Server.NeedsCompat(_modules.ToList()))
        {
            InstallAD1259Compat();
            ReloadModules(install: false);
            EnforceSafeModuleList();
        }

        var gameModules = ExtraModScan.GameModulesDirectory() ?? "";
        var workshopRoots = ExtraModScan.WorkshopContentRoots();
        // The copies on this PC, read once: copies into the server never change them.
        var pcCopies = AD1259Server.PcCopies(gameModules, workshopRoots);
        var mapProblems = new List<MapCopyProblem>();
        var refreshed = AD1259Server.RefreshWrongVersions(ServerRoot, _modules.ToList(), pcCopies, mapProblems, CopyIntoServer);
        NoteMapProblems(mapProblems, "AD1259 set");
        if (refreshed.Count > 0)
        {
            var copied = "Copied the right version of these mods into the server: " + string.Join(", ", refreshed) + ".";
            Append(copied);
            _actions.Note("AD1259 set", copied);
            ReloadModules();
            EnforceSafeModuleList();
        }

        // A server copy with the right version that lacks files (a copy cut short) is copied again too.
        if (RecopyShortCopies(AD1259Server.ShortCopies(ServerRoot, _modules.ToList(), pcCopies), "AD1259 set"))
        {
            ReloadModules();
            EnforceSafeModuleList();
        }

        var turnedOn = AD1259Server.EnableLockedSet(_modules.ToList());
        if (turnedOn.Count > 0)
        {
            var line = AD1259Server.TurnedOnLine(turnedOn);
            Append(line);
            _actions.Note("AD1259 set", line);
            var profile = _settings.Current();
            profile.DisabledModules = profile.DisabledModules
                .Where(id => !turnedOn.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        // Which Open Source set the server got from this PC, and a mix of the two sets, so the host can tell players. The
        // missing textures line for 2.0 shows on the first Start of this run only.
        if (AD1259Server.OpenSourceLine(_modules.ToList(), withTextures: !_openSourceTexturesShown) is { } openSource)
        {
            _openSourceTexturesShown |= openSource.Contains(BannerlordCoopLauncher.Core.AD1259.OpenSourceSet.MissingTextures, StringComparison.Ordinal);
            foreach (var line in openSource.Split('\n'))
                Append(line);
            _actions.Note("AD1259 set", openSource.Replace('\n', ' '));
        }

        var problem = AD1259Server.Problem(_modules.ToList(), AD1259Server.PcVersions(pcCopies));
        if (problem != null)
            throw new InvalidOperationException(problem);
    }

    // Coop 0.1.6+ servers exit with code 6 until the host accepts the Coop
    // server terms. The operator reads EULA.md and LICENSE.txt and decides; the
    // host never accepts on its own. The answer is kept until the terms change.
    private bool AskServerTerms()
    {
        var folder = Path.GetDirectoryName(Executable);
        if (string.IsNullOrWhiteSpace(folder))
            return false;
        var hash = ServerEula.TermsHash(folder);
        if (hash == null)
            return false;
        if (!ServerEula.NeedsAnswer(hash, _settings.AcceptedEulaSha256))
            return true;
        if (!ConfirmWindow.Ask(this, ServerEula.Title, ServerEula.Body, ServerEula.TermsText(folder), ServerEula.AcceptButton))
        {
            _actions.Note("Server terms", "Not accepted");
            throw new InvalidOperationException(ServerEula.Refused);
        }

        _settings.AcceptedEulaSha256 = hash;
        HostSettings.Save(SettingsPath, _settings);
        _actions.Note("Server terms", "Accepted " + hash[..12]);
        Append("You accepted the Coop server terms. The server saves that in CoopData\\DedicatedServer\\eula.txt.");
        return true;
    }

    private void AppendServerModuleNotes(bool createNew)
    {
        var libraries = false;
        foreach (var row in _modules.Where(row => row.Enabled))
        {
            if (ServerModuleNotes.IsLibrary(row.Id))
                libraries = true;
            if (ServerModuleNotes.KeepsClientCampaign(row.Id))
                continue;
            var xmlPath = Path.Combine(ModuleCatalog.ModulesRoot(ServerRoot), row.Id, "SubModule.xml");
            if (!File.Exists(xmlPath))
                continue;
            var note = ServerModuleNotes.SkippedCodeNote(row.Id, string.IsNullOrWhiteSpace(row.Name) ? row.Id : row.Name, File.ReadAllText(xmlPath));
            if (note != null)
                Append(note);
        }

        if (libraries)
            Append(ServerModuleNotes.Libraries);
        if (createNew)
            Append(ServerModuleNotes.NewCampaign);
        var diplomacy = BannerlordCoopLauncher.Core.Profile.DiplomacyCompatRule.WarningFor(_modules.Where(row => row.Enabled).Select(row => row.Id));
        if (diplomacy != null)
            Append(diplomacy);
        try
        {
            foreach (var warning in ModuleDllClash.Warnings(ModuleCatalog.ModulesRoot(ServerRoot), _modules.Where(row => row.Enabled).Select(row => row.Id)))
                Append(warning);
        }
        catch (Exception ex)
        {
            Append("Could not compare mod DLLs. The server will still start. " + ex.Message);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var refusal = SaveRefusal();
        if (refusal != null)
        {
            SetControl(refusal, failed: true);
            Append(refusal);
            return;
        }

        if (!BeginControl("Saving the world..."))
            return;
        try
        {
            if (!_session.CanSendCommands)
            {
                var blocked = _session.CommandBlockReason();
                SetControl(blocked, failed: true);
                Append(blocked);
                return;
            }

            var reply = await Task.Run(() => _session.SaveCurrentWorld(_settings.Current().SaveName));
            Append(reply);
            SetControl(reply, done: true);
        }
        catch (Exception ex)
        {
            Append(ex.Message);
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            EndControl();
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Ask(this, "Stop server", "Stop the server? It saves the campaign first, then players are disconnected."))
            return;
        if (!BeginControl("Please wait. The server is saving, then it will stop."))
            return;
        _userStop = true;
        _stoppingNow = true;
        _actions.Note("Stop", "Requested stop for " + (_settings.Current().SaveName ?? "(no save)"));
        try
        {
            // A campaign that did not load is never saved or sent stop, like a broken world.
            var refusal = SaveRefusal(stopping: true);
            if (refusal != null)
            {
                await Task.Run(() => _session.Stop());
                _engineExited = false;
                _serving = false;
                if (!_session.IsRunning)
                    _launchedSave = "";
                Append(refusal);
                SetControl(refusal, failed: true);
                RefreshState();
                return;
            }

            var startedAt = _session.StartedAt;
            var consoleLog = _session.ConsoleLogPath;
            var saveNote = await SaveBeforeShutdown();
            SetControl("Please wait. The server is saving, then it will stop.");
            var outcome = await Task.Run(() => _session.StopGracefully());
            _engineExited = false;
            _serving = false;
            if (!_session.IsRunning)
                _launchedSave = "";
            if (StopNote(outcome) is { } stopNote)
                Append(stopNote);
            var message = (_session.IsRunning
                ? "Stop was sent, but a server process is still running."
                : "Server is stopped.") + " " + saveNote;
            Append(message);
            SetControl(message, done: !_session.IsRunning, failed: _session.IsRunning);
            RefreshState();
            if (!_session.IsRunning)
                ScanAfterStop(startedAt, consoleLog);
        }
        catch (Exception ex)
        {
            Append(ex.Message);
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            _stoppingNow = false;
            EndControl();
        }
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Ask(this, "Restart server", "Restart the server? It saves the campaign first. Players are disconnected, then the same save loads again."))
            return;
        await RestartServerAsync();
    }

    // Saves, stops and starts the same save again. Restart and Copy invite (turning on Steam lobby) use it. False when
    // another server action runs: that refusal comes before the first await, so the task is already done.
    private async Task<bool> RestartServerAsync()
    {
        if (!BeginControl("Please wait. The server is saving, then it will restart."))
            return false;
        _userStop = true;
        _restartingNow = true;
        _actions.Note("Restart", "Requested restart for " + (_settings.Current().SaveName ?? "(no save)"));
        _loadRetry.ManualStart();
        try
        {
            PersistProfile(allowLockedStart: true);
            var refusal = SaveRefusal(stopping: true);
            if (refusal == null)
            {
                await WarnPlayersOfRestart();
                await SaveBeforeShutdown();
            }
            else
            {
                Append(refusal);
                SetControl(refusal, failed: true);
            }

            _announceSaveOnServing = null;
            if (refusal == null)
            {
                SetControl("Please wait. The server is saving, then it will restart.");
                if (StopNote(await Task.Run(() => _session.StopGracefully())) is { } stopNote)
                    Append(stopNote);
            }
            else
                await Task.Run(() => _session.Stop());
            _engineExited = false;
            _serving = false;
            await Task.Delay(1000);
            if (refusal == null)
                SetControl("Please wait. The server is saving, then it will restart.");
            StartServer();
            if (refusal != null)
                SetControl(_autosaveHeld ? BrokenWorldGuard.AutosaveHeldText : refusal, failed: true);
            // Restart stays on the page it was pressed on, like Start; the crash paths still open Logs.
        }
        catch (Exception ex)
        {
            Append(ex.Message);
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            _restartingNow = false;
            EndControl();
        }

        return true;
    }

    private async void RestartLastGood_Click(object sender, RoutedEventArgs e)
    {
        var name = _settings.Current().SaveName?.Trim() ?? "";
        if (name.Length == 0)
        {
            SetControl("Pick a campaign save first.", failed: true);
            return;
        }

        var brokenAt = BrokenWorldTime();
        if (brokenAt == null)
        {
            SetControl("The hero can rejoin this world. Last good save is kept for when they cannot.");
            return;
        }

        KeepLastGoodCopy();
        var live = Path.Combine(ServerSession.SavesPath, name + ".sav");
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
        if (!BeginControl("Loading the save from " + when + "."))
            return;

        _userStop = true;
        try
        {
            _session.Stop();
            _engineExited = false;
            _serving = false;
            _loadRetry.ManualStart();
            BackupBeforeReplace(name);
            LastGoodSave.CopyOver(pick, live);
            var jsonSource = Path.Combine(ServerSession.SavesPath, Path.GetFileNameWithoutExtension(pick) + ".json");
            var jsonDestination = Path.Combine(ServerSession.SavesPath, name + ".json");
            if (File.Exists(jsonSource))
                LastGoodSave.CopyOver(jsonSource, jsonDestination);
            _actions.Note("Last good save", "Restored " + name + " from " + Path.GetFileName(pick));
            // A lingering instance lock or engine would trip StartServer's running check; wait up to 5 s like Restart.
            for (var waited = 0; waited < 50 && _session.IsRunning; waited++)
                await Task.Delay(100);
            SetControl("Loaded the save from " + when + ". Starting the server.");
            _startClicked = true;
            StartServer();
            ShowPage("Logs");
        }
        catch (Exception ex)
        {
            Append(ex.Message);
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            _startClicked = false;
            EndControl();
        }
    }

    private void ApplyAutosaveForStart(string savePath)
    {
        if (!File.Exists(ServerSession.ServerConfigPath))
            return;
        var chosen = ChosenAutosaveMinutes();
        var brokenAt = BrokenWorldTime();
        var hold = BrokenWorldGuard.HoldAutosave(File.Exists(savePath) ? SaveHeader.ReadCreated(savePath) : null, brokenAt);
        var server = JsoncEditor.Read(ServerSession.ServerConfigPath);
        server = JsoncEditor.SetNumber(server, "autosaveMinutes", (hold ? 0 : chosen).ToString());
        JsoncEditor.Write(ServerSession.ServerConfigPath, server);
        if (chosen > 0)
        {
            _settings.Current().AutosaveMinutes = chosen;
            AutosaveBox.Text = chosen.ToString();
        }

        _autosaveHeld = hold;
        _spareUntilOnMap = brokenAt != null && !hold;
        if (hold)
            SetControl(BrokenWorldGuard.AutosaveHeldText, failed: true);
    }

    private int ChosenAutosaveMinutes()
    {
        if (int.TryParse(AutosaveBox.Text.Trim(), out var box) && box > 0)
            return box;
        if (_settings.Current().AutosaveMinutes > 0)
            return _settings.Current().AutosaveMinutes;
        return 5;
    }

    private void KeepLastGoodCopy()
    {
        try
        {
            var name = _settings.Current().SaveName?.Trim() ?? "";
            if (name.Length == 0)
                return;
            var directory = ServerSession.SavesPath;
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

    private void HoldBrokenAutosave()
    {
        if (_spareUntilOnMap)
        {
            if (BrokenWorldRefusal() == null)
                _spareUntilOnMap = false;
            return;
        }

        if (_autosaveHeld || _controlBusy || _copyingMods || !_session.StartedHere)
            return;
        if (!BrokenWorldGuard.StopOwnedServer(ownsProcess: true, alreadyHeld: false, spareUntilOnMap: false, RecentWorldLog(out _)))
            return;
        _autosaveHeld = true;
        _userStop = true;
        _session.Stop();
        _engineExited = false;
        _serving = false;
        SetControl(BrokenWorldGuard.AutosaveStoppedText, failed: true);
        Append(BrokenWorldGuard.AutosaveStoppedText);
    }

    private List<SaveCopy> SaveCopies(string name)
    {
        var copies = new List<SaveCopy>();
        foreach (var fileName in new[] { name + ".sav", name + ".backup1.sav", name + ".backup2.sav", name + LastGoodSave.Suffix + ".sav" })
        {
            var path = Path.Combine(ServerSession.SavesPath, fileName);
            if (!File.Exists(path))
                continue;
            copies.Add(new SaveCopy(path, SaveHeader.ReadCreated(path)));
        }

        return copies;
    }

    private string? BrokenWorldRefusal() => BrokenWorldGuard.Refusal(RecentWorldLog(out _));

    // Every path that saves, backs up or restarts asks this: a failed load first, then the broken world.
    private string? SaveRefusal(bool stopping = false) =>
        _session.LoadFailed ? (stopping ? BrokenWorldGuard.FailedLoadStopText : BrokenWorldGuard.FailedLoadSaveText) : BrokenWorldRefusal();

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
        if (!Directory.Exists(ServerSession.LogsPath))
            return _worldLogText;
        var file = new DirectoryInfo(ServerSession.LogsPath).GetFiles("coop-server-*.log")
            .OrderByDescending(item => item.LastWriteTimeUtc)
            .FirstOrDefault();
        if (file == null)
            return _worldLogText;
        file.Refresh();
        writtenAt = file.LastWriteTime;
        if (file.FullName == _worldLogPath && file.Length == _worldLogLength)
            return _worldLogText;
        var tail = HostLogFile.ReadTail(file.FullName, 400_000);
        if (!tail.Contains(BrokenWorldGuard.Marker, StringComparison.OrdinalIgnoreCase))
        {
            var older = HostLogFile.ReadTailContaining(file.FullName, BrokenWorldGuard.Marker, 400_000);
            if (older.Contains(BrokenWorldGuard.Marker, StringComparison.OrdinalIgnoreCase))
                tail = older;
        }

        _worldLogText = tail;
        _worldLogPath = file.FullName;
        _worldLogLength = file.Length;
        return _worldLogText;
    }

    private async Task<string> SaveBeforeShutdown()
    {
        if (_engineExited || !_session.CanSendCommands)
        {
            var skipped = _engineExited
                ? "Nothing new was saved because the game engine had already exited."
                : _session.CommandBlockReason();
            Append(SaveLogLine(skipped));
            SetControl(SaveLogLine(skipped), failed: true);
            return skipped;
        }

        Append("Saving the world.");
        try
        {
            // Wait until the save file is written; the server stops right after this.
            var saveName = _settings.Current().SaveName;
            var reply = string.IsNullOrWhiteSpace(saveName)
                ? await Task.Run(() => _session.RequestSave())
                : await Task.Run(() => _session.SaveCurrentWorld(saveName));
            Append(SaveLogLine(reply));
            return reply;
        }
        catch (Exception ex)
        {
            Append(SaveLogLine(ex.Message));
            return ex.Message;
        }
    }

    private static string SaveLogLine(string note) => "SAVE: " + note;

    private bool BeginControl(string message)
    {
        if (_controlBusy || _copyingMods)
            return false;
        _controlBusy = true;
        CancelCrashRestart();
        CancelRescueRetry();
        SetLaunchButtonsEnabled(false);
        SetControl(message);
        Append(message);
        return true;
    }

    private void EndControl()
    {
        _controlBusy = false;
        SetLaunchButtonsEnabled(true);
        // A Start that failed after it read the mods again left every row locked (ApplyProfileModules saw the busy flag),
        // and the block still matched, so the rows are set again by the real state now.
        ApplyModListBlock(force: true);
    }

    // The Dashboard's buttons follow the state and _controlBusy (ShowServerActions); off while an action runs.
    private void SetLaunchButtonsEnabled(bool enabled)
    {
        if (!enabled)
        {
            foreach (var button in new[] { StartButton, StopButton, StopStartingButton, SaveWorldButton, RestartButton, LastGoodButton, BackupNowButton })
                button.IsEnabled = false;
            return;
        }

        ShowServerActions(DashboardFacts());
    }

    private void SetControl(string message, bool done = false, bool failed = false)
    {
        // The missing server lines send the host to Download Coop server under Advanced: server folder, so that group
        // opens with them, the way ShowBlockedDllFix opens Advanced: Windows.
        if (SendsToServerFolder(message))
            ServerFolderPanel.Visibility = Visibility.Visible;
        ControlStatus.Foreground = failed
            ? new SolidColorBrush(Color.FromRgb(232, 120, 108))
            : done
                ? new SolidColorBrush(Color.FromRgb(90, 138, 96))
                : new SolidColorBrush(Color.FromRgb(176, 132, 72));
        var lines = message.Split('\n');
        if (lines.Length < 3)
        {
            ControlStatus.Text = message;
            return;
        }

        // Three lines (what happened, a list, what to do): the list sits on its own lighter line, so a long
        // run of mod names does not read as one wall of red text.
        ControlStatus.Text = "";
        ControlStatus.Inlines.Clear();
        ControlStatus.Inlines.Add(new Run(BannerlordCoopLauncher.Core.Localization.Translator.T(lines[0])));
        ControlStatus.Inlines.Add(new LineBreak());
        ControlStatus.Inlines.Add(new Run(string.Join("\n", lines[1..^1])) { FontWeight = FontWeights.Normal, Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)) });
        ControlStatus.Inlines.Add(new LineBreak());
        ControlStatus.Inlines.Add(new Run(BannerlordCoopLauncher.Core.Localization.Translator.T(lines[^1])) { FontWeight = FontWeights.Normal });
    }

    // MissingMessage, or the AD1259 server check with the server download line (English or already translated).
    private static bool SendsToServerFolder(string message)
    {
        if (message.Contains(DedicatedServerFinder.MissingMessage, StringComparison.Ordinal))
            return true;
        return BannerlordCoopLauncher.Core.Modules.CoopInstall.ServerGetLineNow("Set server folder") is { } line
            && (message.Contains(line, StringComparison.Ordinal)
                || message.Contains(BannerlordCoopLauncher.Core.Localization.Translator.T(line), StringComparison.Ordinal));
    }
}
