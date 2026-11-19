using System.Windows;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresLauncher;

// Reporting in plain view, the same as in Coop and Friends: Report a mod on Logs and help, Mod crashed?
// Report it under Connect, the green and red count chips next to each mod, and the report bar (the one time card, How did your mods work? after a
// game and the crash bar). Each one opens the report window, which shows what is sent first.
public partial class MainWindow
{
    private Coodsz.Ui.ModReportBar? _reportBar;

    private void SetUpModReports()
    {
        // ModderLords' records for the verdict chip: the kept copy now, the downloaded one when it arrives.
        // The count chips come from the Burning Empires list (LoadCompatList).
        CommunityCompat.Changed += () => Dispatcher.BeginInvoke(ShowModuleList);
        _ = CommunityCompat.RefreshAsync("CoodszBurningEmpiresClientLauncher");
        if (!ModReports.HasEndpoint())
            return;
        _reportBar = new Coodsz.Ui.ModReportBar(this, ReportBarHost,
            () => ReportRequest(SessionExtras(LaunchSetup), LaunchOrder(), afterGame: false, crashed: false), Log)
        {
            Tool = "be",
            CrashRequest = CrashRequest
        };
        SetUpCrashReports();
        ReportModSlot.Content = Coodsz.Ui.ModReportUi.MainButton(this, _reportBar.OpenCurrent);
        ReportHintSlot.Content = Coodsz.Ui.ModReportUi.Hint(this, _reportBar.OpenCurrent);
        CrashLinkSlot.Content = Coodsz.Ui.ModReportUi.CrashLink(this, _reportBar.OpenCrash);
        Loaded += (_, _) => _reportBar.ShowIntroOnce();
    }

    // The green and red count chips of a mod, from the reports sent with tool be that the tally counts into
    // the Burning Empires list (not the Coop and Friends list), with ModderLords' verdict chip in front when
    // his records test the mod. version is the copy the row shows (a core module is not in the extras scan). Not on the
    // Burning Empires core mods: his records test plain Coop, not the Burning Empires stack.
    private FrameworkElement? CommunityCounts(string moduleId, string? workshopId, string? name, string version) =>
        Coodsz.Ui.ModReportUi.CountChips(this, _compat.CountsFor(moduleId, workshopId, name) is { } counts
                ? new ModCompatEntry { Id = moduleId, Works = counts.Works, Broken = counts.Broken }
                : null,
            BeModChips.RecordsChip(CompatRecords.Current, moduleId, version));

    // The mods a crash log names, for the report window to mark as suggested Broken.
    private IReadOnlyList<string> CrashNamed(StartupCrashExplain.Capture capture, TaomSetup setup) =>
        StartupCrashExplain.NamedMods(capture.ReportText, capture.GameLog,
            setup.EnabledExtras.Select(mod => new StartupCrashExplain.ModName(mod.ModuleId, mod.Label)).ToList());

    // The crash bar for a run of this setup, with the named mods (or the one Find the crashing mod found) suggested Broken.
    private void ShowCrashReport(TaomSetup setup, IReadOnlyList<string> suggested, string? crashLine = null) =>
        _reportBar?.Crash(ReportRequest(SessionExtras(setup), LaunchOrder(), afterGame: false, crashed: true).AsCrash(suggested, crashLine));
}
