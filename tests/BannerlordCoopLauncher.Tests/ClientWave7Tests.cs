using System.Xml.Linq;

namespace BannerlordCoopLauncher.Tests;

// Redesign wave 7: the TAOM, AD1259 and Burning Empires Client Launchers on the Coop client's frame (wave 5): a sidebar
// with Play, Mods, Logs and help and Settings (plus Guides on TAOM and Burning Empires), a header with the setup picker or
// the pack name, the pill and the clock, one notice at a time, the Next step card and the Needs attention list on Play.
public sealed class ClientWave7Tests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Ui = "clr-namespace:Coodsz.Ui";

    public static TheoryData<string> Tools => new() { "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher" };

    public static TheoryData<string> SetupTools => new() { "TaomCoopLauncher", "BurningEmpiresLauncher" };

    private static string XamlText(string project) => SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));

    private static XElement Named(XDocument xaml, string name) =>
        xaml.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    private static bool Inside(XElement element, XElement page) => element.Ancestors().Contains(page);

    [Theory]
    [MemberData(nameof(Tools))]
    public void The_window_opens_on_Play_with_the_sidebar_pages(string project)
    {
        var xaml = XDocument.Parse(XamlText(project));
        // Fits 960 px (the sidebar goes to icons under 1000 px) and a 1366x768 screen at 150 percent.
        Assert.Equal("900", (string?)xaml.Root!.Attribute("MinWidth"));
        var window = SourceFiles.Window(SourceFiles.Root(), project);
        Assert.Contains("StartFrame();", SourceFiles.Method(window, "public MainWindow()"));
        Assert.Contains("ShowPage(\"Play\");", SourceFiles.Method(window, "private void StartFrame()"));
        var pages = SourceFiles.Method(window, "private IReadOnlyList<(string Name, FrameworkElement Page)> Pages");
        foreach (var page in new[] { "(\"Play\", PageScroll)", "(\"Mods\", ModsCard)", "(\"Logs\", LogsPage)", "(\"Settings\", SettingsPage)" })
            Assert.Contains(page, pages);
        var guides = project != "AD1259Launcher";
        Assert.Equal(guides, pages.Contains("(\"Guides\", GuidesPage)", StringComparison.Ordinal));
        foreach (var nav in new[] { "NavPlay", "NavMods", "NavLogs", "NavSettings" })
            Assert.Equal("Nav_Click", (string?)Named(xaml, nav).Attribute("Click"));
        if (guides)
        {
            // Guides is the fifth page, in the sidebar.
            var button = Named(xaml, "GuidesButton");
            Assert.Equal("Guides", (string?)button.Attribute("Tag"));
            Assert.Equal("{StaticResource Keep.Nav}", (string?)button.Attribute("Style"));
            Assert.Contains("ShowPage(\"Guides\");", SourceFiles.Method(window, "private void Guides_Click("));
        }
        // The page name has its own key, since "Play" alone is the server time button.
        Assert.Contains("private const string PlayPageKey = \"Play (the page name)\";", window);
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void The_header_has_the_pill_and_the_clock_and_one_notice_shows(string project)
    {
        var text = XamlText(project);
        var xaml = XDocument.Parse(text);
        Assert.Contains("<ui:StatusPill x:Name=\"ClientPill\"", text);
        Assert.Contains("<ui:HeaderClock", text);
        if (project == "AD1259Launcher")
        {
            // No setups: the header names the pack, with its versions on the hover.
            Assert.Equal("Anno Domini 1259", (string?)Named(xaml, "PackText").Attribute("Text"));
            Assert.Contains("PackText.ToolTip = AD1259Stack.SetText;", SourceFiles.Window(SourceFiles.Root(), project));
            Assert.DoesNotContain("x:Name=\"SetupBox\"", text);
        }
        else
        {
            Assert.True(text.IndexOf("x:Name=\"PageTitle\"", StringComparison.Ordinal) < text.IndexOf("x:Name=\"SetupBox\"", StringComparison.Ordinal));
            Assert.True(text.IndexOf("x:Name=\"SetupBox\"", StringComparison.Ordinal) < text.IndexOf("x:Name=\"Banners\"", StringComparison.Ordinal));
            Named(xaml, "LockedSetupTag");
            Assert.Contains("LockedSetupTag.Visibility = LockedSetupText.Visibility;", SourceFiles.Window(SourceFiles.Root(), project));
        }

        var banners = Named(xaml, "Banners");
        Assert.Equal("1", (string?)banners.Attribute("MaxShown"));
        Assert.Equal(new[] { "ReportBarHost", "ReplyBannerHost", "AnnouncementBanner", "UpdateNoticeBar" },
            banners.Elements().OrderBy(e => (int)e.Attribute(Ui + "BannerStack.Priority")!).Select(e => (string)e.Attribute(X + "Name")!));
        // An empty hint slot would take a place in the stack, so it sits on the Mods page.
        Assert.True(Inside(Named(xaml, "ReportHintSlot"), Named(xaml, "ModsCard")));
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void Play_has_the_next_step_card_and_the_needs_attention_list(string project)
    {
        var xaml = XDocument.Parse(XamlText(project));
        var play = Named(xaml, "PageScroll");
        foreach (var name in new[] { "NextStepText", "ActionButtonRow", "NextFindButton", "ConnectButton", "NeedsAttentionCard", "NeedsAttentionList", "GameText", "HostBox", "SendLogsSlot", "RealmCard" })
            Assert.True(Inside(Named(xaml, name), play), name + " is on Play");
        Assert.Equal("ActionButtonRow", (string?)Named(xaml, "ConnectButton").Parent!.Attribute(X + "Name"));
        // Connect starts plain: UpdateConnectButton paints it green when it is ready, so it is never gold too.
        Assert.Equal("{StaticResource Keep.BtnGhost}", (string?)Named(xaml, "ConnectButton").Attribute("Style"));

        var window = SourceFiles.Window(SourceFiles.Root(), project);
        var step = SourceFiles.Method(window, "private void ShowNextStep(bool connectReady)");
        Assert.Contains("ClientNextStep.For(gameMissing, modsChecked: true, connectReady,", step);
        Assert.Contains("ShowNeedsAttention(", step);
        Assert.Contains("ShowClientPill(connectReady);", step);
        Assert.Contains("ShowNextStep(enabled);", SourceFiles.Method(window, "private void UpdateConnectButton()"));
        // The pill follows the join: Joining when the game starts, In game or Refused at the result, cleared when it closes.
        foreach (var state in new[] { "SetJoinState(ClientNextStep.Joining);", "SetJoinState(ClientNextStep.InGame);", "SetJoinState(ClientNextStep.Refused);", "ClearJoinStateWhenClosed(started);" })
            Assert.Contains(state, window);
    }

    [Theory]
    [MemberData(nameof(SetupTools))]
    public void Taom_and_burning_empires_show_the_active_coop_and_their_kit_line_on_play(string project)
    {
        var xaml = XDocument.Parse(XamlText(project));
        var play = Named(xaml, "PageScroll");
        Assert.True(Inside(Named(xaml, "ActiveCoopText"), play));
        Assert.Contains("CoopInstall.ActiveClientLine(", SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), project), "private void ShowActiveCoop()"));
        // The kit line is the Made for card now: TAOM keeps saying it needs Coop 0.1.6 (its pin), Burning Empires pins no Coop.
        Assert.Equal("MadeForCard", (string?)Named(xaml, "ActiveCoopText").Parent!.Attribute(X + "Name"));
        var madeFor = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), project), "private void ShowMadeFor()");
        Assert.Contains(project == "TaomCoopLauncher" ? "TaomStack.CoopVersion" : "new VersionPin(\"Coop\", PinIcon.Coop, null,", madeFor);
        Assert.Contains(project == "TaomCoopLauncher" ? "Old TAOM 2.0.27 campaigns do not load on 2.0.29.5" : "War Sails and the Historical start are not supported.", madeFor);
        if (project == "TaomCoopLauncher")
            Assert.Contains("CoopVersionProblem(out newer)", SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), project), "private List<AttentionItem> AttentionItems()"));
    }

    [Theory]
    [MemberData(nameof(SetupTools))]
    public void The_mods_page_shows_one_line_at_a_time_and_rows_use_the_shared_parts(string project)
    {
        var xaml = XDocument.Parse(XamlText(project));
        var notices = Named(xaml, "ModsNotices");
        Assert.Equal("1", (string?)notices.Attribute("MaxShown"));
        Assert.Equal(new[] { "ModuleSaveHint", "LockedSetupText", "HostNeedsModsText" },
            notices.Elements().OrderBy(e => (int)e.Attribute(Ui + "BannerStack.Priority")!).Select(e => (string)e.Attribute(X + "Name")!));
        Assert.True(Inside(Named(xaml, "ModulePanel"), Named(xaml, "ModsCard")));

        var setups = SourceFiles.Read(Path.Combine("src", project, "MainWindow.Setups.cs"));
        var row = SourceFiles.Method(setups, "private Border ExtraRow(");
        Assert.Contains("new Coodsz.Ui.MoveButtons { ItemName = mod.Label", row);
        Assert.Contains("Style = (Style)FindResource(\"Keep.Switch\")", row);
        Assert.DoesNotContain("\"▲\"", row);
        var core = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), project), "private Border CoreRow(int number, string id)");
        Assert.Contains("Text = ModChips.AlwaysOn", core);
        Assert.DoesNotContain("Text = \"Locked\"", core);
    }

    [Theory]
    [MemberData(nameof(SetupTools))]
    public void Settings_holds_the_setups_the_core_install_and_the_tools(string project)
    {
        var xaml = XDocument.Parse(XamlText(project));
        var settings = Named(xaml, "SettingsPage");
        foreach (var name in new[] { "SetupNameBox", "NewSetupButton", "CopySetupButton", "RenameSetupButton", "DeleteSetupButton", "InstallOrRepairButton" })
            Assert.True(Inside(Named(xaml, name), settings), name + " is on Settings");
        foreach (var click in new[] { "Unblock_Click", "UseCoop_Click", "RestoreCoop_Click", "ModuleFolder_Click", "ClearShaders_Click", "FindBannerlord_Click" })
            Assert.Contains(settings.Descendants(Wpf + "Button"), button => (string?)button.Attribute("Click") == click);
        // No More tools menu any more, in the window or in what the player reads.
        var text = XamlText(project);
        Assert.DoesNotContain("More tools", text);
        Assert.DoesNotContain("open More tools", SourceFiles.Window(SourceFiles.Root(), project), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ad1259_puts_the_pack_on_mods_and_rbm_and_the_windows_tools_on_settings()
    {
        var xaml = XDocument.Parse(XamlText("AD1259Launcher"));
        var mods = Named(xaml, "ModsCard");
        foreach (var name in new[] { "GetMissingButton", "CheckModsButton", "ModulePanel" })
            Assert.True(Inside(Named(xaml, name), mods), name + " is on Mods");
        var settings = Named(xaml, "SettingsPage");
        foreach (var name in new[] { "RbmCombatBox", "RbmAiBox", "RbmCampaignBox", "RbmTournamentBox", "RbmCoopFriendlyBox", "UnblockButton", "UnblockFirstSetup" })
            Assert.True(Inside(Named(xaml, name), settings), name + " is on Settings");
        foreach (var click in new[] { "ModuleFolder_Click", "ClearShaders_Click" })
            Assert.Contains(settings.Descendants(Wpf + "Button"), button => (string?)button.Attribute("Click") == click);
        // The first unblock shows on Play with its own button while it is needed.
        Assert.True(Inside(Named(xaml, "UnblockFirstJoin"), Named(xaml, "PageScroll")));
        var logs = Named(xaml, "LogsPage");
        Assert.True(Inside(Named(xaml, "LogsMenuButton"), logs));
        Assert.True(Inside(Named(xaml, "LogBox"), logs));
        // The row status is the shared chip.
        Assert.Contains("Child = new Coodsz.Ui.Chip { Text = mark, Kind = kind }", SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "AD1259Launcher"), "private Border ModuleRow("));
    }

    [Theory]
    [InlineData("TaomCoopLauncher", "TaomClient", "redesign-backdrop-taom.jpg")]
    [InlineData("AD1259Launcher", "AD1259Client", "redesign-backdrop-castle.jpg")]
    [InlineData("BurningEmpiresLauncher", "BeClient", "redesign-backdrop-castle.jpg")]
    public void Each_launcher_has_its_accent_and_backdrop(string project, string rule, string picture)
    {
        var app = SourceFiles.Read(Path.Combine("src", project, "App.xaml.cs"));
        Assert.Contains("Coodsz.Ui.ToolAccent.Apply(Resources, Coodsz.Ui.FrameRules." + rule + ");", app);
        Assert.Contains("pack://application:,,,/Assets/" + picture, app);
        Assert.Contains("Link=\"Assets\\" + picture + "\"", SourceFiles.Read(Path.Combine("src", project, project + ".csproj")));
        var appXaml = SourceFiles.Read(Path.Combine("src", project, "App.xaml"));
        Assert.True(appXaml.IndexOf("Keep/KeepColors.xaml", StringComparison.Ordinal) < appXaml.IndexOf("Keep/KeepTheme.xaml", StringComparison.Ordinal));
        Assert.Contains("ImageSource=\"{DynamicResource Keep.BackdropArt}\"", XamlText(project));
        Assert.DoesNotContain("calradia-backdrop", XamlText(project) + SourceFiles.Read(Path.Combine("src", project, project + ".csproj")));
        // Nothing uses the old Calradia picture any more.
        Assert.False(File.Exists(Path.Combine(SourceFiles.Root(), "src", "TaomCoopHost", "Assets", "calradia-backdrop.png")));
    }
}
