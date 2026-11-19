using System.Xml.Linq;

namespace BannerlordCoopLauncher.Tests;

// The TAOM and Burning Empires server tools' page buttons on the left look like the Coop Server Tool's since redesign
// wave 6: the shared Keep.Nav style with an icon each, compact and at the top, with Saves keeping the Tag Server.
public sealed class BurningEmpiresSidebarTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Ui = "clr-namespace:Coodsz.Ui";

    private static XDocument Xaml(string project) => XDocument.Parse(SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml")));

    private static XElement Named(XDocument xaml, string name) =>
        xaml.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    [Theory]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void The_page_buttons_use_the_shared_nav_style_at_the_top(string project)
    {
        var xaml = Xaml(project);
        var panel = Named(xaml, "NavServer").Parent!;
        Assert.Equal("NavList", (string?)panel.Attribute(X + "Name"));
        Assert.Equal("Top", (string?)panel.Attribute("VerticalAlignment"));
        var buttons = panel.Elements(Wpf + "Button").ToList();
        Assert.Equal(new[] { "Dashboard", "Server", "Modules", "Settings", "Cheats", "Console", "Logs", "Guides" }, buttons.Select(b => (string?)b.Attribute("Tag")));
        Assert.All(buttons, b => Assert.Equal("{StaticResource Keep.Nav}", (string?)b.Attribute("Style")));
        Assert.All(buttons, b => Assert.StartsWith("{StaticResource Keep.Icon", (string?)b.Attribute(Ui + "NavRail.Icon")));
        Assert.All(buttons, b => Assert.False(string.IsNullOrWhiteSpace((string?)b.Attribute("ToolTip"))));
        Assert.Equal("Saves", (string?)Named(xaml, "NavServer").Attribute("Content"));
        Assert.Equal("Mods", (string?)Named(xaml, "NavModules").Attribute("Content"));

        // The old warm Nav style and the Join my server tile are gone; the sidebar is 220 wide, 64 under 1000 px.
        Assert.DoesNotContain(xaml.Descendants(Wpf + "Style"), style => (string?)style.Attribute(X + "Key") == "Nav");
        Assert.Equal("220", (string?)Named(xaml, "NavColumn").Attribute("Width"));
        Assert.Contains("Coodsz.Ui.NavRail.Fit(this, NavColumn, changed: ShowCompactSidebar);", SourceFiles.Window(SourceFiles.Root(), project));
    }
}
