using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// AD1259 1.4.6 was made for Open Source Armory 1.18, Saddlery 1.8 and Weaponry 1.8 (the Nexus files "FOR v1.2.x"), and
// with 2.0 some AD1259 gear shows missing textures. The AD1259 tools take either set, but the host and every player need
// the same versions, so the server gets the copy the host's game loads.
public class OpenSourceSetTests
{
    private static AD1259Mod Armory => AD1259Stack.Find("OpenSourceArmory")!;
    private static AD1259Mod Saddlery => AD1259Stack.Find("OpenSourceSaddlery")!;
    private static AD1259Mod Weaponry => AD1259Stack.Find("OpenSourceWeaponry")!;

    [Theory]
    [InlineData("v1.18.0")]
    [InlineData("v1.18.0.0")]
    [InlineData("1.18.0")]
    [InlineData("v1.18")]
    [InlineData("V1.18.0")]
    [InlineData("v2.0.0")]
    [InlineData("2.0.0.0")]
    public void Armory_takes_the_1_18_and_2_0_sets_in_every_way_they_are_written(string found) =>
        Assert.True(AD1259Stack.VersionMatches(Armory, found));

    [Theory]
    [InlineData("v1.17.0")]
    [InlineData("v1.18.1")]
    [InlineData("v1.8.0")]
    [InlineData("v2.0.1")]
    [InlineData("e1.18.0")]
    [InlineData("")]
    public void Armory_refuses_other_versions(string found) =>
        Assert.False(AD1259Stack.VersionMatches(Armory, found));

    [Fact]
    public void Saddlery_and_weaponry_take_1_8_and_their_2_0_pins()
    {
        foreach (var version in new[] { "v1.8.0", "1.8.0", "v1.8.0.0", "v1.8" })
        {
            Assert.True(AD1259Stack.VersionMatches(Saddlery, version));
            Assert.True(AD1259Stack.VersionMatches(Weaponry, version));
        }
        Assert.True(AD1259Stack.VersionMatches(Saddlery, "v2.0.0"));
        Assert.True(AD1259Stack.VersionMatches(Weaponry, "v2.0.1"));
        Assert.False(AD1259Stack.VersionMatches(Weaponry, "v2.0.0"));
        Assert.False(AD1259Stack.VersionMatches(Saddlery, "v1.18.0"));
    }

    [Fact]
    public void The_1_x_set_is_not_newer_than_the_pin_and_the_made_for_row_shows_it_as_a_match()
    {
        Assert.False(AD1259Stack.IsNewerThanPinned(Armory, "v1.18.0"));
        Assert.False(AD1259Stack.IsNewerThanPinned(Weaponry, "v1.8.0"));
        Assert.False(AD1259Stack.IsNewerThanPinned(Armory, "v1.19.0"));
        Assert.True(AD1259Stack.IsNewerThanPinned(Armory, "v2.1.0"));
        Assert.Equal(PinState.Match, new VersionPin("Open Source Armory", PinIcon.Mod, Armory.Version, "v1.18.0.0", AlsoAccepted: Armory.AlsoAccepted).State);
        Assert.Equal(PinState.Match, new VersionPin("Open Source Weaponry", PinIcon.Mod, Weaponry.Version, "1.8.0", AlsoAccepted: Weaponry.AlsoAccepted).State);
        Assert.Equal(PinState.Mismatch, new VersionPin("Open Source Armory", PinIcon.Mod, Armory.Version, "v1.17.0", AlsoAccepted: Armory.AlsoAccepted).State);
    }

    [Fact]
    public void The_v_and_padding_tolerance_keeps_other_letters_and_the_base_game_build()
    {
        Assert.True(AD1259Stack.VersionMatches(AD1259Stack.Find("RBM")!, "4.5.4"));
        Assert.False(AD1259Stack.VersionMatches(AD1259Stack.Find("ExpandedArmouryBL")!, "v1.5.2"));
        Assert.True(AD1259Stack.VersionMatches(AD1259Stack.Find("Native")!, "v1.4.8.118999"));
        Assert.False(AD1259Stack.VersionMatches(AD1259Stack.Find("Native")!, "v1.4.6"));
    }

