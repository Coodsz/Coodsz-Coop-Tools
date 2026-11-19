using System.Xml.Linq;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Tests;

// Steam moved many players to Bannerlord 1.5.7 on 2026-11-11. Every tool says so in one plain line (NewerGame) on its
// home page and next to Connect or Start server; only the AD1259 Client Launcher keeps Connect off, as it did before.
[Collection(TranslatorLanguage.Name)]
public sealed class NewerGameTests
{
    [Fact]
    public void Picking_the_game_folder_in_the_server_tool_updates_the_line()
    {
        // The tool may open with no game found; a 1.5.7 folder picked with Find Bannerlord shows the line at once (Codex).
        var keep = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.Keep.cs"));
        var save = keep[keep.IndexOf("KeepCheats.SaveGamePath = root =>", StringComparison.Ordinal)..];
        save = save[..save.IndexOf("};", StringComparison.Ordinal)];
        Assert.Contains("GameModulesCoop(out _newerGame)", save);
        Assert.Contains("ShowMadeFor()", save);
        // Run at once on the window thread, queued from any other, so it never waits on a busy window thread.
        Assert.Matches(@"if \(Dispatcher\.CheckAccess\(\)\)\s+Follow\(\);\s+else\s+Dispatcher\.BeginInvoke\(\(\) => Follow\(\)\);", save);
        Assert.DoesNotContain("Dispatcher.Invoke(", save);
    }

