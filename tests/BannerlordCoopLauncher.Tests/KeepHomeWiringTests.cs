using System.Xml.Linq;

namespace BannerlordCoopLauncher.Tests;

// The Server Tool's Dashboard (redesign wave 2, once the CoopKeep Home page): one first page for both editions
// with the server buttons, the values Home had and the newest server lines; the old Server page is Saves; the
// Join rows and the folder and Windows tools are on Settings. Players, presets, the wizard, streamer mode and the
// filter chips stay Coodsz Host only, and the AD1259 build keeps them hidden.
public sealed class KeepHomeWiringTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument Window() => XDocument.Load(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));

    private static XElement Named(XContainer doc, string name) =>
        doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static bool Holds(XElement page, string name) =>
        page.Descendants().Any(e => (string?)e.Attribute(X + "Name") == name);

    [Fact]
    public void AD1259_keeps_its_window_the_new_parts_start_hidden()
    {
        // StartKeepPages (Coodsz Host only) shows them; the AD1259 build never calls it.
        var doc = Window();
        foreach (var name in new[] { "NavPlayers", "StreamerButton", "PresetCard", "ModFilterChips", "KeepPlayers", "AddressMask", "PasswordMask", "RealmRewardMask", "OnlinePlayersCard", "CreateWorldButton" })
            Assert.Equal("Collapsed", (string?)Named(doc, name).Attribute("Visibility"));
        // Both editions open on the Dashboard; the AD1259 build shows its player card there.
        Assert.Null(Named(doc, "NavDashboard").Attribute("Visibility"));
        Assert.Null(Named(doc, "PlayersCard").Attribute("Visibility"));
        // The page buttons sit at the top of the sidebar list (wave 1 of the redesign), so no row count to fit.
        Assert.Equal("Top", (string?)Named(doc, "NavList").Attribute("VerticalAlignment"));

        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var start = SourceFiles.Method(window, "private void StartKeep()");
        Assert.True(start.IndexOf("return;", StringComparison.Ordinal) < start.IndexOf("StartKeepPages();", StringComparison.Ordinal));
        Assert.Contains("private const string StartPage = \"Dashboard\";", window);
        Assert.DoesNotContain("\"Home\"", window);
        Assert.Contains("!Edition.IsAD1259 && _settings.StreamerMode", window);
        Assert.Contains("if (Edition.IsAD1259)", SourceFiles.Method(window, "private void ApplyStreamerMode()"));
    }

    [Fact]
    public void Dashboard_and_Saves_are_pages_and_the_Home_page_is_gone()
    {
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("(\"Dashboard\", DashboardPage)", window);
        // The Saves page keeps the key Server, so every ShowPage("Server") still lands on it.
        Assert.Contains("(\"Server\", ServerPage)", window);
        Assert.Contains("\"Server\" => \"Saves\"", SourceFiles.Method(window, "private static string PageTitleOf("));
        Assert.Contains("(\"Players\", KeepPlayers)", window);
        Assert.DoesNotContain("KeepHome", window);
        Assert.DoesNotContain("OnHomeCommand", window);
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "src", "CoodszHost", "Keep", "HomePage.xaml")));
        // The new world wizard goes back to the Dashboard.
        Assert.Contains("ShowPage(\"Dashboard\");", SourceFiles.Method(window, "private void CreateWorld(NewWorldPlan plan)"));
        // No setup bar under the header: the setup tools and Rename are on Saves.
        var doc = Window();
        Assert.DoesNotContain(doc.Descendants(), e => (string?)e.Attribute(X + "Name") == "SetupBar");
        Assert.Equal("Saves", (string?)Named(doc, "NavServer").Attribute("Content"));
        Assert.Equal("Server", (string?)Named(doc, "NavServer").Attribute("Tag"));
    }

    [Fact]
    public void Every_control_moved_whole_to_its_new_page()
    {
        var doc = Window();
        var dashboard = Named(doc, "DashboardPage");
        foreach (var name in new[]
                 {
                     "StartButton", "StopButton", "SaveWorldButton", "RestartButton", "LastGoodButton", "BackupNowButton", "StopAutoRestartButton",
                     "ControlStatus", "CrashRestartBox", "SendLogsLinkSlot", "ServerSaveMods", "OverviewSave", "OverviewPorts", "ServerLoadText",
                     "GameSpeedArea", "GameTimeStatus", "JoinAddressList", "JoinBestText", "RouterPortBox", "FirewallButton", "PasswordChip", "SteamChip",
                     "DashUptime", "DashLastSave", "DashLastBackup", "DashAutosave", "DashChips", "DashLog", "FirstRunSteps", "PlayersCard",
                     "OnlinePlayersCard", "DashActiveCoop", "RealmCard",
                 })
            Assert.True(Holds(dashboard, name), name + " is on the Dashboard.");

        var saves = Named(doc, "ServerPage");
        foreach (var name in new[] { "SaveBox", "NewSaveNameBox", "MapBox", "SaveFacts", "MoreNationsSaveInfo", "ProfileNameBox", "NewSetupButton", "CopySetupButton", "SaveSetupButton", "DeleteSetupButton", "CreateWorldButton" })
            Assert.True(Holds(saves, name), name + " is on Saves.");

        // The Join rows moved whole with their masks; the folder and Windows tools are in collapsed groups.
        var settings = Named(doc, "SettingsPage");
        foreach (var name in new[] { "EnginePortBox", "RegionBox", "ServerAddressBox", "AddressMask", "PasswordBox", "PasswordMask", "ServerUdpBox", "SteamBox", "UnblockButton", "ServerFolderPanel", "WindowsPanel" })
            Assert.True(Holds(settings, name), name + " is on Settings.");
        Assert.Equal("Collapsed", (string?)Named(doc, "ServerFolderPanel").Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)Named(doc, "WindowsPanel").Attribute("Visibility"));
        Assert.Contains(settings.Descendants(), e => (string?)e.Attribute("Click") == "SetServerFolder_Click");
        Assert.Contains(settings.Descendants(), e => (string?)e.Attribute("Click") == "FixAntivirus_Click");
        Assert.DoesNotContain(dashboard.Descendants(), e => (string?)e.Attribute("Click") is "SetServerFolder_Click" or "Unblock_Click" or "FixAntivirus_Click" or "Logs_Click");
        // Open server logs is on the Logs page.
        Assert.Contains(Named(doc, "LogsPage").Descendants(), e => (string?)e.Attribute("Click") == "Logs_Click");

        // Every button on the Dashboard and Saves explains itself.
        foreach (var page in new[] { dashboard, saves })
            Assert.All(page.Descendants().Where(e => e.Name.LocalName is "Button" or "CheckBox"), button => Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("ToolTip")), (string?)button.Attribute("Content")));
    }

    [Fact]
    public void Start_stays_on_the_Dashboard_and_crash_paths_still_open_Logs()
    {
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.DoesNotContain("ShowPage(\"Logs\")", SourceFiles.Method(window, "private void Start_Click("));
        Assert.DoesNotContain("Logs page", (string?)Named(Window(), "StartButton").Attribute("ToolTip"));
        Assert.Contains("ShowPage(\"Logs\")", SourceFiles.Method(window, "private async void RestartLastGood_Click("));
        Assert.Contains("ShowPage(\"Logs\")", SourceFiles.Method(window, "private void ShowLogSamples("));
        // Recent server output gets every line the Logs page draws.
        Assert.Contains("PushDashboardLine(entry.Kind, entry.Message, entry.Tone);", SourceFiles.Method(window, "private void Append(string line)"));
    }

    [Fact]
    public void The_picture_sits_behind_the_pages_under_a_dark_gradient_and_the_cards_stay_near_solid()
    {
        var doc = Window();
        // Behind every page, first in the page grid, from the one per tool resource.
        var backdrop = Named(doc, "PageBackdrop");
        var brush = backdrop.Descendants().Single(e => e.Name.LocalName == "ImageBrush");
        Assert.Equal("{DynamicResource Keep.BackdropArt}", (string?)brush.Attribute("ImageSource"));
        Assert.Equal("UniformToFill", (string?)brush.Attribute("Stretch"));
        Assert.Equal("HighQuality", (string?)backdrop.Attribute("RenderOptions.BitmapScalingMode"));
        Assert.Same(backdrop, Named(doc, "ServerPage").Parent!.Elements().First());
        var stops = Named(doc, "PageBackdropShade").Descendants().Where(e => e.Name.LocalName == "GradientStop").Select(e => (string?)e.Attribute("Color")).ToList();
        Assert.Equal(new[] { "{DynamicResource Keep.BackdropOverlayTop}", "{DynamicResource Keep.BackdropOverlayBottom}" }, stops);
        var theme = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Keep", "KeepTheme.xaml"));
        // The picture shows clearly (25% at the top, sharp); the near solid cards carry the text contrast.
        Assert.Contains("<Color x:Key=\"Keep.BackdropOverlayTop\">#400E1418</Color>", theme);
        Assert.Contains("<sys:Double x:Key=\"Keep.BackdropBlur\">0</sys:Double>", theme);
        Assert.Contains("Value=\"#F0151D24\"", File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml")));
        // Both editions show the castle until AD1259 has its own art: one file and Edition.BackdropArt.
        Assert.Contains("Backdrop.Apply(Resources, new Uri(\"pack://application:,,,/Assets/\" + CoodszHost.Services.Edition.BackdropArt));",
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "App.xaml.cs")));
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "src", "CoodszHost", "Assets", "redesign-backdrop-castle.jpg")));
        Assert.DoesNotContain("redesign-backdrop-castle", File.ReadAllText(Path.Combine(RepoRoot(), "src", "AD1259Host", "AD1259Host.csproj")));
    }

    [Fact]
    public void The_big_button_follows_the_state_rules()
    {
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var show = SourceFiles.Method(window, "private void ShowServerActions(HomeFacts facts)");
        Assert.Contains("HomeDashboard.Primary(state)", show);
        Assert.Contains("HomeDashboard.Secondary(state, CrashRestartWaits)", show);
        Assert.Contains("!_controlBusy", show);
        // Crashed lasts until the next start; Restart and Stop mark their pending states.
        Assert.Contains("_crashedSinceStart = !info.WasExpected;", SourceFiles.Method(window, "private void OnServerExited("));
        Assert.Contains("_crashedSinceStart = false;", SourceFiles.Method(window, "private void StartServer("));
        Assert.Contains("_restartingNow = true;", SourceFiles.Method(window, "private async Task<bool> RestartServerAsync("));
        Assert.Contains("_stoppingNow = true;", SourceFiles.Method(window, "private async void Stop_Click("));
    }

    [Fact]
    public void The_wizard_creates_worlds_with_the_existing_save_code()
    {
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var create = SourceFiles.Method(window, "private void CreateWorld(NewWorldPlan plan)");
        Assert.Contains("NewSave_Click(", create);
        Assert.Contains("LoadSinglePlayerSave_Click(", create);
        Assert.Contains("KeepSaveProfile(name, plan.Values)", create);
        Assert.Contains("WriteServerPassword(plan.Password)", create);
    }

    [Fact]
    public void New_lists_are_not_walked_by_the_translator()
    {
        var players = XDocument.Load(Path.Combine(RepoRoot(), "src", "CoodszHost", "Keep", "PlayersPage.xaml"));
        var lists = players.Descendants().Where(e => e.Name.LocalName == "ItemsControl").ToList();
        Assert.NotEmpty(lists);
        Assert.All(lists, list => Assert.Equal("coodsz:no-translate", (string?)list.Attribute("Tag")));
        // Player names and the preset tags on the Dashboard (the tags are translated before they go in).
        var doc = Window();
        foreach (var name in new[] { "DashChips", "DashPlayers" })
            Assert.Equal("coodsz:no-translate", (string?)Named(doc, name).Attribute("Tag"));
    }
}
