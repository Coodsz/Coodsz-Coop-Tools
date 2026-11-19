using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Updates;
using CoodszHost.Services;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// What a click test of every button found: layouts that ran off the window, a raw HTTP error,
// guides naming the wrong tools, two save boxes that disagreed, boxes that saved without
// Save settings, buttons that did nothing, and the Test mods candidates and badges.
public sealed class ClickTestFixesTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Ui = "clr-namespace:Coodsz.Ui";

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), "src", .. parts]));

    private static XDocument Xaml(string project, string file = "MainWindow.xaml") => XDocument.Load(Path.Combine(RepoRoot(), "src", project, file));

    private static XElement Named(XDocument xaml, string name) =>
        xaml.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    // ----- 1. Coop and Friends fits its window -----

    [Fact]
    public void The_coop_client_page_is_as_wide_as_the_window_and_its_button_rows_wrap()
    {
        var xaml = Xaml("BannerlordCoopLauncher");
        var page = Named(xaml, "PageScroll").Elements(Wpf + "Grid").Single();
        // Without a width the horizontal scroller measured everything at its full length, twice the window.
        Assert.Equal("{Binding ViewportWidth, ElementName=PageScroll}", (string?)page.Attribute("Width"));
        Assert.Equal("WrapPanel", Named(xaml, "ModuleButtonRow").Name.LocalName);
        Assert.Equal("WrapPanel", Named(xaml, "ActionButtonRow").Name.LocalName);
        // Module folder sits in the More menu of the mod buttons.
        Assert.Contains(Named(xaml, "ModuleButtonRow").Descendants(Wpf + "MenuItem"), item => (string?)item.Attribute(X + "Name") == "ModuleFolderMenuItem");

        // The Install mods / Add mods / Connect help sits in its own card, in readable text.
        var help = Named(xaml, "ButtonHelpCard");
        Assert.Equal("{StaticResource Card}", (string?)help.Attribute("Background"));
        Assert.Contains("Install mods", help.Descendants(Wpf + "Run").Select(run => (string?)run.Attribute("Text")));
        Assert.DoesNotContain(help.Descendants(Wpf + "TextBlock"), block => (string?)block.Attribute("Foreground") == "{StaticResource Muted}");
        Assert.DoesNotContain("Sits beside Install mods", File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "MainWindow.xaml")));
    }

    // ----- 2. Steam's 400 Bad Request -----

    [Fact]
    public async Task Local_mods_are_left_out_of_the_steam_request_so_it_is_not_refused()
    {
        var handler = new FormHandler();
        var client = new SteamWorkshopClient(new HttpClient(handler));
        await client.GetDetailsAsync(["2859188632", "0", "", " ", "abc", "2859188632", "-5"], CancellationToken.None);

        Assert.Single(handler.Forms);
        Assert.Contains("itemcount=1", handler.Forms[0]);
        Assert.Contains("publishedfileids%5B0%5D=2859188632", handler.Forms[0]);
        Assert.DoesNotContain("%5B1%5D", handler.Forms[0]);
    }

    [Fact]
    public async Task Only_local_mods_means_no_steam_request_at_all()
    {
        var handler = new FormHandler();
        var details = await new SteamWorkshopClient(new HttpClient(handler)).GetDetailsAsync(["0", "0", ""], CancellationToken.None);
        Assert.Empty(handler.Forms);
        Assert.Empty(details);
    }

    [Fact]
    public void A_failed_steam_check_shows_a_plain_line_and_keeps_the_http_text_out_of_the_activity_log()
    {
        var launch = Source("BannerlordCoopLauncher", "MainWindow.Launch.cs");
        var catchBlock = launch[launch.IndexOf("Cannot verify the latest Steam release", StringComparison.Ordinal)..];
        catchBlock = catchBlock[..catchBlock.IndexOf('}')];
        Assert.DoesNotContain("Log(ex.Message)", catchBlock);
        Assert.Contains("NoteAction(", catchBlock);
    }

    // ----- 3 and 4. Tool names -----

    [Fact]
    public void Shared_list_guides_name_the_tools_of_their_own_build()
    {
        var host = Source("CoodszHost", "MainWindow.SharedList.cs");
        Assert.Contains("SharedModList.ClientStepsFor(ClientToolName, Edition.Name)", host);
        Assert.Contains("SharedModList.ServerStepsFor(Edition.Name, ClientToolName)", host);
        Assert.Contains("SharedModList.ServerToServerSteps(Edition.Name)", host);
        Assert.DoesNotContain("SharedModList.ClientSteps,", host);

        var client = Source("BannerlordCoopLauncher", "MainWindow.SharedList.cs");
        Assert.Contains("SharedModList.ClientStepsFor(LauncherBrand.Name, LauncherBrand.ServerTool)", client);
        Assert.DoesNotContain("Coodsz Coop Client Launcher", Source("BannerlordCoopLauncher", "ShareModListWindow.cs"));

        var ad1259 = SharedModList.ServerToServerSteps("Coodsz AD1259 Server Tool");
        Assert.Contains("1. In Coodsz AD1259 Server Tool, click Share mod list", ad1259);
        Assert.DoesNotContain("Coodsz Coop Server Tool", ad1259 + SharedModList.ClientSetsItsOwnMods("Coodsz AD1259 Client Launcher"));
    }

    [Fact]
    public void Server_tool_messages_name_the_edition_instead_of_coodsz_host()
    {
        foreach (var file in new[] { "Services/ServerEula.cs", "Services/ServerSession.cs", "Services/DedicatedServerRepair.cs", "MainWindow.Shutdown.cs" })
        {
            var text = Source(["CoodszHost", .. file.Split('/')]);
            var strings = Regex.Matches(text, "\\$?\"(?:[^\"\\\\]|\\\\.)*\"").Select(match => match.Value).Where(value => !value.Contains("Coodsz Coop Server Tool log pack"));
            Assert.DoesNotContain(strings, value => value.Contains("Coodsz Coop Server Tool"));
        }

        Assert.Contains("{Edition.Name}", ServerEulaText());
        Assert.Contains("Start the server from {Edition.Name}", Source("CoodszHost", "MainWindow.Modules.cs"));
    }

    private static string ServerEulaText() => Source("CoodszHost", "Services", "ServerEula.cs");

    [Fact]
    public void The_host_window_title_is_the_tool_name()
    {
        Assert.Equal("Coodsz Coop Server Tool", (string?)Xaml("CoodszHost").Root!.Attribute("Title"));
        var code = Source("CoodszHost", "MainWindow.xaml.cs");
        var title = code.IndexOf("Title = Edition.Name;", StringComparison.Ordinal);
        Assert.True(title >= 0);
        Assert.True(title < code.IndexOf("if (Edition.IsAD1259)", StringComparison.Ordinal), "The title is set for both editions.");
        Assert.Contains("MapSaveNote.Visibility = Visibility.Collapsed;", code);
    }

    // ----- 5. Modules page space -----

    [Fact]
    public void The_host_modules_header_is_one_line_and_a_toolbar_so_the_list_gets_the_room()
    {
        var xaml = Xaml("CoodszHost");
        var header = Named(xaml, "ModulesPage").Elements(Wpf + "Border").First();
        // The old header had a 230 pixel button column with a sentence beside every button.
        Assert.DoesNotContain(header.Descendants(Wpf + "ColumnDefinition"), column => (string?)column.Attribute("Width") == "230");
        Assert.Equal("WrapPanel", Named(xaml, "ModuleToolbar").Name.LocalName);
        Assert.NotNull((string?)Named(xaml, "ModulesIntro").Attribute("ToolTip"));
        foreach (var name in new[] { "AddModsButton", "SaveModulesButton", "TestModsButton", "EnabledOnly", "RefreshModulesButton", "AutoOrderButton" })
            Assert.False(string.IsNullOrWhiteSpace((string?)Named(xaml, name).Attribute("ToolTip")), name + " keeps its explanation as a tooltip.");
        Assert.Equal("MenuItem", Named(xaml, "TestModsButton").Name.LocalName);
        Assert.DoesNotContain("EnabledOnlyNote", Source("CoodszHost", "MainWindow.Modules.cs"));

        foreach (var name in new[] { "OverviewSave", "OverviewProfile", "OverviewMods", "OverviewPorts" })
            Assert.Equal("CharacterEllipsis", (string?)Named(xaml, name).Attribute("TextTrimming"));
    }

    // ----- 6. One campaign save -----

    [Fact]
    public void The_settings_page_follows_the_server_page_save()
    {
        var code = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var serverPage = SourceFiles.Method(code, "private void SaveBox_SelectionChanged(");
        Assert.Contains("SettingsSaveBox.SelectedItem = name;", serverPage);
        var settingsPage = SourceFiles.Method(code, "private void SettingsSave_SelectionChanged(");
        Assert.Contains("SaveBox.SelectedItem = name;", settingsPage);
        var load = SourceFiles.Method(code, "private void LoadDifficulty(");
        Assert.StartsWith("private void LoadDifficulty()\r\n    {\r\n        FollowServerPageSave();".Replace("\r\n", Environment.NewLine), load.Replace("\r\n", Environment.NewLine));
        // server-config.json's saveName (the last save that ran) no longer wins over the Server page.
        Assert.Contains("SettingsSaveBox.SelectedItem == null && !string.IsNullOrWhiteSpace(serverSave)", load);
    }

    // ----- 7. The AD1259 server tool only explains RBM -----

    [Fact]
    public void Ad1259_server_rbm_box_is_a_note_with_no_boxes_and_no_write()
    {
        var code = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.DoesNotContain("Rbm_Changed", code);
        Assert.DoesNotContain("KeepRbmFromPage", code);
        Assert.DoesNotContain("ApplyRbmSwitches", code);
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        foreach (var box in new[] { "RbmCombatBox", "RbmAiBox", "RbmCampaignBox", "RbmTournamentBox" })
            Assert.DoesNotContain("x:Name=\"" + box + "\"", xaml);
        Assert.Contains("Players set RBM in their own launcher. ", xaml);
        Assert.Contains("this server does not use RBM's settings", xaml);
        Assert.DoesNotContain("writes them into RBM's own settings", xaml);
    }

    // ----- 8. + New save needs a name -----

    [Fact]
    public void New_save_with_no_name_changes_nothing()
    {
        var method = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "CoodszHost"), "private void NewSave_Click(");
        Assert.DoesNotContain("Campaign_", method);
        var refuse = method.IndexOf("if (name.Length == 0)", StringComparison.Ordinal);
        Assert.True(refuse >= 0);
        Assert.True(refuse < method.IndexOf("profile.NewCampaign", StringComparison.Ordinal));
        Assert.Contains("Type a name for the new save first", method);
    }

    // ----- 9. TAOM server boxes say On or Off -----

    [Fact]
    public void Taom_server_setting_boxes_show_on_or_off_from_their_state()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopHost", "MainWindow.xaml"));
        Assert.Contains("foreach (var box in new[] { LogFileBox, FastForwardBox, BattleSpeedLimitBox })", Source("TaomCoopHost", "MainWindow.xaml.cs"));
        var settings = Source("TaomCoopHost", "MainWindow.Settings.cs");
        Assert.DoesNotContain("Content = \"On\"", settings);
        Assert.DoesNotContain("check.Content = \"On\";", settings);
        Assert.Contains("box.IsChecked == true ? \"On\" : \"Off\"", settings);
        Assert.Contains("x:Name=\"LogFileBox\"", xaml);
    }

    // ----- 10. No silent buttons -----

    [Fact]
    public void Locked_import_shows_its_message_in_the_guide_and_the_guide_says_what_happens()
    {
        var host = Source("CoodszHost", "MainWindow.SharedList.cs");
        var open = SourceFiles.Method(host, "private void OpenSharedListFile(");
        Assert.Contains("ShowSharedGuideStatus(SharedModList.LockedImportRefused)", open);
        Assert.Contains("SharedModList.ExtraModsServer", host);
        Assert.Contains("SharedModList.ExtraModsClient", Source("BannerlordCoopLauncher", "MainWindow.SharedList.cs"));
        foreach (var project in new[] { "BannerlordCoopLauncher", "CoodszHost", "BannerlordCoopLauncher.Core" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", project), "*.*", SearchOption.AllDirectories)
                         .Where(path => (path.EndsWith(".cs") || path.EndsWith(".xaml")) && !path.Contains("\\obj\\") && !path.Contains("\\bin\\")))
                Assert.DoesNotContain("A locked setup is only checked", File.ReadAllText(file));
    }

    [Fact]
    public void Taom_server_auto_sort_and_rename_show_a_status_line()
    {
        var code = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        var sort = SourceFiles.Method(code, "private void AutoSortHost_Click(");
        Assert.Equal(3, Regex.Matches(sort, "ModStatus\\(").Count);
        var rename = SourceFiles.Method(code, "private void RenameHostSetup_Click(");
        Assert.Contains("ModStatus(", rename);
        Assert.Contains("ModStatus(\"Renamed \"", SourceFiles.Method(code, "private void SaveHostSetupName()"));
    }

    [Fact]
    public void Add_selected_with_nothing_picked_asks_for_a_mod_instead_of_closing()
    {
        foreach (var file in new[] { Source("BannerlordCoopLauncher", "CustomModWindow.cs"), Source("CoodszHost", "HostAddModsWindow.cs") })
        {
            var click = file[file.IndexOf("_add.Click += (_, _) =>", StringComparison.Ordinal)..];
            var check = click.IndexOf("_list.SelectedItems.Count == 0", StringComparison.Ordinal);
            Assert.True(check >= 0);
            Assert.True(check < click.IndexOf("DialogResult = true", StringComparison.Ordinal));
            Assert.Contains("Pick at least one mod", click);
        }
    }

    // ----- 11. Small things -----

    [Fact]
    public void Taom_badges_have_one_width_so_the_row_buttons_line_up()
    {
        var checks = Source("TaomCoopLauncher", "MainWindow.Checks.cs");
        var badge = SourceFiles.Method(checks, "private FrameworkElement? CompatBadge(");
        // A fixed width, not a minimum: two pills used to grow the column and push the buttons left.
        Assert.Contains("var panel = BadgeColumn();", badge);
        Assert.DoesNotContain("MinWidth", badge);
        var column = checks[checks.IndexOf("internal static System.Windows.Controls.WrapPanel BadgeColumn()", StringComparison.Ordinal)..];
        Assert.Contains("Width = BadgeColumnWidth", column[..column.IndexOf('}')]);
        Assert.True(TaomBadgeColumnWidth() >= 205, "Tested safe and Passed on your server fit side by side.");
        Assert.Contains("MaxWidth = BadgeColumnWidth", badge);
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", badge);

        // A row without a badge keeps an empty column, so its buttons line up too.
        var row = SourceFiles.Method(Source("TaomCoopLauncher", "MainWindow.Setups.cs"), "private Border ExtraRow(");
        Assert.Contains("?? BadgeColumn();", row);
    }

    private static double TaomBadgeColumnWidth()
    {
        // The width lives in the shared row layout now (ModRowLayout.ClientChipColumn), the same for the TAOM and BE launchers.
        Assert.Contains("BadgeColumnWidth = Coodsz.Ui.ModRowLayout.ClientChipColumn;", Source("TaomCoopLauncher", "MainWindow.Checks.cs"));
        var match = Regex.Match(SourceFiles.Read(Path.Combine("src", "CoodszHost", "Shared", "Frame", "ModRowLayout.cs")), @"ClientChipColumn = (\d+);");
        return double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void A_trimmed_own_result_pill_still_shows_its_text_on_hover()
    {
        var pill = new ModResultPill("Passed", "Passed on your server", "Tested on your server on 2026-10-18.", false);
        Assert.StartsWith("Passed on your server\n", pill.Hover);
        Assert.EndsWith("Tested on your server on 2026-10-18.", pill.Hover);
        Assert.Contains("ToolTip = pill.Hover", Source("BannerlordCoopLauncher", "CustomModWindow.cs"));
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", Source("BannerlordCoopLauncher", "CustomModWindow.cs"));
    }

    [Fact]
    public void The_host_modules_rows_keep_their_badges_in_one_fixed_column()
    {
        var xaml = Xaml("CoodszHost");
        var compat = xaml.Descendants(Wpf + "Button").Single(element => (string?)element.Attribute("Click") == "CompatBadge_Click");
        var line = compat.Parent!;
        // The badges wrap to a second line when long words (Russian) don't fit the column.
        Assert.Equal("WrapPanel", line.Name.LocalName);
        // The fixed column holds the badge line and, under it, the report chips and the Report link.
        var column = line.Parent!;
        Assert.Equal("StackPanel", column.Name.LocalName);
        Assert.Equal("{x:Static ui:ModRowLayout.ChipColumn}", (string?)column.Attribute("Width"));
        Assert.Contains(column.Elements(Wpf + "ContentControl"), element => (string?)element.Attribute("Content") == "{Binding Reports}");
        var mine = line.Elements(Wpf + "Border").Single();
        Assert.Equal("{Binding Mine.Hover}", (string?)mine.Attribute("ToolTip"));
        Assert.Equal("CharacterEllipsis", (string?)mine.Descendants(Wpf + "TextBlock").First().Attribute("TextTrimming"));
    }

    [Fact]
    public void The_coop_mod_list_status_column_holds_the_status_and_both_badges()
    {
        var xaml = Xaml("BannerlordCoopLauncher");
        // Since the redesign (wave 5) the status is a Chip in the picked language, first in the column.
        var column = xaml.Descendants(Wpf + "WrapPanel").Single(panel => panel.Elements().FirstOrDefault()?.Name == Ui + "Chip"
            && (string?)panel.Elements().First().Attribute("Text") == "{Binding Status}");
        Assert.Equal("{Binding StatusKind}", (string?)column.Elements().First().Attribute("Kind"));
        Assert.Equal("3", (string?)column.Attribute("Grid.Column"));
        Assert.Contains(column.Elements(Wpf + "Button"), element => (string?)element.Attribute("Click") == "CompatBadge_Click");
        var mine = column.Elements(Wpf + "Border").Single(element => (string?)element.Attribute("ToolTip") == "{Binding Mine.Hover}");
        Assert.Equal("156", (string?)mine.Attribute("MaxWidth"));
        Assert.Equal("CharacterEllipsis", (string?)mine.Descendants(Wpf + "TextBlock").First().Attribute("TextTrimming"));

        // The header and the rows give the status column the same width, wide enough for Ready and Works
        // side by side with Passed on your server on the line below.
        var header = xaml.Descendants(Wpf + "TextBlock").Single(block => (string?)block.Attribute("Text") == "STATUS");
        Assert.Equal((string?)column.Attribute("Width"), (string?)header.Attribute("Width"));
        Assert.Equal("{x:Static ui:ModRowLayout.ChipColumn}", (string?)column.Attribute("Width"));
    }

    [Fact]
    public void The_host_sidebar_heading_matches_the_window_title()
    {
        // The sidebar shows COODSZ big with the tool name small under it (redesign wave 2); together they read as the title.
        Assert.Equal("COOP SERVER TOOL", Edition.Brand);
        Assert.Equal(Edition.Name.ToUpperInvariant(), "COODSZ " + Edition.Brand);
        var xaml = Xaml("CoodszHost");
        Assert.Equal("COODSZ", (string?)Named(xaml, "BrandWord").Attribute("Text"));
        Assert.Equal(Edition.Brand, (string?)Named(xaml, "ToolHeading").Attribute("Text"));
        Assert.Null(Named(xaml, "ToolHeading").Attribute("TextWrapping"));
        var edition = Source("CoodszHost", "Services", "Edition.cs");
        Assert.Contains("public const string Brand = \"AD1259 SERVER TOOL\";", edition);
        Assert.Contains("ToolHeading.Text = Edition.Brand;", Source("CoodszHost", "MainWindow.xaml.cs"));
    }

    [Fact]
    public void Save_these_changes_has_a_cancel_button()
    {
        var ask = SourceFiles.Method(Source("BannerlordCoopLauncher", "MainWindow.Setups.cs"), "private ModChangeChoice AskToSaveModChanges(");
        Assert.Contains("Content = \"Cancel\", IsCancel = true", ask);
        Assert.Contains("buttons.Children.Add(cancel);", ask);
    }

    [Theory]
    [InlineData("CoodszCoopAndFriends", "Friends")]
    [InlineData("CoodszHost", "Friends")]
    [InlineData("TaomCoopLauncher", "Taom")]
    [InlineData("TaomCoopHost", "Taom")]
    [InlineData("AD1259Launcher", "AD1259")]
    [InlineData("AD1259Host", "AD1259")]
    [InlineData("BurningEmpiresLauncher", "Be")]
    [InlineData("BurningEmpiresHost", "Be")]
    public void Every_exe_carries_its_release_version(string project, string release)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", project, project + ".csproj"));
        var version = Regex.Match(text, "<Version>([^<]+)</Version>").Groups[1].Value;
        var expected = release switch
        {
            "Friends" => ReleaseNotice.FriendsVersion,
            "Taom" => ReleaseNotice.TaomVersion,
            "Be" => ReleaseNotice.BeVersion,
            _ => ReleaseNotice.AD1259Version
        };
        Assert.Equal(expected, "v" + version);
    }

    [Fact]
    public void The_badge_tip_says_the_click_opens_the_browser()
    {
        var badge = CommunityCompat.Badge("SomeNewMod", ModCompatDatabase.Empty)!;
        Assert.EndsWith("Opens the report page in your browser.", badge.Tip);
    }

    // ----- Test mods on the server: candidates and the Community list column -----

    private static ModuleManifest Manifest(string id, params string[] needs) =>
        new(id, id, "v1.0", needs.Select(need => new ModuleLink(need, null, false)).ToList(), [], [], []);

    [Fact]
    public void Total_overhaul_pieces_and_their_submods_are_not_test_candidates()
    {
        var pc = new List<FoundServerMod>
        {
            new("Diplomacy", "Bannerlord.Diplomacy", @"C:\M\Diplomacy", "Workshop"),
            new("Armies of Middle Earth", "ArmiesOfMiddleEarth", @"C:\M\AOME", "From Nexus or ModDB"),
            new("AIInfluence-TAOM-2.0.29.5", "AIInfluence", @"C:\M\AIInfluence", "From Nexus or ModDB"),
            new("TAOM compat", "TAOM_Standalone_Compat_RBM", @"C:\M\TAOMCompat", "From Nexus or ModDB"),
            new("TAOM Dependencies", "TAOM.Dependencies", @"C:\M\TAOMDeps", "From Nexus or ModDB"),
            new("AD1259", "AD1259", @"C:\M\AD1259", "From Nexus or ModDB"),
            new("AD1259 - Coop Client Stability", "AD1259CoopClientFix", @"C:\M\AD1259Fix", "From Nexus or ModDB"),
            new("Armory patch", "ArmoryPatch", @"C:\M\ArmoryPatch", "From Nexus or ModDB"),
            new("Needs a submod", "SubSub", @"C:\M\SubSub", "From Nexus or ModDB"),
            new("Optional link", "OptionalTaom", @"C:\M\OptionalTaom", "From Nexus or ModDB"),
        };
        var manifests = new Dictionary<string, ModuleManifest>
        {
            [@"C:\M\Diplomacy"] = Manifest("Bannerlord.Diplomacy", "Bannerlord.Harmony"),
            [@"C:\M\AOME"] = Manifest("ArmiesOfMiddleEarth", "TAOM", "TAOM_Map"),
            [@"C:\M\AIInfluence"] = Manifest("AIInfluence", "Bannerlord.Harmony"),
            [@"C:\M\AD1259Fix"] = Manifest("AD1259CoopClientFix", "AD1259"),
            [@"C:\M\ArmoryPatch"] = Manifest("ArmoryPatch", "LOTRLOME_Armory"),
            [@"C:\M\SubSub"] = Manifest("SubSub", "ArmiesOfMiddleEarth"),
            [@"C:\M\OptionalTaom"] = new("OptionalTaom", "OptionalTaom", "v1", [new ModuleLink("TAOM", null, true)], [], [], []),
        };
        var mods = HostModTest.Candidates(pc, StableCoop.Order, [], dir => manifests.TryGetValue(dir, out var m) ? m : null);

        Assert.Equal(["Bannerlord.Diplomacy", "OptionalTaom"], mods.Select(mod => mod.ModuleId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.True(HostModTest.IsOverhaulPiece("AD1259"));
        Assert.True(HostModTest.IsOverhaulPiece("TAOM_Standalone_Compat_Anything"));
        Assert.False(HostModTest.IsOverhaulPiece("Taomething"));
    }

    [Fact]
    public void The_community_column_shows_the_status_the_list_has_for_rbm_and_better_exception_window()
    {
        ModTestCandidate Mod(string id, string workshop = "0") => new(id, id, workshop, "", []);

        // The built in list (the same database the window gets from CommunityCompat.Current).
        var builtIn = ModCompatDatabase.Embedded();
        Assert.True(builtIn.Count > 500);
        foreach (var id in new[] { "RBM", "BetterExceptionWindow" })
        {
            var entry = builtIn.Find(id);
            Assert.NotNull(entry);
            var badge = HostModTest.CommunityBadge(builtIn, Mod(id))!;
            Assert.Equal(entry!.Status.ToString(), badge.Kind);
        }

        // A list where people reported them: the row follows the list.
        var reported = ModCompatDatabase.Parse("""
            { "mods": [
              { "id": "RBM", "status": "Works", "works": 4, "broken": 0 },
              { "id": "BetterExceptionWindow", "status": "ClientOnly", "workshop": "2875478172", "idGuess": true }
            ] }
            """)!;
        Assert.Equal("Works for 4 players", HostModTest.CommunityBadge(reported, Mod("RBM"))!.Text);
        Assert.Equal("Client only", HostModTest.CommunityBadge(reported, Mod("BetterExceptionWindow"))!.Text);
        // A list entry whose id was a guess still matches by the Workshop item id.
        Assert.Equal("Client only", HostModTest.CommunityBadge(reported, Mod("BEW.RealModuleId", "2875478172"))!.Text);
        Assert.Equal(UnknownModBadge.Text, HostModTest.CommunityBadge(reported, Mod("BEW.RealModuleId", "0"))!.Text);
    }

    // ----- Opening Coodsz Host replays the last log; its save line makes no backup -----

    [Fact]
    public void The_replayed_log_tail_does_not_back_up_an_old_save_again()
    {
        // Each open showed the end of the last coop-server log, and its "world saved" line from an
        // earlier run backed that save up again, or logged Backup Failed when the save was gone.
        var session = Source("CoodszHost", "Services", "ServerSession.cs");
        var tail = SourceFiles.Method(session, "private void EmitTail(string path, int maxLines)").Replace("\r", "");
        Assert.Contains("ReplayingLog = true;", tail);
        Assert.Contains("finally\n            {\n                ReplayingLog = false;", tail);
        Assert.True(tail.IndexOf("ReplayingLog = true;", StringComparison.Ordinal) < tail.IndexOf("ServerLine?.Invoke(item);", StringComparison.Ordinal));

        var noted = SourceFiles.Method(Source("CoodszHost", "MainWindow.Backups.cs"), "private void NoteSavedWorld(string line)");
        var skip = noted.IndexOf("if (_session.ReplayingLog)", StringComparison.Ordinal);
        Assert.True(skip > 0 && skip < noted.IndexOf("backups.Create(name, _autoBackups.Reason(name));", StringComparison.Ordinal));
        // Live lines still make the backup.
        Assert.Contains("ServerLine?.Invoke(line);", SourceFiles.Method(session, "private void Pump()"));
    }

    private sealed class FormHandler : HttpMessageHandler
    {
        public List<string> Forms { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Forms.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"response\":{\"publishedfiledetails\":[]}}") };
        }
    }
}
