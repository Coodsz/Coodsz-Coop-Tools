using BannerlordCoopLauncher.Core.Keep;

namespace BannerlordCoopLauncher.Tests;

// The Console page's command list: made from the text of bannerlordcoop.com/cheats?tab=all
// (Fixtures/bannerlordcoop-cheats-2026-10-19.txt) and shipped as Keep/CoopCommands/coop-commands.json.
// To make the JSON again from a newer copy of the page, save its text over the fixture and run this
// test once with COODSZ_WRITE_COOP_COMMANDS=1.
public sealed class CoopCommandCatalogTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string PageText() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "tests", "BannerlordCoopLauncher.Tests", "Fixtures", "bannerlordcoop-cheats-2026-10-19.txt"));

    private static string JsonPath() =>
        Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Keep", "CoopCommands", "coop-commands.json");

    [Fact]
    public void Page_text_gives_every_command_with_its_side_and_type()
    {
        var commands = CoopCommandCatalog.ParsePageText(PageText());
        Assert.Equal(414, commands.Count);
        Assert.Equal(143, commands.Count(c => c.Side == "SERVER"));
        Assert.Equal(64, commands.Count(c => c.Side == "CLIENT"));
        Assert.Equal(207, commands.Count(c => c.Side == "BOTH"));
        Assert.All(commands, c => Assert.Contains(c.Type, new[] { "GAMEPLAY", "INSPECT", "AUDIT" }));
        Assert.All(commands, c => Assert.StartsWith(c.Name, c.Usage));

        var gold = commands.Single(c => c.Name == "coop.debug.hero.set_gold");
        Assert.Equal("Sets gold for every hero with an exact display name on the server.", gold.Description);
        Assert.Equal("coop.debug.hero.set_gold <hero_name> <gold>", gold.Usage);
        Assert.Equal("HEROES", gold.Category);
        Assert.Equal("Heroes", gold.CategoryName);
        Assert.Equal(new[] { new CoopParameter("hero_name", false), new CoopParameter("gold", false) }, gold.Parameters);

        var vassal = commands.Single(c => c.Name == "coop.debug.kingdom.force_player_vassalage");
        Assert.Equal(new[] { false, false, true }, vassal.Parameters.Select(p => p.Optional));
        Assert.Empty(commands.Single(c => c.Name == "coop.debug.kingdom.list").Parameters);
    }

    [Fact]
    public void Shipped_json_is_the_parsed_page()
    {
        var json = CoopCommandCatalog.ToJson(CoopCommandCatalog.ParsePageText(PageText()));
        if (Environment.GetEnvironmentVariable("COODSZ_WRITE_COOP_COMMANDS") == "1")
            File.WriteAllText(JsonPath(), json);
        Assert.Equal(json, File.ReadAllText(JsonPath()).Replace("\r\n", "\n"));
        Assert.Equal(414, CoopCommandCatalog.All.Count);
        Assert.NotNull(CoopCommandCatalog.Find("coop.debug.players.list"));
    }

    [Fact]
    public void Every_command_the_Cheats_page_sends_is_a_server_command_in_the_list()
    {
        foreach (var cheat in PlayerCheats.All.Where(c => !c.Tools))
        {
            var info = CoopCommandCatalog.Find(cheat.CoopCommand);
            Assert.True(info != null, cheat.CoopCommand + " is not in the list.");
            Assert.False(info!.IsClientOnly, cheat.CoopCommand);
        }

        foreach (var list in new[] { CoopReplies.PlayersCommand, CoopReplies.KingdomsCommand, CoopReplies.TownsCommand, CoopReplies.VillagesCommand, CoopReplies.HeroesCommand })
            Assert.False(CoopCommandCatalog.Find(list)!.IsClientOnly, list);
    }

    [Fact]
    public void Optional_parameters_are_read_from_brackets()
    {
        Assert.Equal(new[] { new CoopParameter("clan_id", false), new CoopParameter("count", false), new CoopParameter("parent", true) },
            CoopCommandCatalog.ParametersOf("coop.debug.clan.add_heirs <clan_id> <count> [<parent>]"));
        Assert.Equal(new[] { new CoopParameter("name_prefix", true) }, CoopCommandCatalog.ParametersOf("coop.debug.hero.list [<name_prefix>]"));
    }

    private static readonly CoopCommandInfo Gold = new("coop.debug.hero.set_gold", "", "coop.debug.hero.set_gold <hero_name> <gold>", "HEROES", "SERVER", "GAMEPLAY");
    private static readonly CoopCommandInfo Heirs = new("coop.debug.clan.add_heirs", "", "coop.debug.clan.add_heirs <clan_id> <count> [<parent>]", "CLANS", "SERVER", "GAMEPLAY");
    private static readonly CoopCommandInfo Who = new("coop.debug.mobile_party.who_am_i", "", "coop.debug.mobile_party.who_am_i", "MOBILE PARTIES", "CLIENT", "INSPECT");

    [Fact]
    public void Building_quotes_names_with_spaces_and_drops_empty_optional_values()
    {
        Assert.Equal("coop.debug.hero.set_gold \"Big Bob\" 500", CoopCommandCatalog.Build(Gold, new[] { "Big Bob", " 500 " }));
        Assert.Equal("coop.debug.clan.add_heirs clan_1 2", CoopCommandCatalog.Build(Heirs, new[] { "clan_1", "2", "" }));
        Assert.Equal("coop.debug.clan.add_heirs clan_1 2 lord_3", CoopCommandCatalog.Build(Heirs, new[] { "clan_1", "2", "lord_3" }));
        Assert.Equal("\"say \\\"hi\\\"\"", CoopArg.Quote("say \"hi\""));
        Assert.Equal(new[] { "coop.debug.hero.set_gold", "Big Bob", "500" }, CoopArg.Split("coop.debug.hero.set_gold \"Big Bob\" 500"));
        Assert.Equal(new[] { "a", "say \"hi\"" }, CoopArg.Split("a " + CoopArg.Quote("say \"hi\"")));
    }

    [Fact]
    public void A_missing_value_shows_the_usage_line()
    {
        Assert.Null(CoopCommandCatalog.Problem(Gold, new[] { "Bob", "10" }));
        Assert.Equal("Fill in gold. Usage coop.debug.hero.set_gold <hero_name> <gold>", CoopCommandCatalog.Problem(Gold, new[] { "Bob", " " }));
        Assert.Null(CoopCommandCatalog.Problem(Heirs, new[] { "c", "1", "" }));
    }

    [Fact]
    public void Typed_lines_are_checked_against_the_list()
    {
        var list = new[] { Gold, Heirs, Who };
        Assert.Null(CoopCommandCatalog.CheckLine("coop.debug.hero.set_gold \"Big Bob\" 10", list));
        Assert.Contains("Usage coop.debug.hero.set_gold <hero_name> <gold>", CoopCommandCatalog.CheckLine("coop.debug.hero.set_gold Bob", list));
        Assert.Contains("Put quotes around a name with spaces", CoopCommandCatalog.CheckLine("coop.debug.hero.set_gold Big Bob 10", list));
        Assert.Equal(CoopCommandCatalog.ClientOnlyNote, CoopCommandCatalog.CheckLine("coop.debug.mobile_party.who_am_i", list));
        Assert.Contains("no coop.debug.nope", CoopCommandCatalog.CheckLine("coop.debug.nope", list));
        Assert.Null(CoopCommandCatalog.CheckLine("save", list));
        Assert.Null(CoopCommandCatalog.CheckLine("coop.debug.clan.add_heirs c 1", list));
    }

    [Fact]
    public void Search_matches_every_word_in_one_category()
    {
        var list = new[] { Gold, Heirs, Who };
        Assert.Equal(new[] { Gold }, CoopCommandCatalog.Search(list, "hero gold"));
        Assert.Equal(new[] { Heirs }, CoopCommandCatalog.Search(list, "", "CLANS"));
        Assert.Empty(CoopCommandCatalog.Search(list, "gold", "CLANS"));
    }
}
