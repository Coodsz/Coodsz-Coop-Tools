using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;

namespace CoodszHost;

// The community mod list badge on the Modules page: Works, Broken, Client only or Unknown,
// with the community counts on hover, and under it what players reported (the green and red count
// chips, ModderLords' verdict) and a small Report link, like each mod row in the client launchers.
// With the report endpoint set, the badge and the link open the report window (tool coop, or ad1259
// in the AD1259 server tool); without it, the badge opens the GitHub form as before.
public partial class MainWindow
{
    private void WatchCommunityCompat()
    {
        // The Full mod list link beside the mods on count opens the AD1259 list in the AD1259 server tool.
        if (Edition.IsAD1259)
        {
            FullModListText.Text = "Full AD1259 mod list";
            FullModListLink.ToolTip = "Opens the AD1259 mod list on GitHub in your browser: every pack mod with its version, player reports and notes.";
        }
        _modules.CollectionChanged += (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace && e.NewItems != null)
                foreach (ModuleRow row in e.NewItems)
                {
                    row.Compat = CommunityCompat.Badge(row.Id, CommunityCompat.Current, Edition.ShowsModResults, reportTip: ModReports.ReportTip())?.Translated();
                    row.Reports = ReportChips(row);
                    // The AD1259 server tool leaves out this PC's own results.
                    row.Mine = Edition.ShowsModResults && UnknownModBadge.Applies(row.Id) ? ModResults.Pill(row.Id, row.Version)?.Translated() : null;
                }
        };
        CommunityCompat.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            var database = CommunityCompat.Current;
            foreach (var row in _modules)
            {
                row.Compat = CommunityCompat.Badge(row.Id, database, Edition.ShowsModResults, reportTip: ModReports.ReportTip())?.Translated();
                row.Reports = ReportChips(row);
            }
        });
        _ = CommunityCompat.RefreshAsync("CoodszHost");
    }

    // MODS.md in Coodsz-Coop-and-Friends, or in Coodsz-AD1259 for the AD1259 server tool.
    private void FullModList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Edition.IsAD1259 ? BannerlordCoopLauncher.Core.AD1259.AD1259Stack.ModsPageUrl : ModCompatDatabase.ModsPageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    // What players reported for a mod row and the Report link, on a line under the badge. Null for the
    // base game modules, the DedicatedServer.* host modules and Coop.
    private FrameworkElement? ReportChips(ModuleRow row)
    {
        if (!UnknownModBadge.Applies(row.Id))
            return null;
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 6, 0) };
        if (Coodsz.Ui.ModReportUi.CountChips(this, CommunityCompat.Current.Find(row.Id, null),
                CompatRecords.Chip(CompatRecords.Current.Find(row.Id), row.Version)) is { } chips)
            panel.Children.Add(chips);
        if (ModReports.HasEndpoint() && Reportable(row))
            panel.Children.Add(Coodsz.Ui.ModReportUi.RowLink(this, () => OpenReport(row)));
        return panel.Children.Count == 0 ? null : panel;
    }

    // The report window for one mod, with the server's mods in load order, the same as the clients send.
    private Coodsz.Ui.ModReportRequest ReportRequest(ModuleRow row) => new()
    {
        Tool = Edition.ReportTool,
        ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
        Coop = ModuleVersion("Coop") is { Length: > 0 } coop ? coop : ModuleVersion("CoopNightly"),
        Game = ModuleVersion("Native"),
        Rate = UnknownModBadge.Applies(row.Id) && row.Enabled && Reportable(row)
            ? new[] { new Coodsz.Ui.ModReportChoice(row.Id, row.Name is { Length: > 0 } name ? name : row.Id, row.Version) }
            : Array.Empty<Coodsz.Ui.ModReportChoice>(),
        ModList = ModReports.ModList(_modules.Where(module => module.Enabled).Select(module => (module.Id, (string?)module.Version)))
    };

    // A host report says the server ran with the mod. Not for a mod whose code the server skips (its Works would
    // only mean it loaded), and on the AD1259 server only for the AD1259 pack, like the AD1259 client.
    private bool Reportable(ModuleRow row)
    {
        if (Edition.IsAD1259 && BannerlordCoopLauncher.Core.AD1259.AD1259Stack.Find(row.Id) == null)
            return false;
        var xmlPath = System.IO.Path.Combine(ModuleCatalog.ModulesRoot(ServerRoot), row.Id, "SubModule.xml");
        try
        {
            return !System.IO.File.Exists(xmlPath) || ServerModuleNotes.SkippedCode(row.Id, System.IO.File.ReadAllText(xmlPath)) == null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private void OpenReport(ModuleRow row)
    {
        if (!row.Enabled)
        {
            Append("Turn " + (row.Name is { Length: > 0 } name ? name : row.Id) + " on and run the server with it before you report it.");
            return;
        }

        Coodsz.Ui.ModReportWindow.Show(this, ReportRequest(row));
    }

    private string ModuleVersion(string id) =>
        _modules.FirstOrDefault(module => module.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Version ?? "";

    private void CompatBadge_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModuleRow row)
            return;
        // A mod the server can rate opens the report window; the others keep the GitHub form.
        if (ModReports.HasEndpoint() && Reportable(row))
        {
            OpenReport(row);
            return;
        }

        var coop = _modules.FirstOrDefault(module => module.Id.Equals("Coop", StringComparison.OrdinalIgnoreCase))?.Version;
        var game = _modules.FirstOrDefault(module => module.Id.Equals("Native", StringComparison.OrdinalIgnoreCase))?.Version;
        try
        {
            Process.Start(new ProcessStartInfo(CommunityCompat.ReportUrl(row.Id, row.Name, row.Version, coop, game)) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Append("Could not open the browser. " + ex.Message);
        }
    }
}
