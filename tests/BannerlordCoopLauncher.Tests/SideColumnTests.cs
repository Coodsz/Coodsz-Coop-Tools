using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

// The redesign side column (D1): the Mods card counts the mods that are on in one group each, and the Made for card
// ticks a row when this PC or server has the version the tool pins.
public sealed class SideColumnTests
{
    [Fact]
    public void The_mods_card_counts_each_mod_that_is_on_in_one_group()
    {
        var tally = ModTally.Count(new[]
        {
            new TallyRow("Native", true, AlwaysOn: true),
            new TallyRow("Coop", true, AlwaysOn: true),
            // Coodsz module beats Always on, like the row chip.
            new TallyRow("DedicatedServer.CoodszJoinPassword", true, AlwaysOn: true),
            new TallyRow(AD1259Stack.CompatModuleId, true, AlwaysOn: true),
            new TallyRow("Bannerlord.Harmony", true, AlwaysOn: false, Works: true),
            new TallyRow("SomeMod", true, AlwaysOn: false),
            new TallyRow("OffMod", false, AlwaysOn: false, Works: true),
            // A second copy of a row counts once.
            new TallyRow("bannerlord.harmony", true, AlwaysOn: false, Works: true),
        });

        Assert.Equal(6, tally.On);
        Assert.Equal(2, tally.AlwaysOn);
        Assert.Equal(1, tally.Coodsz);
        Assert.Equal(1, tally.Bundled);
        Assert.Equal(1, tally.Works);
        Assert.Equal(1, tally.Other);
        Assert.Equal(0, ModTally.Count(Array.Empty<TallyRow>()).Other);
    }

    [Fact]
    public void Works_comes_from_the_community_list_and_skips_the_modules_badges_leave_out()
    {
        var db = ModCompatDatabase.Parse("""
            { "mods": [
              { "id": "Bannerlord.Harmony", "status": "Works" },
              { "id": "Broken.Mod", "status": "Broken" },
              { "id": "Coop", "status": "Works" }
            ] }
            """)!;

        Assert.True(CommunityCompat.Works("Bannerlord.Harmony", db));
        Assert.False(CommunityCompat.Works("Broken.Mod", db));
        Assert.False(CommunityCompat.Works("Unknown.Mod", db));
        Assert.False(CommunityCompat.Works("Coop", db));
        Assert.False(CommunityCompat.Works("Native", db));
        Assert.False(CommunityCompat.Works(null, db));
    }

    [Theory]
    [InlineData("v1.4.8", "v1.4.8", true, PinState.Match)]
    // The game's build number after the pin differs between the client and the server.
    [InlineData("v1.4.8", "v1.4.8.31234", true, PinState.Match)]
    [InlineData("v1.4.8", "v1.4.7", true, PinState.Mismatch)]
    [InlineData("v1.4.8", "v1.4", true, PinState.Mismatch)]
    [InlineData("v2.0.29.5", "v2.0.29.5", false, PinState.Match)]
    [InlineData("2.0.29.5", "v2.0.27", false, PinState.Mismatch)]
    [InlineData("v1.1.0", "1.1", false, PinState.Match)]
    // A mod's longer version is another release, not a build number.
    [InlineData("v0.1.6", "v0.1.6.1", false, PinState.Mismatch)]
    [InlineData("v0.1.6", "", false, PinState.Missing)]
    [InlineData("v0.1.6", null, false, PinState.Missing)]
    // No pin: the tool takes any version it finds.
    [InlineData(null, "v0.1.7", false, PinState.Match)]
    [InlineData(null, null, false, PinState.Missing)]
    public void A_made_for_row_matches_the_pin(string? pinned, string? found, bool baseGame, PinState expected) =>
        Assert.Equal(expected, VersionPins.Check(pinned, found, baseGame));

