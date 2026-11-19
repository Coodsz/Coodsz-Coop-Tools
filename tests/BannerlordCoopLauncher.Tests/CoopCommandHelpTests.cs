using BannerlordCoopLauncher.Core.Keep;

namespace BannerlordCoopLauncher.Tests;

// The "How to use" guide of the Console page: every parameter of every Coop command has help with
// an example, every command gets a plain sentence and an example line, and Find ids only runs real
// SERVER or BOTH list commands that need no values.
public sealed class CoopCommandHelpTests
{
    [Fact]
    public void Every_parameter_in_the_catalog_gets_help_with_an_example()
    {
        var names = CoopCommandCatalog.All.SelectMany(c => c.Parameters).Select(p => p.Name).Distinct().ToList();
        Assert.True(names.Count > 150);
        var missing = names.Where(n => CoopCommandHelp.Known(n) == null).ToList();
        Assert.True(missing.Count == 0, "No help for " + string.Join(", ", missing));
        Assert.All(names, n =>
        {
            var help = CoopCommandHelp.Known(n)!;
            Assert.False(string.IsNullOrWhiteSpace(help.What), n);
            Assert.False(string.IsNullOrWhiteSpace(help.Example), n);
            Assert.False(string.IsNullOrWhiteSpace(help.Where), n);
            Assert.DoesNotContain(":", help.What);
        });
    }

    [Fact]
    public void Every_command_gets_a_plain_sentence_and_an_example_line_that_passes_its_own_check()
    {
        Assert.All(CoopCommandCatalog.All, c =>
        {
            var guide = CoopCommandHelp.Guide(c);
            Assert.False(string.IsNullOrWhiteSpace(guide.Plain), c.Name);
            Assert.EndsWith(".", guide.Plain);
            Assert.StartsWith(c.Name, guide.ExampleLine);
            Assert.Null(CoopCommandCatalog.Problem(c, guide.ExampleValues));
            Assert.Equal(c.Parameters.Count, guide.Parameters.Count);
            if (!c.IsClientOnly)
                Assert.Null(CoopCommandCatalog.CheckLine(guide.ExampleLine));
        });
    }

    [Fact]
    public void Thin_site_descriptions_are_replaced()
    {
        var focus = CoopCommandCatalog.Find("coop.debug.hero_developer.add_focus_points")!;
        Assert.Equal("Adds unspent focus points to a hero.", CoopCommandHelp.Plain(focus));
        Assert.Equal("Adds focus points to a hero.", CoopCommandHelp.FromName("coop.debug.hero_developer.add_focus_points"));
        Assert.Equal("Sets loyalty of a town.", CoopCommandHelp.FromName("coop.debug.town.set_loyalty"));
        Assert.Equal("Lists every location with its id.", CoopCommandHelp.FromName("coop.debug.location.list"));
        Assert.DoesNotContain(CoopCommandCatalog.All, c => CoopCommandHelp.Plain(c).StartsWith("Runs the ", StringComparison.Ordinal));
        Assert.DoesNotContain(CoopCommandCatalog.All, c => CoopCommandHelp.Plain(c).Contains("for co-op debugging", StringComparison.Ordinal));
    }

    [Fact]
    public void Examples_use_the_known_player()
    {
        var focus = CoopCommandCatalog.Find("coop.debug.hero_developer.add_focus_points")!;
        Assert.Equal("coop.debug.hero_developer.add_focus_points Munim 5", CoopCommandHelp.Guide(focus, "Munim").ExampleLine);
        Assert.Equal("coop.debug.hero_developer.add_focus_points \"Big Bob\" 5", CoopCommandHelp.Guide(focus, "Big Bob").ExampleLine);
        var vassal = CoopCommandCatalog.Find("coop.debug.kingdom.force_player_vassalage")!;
        Assert.Equal("coop.debug.kingdom.force_player_vassalage 76561198000000007 empire", CoopCommandHelp.Guide(vassal, controllerId: "76561198000000007").ExampleLine);
        var relation = CoopCommandCatalog.Find("coop.debug.hero.set_relation")!;
        Assert.Equal("coop.debug.hero.set_relation hero_7 hero_7 100", CoopCommandHelp.Guide(relation, heroId: "hero_7").ExampleLine);
    }

    [Fact]
    public void Find_ids_runs_real_list_commands()
    {
        var gold = CoopCommandCatalog.Find("coop.debug.hero.set_gold")!;
        Assert.Equal(new[] { CoopCommandHelp.HeroList }, CoopCommandHelp.FindCommands(gold));
        var vassal = CoopCommandCatalog.Find("coop.debug.kingdom.force_player_vassalage")!;
        Assert.Equal(new[] { CoopCommandHelp.PlayerList, CoopCommandHelp.KingdomList }, CoopCommandHelp.FindCommands(vassal));
        Assert.Equal(new[] { CoopCommandHelp.TownList }, CoopCommandHelp.FindCommands(CoopCommandCatalog.Find("coop.debug.town.set_prosperity")!));
        Assert.Empty(CoopCommandHelp.FindCommands(CoopCommandCatalog.Find("coop.debug.kingdom.list")!));

        var used = CoopCommandCatalog.All.SelectMany(c => CoopCommandHelp.FindCommands(c)).Distinct().ToList();
        Assert.Contains(CoopCommandHelp.VillageList, used);
        Assert.All(used, name =>
        {
            var info = CoopCommandCatalog.Find(name);
            Assert.True(info != null, name);
            Assert.False(info!.IsClientOnly, name);
            Assert.All(info.Parameters, p => Assert.True(p.Optional, name));
        });
    }
}
