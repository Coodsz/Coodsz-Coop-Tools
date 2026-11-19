using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace TaomCoopHost;

// "Test mods on the server" on the Mods page: the automatic server load test (TaomCoop.Core.ModLoadTest).
// Each mod gets one start of the Coodsz TAOM server (the same path as Start server for a setup with
// extra mods: copy to the server, unblock DLLs, the TAOM dependency fix, the engine started directly)
// with the TAOM core plus that mod, on a copy of the picked save named save-modtest. server-config.json
// points at the copy while the test runs and is put back afterwards; the copy is deleted at the end.
public partial class MainWindow
{
    private string ModTestResultsPath => Path.Combine(_settings.DataRoot, ModLoadTest.ResultsFileName);

    private IReadOnlyDictionary<string, ModuleManifest> HostManifests() =>
        HostModFacts().Where(pair => pair.Value.Manifest != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Manifest!, StringComparer.OrdinalIgnoreCase);

    private async void TestMods_Click(object sender, RoutedEventArgs e)
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
        var missingPiece = string.IsNullOrWhiteSpace(server) ? "The server folder is not set." : DedicatedEngineLaunch.MissingPiece(server);
        if (!File.Exists(Path.Combine(server ?? "", "BannerlordCoopServer.exe")) || missingPiece != null)
        {
            ModStatus("Prepare the server before testing mods. " + (missingPiece ?? ""), failed: true);
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
        // The bundled TAOM plus mods must be written out before the list looks for them.
        await ToolPayload.FolderAsync(ToolPayload.BundledMods);
        var mods = ModLoadTest.DefaultCandidates(_compat, HostMods(rescan: true), manifests);
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
        if (_busy || _startingServer)
            return "Wait for the current action to finish.";
        if (ServerIsRunning())
            return "Stop the server first. The test starts its own server for each mod.";
        var server = _settings.ServerRoot;
        if (UdpPortBusy(4200))
            return "UDP 4200 is already in use. Stop the other dedicated server first.";
        if (WarSailsCheck.Refusal(CoopModConfig.UserPath(), TaomLoadOrder.ServerOrder(HostSetup)) is { } warSails)
            return warSails;
        var acceptTerms = AskServerTerms(server);
        if (acceptTerms == null)
            return ServerTerms.Refused;
        var framework = ServerFramework.Ensure(server);
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
            // Autosave off and no Steam lobby: the test server only has to reach serving.
            WriteConfigFile(configPath, testName, PasswordBox.Text, 0, false, LogFileBox.IsChecked != false);
            NoteLog("Server load test on " + testName + ", a copy of " + saveName + ". Testing " + string.Join(", ", mods.Select(mod => mod.Label)) + ".");
            var host = new HostModTestServer(this, server, acceptTerms == true);
            var results = await ModLoadTest.RunAllAsync(mods, mod => ModLoadTest.SetupFor(mod, manifests), host, ModTestOptions.Default,
                SystemModTestClock.Instance, progress, result =>
                {
                    try
                    {
                        ModLoadTest.Record(ModTestResultsPath, result);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        NoteLog("Could not save the test result. " + ex.Message);
                    }

                    // This PC's own results, shown as Passed or Failed on your server next to the list badge.
                    BannerlordCoopLauncher.Core.Modules.ModResults.Record(TaomModResults.FromServerTest(result, BannerlordCoopLauncher.Core.Modules.ModResults.Now()));

                    var name = result.Name.Length > 0 ? result.Name : result.Id;
                    NoteLog(result.Passed ? name + " passed the server load test." : name + " failed the server load test. " + result.Reason);
                    recorded(result);
                }, cancellationToken);
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
                    ModLoadTest.RemoveTestSave(ServerSavesDirectory(), testName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                NoteLog("Could not delete the test save " + testName + ". " + ex.Message);
            }

            _busy = false;
            await ToolPayload.FolderAsync(ToolPayload.BundledMods);
            HostMods(rescan: true);
            RefreshState();
        }
    }

    // Writes the TAOM mod list with the passed mods in it as source server-loads, for Coodsz to publish.
    private string ExportModTestResults()
    {
        var results = ModLoadTest.LoadResults(ModTestResultsPath);
        if (!results.Any(result => result.Passed))
            return "No mod passed yet, so there is nothing to copy into the mod list.";
        var path = Path.Combine(_settings.DataRoot, ModLoadTest.ExportFileName);
        try
        {
            var merged = ModLoadTest.MergePassed(_compat, results, DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            File.WriteAllText(path, ModLoadTest.Serialize(merged));
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "Could not write the mod list file. " + ex.Message;
        }

        var line = "Wrote " + path + ". It is the TAOM mod list with the mods that passed marked Loads on the server. Send it to Coodsz to publish.";
        NoteLog(line);
        return line;
    }

    // One test start of the Coodsz TAOM server for the load test.
    private sealed class HostModTestServer(MainWindow owner, string serverRoot, bool acceptTerms) : IModTestServer
    {
        public async Task<IModTestRun> StartAsync(TaomSetup setup, CancellationToken cancellationToken)
        {
            var missingPiece = DedicatedEngineLaunch.MissingPiece(serverRoot);
            if (missingPiece != null)
                throw new InvalidOperationException(missingPiece + " Prepare the server again.");
            var plan = TaomServerPath.Plan(setup, owner.HostMods());
            if (plan.Missing.Count > 0)
                throw new InvalidOperationException(string.Join(", ", plan.Missing.Select(mod => mod.Label)) + " is not on this PC.");

            // The last test server can hold the port for a moment after it stops.
            for (var wait = 0; wait < 30 && UdpPortBusy(4200); wait++)
                await Task.Delay(1000, cancellationToken);
            if (UdpPortBusy(4200))
                throw new InvalidOperationException("UDP 4200 is still in use by another server.");

            var lines = await Task.Run(() =>
            {
                var staged = TaomServerPath.Stage(serverRoot, plan);
                ServerModuleGuard.RejectClientXorberax(serverRoot);
                return staged;
            }, cancellationToken);
            var coopData = Path.GetDirectoryName(CoopModConfig.UserPath()) ?? "";
            var engine = TaomServerPath.Launch(serverRoot, owner._settings.DataRoot, coopData, plan.Setup);
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
            // The data folder for Coodsz Server Tools, as on a real start, so a test never reads the Coop Server Tool's files.
            foreach (var (key, value) in TaomServerTools.StartVariables(owner._settings.DataRoot))
                start.Environment[key] = value;
            if (acceptTerms)
                start.Environment[ServerTerms.EnvironmentVariable] = "true";

            var log = new ConsoleLogFile();
            log.Open(Path.Combine(owner._settings.DataRoot, "logs", "modtest"), "[TAOM host] Server load test " + string.Join(", ", plan.Setup.EnabledExtras.Select(mod => mod.Label)) + ". Start " + start.FileName + " " + start.Arguments);
            foreach (var line in lines)
                log.Write("[TAOM host] " + line);
            log.Write("[TAOM host] Server load order: " + string.Join(", ", engine.Modules) + ".");
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
            file.Write("[TAOM host] Test server stopped.");
        }

        public void Dispose()
        {
            file.Close();
            process.Dispose();
        }
    }
}
