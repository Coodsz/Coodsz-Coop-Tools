using System.Globalization;
using System.Xml.Linq;

namespace BannerlordCoopLauncher.Tests;

// BE #14 (CR-7DZWB): the Burning Empires Server Tool's Console output box was squashed to a thin line. Its row
// was 2 parts in 7 with a 170 minimum, and the recent command chips, the command box and the buttons below the
// box take about that much on their own, so the box got next to nothing. Every Server Tool had the same rows, so
// they all get the same fix. The CoodszHost window (the Coop and the AD1259 Server Tool) has its own rule: its output
// row minimum is the sum of what is docked in it and its command browser has none, pinned in HostPagesWave4Tests.
public sealed class ConsoleOutputHeightTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument Window(string host) => XDocument.Parse(SourceFiles.Read(Path.Combine("src", host, "MainWindow.xaml")));

    private static List<XElement> ConsoleRows(string host)
    {
        var page = Window(host).Descendants().Single(element => (string?)element.Attribute(X + "Name") == "ConsolePage");
        return page.Element(Wpf + "Grid.RowDefinitions")!.Elements(Wpf + "RowDefinition").ToList();
    }

    private static double Min(XElement row) => double.Parse((string?)row.Attribute("MinHeight") ?? "0", CultureInfo.InvariantCulture);

    private static double Stars(XElement row) => double.Parse(((string)row.Attribute("Height")!).TrimEnd('*') is { Length: > 0 } n ? n : "1", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("BurningEmpiresHost")]
    [InlineData("TaomCoopHost")]
    [InlineData("RealmOfThronesHost")]
    public void The_output_row_leaves_room_for_the_box_after_the_chips_command_box_and_buttons(string host)
    {
        var rows = ConsoleRows(host);
        Assert.Equal(4, rows.Count);
        // The sizes only make sense while the output box is in row 1 and the command browser in row 3.
        var page = Window(host).Descendants().Single(element => (string?)element.Attribute(X + "Name") == "ConsolePage");
        var outputCard = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "HistoryBox").Ancestors().First(element => element.Parent == page);
        Assert.Equal("1", (string?)outputCard.Attribute("Grid.Row"));
        var browser = page.Elements().Single(element => (string?)element.Attribute(X + "Name") == "CommandBrowser");
        Assert.Equal("3", (string?)browser.Attribute("Grid.Row"));
        // About 20 card padding, one or two rows of chips (about 40 to 70), the command box (about 44) and the buttons
        // (about 40), and then at least 100 for the output box itself.
        Assert.True(Min(rows[1]) >= 280, host + " output row minimum is " + Min(rows[1]));
        // The output row gets the bigger share, so it is readable at the default 900 high window too.
        Assert.True(Stars(rows[1]) > Stars(rows[3]), host + " output row " + rows[1].Attribute("Height") + ", command browser " + rows[3].Attribute("Height"));
    }

    [Theory]
    [InlineData("BurningEmpiresHost")]
    [InlineData("TaomCoopHost")]
    [InlineData("RealmOfThronesHost")]
    public void Both_minimums_still_fit_the_smallest_window(string host)
    {
        var rows = ConsoleRows(host);
        Assert.Equal("720", (string?)Window(host).Root!.Attribute("MinHeight"));
        // At 720 the page has about 455 for the two star rows (title bar, header, the top card and the splitter take the rest).
        Assert.True(Min(rows[1]) + Min(rows[3]) <= 455, host + " minimums add up to " + (Min(rows[1]) + Min(rows[3])));
        // The command browser's search box, Side pills and count line take about 130, so it needs room for its list too.
        Assert.True(Min(rows[3]) >= 175, host + " command browser minimum is " + Min(rows[3]));
    }
}
