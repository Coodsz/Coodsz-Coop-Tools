using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using TaomCoop.Core;

namespace CoodszHost;

// "Test mods on the server" on the Modules page (Coodsz Host only; the AD1259 build hides it). The
// TAOM server tool's load test (TaomCoop.Core.ModLoadTest) with Coodsz Host's server path: for each
// mod the server starts through Start-ModdedServer.ps1 with the working list core plus that mod and
// the mods it needs, on a new throwaway campaign (HostModTest). The module list files and
// server-config.json are put back afterwards, mods copied in only for the test are taken out again,
// and the throwaway campaigns are deleted. Results go to Data\modtest-results.json.
public partial class MainWindow
{
    // True while a test runs: the main server session then leaves the test server's lines alone.
    // Read on the server output threads too, when a line is queued.
    private volatile bool _modTestRunning;

    private static string ModTestData => HostModTest.DataFolder(ToolData.Folder);

    private static string ModTestResultsPath => Path.Combine(ModTestData, HostModTest.ResultsFileName);

    // The button is Coodsz Host only.
    private void ShowModTestButton() =>
        TestModsButton.Visibility = HostModTest.Offered ? Visibility.Visible : Visibility.Collapsed;

    private void ModTestHint(string text)
    {
        SetModuleSaveHint(text);
        ModuleSaveHint.Visibility = Visibility.Visible;
    }

    private void TestMods_Click(object sender, RoutedEventArgs e)
    {
        if (!HostModTest.Offered)
            return;
        if (RefuseWhileModListRuns())
            return;
        if (_controlBusy)
        {
            ModTestHint("Test mods waits until the tool finishes what it is doing now. Click it again in a moment.");
            return;
        }

        if (!File.Exists(Executable))
        {
            ModTestHint(DedicatedServerFinder.MissingMessage);
            return;
        }

        if (_session.IsRunning)
        {
            ModTestHint("Stop the server first. The test starts its own server for each mod.");
            return;
        }

        var onServer = ModuleCatalog.Load(ServerRoot);
        var onThisPc = ExtraModScan.Scan(AppContext.BaseDirectory, StableCoop.Order).ToList();
        // Mods already copied into the server Modules folder count as on this PC too.
        var modulesRoot = ModuleCatalog.ModulesRoot(ServerRoot);
        foreach (var row in onServer)
        {
            if (onThisPc.Any(mod => mod.ModuleId.Equals(row.Id, StringComparison.OrdinalIgnoreCase)))
                continue;
            var folder = ModuleCatalog.FoldersFor(modulesRoot, row.Id).FirstOrDefault();
            if (folder != null)
                onThisPc.Add(new FoundServerMod(row.Name, row.Id, folder, "Server"));
        }

        var mods = HostModTest.Candidates(onThisPc, StableCoop.Order, onServer);
        HostModTestWindow.Show(this, mods, CommunityCompat.Current,
            (picked, progress, recorded, token) => RunModTestsAsync(picked, onThisPc, progress, recorded, token),
            () => ModLoadTest.LoadResults(ModTestResultsPath),
            ExportModTestResults);
    }

