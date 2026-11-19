using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Steam;
using TaomCoop.Core;

namespace TaomCoopLauncher;

// Crash tools: safe mode (Plain TAOM once) and Find the crashing mod. Neither changes the saved setup.
public partial class MainWindow
{
    // Plain TAOM for the Connect one Safe mode click starts, and only for that one.
    private readonly SafeModeJoin _safeMode = new();

    // The setup the last Connect started the game with when that was a Safe mode start, else null. Send my logs and
    // a crash report list its mods, not the saved setup's, which that game never loaded. Set on every Connect start.
    private TaomSetup? _safeModeGameSetup;

    // The setup Connect starts: the saved one, or Plain TAOM for one safe mode start.
    // Mod ids are taken from the mods on this PC, also matched by Workshop id (TaomModIds).
    private TaomSetup LaunchSetup => TaomModIds.Resolve(_safeMode.On
        ? _settings.ModSetups.Find(TaomSetupBook.PlainName) ?? _settings.ModSetups.Current()
        : _settings.ModSetups.Current(), _installedExtras.Values);

    private async void SafeMode_Click(object sender, RoutedEventArgs e)
    {
        // The running Connect keeps its own setup, so Safe mode can not change it now.
        if (_connect.Running)
        {
            _actions.Note("Safe mode", "Clicked while a Connect was running. Nothing started.");
            Log(ConnectAlreadyRunning);
            return;
        }
        Log("Safe mode joins with Plain TAOM this one time. Your setups are not changed.");
        // The client can not see the host's setup before joining (Report Replies, TAOM): a host on TAOM plus
        // or a setup with extra mods turns a Plain TAOM join down, so say so before the game starts.
        Log(SafeModeNeedsPlainHost);
        await _safeMode.RunAsync(async () =>
        {
            UpdateConnectButton();
            await ConnectAsync();
        });
    }

    private void RememberJoin(string key, IReadOnlyList<string> modules)
    {
        void Save()
        {
            _settings.LastJoins[key] = CoopJoinRules.Compared(modules).ToList();
            SaveSettings();
        }

        if (Dispatcher.CheckAccess())
            Save();
        else
            Dispatcher.Invoke(Save);
    }

