// Tests for the Coodsz Server Tools protocol, the Items and troop catalogs, the roster merge and the
// admin log, which port code from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// ServerToolsParser.cs, ServerItemCatalog.cs, CoopSession.cs and AuditLogStore.cs; some cases follow
// its ServerToolsParserTests.cs and ServerToolsTests.cs.

using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

public sealed class CoopKeepToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coodsz-keep-" + Guid.NewGuid().ToString("N"));

    public CoopKeepToolsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // The answers as the module writes them (compat\CoodszServerTools), with a log line mixed in.
    private static readonly string[] SkillsAnswer =
    {
        "[DedicatedServer] > coodsz.skills lord_1_1",
        "DEV|27|3558372|2|3",
        "ATTR|vigor|Vigor|4",
        "ATTR|social|Social|5",
        "SKILL|Leadership|Leadership|209|2|5000|social",
        "SKILL|Crafting|Smithing|158|1|0|endurance",
        "COODSZ_END",
    };

    [Fact]
    public void Skills_answer_reads_level_unspent_points_attributes_and_skills()
    {
        var dev = ToolsParser.ParseDevelopment(SkillsAnswer)!;
        Assert.Equal(27, dev.Level);
        Assert.Equal(3558372, dev.TotalXp);
        Assert.Equal(2, dev.UnspentAttributePoints);
        Assert.Equal(3, dev.UnspentFocusPoints);
        Assert.Equal(5, dev.Attributes.Single(a => a.Id == "social").Value);
        var leadership = dev.Skills.Single(s => s.Id == "Leadership");
        Assert.Equal((209, 2, 5000, "social"), (leadership.Value, leadership.Focus, leadership.XpProgress, leadership.AttributeId));
        Assert.True(ToolsParser.IsComplete(SkillsAnswer));
    }

    [Fact]
    public void Profile_answer_reads_every_stat_tile_and_the_header()
    {
        var profile = ToolsParser.ParseProfile(new[]
        {
            "HERO|lord_1_1|Lucon|27|160000|100|112|62.13|512|3200|5|200|3|clan_empire_north_1|Osticos|empire|Northern Empire|empire|Empire|town_EN1|Argoron|lord_1_1_party_1",
            "COODSZ_END",
        })!;
        Assert.Equal("Lucon", profile.Name);
        Assert.Equal((27, 160000, 100, 112), (profile.Level, profile.Gold, profile.HitPoints, profile.MaxHitPoints));
        Assert.Equal(62, (int)profile.Age);
        Assert.Equal((512, 3200, 5, 200, 3), (profile.Influence, profile.Renown, profile.ClanTier, profile.Troops, profile.Wounded));
        Assert.Equal(("Osticos", "Northern Empire", "Argoron"), (profile.ClanName, profile.KingdomName, profile.PlaceName));
        Assert.Equal("lord_1_1_party_1", profile.PartyId);
    }

    [Fact]
    public void Equipment_inventory_and_roster_answers_are_read()
    {
        var gear = ToolsParser.ParseEquipment(new[]
        {
            "EQ|battle|Weapon0|empire_lance_2_t4|Courser Lance|2200",
            "EQ|civilian|Body|imperial_robes|Toga|900",
            "COODSZ_END",
        });
        Assert.Equal(2, gear.Count);
        Assert.Equal(("civilian", "Body", "imperial_robes", 900), (gear[1].Set, gear[1].Slot, gear[1].ItemId, gear[1].Value));

        var inventory = ToolsParser.ParseInventory(new[] { "INV|grain|Grain|41|10", "COODSZ_END" });
        Assert.Equal(new InventoryItem("grain", "Grain", 41, 10), inventory.Single());

        var roster = ToolsParser.ParseRoster(new[]
        {
            "PARTY|lord_1_1_party_1|200|3",
            "TROOP|members|lord_1_1|Lucon|1|0|0|1|0|empire",
            "TROOP|members|imperial_recruit|Imperial Recruit|46|3|20|0|1|empire",
            "TROOP|prisoners|looter|Looter|5|0|0|0|0|looters",
            "COODSZ_END",
        })!;
        Assert.Equal((200, 3), (roster.Troops, roster.Wounded));
        Assert.Equal(2, roster.Members.Count);
        Assert.True(roster.Members[0].IsHero);
        Assert.Equal((46, 3, 1), (roster.Members[1].Number, roster.Members[1].Wounded, roster.Members[1].Tier));
        Assert.Equal("looter", roster.Prisoners.Single().Id);
    }

    [Fact]
    public void A_change_is_ok_only_with_the_value_the_module_read_back()
    {
        var ok = KeepClient.Interpret(new[] { "COODSZ_SET hero=lord_1_1 what=skill:Leadership old=209 new=219" });
        Assert.True(ok.Ok);
        Assert.Equal(("skill:Leadership", 209, 219), (ok.Change!.What, ok.Change.Old, ok.Change.New));

        var refused = KeepClient.Interpret(new[] { "COODSZ_ERROR hero 'lord_1_1' has no party." });
        Assert.False(refused.Ok);
        Assert.Contains("no party", refused.Message);

        var unknown = KeepClient.Interpret(new[] { "unknown command 'coodsz.gold'" });
        Assert.False(unknown.Ok);
        Assert.Equal(KeepClient.NoTools, unknown.Message);

        Assert.Equal(KeepClient.NoAnswer, KeepClient.Interpret(Array.Empty<string>()).Message);
    }

    [Fact]
    public void Commands_are_clamped_and_never_take_a_name_as_an_id()
    {
        Assert.Equal("coodsz.set_gold lord_1_1 10000000", ToolsCommand.SetGold("lord_1_1", int.MaxValue));
        Assert.Equal("coodsz.gold lord_1_1 -500", ToolsCommand.Gold("lord_1_1", -500));
        Assert.Equal("coodsz.health lord_1_1 1", ToolsCommand.Health("lord_1_1", -5));
        Assert.Equal("coodsz.age lord_1_1 90", ToolsCommand.Age("lord_1_1", 200));
        Assert.Equal("coodsz.skill lord_1_1 Leadership -10", ToolsCommand.Skill("lord_1_1", "Leadership", -10));
        Assert.Equal("coodsz.unspent lord_1_1 focus 1", ToolsCommand.Unspent("lord_1_1", false, 1));
        Assert.Equal("coodsz.troops lord_1_1 imperial_recruit -1000", ToolsCommand.Troops("lord_1_1", "imperial_recruit", -5000));
        Assert.Equal("coodsz.give_item lord_1_1 grain 10000", ToolsCommand.GiveItem("lord_1_1", "grain", 99999));
        Assert.Equal("coodsz.heroes Lucon the Bold", ToolsCommand.Heroes("Lucon \"the\" Bold"));
        Assert.Throws<ArgumentException>(() => ToolsCommand.Hero("John Smith"));
        Assert.Throws<ArgumentException>(() => ToolsCommand.Gold("lord_1_1", 0));
        Assert.Throws<ArgumentException>(() => ToolsCommand.Skill("lord_1_1", "Lead ership", 1));
        Assert.True(ToolsCommand.IsToolsCommand("coodsz.ping"));
    }

    [Fact]
    public void Items_fall_into_the_sidebar_categories()
    {
        GameItem Item(string type, string weaponClass = "", bool food = false, bool trade = false) =>
            new("x", "X", type, 10, 1, "", "", false, food, trade, weaponClass, 1);

        Assert.Equal(ItemGroup.Sword, Item("OneHandedWeapon", "OneHandedSword").Group);
        Assert.Equal(ItemGroup.Polearm, Item("Polearm", "TwoHandedPolearm").Group);
        Assert.Equal(ItemGroup.Thrown, Item("Thrown", "Javelin").Group);
        Assert.Equal(ItemGroup.Bow, Item("Sling").Group);
        Assert.Equal(ItemGroup.Ammo, Item("Bolts").Group);
        Assert.Equal(ItemGroup.Body, Item("BodyArmor").Group);
        Assert.Equal(ItemGroup.Harness, Item("HorseHarness").Group);
        Assert.Equal(ItemGroup.Food, Item("Goods", food: true).Group);
        Assert.Equal(ItemGroup.Goods, Item("Goods", trade: true).Group);
        Assert.Equal(ItemGroup.Other, Item("Book").Group);
        Assert.Equal("Spears and polearms", ItemCatalog.GroupName(ItemGroup.Polearm));
        Assert.Equal(20, ItemCatalog.Order.Count);
    }

    [Fact]
    public void Item_filter_searches_sorts_and_cuts_at_240()
    {
        var items = Enumerable.Range(1, 300)
            .Select(i => new GameItem("tunic_" + i, "Tunic " + i, "BodyArmor", i, i % 6, "empire", "Empire", i % 2 == 0, false, false, "", 1))
            .Append(new GameItem("sword", "Fine Sword", "OneHandedWeapon", 5000, 5, "vlandia", "Vlandia", false, false, false, "OneHandedSword", 1))
            .Append(new GameItem("banner", "Banner", "Banner", 1, 1, "", "", false, false, false, "", 1))
            .Append(new GameItem("tournament_sword", "Practice", "OneHandedWeapon", 1, 1, "", "", false, false, false, "OneHandedSword", 1))
            .ToList();
        var giveable = ItemCatalog.Giveable(items);
        Assert.DoesNotContain(giveable, i => i.Id is "banner" or "tournament_sword");

        var page = ItemCatalog.Filter(giveable, ItemGroup.Body, null, ItemSort.Cheapest, ItemWear.All);
        Assert.Equal(240, page.Shown.Count);
        Assert.Equal(300, page.Total);
        Assert.Equal("First 240 of 300; narrow the search", ItemCatalog.ResultText(page));
        Assert.Equal("tunic_1", page.Shown[0].Id);

        Assert.Equal("sword", ItemCatalog.Filter(giveable, null, null, ItemSort.MostExpensive, ItemWear.All).Shown[0].Id);
        Assert.Equal(150, ItemCatalog.Filter(giveable, ItemGroup.Body, null, ItemSort.Name, ItemWear.Civilian).Total);
        Assert.Equal("sword", ItemCatalog.Filter(giveable, null, "vlandia", ItemSort.Cheapest, ItemWear.All).Shown.Single().Id);
        Assert.Equal("tunic_12", ItemCatalog.Filter(giveable, null, "Tunic 12", ItemSort.Cheapest, ItemWear.All).Shown[0].Id);
        Assert.Equal(new[] { (ItemGroup.Sword, 1), (ItemGroup.Body, 300) }, ItemCatalog.Counts(giveable));
    }

    [Fact]
    public void Troop_types_fold_into_four_classes_and_filter_by_culture_class_and_name()
    {
        var troops = ToolsParser.ParseTroopTypes(new[]
        {
            "TT|imperial_recruit|Imperial Recruit|empire|Empire|1|Infantry|0|0",
            "TT|imperial_archer|Imperial Archer|empire|Empire|3|Ranged|0|1",
            "TT|aserai_mameluke|Aserai Mameluke Cavalry|aserai|Aserai|4|HeavyCavalry|1|0",
            "TT|khuzait_horse_archer|Khuzait Horse Archer|khuzait|Khuzait|4|HorseArcher|1|1",
            "COODSZ_END",
        });
        Assert.Equal(new[] { TroopClass.Infantry, TroopClass.Archer, TroopClass.Cavalry, TroopClass.HorseArcher }, troops.Select(t => t.Class));
        Assert.Equal(2, TroopCatalog.Filter(troops, "empire", null, null).Count);
        Assert.Equal("aserai_mameluke", TroopCatalog.Filter(troops, null, TroopClass.Cavalry, null).Single().Id);
        Assert.Equal("khuzait_horse_archer", TroopCatalog.Filter(troops, null, null, "horse").Single().Id);
        Assert.Equal(new[] { "Aserai", "Empire", "Khuzait" }, TroopCatalog.Cultures(troops).Select(c => c.Name));
        Assert.Equal("★★★", TroopCatalog.Stars(3));
        Assert.Equal("★", TroopCatalog.Stars(0));
        Assert.Equal("200 troops · 3 wounded", TroopCatalog.ArmyLine(200, 3));
    }

    [Fact]
    public async Task Lists_come_from_the_file_the_module_names_and_are_read_once()
    {
        var file = Path.Combine(_dir, "items.txt");
        File.WriteAllText(file, "ITEM|grain|Grain|Goods|10|0|||0|1|1||10\nITEM|banner_x|Banner|Banner|1|0|||0|0|0||1\nCOODSZ_END");
        var sent = new List<string>();
        var client = new KeepClient(command =>
        {
            sent.Add(command);
            return Task.FromResult<IReadOnlyList<string>>(new[] { "COODSZ_FILE|items|" + file + "|2", "COODSZ_END" });
        }, path => File.ReadAllText(path));

        var items = await client.ItemsAsync();
        Assert.Equal("grain", items.Single().Id);
        Assert.Equal(ItemGroup.Food, items.Single().Group);
        await client.ItemsAsync();
        Assert.Equal(new[] { "coodsz.items" }, sent);
    }

    [Fact]
    public void Roster_merges_server_heroes_the_save_json_and_live_players()
    {
        var json = Path.Combine(_dir, "camp.json");
        File.WriteAllText(json, """
            { "UniqueGameId": "camp", "Players": [
              { "ControllerId": "76561198000000010", "HeroId": "Hero_Player", "MobilePartyId": "MobileParty_Player", "ClanId": "Clan_Player" },
              { "ControllerId": "76561198000000009", "HeroId": "Hero_Player2863", "MobilePartyId": "MobileParty_Player438", "ClanId": "Clan_Player12" } ] }
            """);
        var session = KeepRoster.ReadSession(json);
        Assert.Equal(2, session.Count);
        Assert.Equal("Player2863", KeepRoster.GameId(session[1].HeroId));

        var heroes = new[]
        {
            new HeroSummary("Player", "Kaan", "Clan_Player", "Kaan's clan", "", 8400, 72, 100, 10, "p1", "", true),
            new HeroSummary("Player2863", "Lucon", "Clan_Player12", "Osticos", "Northern Empire", 160000, 100, 112, 27, "p2", "Argoron", true),
        };
        var rows = KeepRoster.Merge(new[] { new LivePlayerInfo("Lucon", ""), new LivePlayerInfo("Newcomer", "76561190000000001") }, session, heroes);
        Assert.Equal(new[] { "Lucon", "Newcomer", "Kaan" }, rows.Select(r => r.Name));
        Assert.True(rows[0].Online);
        Assert.Equal("76561198000000009", rows[0].SteamId);
        Assert.Equal("", rows[1].HeroId);
        Assert.Equal(new[] { "Newcomer" }, KeepRoster.Unmatched(rows));
        Assert.Equal("Steam ••••0009", KeepRoster.MaskedSteamId(rows[0].SteamId));
        Assert.Equal("", KeepRoster.MaskedSteamId(""));
        Assert.Empty(KeepRoster.ReadSession(Path.Combine(_dir, "missing.json")));
    }

    [Fact]
    public async Task Admin_log_keeps_entries_on_disk_and_undo_only_for_recent_ones()
    {
        var path = Path.Combine(_dir, "admin-log.json");
        var log = new AuditLog(path);
        var undone = 0;
        var first = log.Add("Lucon", "gold +1,000", "troops lost to a cheater", () => { undone++; return Task.CompletedTask; });
        for (var i = 0; i < AuditLog.MaxUndo; i++)
            log.Add("Kaan", "health set to 100", undo: () => Task.CompletedTask);

        Assert.False(log.CanUndo(first));
        Assert.False(await log.UndoAsync(first));
        Assert.Equal(0, undone);
        Assert.Equal("Lucon: gold +1,000 (troops lost to a cheater)", first.Line);

        var reread = new AuditLog(path);
        Assert.Equal(AuditLog.MaxUndo + 1, reread.Entries.Count);
        Assert.Equal("Lucon", reread.Entries[^1].Player);

        var latest = log.Entries[0];
        Assert.True(log.CanUndo(latest));
        Assert.True(await log.UndoAsync(latest));
        Assert.False(log.CanUndo(latest));

        File.WriteAllText(path, "{ broken");
        Assert.Empty(new AuditLog(path).Entries);
    }

    [Fact]
    public void Image_store_finds_pictures_by_game_id_only()
    {
        var store = new GameImageStore(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "item"));
        File.WriteAllBytes(Path.Combine(_dir, "item", "grain.png"), new byte[] { 1 });
        Assert.NotNull(store.Find(GameImageKind.Item, "grain"));
        Assert.Null(store.Find(GameImageKind.Troop, "grain"));
        Assert.Null(store.Find(GameImageKind.Item, "..\\grain"));
        Assert.Equal(1, store.Count(GameImageKind.Item));
        Assert.True(store.HasPictures);
        Assert.False(store.Finished);
    }

    [Fact]
    public void Skill_layout_matches_the_game_screen()
    {
        Assert.Equal(6, SkillLayout.Columns.Count);
        Assert.Equal(18, SkillLayout.Columns.Sum(c => c.Skills.Length));
        Assert.Equal(new[] { "VIG", "CTR", "END", "CNG", "SOC", "INT" }, SkillLayout.Columns.Select(c => c.Short));
        Assert.Equal("Smithing", SkillLayout.SkillName("Crafting"));
        Assert.Equal("+10", SkillLayout.Signed(10));
        Assert.Equal("-1", SkillLayout.Signed(-1));
    }

    [Fact]
    public void Tools_module_is_server_only_and_added_last_to_every_start()
    {
        Assert.StartsWith("DedicatedServer.", CoodszServerTools.ModuleId);
        Assert.True(CoopJoinRules.IsIgnored(CoodszServerTools.ModuleId));

        var rows = new List<ModuleRow>
        {
            new() { Id = "Native", Required = true },
            new() { Id = "Coop" },
            new() { Id = CoodszServerTools.ModuleId },
        };
        var start = CoodszServerTools.WithTools(rows);
        Assert.Equal(new[] { "Native", "Coop", CoodszServerTools.ModuleId }, start.Select(r => r.Id));
        Assert.True(start[^1].Enabled);
    }

    [Fact]
    public void Tools_module_is_copied_into_the_server_and_only_when_it_changed()
    {
        var payload = Path.Combine(_dir, "payload");
        Directory.CreateDirectory(Path.Combine(payload, "bin", "Win64_Shipping_Server"));
        File.WriteAllText(Path.Combine(payload, "SubModule.xml"), "<Module/>");
        File.WriteAllText(Path.Combine(payload, "bin", "Win64_Shipping_Server", "DedicatedServer.CoodszTools.dll"), "dll");
        var server = Path.Combine(_dir, "server");
        Directory.CreateDirectory(Path.Combine(server, "engine", "Modules"));

        Assert.NotNull(CoodszServerTools.Install(server, payload));
        Assert.True(File.Exists(Path.Combine(server, "engine", "Modules", CoodszServerTools.ModuleId, "bin", "Win64_Shipping_Server", "DedicatedServer.CoodszTools.dll")));
        Assert.Null(CoodszServerTools.Install(server, payload));
        Assert.Contains("missing", CoodszServerTools.Install(server, Path.Combine(_dir, "nothing")));
    }

    [Fact]
    public void Shipped_tools_module_never_goes_into_Coop_and_Modules_list_hides_it()
    {
        var root = RepoRoot();
        var payload = Path.Combine(root, "src", "CoodszHost", "ServerTools", CoodszServerTools.ModuleId);
        var xml = XDocument.Load(Path.Combine(payload, "SubModule.xml"));
        Assert.Equal(CoodszServerTools.ModuleId, xml.Root!.Element("Id")!.Attribute("value")!.Value);
        Assert.Equal(ToolsCommand.ModuleVersion, xml.Root!.Element("Version")!.Attribute("value")!.Value);
        Assert.True(File.Exists(Path.Combine(payload, "bin", "Win64_Shipping_Server", "DedicatedServer.CoodszTools.dll")));

        var project = File.ReadAllText(Path.Combine(root, "compat", "CoodszServerTools", "CoodszServerTools.csproj"));
        Assert.DoesNotContain("Modules\\Coop", project);
        Assert.Contains("<Private>false</Private>", project);

        var modules = Path.Combine(_dir, "server", "engine", "Modules");
        foreach (var id in new[] { "Native", CoodszServerTools.ModuleId })
        {
            Directory.CreateDirectory(Path.Combine(modules, id));
            File.WriteAllText(Path.Combine(modules, id, "SubModule.xml"),
                "<Module><Name value=\"" + id + "\"/><Id value=\"" + id + "\"/><Version value=\"v1.0.0\"/></Module>");
        }

        Assert.DoesNotContain(ModuleCatalog.Load(Path.Combine(_dir, "server")), row => row.Id == CoodszServerTools.ModuleId);
    }

    [Fact]
    public void Start_adds_the_tools_module_after_the_rescue_module_in_both_builds()
    {
        var source = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var start = SourceFiles.Method(source, "private void StartServer()");
        Assert.Contains("CoodszServerTools.Install(ServerRoot)", start);
        Assert.Contains("CoodszServerTools.WithTools(startModules)", start);
        Assert.True(start.IndexOf("SaveRescue.WithRescue", StringComparison.Ordinal) < start.IndexOf("CoodszServerTools.WithTools", StringComparison.Ordinal));
        // Every start writes its module list, since the tools module is always in it.
        Assert.Contains("ModuleCatalog.Write(ServerRoot, startModules);", start);
        Assert.DoesNotContain("if (rescue ||", start);
        // The AD1259 build ships the module for the host rules and the settings sync, so its exe carries the payload.
        Assert.Contains("<CoodszPayload Include=\"$(HostSource)ServerTools\\**\\*\" Payload=\"ServerTools\" />",
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "AD1259Host", "AD1259Host.csproj")));
    }
}
