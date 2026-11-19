using System.Xml.Linq;
using Coodsz.Ui;

namespace BannerlordCoopLauncher.Tests;

// Fixes from the v1.3.7 release review.
public class V137ReviewFixesTests
{
    // The first load crash hides the Send server crash report bar because a retry follows. A retry that never ran
    // (another action was busy, Start server or Stop auto restart in the wait, a start check refused it) left no
    // retry and no bar.
    [Theory]
    [InlineData("TaomCoopHost", "_retryCrashProcess = process;", "if (_process == null || ReferenceEquals(_process, crashed))")]
    [InlineData("BurningEmpiresHost", "_retryCrashProcess = process;", "if (_process == null || ReferenceEquals(_process, crashed))")]
    [InlineData("CoodszHost", "_retryCrash = (exit, crashLine);", "_actions.Note(\"Save fixes\", \"Start after the load crash failed: \" + ex.Message);")]
    public void A_load_retry_that_does_not_run_shows_the_crash_bar_it_held_back(string project, string held, string failed)
    {
        var source = SourceFiles.Window(SourceFiles.Root(), project).Replace("\r\n", "\n");
        var scheduled = project == "CoodszHost"
            ? SourceFiles.Method(source, "private bool RetryAfterLoadCrash(")
            : SourceFiles.Method(source, "private void OnRescueExit(");
        Assert.True(scheduled.IndexOf(held, StringComparison.Ordinal) is > 0 and var at
            && at < scheduled.IndexOf("timer.Start();", StringComparison.Ordinal), project);

        var retry = SourceFiles.Method(source, "private async Task StartAfterLoadCrashAsync()");
        // Busy: the held bar shows before the early return.
        var busy = retry.IndexOf("OfferHeldCrashReport(crashed);\n            return;", StringComparison.Ordinal);
        Assert.True(busy > 0, project);
        // A start that did not get a server running shows it too.
        var refused = retry.IndexOf(failed, StringComparison.Ordinal);
        Assert.True(refused > busy && retry.IndexOf("OfferHeldCrashReport(crashed);", refused, StringComparison.Ordinal) > refused, project);

        // A waiting retry that is cancelled shows it as well.
        var cancel = SourceFiles.Method(source, "private void CancelRescueRetry()");
        Assert.Contains("var waiting = _rescueRetryTimer != null;", cancel);
        Assert.Contains("if (waiting)\n            OfferHeldCrashReport(crashed);", cancel);
        Assert.Contains("OfferServerCrashReport(", SourceFiles.Method(source, "private void OfferHeldCrashReport("));
    }

    // What's new says one notice shows at a time; the Coop and AD1259 Server Tool showed two.
    [Fact]
    public void The_Coop_and_AD1259_Server_Tool_show_one_notice_at_a_time()
    {
        var xaml = XDocument.Parse(SourceFiles.Read("src/CoodszHost/MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var banners = xaml.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "Banners");
        Assert.Equal("1", (string?)banners.Attribute("MaxShown"));
        // Three notices up: the most urgent shows, two fold under N more notices, and all open when expanded.
        var up = new List<(bool, int)> { (true, 4), (true, 1), (false, 0), (true, 2) };
        var (shown, hidden) = FrameRules.Banners(up, max: 1);
        Assert.Equal(new[] { 1 }, shown);
        Assert.Equal(2, hidden);
        Assert.Equal(3, FrameRules.Banners(up, max: 1, expanded: true).Shown.Count);
    }

    // The client's "12 on" chip stayed English: a template needs 4 letters besides its numbers, and "{0} on" has 2.
    // The French Missing on Workshop chip was wider than the 150 px chip on the Play page.
    [Theory]
    [InlineData("ar")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("tr")]
    public void The_mods_on_chip_is_translated_and_the_French_workshop_chip_is_short(string language)
    {
        var entries = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language);
        Assert.Contains("{0}", entries["{0} mods on"]);
        Assert.Contains("{0}", entries["{0} mods on, {1} need attention"]);
        Assert.Contains("{1}", entries["{0} mods on, {1} need attention"]);
        Assert.False(entries.ContainsKey("{0} on"));
        if (language == "fr")
            Assert.Equal("Manquant (Workshop)", entries["Missing on Workshop"]);
        var status = SourceFiles.Read("src/BannerlordCoopLauncher/MainWindow.Status.cs");
        Assert.Contains("enabled.Count + \" mods on\" : enabled.Count + \" mods on, \" + attention + \" need attention\"", status);
    }
}
