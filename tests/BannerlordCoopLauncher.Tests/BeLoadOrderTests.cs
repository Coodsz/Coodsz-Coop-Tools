using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Parsing;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class BeLoadOrderTests
{
    private static TaomSetup Setup(params string[] ids)
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var id in ids)
            Assert.Null(BeLoadOrder.Add(setup, id, id, null));
        return setup;
    }

    private static TaomSetup Locked(string name)
    {
        var book = new BeSetupBook();
        book.Ensure();
        return book.Find(name)!;
    }

    // ----- The stack -----

    [Fact]
    public void The_burning_empires_setup_loads_butr_first_the_compat_module_right_before_burning_empires_and_elephants_right_after()
    {
        var client = BeLoadOrder.ClientOrder(Locked(BeSetupBook.CoreName));
        Assert.Equal(BeStack.ClientModules, client);
        Assert.Equal(BeStack.Butr.Select(mod => mod.ModuleId), client.Take(4));
        Assert.Equal(new[] { "Coop", "BurningEmpires.CoopCompat", "BurningEmpires", BeStack.ElephantsModuleId }, client.TakeLast(4));
        Assert.True(BeStack.CompatIsAdjacent(client));
        Assert.DoesNotContain("DedicatedServer.Windows", client);
        Assert.Null(BeLoadOrder.CoreProblem(client));
    }

    [Fact]
    public void The_server_loads_dedicated_server_windows_after_native_no_story_mode_and_elephants_last()
    {
        var server = BeLoadOrder.ServerOrder(Locked(BeSetupBook.CoreName));
        Assert.Equal(BeStack.ServerModules, server);
        Assert.Equal("DedicatedServer.Windows", server[server.ToList().IndexOf("Native") + 1]);
        Assert.DoesNotContain("StoryMode", server);
        Assert.DoesNotContain("CustomBattle", server);
        Assert.Equal(new[] { "BurningEmpires", BeStack.ElephantsModuleId }, server.TakeLast(2));
        Assert.Null(BeLoadOrder.CoreProblem(server, BeStack.ServerModules));
    }

    [Fact]
    public void Elephants_is_core_on_both_sides_so_every_battle_has_the_elephant_extension()
    {
        // Live test: without Elephants every battle showed "Elephant extension could not start".
        Assert.Contains(BeStack.ElephantsModuleId, BeStack.ClientModules);
        Assert.Contains(BeStack.ElephantsModuleId, BeStack.ServerModules);
        Assert.True(BeLoadOrder.IsCore(BeStack.ElephantsModuleId));
        Assert.Contains("Burning Empires core", BeLoadOrder.RefusalReason(BeStack.ElephantsModuleId));
        Assert.Contains(BeStack.ElephantsModuleId, BeLoadOrder.BeBlock);
        Assert.Contains(BeStack.ElephantsModuleId, BeLoadOrder.ServerOrder(Setup("Other")));
    }

    [Fact]
    public void Extras_load_before_coop_or_after_elephants_and_never_inside_the_block()
    {
        var setup = Setup("After", "Before");
        BeLoadOrder.MoveTo(setup, "Before", BeLoadOrder.SlotBeforeCoop, 0);
        var order = BeLoadOrder.ClientOrder(setup).ToList();
        Assert.Equal(order.IndexOf("CustomBattle") + 1, order.IndexOf("Before"));
        Assert.Equal(order.IndexOf("Before") + 1, order.IndexOf("Coop"));
        Assert.Equal(new[] { "BurningEmpires", BeStack.ElephantsModuleId, "After" }, order.TakeLast(3));
        // A setup written with the TAOM "before Coop" slot (between the content and Coop) reads as after Burning Empires.
        setup.Extras.Single(mod => mod.ModuleId == "After").Slot = ExtraSlot.BeforeCoop;
        BeLoadOrder.Normalize(setup);
        Assert.Equal(new[] { "Before", "After" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Equal(BeLoadOrder.SlotAfterBe, setup.Extras[1].Slot);
        Assert.Equal("ModLast", BeLoadOrder.LaunchModules(Setup("ModLast"), joinPassword: false)[^1]);
        Assert.Equal(ClientAddOns.JoinPasswordId, BeLoadOrder.LaunchModules(Setup("ModLast"), joinPassword: true)[^1]);
    }

    [Fact]
    public void An_elephants_extra_saved_before_it_joined_the_core_is_dropped_and_loads_once()
    {
        var setup = new TaomSetup { Name = "Old copy" };
        setup.Extras.Add(new TaomExtraMod { Name = BeStack.ElephantsName, ModuleId = BeStack.ElephantsModuleId, WorkshopId = "0", Enabled = true, Slot = BeLoadOrder.SlotAfterBe });
        setup.Extras.Add(new TaomExtraMod { Name = "A", ModuleId = "A", WorkshopId = "0", Enabled = true, Slot = BeLoadOrder.SlotAfterBe });
        // Before it is cleaned up, the order still has Elephants once, in the core.
        var order = BeLoadOrder.ClientOrder(setup);
        Assert.Single(order, id => id == BeStack.ElephantsModuleId);
        Assert.Null(BeLoadOrder.CoreProblem(order));
        Assert.DoesNotContain(BeLoadOrder.ServerRows(setup), row => !row.IsCore && row.ModuleId == BeStack.ElephantsModuleId);
        BeLoadOrder.Normalize(setup);
        Assert.Equal(new[] { "A" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.True(BeLoadOrder.Move(setup, "A", -1));
        Assert.Equal(BeLoadOrder.SlotBeforeCoop, setup.Extras.Single().Slot);
    }

    [Fact]
    public void Butr_and_elephants_are_core_and_taom_and_old_coop_are_refused()
    {
        foreach (var butr in BeStack.Butr)
        {
            Assert.True(BeLoadOrder.IsCore(butr.ModuleId));
            Assert.Contains("Burning Empires core", BeLoadOrder.RefusalReason(butr.ModuleId));
        }

        foreach (var taom in new[] { "TAOM", "TAOM.Dependencies", "TAOM_Map", "LOTRLOME_Armory", "TAOM.CoopCompat" })
            Assert.Contains("part of TAOM", BeLoadOrder.RefusalReason(taom));
        Assert.Contains("old Coop copy", BeLoadOrder.RefusalReason("CoopNightly"));
        Assert.NotNull(BeLoadOrder.RefusalReason("DedicatedServer.CoodszJoinPassword"));
        Assert.NotNull(BeLoadOrder.RefusalReason("Multiplayer"));
        Assert.NotNull(BeLoadOrder.RefusalReason("be:Mod"));
        // TAOM's own mods that are not its core are ordinary picks.
        Assert.Null(BeLoadOrder.RefusalReason("TAOM_PlayerVoices"));
        Assert.True(BeLoadOrder.IsBeModule(BeStack.ElephantsModuleId));
        Assert.True(BeLoadOrder.IsBeModule("BurningEmpires.CoopCompat"));
        Assert.False(BeLoadOrder.IsBeModule("RTSCamera"));
    }

    [Fact]
    public void Core_problem_names_what_is_wrong()
    {
        var good = BeStack.ClientModules.ToList();
        Assert.Contains("Harmony, ButterLib", BeLoadOrder.CoreProblem(good.Skip(1).Prepend("Other").ToList()));
        Assert.Contains("twice", BeLoadOrder.CoreProblem(good.Append("Native").ToList()));
        var beFirst = good.Take(9).Concat(new[] { "BurningEmpires", "Coop", "BurningEmpires.CoopCompat", BeStack.ElephantsModuleId }).ToList();
        Assert.Contains("right before Burning Empires", BeLoadOrder.CoreProblem(beFirst));
        var split = good.Take(10).Append("Mod").Concat(good.Skip(10)).ToList();
        Assert.Contains("stay together", BeLoadOrder.CoreProblem(split));
        Assert.Contains("Coop is missing", BeLoadOrder.CoreProblem(good.Where(id => id != "Coop").ToList()));
        var withoutElephants = good.Where(id => id != BeStack.ElephantsModuleId).ToList();
        Assert.Contains("right after Burning Empires", BeLoadOrder.CoreProblem(withoutElephants));
        Assert.Contains("right after Burning Empires", BeLoadOrder.CoreProblem(withoutElephants.Append("Mod").Append(BeStack.ElephantsModuleId).ToList()));
        Assert.Null(BeLoadOrder.CoreProblem(good.Take(9).Append("Mod").Concat(good.Skip(9)).Append("After").ToList()));
    }

    [Fact]
    public void Server_rows_show_every_extra_in_its_place()
    {
        var setup = Setup("After", "Before");
        BeLoadOrder.MoveTo(setup, "Before", BeLoadOrder.SlotBeforeCoop, 0);
        BeLoadOrder.SetEnabled(setup, "After", false);
        var rows = BeLoadOrder.ServerRows(setup);
        var ids = rows.Select(row => row.ModuleId).ToList();
        Assert.Equal(BeStack.ServerModules.Take(8).Append("Before").Concat(BeStack.ServerModules.Skip(8)).Append("After"), ids);
        Assert.Equal(BeStack.ElephantsModuleId, ids[ids.IndexOf("After") - 1]);
        Assert.All(rows.Where(row => !row.IsCore), row => Assert.Equal(row.ModuleId, setup.Extras[row.ExtraIndex].ModuleId));
        Assert.DoesNotContain("After", BeLoadOrder.ServerOrder(setup));
    }

    [Fact]
    public void Rts_camera_sorts_last()
    {
        Assert.Equal(1, BeLoadOrder.LastRank("RTSCamera"));
        Assert.Equal(0, BeLoadOrder.LastRank("Anything"));
    }

    // ----- Setups -----

    [Fact]
    public void A_new_book_has_the_one_locked_setup_and_keeps_it_exact()
    {
        var book = new BeSetupBook();
        book.Setups.Add(new TaomSetup { Name = "burning EMPIRES", Locked = false, Extras = new() { new TaomExtraMod { ModuleId = "Added" } } });
        book.Setups.Add(new TaomSetup { Name = "Mine", Locked = true });
        book.Ensure();
        Assert.Equal(new[] { BeSetupBook.CoreName, "Mine" }, book.Setups.Select(setup => setup.Name));
        Assert.True(book.Setups[0].Locked);
        Assert.Empty(book.Setups[0].Extras);
        Assert.False(book.Setups[1].Locked);
        Assert.Equal(BeSetupBook.CoreName, book.Active);
        Assert.Throws<InvalidOperationException>(() => book.Delete(BeSetupBook.CoreName));
        Assert.Throws<InvalidOperationException>(() => book.Rename(BeSetupBook.CoreName, "Other"));
        Assert.Throws<InvalidOperationException>(() => BeLoadOrder.Add(book.Setups[0], "X", "X", null));

        var copy = book.CopyOf(BeSetupBook.CoreName, "Burning Empires and more");
        Assert.Null(BeLoadOrder.Add(copy, "More", "More", "123"));
        Assert.Equal(new[] { "More" }, copy.Extras.Select(mod => mod.ModuleId));
        book.Delete("Burning Empires and more");
        Assert.Equal(BeSetupBook.CoreName, book.Active);
    }

    [Theory]
    [InlineData("Plain Burning Empires")]
    [InlineData("Burning Empires with Elephants")]
    [InlineData("plain burning empires")]
    public void Settings_that_name_an_old_locked_setup_map_to_the_burning_empires_setup(string oldName)
    {
        var book = new BeSetupBook { Active = oldName };
        book.Setups.Add(new TaomSetup { Name = "Plain Burning Empires", Locked = true });
        book.Setups.Add(new TaomSetup
        {
            Name = "Burning Empires with Elephants",
            Locked = true,
            Extras = new() { new TaomExtraMod { Name = BeStack.ElephantsName, ModuleId = BeStack.ElephantsModuleId, WorkshopId = "0", Enabled = true, Slot = BeLoadOrder.SlotAfterBe } }
        });
        book.Setups.Add(new TaomSetup
        {
            Name = "Mine",
            Extras = new() { new TaomExtraMod { Name = BeStack.ElephantsName, ModuleId = BeStack.ElephantsModuleId, WorkshopId = "0", Enabled = true, Slot = BeLoadOrder.SlotAfterBe } }
        });
        book.Ensure();
        Assert.Equal(new[] { BeSetupBook.CoreName, "Mine" }, book.Setups.Select(setup => setup.Name));
        Assert.Equal(BeSetupBook.CoreName, book.Active);
        Assert.Equal(BeSetupBook.CoreName, book.Current().Name);
        Assert.Empty(book.Setups[1].Extras);
        // The old names still find the setup, switch to it and cannot be taken again.
        Assert.Same(book.Setups[0], book.Find(oldName));
        book.Switch("Mine");
        book.Switch(oldName);
        Assert.Equal(BeSetupBook.CoreName, book.Active);
        Assert.Throws<InvalidOperationException>(() => book.Create(oldName));
        Assert.Throws<InvalidOperationException>(() => book.Rename("Mine", oldName));
    }

    [Fact]
    public void Client_settings_load_with_the_locked_setup_and_the_tested_port()
    {
        var settings = BeClientSettings.Parse("{ \"Host\": \"1.2.3.4\", \"BeArchive\": \"C:\\\\be.7z\" }");
        Assert.Equal("1.2.3.4", settings.Host);
        Assert.Equal("C:\\be.7z", settings.BeArchive);
        Assert.Equal(BeStack.DirectPort, settings.Port);
        Assert.Equal(new[] { BeSetupBook.CoreName }, settings.ModSetups.Setups.Select(setup => setup.Name));
        settings.ModSetups.Create("Mine");
        var again = BeClientSettings.Parse(settings.Serialize());
        Assert.Equal("Mine", again.ModSetups.Active);
        var broken = BeClientSettings.Parse("not json");
        Assert.Equal(BeSetupBook.CoreName, broken.ModSetups.Active);
        Assert.Single(broken.ModSetups.Setups);
        // A be-launcher.json from the first tools with the Elephants setup picked opens on the new one.
        var old = BeClientSettings.Parse("{ \"ModSetups\": { \"Active\": \"Burning Empires with Elephants\", \"Setups\": [ { \"Name\": \"Plain Burning Empires\", \"Locked\": true }, { \"Name\": \"Burning Empires with Elephants\", \"Locked\": true } ] } }");
        Assert.Equal(BeSetupBook.CoreName, old.ModSetups.Active);
        Assert.Equal(new[] { BeSetupBook.CoreName }, old.ModSetups.Setups.Select(setup => setup.Name));
    }

    // ----- Auto sort and checks -----

    private static ModuleManifest M(string id, string body = "") =>
        ModuleManifestReader.Parse("<Module><Id value=\"" + id + "\"/><Name value=\"" + id + "\"/><Version value=\"v1.0.0\"/>" + body + "<SubModules/></Module>")!;

    [Fact]
    public void Auto_sort_puts_a_mod_burning_empires_wants_first_before_coop_and_the_rest_after()
    {
        var setup = Setup("Free", "LoadsFirst", "RTSCamera", "NeedsBe");
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase)
        {
            ["LoadsFirst"] = M("LoadsFirst", "<ModulesToLoadAfterThis><Module Id=\"BurningEmpires\"/></ModulesToLoadAfterThis>"),
            ["NeedsBe"] = M("NeedsBe", "<DependedModules><DependedModule Id=\"BurningEmpires\"/></DependedModules>")
        };
        var lines = BeAutoSort.Sort(setup, manifests);
        Assert.Equal(new[] { "LoadsFirst", "Free", "NeedsBe", "RTSCamera" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Equal(BeLoadOrder.SlotBeforeCoop, setup.Extras[0].Slot);
        Assert.Contains("LoadsFirst now loads before Coop.", lines);
    }

    [Fact]
    public void A_mod_with_its_own_harmony_is_a_clash_but_its_own_mcm_is_not()
    {
        var setup = Setup("Bundler", "McmOnly");
        var facts = new Dictionary<string, TaomModFact>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bundler"] = new("Bundler", M("Bundler"), new[] { "C:\\Mods\\Bundler" }, new[] { "Harmony", "UIExtenderEx" }),
            ["McmOnly"] = new("McmOnly", M("McmOnly", "<DependedModules><DependedModule Id=\"Bannerlord.MBOptionScreen\"/></DependedModules>"), new[] { "C:\\Mods\\McmOnly" }, Array.Empty<string>())
        };
        var issues = BeModChecks.Run(setup, facts);
        var clash = Assert.Single(issues);
        Assert.False(clash.Blocks);
        Assert.Contains("Bundler brings its own Harmony, UIExtenderEx", clash.Text);
        Assert.DoesNotContain("MCMv5.dll", BeModFacts.SharedLibraries.Keys);
        // Needing a BUTR module is fine: it is in the core.
        Assert.DoesNotContain(issues, issue => issue.Text.Contains("McmOnly"));
    }

    [Fact]
    public void A_mod_that_needs_elephants_is_fine_because_elephants_is_core()
    {
        var setup = Setup("ElephantPatch");
        var facts = new Dictionary<string, TaomModFact>(StringComparer.OrdinalIgnoreCase)
        {
            ["ElephantPatch"] = new("ElephantPatch", M("ElephantPatch", "<DependedModules><DependedModule Id=\"BurningEmpires_Elephants\"/></DependedModules>"), new[] { "C:\\Mods\\ElephantPatch" }, Array.Empty<string>())
        };
        Assert.Empty(BeModChecks.Run(setup, facts));
        Assert.Contains("Burning Empires core", BeLoadOrder.Add(setup, BeStack.ElephantsName, BeStack.ElephantsModuleId, null));
    }
}
