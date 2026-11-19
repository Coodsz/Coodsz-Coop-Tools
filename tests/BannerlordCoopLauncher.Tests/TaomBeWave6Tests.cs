using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

// Redesign wave 6: the TAOM and Burning Empires server tools on the Coop Server Tool's frame, with the Dashboard, Saves,
// Mods (with the running block), Settings in cards, Cheats, Console, Logs and Guides.
public sealed class TaomBeWave6Tests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Ui = "clr-namespace:Coodsz.Ui";

    public static TheoryData<string> Tools => new() { "TaomCoopHost", "BurningEmpiresHost" };

    private static string XamlText(string project) => SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));

    private static XElement Named(XDocument xaml, string name) =>
        xaml.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    [Theory]
    [MemberData(nameof(Tools))]
    public void The_window_opens_on_the_Dashboard_and_fits_a_small_screen(string project)
    {
        var xaml = XDocument.Parse(XamlText(project));
        // BeServerUiFixTests: 1366x768 at 150 percent leaves about 910 px.
        Assert.Equal("900", (string?)xaml.Root!.Attribute("MinWidth"));
        var window = SourceFiles.Window(SourceFiles.Root(), project);
        Assert.Contains("WindowFit.Fit(new WindowSize(Width, Height, MinWidth, MinHeight)", SourceFiles.Method(window, "public MainWindow()"));
        Assert.Contains("private const string StartPage = \"Dashboard\";", window);
        Assert.Contains("ShowPage(StartPage);", window);
        var pages = SourceFiles.Method(window, "private IReadOnlyList<(string Name, FrameworkElement Page)> Pages");
        foreach (var page in new[] { "(\"Dashboard\", DashboardPage)", "(\"Server\", ServerPage)", "(\"Modules\", ModulesPage)", "(\"Settings\", SettingsPage)", "(\"Cheats\", CoopCheats)", "(\"Console\", ConsolePage)", "(\"Logs\", LogsPage)", "(\"Guides\", GuidesPage)", "(\"Realm\", RealmPage)" })
            Assert.Contains(page, pages);
        var show = SourceFiles.Method(window, "private void ShowPage(string page)");
        Assert.Contains("foreach (var (name, element) in Pages)", show);
        Assert.Contains("Coodsz.Ui.NavRail.Select(NavButtons, page);", show);
        Assert.Contains("PageTitle.Text = PageTitleOf(page);", show);
        Assert.DoesNotContain("Color.FromRgb", show);
        Assert.DoesNotContain("UpdateRealmNav", window);
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void The_header_holds_the_setup_picker_the_pill_and_the_clock_and_one_notice_shows(string project)
    {
        var text = XamlText(project);
        var xaml = XDocument.Parse(text);
        // The setup picker of the Mods page sits in the header now, before the notices and the pages.
        Assert.True(text.IndexOf("x:Name=\"PageTitle\"", StringComparison.Ordinal) < text.IndexOf("x:Name=\"ModSetupBox\"", StringComparison.Ordinal));
        Assert.True(text.IndexOf("x:Name=\"ModSetupBox\"", StringComparison.Ordinal) < text.IndexOf("x:Name=\"Banners\"", StringComparison.Ordinal));
        Assert.Contains("<ui:StatusPill x:Name=\"StatePill\"", text);
        Assert.Contains("<ui:HeaderClock", text);
        foreach (var name in new[] { "StateText", "StatusDot", "StateDetail", "StartTopProgress", "LockedSetupTag", "LockedSetupText", "SetupRunningNote" })
            Assert.Contains("x:Name=\"" + name + "\"", text);

        var banners = Named(xaml, "Banners");
        Assert.Equal("1", (string?)banners.Attribute("MaxShown"));
        var order = banners.Elements().ToDictionary(e => (string)e.Attribute(X + "Name")!, e => (int)e.Attribute(Ui + "BannerStack.Priority")!);
        var expected = project == "BurningEmpiresHost"
            ? new[] { "SaveToNewNameBar", "CrashBarHost", "ReplyBannerHost", "AnnouncementBanner", "RescueNotice", "UpdateNoticeBar" }
            : new[] { "CrashBarHost", "ReplyBannerHost", "AnnouncementBanner", "RescueNotice", "UpdateNoticeBar" };
        Assert.Equal(expected, order.OrderBy(p => p.Value).Select(p => p.Key));
        // The version strip is not a banner any more: it is the Made for card on the Dashboard.
        Assert.True(text.IndexOf("<ui:MadeForCard", StringComparison.Ordinal) > text.IndexOf("x:Name=\"DashboardPage\"", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void The_Dashboard_has_one_big_button_and_the_pill_follows_the_state(string project)
    {
        var xaml = XDocument.Parse(XamlText(project));
        Assert.Equal("{StaticResource Keep.BtnState}", (string?)Named(xaml, "StartButton").Attribute("Style"));
        Assert.Equal("{StaticResource Keep.BtnStateStop}", (string?)Named(xaml, "StopButton").Attribute("Style"));
        Assert.Equal("Stop_Click", (string?)Named(xaml, "StopButton").Attribute("Click"));
        foreach (var name in new[] { "FirstRunSteps", "DashLog", "PasswordChip", "SteamChip", "RealmCard", "RealmLink", "PageBackdrop", "PageBackdropShade" })
            Named(xaml, name);
        var frame = SourceFiles.Read(Path.Combine("src", project, "MainWindow.Frame.cs"));
        var show = SourceFiles.Method(frame, "private void ShowFrame(");
        Assert.Contains("StatePill.Show(StatusKind.Online, \"Server online\");", show);
        Assert.Contains("ApplyModListBlock();", show);
        Assert.Contains("ShowFrame(running, serving, fatal != null);", SourceFiles.Method(SourceFiles.Read(Path.Combine("src", project, "MainWindow.State.cs")), "private void RefreshState()"));
        // Server lines on the Logs page also show on the Dashboard's Recent server output card.
        Assert.Contains("PushDashboardLines(line);", SourceFiles.Method(SourceFiles.Read(Path.Combine("src", project, "MainWindow.ServerLog.cs")), "private void NoteLog("));
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void The_mod_list_is_blocked_while_the_server_runs_or_starts(string project)
    {
        // A Start under way counts as running, as a pending restart does in the Coop Server Tool.
        Assert.True(ModListBlock.Running(serverRunning: false, modTestRunning: false, restartPending: true));
        var frame = SourceFiles.Read(Path.Combine("src", project, "MainWindow.Frame.cs"));
        Assert.Contains("ModListBlock.Running(_serverRunning, modTestRunning: false, restartPending: _startingServer || _busy)", frame);
        var block = SourceFiles.Method(frame, "private void ShowModListBlock(bool locked)");
        foreach (var control in new[] { "AddHostModsButton", "SaveHostModsButton", "AutoSortHostButton", "InstallHostModsButton", "ImportListItem" })
            Assert.Contains(control, block);
        Assert.Contains("ModsRunningBanner.Visibility = running", block);
        Assert.Contains("SetupRunningNote.Visibility = running", block);

        var source = SourceFiles.Window(SourceFiles.Root(), project);
        foreach (var handler in new[] { "AddHostMods_Click(", "private void MoveHostMod(", "private void RemoveHostMod_Click(", "private void ToggleHostMod_Click(", "private void SaveHostMods_Click(", "private void ImportModList_Click(", "private void AutoSortHost_Click(", "private async void InstallHostMods_Click(" })
            Assert.Contains("if (RefuseWhileModListRuns())", SourceFiles.Method(source, handler));
        Assert.Contains("Blocked = blocked,", SourceFiles.Method(source, "private void ShowModSetups()"));

        // The row switch and the move and remove buttons follow CanEdit (Editable and not blocked).
        var text = XamlText(project);
        Assert.Contains("<CheckBox Style=\"{StaticResource Keep.Switch}\" IsChecked=\"{Binding IsOn, Mode=OneWay}\" IsEnabled=\"{Binding CanEdit}\"", text);
        foreach (var click in new[] { "MoveHostModUp_Click", "MoveHostModDown_Click", "RemoveHostMod_Click" })
            Assert.Contains("IsEnabled=\"{Binding CanEdit}\" Click=\"" + click + "\"", text);
        var xaml = XDocument.Parse(text);
        var notices = Named(xaml, "ModsNotices");
        Assert.Equal("1", (string?)notices.Attribute("MaxShown"));
        Assert.Equal("ModsRunningBanner", (string?)notices.Elements().OrderBy(e => (int)e.Attribute(Ui + "BannerStack.Priority")!).First().Attribute(X + "Name"));
    }

    [Fact]
    public void Each_tool_has_its_backdrop_picture()
    {
        // TAOM: the dwarven gate (converted from webp, which WPF can't read). Burning Empires: the Coop castle for now.
        Assert.True(File.Exists(Path.Combine(SourceFiles.Root(), "src", "TaomCoopHost", "Assets", "redesign-backdrop-taom.jpg")));
        Assert.Contains(@"<Resource Include=""Assets\redesign-backdrop-taom.jpg"" />", SourceFiles.Read(Path.Combine("src", "TaomCoopHost", "TaomCoopHost.csproj")));
        Assert.Contains("pack://application:,,,/Assets/redesign-backdrop-taom.jpg", SourceFiles.Read(Path.Combine("src", "TaomCoopHost", "App.xaml.cs")));
        Assert.Contains(@"<Resource Include=""..\CoodszHost\Assets\redesign-backdrop-castle.jpg"" Link=""Assets\redesign-backdrop-castle.jpg"" />", SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "BurningEmpiresHost.csproj")));
        Assert.Contains("pack://application:,,,/Assets/redesign-backdrop-castle.jpg", SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "App.xaml.cs")));
        foreach (var project in new[] { "TaomCoopHost", "BurningEmpiresHost" })
            Assert.Contains("ImageSource=\"{DynamicResource Keep.BackdropArt}\"", XamlText(project));
    }
}
