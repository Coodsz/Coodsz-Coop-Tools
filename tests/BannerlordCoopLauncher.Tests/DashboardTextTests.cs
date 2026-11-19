using System.Text.Json;
using BannerlordCoopLauncher.Core.Keep;

namespace BannerlordCoopLauncher.Tests;

// The wave 2 review: the big Dashboard button's labels ("Starting...", "Restarting...") and the AD1259 sidebar brand had
// no translation, since the text scan skipped words with three dots and the AD1259 branch of Edition.cs.
public sealed class DashboardTextTests
{
    private static readonly string[] Languages = { "ar", "de", "fr", "pt", "ru", "tr" };

    private static Dictionary<string, string> Table(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json")))!;

    [Fact]
    public void Every_label_of_the_big_Dashboard_button_is_translated()
    {
        var labels = Enum.GetValues<HomeState>().Select(state => HomeDashboard.Primary(state).Label).Distinct().ToList();
        Assert.Contains("Starting...", labels);
        Assert.Contains("Restarting...", labels);
        foreach (var language in Languages)
        {
            var table = Table(language);
            foreach (var label in labels)
                Assert.True(table.ContainsKey(label), label + " has no " + language + " text.");
        }
    }

    [Fact]
    public void Both_sidebar_brands_are_translated_and_the_text_scan_reads_the_AD1259_branch()
    {
        foreach (var language in Languages)
        {
            var table = Table(language);
            Assert.True(table.ContainsKey("COOP SERVER TOOL"), language);
            Assert.True(table.ContainsKey("AD1259 SERVER TOOL"), language);
        }

        var scan = SourceFiles.Read(Path.Combine("tools", "ExtractUiText", "Program.cs"));
        Assert.Contains("WithPreprocessorSymbols(\"AD1259\")", scan);
        Assert.Contains(@"(?:[.:!?]|\.\.\.)?$", scan);
    }

    [Fact]
    public void Restart_stays_on_the_Dashboard_and_the_firewall_asks_once()
    {
        var code = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.DoesNotContain("ShowPage(\"Logs\")", SourceFiles.Method(code, "private async Task<bool> RestartServerAsync("));
        var firewall = SourceFiles.Method(code, "private async void Firewall_Click(");
        Assert.Contains("if (_firewallBusy)", firewall);
        Assert.Contains("_firewallBusy = false;", firewall);
        // The big button compares the English behind its translated label, so it is not set again every second.
        Assert.Contains("UiLanguage.English(main, ContentControl.ContentProperty) != primary.Label", SourceFiles.Method(code, "private void ShowServerActions("));
    }
}
