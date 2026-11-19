using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Modules;
using BurningEmpires.Core;

namespace BurningEmpiresHost;

// The mod reports on the mods page, the same as the TAOM server tool and each mod row of the Burning Empires
// client: what players reported (ModderLords' verdict, the green and red count chips from the Burning Empires
// list) and a small Report link that opens the report window with the server's mods in load order. Reports
// carry tool be.
public partial class MainWindow
{
    // Each mod's version for this redraw of the mods page, and where Start server copies the core from.
    private readonly Dictionary<string, string> _rowVersions = new(StringComparer.OrdinalIgnoreCase);
    private BeCoreSources? _rowSources;

    private void SetUpModReports()
    {
        // ModderLords' records for the verdict chip: the kept copy now, the downloaded one when it arrives.
        // The count chips come from the Burning Empires list (LoadCompatList).
        CommunityCompat.Changed += () => Dispatcher.BeginInvoke(ShowModSetups);
        _ = CommunityCompat.RefreshAsync("CoodszBurningEmpiresServerTool");
    }

    // The chips and the Report link of a mod row, or null for the official modules and Coop.
    private FrameworkElement? ReportChips(string moduleId, string? workshopId, string name, string version, IReadOnlyList<(string Id, string? Version)> loadOrder)
    {
        if (!ModReports.Rates(moduleId))
            return null;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        // ModderLords' records test plain Coop, so the Burning Empires core mods get no "Tested" chip, only player reports.
        if (Coodsz.Ui.ModReportUi.CountChips(this, _compat.CountsFor(moduleId, workshopId, name) is { } counts
                    ? new ModCompatEntry { Id = moduleId, Works = counts.Works, Broken = counts.Broken }
                    : null,
                BeModChips.RecordsChip(CompatRecords.Current, moduleId, version)) is { } chips)
            panel.Children.Add(chips);
        if (ModReports.HasEndpoint() && !ServerSkipsCode(moduleId, loadOrder))
            panel.Children.Add(Coodsz.Ui.ModReportUi.RowLink(this, () => Coodsz.Ui.ModReportWindow.Show(this,
                ReportRequest(BeModChips.RowReport(moduleId, name, version, loadOrder)))));
        return panel.Children.Count == 0 ? null : panel;
    }

    // A host report says the server ran with the mod; for a mod whose code the server skips that would only
    // mean it loaded, so those rows get the chips but no Report link. The core always runs on the server, and
    // so do the mods CoopModPatch turns on there (BeServerPath.RunsForCoopModPatch).
    private bool ServerSkipsCode(string moduleId, IReadOnlyList<(string Id, string? Version)> loadOrder)
    {
        if (BeServerPath.IsCore(moduleId) || BeServerPath.RunsForCoopModPatch(loadOrder.Select(mod => mod.Id).ToList(), moduleId))
            return false;
        var directory = HostMods().FirstOrDefault(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))?.Directory;
        var xmlPath = directory == null ? null : System.IO.Path.Combine(directory, "SubModule.xml");
        try
        {
            return xmlPath != null && System.IO.File.Exists(xmlPath) && ServerModuleNotes.SkippedCode(moduleId, System.IO.File.ReadAllText(xmlPath)) != null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The report window for one mod, with the mods the server loads in order.
    private Coodsz.Ui.ModReportRequest ReportRequest(BeRowReport report) => new()
    {
        Tool = report.Tool,
        ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
        Coop = BeServerPrepare.ReportCoopVersion(_settings.ServerRoot),
        Game = BeStack.GameVersion,
        Rate = new[] { new Coodsz.Ui.ModReportChoice(report.Id, report.Name, report.Version) },
        ModList = ModReports.ModList(report.ServerOrder)
    };

    // The version of a module for this redraw: the folder Start server copies a core module from (the mod scan
    // leaves the BUTR modules and the shipped compat module out), else the mod's copy on this PC. Read once.
    private string RowVersion(string moduleId)
    {
        if (_rowVersions.TryGetValue(moduleId, out var known))
            return known;
        _rowSources ??= CoreSources();
        var folder = BeModChips.CoreFolder(moduleId, _rowSources)
            ?? HostMods().FirstOrDefault(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))?.Directory;
        var version = ManifestVersion(folder);
        _rowVersions[moduleId] = version;
        return version;
    }
}
