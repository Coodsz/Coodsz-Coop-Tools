using System.Xml.Linq;

namespace BannerlordCoopLauncher.Tests;

// CoopKeep part 7, phase 2: the Encyclopedia and Guides pages are wired into MainWindow, read only,
// and the AD1259 build keeps its window as it was.
public sealed class KeepEncyclopediaWiringTests
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

    [Fact]
    public void AD1259_keeps_its_window_the_new_pages_start_hidden()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        foreach (var name in new[] { "NavEncyclopedia", "NavGuides", "KeepEncyclopedia", "KeepGuides" })
            Assert.Equal("Collapsed", (string?)Named(doc, name).Attribute("Visibility"));
        // The page buttons sit at the top of the sidebar list (wave 1 of the redesign), so no row count to fit.
        Assert.Equal("Top", (string?)Named(doc, "NavList").Attribute("VerticalAlignment"));

        // Only StartKeepPages shows them, and StartKeep returns before it in the AD1259 build.
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var pages = SourceFiles.Method(window, "private void StartKeepPages()");
        Assert.Contains("NavEncyclopedia.Visibility = Visibility.Visible;", pages);
        Assert.Contains("NavGuides.Visibility = Visibility.Visible;", pages);
        Assert.Contains("StartEncyclopedia();", pages);
        var start = SourceFiles.Method(window, "private void StartKeep()");
        Assert.True(start.IndexOf("return;", StringComparison.Ordinal) < start.IndexOf("StartKeepPages();", StringComparison.Ordinal));
        Assert.Contains("if (Edition.IsAD1259)\n            return;", SourceFiles.Method(window, "private void OnKeepPageShown(string page)").Replace("\r", ""));
    }

    [Fact]
    public void Encyclopedia_and_Guides_are_pages_with_nav_buttons()
    {
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("(\"Encyclopedia\", KeepEncyclopedia)", window);
        Assert.Contains("(\"Guides\", KeepGuides)", window);
        Assert.Contains("NavEncyclopedia, ", window);
        Assert.Contains("NavGuides }", window);
        // A new server start reads the world again.
        Assert.Contains("KeepEncyclopedia.ServerStarting();", window);
    }

    [Fact]
    public void Encyclopedia_only_reads_and_its_buttons_use_the_existing_windows()
    {
        var root = RepoRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "CoodszHost", "Keep", "EncyclopediaPage.xaml.cs"));
        Assert.Contains("Client.WorldAsync(", page);
        Assert.DoesNotContain("ChangeAsync(", page);
        Assert.DoesNotContain("ToolsCommand.", page);

        var window = SourceFiles.Window(root, "CoodszHost");
        var start = SourceFiles.Method(window, "private void StartEncyclopedia()");
        Assert.Contains("StartRequested += () => Start_Click(this,", start);
        Assert.Contains("new PlayerProfileWindow(keep,", window);
        Assert.Contains("KeepCheats.Pick(target);", SourceFiles.Method(window, "private async Task OpenHeroInCheats("));
        Assert.Contains("new ItemsWindow(keep, targets, itemName)", SourceFiles.Method(window, "private string OpenItemsFor(string itemName)"));
    }

    [Fact]
    public void New_lists_are_not_walked_by_the_translator()
    {
        foreach (var name in new[] { "EncyclopediaPage.xaml", "GuidesPage.xaml" })
        {
            var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "CoodszHost", "Keep", name));
            var lists = doc.Descendants().Where(e => e.Name.LocalName is "ItemsControl" or "ListBox" or "WrapPanel").ToList();
            Assert.NotEmpty(lists);
            // Each list sits inside (or is) an element the translator skips; the page translates its rows itself.
            Assert.All(lists, list => Assert.Contains(list.AncestorsAndSelf(), e => (string?)e.Attribute("Tag") == "coodsz:no-translate"));
        }
    }
}
