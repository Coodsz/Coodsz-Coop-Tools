using BannerlordCoopLauncher.Core.Server;
using Coodsz.ServerRules;

namespace BannerlordCoopLauncher.Tests;

// The host's server rules: who may use coop.unstuck (ServerRulesRules.cs, linked, the rule the
// Coodsz Server Tools module runs), the offline XP freeze set, the settings file Coodsz Host writes,
// and pause on join (JoinPause, which Coodsz Host runs from the @DS@ players events).
public sealed class ServerRulesTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly UnstuckFacts Calm = new(false, false, false, false, false);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coodsz-serverrules-" + Guid.NewGuid().ToString("N"));

    public ServerRulesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // ---- coop.unstuck ---------------------------------------------------------------------------

    [Fact]
    public void Allowed_lets_every_request_through_like_Coop_on_its_own()
    {
        var everything = new UnstuckFacts(true, true, true, true, true);
        Assert.Equal(UnstuckBlock.None, UnstuckRule.Decide(UnstuckMode.Allowed, everything, Now.AddSeconds(-5), Now));
        Assert.Equal(UnstuckBlock.None, UnstuckRule.Decide(UnstuckMode.Allowed, Calm, null, Now));
    }

    [Fact]
    public void Host_only_refuses_every_player_request()
    {
        Assert.Equal(UnstuckBlock.HostOnly, UnstuckRule.Decide(UnstuckMode.HostOnly, Calm, null, Now));
        Assert.Contains("host", UnstuckRule.Message(UnstuckBlock.HostOnly));
    }

    [Theory]
    [InlineData(true, false, false, false, false, UnstuckBlock.Prisoner)]
    [InlineData(false, true, false, false, false, UnstuckBlock.InFight)]
    [InlineData(false, false, true, false, false, UnstuckBlock.InSiege)]
    [InlineData(false, false, false, true, false, UnstuckBlock.ArmyInFight)]
    [InlineData(false, false, false, false, true, UnstuckBlock.EnemiesNear)]
    [InlineData(true, true, true, true, true, UnstuckBlock.Prisoner)]
    [InlineData(false, true, true, false, true, UnstuckBlock.InFight)]
    [InlineData(false, false, false, false, false, UnstuckBlock.None)]
    public void Not_in_fights_refuses_a_prisoner_a_fight_a_siege_an_army_fight_and_enemies_near(
        bool prisoner, bool fight, bool siege, bool army, bool enemies, UnstuckBlock expected)
    {
        var facts = new UnstuckFacts(prisoner, fight, siege, army, enemies);
        Assert.Equal(expected, UnstuckRule.Decide(UnstuckMode.NotInFights, facts, null, Now));
    }

    [Fact]
    public void Not_in_fights_has_a_five_minute_real_time_cooldown()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), UnstuckRule.Cooldown);
        Assert.Equal(UnstuckBlock.Cooldown, UnstuckRule.Decide(UnstuckMode.NotInFights, Calm, Now.AddMinutes(-1), Now));
        Assert.Equal(UnstuckBlock.Cooldown, UnstuckRule.Decide(UnstuckMode.NotInFights, Calm, Now.AddSeconds(-299), Now));
        Assert.Equal(UnstuckBlock.None, UnstuckRule.Decide(UnstuckMode.NotInFights, Calm, Now.AddMinutes(-5), Now));
        Assert.Equal(UnstuckBlock.None, UnstuckRule.Decide(UnstuckMode.NotInFights, Calm, Now.AddHours(-2), Now));
        // A clock that went back never locks a player out.
        Assert.Equal(UnstuckBlock.None, UnstuckRule.Decide(UnstuckMode.NotInFights, Calm, Now.AddMinutes(3), Now));
        Assert.Equal(4, UnstuckRule.MinutesLeft(Now.AddMinutes(-1), Now));
        Assert.Equal(1, UnstuckRule.MinutesLeft(Now.AddSeconds(-290), Now));
        Assert.Equal("You can use unstuck again in 4 minutes.", UnstuckRule.Message(UnstuckBlock.Cooldown, 4));
        Assert.Equal("You can use unstuck again in 1 minute.", UnstuckRule.Message(UnstuckBlock.Cooldown, 1));
    }

    [Fact]
    public void A_fight_is_named_before_the_cooldown()
    {
        var fight = new UnstuckFacts(false, true, false, false, false);
        Assert.Equal(UnstuckBlock.InFight, UnstuckRule.Decide(UnstuckMode.NotInFights, fight, Now.AddMinutes(-1), Now));
        Assert.Equal("You can't use unstuck while in a fight.", UnstuckRule.Message(UnstuckBlock.InFight));
    }

    [Fact]
    public void Every_block_has_a_short_message_and_none_has_none()
    {
        foreach (var block in Enum.GetValues<UnstuckBlock>())
        {
            var text = UnstuckRule.Message(block, 3);
            if (block == UnstuckBlock.None)
                Assert.Equal("", text);
            else
                Assert.InRange(text.Length, 10, 90);
        }
    }

    [Fact]
    public void Cooldowns_are_kept_per_player()
    {
        var cooldowns = new UnstuckCooldowns();
        Assert.Null(cooldowns.LastUsed("76561198000000001"));
        cooldowns.Used("76561198000000001", Now);
        Assert.Equal(Now, cooldowns.LastUsed("76561198000000001"));
        Assert.Null(cooldowns.LastUsed("76561198000000002"));
    }

    // ---- offline XP freeze ------------------------------------------------------------------------

    private sealed class Hero(string name)
    {
        public override string ToString() => name;
    }

    [Fact]
    public void The_offline_set_has_every_player_hero_with_no_connection()
    {
        Hero ana = new("Ana"), bo = new("Bo"), cy = new("Cy");
        var offline = OfflineHeroes.Build(new (Hero?, bool)[] { (ana, true), (bo, false), (cy, false), (null, false) });
        Assert.Equal(new HashSet<Hero> { bo, cy }, offline);
    }

    [Fact]
    public void A_hero_registered_twice_is_online_when_one_connection_is()
    {
        Hero ana = new("Ana");
        Assert.Empty(OfflineHeroes.Build(new (Hero?, bool)[] { (ana, false), (ana, true) }));
        Assert.Empty(OfflineHeroes.Build(Array.Empty<(Hero?, bool)>()));
    }

    [Fact]
    public void A_player_read_that_throws_keeps_the_last_offline_set()
    {
        Hero ana = new("Ana"), bo = new("Bo");
        var last = OfflineHeroes.Build(new (Hero?, bool)[] { (ana, false) });

        IEnumerable<(Hero?, bool)> Throwing()
        {
            yield return (bo, false);
            throw new InvalidOperationException("Collection was modified");
        }

        var kept = OfflineHeroes.Refresh(last, Throwing, out var error);
        Assert.Same(last, kept);
        Assert.IsType<InvalidOperationException>(error);

        var fresh = OfflineHeroes.Refresh(last, () => new (Hero?, bool)[] { (bo, false) }, out error);
        Assert.Null(error);
        Assert.Equal(new HashSet<Hero> { bo }, fresh);
    }

    [Fact]
    public void The_player_list_is_copied_before_it_is_read()
    {
        var list = new List<string> { "a", "b" };
        var copy = OfflineHeroes.Snapshot(list);
        list.Add("c");
        Assert.Equal(new object[] { "a", "b" }, copy);
        Assert.Equal(new object[] { "x" }, OfflineHeroes.Snapshot(new[] { "x" }.Where(_ => true)));
    }

    [Fact]
    public void The_module_keeps_the_offline_set_on_a_failed_tick_and_starts_the_cooldown_after_coop_ran()
    {
        var rules = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszServerTools", "src", "ServerRules.cs"));
        var tick = rules[rules.IndexOf("public static void Tick(", StringComparison.Ordinal)..rules.IndexOf("private static List<(Hero? Hero, bool Online)> Registered()", StringComparison.Ordinal)];
        Assert.Contains("OfflineHeroes.Refresh(_offline, Registered, out var error)", tick);
        // Only the rule being off clears the set; a failure never does.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(tick, @"_offline = new HashSet<Hero>\(\)"));
        Assert.Contains("OfflineHeroes.Snapshot(all)", rules);
        Assert.Contains("MobileParty.All.ToList()", rules);

        // The cooldown is set in the postfix, which Harmony skips when Coop's handler throws.
        var prefix = rules[rules.IndexOf("private static bool UnstuckPrefix(", StringComparison.Ordinal)..rules.IndexOf("private static void UnstuckPostfix(", StringComparison.Ordinal)];
        Assert.DoesNotContain("Cooldowns.Used(", prefix);
        Assert.Contains("out string? __state", prefix);
        Assert.Contains("Cooldowns.Used(__state, DateTime.UtcNow)", rules);
        Assert.Contains("postfix: new HarmonyMethod(typeof(ServerRules), nameof(UnstuckPostfix))", rules);
        // Coop's handler: instance ApplyServerUnstuck(Player) with objectManager, playerManager and network; logged once.
        Assert.Contains("the coop.unstuck hook resolved", rules);
        Assert.Contains("the coop.unstuck hook did not resolve", rules);
        foreach (var field in new[] { "\"objectManager\"", "\"playerManager\"", "\"network\"", "\"Player\"", "IsStatic" })
            Assert.Contains(field, rules);
    }

    // ---- the settings file ----------------------------------------------------------------------------

    [Theory]
    [InlineData(null, UnstuckMode.Allowed, true, true)]
    [InlineData("not json", UnstuckMode.Allowed, true, true)]
    [InlineData("{ \"maxClansPerKingdom\": 12 }", UnstuckMode.Allowed, true, true)]
    [InlineData("{ \"unstuck\": \"notInFights\" }", UnstuckMode.NotInFights, true, true)]
    [InlineData("{\"unstuck\":\"HOSTONLY\",\"freezeOfflineXp\":false}", UnstuckMode.HostOnly, true, false)]
    [InlineData("{ \"unstuck\": \"something new\", \"pauseOnJoin\": false }", UnstuckMode.Allowed, false, true)]
    public void The_module_reads_the_rules_with_safe_defaults(string? json, UnstuckMode unstuck, bool pause, bool freeze)
    {
        var read = ServerRulesSettings.Parse(json);
        Assert.Equal(unstuck, read.Unstuck);
        Assert.Equal(pause, read.PauseOnJoin);
        Assert.Equal(freeze, read.FreezeOfflineXp);
    }

    [Fact]
    public void The_host_file_keeps_the_clan_cap_and_the_module_reads_what_the_host_wrote()
    {
        var path = ClanCapFile.PathIn(_dir);
        Assert.Equal(HostServerRules.Defaults, ServerRulesFile.Read(path));

        ClanCapFile.Write(path, new ClanCap(12, true));
        foreach (var choice in Enum.GetValues<UnstuckChoice>())
        {
            var rules = new HostServerRules(choice, choice != UnstuckChoice.HostOnly, choice == UnstuckChoice.Allowed);
            ServerRulesFile.Write(path, rules);
            Assert.Equal(rules, ServerRulesFile.Read(path));
            var module = ServerRulesSettings.Parse(File.ReadAllText(path));
            Assert.Equal((int)choice, (int)module.Unstuck);
            Assert.Equal(ServerRulesFile.ValueOf(choice), ServerRulesSettings.ValueOf(module.Unstuck));
            Assert.Equal(rules.PauseOnJoin, module.PauseOnJoin);
            Assert.Equal(rules.FreezeOfflineXp, module.FreezeOfflineXp);
        }

        // The clan cap, written by the same page into the same file, is still there.
        Assert.Equal(new ClanCap(12, true), ClanCapFile.Read(path));
        ClanCapFile.Write(path, new ClanCap(3, false));
        Assert.Equal(new HostServerRules(UnstuckChoice.HostOnly, false, false), ServerRulesFile.Read(path));
    }

    [Fact]
    public void The_dropdown_has_one_label_for_each_choice()
    {
        Assert.Equal(Enum.GetValues<UnstuckChoice>().Length, ServerRulesFile.UnstuckLabels.Count);
        Assert.Equal("Allowed", ServerRulesFile.UnstuckLabels[(int)UnstuckChoice.Allowed]);
        Assert.Equal("Not in fights, as a prisoner or near enemies", ServerRulesFile.UnstuckLabels[(int)UnstuckChoice.NotInFights]);
        Assert.Equal("Host only", ServerRulesFile.UnstuckLabels[(int)UnstuckChoice.HostOnly]);
    }

    // ---- player kingdom protection ----------------------------------------------------------------------

    private sealed record Realm(string Name);

    private static readonly Realm Vlandia = new("Vlandia");     // ruled by player Ana
    private static readonly Realm Sturgia = new("Sturgia");     // AI ruled, player Bo is a vassal
    private static readonly Realm Battania = new("Battania");   // AI ruled, player Cy is only a mercenary
    private static readonly Realm Empire = new("Empire");       // AI only

    // Ana rules Vlandia and is online, Bo is an offline vassal of Sturgia, Cy is a mercenary of Battania,
    // Di is in no kingdom. Online or offline does not matter to Always, so it is not even an input.
    private static HashSet<Realm> PlayerRealms() => PlayerKingdoms.Build(new (Realm? Kingdom, bool Mercenary)[]
    {
        (Vlandia, false),
        (Sturgia, false),
        (Battania, true),
        (null, false),
    });

    private static bool Blocked(KingdomProtectionMode mode, WarCause cause, Realm declarer, Realm target)
    {
        var players = PlayerRealms();
        return KingdomProtectionRule.Blocks(mode, cause, players.Contains(declarer), players.Contains(target));
    }

    [Fact]
    public void The_default_follows_the_hosts_existing_Coop_option_and_the_module_adds_nothing()
    {
        // No pick: the host file has no key and the module reads it as Coop's own, doing nothing.
        Assert.Null(HostServerRules.Defaults.KingdomProtection);
        Assert.Null(ServerRulesFile.Read(Path.Combine(_dir, "missing.json")).KingdomProtection);
        Assert.Equal(KingdomProtectionMode.OfflineOnly, ServerRulesSettings.Defaults.KingdomProtection);
        Assert.Equal(KingdomProtectionMode.OfflineOnly, ServerRulesSettings.Parse(null).KingdomProtection);
        Assert.Equal(KingdomProtectionMode.OfflineOnly, ServerRulesSettings.Parse("{ \"unstuck\": \"hostOnly\" }").KingdomProtection);

        Assert.Equal("modOptions.blockAiWarDeclarationsOnOfflinePlayers", ServerRulesFile.CoopOfflineWarOption);
        var coopDefault = BannerlordCoopLauncher.Core.Configuration.ModSettingsCatalog.Find(ServerRulesFile.CoopOfflineWarOption);
        Assert.NotNull(coopDefault);
        Assert.Equal(false, coopDefault!.Default);
        Assert.True(ServerRulesFile.CoopOfflineWarValue(KingdomProtectionChoice.OfflineOnly));
        foreach (var cause in Enum.GetValues<WarCause>())
        {
            Assert.False(Blocked(KingdomProtectionMode.OfflineOnly, cause, Empire, Vlandia));
            Assert.False(Blocked(KingdomProtectionMode.OfflineOnly, cause, Empire, Sturgia));
        }
    }

    [Theory]
    [InlineData(true, KingdomProtectionChoice.OfflineOnly)]  // Coop's option on
    [InlineData(false, KingdomProtectionChoice.Off)]         // Coop's option off, Coop's own default
    public void With_no_pick_the_dropdown_shows_the_hosts_Coop_option(bool coopOption, KingdomProtectionChoice shown)
    {
        Assert.Equal(shown, ServerRulesFile.ShownKingdomChoice(null, coopOption));

        // From the values the Settings page shows: true, false, or missing (Coop's default off).
        var values = new Dictionary<string, object> { [ServerRulesFile.CoopOfflineWarOption] = coopOption };
        Assert.Equal(coopOption, ServerRulesFile.CoopOfflineWarIn(values));
        Assert.False(ServerRulesFile.CoopOfflineWarIn(new Dictionary<string, object>()));
        Assert.Equal(KingdomProtectionChoice.Off, ServerRulesFile.ShownKingdomChoice(null, ServerRulesFile.CoopOfflineWarIn(new Dictionary<string, object>())));

        // A missing key in the host file follows Coop, whatever the rest of the file holds.
        var path = Path.Combine(_dir, "rules.json");
        File.WriteAllText(path, "{ \"unstuck\": \"allowed\", \"pauseOnJoin\": true, \"freezeOfflineXp\": true, \"maxClansPerKingdom\": 4 }");
        Assert.Equal(shown, ServerRulesFile.ShownKingdomChoice(ServerRulesFile.Read(path).KingdomProtection, coopOption));

        // Saving without touching the dropdown keeps following Coop, so the Coop option is never flipped.
        Assert.Null(ServerRulesFile.KingdomChoiceToSave(shown, null, coopOption));
        // Coop's option written from the page is the value it already had.
        Assert.Equal(coopOption, ServerRulesFile.CoopOfflineWarValue(shown));
    }

    [Theory]
    [InlineData(KingdomProtectionChoice.OfflineOnly, true)]
    [InlineData(KingdomProtectionChoice.Always, false)]
    [InlineData(KingdomProtectionChoice.Off, true)]
    [InlineData(KingdomProtectionChoice.Always, true)]
    public void A_pick_is_kept_once_the_host_changes_the_dropdown_or_already_has_one(KingdomProtectionChoice onPage, bool coopOption)
    {
        if (onPage != ServerRulesFile.ShownKingdomChoice(null, coopOption))
            Assert.Equal(onPage, ServerRulesFile.KingdomChoiceToSave(onPage, null, coopOption));
        // An existing pick is never dropped, even when it matches what Coop would give.
        foreach (var picked in Enum.GetValues<KingdomProtectionChoice>())
        {
            Assert.Equal(onPage, ServerRulesFile.KingdomChoiceToSave(onPage, picked, coopOption));
            Assert.Equal(picked, ServerRulesFile.ShownKingdomChoice(picked, coopOption));
        }
    }

    [Fact]
    public void Writing_no_pick_leaves_the_key_out_and_keeps_the_rest()
    {
        var path = ClanCapFile.PathIn(_dir);
        ServerRulesFile.Write(path, new HostServerRules(UnstuckChoice.Allowed, true, true, KingdomProtectionChoice.Always));
        Assert.Contains("kingdomProtection", File.ReadAllText(path));
        ClanCapFile.Write(path, new ClanCap(5, false));
        ServerRulesFile.Write(path, new HostServerRules(UnstuckChoice.HostOnly, true, true, null));
        Assert.DoesNotContain("kingdomProtection", File.ReadAllText(path));
        Assert.Null(ServerRulesFile.Read(path).KingdomProtection);
        Assert.Equal(UnstuckChoice.HostOnly, ServerRulesFile.Read(path).Unstuck);
        Assert.Equal(new ClanCap(5, false), ClanCapFile.Read(path));
    }

    [Fact]
    public void Off_turns_Coops_option_off_and_the_module_adds_nothing()
    {
        Assert.False(ServerRulesFile.CoopOfflineWarValue(KingdomProtectionChoice.Off));
        Assert.True(ServerRulesFile.CoopOfflineWarValue(KingdomProtectionChoice.Always));
        foreach (var cause in Enum.GetValues<WarCause>())
            Assert.False(Blocked(KingdomProtectionMode.Off, cause, Empire, Vlandia));
    }

    [Fact]
    public void Player_kingdoms_are_the_ruler_and_vassal_kingdoms_not_a_mercenarys()
    {
        var players = PlayerRealms();
        Assert.Equal(new HashSet<Realm> { Vlandia, Sturgia }, players);
        Assert.Empty(PlayerKingdoms.Build(Array.Empty<(Realm? Kingdom, bool Mercenary)>()));
        // Two players in one kingdom, one of them a mercenary there: the vassal still makes it a player kingdom.
        Assert.Equal(new HashSet<Realm> { Empire }, PlayerKingdoms.Build(new (Realm? Kingdom, bool Mercenary)[] { (Empire, true), (Empire, false) }));
    }

    [Theory]
    [InlineData("Vlandia", true)]   // a ruler, online
    [InlineData("Sturgia", true)]   // a vassal, offline
    [InlineData("Battania", false)] // only a mercenary
    [InlineData("Empire", false)]   // no player
    public void Always_stops_AI_proposals_and_decisions_against_a_ruler_or_vassal_kingdom(string target, bool blocked)
    {
        var realm = new[] { Vlandia, Sturgia, Battania, Empire }.Single(r => r.Name == target);
        var declarer = realm == Empire ? Battania : Empire;
        Assert.Equal(blocked, Blocked(KingdomProtectionMode.Always, WarCause.AiProposal, declarer, realm));
        Assert.Equal(blocked, Blocked(KingdomProtectionMode.Always, WarCause.KingdomDecision, declarer, realm));
    }

    [Fact]
    public void Always_lets_a_war_the_players_kingdom_declares_go_ahead()
    {
        // Vlandia (a player's) declares on the AI: allowed, by proposal and by decision.
        Assert.False(Blocked(KingdomProtectionMode.Always, WarCause.AiProposal, Vlandia, Empire));
        Assert.False(Blocked(KingdomProtectionMode.Always, WarCause.KingdomDecision, Vlandia, Empire));
        // Sturgia, an AI ruled kingdom with a player vassal, is the player's own kingdom too.
        Assert.False(Blocked(KingdomProtectionMode.Always, WarCause.KingdomDecision, Sturgia, Vlandia));
        // Battania has only a mercenary player, so it counts as AI.
        Assert.True(Blocked(KingdomProtectionMode.Always, WarCause.KingdomDecision, Battania, Vlandia));
    }

    [Theory]
    [InlineData(WarCause.PlayerHostility)]
    [InlineData(WarCause.Rebellion)]
    [InlineData(WarCause.Other)]
    public void Always_never_stops_a_player_attacking_first_a_rebellion_or_other_wars(WarCause cause)
    {
        Assert.False(Blocked(KingdomProtectionMode.Always, cause, Empire, Vlandia));
        Assert.False(Blocked(KingdomProtectionMode.Always, cause, Empire, Sturgia));
        Assert.False(Blocked(KingdomProtectionMode.Always, cause, Vlandia, Empire));
    }

    [Theory]
    [InlineData(KingdomProtectionChoice.OfflineOnly, "offlineOnly")]
    [InlineData(KingdomProtectionChoice.Always, "always")]
    [InlineData(KingdomProtectionChoice.Off, "off")]
    public void The_kingdom_choice_round_trips_and_the_module_reads_what_the_host_wrote(KingdomProtectionChoice choice, string value)
    {
        var path = ClanCapFile.PathIn(_dir);
        ClanCapFile.Write(path, new ClanCap(8, false));
        var rules = new HostServerRules(UnstuckChoice.NotInFights, false, true, choice);
        ServerRulesFile.Write(path, rules);

        Assert.Equal(rules, ServerRulesFile.Read(path));
        var text = File.ReadAllText(path);
        Assert.Contains("\"kingdomProtection\": \"" + value + "\"", text);
        var module = ServerRulesSettings.Parse(text);
        Assert.Equal((int)choice, (int)module.KingdomProtection);
        Assert.Equal(value, ServerRulesSettings.ValueOf(module.KingdomProtection));
        Assert.Equal(UnstuckMode.NotInFights, module.Unstuck);
        Assert.Equal(new ClanCap(8, false), ClanCapFile.Read(path));
    }

    [Theory]
    [InlineData("{ \"kingdomProtection\": \"ALWAYS\" }", KingdomProtectionChoice.Always)]
    [InlineData("{ \"kingdomProtection\": \"Off\" }", KingdomProtectionChoice.Off)]
    [InlineData("{ \"kingdomProtection\": \"offlineonly\" }", KingdomProtectionChoice.OfflineOnly)]
    [InlineData("{ \"kingdomProtection\": \"something new\" }", null)]
    [InlineData("{ \"kingdomProtection\": true }", null)]
    [InlineData("{ \"unstuck\": \"allowed\", \"pauseOnJoin\": true, \"freezeOfflineXp\": true }", null)]
    public void An_unknown_or_missing_kingdom_choice_follows_Coop(string json, KingdomProtectionChoice? expected)
    {
        var path = Path.Combine(_dir, "rules.json");
        File.WriteAllText(path, json);
        Assert.Equal(expected, ServerRulesFile.Read(path).KingdomProtection);
        // The module only acts on always; anything else, missing included, adds nothing.
        var module = ServerRulesSettings.Parse(json).KingdomProtection;
        Assert.Equal(expected == KingdomProtectionChoice.Always, module == KingdomProtectionMode.Always);
    }

    [Fact]
    public void The_kingdom_dropdown_has_one_label_for_each_choice_with_Coops_choice_first()
    {
        Assert.Equal(Enum.GetValues<KingdomProtectionChoice>().Length, ServerRulesFile.KingdomProtectionLabels.Count);
        Assert.Equal(Enum.GetValues<KingdomProtectionMode>().Length, ServerRulesFile.KingdomProtectionLabels.Count);
        Assert.Equal(0, (int)KingdomProtectionChoice.OfflineOnly);
        Assert.Equal("Only while their players are offline", ServerRulesFile.KingdomProtectionLabels[(int)KingdomProtectionChoice.OfflineOnly]);
        Assert.Equal("Always", ServerRulesFile.KingdomProtectionLabels[(int)KingdomProtectionChoice.Always]);
        Assert.Equal("Off", ServerRulesFile.KingdomProtectionLabels[(int)KingdomProtectionChoice.Off]);
    }

    [Fact]
    public void The_module_hooks_only_the_AI_war_path_fails_safe_and_has_no_peace_backstop()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszServerTools", "src", "KingdomProtection.cs"));
        Assert.Contains("Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.", source);
        Assert.Contains("\"ConsiderWar\", new[] { typeof(Clan), typeof(Kingdom), typeof(IFaction) }", source);
        Assert.Contains("\"GetRandomWarDecision\", new[] { typeof(Clan) }", source);
        Assert.Contains("nameof(DeclareWarAction.ApplyByKingdomDecision)", source);
        // Only those three hooks (never player hostility, rebellion or other war actions), and no forced peace.
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(source, @"harmony\.Patch\(").Count);
        foreach (var never in new[] { "nameof(DeclareWarAction.ApplyByPlayerHostility)", "nameof(DeclareWarAction.ApplyByRebellion)", "MakePeaceAction.Apply", "WarDeclared.AddNonSerializedListener" })
            Assert.DoesNotContain(never, source);
        // A failed check lets the war go ahead and is logged once.
        var blocks = source[source.IndexOf("private static bool Blocks(", StringComparison.Ordinal)..source.IndexOf("private static bool ConsiderWarPrefix(", StringComparison.Ordinal)];
        Assert.Contains("_checkFailed = true;", blocks);
        Assert.Contains("return false;", blocks[blocks.IndexOf("catch (Exception e)", StringComparison.Ordinal)..]);
        Assert.Contains("so Always lets those wars go ahead", source);

        var rules = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszServerTools", "src", "ServerRules.cs"));
        Assert.Contains("KingdomProtection.Patch(_harmony)", rules);
        Assert.Contains("KingdomProtection.Tick(dt, Registered)", rules);
    }

    [Fact]
    public void Coodsz_Host_writes_Coops_offline_war_option_at_start_only_after_a_pick()
    {
        var host = Path.Combine(RepoRoot(), "src", "CoodszHost");
        var serverRules = File.ReadAllText(Path.Combine(host, "MainWindow.ServerRules.cs"));
        var apply = serverRules[serverRules.IndexOf("private void ApplyKingdomProtection()", StringComparison.Ordinal)..];
        Assert.Contains("ServerRulesFile.Read(ServerRulesPath).KingdomProtection is not { } picked)", apply);
        Assert.Contains("ServerRulesFile.CoopOfflineWarValue(picked)", apply);
        Assert.Contains("ServerRulesFile.KingdomChoiceToSave(KingdomProtectionFromPage(), ServerRulesFile.Read(ServerRulesPath).KingdomProtection, _kingdomCoopOption)", serverRules);
        Assert.Contains("ServerRulesFile.ShownKingdomChoice(rules.KingdomProtection, _kingdomCoopOption)", serverRules);
        var coopSettings = File.ReadAllText(Path.Combine(host, "MainWindow.CoopSettings.cs"));
        Assert.Contains("ShowKingdomProtection(values);", coopSettings);
        Assert.Contains("shown.Add(BannerlordCoopLauncher.Core.Server.ServerRulesFile.CoopOfflineWarOption)", coopSettings);
        Assert.Contains("ServerRulesFile.CoopOfflineWarValue(KingdomProtectionFromPage())", coopSettings);
        var start = File.ReadAllText(Path.Combine(host, "MainWindow.ServerControl.cs"));
        Assert.True(start.IndexOf("ApplySaveProfile(profile.SaveName);", StringComparison.Ordinal) <
                    start.IndexOf("ApplyKingdomProtection();", StringComparison.Ordinal));
        Assert.Contains("x:Name=\"KingdomProtectionBox\"", File.ReadAllText(Path.Combine(host, "MainWindow.xaml")));
    }

    // ---- pause on join ----------------------------------------------------------------------------------

    private static ConnectedPlayer P(int id, string name, string state) => new(id, name, state, "127.0.0.1:1");

    [Fact]
    public void A_joining_player_pauses_the_game_and_on_the_map_puts_the_speed_back()
    {
        var pause = new JoinPause();
        Assert.Equal(JoinPauseStep.None, pause.Update([P(0, "Ana", "on map")], Now, true));

        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(0, "Ana", "on map"), P(1, ConnectedPlayer.JoiningPlaceholder, "handshake")], Now, true));
        // The question is out: no second pause while the answer is on its way.
        Assert.Equal(JoinPauseStep.None, pause.Update([P(0, "Ana", "on map"), P(1, "Bo", "loading")], Now, true));
        Assert.True(pause.Paused("OK|Play_2x", Now));
        Assert.True(pause.Holding);
        Assert.Equal("coop.debug.set_time_mode Play_2x", pause.ResumeCommand);
        Assert.Equal(["Bo"], pause.Waiting);

        Assert.Equal(JoinPauseStep.None, pause.Update([P(0, "Ana", "on map"), P(1, "Bo", "loading")], Now.AddSeconds(30), true));
        Assert.Equal(JoinPauseStep.Resume, pause.Update([P(0, "Ana", "on map"), P(1, "Bo", "on map")], Now.AddSeconds(40), true));
        Assert.False(pause.Holding);
        Assert.False(pause.GaveUp);
    }

    [Fact]
    public void A_player_seen_on_the_map_never_pauses_again_that_session()
    {
        var pause = new JoinPause();
        pause.Update([P(1, "Bo", "on map")], Now, true);
        Assert.Equal(JoinPauseStep.None, pause.Update([P(1, "Bo", "loading")], Now, true));
        // A reconnect gets a new peer index and the joining placeholder for a while.
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(4, ConnectedPlayer.JoiningPlaceholder, "handshake")], Now, true));
        Assert.True(pause.Paused("Play_1x", Now));
        Assert.Equal(JoinPauseStep.Resume, pause.Update([P(4, "Bo", "loading")], Now.AddSeconds(5), true));

        pause.Reset();
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "loading")], Now, true));
    }

    [Fact]
    public void After_three_minutes_it_gives_up_so_a_stuck_loader_cannot_freeze_the_world()
    {
        var pause = new JoinPause();
        Assert.Equal(TimeSpan.FromMinutes(3), JoinPause.GiveUpAfter);
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(2, "Cy", "loading")], Now, true));
        Assert.True(pause.Paused("Play_1x", Now));
        Assert.Equal(JoinPauseStep.None, pause.Tick(Now.AddMinutes(2.9), true));
        Assert.Equal(JoinPauseStep.Resume, pause.Tick(Now.AddMinutes(3), true));
        Assert.True(pause.GaveUp);
        Assert.Equal("coop.debug.set_time_mode Play_1x", pause.ResumeCommand);
        // Cy is given up on; a different new player still pauses.
        Assert.Equal(JoinPauseStep.None, pause.Update([P(2, "Cy", "loading")], Now.AddMinutes(4), true));
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(2, "Cy", "loading"), P(3, "Di", "loading")], Now.AddMinutes(4), true));
    }

    [Fact]
    public void A_game_that_was_already_paused_is_left_alone()
    {
        var pause = new JoinPause();
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "loading")], Now, true));
        Assert.False(pause.Paused("OK|Pause", Now));
        Assert.False(pause.Holding);
        Assert.Equal(JoinPauseStep.None, pause.Update([P(1, "Bo", "loading")], Now.AddSeconds(5), true));
        Assert.Equal(JoinPauseStep.None, pause.Update([P(1, "Bo", "on map")], Now.AddSeconds(9), true));
        // The next join is asked about again.
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "on map"), P(2, "Cy", "loading")], Now.AddSeconds(20), true));
    }

    [Fact]
    public void The_host_choosing_a_speed_wins_over_the_join_pause()
    {
        var pause = new JoinPause();
        pause.Update([P(1, "Bo", "loading")], Now, true);
        pause.Paused("Play_1x", Now);
        pause.HostSent("launcher.time.set Play");
        Assert.False(pause.Holding);
        Assert.Equal(JoinPause.NormalMode, pause.HostChoice);
        Assert.Equal(JoinPauseStep.None, pause.Update([P(1, "Bo", "on map")], Now.AddSeconds(10), true));

        pause.HostSent("coop.debug.set_time_mode Play_2x");
        Assert.Equal(JoinPause.FastMode, pause.HostChoice);
        pause.HostSent("coop.heroes");
        Assert.Equal(JoinPause.FastMode, pause.HostChoice);

        // When the server gives no answer, the host's last choice is put back.
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "on map"), P(2, "Cy", "loading")], Now.AddSeconds(20), true));
        Assert.True(pause.Paused(null, Now.AddSeconds(20)));
        Assert.Equal("coop.debug.set_time_mode Play_2x", pause.ResumeCommand);
    }

    [Fact]
    public void Turning_it_off_never_pauses_and_lets_a_pause_go()
    {
        var pause = new JoinPause();
        Assert.Equal(JoinPauseStep.None, pause.Update([P(1, "Bo", "loading")], Now, false));
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "loading")], Now, true));
        pause.Paused("Play_1x", Now);
        Assert.Equal(JoinPauseStep.Resume, pause.Tick(Now.AddSeconds(5), false));
    }

    [Fact]
    public void A_pause_that_could_not_be_sent_is_tried_again()
    {
        var pause = new JoinPause();
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "loading")], Now, true));
        Assert.True(pause.Paused("Play_1x", Now));
        pause.PauseFailed();
        Assert.False(pause.Holding);
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "loading")], Now.AddSeconds(2), true));
    }

    [Fact]
    public void A_player_who_leaves_while_loading_lets_the_pause_go()
    {
        var pause = new JoinPause();
        pause.Update([P(1, "Bo", "loading")], Now, true);
        pause.Paused("Play_1x", Now);
        Assert.Equal(JoinPauseStep.Resume, pause.Update([], Now.AddSeconds(3), true));
    }

    [Fact]
    public void A_host_speed_set_while_the_pause_goes_out_is_put_back_once_the_pause_lands()
    {
        var pause = new JoinPause();
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "loading")], Now, true));
        Assert.True(pause.Paused("OK|Play_1x", Now));
        // The host presses 2x before the pause command reached the server.
        pause.HostSent("coop.debug.set_time_mode Play_2x");
        Assert.Equal("coop.debug.set_time_mode Play_2x", pause.PauseLanded());
        Assert.False(pause.Holding);
        // Nothing else resumes or pauses for Bo: the host decided.
        Assert.Equal(JoinPauseStep.None, pause.Update([P(1, "Bo", "loading")], Now.AddSeconds(5), true));
        Assert.Equal(JoinPauseStep.None, pause.Update([P(1, "Bo", "on map")], Now.AddSeconds(9), true));
    }

    [Fact]
    public void A_pause_that_landed_with_no_host_choice_is_held_and_a_host_pause_needs_nothing()
    {
        var pause = new JoinPause();
        pause.Update([P(1, "Bo", "loading")], Now, true);
        Assert.True(pause.Paused("Play_1x", Now));
        // Applied, answered or timed out: nothing to put back, the pause is held and resumed later.
        Assert.Null(pause.PauseLanded());
        Assert.True(pause.Holding);
        Assert.Equal(JoinPauseStep.Resume, pause.Update([P(1, "Bo", "on map")], Now.AddSeconds(3), true));

        var hostPaused = new JoinPause();
        hostPaused.Update([P(2, "Cy", "loading")], Now, true);
        Assert.True(hostPaused.Paused("Play_1x", Now));
        hostPaused.HostSent("coop.debug.set_time_mode Pause");
        Assert.Null(hostPaused.PauseLanded());
        Assert.False(hostPaused.Holding);
    }

    [Fact]
    public void A_host_speed_set_while_the_mode_question_is_out_stops_the_pause()
    {
        var pause = new JoinPause();
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "loading")], Now, true));
        pause.HostSent("launcher.time.set Play");
        Assert.False(pause.Paused("OK|Play_1x", Now));
        Assert.False(pause.Holding);
        Assert.Equal(JoinPauseStep.None, pause.Update([P(1, "Bo", "loading")], Now.AddSeconds(2), true));
    }

    // Live test: the host pressed Pause game while a player rejoined, and the server put Play_1x back 5 seconds after
    // the player entered the campaign ("[DedicatedServer] Player synchronization completed; campaign time resumed.").
    [Fact]
    public void The_servers_own_resume_after_a_join_puts_the_hosts_pause_back_and_nothing_else()
    {
        var resumed = "[08:14:44] " + ServerJoinResumed;
        var pause = new JoinPause();
        // No host choice yet, or the host let time run: the server's resume is left alone.
        Assert.False(pause.HostPauseUndone(resumed));
        pause.HostSent("launcher.time.set Play");
        Assert.False(pause.HostPauseUndone(resumed));

        // The rejoin never paused (Bo was on the map before), and the host paused.
        pause.Update([P(1, "Bo", "on map")], Now, true);
        Assert.Equal(JoinPauseStep.None, pause.Update([P(1, "Bo", "loading")], Now.AddSeconds(5), true));
        pause.HostSent("launcher.time.set Pause");
        Assert.True(pause.HostPauseUndone(resumed));
        Assert.False(pause.HostPauseUndone("[DedicatedServer] Player entered the campaign; keeping campaign time paused for 5 additional seconds while synchronization settles."));
        Assert.False(pause.HostPauseUndone(null));
        // Each later join while the host's pause stands gets it back again.
        Assert.True(pause.HostPauseUndone(resumed));

        // The host's Resume game ends it, and a new server run forgets the pause.
        pause.HostSent("coop.debug.set_time_mode Play_1x");
        Assert.False(pause.HostPauseUndone(resumed));
        pause.HostSent("coop.debug.set_time_mode Pause");
        pause.Reset();
        Assert.False(pause.HostPauseUndone(resumed));

        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("if (_joinPause.HostPauseUndone(line))", host);
        var keep = SourceFiles.Method(host, "private async void KeepHostPause(");
        // It waits its turn, then checks again: a Resume game pressed meanwhile, or a Restart, sends nothing.
        var waits = keep.IndexOf("entered = await JoinGate.EnterHost(ServerSession.PauseCommand);", StringComparison.Ordinal);
        Assert.True(waits > keep.IndexOf("try", StringComparison.Ordinal)
                    && waits < keep.IndexOf("if (_session.RunId != run || !_session.CanSendCommands || !_joinPause.HostPauseUndone(line))", StringComparison.Ordinal));
        Assert.Contains("if (entered)", keep);
        // Players who enter together get one pause: only one waits at a time.
        Assert.Contains("if (_keepPauseQueued)", keep);
        Assert.Contains("KeepHostPause(line);", host);
    }

    // The line CampaignRuleOverrides in the server's DedicatedServer.LauncherBattleSize writes when it resumes after a join.
    private const string ServerJoinResumed = "[DedicatedServer] Player synchronization completed; campaign time resumed.";

    [Fact]
    public void A_pause_the_server_refused_is_not_sent_again_after_a_join()
    {
        var resumed = ServerJoinResumed;
        var pause = new JoinPause();
        pause.HostSent("launcher.time.set Pause");
        pause.HostSendFailed("launcher.time.set Pause", pause.HostCommandCount);
        Assert.False(pause.HostPauseUndone(resumed));

        // A newer pause since the refused one stands.
        pause.HostSent("launcher.time.set Pause");
        var refused = pause.HostCommandCount;
        pause.HostSent("coop.debug.set_time_mode Pause");
        pause.HostSendFailed("launcher.time.set Pause", refused);
        Assert.True(pause.HostPauseUndone(resumed));
        // A failed command that is no pause leaves it alone.
        pause.HostSendFailed("coop.heroes", pause.HostCommandCount);
        pause.HostSendFailed("launcher.time.set Play", pause.HostCommandCount);
        Assert.True(pause.HostPauseUndone(resumed));

        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("_joinPause.HostSendFailed(command, hostCommands);", SourceFiles.Method(host, "private async Task SendServerCommand("));
        Assert.Contains("_joinPause.HostSendFailed(command, hostCommands);", SourceFiles.Method(host, "private async Task SendConsoleCommand("));
    }

    [Fact]
    public void A_player_given_up_on_as_a_new_player_is_not_waited_for_again_once_the_name_shows()
    {
        var pause = new JoinPause();
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(5, ConnectedPlayer.JoiningPlaceholder, "handshake")], Now, true));
        Assert.True(pause.Paused("Play_1x", Now));
        Assert.Equal(["#5"], pause.Waiting);
        Assert.Equal(JoinPauseStep.Resume, pause.Tick(Now.AddMinutes(3), true));
        Assert.True(pause.GaveUp);

        // The same peer now shows a name: still the given up player, no second pause.
        Assert.Equal(JoinPauseStep.None, pause.Update([P(5, "Eve", "loading")], Now.AddMinutes(4), true));
        Assert.Empty(pause.Waiting);
        Assert.Equal(JoinPauseStep.None, pause.Update([P(5, "Eve", "loading")], Now.AddMinutes(5), true));

        // A different player who joins later as the same peer index after Eve left is new.
        pause.Update([], Now.AddMinutes(6), true);
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(5, ConnectedPlayer.JoiningPlaceholder, "handshake")], Now.AddMinutes(7), true));
    }

    [Fact]
    public void A_waiting_new_player_keeps_the_pause_held_when_the_name_shows()
    {
        var pause = new JoinPause();
        pause.Update([P(5, ConnectedPlayer.JoiningPlaceholder, "handshake")], Now, true);
        Assert.True(pause.Paused("Play_1x", Now));
        Assert.Equal(JoinPauseStep.None, pause.Update([P(5, "Eve", "loading")], Now.AddSeconds(10), true));
        Assert.True(pause.Holding);
        Assert.Equal(["Eve"], pause.Waiting);
        Assert.Equal(JoinPauseStep.Resume, pause.Update([P(5, "Eve", "on map")], Now.AddSeconds(20), true));
    }

    // ---- the module's wiring --------------------------------------------------------------------------

    [Fact]
    public void The_module_patches_Coops_player_unstuck_path_and_credits_ModderLords()
    {
        var src = Path.Combine(RepoRoot(), "compat", "CoodszServerTools", "src");
        var rules = File.ReadAllText(Path.Combine(src, "ServerRules.cs"));
        // Only a player's own request reaches ApplyServerUnstuck; the host's console calls ServerPlayerUnstuck directly.
        Assert.Contains("GameInterface.Services.MobileParties.Handlers.PlayerUnstuckHandler", rules);
        Assert.Contains("\":ApplyServerUnstuck\"", rules);
        Assert.Contains("nameof(HeroDeveloper.AddSkillXp)", rules);
        // Gold is never frozen: skipping a gold change would make gold from nothing.
        Assert.DoesNotContain("ChangeHeroGold", rules);
        foreach (var name in new[] { "ServerRules.cs", "ServerRulesRules.cs" })
            Assert.Contains("Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.", File.ReadAllText(Path.Combine(src, name)));
        // A host edit still reaches an offline player's hero.
        Assert.Contains("ServerRules.Allow(", File.ReadAllText(Path.Combine(src, "DevelopmentCommands.cs")));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Theory]
    [InlineData("OK|Pause", "Pause")]
    [InlineData("Time control set to Play_2x", "Play_2x")]
    [InlineData("play_1x", "Play_1x")]
    [InlineData("Failed to get time control interface", null)]
    [InlineData(null, null)]
    public void The_time_mode_is_read_from_the_server_reply(string? reply, string? mode) =>
        Assert.Equal(mode, JoinPause.ModeIn(reply));
}
