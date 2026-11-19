using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;

namespace AD1259Launcher;

// Reporting in plain view, the same as in Coop and Friends and the TAOM client: Report a mod on
// Logs and help, Mod crashed? Report it under Connect, the green and red count chips next to each pack
// mod, and the report bar (the one time card, How did your mods work? after a game and the crash bar). AD1259 is a fixed pack, so a report mostly says
// which pack mod crashed. Reports carry tool ad1259; each one opens the report window, which
// shows what is sent first.
public partial class MainWindow
{
    private Coodsz.Ui.ModReportBar? _reportBar;

    private void SetUpModReports()
    {
        // The community counts for the chips: the kept copy now, the downloaded one when it arrives.
        CommunityCompat.Changed += () => Dispatcher.BeginInvoke(RefreshModules);
        _ = CommunityCompat.RefreshAsync("CoodszAD1259ClientLauncher");
        if (!ModReports.HasEndpoint())
            return;
        _reportBar = new Coodsz.Ui.ModReportBar(this, ReportBarHost, () => ReportRequest(afterGame: false, crashed: false), Log)
        {
            Tool = "ad1259",
            CrashRequest = CrashRequest
        };
        SetUpCrashReports();
        ReportModSlot.Content = Coodsz.Ui.ModReportUi.MainButton(this, _reportBar.OpenCurrent);
        ReportHintSlot.Content = Coodsz.Ui.ModReportUi.Hint(this, _reportBar.OpenCurrent);
        CrashLinkSlot.Content = Coodsz.Ui.ModReportUi.CrashLink(this, _reportBar.OpenCrash);
        Loaded += (_, _) => _reportBar.ShowIntroOnce();
    }

    /// <summary>The pack mods a player can rate: every AD1259 mod except the base game and Coop itself.</summary>
    internal static IReadOnlyList<AD1259Mod> RatedMods() =>
        AD1259Stack.ClientMods.Where(mod => ModReports.Rates(mod.Id, mod.BaseGame)).ToList();

    // The report window for the AD1259 pack, with the pack in its load order.
    private Coodsz.Ui.ModReportRequest ReportRequest(bool afterGame, bool crashed) => new()
    {
        Tool = "ad1259",
        ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
        Coop = AD1259Stack.Find("Coop") is { } coop ? InstalledVersion(coop) : "",
        Game = AD1259Stack.GameVersion,
        Rate = RatedMods().Select(mod => new Coodsz.Ui.ModReportChoice(mod.Id, mod.Name, InstalledVersion(mod))).ToList(),
        ModList = ModReports.ModList(AD1259Stack.ClientMods.Select(mod => (mod.Id, (string?)InstalledVersion(mod)))),
        AfterGame = afterGame,
        Crashed = crashed
    };

    // The version found on this PC, or the pack version when the scan has none.
    private string InstalledVersion(AD1259Mod mod) =>
        _mods.FirstOrDefault(status => status.Mod.Id == mod.Id)?.FoundVersion is { Length: > 0 } found ? found : mod.Version;

    // The green and red count chips of a pack mod, from the community list every Coodsz tool shares, with
    // ModderLords' verdict chip in front when his records test the mod.
    private FrameworkElement? CommunityCounts(AD1259Mod mod) =>
        ModReports.Rates(mod.Id, mod.BaseGame)
            ? Coodsz.Ui.ModReportUi.CountChips(this, CommunityCompat.Current.Find(mod.Id, mod.WorkshopId),
                CompatRecords.Chip(CompatRecords.Current.Find(mod.Id), InstalledVersion(mod)))
            : null;

    // The pack mods a crash log names, for the report window to mark as suggested Broken.
    private static IReadOnlyList<string> CrashNamed(StartupCrashExplain.Capture capture) =>
        StartupCrashExplain.NamedMods(capture.ReportText, capture.GameLog,
            RatedMods().Select(mod => new StartupCrashExplain.ModName(mod.Id, mod.Name)).ToList());

    // The game closed before it joined and the crash log says it crashed: the crash bar.
    private void ShowCrashReport(StartupCrashExplain.Capture capture) =>
        _reportBar?.Crash(ReportRequest(afterGame: false, crashed: true).AsCrash(CrashNamed(capture), CrashLine(capture)));

    // After a join: when the game closes, the report bar offers to report the pack mods. The window
    // when the player turned it on, the crash bar after a crash, or How did your mods work? once per run.
    private async Task WatchAfterGameAsync(Process started)
    {
        try
        {
            await started.WaitForExitAsync();
            int? exitCode = null;
            try { exitCode = started.ExitCode; }
            catch (InvalidOperationException) { }
            await Task.Delay(1500);
            var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
            var capture = await Task.Run(() => StartupCrashExplain.Read(documents, started.Id));
            var closedNormally = ModReports.ClosedNormally(capture.ExitCode ?? exitCode, capture.ReportText);
            _ = Dispatcher.BeginInvoke(() =>
            {
                var request = ReportRequest(afterGame: true, crashed: !closedNormally);
                if (!closedNormally)
                    request = request.AsCrash(CrashNamed(capture), CrashLine(capture));
                _reportBar?.AfterGame(request, crashed: !closedNormally);
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
