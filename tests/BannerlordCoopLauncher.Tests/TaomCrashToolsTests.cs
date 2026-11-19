using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Saves;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomCrashToolsTests
{
    private static TaomSetup Setup(params string[] ids)
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var id in ids)
            TaomLoadOrder.Add(setup, id, id, null);
        return setup;
    }

    // Runs the search against a pretend game that crashes whenever the culprit is turned on.
    private static (string? Found, int Rounds) Search(TaomSetup setup, string culprit, IReadOnlyDictionary<string, ModuleManifest>? manifests = null)
    {
        var search = CrashBisect.Start(setup, manifests);
        var rounds = 0;
        while (!search.Done)
        {
            rounds++;
            var test = search.TestSetup();
            search.Report(test.EnabledExtras.Any(mod => mod.ModuleId == culprit));
        }

        return (search.Found, rounds);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("D")]
    [InlineData("G")]
    [InlineData("H")]
    public void Find_the_crashing_mod_narrows_down_to_one_mod(string culprit)
    {
        var setup = Setup("A", "B", "C", "D", "E", "F", "G", "H");
        var (found, rounds) = Search(setup, culprit);
        Assert.Equal(culprit, found);
        Assert.Equal(3, rounds);
    }

    [Fact]
    public void Each_round_keeps_the_players_setup_and_core_untouched()
    {
        var setup = Setup("A", "B", "C");
        TaomLoadOrder.SetEnabled(setup, "C", false);
        var before = System.Text.Json.JsonSerializer.Serialize(setup);
        var search = CrashBisect.Start(setup);
        Assert.Equal(new[] { "A", "B" }, search.Suspects);
        var test = search.TestSetup();
        Assert.Equal(new[] { "A" }, test.EnabledExtras.Select(mod => mod.ModuleId));
        Assert.Null(TaomLoadOrder.CoreProblem(TaomLoadOrder.ClientOrder(test)));
        search.Report(false);
        Assert.True(search.Done);
        Assert.Equal("B", search.Found);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(setup));
        Assert.Throws<InvalidOperationException>(() => CrashBisect.Start(Setup()));
    }

    [Fact]
    public void A_mod_under_test_brings_the_extras_it_needs()
    {
        var setup = Setup("Lib", "B", "Uses");
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase)
        {
            ["Uses"] = ModuleManifestReader.Parse("<Module><Id value=\"Uses\"/><DependedModules><DependedModule Id=\"Lib\"/></DependedModules></Module>")!
        };
        var search = CrashBisect.Start(setup, manifests);
        search.Report(false);
        // Lib and B were tested and fine; Uses is left, and its round turns Lib on too.
        Assert.Equal("Uses", search.Found);
        Assert.Contains("Lib", search.TurnedOn);
        Assert.Equal("Uses", Search(setup, "Uses", manifests).Found);
    }

    [Fact]
    public void Joining_the_same_host_with_other_mods_is_warned_about()
    {
        var key = JoinMemory.Key(false, "", " 192.168.1.10 ", "4200");
        Assert.Equal("direct:192.168.1.10:4200", key);
        Assert.Equal("steam:host name", JoinMemory.Key(true, "Host Name", "", ""));
        var last = new[] { "TAOM", "Coop", "TAOM.CoopCompat", "ModA" };
        Assert.Null(JoinMemory.Problem(null, last));
        Assert.Null(JoinMemory.Problem(last, last.Append("DedicatedServer.CoodszJoinPassword").ToList()));
        var problem = JoinMemory.Problem(last, new[] { "TAOM", "Coop", "TAOM.CoopCompat", "ModB" });
        Assert.Contains("This time you add ModB.", problem);
        Assert.Contains("This time you leave out ModA.", problem);
    }

    [Fact]
    public void Old_settings_get_an_empty_join_memory_and_keep_it_after_saving()
    {
        var settings = TaomClientSettings.Parse("{\"Host\":\"1.2.3.4\"}");
        Assert.Empty(settings.LastJoins);
        settings.LastJoins["direct:1.2.3.4:4200"] = new List<string> { "Coop", "ModA" };
        var back = TaomClientSettings.Parse(settings.Serialize());
        Assert.Equal(new[] { "Coop", "ModA" }, back.LastJoins["DIRECT:1.2.3.4:4200"]);
    }

    [Fact]
    public void A_save_backup_can_be_made_listed_and_put_back()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var saves = Path.Combine(root, "Game Saves");
            var backups = Path.Combine(root, "save-backups");
            Directory.CreateDirectory(saves);
            var save = Path.Combine(saves, "camp.sav");
            File.WriteAllText(save, "old world");
            File.WriteAllText(Path.ChangeExtension(save, ".json"), "old json");

            var backup = SaveBackup.Create(save, backups, new DateTime(2026, 10, 17, 12, 0, 0));
            Assert.Equal("camp-20261017-120000.sav", Path.GetFileName(backup));
            Assert.True(File.Exists(Path.ChangeExtension(backup, ".json")));
            var second = SaveBackup.Create(save, backups, new DateTime(2026, 10, 17, 12, 0, 0));
            Assert.Equal("camp-20261017-120000-2.sav", Path.GetFileName(second));
            Assert.Equal("camp", SaveBackup.SaveNameOf(second));
            Assert.Null(SaveBackup.SaveNameOf("camp.sav"));
            Assert.Equal(2, SaveBackup.List(backups, "camp").Count);

            File.WriteAllText(save, "new world with mods");
            var undo = SaveBackup.Restore(backup, saves, backups);
            Assert.Equal("old world", File.ReadAllText(save));
            Assert.Equal("old json", File.ReadAllText(Path.ChangeExtension(save, ".json")));
            Assert.Equal("new world with mods", File.ReadAllText(undo!));
            Assert.Throws<InvalidOperationException>(() => SaveBackup.Create(Path.Combine(saves, "none.sav"), backups));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