    // Halves the extra mods each round and starts the game to the main menu with one half, until
    // one mod is left. The saved setup stays as it is.
    private async void FindCrash_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _connect.Running)
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
        var setup = LaunchSetup;
        CrashBisect search;
        try
        {
            search = CrashBisect.Start(setup, Manifests(GatherModFacts()));
        }
        catch (InvalidOperationException ex)
        {
            Log(ex.Message);
            return;
        }

        if (Coodsz.Ui.UiLanguage.Message(
                "This starts the game to the main menu a few times, each time with half of the extra mods on " + setup.Name + ". Wait for the main menu or the crash, close the game, then answer. Your setup is not changed.",
                "Find the crashing mod", MessageBoxButton.OKCancel, MessageBoxImage.Information, this) != MessageBoxResult.OK)
            return;

        _busy = true;
        try
        {
            var labels = setup.Extras.ToDictionary(mod => mod.ModuleId, mod => mod.Label, StringComparer.OrdinalIgnoreCase);
            string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => labels.TryGetValue(id, out var label) ? label : id));
            while (!search.Done)
            {
                Log("Round " + search.Round + ". Testing " + Names(search.Testing) + ".");
                var crashed = await TestToMainMenuAsync(search.TestSetup());
                if (crashed == null)
                {
                    Log("Find the crashing mod was stopped. Your setup was not changed.");
                    return;
                }

                // A round that reached the main menu cleared every mod it turned on.
                if (crashed == false)
                    RecordResults(TaomModResults.CrashRoundPassed(search.TurnedOn.Select(id => (id, (string?)InstalledVersion(id)))));
                search.Report(crashed.Value);
            }

            var found = search.Found;
            if (found != null)
            {
                RecordResults(new[] { TaomModResults.CrashFound(found, InstalledVersion(found)) });
                ShowCrashReport(setup, new[] { found });
            }

            Log(found == null
                ? "No single mod was found. The crash may need two mods together, or it may not come from the extra mods."
                : "The crash most likely comes from " + Names(new[] { found }) + ". Turn it off or remove it, then click Save mods.");
        }
        finally
        {
            _busy = false;
        }
    }

    // ----- This PC's own results (mod-results.json), shown as the second pill on the mod rows -----

    private string InstalledVersion(string moduleId) =>
        _installedExtras.TryGetValue(moduleId, out var mod) ? BannerlordCoopLauncher.ModPills.VersionIn(mod.Directory) : "";

    private List<(string Id, string? Version)> SessionExtras(TaomSetup setup) =>
        setup.EnabledExtras
            .Where(mod => TaomModResults.IsExtraMod(mod.ModuleId))
            .Select(mod => (mod.ModuleId, (string?)InstalledVersion(mod.ModuleId)))
            .ToList();

    private void RecordResults(IEnumerable<ModResult> results)
    {
        if (ModResults.Record(results))
            ShowModuleList();
    }

    // The launch order of the setup in use, for the mod list a report carries.
    private IReadOnlyList<string> LaunchOrder() => LaunchOrder(LaunchSetup);

    private static IReadOnlyList<string> LaunchOrder(TaomSetup setup)
    {
        try
        {
            return TaomLoadOrder.LaunchModules(setup, false);
        }
        catch (InvalidOperationException)
        {
            return setup.EnabledExtras.Select(mod => mod.ModuleId).ToList();
        }
    }

    // After a join: when the game closes without a crash report, every extra mod of that session
    // worked for this player. Then the report bar offers to report them: the window when the player
    // turned it on, the crash bar after a crash, or How did your mods work? once per run.
    // setup is the one this game was started with: after the game closes, a Safe mode start is long over.
    private async Task RecordSessionAsync(Process started, TaomSetup setup, IReadOnlyList<(string Id, string? Version)> extras, IReadOnlyList<string> loadOrder)
    {
        // With no extra mod there is nothing to rate, but a crash still brings Send crash report.
        if (extras.Count == 0 && !BannerlordCoopLauncher.Core.Support.CrashReports.HasEndpoint())
            return;
        try
        {
            await started.WaitForExitAsync();
            int? exitCode = null;
            try { exitCode = started.ExitCode; }
            catch (InvalidOperationException) { }
            await Task.Delay(1500);
            var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
            var capture = await Task.Run(() => StartupCrashExplain.Read(documents, started.Id));
            var closedNormally = ModResults.ClosedNormally(capture.ExitCode ?? exitCode, capture.ReportText);
            if (closedNormally && extras.Count > 0)
            {
                var results = ModResults.SessionWorked(extras);
                await Dispatcher.InvokeAsync(() => RecordResults(results));
            }

            _ = Dispatcher.BeginInvoke(() =>
            {
                var request = ReportRequest(extras, loadOrder, afterGame: true, crashed: !closedNormally);
                if (!closedNormally)
                    request = request.AsCrash(CrashNamed(capture, setup), CrashLine(capture));
                _reportBar?.AfterGame(request, crashed: !closedNormally);
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
        }
    }

    // True when the game crashed before the main menu, false when it reached it, null when stopped.
    private async Task<bool?> TestToMainMenuAsync(TaomSetup test)
    {
        var gameRoot = _gameRoot!;
        try
        {
            ClientLaunchGuard.EnsureSingleGame();
            await UnblockBeforeLaunchAsync();
            ApplyCrashGuards();
            var arguments = TaomLaunch.Arguments(connect: false, steamLobby: false, password: null, modules: TaomLoadOrder.LaunchModules(test, joinPassword: false));
            Log(arguments);
            ApplyDependencyFix(test);
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = GameRoot.ExecutablePath(gameRoot),
                WorkingDirectory = GameRoot.WorkingDirectory(gameRoot),
                Arguments = arguments,
                UseShellExecute = false
            });
            if (started == null)
                throw new InvalidOperationException("Bannerlord did not start.");
            // This game is not the Safe mode one, so its reports go back to the saved setup's mods, as before.
            _safeModeGameSetup = null;
            await started.WaitForExitAsync();
            RestoreDependencyFix();
            var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
            await Task.Delay(1500);
            var capture = await Task.Run(() => StartupCrashExplain.Read(documents, started.Id));
            var lines = StartupCrashExplain.Explain(false, capture.ExitCode, capture.ReportText, capture.GameLog,
                Array.Empty<StartupCrashExplain.ModName>(), TaomLoadOrder.ClientOrder(test).ToList(), setupUsesWorkshop: true);
            if (StartupCrashExplain.SawStartupCrash(lines))
            {
                Log("The game crashed in this round.");
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            Log(ex.Message);
            return null;
        }

        var answer = Coodsz.Ui.UiLanguage.Message("Did the game reach the main menu without crashing?\n\nYes if it did. No if it crashed. Cancel stops the search.",
            "Find the crashing mod", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, this);
        return answer switch
        {
            MessageBoxResult.Yes => false,
            MessageBoxResult.No => true,
            _ => null
        };
    }
}
