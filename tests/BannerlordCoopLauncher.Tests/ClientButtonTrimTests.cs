using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The client launchers keep a few everyday buttons in view and move the rest into
// More menus, so a new player is not faced with twenty buttons.
public sealed class ClientButtonTrimTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void The_taom_mod_row_shows_two_main_buttons_and_a_more_menu()
    {
        var xaml = Xaml("TaomCoopLauncher");
        var row = Button(xaml, "AddModsButton").Parent!;
        var visible = row.Elements(Wpf + "Button").Select(button => (string?)button.Attribute("Content")).ToList();
        Assert.Equal(new[] { "Add mods", "Install mods", "Save mods", "More ▾" }, visible);
        // One accent button; Save mods shows up only while a change waits to be saved.
        Assert.Equal(new[] { "AddModsButton" }, AccentButtons(row, "{StaticResource Primary}"));
        Assert.Equal("Collapsed", (string?)Button(xaml, "SaveModsButton").Attribute("Visibility"));
        Assert.Contains("SaveModsButton.Visibility = ModuleSaveHint.Visibility;", SourceFiles.Window(RepoRoot(), "TaomCoopLauncher"));
        Assert.Equal(new[] { "ModList_Click", "SaveMods_Click", "AutoSort_Click", "ShareList_Click", "ImportList_Click", "UndoDependencyFix_Click" }, MenuClicks(Button(xaml, "ModsMoreButton")));
    }

    [Fact]
    public void The_coop_your_mods_card_is_calm()
    {
        var xaml = Xaml("BannerlordCoopLauncher");
        var row = xaml.Descendants().Single(element => (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == "ModuleButtonRow");
        var visible = row.Elements(Wpf + "Button").Select(button => (string?)button.Attribute("Content")).ToList();
        Assert.Equal(new[] { "Add mods", "Check mods", "Save mods", "More ▾" }, visible);
        Assert.Equal(new[] { "AddModsButton" }, AccentButtons(row, "{StaticResource Keep.BtnPrimary}"));
        // No button of its own colour any more: every other one is the plain Keep ghost button.
        Assert.All(row.Elements(Wpf + "Button").Where(button => (string?)button.Attribute("Content") != "Add mods"),
            button => Assert.Equal("{StaticResource Keep.BtnGhost}", (string?)button.Attribute("Style")));
        Assert.DoesNotContain(row.Descendants(Wpf + "Button"), button => button.Attribute("Background") != null);
        Assert.Equal("Collapsed", (string?)Button(xaml, "SaveModsButton").Attribute("Visibility"));
        // Auto dependency order moved into More with the Mods page (redesign wave 5).
        Assert.Equal(new[] { "ShareList_Click", "OpenSharedList_Click", "SaveMods_Click", "AutoOrder_Click", "ModuleFolder_Click" }, MenuClicks(Button(xaml, "ModsMoreButton")));

        var text = File.ReadAllText(XamlPath("BannerlordCoopLauncher"));
        Assert.DoesNotContain("ModuleSummaryBadge", text);
        Assert.DoesNotContain("Text=\"mods on\"", text);
        var status = SourceFiles.Window(RepoRoot(), "BannerlordCoopLauncher");
        // The count is a chip like the Server Tool's: 12 mods on, amber when mods need attention.
        Assert.Contains("enabled.Count + \" mods on\"", status);
        Assert.Contains("ModuleSummaryText.Kind = ok ? Coodsz.Ui.ChipKind.Ok : Coodsz.Ui.ChipKind.Warn;", status);
        Assert.Contains("<ui:Chip x:Name=\"ModuleSummaryText\"", text);
    }

    [Theory]
    [InlineData("BannerlordCoopLauncher")]
    [InlineData("TaomCoopLauncher")]
    [InlineData("AD1259Launcher")]
    [InlineData("BurningEmpiresLauncher")]
    public void The_mods_card_has_no_second_report_button(string project)
    {
        // The header's Report a mod does the same, and the count and verdict chips stay on the rows.
        Assert.DoesNotContain("ModsReportSlot", File.ReadAllText(XamlPath(project)));
        Assert.DoesNotContain("ModsPageRow", SourceFiles.Window(RepoRoot(), project));
        Assert.Contains("x:Name=\"ReportModSlot\"", File.ReadAllText(XamlPath(project)));
    }

    private static XElement Page(XDocument xaml, string name) =>
        xaml.Descendants().Single(element => (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == name);

    private static IReadOnlyList<string?> AccentButtons(XElement row, string style) =>
        row.Elements(Wpf + "Button").Where(button => (string?)button.Attribute("Style") == style)
            .Select(button => (string?)button.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")).ToList();

    [Fact]
    public void The_taom_tools_are_plain_buttons_on_settings_and_logs()
    {
        // Redesign wave 7: the More tools menu is gone. Install or repair TAOM has its own card on Settings, the rarely
        // needed tools are plain buttons in Settings, Advanced, and the two log tools sit on Logs and help.
        var xaml = Xaml("TaomCoopLauncher");
        var row = Button(xaml, "InstallOrRepairButton").Parent!;
        Assert.Equal(new[] { "Install or repair TAOM" }, row.Elements(Wpf + "Button").Select(button => (string?)button.Attribute("Content")));
        Assert.DoesNotContain(xaml.Descendants(), element => (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == "MoreToolsButton");
        var settings = Page(xaml, "SettingsPage");
        Assert.Equal(new[] { "Unblock_Click", "UseCoop_Click", "RestoreCoop_Click", "ModuleFolder_Click", "ClearShaders_Click" },
            settings.Descendants(Wpf + "WrapPanel").Last().Elements(Wpf + "Button").Select(button => (string?)button.Attribute("Click")));
        var unblock = settings.Descendants(Wpf + "Button").Single(item => (string?)item.Attribute("Click") == "Unblock_Click");
        Assert.Contains("before every launch", (string?)unblock.Attribute("ToolTip"));
        var logs = Page(xaml, "LogsPage");
        foreach (var click in new[] { "OpenClientLogs_Click", "FixAntivirus_Click", "SafeMode_Click", "FindCrash_Click", "SendLogs_Click", "LowFps_Click" })
            Assert.Single(logs.Descendants(Wpf + "Button"), button => (string?)button.Attribute("Click") == click);
        Assert.DoesNotContain(xaml.Descendants(Wpf + "MenuItem"), item => (string?)item.Attribute("Click") is "UseCoop_Click" or "OpenClientLogs_Click");

        var source = SourceFiles.Window(RepoRoot(), "TaomCoopLauncher");
        var both = SourceFiles.Method(source, "private async void InstallOrRepair_Click(");
        Assert.Contains("TaomArchive.NeedsInstall(gameRoot)", both);
        Assert.Contains("Install_Click(sender, e)", both);
        Assert.Contains("Repair_Click(sender, e)", both);
    }

    [Fact]
    public void The_taom_trouble_row_has_a_low_fps_toggle_safe_mode_crash_finder_and_send_my_logs()
    {
        var xaml = Xaml("TaomCoopLauncher");
        var row = Button(xaml, "LowFpsButton").Parent!;
        var clicks = row.Elements(Wpf + "Button").Select(button => (string?)button.Attribute("Click")).ToList();
        Assert.Equal(new[] { "LowFps_Click", "SafeMode_Click", "FindCrash_Click", "SendLogs_Click" }, clicks);
        Assert.Contains("Send my logs", row.Elements(Wpf + "Button").Select(button => (string?)button.Attribute("Content")));
        Assert.DoesNotContain("Content=\"Undo FPS fix\"", File.ReadAllText(XamlPath("TaomCoopLauncher")));
        Assert.DoesNotContain("UnblockFirst", File.ReadAllText(XamlPath("TaomCoopLauncher")));

        var source = SourceFiles.Window(RepoRoot(), "TaomCoopLauncher");
        Assert.Contains("Patchshield.FlagPath(_gameRoot) != null", source);
        var toggle = SourceFiles.Method(source, "private void LowFps_Click(");
        Assert.Contains("UndoFps_Click(sender, e)", toggle);
        Assert.Contains("FixFps_Click(sender, e)", toggle);
        Assert.Contains("\"Low FPS fix: On\"", source);
        Assert.Contains("\"Low FPS fix: Off\"", source);
    }

    [Fact]
    public void The_taom_client_clears_the_download_block_before_every_launch()
    {
        var source = SourceFiles.Window(RepoRoot(), "TaomCoopLauncher");
        Assert.Contains("await UnblockBeforeLaunchAsync();", SourceFiles.Method(source, "private async Task ConnectOnceAsync("));
        Assert.Contains("await UnblockBeforeLaunchAsync();", SourceFiles.Method(source, "private async Task<bool?> TestToMainMenuAsync("));
        Assert.DoesNotContain("Click Unblock DLLs before the game starts", source);
    }

    [Theory]
    [InlineData("TaomCoopLauncher")]
    [InlineData("BannerlordCoopLauncher")]
    [InlineData("AD1259Launcher")]
    [InlineData("BurningEmpiresLauncher")]
    public void Every_button_and_menu_click_has_its_handler(string project)
    {
        var xaml = Xaml(project);
        var source = SourceFiles.Window(RepoRoot(), project);
        var handlers = xaml.Descendants()
            .Where(element => element.Name == Wpf + "Button" || element.Name == Wpf + "MenuItem")
            .Select(element => (string?)element.Attribute("Click"))
            .Where(click => !string.IsNullOrEmpty(click))
            .Distinct();
        foreach (var handler in handlers)
            Assert.Matches(new Regex(@"void " + handler + @"\(object"), source);
    }

    [Theory]
    [InlineData("BannerlordCoopLauncher", "SendLogs_Click", "CrashLogs_Click")]
    [InlineData("AD1259Launcher", "SendLogs_Click", "OpenClientLogs_Click")]
    public void The_other_clients_keep_one_send_my_logs_button_with_the_open_logs_arrow(string project, string send, string open)
    {
        var xaml = Xaml(project);
        var button = xaml.Descendants(Wpf + "Button").Single(item => (string?)item.Attribute("Click") == send);
        Assert.Equal("Send my logs", (string?)button.Attribute("Content"));
        Assert.DoesNotContain(xaml.Descendants(Wpf + "Button"), button => (string?)button.Attribute("Click") == open);
        var arrow = Button(xaml, "LogsMenuButton");
        Assert.Equal(new[] { open, "FixAntivirus_Click" }, MenuClicks(arrow));
        Assert.DoesNotContain("Save all logs to one zip", File.ReadAllText(XamlPath(project)));
    }

    [Fact]
    public void Install_or_repair_installs_when_taom_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-needs-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Modules"));
        try
        {
            Assert.True(TaomArchive.NeedsInstall(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IReadOnlyList<string?> MenuClicks(XElement button) =>
        button.Descendants(Wpf + "MenuItem").Select(item => (string?)item.Attribute("Click")).ToList();

    private static XElement Button(XDocument xaml, string name) =>
        xaml.Descendants(Wpf + "Button").Single(button => (string?)button.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == name);

    private static XDocument Xaml(string project) => XDocument.Load(XamlPath(project));

    private static string XamlPath(string project) => Path.Combine(RepoRoot(), "src", project, "MainWindow.xaml");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
