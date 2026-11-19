using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

// Redesign wave 4 review: a failed Start no longer leaves the mod rows locked, the save mods card names the real reason it
// waits, and the order reasons on hover stay on the order line only.
public sealed class HostWave4ReviewFixTests
{
    private static string Host() => SourceFiles.Window(SourceFiles.Root(), "CoodszHost");

    [Fact]
    public void A_failed_Start_gives_the_rows_their_moves_back()
    {
        // StartServer reads the mods while _controlBusy is set, so every row came back with CanMove false. When Start then
        // throws, EndControl clears the flag and must set the rows again, past the "nothing changed" check.
        var end = SourceFiles.Method(Host(), "private void EndControl()");
        Assert.True(end.IndexOf("_controlBusy = false;", StringComparison.Ordinal) < end.IndexOf("ApplyModListBlock(force: true);", StringComparison.Ordinal));
        var apply = SourceFiles.Method(Host(), "private void ApplyModListBlock(bool force = false)");
        Assert.Contains("if (!force && _modListBlockShown == (running, locked))", apply);
        Assert.Contains("row.CanMove = !running && !locked;", apply);
    }

    [Fact]
    public void The_save_mods_card_says_the_server_runs_only_when_it_does()
    {
        var fix = SourceFiles.Method(Host(), "private void FixSaveMods(SaveModCard card)");
        Assert.Contains("ServerHoldsModFiles || CrashRestartWaits || _restartingNow || _restartingAfterCrash ? ModListBlock.RunningLine : ModListBlock.BusyLine", fix);
        Assert.DoesNotContain("server", ModListBlock.BusyLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_save_hint_line_clears_the_order_hover()
    {
        var source = Host();
        Assert.DoesNotContain("ModuleSaveHint.Text =", source.Replace(SourceFiles.Method(source, "private void SetModuleSaveHint(string text)"), ""));
        var set = SourceFiles.Method(source, "private void SetModuleSaveHint(string text)");
        Assert.True(set.IndexOf("ModuleSaveHint.ToolTip = null;", StringComparison.Ordinal) < set.IndexOf("ModuleSaveHint.Text = text;", StringComparison.Ordinal));
    }

    [Fact]
    public void The_page_My_reports_button_has_its_own_ids()
    {
        var replies = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        var add = SourceFiles.Method(replies, "public static void AddMyReports(");
        Assert.Contains("\"MyReportsButtonPage\"", add);
        Assert.Contains("\"MyReportsGlowPage\"", add);
    }
}