    [Fact]
    public void An_also_accepted_version_is_a_match_and_the_row_shows_the_found_version()
    {
        var coop = AD1259Stack.Find("Coop")!;
        var pin = new VersionPin("Coop", PinIcon.Coop, coop.Version, "v0.1.6", AlsoAccepted: coop.AlsoAccepted);

        Assert.Equal(PinState.Match, pin.State);
        Assert.Equal("0.1.6", pin.Shown);
        Assert.Equal(VersionPins.MatchTip, VersionPins.Tip(pin));
        Assert.Equal("0.1.7", new VersionPin("Coop", PinIcon.Coop, coop.Version, null).Shown);
        Assert.Equal(VersionPins.MissingTip, VersionPins.Tip(new VersionPin("Coop", PinIcon.Coop, coop.Version, null)));
        Assert.Equal(VersionPins.FoundTip, VersionPins.Tip(new VersionPin("Coop", PinIcon.Coop, null, "v0.1.7")));
        Assert.Equal(VersionPins.MismatchTip, VersionPins.Tip(new VersionPin("Coop", PinIcon.Coop, "v0.1.6", "v0.1.5")));
    }

    [Fact]
    public void A_stacked_play_page_puts_the_next_step_card_first()
    {
        var (main, side, realm) = Coodsz.Ui.FrameRules.PlaySlots(stack: true);
        Assert.Equal((0, 0), (side.Row, side.Column));
        Assert.Equal((1, 0, 1), (main.Row, main.Column, main.RowSpan));
        Assert.Equal((2, 0), (realm.Row, realm.Column));
        (main, side, realm) = Coodsz.Ui.FrameRules.PlaySlots(stack: false);
        Assert.Equal((0, 0, 2), (main.Row, main.Column, main.RowSpan));
        Assert.Equal((0, 1), (side.Row, side.Column));
        Assert.Equal((1, 1), (realm.Row, realm.Column));
        foreach (var project in new[] { "BannerlordCoopLauncher", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher", "RealmOfThronesLauncher" })
            Assert.Contains("Coodsz.Ui.PlayLayout.Fit(stack, PlayMain, PlaySide, RealmCard);",
                SourceFiles.Method(SourceFiles.Read(Path.Combine("src", project, "MainWindow.Frame.cs")), "private void FitPlay("));
    }

    [Theory]
    [InlineData("CoodszHost")]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    [InlineData("RealmOfThronesHost")]
    public void Every_server_tool_has_the_new_side_column(string project)
    {
        var xaml = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
        var side = xaml[xaml.IndexOf("x:Name=\"DashSide\"", StringComparison.Ordinal)..];
        side = side[..side.IndexOf("</StackPanel>\r\n                    </Grid>", StringComparison.Ordinal)];
        var mods = side.IndexOf("<ui:SideModsCard x:Name=\"ModsSummaryCard\">", StringComparison.Ordinal);
        var madeFor = side.IndexOf("<ui:MadeForCard x:Name=\"MadeForCard\"", StringComparison.Ordinal);
        var realm = side.IndexOf("<ui:RealmCard x:Name=\"RealmCard\">", StringComparison.Ordinal);
        Assert.True(mods >= 0 && mods < madeFor && madeFor < realm, "Mods, Made for, then Join my server");
        Assert.Contains("Content=\"Open Mods\" Tag=\"Modules\" Click=\"Nav_Click\"", side);
        Assert.Contains("Style=\"{StaticResource Keep.BtnSolidGold}\" Content=\"Join on Discord\" Click=\"RealmJoin_Click\"", side);
        Assert.Contains("Content=\"Learn more\" Tag=\"Realm\" Click=\"Nav_Click\"", side);
        Assert.Contains("ModsSummaryCard.Show(", SourceFiles.Window(SourceFiles.Root(), project));
        Assert.Contains("MadeForCard.Show(", SourceFiles.Window(SourceFiles.Root(), project));
    }

    [Fact]
    public void Orange_text_uses_the_lighter_orange_ink()
    {
        var colors = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Keep", "KeepColors.xaml"));
        Assert.Contains("<SolidColorBrush x:Key=\"Keep.OrangeInk\" Color=\"#FFF08A4B\"/>", colors);
        Assert.Contains("\"Keep.OrangeInk\"", SourceFiles.Read(Path.Combine("src", "CoodszHost", "Shared", "Frame", "SideModsCard.cs")));
    }
}
