using System.Xml.Linq;
using CoodszHost.Services;
using Coodsz.Ui;

namespace BannerlordCoopLauncher.Tests;

// Redesign wave 4: the Settings groups as cards, the Logs, Console and Keep pages over the backdrop, the narrow
// Dashboard, the decluttered Mods page and the one time crash report card.
public sealed class HostPagesWave4Tests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument HostXaml() => XDocument.Parse(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml")));

    private static XElement Named(XDocument xaml, string name) =>
        xaml.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    // The nearest Border around the element is a card or a backing strip, so the text never sits on the backdrop.
    private static bool InCard(XElement element) =>
        element.Ancestors(Wpf + "Border").FirstOrDefault() is { } border
        && ((string?)border.Attribute("Style") is "{StaticResource Card}" or "{StaticResource Keep.Card}" or "{StaticResource Keep.Panel}" || border.Attribute("Background") != null);

    // ----- Settings -----

    [Fact]
    public void Save_settings_writes_the_password_and_Steam_advertise_and_the_page_reads_them_back()
    {
        var path = Path.Combine(Path.GetTempPath(), "coodsz-wave4-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\n  \"password\": \"old\",\n  \"steam\": true,\n  \"logFile\": true\n}\n");
            // Save settings, as SaveSettingsPage does it: a new password, Steam advertise unticked on a page that read the file.
            var server = JsoncEditor.Read(path);
            server = JsoncEditor.SetString(server, "password", ServerConfigFlags.NormalPassword("friends only"));
            server = JsoncEditor.SetBool(server, "steam", ServerConfigFlags.ToWrite("steam", false, loadedFromFile: true, server));
            JsoncEditor.Write(path, server);

            // The page opens again (LoadServerSwitches): the boxes show what was saved.
            var reread = JsoncEditor.Read(path);
            Assert.Equal("friends only", JsoncEditor.GetString(reread, "password"));
            Assert.Equal("false", JsoncEditor.GetRaw(reread, "steam"));
            Assert.Equal("true", JsoncEditor.GetRaw(reread, "logFile"));

            // A password of only spaces clears it, and ticking Steam advertise again writes true.
            reread = JsoncEditor.SetString(reread, "password", ServerConfigFlags.NormalPassword("   "));
            reread = JsoncEditor.SetBool(reread, "steam", ServerConfigFlags.ToWrite("steam", true, loadedFromFile: true, reread));
            JsoncEditor.Write(path, reread);
            var last = JsoncEditor.Read(path);
            Assert.Equal("", JsoncEditor.GetString(last, "password"));
            Assert.Equal("true", JsoncEditor.GetRaw(last, "steam"));
        }
        finally
        {
            File.Delete(path);
        }

        // The window does those same calls with the boxes of the Join card.
        var code = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        var save = SourceFiles.Method(code, "private bool SaveSettingsPage()");
        Assert.Contains("ServerConfigFlags.NormalPassword(PasswordBox.Text)", save);
        Assert.Contains("JsoncEditor.SetString(server, \"password\", password)", save);
        Assert.Contains("ServerConfigFlags.ToWrite(\"steam\", SteamBox.IsChecked == true, _serverFlagsLoaded, server)", save);
        var load = SourceFiles.Method(code, "private void LoadServerSwitches(");
        Assert.Contains("SteamBox.IsChecked = Flag(server, \"steam\", true);", load);
        Assert.Contains("JsoncEditor.GetString(server, \"password\")", load);
    }

    [Fact]
    public void Every_Settings_group_heading_sits_in_its_card_and_the_save_bar_stays()
    {
        var xaml = HostXaml();
        var page = Named(xaml, "SettingsPage");
        var headings = page.Descendants(Wpf + "TextBlock")
            .Where(text => (string?)text.Attribute("FontSize") == "18" && (string?)text.Attribute(X + "Name") != "SettingsSavePrompt")
            .ToList();
        Assert.True(headings.Count >= 15);
        foreach (var heading in headings)
            Assert.True(InCard(heading), ((string?)heading.Attribute("Text") ?? "") + " sits straight on the backdrop.");
        // Join is the first card, with the password and Steam advertise in it.
        var join = headings.First();
        Assert.Equal("Join", (string?)join.Attribute("Text"));
        var card = join.Ancestors(Wpf + "Border").First();
        Assert.Contains(card.Descendants(), element => (string?)element.Attribute(X + "Name") == "PasswordBox");
        Assert.Contains(card.Descendants(), element => (string?)element.Attribute(X + "Name") == "SteamBox");
        // The sticky bar is above the scrolling groups, and there are no tabs.
        Assert.Equal("SettingsSaveBanner", (string?)page.Elements(Wpf + "Border").First().Attribute(X + "Name"));
        Assert.Contains(page.Descendants(), element => (string?)element.Attribute(X + "Name") == "SettingsSaveDetail");
        Assert.Empty(page.Descendants(Wpf + "TabControl"));
    }

    // ----- Logs and Console -----

    [Fact]
    public void The_Logs_page_frames_the_log_and_has_My_reports_beside_Send_my_logs()
    {
        var xaml = HostXaml();
        var log = Named(xaml, "ServerLog");
        Assert.Equal("#FF0A1015", (string?)log.Ancestors(Wpf + "Border").First().Attribute("Background"));
        var buttons = Named(xaml, "LogsPage").Descendants(Wpf + "Button").Select(button => (string?)button.Attribute("Click")).ToList();
        Assert.Equal(new[] { "Logs_Click", "SendLogs_Click", "RescueSave_Click", "RescueHelp_Click" }, buttons);
        var send = Named(xaml, "LogsPage").Descendants(Wpf + "Button").Single(button => (string?)button.Attribute("Click") == "SendLogs_Click");
        Assert.Equal("LogsMyReportsSlot", (string?)((XElement)send.NextNode!).Attribute(X + "Name"));
        Assert.True(InCard(send));
        // The gold help line stays, in the card with the buttons.
        var gold = Named(xaml, "LogsPage").Descendants(Wpf + "TextBlock").Single(text => ((string?)text.Attribute("Text") ?? "").StartsWith("Opens the server logs.", StringComparison.Ordinal));
        Assert.Equal("#FFE2C27A", (string?)gold.Attribute("Foreground"));
        Assert.True(InCard(gold));
        Assert.Contains("Coodsz.Ui.ReportRepliesUi.AddMyReports(this, LogsMyReportsSlot,", SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.CrashReports.cs")));
        // The second button counts like the sidebar one but never starts a second reply check.
        var replies = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs")), "public static void AddMyReports(");
        Assert.DoesNotContain("ReplyChecker", replies);
        Assert.Contains("Changed += Refresh;", replies);
    }

    [Fact]
    public void The_Console_state_line_and_tick_boxes_sit_in_cards()
    {
        var xaml = HostXaml();
        Assert.True(InCard(Named(xaml, "ConsoleState")));
        Assert.True(InCard(Named(xaml, "AutoScroll")));
        Assert.DoesNotContain(Named(xaml, "ConsolePage").Descendants(Wpf + "TextBlock"), text => (string?)text.Attribute("Text") == "Commands");
    }

    [Fact]
    public void The_Console_output_box_keeps_several_lines_on_a_small_window()
    {
        // The output box was squashed to one line: the recent command chips, the command box and the Clear row docked
        // under it took the whole row, and the page has no scroll bar to give it more.
        // The heights come from the XAML and its styles; only the text line height (Segoe UI about 1.33 times the font size,
        // Consolas about 1.17) is an estimate, so the live test still checks it on a real small screen.
        var xaml = HostXaml();
        var keep = XDocument.Parse(SourceFiles.Read(Path.Combine("src", "CoodszHost", "Keep", "KeepTheme.xaml")));
        var page = Named(xaml, "ConsolePage");
        Assert.Equal(Wpf + "Grid", page.Name);
        Assert.DoesNotContain(page.Ancestors(), parent => parent.Name == Wpf + "ScrollViewer");
        var rows = page.Element(Wpf + "Grid.RowDefinitions")!.Elements(Wpf + "RowDefinition").ToList();
        static double Number(string text) => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        // Top plus bottom of a Thickness ("10", "12,6" or "0,8,0,0").
        static double Vertical(string? text)
        {
            var parts = (text ?? "0").Split(',').Select(Number).ToArray();
            return parts.Length switch { 1 => 2 * parts[0], 2 => 2 * parts[1], _ => parts[1] + parts[3] };
        }
        static double Top(string? text) => (text ?? "0").Split(',') is { Length: 4 } parts ? Number(parts[1]) : Vertical(text) / 2;
        static string Setter(XDocument doc, string? key, string type, string property) => doc.Descendants(Wpf + "Style")
            .Single(style => key == null ? style.Attribute(X + "Key") == null && (string?)style.Attribute("TargetType") == type : (string?)style.Attribute(X + "Key") == key)
            .Elements(Wpf + "Setter").Single(setter => (string?)setter.Attribute("Property") == property).Attribute("Value")!.Value;
        static double Line(double fontSize, double factor = 1.33) => Math.Ceiling(fontSize * factor);

        // A Ghost button (Send, Clear, Copy) and the command box are both 30 high: one 12 pt line, padding and a 1 px border.
        var ghost = Line(Number(Setter(xaml, "Ghost", "Button", "FontSize"))) + Vertical(Setter(xaml, "Ghost", "Button", "Padding"))
            + Vertical(Setter(xaml, "Ghost", "Button", "BorderThickness"));
        var commandBox = Line(12) + Vertical(Setter(xaml, null, "TextBox", "Padding")) + 2;
        var buttonRow = Math.Max(ghost, commandBox);
        Assert.Equal(30, buttonRow);

        // The chips (Keep.Small, Consolas 12) stop at two rows and scroll after that.
        var chips = Named(xaml, "RecentCommands").Parent!;
        Assert.Equal(Wpf + "ScrollViewer", chips.Name);
        var chipRow = Line(12, 1.17) + Vertical(Setter(keep, "Keep.Small", "Button", "Padding")) + 2 + 6;
        var chipsMax = Number((string)chips.Attribute("MaxHeight")!);
        Assert.InRange(chipsMax, 2 * chipRow, 3 * chipRow - 1);

        // Row 1's MinHeight is exactly what is docked in it: card border and padding, chips, command row, Clear row, output box.
        var card = Named(xaml, "HistoryBox").Ancestors(Wpf + "Border").First();
        var history = Number((string)Named(xaml, "HistoryBox").Attribute("MinHeight")!);
        Assert.True(history >= 100, "the output box shows several lines");
        var docked = card.Element(Wpf + "DockPanel")!.Elements(Wpf + "DockPanel").ToList();
        Assert.Equal(2, docked.Count);
        var sum = Vertical(Setter(xaml, "Card", "Border", "BorderThickness")) + Vertical((string?)card.Attribute("Padding"))
            + Top((string?)chips.Attribute("Margin")) + chipsMax
            + docked.Sum(row => Top((string?)row.Attribute("Margin")) + buttonRow)
            + history;
        Assert.Equal(sum, Number((string)rows[1].Attribute("MinHeight")!));
        Assert.Equal("3*", (string?)rows[1].Attribute("Height"));
        Assert.Equal("4*", (string?)rows[3].Attribute("Height"));
        // The command browser has no minimum, so it gives way when space runs short.
        Assert.Null(rows[3].Attribute("MinHeight"));

        // The top card: padding and border, the 18 pt state line, then the link box (8 margin, 8 padding, border, a 22 pt line),
        // and 8 under it. MainWindow.xaml.cs fits MinHeight to the work area (WindowFit), so at 1366x768 and 125% the page
        // area is about 410 high. The top card, the output row and the splitter must fit in that.
        var topCard = page.Elements(Wpf + "Border").First();
        var top = Vertical((string?)topCard.Attribute("Padding")) + 2 + Line(18) + 8 + 16 + 2 + Line(22) + Vertical((string?)topCard.Attribute("Margin"));
        var splitter = Number((string)page.Element(Wpf + "GridSplitter")!.Attribute("Height")!);
        Assert.True(top + sum + splitter <= 410, $"top {top} + output row {sum} + splitter {splitter} must fit a 410 high page");
        Assert.Contains("WindowFit.Fit(", SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml.cs")));
    }

    // ----- Keep pages -----

    [Theory]
    [InlineData("CheatsPage.xaml", "Cheats")]
    [InlineData("MapPage.xaml", "Map")]
    [InlineData("EncyclopediaPage.xaml", "Encyclopedia")]
    [InlineData("GuidesPage.xaml", "Guides")]
    [InlineData("PlayersPage.xaml", "Players")]
    public void Keep_pages_leave_the_title_to_the_header_and_back_their_intro(string file, string title)
    {
        var page = XDocument.Parse(SourceFiles.Read(Path.Combine("src", "CoodszHost", "Keep", file)));
        Assert.DoesNotContain(page.Descendants(Wpf + "TextBlock"), text => (string?)text.Attribute("Text") == title && (string?)text.Attribute("Style") == "{StaticResource Keep.H1}");
        var intro = page.Descendants(Wpf + "TextBlock").First(text => (string?)text.Attribute("Style") == "{StaticResource Keep.Dim}");
        Assert.Equal("{StaticResource Keep.Panel}", (string?)intro.Ancestors(Wpf + "Border").First().Attribute("Style"));
    }

    // ----- Narrow windows -----

    [Fact]
    public void The_Dashboard_side_column_goes_under_the_main_one_when_the_page_is_narrow()
    {
        // 1280 wide leaves the page about 1000 px, 1100 and 960 wide about 830 and 850.
        Assert.False(FrameRules.SideColumnWraps(1000, 300));
        Assert.True(FrameRules.SideColumnWraps(850, 300));
        Assert.True(FrameRules.SideColumnWraps(919, 300));
        Assert.False(FrameRules.SideColumnWraps(920, 300));
        Assert.False(FrameRules.SideColumnWraps(0, 300));
        var fit = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private void FitDashboard(");
        Assert.Contains("Grid.SetRow(DashSide, stack ? 1 : 0);", fit);
        Assert.Contains("DashSideColumn.Width", fit);
    }

    [Fact]
    public void The_Saves_setup_buttons_and_the_world_buttons_are_even()
    {
        var xaml = HostXaml();
        var setup = new[] { "NewSetupButton", "CopySetupButton", "SaveSetupButton", "DeleteSetupButton" }.Select(name => Named(xaml, name)).ToList();
        // One WrapPanel, no inner pairs, so a narrow window wraps them one at a time.
        Assert.Single(setup.Select(button => button.Parent).Distinct());
        Assert.Equal("WrapPanel", setup[0].Parent!.Name.LocalName);
        Assert.All(setup, button => Assert.Equal("0,0,8,8", (string?)button.Attribute("Margin")));
        var create = Named(xaml, "CreateWorldButton");
        var backup = create.ElementsAfterSelf(Wpf + "Button").Single();
        Assert.Equal("BackupNow_Click", (string?)backup.Attribute("Click"));
        Assert.Equal((string?)create.Attribute("Height"), (string?)backup.Attribute("Height"));
        Assert.Equal((string?)create.Attribute("Padding"), (string?)backup.Attribute("Padding"));
    }

    // ----- Mods page declutter -----

    [Fact]
    public void The_Mods_page_top_shows_one_notice_the_toolbar_and_short_lines()
    {
        var xaml = HostXaml();
        var text = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml"));
        Assert.DoesNotContain("ModsSameBanner", text);
        Assert.DoesNotContain("ModsSameBanner", SourceFiles.Window(SourceFiles.Root(), "CoodszHost"));
        // Players need the same mods: the hover of the count.
        Assert.Equal("Players need the same mods at the same versions to join.", (string?)Named(xaml, "ModuleSummaryText").Ancestors(Wpf + "Border").First().Attribute("ToolTip"));
        // The toolbar comes before the grey lines and holds Full mod list, so neither is cut off or pushed away.
        Assert.True(text.IndexOf("x:Name=\"ModuleToolbar\"", StringComparison.Ordinal) < text.IndexOf("x:Name=\"ModulesIntro\"", StringComparison.Ordinal));
        Assert.Equal("ModuleToolbar", (string?)Named(xaml, "FullModListLink").Parent!.Attribute(X + "Name"));
        Assert.Equal("Keep the Coop mods in this order. Extra mods go below.", (string?)Named(xaml, "ModulesIntro").Attribute("Text"));
        Assert.Equal((string?)Named(xaml, "ModulesIntro").Attribute("ToolTip"), (string?)((XElement)Named(xaml, "ModulesIntro").NextNode!).Attribute("ToolTip"));
        // The save hint hides while the running banner shows.
        var hint = Named(xaml, "ModuleSaveHint").Parent!;
        Assert.Contains(hint.Descendants(Wpf + "DataTrigger"), trigger => (string?)trigger.Attribute("Binding") == "{Binding Visibility, ElementName=ModsRunningBanner}");
        // The dependency line only for a problem.
        Assert.Equal("Collapsed", (string?)Named(xaml, "DependencyNote").Attribute("Visibility"));
        Assert.False(ModuleCatalog.ShowsNote(ModuleCatalog.ValidNote));
        Assert.False(ModuleCatalog.ShowsNote(""));
        Assert.True(ModuleCatalog.ShowsNote("RBM needs Harmony, which is disabled."));
        var hintCode = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private void ShowWorkingOrderHint(");
        Assert.DoesNotContain("can break the map", hintCode);
        Assert.Contains("ModuleSaveHint.ToolTip = ModulesIntro.ToolTip;", hintCode);
    }

    // ----- The one time crash report card -----

    [Fact]
    public void The_crash_report_card_shows_once_even_when_nobody_presses_it()
    {
        var bar = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        var show = SourceFiles.Method(bar, "public void ShowIntroOnce()");
        Assert.True(show.IndexOf("Show(ModReportUi.Frame(", StringComparison.Ordinal) < show.IndexOf("MarkIntroSeen();", StringComparison.Ordinal));
        // Pressing x or Try it still hides the card that shows now.
        var done = SourceFiles.Method(bar, "private void IntroDone()");
        Assert.Contains("if (_introShown && !ShowsCrash)", done);
    }

    // ----- Page names in the guides and the README -----

    [Fact]
    public void The_Coop_Server_Tool_guides_and_README_name_the_new_pages()
    {
        // The Coop guides (the TAOM and Burning Empires ones are in their own folders and keep their Server page until wave 6).
        foreach (var file in Directory.GetFiles(Path.Combine(SourceFiles.Root(), "src", "BannerlordCoopLauncher.Core", "Keep", "Guides"), "*.json"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Server page", text);
            Assert.DoesNotContain(" on Home", text);
            Assert.DoesNotContain("On Home", text);
            Assert.DoesNotContain("Go to Modules", text);
        }

        // The README's Coop host part (the TAOM server tool part above it still has its Server page).
        var readme = SourceFiles.Read("README.txt");
        var host = readme[readme.IndexOf("# Coodsz Coop tools players start here", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Server page", host);
        Assert.DoesNotContain("server page", host);
        Assert.DoesNotContain("Modules tab", host);
        Assert.Contains("The Dashboard lists the addresses friends can use", host);
    }
}