    private async Task<string> RunModTestsAsync(
        IReadOnlyList<ModTestCandidate> mods,
        IReadOnlyList<FoundServerMod> onThisPc,
        IProgress<ModTestProgress> progress,
        Action<ModTestResult> recorded,
        CancellationToken cancellationToken)
    {
        if (_session.IsRunning)
            return "Stop the server first. The test starts its own server for each mod.";
        if (!File.Exists(Executable))
            return DedicatedServerFinder.MissingMessage;
        if (BannerlordCoopLauncher.Core.Server.WarSailsCheck.Refusal(ServerSession.ModConfigPath, mods.Select(mod => mod.ModuleId)) is { } warSails)
            return warSails;
        bool acceptEula;
        try
        {
            acceptEula = AskServerTerms();
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        if (!BeginControl("Testing mods on the server. Players cannot join while it runs."))
            return "Test mods waits until the tool finishes what it is doing now. Click Start test again in a moment.";

        var root = ServerRoot;
        var kept = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        var copied = new List<string>();
        var saves = new List<string>();
        _modTestRunning = true;
        try
        {
            foreach (var path in new[] { ModuleCatalog.ListPath(root), ModuleCatalog.SelectionPath(root), ServerSession.ServerConfigPath })
                kept[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;

            var repaired = DedicatedServerRepair.Ensure(root);
            if (repaired != null)
                Append(repaired);
            foreach (var note in (CoopServerBin.Ensure(root) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Append(note);
            var framework = ServerFramework.Ensure(root, createDotnetHome: true);
            if (!framework.Ready)
                return framework.Detail;

            var installed = ModuleCatalog.Load(root);
            var baseOrder = HostModTest.BaseOrder(StableCoop.Order, installed.Select(row => row.Id).ToList());
            var coop = installed.FirstOrDefault(row => baseOrder.Contains(row.Id, StringComparer.OrdinalIgnoreCase)
                && row.Id is "Coop" or "CoopNightly")?.Version ?? "";
            ModTestCandidate? current = null;
            var flavor = HostModTest.Flavor(coop, StableCoop.Order, () => current);
            var profile = _settings.Current();
            var server = new HostModTestServer(this, root, baseOrder, onThisPc, () => current, copied, saves,
                profile.EnginePort, profile.Region, acceptEula);
            Append("Test mods on the server: " + string.Join(", ", mods.Select(mod => mod.Label)) + ". Each one runs on the working list core and a new throwaway campaign.");
            _actions.Note("Test mods", string.Join(", ", mods.Select(mod => mod.ModuleId)));
            var results = await ModLoadTest.RunAllAsync(mods,
                mod =>
                {
                    current = mod;
                    var setup = new TaomSetup { Name = ModLoadTest.SetupName };
                    foreach (var need in mod.Needs)
                        setup.Extras.Add(need.Copy());
                    setup.Extras.Add(new TaomExtraMod { Name = mod.Name, ModuleId = mod.ModuleId, WorkshopId = mod.WorkshopId });
                    return setup;
                },
                server, ModTestOptions.Default, SystemModTestClock.Instance, progress, result =>
                {
                    try
                    {
                        ModLoadTest.Record(ModTestResultsPath, result, HostModTest.ResultsKind);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Dispatcher.Invoke(() => Append("Could not save the test result. " + ex.Message));
                    }

                    // This PC's own results (the Passed or Failed on your server pill next to the list badge).
                    if (Edition.ShowsModResults)
                        ModResults.Record(TaomModResults.FromServerTest(result, ModResults.Now()));

                    var name = result.Name.Length > 0 ? result.Name : result.Id;
                    Dispatcher.Invoke(() => Append(result.Passed
                        ? name + " passed the server load test."
                        : name + " failed the server load test. " + result.Reason));
                    recorded(result);
                }, cancellationToken, flavor);
            var passed = results.Count(result => result.Passed);
            var summary = "Tested " + results.Count + " of " + mods.Count + " mods. " + passed + " passed, " + (results.Count - passed) + " failed.";
            return cancellationToken.IsCancellationRequested ? "Stopped. " + summary : summary;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return ex.Message;
        }
        finally
        {
            foreach (var (path, bytes) in kept)
            {
                try
                {
                    if (bytes != null)
                        File.WriteAllBytes(path, bytes);
                    else if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Append("Could not put " + Path.GetFileName(path) + " back after the test. Click Save module config and Save settings to write it again. " + ex.Message);
                }
            }

            foreach (var folder in copied)
            {
                try
                {
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Append("Could not take the test copy of " + Path.GetFileName(folder) + " out of the server Modules folder. " + ex.Message);
                }
            }

            foreach (var save in saves)
            {
                try
                {
                    HostModTest.RemoveTestSave(ServerSession.SavesPath, save);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Append("Could not delete the throwaway campaign " + save + ". " + ex.Message);
                }
            }

            _modTestRunning = false;
            EndControl();
            SetControl("Test mods on the server finished.", done: true);
            ReloadModules();
            ReloadSaves();
            RefreshState();
        }
    }

    // Writes a copy of the community mod list with the passed mods marked as loading on the server.
    private string ExportModTestResults()
    {
        var passes = HostModTest.Passes(ModLoadTest.LoadResults(ModTestResultsPath));
        if (passes.Count == 0)
            return "No mod passed yet, so there is nothing to copy into the mod list.";
        var path = Path.Combine(ModTestData, HostModTest.ExportFileName);
        try
        {
            Directory.CreateDirectory(ModTestData);
            File.WriteAllText(path, CompatListServerLoads.Merge(CompatListServerLoads.CurrentText(), passes), new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            return "Could not write the mod list file. " + ex.Message;
        }

        var line = "Wrote " + path + ". It is the community mod list with the mods that passed marked as loading on the server. Send it to Coodsz to publish.";
        Append(line);
        return line;
    }

    // One test start: the test module list, a new throwaway campaign, and a server session of its own.
    private sealed class HostModTestServer(
        MainWindow owner,
        string serverRoot,
        IReadOnlyList<string> baseOrder,
        IReadOnlyList<FoundServerMod> onThisPc,
        Func<ModTestCandidate?> current,
        List<string> copied,
        List<string> saves,
        int enginePort,
        string region,
        bool acceptEula) : IModTestServer
    {
        public async Task<IModTestRun> StartAsync(TaomSetup setup, CancellationToken cancellationToken)
        {
            var mod = current() ?? throw new InvalidOperationException("No mod to test.");
            var modulesRoot = ModuleCatalog.ModulesRoot(serverRoot);
            await Task.Run(() =>
            {
                // Mods that are not on the server yet are copied in for this test and taken out after.
                foreach (var extra in setup.Extras)
                {
                    if (ModuleCatalog.FoldersFor(modulesRoot, extra.ModuleId).Count > 0)
                        continue;
                    var source = onThisPc.FirstOrDefault(found => found.ModuleId.Equals(extra.ModuleId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(extra.Label + " is not on this PC.");
                    var destination = Path.Combine(modulesRoot, extra.ModuleId);
                    if (Directory.Exists(destination))
                        throw new InvalidOperationException("The server Modules folder has a " + extra.ModuleId + " folder that is not " + extra.Label + ".");
                    ExtraModScan.Install(source.Directory, serverRoot, extra.ModuleId);
                    lock (copied)
                        copied.Add(destination);
                }

                var (rows, missing) = HostModTest.TestRows(ModuleCatalog.Load(serverRoot), baseOrder, mod);
                if (missing.Count > 0)
                    throw new InvalidOperationException(string.Join(", ", missing) + " is not in the server Modules folder. Start the Working stable mods setup once so the tool copies it in.");
                ModuleCatalog.Write(serverRoot, rows);
            }, cancellationToken);

            for (var wait = 0; wait < 30 && ServerSession.InstanceLockHeld(serverRoot); wait++)
                await Task.Delay(1000, cancellationToken);

            var save = HostModTest.NewSaveName();
            lock (saves)
                saves.Add(save);
            var session = new ServerSession();
            var run = new HostModTestRun(session, save);
            session.Start(owner.Executable, new LaunchOptions
            {
                EnginePort = enginePort,
                Region = region,
                SaveName = save,
                CreateNew = true,
                AcceptEula = acceptEula
            });
            return run;
        }
    }

    private sealed class HostModTestRun : IModTestRun
    {
        // Enough for the state events and the last errors; a long start is cut from the front.
        private const int Keep = 2_000_000;
        private readonly ServerSession _session;
        private readonly string _save;
        private readonly StringBuilder _log = new();
        private readonly object _gate = new();
        private volatile bool _exited;
        private int? _exitCode;

        public HostModTestRun(ServerSession session, string save)
        {
            _session = session;
            _save = save;
            session.ServerLine += Add;
            session.StatusLine += line =>
            {
                Add(line);
                // Sent after the last output lines of the process were read.
                if (line.Contains("Server process exited", StringComparison.OrdinalIgnoreCase))
                    _exited = true;
            };
            session.Exited += exit => _exitCode = exit.ExitCode;
        }

        private void Add(string line)
        {
            lock (_gate)
            {
                _log.Append(line).Append('\n');
                if (_log.Length > Keep)
                    _log.Remove(0, _log.Length - Keep / 2);
            }
        }

        public string Log
        {
            get
            {
                lock (_gate)
                    return _log.ToString();
            }
        }

        public bool HasExited => _exited;

        public int? ExitCode => _exited ? _exitCode : null;

        // The server's own stop first, then whatever is left of it is ended; the campaign is thrown away.
        public async Task StopAsync()
        {
            await Task.Run(() =>
            {
                if (_session.StartedHere)
                    _session.StopGracefully(TimeSpan.FromSeconds(30));
                HostModTest.RemoveTestSave(ServerSession.SavesPath, _save);
            });
        }

        public void Dispose() => _session.Dispose();
    }
}
