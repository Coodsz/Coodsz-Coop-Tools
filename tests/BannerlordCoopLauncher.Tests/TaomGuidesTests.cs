using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace BannerlordCoopLauncher.Tests;

// The Guides pages of the TAOM server tool and the TAOM client launcher: their own guide sets,
// the text rules, the search, the translations and the way into the page from each window.
public sealed class TaomGuidesTests
{
    [Fact]
    public void Each_tool_loads_its_own_guides_in_order()
    {
        Assert.Equal(new[] { "getting-started", "inviting-friends", "saves", "backups-and-rescue", "extra-mods", "game-speed", "common-problems" },
            GuideLibrary.For(GuideSet.TaomServer).Select(g => g.Id));
        Assert.Equal(new[] { "getting-started", "joining-a-friend", "mods-and-setups", "common-problems" },
            GuideLibrary.For(GuideSet.TaomClient).Select(g => g.Id));
        // Coodsz Host keeps its own ten, and no TAOM only guide slips into them.
        Assert.Equal(10, GuideLibrary.All.Count);
        Assert.Same(GuideLibrary.All, GuideLibrary.For(GuideSet.CoodszHost));
        Assert.DoesNotContain(GuideLibrary.All, g => g.Id is "extra-mods" or "backups-and-rescue" or "joining-a-friend");
    }

    [Theory]
    [InlineData(GuideSet.TaomServer)]
    [InlineData(GuideSet.TaomClient)]
    public void Every_guide_has_text_and_keeps_to_the_house_style(GuideSet set)
    {
        foreach (var guide in GuideLibrary.For(set))
        {
            Assert.NotEmpty(guide.Title);
            Assert.NotEmpty(guide.Summary);
            Assert.NotEmpty(guide.Sections);
            Assert.All(guide.Sections, s =>
            {
                Assert.NotEmpty(s.Heading);
                Assert.NotEmpty(s.Steps);
            });
            foreach (var line in GuideLibrary.Lines(guide))
            {
                Assert.True(line.Length <= 160, "Too long for a step: " + line);
                Assert.False(char.IsLower(line[0]), "Starts with a small letter: " + line);
                Assert.DoesNotContain("{", line);
            }
        }
    }

    [Fact]
    public void Search_finds_the_right_guide()
    {
        var server = GuideLibrary.For(GuideSet.TaomServer);
        Assert.Equal(server.Count, GuideLibrary.Search(server, " ").Count);
        Assert.Equal("backups-and-rescue", GuideLibrary.Search(server, "rescue")[0].Id);
        Assert.Equal("game-speed", GuideLibrary.Search(server, "Game speed")[0].Id);
        Assert.Contains(GuideLibrary.Search(server, "auto sort"), g => g.Id == "extra-mods");
        Assert.Contains(GuideLibrary.Search(server, "shader"), g => g.Id == "common-problems");
        Assert.Empty(GuideLibrary.Search(server, "zzzz"));

        var client = GuideLibrary.For(GuideSet.TaomClient);
        Assert.Equal("joining-a-friend", GuideLibrary.Search(client, "Steam lobby")[0].Id);
        Assert.Contains(GuideLibrary.Search(client, "low fps"), g => g.Id == "common-problems");
        Assert.Contains(GuideLibrary.Search(client, "Import mod list"), g => g.Id == "mods-and-setups");
        // A word in the picked language finds the guide too.
        var de = Translator.Entries("de");
        var german = GuideLibrary.Search(client, "Freund", line => de.TryGetValue(line, out var shown) ? shown : line);
        Assert.Contains(german, g => g.Id == "joining-a-friend");
    }

    [Fact]
    public void Every_guide_line_is_translated()
    {
        // Every set, so a new tool's guides are checked too.
        var lines = Enum.GetValues<GuideSet>()
            .SelectMany(GuideLibrary.For).SelectMany(GuideLibrary.Lines).ToHashSet();
        foreach (var language in Translator.Languages.Where(l => l.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            var missing = lines.Where(line => !table.ContainsKey(line)).ToList();
            Assert.True(missing.Count == 0, language.Code + " is missing: " + string.Join(" | ", missing));
        }
    }

    [Fact]
    public void Server_tool_has_a_Guides_page_in_its_side_menu()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "TaomCoopHost", "MainWindow.xaml"));
        var nav = doc.Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "NavGuides");
        Assert.Equal("Guides", (string?)nav.Attribute("Tag"));
        Assert.Equal("Nav_Click", (string?)nav.Attribute("Click"));
        Assert.Contains(doc.Descendants(), e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "GuidesPage");
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopHost", "MainWindow.xaml.cs"));
        Assert.Contains("GuideSet.TaomServer", code);
        Assert.Contains("NavGuides }", code);
    }

    [Fact]
    public void Client_launcher_opens_its_guides_from_the_header()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "TaomCoopLauncher", "MainWindow.xaml"));
        var button = doc.Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "GuidesButton");
        Assert.Equal("Guides_Click", (string?)button.Attribute("Click"));
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopLauncher", "MainWindow.Guides.cs"));
        Assert.Contains("GuideSet.TaomClient", code);
        // The launcher builds the same page file as the server tool.
        Assert.Contains(@"..\TaomCoopHost\TaomGuidesPage.cs", File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopLauncher", "TaomCoopLauncher.csproj")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
