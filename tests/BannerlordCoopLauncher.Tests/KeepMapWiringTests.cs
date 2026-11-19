using System.Xml.Linq;

namespace BannerlordCoopLauncher.Tests;

// CoopKeep part 7, phase 3: the live map page is wired into MainWindow, read only, Show on map
// works from the profile and the Encyclopedia, and the AD1259 build keeps its window as it was.
public sealed class KeepMapWiringTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static string Keep(string file) => File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Keep", file));

    [Fact]
    public void AD1259_keeps_its_window_the_map_starts_hidden()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        foreach (var name in new[] { "NavMap", "KeepMap" })
            Assert.Equal("Collapsed", (string?)Named(doc, name).Attribute("Visibility"));
        // The page buttons sit at the top of the sidebar list (wave 1 of the redesign), so no row count to fit.
        Assert.Equal("Top", (string?)Named(doc, "NavList").Attribute("VerticalAlignment"));

        // Only StartKeepPages shows it, and StartKeep returns before it in the AD1259 build.
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var pages = SourceFiles.Method(window, "private void StartKeepPages()");
        Assert.Contains("NavMap.Visibility = Visibility.Visible;", pages);
        Assert.Contains("StartMap();", pages);
        var start = SourceFiles.Method(window, "private void StartKeep()");
        Assert.True(start.IndexOf("return;", StringComparison.Ordinal) < start.IndexOf("StartKeepPages();", StringComparison.Ordinal));
        var shown = SourceFiles.Method(window, "private void OnKeepPageShown(string page)").Replace("\r", "");
        Assert.Contains("if (Edition.IsAD1259)\n            return;", shown);
        Assert.Contains("KeepMap.Shown();", shown);
        // Streamer mode reaches the map; ApplyStreamerMode returns first in the AD1259 build.
        var streamer = SourceFiles.Method(window, "private void ApplyStreamerMode()").Replace("\r", "");
        Assert.True(streamer.IndexOf("return;", StringComparison.Ordinal) < streamer.IndexOf("KeepMap.SetStreamer(on);", StringComparison.Ordinal));
    }

    [Fact]
    public void Map_is_a_page_with_a_nav_button_that_refreshes_only_while_open()
    {
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("(\"Map\", KeepMap)", window);
        Assert.Contains("NavMap, ", window);
        // A new server start reads the map again.
        Assert.Contains("KeepMap.ServerStarting();", window);
        // The one second clock reads the map only while the Map page is open.
        Assert.Contains("if (_page == \"Map\")\n            KeepMap.Shown();", SourceFiles.Method(window, "private void RefreshKeepPages()").Replace("\r", ""));
    }

    [Fact]
    public void Map_only_reads_and_has_no_teleport()
    {
        foreach (var file in new[] { "MapPage.xaml.cs", "MapCanvas.cs" })
        {
            var page = Keep(file);
            Assert.DoesNotContain("ChangeAsync(", page);
            Assert.DoesNotContain("ToolsCommand.", page);
            Assert.DoesNotContain("Teleport(", page, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Client.MapAsync(", Keep("MapPage.xaml.cs"));
    }

    [Fact]
    public void Show_on_map_works_from_the_profile_and_the_Encyclopedia()
    {
        var profile = XDocument.Load(Path.Combine(RepoRoot(), "src", "CoodszHost", "Keep", "PlayerProfileWindow.xaml"));
        var button = profile.Descendants().Single(e => (string?)e.Attribute("Content") == "Show on map");
        Assert.Null(button.Attribute("IsEnabled"));
        Assert.Equal("ShowOnMap_Click", (string?)button.Attribute("Click"));
        Assert.Contains("_keep.ShowOnMap(Target.HeroId, Target.Name);", Keep("PlayerProfileWindow.xaml.cs"));

        Assert.Contains("{Binding ShowOnMapText}", Keep("EncyclopediaPage.xaml"));
        Assert.Contains("MapRequested?.Invoke(", Keep("EncyclopediaPage.xaml.cs"));

        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var map = SourceFiles.Method(window, "private void StartMap()");
        Assert.Contains("keep.ShowOnMap = ShowHeroOnMap;", map);
        Assert.Contains("KeepEncyclopedia.MapRequested +=", map);
        Assert.Contains("KeepMap.FocusSettlement(id, name);", map);
        Assert.Contains("KeepEncyclopedia.OpenEntry(section, id);", map);
        Assert.Contains("StartRequested += () => Start_Click(this,", map);
        Assert.Contains("KeepMap.FocusHero(heroId, name);", SourceFiles.Method(window, "private void ShowHeroOnMap(string heroId, string name)"));
    }

    [Fact]
    public void Map_lists_are_not_walked_by_the_translator()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "CoodszHost", "Keep", "MapPage.xaml"));
        var lists = doc.Descendants().Where(e => e.Name.LocalName is "ItemsControl" or "ListBox").ToList();
        Assert.Equal(3, lists.Count);
        Assert.All(lists, list => Assert.Contains(list.AncestorsAndSelf(), e => (string?)e.Attribute("Tag") == "coodsz:no-translate"));
    }
}
