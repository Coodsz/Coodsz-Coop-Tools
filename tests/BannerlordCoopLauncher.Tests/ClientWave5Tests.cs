using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

// Redesign wave 5: the Coop client on the shared frame, with the Play, Mods, Logs and help and Settings pages.
public sealed class ClientWave5Tests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Ui = "clr-namespace:Coodsz.Ui";

    private static string ClientFile(string file) => SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", file));

    private static XDocument Xaml() => XDocument.Parse(ClientFile("MainWindow.xaml"));

    private static XElement Named(XDocument xaml, string name) =>
        xaml.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    // ----- The next step -----

    [Theory]
    [InlineData(true, false, false, 0, NextStep.FindBannerlord)]
    [InlineData(true, true, true, 0, NextStep.FindBannerlord)]
    [InlineData(false, false, false, 3, NextStep.CheckMods)]
    [InlineData(false, true, true, 2, NextStep.Connect)]
    [InlineData(false, true, false, 2, NextStep.InstallMods)]
    [InlineData(false, true, false, 0, NextStep.FillJoin)]
    public void The_next_step_follows_the_game_then_the_mods_then_the_join(bool gameMissing, bool modsChecked, bool connectReady, int attention, NextStep expected) =>
        Assert.Equal(expected, ClientNextStep.For(gameMissing, modsChecked, connectReady, attention));

    [Fact]
    public void The_next_step_line_and_the_pill_say_what_is_going_on()
    {
        Assert.Equal("1 mod needs attention.", ClientNextStep.Title(NextStep.InstallMods, 1));
        Assert.Equal("2 mods need attention.", ClientNextStep.Title(NextStep.InstallMods, 2));
        Assert.Equal("Ready to join.", ClientNextStep.Title(NextStep.Connect, 0));
        Assert.Equal("Not ready", ClientNextStep.PillText(false, null));
        Assert.Equal("Ready", ClientNextStep.PillText(true, null));
        // A join under way beats Ready.
        Assert.Equal("In game", ClientNextStep.PillText(true, ClientNextStep.InGame));

        var window = SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher");
        // Every Connect state change also moves the gold button and the pill, before the early return.
        var update = SourceFiles.Method(window, "private void UpdateConnectButton()");
        Assert.True(update.IndexOf("ShowNextStep(enabled);", StringComparison.Ordinal) < update.IndexOf("return;", StringComparison.Ordinal));
        var watch = SourceFiles.Method(window, "private async Task WatchJoinAsync(");
        Assert.Contains("SetJoinState(BannerlordCoopLauncher.Core.Launch.ClientNextStep.Joining);", watch);
        Assert.Contains("endState = BannerlordCoopLauncher.Core.Launch.ClientNextStep.Refused;", watch);
        Assert.Contains("ClearJoinStateWhenClosed(started);", watch);
    }

    // Wave 5 review: with no Coop in the list Connect stayed off and said so, but the card said Fill in the Join box with
    // no button. The card now shows the same reason and Open Mods.
    [Fact]
    public void A_mod_list_without_Coop_gets_the_block_reason_and_Open_Mods_not_Fill_in_the_Join_box()
    {
        static ResolvedModule Base(string id) => new(id, id, null, true, true, ModuleState.Ready, "", "C:\\Game\\Modules\\" + id);
        var noCoop = new ModuleResolution(new[] { Base("Native"), Base("SandBoxCore") }, Array.Empty<ResolvedModule>(), false);
        var block = ModuleResolver.ConnectBlockReason(noCoop);
        Assert.NotNull(block);
        var step = ClientNextStep.For(gameMissing: false, modsChecked: true, connectReady: false, attention: 0, block);
        Assert.Equal(NextStep.FixMods, step);
        Assert.Equal(block, ClientNextStep.Title(step, 0, block));
        // Nothing blocks: the join box is what is left.
        Assert.Equal(NextStep.FillJoin, ClientNextStep.For(false, true, false, 0, null));
        // While the game search runs the card waits instead of asking for a mod check.
        Assert.Equal(NextStep.Searching, ClientNextStep.For(false, false, false, 0, null, searching: true));
        Assert.Equal("Looking for Bannerlord...", ClientNextStep.Title(NextStep.Searching, 0));

        var frame = ClientFile("MainWindow.Frame.cs");
        var show = SourceFiles.Method(frame, "private void ShowNextStep(bool connectReady)");
        Assert.Contains("ModuleResolver.ConnectBlockReason(_resolution)", show);
        Assert.Contains("NextModsButton.Visibility = step == NextStep.FixMods", show);
        Assert.Equal("Mods", (string?)Named(Xaml(), "NextModsButton").Attribute("Tag"));
        // Install mods and the Needs attention list count with one rule.
        Assert.Contains("_rows.Count(NeedsAttention)", SourceFiles.Method(frame, "private int AttentionCount()"));
        Assert.Contains("_rows.Where(NeedsAttention)", SourceFiles.Method(frame, "private void ShowNeedsAttention()"));
    }

    // Wave 5 review: Refused never cleared, and a join with no result after 3 minutes jumped back to Ready while the game ran.
    [Fact]
    public void Refused_and_a_join_that_timed_out_clear_when_the_game_closes()
    {
        var watch = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher"), "private async Task WatchJoinAsync(");
        Assert.Contains("if (endState != null)", watch);
        Assert.DoesNotContain("if (endState == BannerlordCoopLauncher.Core.Launch.ClientNextStep.InGame)", watch);
        // Joining, or Game open when both client logs stayed empty (ClientJoinLog.TimedOutState).
        Assert.True(watch.IndexOf("endState = joinLog.TimedOutState(", StringComparison.Ordinal) > watch.IndexOf("while (DateTime.UtcNow < deadline)", StringComparison.Ordinal));
        // Move up and Move down follow the drag rule: the working Coop mods stay put in every unlocked setup.
        Assert.Contains("CanMove = !setupNow.Locked && !DefaultClientSet.IsFixed(module.ModuleId),", ClientFile("MainWindow.Status.cs"));
    }

    // ----- The frame -----

    [Fact]
    public void The_client_loads_the_Keep_look_before_its_hover_style()
    {
        var app = ClientFile("App.xaml");
        var colours = app.IndexOf("Source=\"Keep/KeepColors.xaml\"", StringComparison.Ordinal);
        var theme = app.IndexOf("Source=\"Keep/KeepTheme.xaml\"", StringComparison.Ordinal);
        Assert.True(colours >= 0 && colours < theme && theme < app.IndexOf("<Style TargetType=\"ToolTip\">", StringComparison.Ordinal));
        var start = SourceFiles.Method(ClientFile("App.xaml.cs"), "protected override void OnStartup(");
        Assert.Contains("Coodsz.Ui.ToolAccent.Apply(Resources, Coodsz.Ui.FrameRules.CoopClient);", start);
        Assert.Contains("pack://application:,,,/Assets/redesign-backdrop-castle.jpg", start);
        Assert.Contains(@"<Resource Include=""..\CoodszHost\Assets\redesign-backdrop-castle.jpg"" Link=""Assets\redesign-backdrop-castle.jpg"" />",
            SourceFiles.Read(Path.Combine("src", "CoodszCoopAndFriends", "CoodszCoopAndFriends.csproj")));
    }

    [Fact]
    public void The_sidebar_has_four_pages_and_one_notice_shows_at_a_time()
    {
        var xaml = Xaml();
        var tags = Named(xaml, "NavList").Elements(Wpf + "Button").Select(button => (string?)button.Attribute("Tag")).ToList();
        Assert.Equal(new[] { "Play", "Mods", "Logs", "Settings" }, tags);
        var pages = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher"), "private IReadOnlyList<(string Name, FrameworkElement Page)> Pages");
        foreach (var page in new[] { "(\"Play\", PageScroll)", "(\"Mods\", ModsCard)", "(\"Logs\", LogsPage)", "(\"Settings\", SettingsPage)" })
            Assert.Contains(page, pages);
        var banners = Named(xaml, "Banners");
        Assert.Equal("1", (string?)banners.Attribute("MaxShown"));
        Assert.Equal(new[] { "AnnouncementBanner", "ReplyBannerHost", "ReportBarHost", "UpdateNoticeBar" }, banners.Elements().Select(element => (string?)element.Attribute(X + "Name")));
        // The footer: Language, What's new, Donate and the version.
        var footer = Named(xaml, "FooterFit").Descendants().Select(element => (string?)element.Attribute(X + "Name")).Where(name => name != null);
        Assert.Equal(new[] { "LanguageSlot", "WhatsNewSlot", "DonateSlot", "VersionText" }, footer);
        // The header: the title, the setup picker with its Locked tag, the state pill and the clock.
        Assert.Equal("NotReady", (string?)Named(xaml, "ClientPill").Attribute("Kind"));
        Assert.Single(xaml.Descendants(Ui + "HeaderClock"));
        Assert.True(Named(xaml, "LockedSetupTag").IsBefore(Named(xaml, "SetupBox")));
    }

    [Fact]
    public void The_hidden_join_boxes_stay_in_the_window()
    {
        var xaml = Xaml();
        foreach (var name in new[] { "ProfileUrlBox", "NexusKeyBox", "GamePathBox" })
            Assert.Equal("Collapsed", (string?)Named(xaml, name).Attribute("Visibility"));
    }

    // ----- Play -----

    [Fact]
    public void Play_has_one_next_step_card_with_the_help_closed()
    {
        var xaml = Xaml();
        var row = Named(xaml, "ActionButtonRow");
        Assert.Equal(new[] { "NextFindButton", "NextModsButton", "InstallButton", "ConnectButton", "PlayCheckModsButton", "DiagnosticButton" },
            row.Elements(Wpf + "Button").Select(button => (string?)button.Attribute(X + "Name")));
        // No button starts gold: ShowNextStep picks the one.
        Assert.All(row.Elements(Wpf + "Button"), button => Assert.Equal("{StaticResource Keep.BtnGhost}", (string?)button.Attribute("Style")));
        Assert.True(Named(xaml, "BlockReasonText").IsAfter(row));
        Assert.Equal("Collapsed", (string?)Named(xaml, "NeedsAttentionCard").Attribute("Visibility"));
        Assert.Equal("{Binding IsChecked, ElementName=ButtonHelpToggle, Converter={StaticResource BoolToVisible}}",
            (string?)((XElement)Named(xaml, "ButtonHelpToggle").NextNode!).Attribute("Visibility"));
        // The Join my server card sits under the Next step card; PlayLayout puts it last when the page stacks.
        Assert.Equal(("1", "1"), ((string?)Named(xaml, "RealmCard").Attribute("Grid.Row"), (string?)Named(xaml, "RealmCard").Attribute("Grid.Column")));
    }

    [Fact]
    public void The_needs_attention_list_holds_the_first_three_problem_mods()
    {
        var frame = ClientFile("MainWindow.Frame.cs");
        var show = SourceFiles.Method(frame, "private void ShowNeedsAttention()");
        Assert.Contains("problems.Take(3)", show);
        Assert.Contains("row.IsOn", show);
        Assert.Contains("ShowNeedsAttention();", SourceFiles.Method(ClientFile("MainWindow.Status.cs"), "private void BindModules(ModuleResolution resolution)"));
    }

    // ----- Mods -----

    [Fact]
    public void A_mod_row_moves_with_its_buttons_and_Alt_arrows_by_the_drag_rules()
    {
        var xaml = Xaml();
        var list = Named(xaml, "ModuleList");
        Assert.Equal("ModuleListKeys", (string?)list.Attribute("PreviewKeyDown"));
        var move = list.Descendants(Ui + "MoveButtons").Single();
        // Devin review: the first movable row's up arrow and the last one's down arrow are off, not silent.
        Assert.Equal("{Binding CanMoveUp}", (string?)move.Attribute("CanMoveUp"));
        Assert.Equal("{Binding CanMoveDown}", (string?)move.Attribute("CanMoveDown"));
        var window = SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher");
        Assert.Contains("_rows.CollectionChanged += (_, _) => RefreshMoveEdges();", window);
        var edges = SourceFiles.Method(window, "private void RefreshMoveEdges()");
        Assert.Contains("row.CanMoveUp = row.CanMove && !ReferenceEquals(row, movable[0]);", edges);
        Assert.Contains("row.CanMoveDown = row.CanMove && !ReferenceEquals(row, movable[^1]);", edges);
        Assert.Equal("ModuleMoveUp", (string?)move.Attribute("MoveUp"));
        Assert.Equal("{StaticResource Keep.Switch}", (string?)list.Descendants(Wpf + "CheckBox").Single().Attribute("Style"));

        var frame = ClientFile("MainWindow.Frame.cs");
        var moveRow = SourceFiles.Method(frame, "private void MoveRow(ModuleRow? row, int delta)");
        Assert.Contains("WorkingLoadOrder.CanMove(", moveRow);
        Assert.Contains("SaveRowOrder();", moveRow);
        Assert.Contains("Keyboard.Modifiers == ModifierKeys.Alt", SourceFiles.Method(frame, "private void ModuleListKeys("));
        // Dragging saves the same way.
        Assert.Contains("SaveRowOrder();", SourceFiles.Method(ClientFile("MainWindow.Modules.cs"), "private void FinishModuleDrag()"));
    }

    // ----- Logs and help, Settings -----

    [Fact]
    public void Logs_and_help_and_Settings_hold_the_tools()
    {
        var xaml = Xaml();
        var logs = Named(xaml, "LogsPage");
        foreach (var name in new[] { "SendLogsButton", "LogsMenuButton", "LogsMyReportsSlot", "ReportModSlot", "LogBox" })
            Assert.Contains(logs.Descendants(), element => (string?)element.Attribute(X + "Name") == name);
        Assert.Contains("Coodsz.Ui.ReportRepliesUi.AddMyReports(this, LogsMyReportsSlot,", ClientFile("MainWindow.CrashReports.cs"));
        // The calm Report a mod line sits with the mods, not under the Report a mod button.
        Assert.Contains(Named(xaml, "ModsCardHeader").Elements(), element => (string?)element.Attribute(X + "Name") == "ReportHintSlot");

        var settings = Named(xaml, "SettingsPage");
        foreach (var name in new[] { "SetupNameBox", "NewSetupButton", "CopySetupButton", "RenameSetupButton", "DeleteSetupButton", "UnblockButton" })
            Assert.Contains(settings.Descendants(), element => (string?)element.Attribute(X + "Name") == name);
        // The advanced tools in one button style.
        var advanced = settings.Descendants(Wpf + "Button").Where(button => (string?)button.Attribute("Click") is "Unblock_Click" or "ClearShaders_Click" or "ClearUnusedMcm_Click").ToList();
        Assert.Equal(3, advanced.Count);
        Assert.All(advanced, button => Assert.Equal("{StaticResource Keep.BtnGhost}", (string?)button.Attribute("Style")));
    }

    // ----- Seen once -----

    [Fact]
    public void The_client_intro_card_counts_as_seen_when_it_shows()
    {
        var show = SourceFiles.Method(ClientFile("ModReportWindow.cs"), "public void ShowIntroOnce()");
        Assert.True(show.IndexOf("Put(ModReportBarKind.Intro, BuildCrashIntro());", StringComparison.Ordinal) < show.IndexOf("CrashReports.MarkIntroSeen(settings, Tool, \"client\");", StringComparison.Ordinal));
        Assert.True(show.IndexOf("Put(ModReportBarKind.Intro, BuildIntro());", StringComparison.Ordinal) < show.IndexOf("settings.IntroSeen = true;", StringComparison.Ordinal));
        Assert.Contains("if (Kind != ModReportBarKind.Intro)", show);
    }

    [Fact]
    public void The_row_status_is_the_english_word_so_a_language_switch_updates_it()
    {
        var bind = SourceFiles.Method(ClientFile("MainWindow.Status.cs"), "private void BindModules(ModuleResolution resolution)");
        Assert.Contains("Status = StatusText(module),", bind);
    }
}
