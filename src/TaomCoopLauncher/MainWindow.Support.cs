using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace TaomCoopLauncher;

public partial class MainWindow
{
    private string? _memoryNoted;
    private string _twinsNoted = "";

    // A mod of this setup in two Workshop items: the game loads one of them, and a
    // re-upload of Harmony, ButterLib or UIExtenderEx crashes it at startup.
    private IReadOnlyList<WorkshopTwin> FindWorkshopTwins(string? gameLog = null) =>
        WorkshopTwins.Find(Workshops().Select(library => library.ContentDirectory),
            TaomLoadOrder.ClientOrder(_settings.ModSetups.Current()),
            gameLog: () => gameLog, fallbackLog: () => WorkshopTwins.LatestClientLog(StartupCrashExplain.EngineLogDirectory()));

    private void NoteWorkshopTwins()
    {
        var lines = FindWorkshopTwins().Select(WorkshopTwins.Describe).ToList();
        var key = string.Join("|", lines);
        if (key == _twinsNoted)
            return;
        _twinsNoted = key;
        foreach (var line in lines)
            Log(line);
    }

    // A short line under Connect when this PC is low on memory, or runs a dedicated server
    // with too little memory for both (TAOM froze at 97% on a 16 GB laptop that hosted).
    // It never blocks Connect.
    private async Task CheckMemoryAsync()
    {
        string? warning = null;
        try
        {
            warning = await Task.Run(() => LowMemory.ClientWarning(LowMemory.Read(), LowMemory.DedicatedServerRunning()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        MemoryWarning.Text = warning ?? "";
        MemoryWarning.Visibility = warning == null ? Visibility.Collapsed : Visibility.Visible;
        if (warning == null || warning == _memoryNoted)
            return;
        _memoryNoted = warning;
        Log(warning);
        _actions.Note("Low memory", warning);
    }

    // The game closed before it joined: read its crash report and engine log
    // and say what to change, like the Coop and Friends client does.
    // setup is the one this game was started with: Safe mode's Plain TAOM, or the saved setup.
    private async Task ExplainEarlyExitAsync(Process started, TaomSetup setup)
    {
        int? exitCode = null;
        try
        {
            exitCode = started.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }

        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
        await Task.Delay(1500);
        var capture = await Task.Run(() => StartupCrashExplain.Read(documents, started.Id));
        NoteBlockedDll(capture.ReportText + "\n" + capture.GameLog);
        // Another Bannerlord still open: two copies fight over TAOM's log file and the second one closes
        // (TAOM CR-W4H9X). The player may have opened it on purpose, so a log that names a mod still gets its hint.
        var otherGame = ClientLaunchGuard.OtherGameRunning(started.Id);
        if (otherGame)
            Log(ClientLaunchGuard.SecondCopyClosed);
        var lines = StartupCrashExplain.Explain(
            false,
            capture.ExitCode ?? exitCode,
            capture.ReportText,
            capture.GameLog,
            Array.Empty<StartupCrashExplain.ModName>(),
            TaomLoadOrder.ClientOrder(setup).ToList(),
            setupUsesWorkshop: false);
        if (!StartupCrashExplain.SawStartupCrash(lines))
            return;
        var named = CrashNamed(capture, setup);
        if (StartupCrashExplain.OnlyTheOtherCopy(otherGame, named, lines, capture.ReportText, capture.GameLog))
            return;
        // The shared line says "no extra mods" because no list is passed in; a setup with extra mods gets
        // TAOM's own buttons instead.
        if (setup.HasEnabledExtras && lines is [StartupCrashExplain.NoExtraModsLine])
            lines = [NoModNamedLine(setup)];
        foreach (var line in WorkshopTwins.PointCrashAt(lines, FindWorkshopTwins(capture.GameLog)))
            Log(line);
        ShowCrashReport(setup, named, CrashLine(capture));
    }

    private static string NoModNamedLine(TaomSetup setup) =>
        "The game closed while starting, and the log does not name a mod. " + setup.Name + " adds extra mods. Try Safe mode or Find the crashing mod on the Logs and help page.";

    // Subscribes to Bannerlord Coop (Workshop 3770450698, Coop 0.1.7 since 2026-10-30) through Steam itself and
    // waits for the download. Falls back to the Workshop page.
    private async Task<string?> SubscribeCoopAsync()
    {
        var gameRoot = _gameRoot;
        if (gameRoot == null || !ulong.TryParse(TaomStack.CoopWorkshopId, out var id))
            return null;

        Log("Workshop item " + TaomStack.CoopWorkshopId + " (Bannerlord Coop) is not downloaded. Asking Steam to subscribe and download it.");
        string? failure = null;
        WorkshopSubscribeResult? result = null;
        var names = new Dictionary<ulong, string> { [id] = "Bannerlord Coop" };
        await Task.Factory.StartNew(() =>
        {
            // Every Steam call has to come from this one thread.
            using var steam = SteamApiWorkshop.TryOpen(gameRoot, out var reason);
            if (steam == null)
            {
                failure = reason;
                return;
            }

            result = WorkshopSubscribe.Run(steam, new[] { id }, names, line => Dispatcher.Invoke(() => Log(line)), TimeSpan.FromMinutes(10), CancellationToken.None);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        if (failure != null || result == null || result.Ready.Count == 0)
        {
            Log(failure != null
                ? "Could not subscribe through Steam by itself. " + failure
                : "Steam has not finished the download yet.");
            Log("Opening the Workshop page. Subscribe there, wait for the download in Steam, then click Use Workshop Coop 0.1.6 again.");
            if (!ShellOpen.TryOpen(ShellOpen.SteamWorkshopPage(TaomStack.CoopWorkshopId), out _))
                Log(ShellOpen.SteamPageProblem);
            return null;
        }

        return CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries());
    }
}