    [Theory]
    [InlineData("v2.0.0", "v2.0.0", "v2.0.1", OpenSourceGeneration.Current)]
    [InlineData("v1.18.0", "v1.8.0", "v1.8.0", OpenSourceGeneration.Older)]
    [InlineData("1.18.0.0", "1.8", "v1.8.0.0", OpenSourceGeneration.Older)]
    [InlineData("v1.18.0", "v2.0.0", "v2.0.1", OpenSourceGeneration.Mixed)]
    [InlineData("v2.0.0", "v2.0.0", "v1.8.0", OpenSourceGeneration.Mixed)]
    [InlineData("v1.18.0", null, "v2.0.1", OpenSourceGeneration.Mixed)]
    // Only a whole set is named: one or two mods of a set, or one wrong, leave it to their own lines.
    [InlineData("v1.18.0", null, null, OpenSourceGeneration.None)]
    [InlineData(null, "v2.0.0", "v2.0.1", OpenSourceGeneration.None)]
    [InlineData("v1.17.0", "v2.0.0", "v2.0.1", OpenSourceGeneration.None)]
    [InlineData(null, null, null, OpenSourceGeneration.None)]
    public void The_generation_is_one_set_or_a_mix(string? armory, string? saddlery, string? weaponry, OpenSourceGeneration expected)
    {
        var versions = new Dictionary<string, string?> { ["OpenSourceArmory"] = armory, ["OpenSourceSaddlery"] = saddlery, ["OpenSourceWeaponry"] = weaponry };
        Assert.Equal(expected, OpenSourceSet.Of(id => versions[id]));
    }

    [Fact]
    public void The_client_line_names_the_set_and_a_mix_names_each_version()
    {
        AD1259ModStatus Ready(AD1259Mod mod, string version) => new(mod, AD1259ModState.Ready, version, ["Modules\\" + mod.Id], []);

        var older = OpenSourceSet.PcLine([Ready(Armory, "v1.18.0"), Ready(Saddlery, "v1.8.0"), Ready(Weaponry, "v1.8.0")])!;
        Assert.Equal(OpenSourceSet.PcOlder + "\n" + OpenSourceSet.SameSet, older);

        var current = OpenSourceSet.PcLine([Ready(Armory, "v2.0.0"), Ready(Saddlery, "v2.0.0"), Ready(Weaponry, "v2.0.1")])!;
        Assert.Equal("This PC has the Open Source 2.0 set.\n" + OpenSourceSet.MissingTextures + "\n" + OpenSourceSet.SameSet, current);
        // The Mods list note and later checks leave the missing textures line out (once per run).
        Assert.Equal("This PC has the Open Source 2.0 set.\n" + OpenSourceSet.SameSet,
            OpenSourceSet.PcLine([Ready(Armory, "v2.0.0"), Ready(Saddlery, "v2.0.0"), Ready(Weaponry, "v2.0.1")], withTextures: false));
        Assert.Equal(OpenSourceSet.ServerCurrent + "\n" + OpenSourceSet.SameSet,
            OpenSourceSet.ServerLine(id => AD1259Stack.Find(id)!.Version, withTextures: false));

        var mixed = OpenSourceSet.PcLine([Ready(Armory, "v1.18.0"), Ready(Saddlery, "v2.0.0"), Ready(Weaponry, "v2.0.1")])!;
        Assert.Equal("The Open Source mods on this PC are from two sets: Open Source Armory v1.18.0, Open Source Saddlery v2.0.0, Open Source Weaponry v2.0.1."
            + "\n" + OpenSourceSet.OneSet + "\n" + OpenSourceSet.SameSet, mixed);

        // A missing or wrong mod has its own line; the set line leaves it out.
        Assert.Null(OpenSourceSet.PcLine([new AD1259ModStatus(Armory, AD1259ModState.Missing, null, [], [])]));
        // An Armory 1.18 alone is no 1.x set.
        Assert.Null(OpenSourceSet.PcLine([Ready(Armory, "v1.18.0"), new AD1259ModStatus(Saddlery, AD1259ModState.Missing, null, [], [])]));
    }

