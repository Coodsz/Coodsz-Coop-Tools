using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

// Coodsz: the TAOM client showed the report chips and the Report link only for extra mods, while Coop and Friends
// shows them on every mod row. The TAOM core rows now get them too; the base game modules and Coop do not.
public sealed class TaomModReportChipsTests
{
    [Fact]
    public void Taom_core_rows_carry_the_chips_and_the_report_link()
    {
        var window = SourceFiles.Read(Path.Combine("src", "TaomCoopLauncher", "MainWindow.xaml.cs"));
        var coreRow = window.IndexOf("private Border CoreRow(int number, string id)", StringComparison.Ordinal);
        Assert.True(coreRow > 0);
        Assert.True(window.IndexOf("PackModChips(id,", coreRow, StringComparison.Ordinal) > coreRow, "CoreRow adds the chips column.");

        var checks = SourceFiles.Read(Path.Combine("src", "TaomCoopLauncher", "MainWindow.Checks.cs"));
        var chips = checks.IndexOf("private FrameworkElement? PackModChips(", StringComparison.Ordinal);
        Assert.True(chips > 0);
        var body = checks[chips..checks.IndexOf("// The report window for these mods", chips, StringComparison.Ordinal)];
        Assert.Contains("CommunityCounts(moduleId, null)", body);
        Assert.Contains("ModReportUi.RowLink(", body);
        Assert.Contains("ModResults.Pill(", body);
        // The core row's Report rates that core mod; the request filter lets it through (a review found the
        // window opened with nothing to rate before).
        Assert.Contains("crashed: false, core: true)", body);
        Assert.Contains("TaomModResults.IsExtraMod(mod.Id) || (core && BannerlordCoopLauncher.Core.Modules.UnknownModBadge.Applies(mod.Id))", checks);

        // ModderLords' records test plain Coop: no "Tested" chip on the TAOM core rows (live test showed
        // "Tested: broken" on TAOM.CoopCompat), in the client and the TAOM server tool.
        var clientReports = SourceFiles.Read(Path.Combine("src", "TaomCoopLauncher", "MainWindow.Reports.cs"));
        Assert.Contains("TaomModResults.IsExtraMod(moduleId) ? CompatRecords.Chip(", clientReports);
        var hostReports = SourceFiles.Read(Path.Combine("src", "TaomCoopHost", "MainWindow.Reports.cs"));
        Assert.Contains("TaomModResults.IsExtraMod(moduleId) ? CompatRecords.Chip(", hostReports);
        Assert.False(TaomCoop.Core.TaomModResults.IsExtraMod("TAOM.CoopCompat"));
    }

    [Theory]
    [InlineData("TAOM", true)]
    [InlineData("Bannerlord.Harmony", true)]
    [InlineData("Native", false)]
    [InlineData("SandBoxCore", false)]
    [InlineData("Coop", false)]
    public void Only_real_mods_get_chips(string id, bool chips)
    {
        Assert.Equal(chips, UnknownModBadge.Applies(id));
    }
}
