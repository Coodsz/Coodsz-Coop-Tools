using BannerlordCoopLauncher.Core.Parsing;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomModRulesTests
{
    private static string Xml(string id, string body = "", string version = "v1.0.0") =>
        "<Module><Id value=\"" + id + "\"/><Name value=\"" + id + " name\"/><Version value=\"" + version + "\"/>" + body + "<SubModules/></Module>";

    private static ModuleManifest M(string id, string body = "") => ModuleManifestReader.Parse(Xml(id, body))!;

    private static TaomSetup Setup(params string[] ids)
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var id in ids)
            Assert.Null(TaomLoadOrder.Add(setup, id, id, null));
        return setup;
    }

    private static IReadOnlyDictionary<string, TaomModFact> Facts(params ModuleManifest[] manifests) =>
        manifests.ToDictionary(m => m.Id, m => new TaomModFact(m.Id, m, new[] { "C:\\Mods\\" + m.Id }, Array.Empty<string>()), StringComparer.OrdinalIgnoreCase);

    // ----- SubModule.xml reading -----

    [Fact]
    public void The_manifest_reads_every_kind_of_load_order_rule()
    {
        var manifest = ModuleManifestReader.Parse(Xml("A",
            "<DependedModules><DependedModule Id=\"Native\" DependentVersion=\"v1.2.9\"/><DependedModule Id=\"B\" Optional=\"true\"/></DependedModules>"
            + "<DependedModuleMetadatas><DependedModuleMetadata id=\"C\" order=\"LoadBeforeThis\"/><DependedModuleMetadata id=\"B\" order=\"LoadBeforeThis\" optional=\"false\"/>"
            + "<DependedModuleMetadata id=\"D\" order=\"LoadAfterThis\"/><DependedModuleMetadata id=\"E\" incompatible=\"true\"/></DependedModuleMetadatas>"
            + "<ModulesToLoadAfterThis><Module Id=\"F\"/></ModulesToLoadAfterThis><IncompatibleModules><Module Id=\"G\"/></IncompatibleModules>"))!;
        Assert.Equal("v1.2.9", manifest.NativeVersion);
        Assert.Equal(new[] { "Native", "B", "C" }, manifest.Needs.Select(link => link.Id));
        Assert.Equal(new[] { "Native", "B", "C" }, manifest.RequiredIds);
        Assert.Equal(new[] { "D", "F" }, manifest.LoadsBefore);
        Assert.Equal(new[] { "E", "G" }, manifest.Incompatible);
        Assert.Null(ModuleManifestReader.Parse("<not xml"));
        Assert.Null(ModuleManifestReader.Parse("<Module/>"));
    }

    // ----- Auto sort -----

    [Fact]
    public void Auto_sort_puts_each_mod_after_the_mods_it_needs()
    {
        var setup = Setup("C", "B", "A");
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = M("A"),
            ["B"] = M("B", "<DependedModules><DependedModule Id=\"A\"/></DependedModules>"),
            ["C"] = M("C", "<DependedModuleMetadatas><DependedModuleMetadata id=\"B\" order=\"LoadBeforeThis\"/></DependedModuleMetadatas>")
        };
        var lines = TaomAutoSort.Sort(setup, manifests);
        Assert.Equal(new[] { "A", "B", "C" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Single(lines);
        Assert.Empty(TaomAutoSort.Sort(setup, manifests));
    }

    [Fact]
    public void Auto_sort_follows_the_taom_rules_for_where_a_mod_goes()
    {
        var setup = Setup("NeedsTaom", "LoadsFirst", "Alliance.Wargs", "Free");
        TaomLoadOrder.MoveTo(setup, "NeedsTaom", ExtraSlot.BeforeTaom, 0);
        TaomLoadOrder.MoveTo(setup, "Free", ExtraSlot.BeforeTaom, 0);
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase)
        {
            ["NeedsTaom"] = M("NeedsTaom", "<DependedModules><DependedModule Id=\"TAOM\"/></DependedModules>"),
            ["LoadsFirst"] = M("LoadsFirst", "<ModulesToLoadAfterThis><Module Id=\"TAOM\"/></ModulesToLoadAfterThis>"),
            ["Alliance.Wargs"] = M("Alliance.Wargs", "<DependedModules><DependedModule Id=\"TAOM\"/></DependedModules>")
        };
        var lines = TaomAutoSort.Sort(setup, manifests);
        var slots = setup.Extras.ToDictionary(mod => mod.ModuleId, mod => mod.Slot);
        Assert.Equal(ExtraSlot.AfterTaom, slots["NeedsTaom"]);
        Assert.Equal(ExtraSlot.BeforeTaom, slots["LoadsFirst"]);
        Assert.Equal(ExtraSlot.BeforeCoop, slots["Alliance.Wargs"]);
        // The TAOM developer: every extra mod goes below all TAOM files unless TAOM asks for it first.
        Assert.Equal(ExtraSlot.AfterTaom, slots["Free"]);
        Assert.Equal(4, lines.Count);

        var order = TaomLoadOrder.ClientOrder(setup);
        Assert.Null(TaomLoadOrder.CoreProblem(order));
        Assert.Equal("Alliance.Wargs", order[order.ToList().IndexOf("Coop") - 1]);
        Assert.True(TaomStack.CoopIsAdjacent(order));
    }

    [Fact]
    public void Only_mods_that_coop_compat_wants_first_may_sit_before_coop()
    {
        var setup = Setup("ModA", "Alliance.Wargs");
        TaomLoadOrder.MoveTo(setup, "ModA", ExtraSlot.BeforeCoop, 0);
        Assert.Equal(ExtraSlot.AfterTaom, setup.Extras.Single(mod => mod.ModuleId == "ModA").Slot);
        // Moving up from after TAOM: ModA skips the place before Coop, Alliance.Wargs stops there.
        Assert.True(TaomLoadOrder.Move(setup, "Alliance.Wargs", -1));
        Assert.True(TaomLoadOrder.Move(setup, "Alliance.Wargs", -1));
        Assert.Equal(ExtraSlot.BeforeCoop, setup.Extras.Single(mod => mod.ModuleId == "Alliance.Wargs").Slot);
        Assert.True(TaomLoadOrder.Move(setup, "Alliance.Wargs", -1));
        Assert.Equal(ExtraSlot.BeforeTaom, setup.Extras.Single(mod => mod.ModuleId == "Alliance.Wargs").Slot);
        Assert.True(TaomLoadOrder.Move(setup, "ModA", -1));
        Assert.Equal(ExtraSlot.BeforeTaom, setup.Extras.Single(mod => mod.ModuleId == "ModA").Slot);

        var core = TaomStack.ClientModules.ToList();
        core.Insert(core.IndexOf("Coop"), "Alliance.Wargs");
        Assert.Null(TaomLoadOrder.CoreProblem(core));
    }

    [Fact]
    public void Auto_sort_keeps_the_old_order_for_mods_that_need_each_other()
    {
        var setup = Setup("A", "B");
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = M("A", "<DependedModules><DependedModule Id=\"B\"/></DependedModules>"),
            ["B"] = M("B", "<DependedModules><DependedModule Id=\"A\"/></DependedModules>")
        };
        var lines = TaomAutoSort.Sort(setup, manifests);
        Assert.Equal(new[] { "A", "B" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Contains("each ask to load first", Assert.Single(lines));
        Assert.Throws<InvalidOperationException>(() => TaomAutoSort.Sort(new TaomSetup { Name = "Plain TAOM", Locked = true }, manifests));
    }

    // ----- Pre launch checks -----

    [Fact]
    public void A_missing_or_turned_off_dependency_and_an_incompatible_pair_block()
    {
        var setup = Setup("A", "B", "C", "Off");
        TaomLoadOrder.SetEnabled(setup, "Off", false);
        var facts = Facts(
            M("A", "<DependedModules><DependedModule Id=\"Missing\"/><DependedModule Id=\"Off\"/><DependedModule Id=\"Optional\" Optional=\"true\"/></DependedModules>"),
            M("B", "<IncompatibleModules><Module Id=\"C\"/></IncompatibleModules>"),
            M("C", "<DependedModuleMetadatas><DependedModuleMetadata id=\"B\" incompatible=\"true\"/></DependedModuleMetadatas>"),
            M("Off"));
        var issues = TaomModChecks.Run(setup, facts);
        Assert.All(issues, issue => Assert.True(issue.Blocks));
        Assert.Contains(issues, issue => issue.Text == "A needs Missing, which is not on this setup.");
        Assert.Contains(issues, issue => issue.Text == "A needs Off, which is turned off.");
        Assert.Single(issues, issue => issue.Text.Contains("does not work together"));
        Assert.Equal(3, issues.Count);
    }

    [Fact]
    public void Core_modules_and_the_libraries_inside_taom_dependencies_count_as_present()
    {
        var setup = Setup("A");
        var facts = Facts(M("A", "<DependedModules><DependedModule Id=\"Native\" DependentVersion=\"v1.4.8\"/><DependedModule Id=\"Bannerlord.Harmony\"/>"
            + "<DependedModule Id=\"Bannerlord.MBOptionScreen\"/><DependedModule Id=\"TAOM\"/><DependedModule Id=\"CoopNightly\"/><DependedModule Id=\"BirthAndDeath\"/></DependedModules>"));
        Assert.Empty(TaomModChecks.Run(setup, facts));
    }

    [Fact]
    public void Wrong_order_other_game_version_bundled_libraries_and_duplicates_only_warn()
    {
        var setup = Setup("A", "B", "Dismember");
        var facts = new Dictionary<string, TaomModFact>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new("A", M("A", "<DependedModules><DependedModule Id=\"B\"/><DependedModule Id=\"Native\" DependentVersion=\"v1.2.12\"/></DependedModules>"), new[] { "W\\A", "M\\A" }, new[] { "Harmony", "MCM" }),
            ["B"] = new("B", M("B"), new[] { "M\\B" }, Array.Empty<string>()),
            ["Dismember"] = new("Dismember", M("DismembermentPlus"), new[] { "W\\D" }, new[] { "Harmony" })
        };
        // A known bundler from TAOM.Dependencies ModulesToLoadAfterThis is not warned about.
        facts["Dismember"] = facts["Dismember"] with { Id = "Dismember" };
        var issues = TaomModChecks.Run(setup, facts);
        Assert.All(issues, issue => Assert.False(issue.Blocks));
        Assert.Contains(issues, issue => issue.Text == "A loads before B, which it needs. Click Auto sort.");
        Assert.Contains(issues, issue => issue.Text.StartsWith("A was made for Bannerlord v1.2.12"));
        Assert.Contains(issues, issue => issue.Text.StartsWith("A brings its own Harmony, MCM."));
        Assert.Contains(issues, issue => issue.Text.StartsWith("A is on this PC 2 times."));
        Assert.Contains(issues, issue => issue.Text.StartsWith("Dismember brings its own"));

        var known = Setup("DismembermentPlus");
        var knownFacts = new Dictionary<string, TaomModFact>(StringComparer.OrdinalIgnoreCase)
        {
            ["DismembermentPlus"] = new("DismembermentPlus", M("DismembermentPlus"), new[] { "W\\D" }, new[] { "Harmony" })
        };
        Assert.Empty(TaomModChecks.Run(known, knownFacts));
    }

    [Theory]
    [InlineData("v1.4.8", true)]
    [InlineData("v1.4.0", true)]
    [InlineData("e1.4.5", true)]
    [InlineData("v1.2.12", false)]
    [InlineData("v1.3.0.12345", false)]
    [InlineData("something", true)]
    public void Game_versions_match_by_release_line(string version, bool same) =>
        Assert.Equal(same, TaomModChecks.SameGame(version, TaomStack.GameVersion));

    [Fact]
    public void Facts_find_every_copy_and_the_libraries_a_mod_brings()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-facts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workshop = Path.Combine(root, "workshop", "123", "ModA");
            var modules = Path.Combine(root, "Modules", "ModA");
            foreach (var dir in new[] { workshop, modules })
            {
                Directory.CreateDirectory(Path.Combine(dir, "bin", "Win64_Shipping_Client"));
                File.WriteAllText(Path.Combine(dir, "SubModule.xml"), Xml("ModA"));
            }

            File.WriteAllText(Path.Combine(workshop, "bin", "Win64_Shipping_Client", "0Harmony.dll"), "x");
            var facts = TaomModFacts.Gather(new[] { Path.Combine(root, "workshop"), Path.Combine(root, "Modules") });
            var fact = facts["moda"];
            Assert.Equal(2, fact.Copies.Count);
            Assert.Equal(new[] { "Harmony" }, fact.BundledLibs);
            Assert.Equal("ModA", fact.Manifest!.Id);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
