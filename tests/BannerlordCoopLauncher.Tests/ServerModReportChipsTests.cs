using System.ComponentModel;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Coodsz: the server tools get the same mod report chips as the client launchers. Each mod row on the
// Modules page of Coodsz Host (and so the AD1259 server tool) and of the TAOM server tool shows what
// players reported and a small Report link; the base game modules and Coop do not.
public sealed class ServerModReportChipsTests
{
    [Fact]
    public void Coodsz_host_rows_carry_the_chips_and_the_report_link()
    {
        var compat = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.CommunityCompat.cs"));
        var chips = compat.IndexOf("private FrameworkElement? ReportChips(ModuleRow row)", StringComparison.Ordinal);
        Assert.True(chips > 0);
        var body = compat[chips..compat.IndexOf("private Coodsz.Ui.ModReportRequest ReportRequest(", chips, StringComparison.Ordinal)];
        Assert.Contains("UnknownModBadge.Applies(row.Id)", body);
        Assert.Contains("ModReportUi.CountChips(", body);
        Assert.Contains("CompatRecords.Chip(", body);
        Assert.Contains("ModReportUi.RowLink(", body);

        // Set for every new row and again when the community list arrives.
        Assert.Equal(2, CountOf(compat, "row.Reports = ReportChips(row);"));
        Assert.Contains("_ = CommunityCompat.RefreshAsync(\"CoodszHost\");", compat);

        var request = compat[compat.IndexOf("private Coodsz.Ui.ModReportRequest ReportRequest(", StringComparison.Ordinal)..];
        Assert.Contains("Tool = Edition.ReportTool", request);
        Assert.Contains("ToolVersion = typeof(MainWindow).Assembly", request);
        Assert.Contains("ModList = ModReports.ModList(_modules.Where(module => module.Enabled)", request);

        var xaml = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml"));
        Assert.Contains("<ContentControl Content=\"{Binding Reports}\"", xaml);
    }

    [Fact]
    public void The_ad1259_server_tool_builds_the_same_rows()
    {
        var project = SourceFiles.Read(Path.Combine("src", "AD1259Host", "AD1259Host.csproj"));
        Assert.Contains("<HostSource>..\\CoodszHost\\</HostSource>", project);
        Assert.Contains("<Compile Include=\"$(HostSource)**\\*.cs\"", project);
        Assert.Contains("ModReportWindow.cs", project);
        Assert.Contains("IsAD1259 ? \"ad1259\" : \"coop\"", SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "Edition.cs")));
    }

    [Fact]
    public void Coodsz_host_reports_as_coop()
    {
        // The test project builds Edition.cs without AD1259 defined, like Coodsz Host.
        Assert.Equal("coop", Edition.ReportTool);
    }

    [Fact]
    public void A_module_row_tells_the_list_when_its_chips_change()
    {
        var row = new ModuleRow { Id = "Bannerlord.Harmony" };
        var changed = new List<string?>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var chips = new object();
        row.Reports = chips;
        row.Reports = chips;
        row.Reports = null;
        Assert.Equal(new[] { nameof(ModuleRow.Reports), nameof(ModuleRow.Reports) }, changed);
    }

    [Fact]
    public void Taom_server_tool_rows_carry_the_chips_and_the_report_link()
    {
        var setups = SourceFiles.Read(Path.Combine("src", "TaomCoopHost", "MainWindow.ModSetups.cs"));
        // The extra mod rows and the TAOM core rows.
        Assert.Contains("Reports = ReportChips(extra.ModuleId, extra.WorkshopId, extra.Label, version, loadOrder)", setups);
        Assert.Contains("Reports = ReportChips(id, null, id, coreVersion, loadOrder)", setups);

        var reports = SourceFiles.Read(Path.Combine("src", "TaomCoopHost", "MainWindow.Reports.cs"));
        Assert.Contains("UnknownModBadge.Applies(moduleId)", reports);
        Assert.Contains("ModReportUi.CountChips(", reports);
        Assert.Contains("ModReportUi.RowLink(", reports);
        Assert.Contains("Tool = \"taom\"", reports);
        Assert.Contains("ModList = ModReports.ModList(loadOrder)", reports);
        Assert.Contains("CommunityCompat.RefreshAsync(", reports);

        Assert.Contains("SetUpModReports();", SourceFiles.Read(Path.Combine("src", "TaomCoopHost", "MainWindow.xaml.cs")));
        Assert.Contains("<ContentControl Content=\"{Binding Reports}\"", SourceFiles.Read(Path.Combine("src", "TaomCoopHost", "MainWindow.xaml")));
    }

    // The TAOM client's ReportRequest kept the extra mods only, so the Report link on a TAOM core row
    // opened nothing (ModReportWindow.Show refuses an empty list). A core row's link now asks for core mods too.
    [Fact]
    public void Taom_client_report_link_of_a_core_row_has_a_mod_to_rate()
    {
        Assert.False(TaomModResults.IsExtraMod("TAOM"));
        Assert.True(UnknownModBadge.Applies("TAOM"));

        var checks = SourceFiles.Read(Path.Combine("src", "TaomCoopLauncher", "MainWindow.Checks.cs"));
        var request = checks.IndexOf("private Coodsz.Ui.ModReportRequest ReportRequest(", StringComparison.Ordinal);
        Assert.True(request > 0);
        var body = checks[request..checks.IndexOf("ModList =", request, StringComparison.Ordinal)];
        Assert.Contains("TaomModResults.IsExtraMod(mod.Id) || (core && BannerlordCoopLauncher.Core.Modules.UnknownModBadge.Applies(mod.Id))", body);
        Assert.Contains("crashed: false, core: true)", checks);
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
