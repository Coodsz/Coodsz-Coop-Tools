namespace BannerlordCoopLauncher.Tests;

// A player clicked Prepare server on the Saves page (Workshop Coop not subscribed) and saw nothing: the reason went only
// to the note under Start server on the Dashboard. Each of these Server Tools shows that note on the Saves page too.
public sealed class ServerPageStatusTests
{
    public static TheoryData<string> Tools => new() { "RealmOfThronesHost", "TaomCoopHost", "BurningEmpiresHost" };

    [Theory]
    [MemberData(nameof(Tools))]
    public void The_saves_page_shows_the_note_above_prepare_server(string project)
    {
        var xaml = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
        var page = xaml.IndexOf("x:Name=\"ServerPage\"", StringComparison.Ordinal);
        var card = xaml.IndexOf("x:Name=\"ServerPageStatusCard\"", StringComparison.Ordinal);
        var prepare = xaml.IndexOf("Click=\"Prepare_Click\"", StringComparison.Ordinal);
        Assert.True(page >= 0 && page < card && card < prepare, project + ": the note card sits on the Saves page above Prepare server");
        Assert.Contains("x:Name=\"ServerPageStatus\"", xaml);
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void Every_note_and_the_ready_swap_reach_the_saves_page(string project)
    {
        var control = SourceFiles.Read(Path.Combine("src", project, "MainWindow.ServerControl.cs"));
        Assert.Contains("ShowOnServerPage(message, failed);", SourceFiles.Method(control, "private void SetControl(string message, bool failed = false)"));
        // Only a problem or the first note scrolls the page, so Prepare's progress lines do not drag it.
        Assert.Contains("(failed || first) && ServerPage.Visibility == Visibility.Visible", SourceFiles.Method(control, "private void ShowOnServerPage(string message, bool failed)"));
        // The Server is ready swap writes ControlStatus itself, so it must update the Saves page note too (it kept saying Server is starting).
        var state = SourceFiles.Read(Path.Combine("src", project, "MainWindow.State.cs"));
        Assert.Contains("ShowOnServerPage(ServerReadyNote.Ready, failed: false);", state);
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void The_blocked_dll_note_opens_the_page_with_unblock_dlls(string project)
    {
        var log = SourceFiles.Read(Path.Combine("src", project, "MainWindow.ServerLog.cs"));
        Assert.Contains("ShowPage(\"Settings\");", SourceFiles.Method(log, "private void ShowBlockedDllFix()"));
        var xaml = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
        var settings = xaml.IndexOf("x:Name=\"SettingsPage\"", StringComparison.Ordinal);
        var unblock = xaml.IndexOf("x:Name=\"UnblockButton\"", StringComparison.Ordinal);
        var next = xaml.IndexOf("x:Name=\"ModulesPage\"", StringComparison.Ordinal);
        Assert.True(settings >= 0 && settings < unblock && unblock < next, project + ": Unblock DLLs is on the Settings page");
    }
}
