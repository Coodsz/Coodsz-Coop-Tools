using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Coodsz.Ui;

namespace BannerlordCoopLauncher.Tests;

// Wave 1 of the tools redesign (docs/design/redesign/build-plan.md 1 and 2): the shared colours, chips and notice stack,
// and the Coop Server Tool's new sidebar and header.
public sealed class ToolFrameTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static Dictionary<string, string> Colours(string relative) =>
        XDocument.Parse(SourceFiles.Read(relative)).Root!.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => ((string)e.Attribute("Color")!).ToUpperInvariant());

    private static string Hex(string argbOrRgb) => "#" + argbOrRgb.TrimStart('#')[^6..].ToUpperInvariant();

    private static double Luminance(string hex)
    {
        var c = Hex(hex);
        double Channel(int at)
        {
            var v = int.Parse(c.Substring(at, 2), NumberStyles.HexNumber) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void Each_tool_accent_is_the_banner_fill_of_its_icon()
    {
        Assert.Equal(10, FrameRules.Accents.Count);
        foreach (var accent in FrameRules.Accents)
        {
            var svg = SourceFiles.Read(Path.Combine("assets", "icons", accent.Icon));
            var fill = Regex.Match(svg, "<path d=\"M24 15 [^\"]*\" fill=\"(#[0-9A-Fa-f]{6})\"").Groups[1].Value;
            Assert.Equal(fill.ToUpperInvariant(), accent.Deep.ToUpperInvariant());
        }
        // Every tool has its own colour, so no two tools look the same in the taskbar.
        Assert.Equal(FrameRules.Accents.Count, FrameRules.Accents.Select(accent => accent.Deep.ToUpperInvariant()).Distinct().Count());
    }

    [Fact]
    public void Each_accent_ink_reads_on_the_dark_window()
    {
        foreach (var accent in FrameRules.Accents)
        {
            Assert.True(Contrast(accent.Ink, "#0E1418") >= 4.5, accent.Tool + " ink on the window: " + Contrast(accent.Ink, "#0E1418"));
            Assert.True(Contrast(accent.Ink, "#0A1015") >= 4.5, accent.Tool + " ink on the sidebar: " + Contrast(accent.Ink, "#0A1015"));
        }
    }

    [Fact]
    public void The_colour_files_start_on_their_own_tool_accent()
    {
        var colours = Colours(Path.Combine("src", "CoodszHost", "Keep", "KeepColors.xaml"));
        Assert.Equal(FrameRules.Accent(FrameRules.CoopServer).Deep, Hex(colours["Keep.ToolAccent"]));
        Assert.Equal(FrameRules.Accent(FrameRules.CoopServer).Ink, Hex(colours["Keep.ToolAccentInk"]));
        // Since redesign wave 6 the TAOM and Burning Empires server tools share KeepColors.xaml and set their own accent
        // at startup: TAOM server #085041 / #84A8A0, Burning Empires server #D35400 / #E0874D.
        foreach (var (project, tool) in new[] { ("TaomCoopHost", nameof(FrameRules.TaomServer)), ("BurningEmpiresHost", nameof(FrameRules.BeServer)) })
        {
            Assert.False(File.Exists(Path.Combine(SourceFiles.Root(), "src", project, "TaomKeepColors.xaml")), project);
            Assert.Contains("Coodsz.Ui.ToolAccent.Apply(Resources, Coodsz.Ui.FrameRules." + tool + ");", SourceFiles.Read(Path.Combine("src", project, "App.xaml.cs")));
        }
        Assert.Equal(("#085041", "#84A8A0"), (FrameRules.Accent(FrameRules.TaomServer).Deep, FrameRules.Accent(FrameRules.TaomServer).Ink));
        Assert.Equal(("#D35400", "#E0874D"), (FrameRules.Accent(FrameRules.BeServer).Deep, FrameRules.Accent(FrameRules.BeServer).Ink));
        // An unknown name falls back to the Coop Server Tool, never throws.
        Assert.Equal(FrameRules.CoopServer, FrameRules.Accent("nope").Tool);
    }

    [Fact]
    public void Chip_kinds_use_Keep_colours_with_matching_fallbacks()
    {
        var colours = Colours(Path.Combine("src", "CoodszHost", "Keep", "KeepColors.xaml"));
        var inks = new HashSet<string>();
        foreach (var kind in Enum.GetValues<ChipKind>())
        {
            var (inkKey, inkFallback, backKey, backFallback) = FrameRules.Chip(kind);
            Assert.Equal(Hex(colours[inkKey]), inkFallback);
            Assert.Equal(Hex(colours[backKey]), backFallback);
            Assert.True(inks.Add(inkFallback), kind + " has the same colour as another kind");
            // The word sits on its own fill, so it has to read there too.
            Assert.True(Contrast(inkFallback, backFallback) >= 4.5, kind + ": " + Contrast(inkFallback, backFallback));
        }
        Assert.Equal("#8FCB9B", FrameRules.Chip(ChipKind.Ok).InkFallback);
        Assert.Equal("#E0A23A", FrameRules.Chip(ChipKind.Warn).InkFallback);
        Assert.Equal("#E2786C", FrameRules.Chip(ChipKind.Bad).InkFallback);
        Assert.Equal("#8EB4D4", FrameRules.Chip(ChipKind.Info).InkFallback);
        Assert.Equal("#E2C27A", FrameRules.Chip(ChipKind.Gold).InkFallback);
        Assert.Equal("Keep.Warning", FrameRules.Chip(ChipKind.Gold).InkKey);
    }

    [Fact]
    public void Realm_of_Thrones_has_its_own_black_green_and_red_colours()
    {
        var shared = Colours(Path.Combine("src", "CoodszHost", "Keep", "KeepColors.xaml"));
        var rot = Colours(Path.Combine("src", "RealmOfThronesHost", "Keep", "KeepColors.Rot.xaml"));
        // Every shared key under the shared file's name, so KeepTheme.xaml finds them all; only the RoT tools link it.
        Assert.Empty(shared.Keys.Except(rot.Keys));
        var host = SourceFiles.Read(Path.Combine("src", "RealmOfThronesHost", "RealmOfThronesHost.csproj"));
        var launcher = SourceFiles.Read(Path.Combine("src", "RealmOfThronesLauncher", "RealmOfThronesLauncher.csproj"));
        Assert.Contains(@"<Page Include=""Keep\KeepColors.Rot.xaml"" Link=""Keep\KeepColors.xaml"" />", host);
        Assert.Contains(@"<Page Include=""..\RealmOfThronesHost\Keep\KeepColors.Rot.xaml"" Link=""Keep\KeepColors.xaml"" />", launcher);
        Assert.DoesNotContain(@"CoodszHost\Keep\KeepColors.xaml", host + launcher);
        Assert.Contains(@"<Page Include=""..\CoodszHost\Keep\KeepColors.xaml"" Link=""Keep\KeepColors.xaml"" />", SourceFiles.Read(Path.Combine("src", "TaomCoopHost", "TaomCoopHost.csproj")));
        // The red edge is RoT's own; the other tools keep the tool colour on the selected page and a gold Join my server strip.
        Assert.False(shared.ContainsKey("Keep.ToolEdge"));
        Assert.Contains("Background=\"{DynamicResource Keep.ToolEdge}\"", SourceFiles.Read(Path.Combine("src", "CoodszHost", "Keep", "KeepTheme.xaml")));
        Assert.Contains("ToolAccent.OwnEdge ? ToolAccent.EdgeKey : \"Keep.GoldInk\"", SourceFiles.Read(Path.Combine("src", "CoodszHost", "Shared", "Frame", "RealmCard.cs")));
        // The approved look (theme D): coal black, green for go and links, red for the edge, small headings and Join on Discord, amber warnings.
        Assert.Equal(("#08090A", "#6CCB8A", "#C42E33", "#E0564E", "#B3262B", "#E3B341"),
            (Hex(rot["Keep.BgDeep"]), Hex(rot["Keep.GoldInk"]), Hex(rot["Keep.ToolEdge"]), Hex(rot["Keep.CardHeading"]), Hex(rot["Keep.SolidFill"]), Hex(rot["Keep.Warning"])));
        // Words read at 4.5:1 on the cards, the buttons and the sidebar, and every chip on its own fill.
        foreach (var key in new[] { "Keep.TextMain", "Keep.TextDim", "Keep.GoldInk", "Keep.CardHeading", "Keep.Red", "Keep.Amber", "Keep.WarningInk", "Keep.Teal", "Keep.Accent" })
            Assert.True(Contrast(rot[key], rot["Keep.BgPanel"]) >= 4.5, key + ": " + Contrast(rot[key], rot["Keep.BgPanel"]));
        Assert.True(Contrast(rot["Keep.OnSolid"], rot["Keep.SolidFill"]) >= 4.5);
        Assert.True(Contrast(rot["Keep.OnDanger"], rot["Keep.RedSoft"]) >= 4.5);
        Assert.True(Contrast("#FFFFFF", rot["Keep.GoFill"]) >= 4.5);
        Assert.True(Contrast(rot["Keep.NavText"], rot["Keep.SidebarBack"]) >= 4.5);
        Assert.True(Contrast(rot["Keep.DonateInk"], rot["Keep.DonateBack"]) >= 4.5);
        foreach (var kind in Enum.GetValues<ChipKind>())
        {
            var (inkKey, _, backKey, _) = FrameRules.Chip(kind);
            Assert.True(Contrast(rot[inkKey], rot[backKey]) >= 4.5, kind + ": " + Contrast(rot[inkKey], rot[backKey]));
        }
        // No blue grey left in the RoT windows: each of today's Keep colours is a key there now.
        foreach (var file in new[] { "RealmOfThronesHost/MainWindow.xaml", "RealmOfThronesHost/ConfirmWindow.xaml", "RealmOfThronesHost/App.xaml", "RealmOfThronesLauncher/MainWindow.xaml", "RealmOfThronesLauncher/App.xaml" })
        {
            var xaml = SourceFiles.Read(Path.Combine("src", file));
            foreach (var old in new[] { "#FFD5DEE6", "#F0151D24", "#FF151D24", "#FF3A4C5C", "#FF8EB4D4", "#FF1F4E79", "#FF10161B", "#FF8EA0AE", "#FFE2C27A", "#FF0E1418", "#FFEF9F27" })
                Assert.False(xaml.Contains(old, StringComparison.OrdinalIgnoreCase), file + " still has " + old);
        }
        // The dark stone picture under a near black gradient in both tools.
        Assert.True(File.Exists(Path.Combine(SourceFiles.Root(), "src", "RealmOfThronesHost", "Assets", "rot-backdrop-ember-green.jpg")));
        foreach (var project in new[] { "RealmOfThronesHost", "RealmOfThronesLauncher" })
        {
            var app = SourceFiles.Read(Path.Combine("src", project, "App.xaml.cs"));
            Assert.Contains("pack://application:,,,/Assets/rot-backdrop-ember-green.jpg", app);
            Assert.Contains("Resources[\"Keep.BackdropOverlayTop\"] = System.Windows.Media.Color.FromArgb(0x4D, 0x05, 0x06, 0x06);", app);
            Assert.Contains("Resources[\"Keep.BackdropOverlayBottom\"] = System.Windows.Media.Color.FromArgb(0xD1, 0x05, 0x06, 0x06);", app);
            Assert.DoesNotContain("redesign-backdrop-castle", SourceFiles.Read(Path.Combine("src", project, project + ".csproj")));
        }
    }

    [Fact]
    public void Header_states_have_the_agreed_colours()
    {
        Assert.Equal(ChipKind.Warn, FrameRules.Status(StatusKind.Stopped));
        Assert.Equal(ChipKind.Info, FrameRules.Status(StatusKind.Starting));
        Assert.Equal(ChipKind.Ok, FrameRules.Status(StatusKind.Online));
        Assert.Equal(ChipKind.Bad, FrameRules.Status(StatusKind.Crashed));
        Assert.Equal(ChipKind.Bad, FrameRules.Status(StatusKind.Refused));
        Assert.Equal(ChipKind.Mute, FrameRules.Status(StatusKind.NotReady));
    }

    [Fact]
    public void The_notice_stack_shows_the_two_most_urgent_and_counts_the_rest()
    {
        // File order of the Server Tool: UpdateNoticeBar 3, LogAlert 0, RescueNotice 2, ReplyBannerHost 4, CrashBarHost 1.
        var all = new List<(bool, int)> { (true, 3), (true, 0), (true, 2), (true, 4), (true, 1) };
        var (shown, hidden) = FrameRules.Banners(all);
        Assert.Equal(new[] { 1, 4 }, shown);
        Assert.Equal(3, hidden);

        var (open, none) = FrameRules.Banners(all, expanded: true);
        Assert.Equal(new[] { 1, 4, 2, 0, 3 }, open);
        Assert.Equal(0, none);

        // Hidden banners don't count; two or fewer need no chip.
        var (two, left) = FrameRules.Banners(new List<(bool, int)> { (false, 0), (true, 3), (false, 1), (true, 4) });
        Assert.Equal(new[] { 1, 3 }, two);
        Assert.Equal(0, left);

        // Same priority keeps file order.
        Assert.Equal(new[] { 0, 1 }, FrameRules.Banners(new List<(bool, int)> { (true, 5), (true, 5), (true, 5) }).Shown);
    }

    [Fact]
    public void Wave_1_review_fixes_hold()
    {
        string Frame(string file) => SourceFiles.Read(Path.Combine("src", "CoodszHost", "Shared", "Frame", file));
        // The stack listens to the static language event only while loaded, and a parked banner is out of Tab and UI Automation.
        var stack = Frame("BannerStack.cs");
        Assert.Contains("Unloaded += (_, _) => Translator.Changed -= LanguageChanged;", stack);
        Assert.DoesNotContain("Translator.Changed += () =>", stack);
        Assert.Contains("KeyboardNavigation.SetTabNavigation(child, KeyboardNavigationMode.None);", stack);
        Assert.Contains("AutomationProperties.SetIsOffscreenBehavior(child, IsOffscreenBehavior.Offscreen);", stack);
        Assert.Contains("child.ClearValue(AutomationProperties.IsOffscreenBehaviorProperty);", stack);
        // The pill compares the English text it painted, not the translated word.
        Assert.Contains("if (_painted == Text)", Frame("StatusPill.cs"));
        Assert.Contains("TimeSpan.FromSeconds(1)", Frame("HeaderClock.cs"));
        Assert.Contains("Translator.T(\"Move this mod up\")", Frame("MoveButtons.xaml.cs"));
        // My reports can be reached from the 64 px sidebar's More menu.
        var more = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private void CompactMore_Click(");
        Assert.Contains("FindMyReports()", more);
        Assert.Contains("ReportReplyText.MyReports", more);
        Assert.False(File.Exists(Path.Combine(SourceFiles.Root(), "src", "CoodszHost", "Assets", "calradia-backdrop.png")));
    }

    [Fact]
    public void The_notice_stack_never_changes_a_banner_s_visibility()
    {
        var stack = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Shared", "Frame", "BannerStack.cs"));
        Assert.Contains("FrameRules.Banners(", stack);
        Assert.DoesNotContain("child.Visibility =", stack);
        Assert.DoesNotContain("children[index].Visibility =", stack);

        var xaml = XDocument.Parse(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml")));
        var banners = xaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "Banners");
        var priority = XName.Get("BannerStack.Priority", "clr-namespace:Coodsz.Ui");
        var order = banners.Elements().ToDictionary(e => (string)e.Attribute(X + "Name")!, e => (int)e.Attribute(priority)!);
        Assert.Equal(new[] { "LogAlert", "CrashBarHost", "ReplyBannerHost", "AnnouncementBanner", "RescueNotice", "UpdateNoticeBar" }, order.OrderBy(p => p.Value).Select(p => p.Key));
    }

    [Fact]
    public void The_server_tool_uses_the_shared_frame()
    {
        var root = SourceFiles.Root();
        var xaml = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml"));
        var window = SourceFiles.Window(root, "CoodszHost");
        var doc = XDocument.Parse(xaml);
        string? Attr(string name, string attribute) => (string?)doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name).Attribute(attribute);

        // The page buttons, in the plan's order, each with an icon and a hover tip.
        var nav = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "NavList").Elements().ToList();
        Assert.Equal(new[] { "Dashboard", "Server", "Modules", "Settings", "Players", "Cheats", "Console", "Logs", "Map", "Encyclopedia", "Guides" }, nav.Select(b => (string)b.Attribute("Tag")!));
        Assert.All(nav, b => Assert.Equal("{StaticResource Keep.Nav}", (string?)b.Attribute("Style")));
        Assert.All(nav, b => Assert.StartsWith("{StaticResource Keep.Icon", (string?)b.Attribute(XName.Get("NavRail.Icon", "clr-namespace:Coodsz.Ui"))));
        Assert.Equal("Mods", Attr("NavModules", "Content"));
        // Saves keeps the Tag Server (wave 2), so ShowPage("Server") still opens it.
        Assert.Equal("Saves", Attr("NavServer", "Content"));

        // The selected look comes from Keep.Nav, not from colours in ShowPage; the header title follows the page.
        var show = SourceFiles.Method(window, "private void ShowPage(string page)");
        Assert.Contains("Coodsz.Ui.NavRail.Select(NavButtons, page);", show);
        Assert.Contains("PageTitle.Text = PageTitleOf(page);", show);
        Assert.DoesNotContain("Color.FromRgb", show);
        Assert.DoesNotContain("UpdateRealmNav", window);
        Assert.Equal("Realm", Attr("RealmLink", "Tag"));

        // The header pill follows RefreshState; the older state names stay for the code that sets them.
        Assert.Contains("ShowStatePill(running);", SourceFiles.Method(window, "private void RefreshState()"));
        foreach (var name in new[] { "StateText", "StatusDot", "StateDetail", "StatePill", "PageTitle", "ProfileBox", "LockedSetupTag", "LockedSetupText" })
            Assert.Contains("x:Name=\"" + name + "\"", xaml);
        Assert.Contains("<ui:HeaderClock", xaml);

        // A narrower window: about 900 wide, fitted to the screen, icons only under 1000.
        Assert.Equal("900", (string?)doc.Root!.Attribute("MinWidth"));
        Assert.Contains("WindowFit.Fit(new WindowSize(Width, Height, MinWidth, MinHeight)", window);
        Assert.Contains("Coodsz.Ui.NavRail.Fit(this, NavColumn, changed: ShowCompactSidebar);", window);

        // The update banner is steady, without the pulse.
        var update = doc.Descendants().Single(e => (string?)e.Attribute(X + "Key") == "UpdateInstall");
        Assert.DoesNotContain("Storyboard", update.ToString());

        // Each edition gets its own colour before the window opens.
        Assert.Contains("Coodsz.Ui.ToolAccent.Apply(Resources, CoodszHost.Services.Edition.IsAD1259 ? Coodsz.Ui.FrameRules.AD1259Server : Coodsz.Ui.FrameRules.CoopServer);", SourceFiles.Read(Path.Combine("src", "CoodszHost", "App.xaml.cs")));
    }

    [Fact]
    public void Every_tool_builds_the_shared_frame()
    {
        foreach (var project in new[] { "TaomCoopHost", "BurningEmpiresHost", "CoodszCoopAndFriends", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher" })
        {
            var csproj = SourceFiles.Read(Path.Combine("src", project, project + ".csproj"));
            Assert.Contains(@"<Page Include=""..\CoodszHost\Shared\Frame\*.xaml"" Link=""Shared\Frame\%(Filename)%(Extension)"" />", csproj);
            Assert.Contains(@"<Compile Include=""..\CoodszHost\Shared\Frame\*.cs"" Link=""Shared\Frame\%(Filename)%(Extension)"" />", csproj);
            // KeepTheme.xaml uses Keep colours, so a tool that builds it builds a colour file too.
            Assert.Contains(@"..\CoodszHost\Keep\KeepTheme.xaml", csproj);
        }
    }

    [Fact]
    public void The_frame_words_are_in_every_language()
    {
        var words = new[] { "Stopped", "Starting", "Stopping", "Server online", "Engine exited", "Show fewer notices", "1 more notice", "{0} more notices", "Move {0} up", "Move {0} down", "Recent server output", "Open full log", "Hides this notice." };
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            using var json = JsonDocument.Parse(SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json")));
            foreach (var word in words)
                Assert.True(json.RootElement.TryGetProperty(word, out var text) && text.GetString()!.Length > 0, language + " misses " + word);
        }
    }
}
