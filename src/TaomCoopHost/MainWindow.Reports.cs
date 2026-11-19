using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace TaomCoopHost;

// The mod reports on the Mods page, the same as each mod row of the TAOM client: what players
// reported (the green and red count chips, ModderLords' verdict) and a small Report link that opens
// the report window with the server's mods in load order. Reports carry tool taom.
public partial class MainWindow
{
    private void SetUpModReports()
    {
        // The community counts for the chips: the kept copy now, the downloaded one when it arrives.
        CommunityCompat.Changed += () => Dispatcher.BeginInvoke(ShowModSetups);
        _ = CommunityCompat.RefreshAsync("CoodszTaomServerTool");
    }

    // The chips and the Report link of a mod row, or null for the base game modules and Coop.
    private FrameworkElement? ReportChips(string moduleId, string? workshopId, string name, string version, IReadOnlyList<(string Id, string? Version)> loadOrder)
    {
        if (!UnknownModBadge.Applies(moduleId))
            return null;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        // ModderLords' records test plain Coop, so the TAOM core mods get no "Tested" chip, only player reports.
        if (Coodsz.Ui.ModReportUi.CountChips(this, CommunityCompat.Current.Find(moduleId, workshopId),
                TaomModResults.IsExtraMod(moduleId) ? CompatRecords.Chip(CompatRecords.Current.Find(moduleId), version) : null) is { } chips)
            panel.Children.Add(chips);
        if (ModReports.HasEndpoint() && !ServerSkipsCode(moduleId))
            panel.Children.Add(Coodsz.Ui.ModReportUi.RowLink(this, () => Coodsz.Ui.ModReportWindow.Show(this,
                ReportRequest(new Coodsz.Ui.ModReportChoice(moduleId, name, version), loadOrder))));
        return panel.Children.Count == 0 ? null : panel;
    }

    // The report window for one mod, with the mods the server loads in order.
    private static Coodsz.Ui.ModReportRequest ReportRequest(Coodsz.Ui.ModReportChoice mod, IEnumerable<(string Id, string? Version)> loadOrder) => new()
    {
        Tool = "taom",
        ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
        Coop = TaomStack.CoopVersion,
        Game = TaomStack.GameVersion,
        Rate = new[] { mod },
        ModList = ModReports.ModList(loadOrder)
    };

    // A host report says the server ran with the mod; for a mod whose code the server skips that would only
    // mean it loaded, so those rows get the chips but no Report link.
    private bool ServerSkipsCode(string moduleId)
    {
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

    // The version of a mod on this PC, or empty when the scan does not have it.
    private string HostModVersion(string moduleId) =>
        ManifestVersion(HostMods().FirstOrDefault(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))?.Directory);
}
