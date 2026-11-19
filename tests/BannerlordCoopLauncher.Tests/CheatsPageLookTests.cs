using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BannerlordCoopLauncher.Tests;

// The Cheats page, player profile and Items window of Coodsz Host (CoopKeep part 7): their shared
// look must load, and the AD1259 build must keep its earlier Cheats page.
public sealed class CheatsPageLookTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Host(params string[] parts) => Path.Combine(new[] { RepoRoot(), "src", "CoodszHost" }.Concat(parts).ToArray());

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static IReadOnlyList<string> Keys(string path) =>
        XDocument.Load(path).Root!.Elements().Select(e => (string?)e.Attribute(X + "Key") ?? "").ToList();

    // The styles (KeepTheme.xaml) and the colours (KeepColors.xaml, linked by every tool since redesign wave 6).
    private static IReadOnlyList<string> ThemeKeys() =>
        Keys(Host("Keep", "KeepTheme.xaml")).Concat(Keys(Host("Keep", "KeepColors.xaml"))).ToList();

    [Fact]
    public void Theme_has_no_key_twice()
    {
        // Two resources with one key stop the whole window from opening.
        var keys = ThemeKeys();
        Assert.Empty(keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.All(keys, key => Assert.StartsWith("Keep.", key));
    }

    [Fact]
    public void Every_Keep_resource_the_pages_use_is_in_the_theme()
    {
        var keys = ThemeKeys().ToHashSet(StringComparer.Ordinal);
        var taom = Path.Combine(RepoRoot(), "src", "TaomCoopHost");
        var files = Directory.GetFiles(Host("Keep"), "*.xaml").Concat(Directory.GetFiles(Host("Shared"), "*.xaml"))
            .Append(Host("MainWindow.xaml")).Append(Path.Combine(taom, "CoopCheatsPage.xaml")).Append(Path.Combine(taom, "MainWindow.xaml"));
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"StaticResource (Keep\.[A-Za-z]+)\}"))
                Assert.True(keys.Contains(m.Groups[1].Value), Path.GetFileName(file) + " uses " + m.Groups[1].Value + ", which the theme does not have.");
        }

        var code = Directory.GetFiles(Host("Keep"), "*.cs").Concat(Directory.GetFiles(Host("Shared"), "*.cs"))
            .Append(Path.Combine(taom, "CoopCheatsPage.xaml.cs")).Append(Path.Combine(taom, "MainWindow.Console.cs"));
        foreach (var file in code)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Keep\\.[A-Za-z]+)\""))
                Assert.True(keys.Contains(m.Groups[1].Value), Path.GetFileName(file) + " looks up " + m.Groups[1].Value + ", which the theme does not have.");
        }
    }

    [Fact]
    public void App_loads_the_theme_for_every_window()
    {
        // The colours first, then the styles that use them.
        var app = File.ReadAllText(Host("App.xaml"));
        Assert.True(app.IndexOf("Source=\"Keep/KeepColors.xaml\"", StringComparison.Ordinal) is >= 0 and var colours
                    && colours < app.IndexOf("Source=\"Keep/KeepTheme.xaml\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TAOM_and_Burning_Empires_tools_load_the_shared_Keep_colours_and_styles()
    {
        // Redesign wave 6: the two warm copies (TaomKeepColors.xaml) are gone; both tools link the one KeepColors.xaml.
        foreach (var name in new[] { "TaomCoopHost", "BurningEmpiresHost" })
        {
            var tool = Path.Combine(RepoRoot(), "src", name);
            Assert.False(File.Exists(Path.Combine(tool, "TaomKeepColors.xaml")), name);
            var app = File.ReadAllText(Path.Combine(tool, "App.xaml"));
            var colours = app.IndexOf("Source=\"Keep/KeepColors.xaml\"", StringComparison.Ordinal);
            Assert.True(colours >= 0 && colours < app.IndexOf("Source=\"Keep/KeepTheme.xaml\"", StringComparison.Ordinal), name);
            Assert.DoesNotContain("TaomKeepColors", app);
            var project = File.ReadAllText(Path.Combine(tool, name + ".csproj"));
            Assert.Contains(@"<Page Include=""..\CoodszHost\Keep\KeepColors.xaml"" Link=""Keep\KeepColors.xaml"" />", project);
            Assert.Contains(@"..\CoodszHost\Keep\KeepTheme.xaml", project);
            Assert.Contains(@"..\CoodszHost\Shared\CoopCommandBrowser.xaml", project);
            Assert.Contains(@"..\CoodszHost\Shared\CoopCommandBrowser.xaml.cs", project);
        }
    }

    [Fact]
    public void TAOM_Cheats_page_sends_only_Coop_commands()
    {
        // The TAOM server runs Coodsz Server Tools for the host rules only; this page sends Coop's own commands.
        var page = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopHost", "CoopCheatsPage.xaml.cs"));
        Assert.Contains("private const bool Tools = false;", page);
        Assert.Contains("PlayerCheats.In(category, target, Tools)", page);
        Assert.Contains("PlayerCheats.Favourites(favourites, target, Tools)", page);
        Assert.DoesNotContain("ToolsCommand", page);
        Assert.DoesNotContain("\"coodsz.", page);
        Assert.DoesNotContain("CoodszTools", string.Join("\n", Directory.GetFiles(Path.Combine(RepoRoot(), "src", "TaomCoopHost"), "*.cs").Select(File.ReadAllText)));
    }

    [Fact]
    public void AD1259_has_the_new_Cheats_page_without_server_tools_cheats_or_profile()
    {
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("(\"Cheats\", KeepCheats)", window);
        Assert.DoesNotContain("Edition.IsAD1259 ? CheatsPage", window);
        var start = SourceFiles.Method(window, "private void StartKeep()");
        // The AD1259 server loads Coodsz Server Tools for the host rules and settings sync, but its Cheats page stays on coop.debug.
        Assert.Contains("ToolsModule = CoodszServerTools.CheatsUseTools", start);
        // The profile, Home, Players, Map and Encyclopedia stay Coodsz Host only.
        var guard = start.IndexOf("Edition.IsAD1259", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < start.IndexOf("StartKeepPages()", StringComparison.Ordinal));
        Assert.Contains("OpenProfileButton.Visibility = Visibility.Collapsed", start);
        // Every start adds the tools module (both builds); the Encyclopedia and Map are never attached in AD1259.
        var startServer = SourceFiles.Method(window, "private void StartServer()");
        Assert.Contains("startModules = CoodszServerTools.WithTools(startModules);", startServer);
        var mapGuard = startServer.IndexOf("if (!Edition.IsAD1259)", StringComparison.Ordinal);
        Assert.True(mapGuard >= 0 && mapGuard < startServer.IndexOf("KeepMap.ServerStarting();", StringComparison.Ordinal));
        Assert.Contains("public static bool CheatsUseTools => !Edition.IsAD1259;", File.ReadAllLines(Host("Services", "CoodszServerTools.cs")).Select(l => l.Trim()));

        // The page filters the coodsz.* cheats, and asks nothing of the tools module, when it is not there.
        var page = File.ReadAllText(Host("Keep", "CheatsPage.xaml.cs"));
        Assert.Contains("PlayerCheats.In(category, target, Tools)", page);
        Assert.Contains("PlayerCheats.Favourites(favourites, target, Tools)", page);
        Assert.Contains("ToolsModule: true", page);
        Assert.Contains("!CanSend || !ToolsModule", File.ReadAllText(Host("Keep", "KeepContext.cs")));
    }

    [Fact]
    public void Pages_are_listed_once_so_a_new_page_is_one_line()
    {
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var show = SourceFiles.Method(window, "private void ShowPage(string page)");
        Assert.Contains("foreach (var (name, element) in Pages)", show);
        Assert.DoesNotContain("CheatsPage.Visibility", show);
    }

    [Fact]
    public void Pages_only_send_coodsz_commands_built_by_ToolsCommand()
    {
        foreach (var file in Directory.GetFiles(Host("Keep"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("\"coodsz.", text);
            Assert.DoesNotContain("\"coopkeep.", text);
        }
    }

    [Fact]
    public void Card_lists_are_not_walked_by_the_translator()
    {
        // Hundreds of item and troop names would be looked up on every layout pass.
        foreach (var name in new[] { "CheatsPage.xaml", "ItemsWindow.xaml", "PlayerProfileWindow.xaml" })
        {
            var doc = XDocument.Load(Host("Keep", name));
            var lists = doc.Descendants().Where(e => e.Name.LocalName == "ItemsControl" && e.Attribute("ItemTemplate") == null && e.Elements().Any(c => c.Name.LocalName == "ItemsControl.ItemTemplate")
                                                      || e.Name.LocalName == "ItemsControl" && e.Attribute("ItemTemplate") != null).ToList();
            Assert.NotEmpty(lists);
            Assert.All(lists, list => Assert.Equal("coodsz:no-translate", (string?)list.Attribute("Tag")));
        }
    }
}