    [Fact]
    public void An_empty_newer_game_note_shows_no_blank_line_in_the_made_for_card()
    {
        // At 1.4.8 the tools pass "" as the first note; the card leaves blank notes out.
        var card = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "CoodszHost", "Shared", "Frame", "MadeForCard.cs")),
            "public void Show(IReadOnlyList<VersionPin> pins, IReadOnlyList<string>? notes = null)");
        Assert.Contains("foreach (var note in notes.Where(note => !string.IsNullOrWhiteSpace(note)))", card);
    }

    [Fact]
    public void The_line_shows_translated_with_the_version_in_every_language()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            foreach (var language in BannerlordCoopLauncher.Core.Localization.Translator.Languages.Where(language => language.Code != "en"))
            {
                BannerlordCoopLauncher.Core.Localization.Translator.Use(language.Code);
                foreach (var (line, english) in new[] { (NewerGame.Line("v1.5.7"), Line157), (NewerGame.Line("v1.5.7", realmOfThrones: true), RotLine157) })
                {
                    var shown = BannerlordCoopLauncher.Core.Localization.Translator.T(line);
                    Assert.NotEqual(english, shown);
                    Assert.Contains("1.5.7", shown);
                    Assert.Contains("1.4.8", shown);
                    Assert.DoesNotContain("{0}", shown);
                }
            }
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }

    private const string Line157 = "Bannerlord 1.5.7 isn't supported yet. The tools work on Bannerlord 1.4.8 with Coop 0.1.6 and 0.1.7. To switch back, right click Bannerlord in Steam, then Properties, Betas, and pick v1.4.8.";

    // The Realm of Thrones tools run on a Coop nightly build, so their line does not name Coop 0.1.6 and 0.1.7.
    private const string RotLine157 = "Bannerlord 1.5.7 isn't supported yet. Realm of Thrones in Coop runs on Bannerlord 1.4.8. To switch back, right click Bannerlord in Steam, then Properties, Betas, and pick v1.4.8.";

    [Fact]
    public void The_realm_of_thrones_tools_get_their_own_line()
    {
        Assert.Equal(RotLine157, NewerGame.Line("v1.5.7.106711", realmOfThrones: true));
        Assert.Null(NewerGame.Line("v1.4.8", realmOfThrones: true));
        Assert.Equal(Line157, NewerGame.Line("v1.5.7"));
        foreach (var project in new[] { "RealmOfThronesHost", "RealmOfThronesLauncher" })
            Assert.Contains("NewerGame.Notice(_gameRoot, realmOfThrones: true)",
                SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), project), "private void ShowMadeFor()"));
        // A game folder picked with Find Bannerlord shows in the Made for card at the next Dashboard refresh.
        Assert.Contains("_madeForRead = DateTime.MinValue;",
            SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesHost"), "private void ApplyGameFound(string root)"));
    }

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Theory]
    [InlineData("1.4.8", null)]
    [InlineData("v1.4.8", null)]
    [InlineData("v1.4.8.12345", null)]
    [InlineData("v1.4.7", null)]
    [InlineData("v1.5.7", "1.5.7")]
    [InlineData("v1.5.7.106711", "1.5.7")]
    [InlineData("1.5.0", "1.5.0")]
    [InlineData("v1.4.9", "1.4.9")]
    [InlineData("v2.0.0", "2.0.0")]
    [InlineData("garbage", null)]
    [InlineData("v1.5", null)]
    [InlineData("e1.5.7", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_a_game_newer_than_1_4_8_gets_the_line(string? version, string? shown)
    {
        Assert.Equal(shown == null ? null : Line157.Replace("1.5.7", shown), NewerGame.Line(version));
    }

    [Fact]
    public void The_line_reads_the_native_module_of_the_game_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "newer-game-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(NewerGame.Notice(null));
            Assert.Null(NewerGame.Notice(root));
            var native = Path.Combine(root, "Modules", "Native");
            Directory.CreateDirectory(native);
            var xml = Path.Combine(native, "SubModule.xml");
            File.WriteAllText(xml, "<Module><Id value=\"Native\"/><Version value=\"v1.5.7\"/></Module>");
            Assert.Equal(Line157, NewerGame.Notice(root));
            File.WriteAllText(xml, "<Module><Id value=\"Native\"/><Version value=\"v1.4.8\"/></Module>");
            Assert.Null(NewerGame.Notice(root));
            File.WriteAllText(xml, "not xml <<<");
            Assert.Null(NewerGame.Notice(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_ad1259_launcher_says_how_to_go_back_instead_of_update_the_tool()
    {
        var native = AD1259Stack.Find("Native")!;
        var status = new AD1259ModStatus(native, AD1259ModState.WrongVersion, "v1.5.7.106711", ["Modules\\Native"], []);
        Assert.Equal(Line157, status.Problem);
        Assert.DoesNotContain("Update the tool first", status.RowDetail);
        Assert.Equal("Needs v1.4.8, found v1.5.7.106711 · Bannerlord", status.RowDetail);
        // Five base game modules give the same line once, and they still keep Connect off.
        var window = SourceFiles.Window(SourceFiles.Root(), "AD1259Launcher");
        Assert.Contains(".Select(mod => mod.Problem!).Distinct().ToList()", window);
        Assert.Contains("ModProblems().Count == 0 && joinReady", window);
    }

    [Theory]
    [InlineData("BannerlordCoopLauncher", "ConnectButton")]
    [InlineData("AD1259Launcher", "ConnectButton")]
    [InlineData("TaomCoopLauncher", "ConnectButton")]
    [InlineData("BurningEmpiresLauncher", "ConnectButton")]
    [InlineData("RealmOfThronesLauncher", "ConnectButton")]
    [InlineData("CoodszHost", "StartButton")]
    [InlineData("TaomCoopHost", "StartButton")]
    [InlineData("BurningEmpiresHost", "StartButton")]
    [InlineData("RealmOfThronesHost", "StartButton")]
    public void Every_tool_shows_the_line_next_to_its_button_and_on_its_home_card(string project, string button)
    {
        var xaml = XDocument.Parse(SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml")));
        XElement Named(string name) => xaml.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);
        // The line sits in the same card as Connect or Start server.
        Assert.Same(Named(button).Ancestors(Wpf + "Border").First(), Named("NewerGameText").Ancestors(Wpf + "Border").First());

        var window = SourceFiles.Window(SourceFiles.Root(), project);
        Assert.Contains("NewerGame.Notice(", window);
        Assert.Contains("NewerGameText.Visibility = ", window);
        if (project == "BannerlordCoopLauncher")
        {
            // The Coop client's home is Play, where the line sits in the Next step card with Connect.
            Assert.Contains("NewerGame.Notice(_gameRoot)", SourceFiles.Method(window, "private async Task LoadPackAsync("));
            return;
        }

        // The rest also put it in the Made for card on their home page, first among its notes.
        var madeFor = SourceFiles.Method(window, "private void ShowMadeFor()");
        Assert.Contains(project == "CoodszHost" ? "new[] { _newerGame ?? \"\" }" : "newerGame ?? \"\"", madeFor);
        Assert.Contains("NewerGameText.Text = ", madeFor);
    }

    [Fact]
    public void The_server_tools_read_this_pcs_game_not_the_servers()
    {
        // The dedicated server's own Native is 1.4.8 from the Workshop, so the line reads the game the mods are copied from.
        Assert.Contains("NewerGame.Notice(Path.GetDirectoryName(modules))",
            SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private static string? GameModulesCoop(out string? newerGame)"));
        foreach (var project in new[] { "TaomCoopHost", "BurningEmpiresHost", "RealmOfThronesHost" })
            Assert.Contains("NewerGame.Notice(_gameRoot", SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), project), "private void ShowMadeFor()"));
    }
}
