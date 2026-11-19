using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresHost;

// "Test mods on the server" on the Mods page: the automatic server load test (TaomCoop.Core.ModLoadTest
// with the Burning Empires flavor, BeModTest). Each mod gets one start of the Burning Empires server (the
// same path as Start server: the core and the mod staged, the order written, Start-ModdedServer.ps1)
// with the Burning Empires core plus that mod, on a copy of the picked save named save-modtest, its
// BESavedata copied with it. server-config.json points at the copy while the test runs and is put back
// afterwards; the copy and its BESavedata are deleted at the end.
public partial class MainWindow
{
    private string ModTestResultsPath => Path.Combine(_settings.DataRoot, ModLoadTest.ResultsFileName);

    private IReadOnlyDictionary<string, ModuleManifest> HostManifests() =>
        HostModFacts().Where(pair => pair.Value.Manifest != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Manifest!, StringComparer.OrdinalIgnoreCase);

    private void TestMods_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            ModStatus("Test mods waits until the tool finishes what it is doing now. Click it again in a moment.");
            return;
        }

        if (ServerIsRunning())
        {
            ModStatus("Stop the server first. The test starts its own server for each mod.", failed: true);
            return;
        }

        var server = _settings.ServerRoot;
        var missingPiece = string.IsNullOrWhiteSpace(server) ? "The server folder is not set. Click Prepare server." : BeServerPrepare.MissingPiece(server);
        if (missingPiece != null)
        {
            ModStatus("Prepare the server before testing mods. " + missingPiece, failed: true);
            return;
        }

        var save = SelectedSavePath();
        if (!SaveSlot.CanHost(save, ServerSavesDirectory()))
        {
            ModStatus("Pick a save in the server Game Saves folder first. The test runs on a copy of it.", failed: true);
            return;
        }

        var saveName = Path.GetFileNameWithoutExtension(save);
        if (saveName.EndsWith(ModLoadTest.TestSaveSuffix, StringComparison.OrdinalIgnoreCase))
        {
            ModStatus("Pick your real save, not the test copy.", failed: true);
            return;
        }

        var manifests = HostManifests();
        var mods = BeModTest.DefaultCandidates(_compat, HostMods(rescan: true), manifests);
        ModTestWindow.Show(this, mods, _compat, saveName,
            (picked, progress, recorded, token) => RunModTestsAsync(picked, saveName, manifests, progress, recorded, token),
            () => ModLoadTest.LoadResults(ModTestResultsPath),
            ExportModTestResults);
        ShowModSetups();
    }

    private async Task<string> RunModTestsAsync(
        IReadOnlyList<ModTestCandidate> mods,
        string saveName,
        IReadOnlyDictionary<string, ModuleManifest> manifests,
        IProgress<ModTestProgress> progress,
        Action<ModTestResult> recorded,
        CancellationToken cancellationToken)
    {
        if (_busy || _startingServer || ServerIsRunning())
            return "Stop the server first. The test starts its own server for each mod.";
        var server = _settings.ServerRoot;
        if (UdpPortBusy(DirectPort))
            return "UDP " + DirectPort + " is already in use. Stop the other dedicated server first.";
        if (WarSailsCheck.Refusal(CoopModConfig.UserPath(), BeLoadOrder.ServerOrder(HostSetup)) is { } warSails)
            return warSails;
        var acceptTerms = AskServerTerms(server);
        if (acceptTerms == null)
            return BeServerTerms.Refused;
        var framework = ServerFramework.Ensure(server, createDotnetHome: true);
        if (!framework.Ready)
            return framework.Detail;

        var configPath = Path.Combine(_settings.DataRoot, "server-config.json");
        string original;
        try
        {
            original = File.ReadAllText(configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "server-config.json could not be read, so nothing was tested. " + ex.Message;
        }

        string? testName = null;
        _busy = true;
        try
        {
            testName = ModLoadTest.CopyTestSave(ServerSavesDirectory(), saveName);
            BeSaveData.CopyTo(BeSaveData.ServerModule(server), saveName, testName);
            // Autosave off and no Steam lobby: the test server only has to reach serving.
            WriteConfigFile(configPath, testName, PasswordBox.Text, 0, false, LogFileBox.IsChecked != false);
            NoteLog("Server load test on " + testName + ", a copy of " + saveName + ". Testing " + string.Join(", ", mods.Select(mod => mod.Label)) + ".");
            var host = new HostModTestServer(this, server, acceptTerms == true, testName);
            var results = await ModLoadTest.RunAllAsync(mods, mod => BeModTest.SetupFor(mod, manifests), host, ModTestOptions.Default,
                SystemModTestClock.Instance, progress, result =>
                {
                    try
                    {
                        BeModTest.Record(ModTestResultsPath, result);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        NoteLog("Could not save the test result. " + ex.Message);
                    }

                    // This PC's own results, shown as Passed or Failed on your server next to the list badge.
                    BeModResults.Record(BeModResults.FromServerTest(result, BannerlordCoopLauncher.Core.Modules.ModResults.Now()));

                    var name = result.Name.Length > 0 ? result.Name : result.Id;
                    NoteLog(result.Passed ? name + " passed the server load test." : name + " failed the server load test. " + result.Reason);
                    recorded(result);
                }, cancellationToken, BeModTest.Flavor((log, _) => BeHostReport.Explain(log, wasServing: false)?.Status));
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
            try
            {
                File.WriteAllText(configPath, original);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                NoteLog("Could not put server-config.json back after the test. Click Save settings to write it again. " + ex.Message);
            }

            try
            {
                if (testName != null)
                {
                    ModLoadTest.RemoveTestSave(ServerSavesDirectory(), testName);
                    BeSaveData.Delete(BeSaveData.ServerModule(server), testName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                NoteLog("Could not delete the test save " + testName + ". " + ex.Message);
            }

            _busy = false;
            HostMods(rescan: true);
            RefreshState();
        }
    }

    // Writes the Burning Empires mod list with the passed mods in it as source server-loads, for Coodsz to publish.
    private string ExportModTestResults()
    {
        var results = ModLoadTest.LoadResults(ModTestResultsPath);
        if (!results.Any(result => result.Passed))
            return "No mod passed yet, so there is nothing to copy into the mod list.";
        var path = Path.Combine(_settings.DataRoot, BeModTest.ExportFileName);
        try
        {
            var merged = BeModTest.MergePassed(_compat, results, DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            File.WriteAllText(path, BeModTest.Serialize(merged));
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "Could not write the mod list file. " + ex.Message;
        }

        var line = "Wrote " + path + ". It is the Burning Empires mod list with the mods that passed marked Loads on the server. Send it to Coodsz to publish.";
        NoteLog(line);
        return line;
    }

    // One test start of the Burning Empires server for the load test.
    private sealed class HostModTestServer(MainWindow owner, string serverRoot, bool acceptTerms, string testSave) : IModTestServer
    {
        public async Task<IModTestRun> StartAsync(TaomSetup setup, CancellationToken cancellationToken)
        {
            var missingPiece = BeServerPrepare.MissingPiece(serverRoot);
            if (missingPiece != null)
                throw new InvalidOperationException(missingPiece);
            var plan = owner.ServerPlan(setup, out var orderProblem) ?? throw new InvalidOperationException(orderProblem);
            if (plan.Missing.Count > 0)
                throw new InvalidOperationException(string.Join(", ", plan.Missing.Select(mod => mod.Label)) + " is not on this PC.");

            // The last test server can hold the port for a moment after it stops.
            for (var wait = 0; wait < 30 && UdpPortBusy(DirectPort); wait++)
                await Task.Delay(1000, cancellationToken);
            if (UdpPortBusy(DirectPort))
                throw new InvalidOperationException("UDP " + DirectPort + " is still in use by another server.");

            var sources = owner.CoreSources();
            var workshop = owner.ModuleRepairSource(serverRoot);
            var dataRoot = owner._settings.DataRoot;
            var lines = await Task.Run(() =>
            {
                // -AllowAddedModules below needs Coodsz Host's own start script, not the Workshop's.
                var staged = new List<string>();
                if (BeServerPrepare.LayOverRepair(serverRoot) is { } repaired)
                    staged.Add(repaired);
                staged.AddRange(BeServerPath.StageCore(serverRoot, sources));
                staged.AddRange(BeServerPath.StageExtras(serverRoot, plan));
                BeServerPath.WriteOrder(serverRoot, plan.ServerModules);
                BeServerPrepare.WritePolicy(serverRoot);
                // A module DLL antivirus took would fail every test the same way (BeServerModuleRepair).
                staged.AddRange(owner.EnsureModuleFiles(serverRoot, dataRoot, workshop, sources));
                return staged;
            }, cancellationToken);
            // The test copy may get the tested mod added; Start-ModdedServer.ps1 would ask about that.
            var engine = BeServerLaunch.Build(serverRoot, new BeStartOptions { SaveName = testSave, AcceptTerms = acceptTerms, AllowAddedModules = true }, BeHostFolders.GameSaves(owner._settings.DataRoot));
            var start = new ProcessStartInfo
            {
                FileName = engine.FileName,
                Arguments = engine.Arguments,
                WorkingDirectory = engine.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var (key, value) in engine.Environment)
                start.Environment[key] = value;

            var log = new ConsoleLogFile();
            log.Open(Path.Combine(owner._settings.DataRoot, "logs", "modtest"), "[BE host] Server load test " + string.Join(", ", plan.Setup.EnabledExtras.Select(mod => mod.Label)) + ". Start " + start.FileName + " " + start.Arguments);
            foreach (var line in lines)
                log.Write("[BE host] " + line);
            log.Write("[BE host] Server load order: " + string.Join(", ", plan.ServerModules) + ".");
            var run = new HostModTestRun(new Process { StartInfo = start, EnableRaisingEvents = true }, log);
            try
            {
                run.Start();
            }
            catch
            {
                run.Dispose();
                throw;
            }

            return run;
        }
    }

    private sealed class HostModTestRun(Process process, ConsoleLogFile file) : IModTestRun
    {
        // Enough for the state events and the last errors; a long start is cut from the front.
        private const int Keep = 2_000_000;
        private readonly StringBuilder _log = new();
        private readonly object _gate = new();

        public void Start()
        {
            process.OutputDataReceived += (_, args) => Add(args.Data);
            process.ErrorDataReceived += (_, args) => Add(args.Data);
            if (!process.Start())
                throw new InvalidOperationException("The test server did not start.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        private void Add(string? line)
        {
            if (line == null)
                return;
            file.Write(line);
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

        public bool HasExited
        {
            get
            {
                try
                {
                    return process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public int? ExitCode => HasExited ? SafeExitCode() : null;

        private int? SafeExitCode()
        {
            try
            {
                return process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        // The server's own stop first (it saves the test copy and exits), then it is ended.
        public async Task StopAsync()
        {
            if (HasExited)
                return;
            try
            {
                process.StandardInput.WriteLine(ServerCommand.Stop());
                process.StandardInput.Flush();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
            }

            await Task.Run(() =>
            {
                try
                {
                    if (!process.WaitForExit(60_000))
                        process.Kill(entireProcessTree: true);
                    process.WaitForExit(10_000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            });
            file.Write("[BE host] Test server stopped.");
        }

        public void Dispose()
        {
            file.Close();
            process.Dispose();
        }
    }
}