    [Fact]
    public void A_missing_or_wrong_open_source_mod_also_names_the_1_x_set_on_nexus()
    {
        var missing = new AD1259ModStatus(Armory, AD1259ModState.Missing, null, [], []).Problem!;
        Assert.Equal("Open Source Armory is missing. Get v2.0.0 from Steam Workshop.\n" + OpenSourceSet.OlderToo, missing);
        var wrong = new AD1259ModStatus(Weaponry, AD1259ModState.WrongVersion, "v1.7.0", [], []).Problem!;
        Assert.EndsWith("\n" + OpenSourceSet.OlderToo, wrong);
        // Other mods keep their line.
        Assert.DoesNotContain(OpenSourceSet.OlderToo, new AD1259ModStatus(AD1259Stack.Find("RBM")!, AD1259ModState.Missing, null, [], []).Problem);
    }

    [Fact]
    public void The_client_judges_the_modules_copy_when_the_workshop_also_has_2_0()
    {
        var root = Temp();
        try
        {
            var modules = Path.Combine(root, "Modules");
            var workshop = Path.Combine(root, "workshop");
            Module(modules, "OpenSourceArmory", "OpenSourceArmory", "v1.18.0");
            Module(workshop, "3011479883", "OpenSourceArmory", "v2.0.0");
            Module(modules, "OpenSourceSaddlery", "OpenSourceSaddlery", "v1.8.0");
            Module(modules, "OpenSourceSaddlery-copy", "OpenSourceSaddlery", "v1.8.0");

            var statuses = AD1259ModScan.Check(modules, [workshop]).ToDictionary(status => status.Mod.Id);
            Assert.Equal(AD1259ModState.Ready, statuses["OpenSourceArmory"].State);
            Assert.Equal("v1.18.0", statuses["OpenSourceArmory"].FoundVersion);
            // Two copies in Modules are still two copies.
            Assert.Equal(AD1259ModState.Duplicate, statuses["OpenSourceSaddlery"].State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The bug this fixes: a host with the 1.x set from Nexus in the game's Modules folder and 2.0 still subscribed kept a
    // 2.0 server (the Workshop refresh put it there, and the server's 2.0 already counted as right), so every join failed.
    [Fact]
    public void The_server_gets_the_hosts_modules_copy_over_the_workshop_2_0()
    {
        var root = Temp();
        try
        {
            var server = Path.Combine(root, "DedicatedServer");
            var serverModules = ModuleCatalog.ModulesRoot(server);
            var game = Path.Combine(root, "Modules");
            var workshop = Path.Combine(root, "workshop");
            Module(serverModules, "OpenSourceArmory", "OpenSourceArmory", "v2.0.0");
            Module(game, "OpenSourceArmory", "OpenSourceArmory", "v1.18.0");
            Module(workshop, "3011479883", "OpenSourceArmory", "v2.0.0");

            var sources = AD1259Server.PcCopies(game, [workshop]);
            Assert.Equal(Path.Combine(game, "OpenSourceArmory"), AD1259Server.GoodCopy(Armory, sources.ById["OpenSourceArmory"]).Directory);

            // Start's Workshop refresh leaves the Open Source mods alone in the AD1259 edition.
            var ids = new[] { "OpenSourceArmory", "RBM" };
            Assert.Equal(new[] { "RBM" }, AD1259Server.WorkshopRefreshIds(ids));
            var pins = AD1259Stack.ClientMods.ToDictionary(mod => mod.Id, mod => mod.Version, StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(Path.Combine(serverModules, "OpenSourceArmory", "SubModule.xml"),
                "<Module><Id value=\"OpenSourceArmory\"/><Version value=\"v1.18.0\"/></Module>");
            Assert.Empty(ExtraModScan.UpdateFromWorkshop(server, AD1259Server.WorkshopRefreshIds(ids), [workshop], pins));
            Assert.Contains("v1.18.0", File.ReadAllText(Path.Combine(serverModules, "OpenSourceArmory", "SubModule.xml")));

            // A 2.0 server copy counts as right, but the host plays 1.18, so the 1.18 copy goes in.
            File.WriteAllText(Path.Combine(serverModules, "OpenSourceArmory", "SubModule.xml"),
                "<Module><Id value=\"OpenSourceArmory\"/><Version value=\"v2.0.0\"/></Module>");
            var rows = new List<ModuleRow> { new() { Id = "OpenSourceArmory", Version = "v2.0.0" } };
            var refreshed = AD1259Server.RefreshWrongVersions(server, rows, sources);
            Assert.Equal(new[] { "Open Source Armory v1.18.0 from " + Path.Combine(game, "OpenSourceArmory") }, refreshed);
            Assert.Contains("v1.18.0", File.ReadAllText(Path.Combine(serverModules, "OpenSourceArmory", "SubModule.xml")));

            // Once it matches, nothing is copied again, and a short copy check never compares 1.18 with 2.0 files.
            rows = [new() { Id = "OpenSourceArmory", Version = "v1.18.0" }];
            Assert.Empty(AD1259Server.RefreshWrongVersions(server, rows, sources));
            Assert.Empty(AD1259Server.ShortCopies(server, [new() { Id = "OpenSourceArmory", Version = "v2.0.0" }], sources));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Without_a_modules_copy_the_server_gets_the_workshop_2_0_and_a_mix_gets_a_start_line()
    {
        var root = Temp();
        try
        {
            var server = Path.Combine(root, "DedicatedServer");
            var game = Path.Combine(root, "Modules");
            var workshop = Path.Combine(root, "workshop");
            Module(ModuleCatalog.ModulesRoot(server), "OpenSourceArmory", "OpenSourceArmory", "v1.18.0");
            Module(workshop, "3011479883", "OpenSourceArmory", "v2.0.0");
            Directory.CreateDirectory(game);

            var rows = new List<ModuleRow> { new() { Id = "OpenSourceArmory", Version = "v1.18.0" } };
            var refreshed = AD1259Server.RefreshWrongVersions(server, rows, AD1259Server.PcCopies(game, [workshop]));
            Assert.Equal(new[] { "Open Source Armory v2.0.0 from " + Path.Combine(workshop, "3011479883") }, refreshed);

            var mixed = new List<ModuleRow>
            {
                new() { Id = "OpenSourceArmory", Version = "v2.0.0" },
                new() { Id = "OpenSourceSaddlery", Version = "v1.8.0" },
                new() { Id = "OpenSourceWeaponry", Version = "v1.8.0" }
            };
            var line = AD1259Server.OpenSourceLine(mixed)!;
            Assert.StartsWith("The Open Source mods on the server are from two sets: Open Source Armory v2.0.0, Open Source Saddlery v1.8.0, Open Source Weaponry v1.8.0.", line);
            // A mix is told, never refused: the server's set check takes each accepted version.
            Assert.Null(AD1259Server.Problem(mixed.Concat(OtherServerRows()).ToList()));
            Assert.Equal(OpenSourceSet.ServerOlder + "\n" + OpenSourceSet.SameSet, AD1259Server.OpenSourceLine(
            [
                new() { Id = "OpenSourceArmory", Version = "v1.18.0.0" },
                new() { Id = "OpenSourceSaddlery", Version = "v1.8.0" },
                new() { Id = "OpenSourceWeaponry", Version = "v1.8.0" }
            ]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The Mods page Refresh on an unlocked AD1259 setup swapped a 1.x server copy for the Workshop 2.0 and logged
    // "Updated to the newer version". RefreshIds keeps the Open Source mods out of it in the AD1259 edition.
    [Fact]
    public void The_mods_refresh_keeps_a_1_x_server_copy_in_the_ad1259_edition()
    {
        var root = Temp();
        try
        {
            var server = Path.Combine(root, "DedicatedServer");
            var serverModules = ModuleCatalog.ModulesRoot(server);
            var game = Path.Combine(root, "Modules");
            var workshop = Path.Combine(root, "workshop");
            Module(serverModules, "OpenSourceArmory", "OpenSourceArmory", "v1.18.0");
            Module(game, "OpenSourceArmory", "OpenSourceArmory", "v1.18.0");
            Module(workshop, "3011479883", "OpenSourceArmory", "v2.0.0");
            string[] ids = ["OpenSourceArmory"];

            Assert.Equal(ids, AD1259Server.RefreshIds(ad1259: false, ids));
            Assert.Empty(AD1259Server.RefreshIds(ad1259: true, ids));
            Assert.Empty(ExtraModScan.UpdateFromNewerCopies(server, AD1259Server.RefreshIds(ad1259: true, ids), [workshop], [game]));
            Assert.Contains("v1.18.0", File.ReadAllText(Path.Combine(serverModules, "OpenSourceArmory", "SubModule.xml")));
            // Without it the refresh took the Workshop's 2.0 (what the Coop edition still does).
            Assert.Single(ExtraModScan.UpdateFromNewerCopies(server, AD1259Server.RefreshIds(ad1259: false, ids), [workshop], [game]));
            Assert.Contains("v2.0.0", File.ReadAllText(Path.Combine(serverModules, "OpenSourceArmory", "SubModule.xml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // A new locked setup copied the Workshop 2.0 in (CopyMissing through BetterCopy) for a host with the 1.x set in Modules.
    [Fact]
    public void A_new_setup_takes_the_accepted_modules_copy_over_the_workshop()
    {
        var root = Temp();
        try
        {
            var game = Path.Combine(root, "Modules");
            var workshop = Path.Combine(root, "workshop");
            Module(game, "OpenSourceArmory", "OpenSourceArmory", "v1.18.0");
            Module(game, "OpenSourceOld", "OpenSourceSaddlery", "v1.7.0");
            Module(workshop, "3011479883", "OpenSourceArmory", "v2.0.0");
            Module(workshop, "3010990914", "OpenSourceSaddlery", "v2.0.0");
            var gameArmory = Path.Combine(game, "OpenSourceArmory");
            var workshopArmory = Path.Combine(workshop, "3011479883");

            Assert.True(ExtraModScan.Preference(ad1259: true, gameArmory, false, workshopArmory, true, "OpenSourceArmory"));
            Assert.False(ExtraModScan.Preference(ad1259: true, workshopArmory, true, gameArmory, false, "OpenSourceArmory"));
            // A Modules copy of no accepted version decides nothing here, and the Coop edition keeps its own score.
            Assert.Null(ExtraModScan.Preference(ad1259: true, Path.Combine(game, "OpenSourceOld"), false, Path.Combine(workshop, "3010990914"), true, "OpenSourceSaddlery"));
            Assert.Null(ExtraModScan.Preference(ad1259: false, gameArmory, false, workshopArmory, true, "OpenSourceArmory"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_server_refusal_names_the_1_x_set_only_for_an_open_source_problem()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("en");
            var rows = OtherServerRows().Append(new ModuleRow { Id = "OpenSourceSaddlery", Version = "v1.8.0" })
                .Append(new ModuleRow { Id = "OpenSourceWeaponry", Version = "v1.8.0" }).ToList();
            var problem = AD1259Server.Problem(rows)!;
            Assert.Contains("Open Source Armory v2.0.0 (missing; get it from Steam Workshop)", problem);
            Assert.Contains(" " + OpenSourceSet.OlderToo, problem);

            rows.Add(new ModuleRow { Id = "OpenSourceArmory", Version = "v1.18.0" });
            rows.RemoveAll(row => row.Id == "RBM");
            Assert.DoesNotContain(OpenSourceSet.OlderToo, AD1259Server.Problem(rows));
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }

    // The two Start and Refresh call sites are in the WPF window, which this project doesn't build, so only their
    // wiring is checked here; RefreshIds itself is tested above.
    [Fact]
    public void Start_and_the_mods_refresh_both_pass_through_refresh_ids()
    {
        Assert.Contains("ExtraModScan.UpdateFromWorkshop(ServerRoot, AD1259Server.RefreshIds(Edition.IsAD1259, enabledIds), pins);",
            SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs")));
        Assert.Contains("ExtraModScan.UpdateFromNewerCopies(ServerRoot, AD1259Server.RefreshIds(Edition.IsAD1259, _modules.Select(row => row.Id)));",
            SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.Modules.cs")), "private void ReloadModules_Click("));
    }

    // Every other mod of the server set at its pin, and the compat module, so Problem only looks at the Open Source rows.
    private static IEnumerable<ModuleRow> OtherServerRows() =>
        AD1259Stack.ServerModules
            .Where(id => !OpenSourceSet.IsOpenSource(id) && !id.Equals("DedicatedServer.Windows", StringComparison.OrdinalIgnoreCase))
            .Select(id => id.Equals(AD1259Stack.CompatModuleId, StringComparison.OrdinalIgnoreCase)
                ? new ModuleRow { Id = id, Version = AD1259Stack.CompatVersion }
                : new ModuleRow { Id = id, Version = AD1259Stack.Find(id)!.Version });

    private static string Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "ad1259-osa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Module(string root, string folder, string id, string version)
    {
        var directory = Path.Combine(root, folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SubModule.xml"),
            "<Module><Id value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>");
    }
}
