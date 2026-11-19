namespace BannerlordCoopLauncher.Tests;

// The wave 6 review of the TAOM and Burning Empires Server Tools: Start, the restarts, the header setup picker and the
// setup buttons wait for a running action, the running block of the Mods page counts _busy, and the page names in the
// guides and the status lines are the new ones (Dashboard, Saves, Mods, Settings).
public sealed class TaomBeWave6ReviewTests
{
    public static TheoryData<string> Tools => new() { "TaomCoopHost", "BurningEmpiresHost" };

    private static string Window(string project) => SourceFiles.Window(SourceFiles.Root(), project);

    private static void Before(string method, string first, string second) =>
        Assert.True(method.IndexOf(first, StringComparison.Ordinal) is >= 0 and var at && at < method.IndexOf(second, StringComparison.Ordinal),
            first + " should come before " + second);

    [Theory]
    [MemberData(nameof(Tools))]
    public void Start_waits_for_a_running_action_before_it_asks_anything(string project)
    {
        var start = SourceFiles.Method(Window(project), "private async void Start_Click(");
        Before(start, "if (_busy || _startingServer)", "ConfirmHostModChanges()");
        Assert.Contains("Wait for the current action to finish.", start);

        // The restarts call Start_Click only after their own _busy is cleared, or the new check would turn them down.
        foreach (var signature in new[] { "private async Task SaveThenStop(", project == "TaomCoopHost" ? "private void RestartLastGood_Click(" : "private async void RestartLastGood_Click(" })
        {
            var method = SourceFiles.Method(Window(project), signature);
            Before(method, "_busy = false;", "Start_Click(this, new RoutedEventArgs());");
        }

        // A failed start opens the Mods page again right away.
        var startAsync = SourceFiles.Method(Window(project), "private async Task StartServerAsync(");
        Before(startAsync, "_startingServer = false;", "ApplyModListBlock();");
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void The_setup_picker_and_the_setup_buttons_refuse_while_starting_or_busy(string project)
    {
        var window = Window(project);
        var refuse = SourceFiles.Method(window, "private bool RefuseSetupChangeWhileBusy(");
        Assert.Contains("if (!_busy && !_startingServer)", refuse);
        Assert.Contains("ModListBlock.BusyLine", refuse);

        // A refused pick puts the box back on the active setup; a running server alone still lets the pick through.
        var pick = SourceFiles.Method(window, "private void ModSetupBox_SelectionChanged(");
        Before(pick, "RefuseSetupChangeWhileBusy()", "ConfirmHostModChanges()");
        Before(pick, "RefuseSetupChangeWhileBusy()", "ShowModSetups();");
        Assert.DoesNotContain("_serverRunning", pick);
        foreach (var signature in new[] { "private void NewHostSetup_Click(", "private void CopyHostSetup_Click(", "private void RenameHostSetup_Click(", "private void DeleteModSetup_Click(", "private void SaveHostSetupName()" })
            Assert.Contains("RefuseSetupChangeWhileBusy()", SourceFiles.Method(window, signature));
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void The_mods_page_block_counts_a_busy_tool_and_says_which_reason(string project)
    {
        var window = Window(project);
        Assert.Contains("ModListBlock.Running(_serverRunning, modTestRunning: false, restartPending: _startingServer || _busy)", window);
        Assert.Contains("_serverRunning || _startingServer ? ModListBlock.RunningLine : ModListBlock.BusyLine", window);
        Assert.Contains("ModsRunningBanner.Title = ModListBlockLine;", SourceFiles.Method(window, "private void ApplyModListBlock("));
    }

    [Fact]
    public void Burning_empires_stop_and_restart_check_busy_before_the_question()
    {
        var window = Window("BurningEmpiresHost");
        foreach (var signature in new[] { "private void Stop_Click(", "private void Restart_Click(" })
            Before(SourceFiles.Method(window, signature), "if (_busy || _startingServer)", "ConfirmWindow.Ask(");
    }

    [Theory]
    [InlineData("TaomServer", "TaomCoopHost")]
    [InlineData("BeServer", "BurningEmpiresHost")]
    public void The_guides_and_status_lines_name_the_new_pages(string guides, string project)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(SourceFiles.Root(), "src", "BannerlordCoopLauncher.Core", "Keep", "Guides", guides), "*.json"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Modules page", text);
            Assert.DoesNotContain("Server page", text);
        }
        var code = Window(project);
        Assert.DoesNotContain("on the Modules page", code);
        Assert.Contains("Restore save backup on the Saves page puts it back.", code);
        Assert.DoesNotContain("Modules page", SourceFiles.Read(Path.Combine("src", "BurningEmpires.Core", "BeRescueText.cs")));
        Assert.Contains("Click Unblock DLLs on the Settings page", SourceFiles.Read(Path.Combine("src", "BurningEmpires.Core", "BeHostReport.cs")));
    }
}
