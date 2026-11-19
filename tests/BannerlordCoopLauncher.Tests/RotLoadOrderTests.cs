using BannerlordCoopLauncher.Core.Parsing;
using BurningEmpires.Core;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class RotLoadOrderTests
{
    private static TaomSetup Setup(params string[] ids)
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var id in ids)
            Assert.Null(RotLoadOrder.Add(setup, id, id, null));
        return setup;
    }

    private static TaomSetup Plain()
    {
        var book = new RotSetupBook();
        book.Ensure();
        return book.Find(RotSetupBook.CoreName)!;
    }

    private static ModuleManifest Manifest(string id, string[]? needs = null, string[]? loadsBefore = null, string? native = null)
    {
        var links = (needs ?? []).Select(need => new ModuleLink(need, null, false)).ToList();
        if (native != null)
            links.Add(new ModuleLink("Native", native, false));
        return new ModuleManifest(id, id, "v1.0.0", links, loadsBefore ?? [], [], []);
    }

    // ----- The stack -----

    [Fact]
    public void Plain_loads_exactly_the_client_order_of_the_join_tests()
    {
        // rot-wave8 client logs try1 and try2, rgl_log line 144 (Command Args).
        var joinTests = "Bannerlord.Harmony*Bannerlord.ButterLib*Bannerlord.UIExtenderEx*Bannerlord.MBOptionScreen*Native*SandBoxCore*Sandbox*CustomBattle*StoryMode*NavalDLC*CoopNightly*ROT-Core*ROT-Content*ROT_Map*ROT-Dragon*RealmOfThrones.CoopCompat".Split('*');
        Assert.Equal(joinTests, RotLoadOrder.ClientOrder(Plain()));
        Assert.Equal(joinTests, RotStack.ClientModules);
        Assert.Null(RotLoadOrder.CoreProblem(joinTests));
    }

    [Fact]
    public void Plain_gives_the_server_list_without_the_modules_the_server_always_loads()
    {
        var server = RotLoadOrder.ServerOrder(Plain());
        Assert.Equal(new[]
        {
            "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen",
            "ROT-Core", "ROT-Content", "ROT_Map", "ROT-Dragon", "RealmOfThrones.CoopCompat"
        }, server);
        foreach (var fixedId in RotStack.ServerLoadsFirst.Append(RotStack.ServerLoadsLast))
            Assert.DoesNotContain(fixedId, server);
        Assert.Equal(new[] { "Native", "SandBoxCore", "Sandbox", RotStack.CoopModuleId, "NavalDLC" }, RotStack.ServerLoadsFirst);
        Assert.Equal(RotStack.ServerModules.Select(id => new TaomOrderRow(id, -1)), RotLoadOrder.ServerRows(Plain()));
    }

    [Fact]
    public void The_coop_id_lives_in_one_place_and_the_other_coop_is_refused()
    {
        Assert.Equal("CoopNightly", RotStack.CoopModuleId);
        Assert.Equal("v0.1.8", RotStack.CoopVersion);
        Assert.Contains(RotStack.CoopModuleId, RotStack.ClientModules);
        Assert.Equal(new[] { "Coop" }, RotStack.OtherCoopModuleIds);
        Assert.Contains("old Coop copy", RotLoadOrder.RefusalReason("Coop"));
        Assert.Contains("core", RotLoadOrder.RefusalReason("CoopNightly"));
    }

    [Fact]
    public void The_pinned_versions_match_the_realm_of_thrones_files()
    {
        Assert.Equal("v8.1.8", RotStack.ContentVersions["ROT-Core"]);
        Assert.Equal("v8.1.8", RotStack.ContentVersions["ROT-Content"]);
        Assert.Equal("v8.1.8.2", RotStack.ContentVersions["ROT_Map"]);
        Assert.Equal("v8.1.8", RotStack.ContentVersions["ROT-Dragon"]);
        Assert.Equal("v0.1.0", RotStack.ContentVersions["RealmOfThrones.CoopCompat"]);
        Assert.Equal(new[] { "2859188632", "2859232415", "2859222409", "2859238197" }, RotStack.Libraries.Select(mod => mod.WorkshopId));
    }

    // ----- The setup book -----

    [Fact]
    public void A_fresh_book_has_the_locked_setup_first_and_active()
    {
        var book = new RotSetupBook();
        var current = book.Current();
        Assert.Equal(RotSetupBook.CoreName, book.Active);
        Assert.Same(book.Setups[0], current);
        Assert.Equal("Plain Realm of Thrones", current.Name);
        Assert.True(current.Locked);
        Assert.Empty(current.Extras);
        Assert.True(RotSetupBook.IsBuiltIn(" plain realm of thrones "));
        Assert.Equal("Plain Realm of Thrones is locked. Copy it to change mods or their order.", RotSetupBook.LockedNote(current.Name));
    }

    [Fact]
    public void The_locked_setup_cannot_be_renamed_deleted_or_edited_but_can_be_copied()
    {
        var book = new RotSetupBook();
        book.Ensure();
        var plain = book.Find(RotSetupBook.CoreName)!;
        Assert.Contains("cannot be changed", Assert.Throws<InvalidOperationException>(() => book.Rename(RotSetupBook.CoreName, "Mine")).Message);
        Assert.Contains("cannot be deleted", Assert.Throws<InvalidOperationException>(() => book.Delete(RotSetupBook.CoreName)).Message);
        Assert.Throws<InvalidOperationException>(() => RotLoadOrder.Add(plain, "RBM", "RBM", "2859251492"));
        Assert.Throws<InvalidOperationException>(() => RotAutoSort.Sort(plain, new Dictionary<string, ModuleManifest>()));

        var copy = book.CopyOf(RotSetupBook.CoreName);
        Assert.Equal("Setup 2", copy.Name);
        Assert.False(copy.Locked);
        Assert.Equal(copy.Name, book.Active);
        Assert.Null(RotLoadOrder.Add(copy, "RBM", "RBM", "2859251492"));
        Assert.Empty(plain.Extras);
        Assert.Equal(new[] { RotSetupBook.CoreName, "Setup 2" }, book.Setups.Select(setup => setup.Name));

        book.Delete("Setup 2");
        Assert.Equal(RotSetupBook.CoreName, book.Active);
    }

    // ----- Extras -----

    [Theory]
    [InlineData(ExtraSlot.AfterTaom)]
    [InlineData(ExtraSlot.BeforeTaom)]
    [InlineData(ExtraSlot.BeforeCoop)]
    public void Extras_sit_between_rot_dragon_and_our_module_on_both_sides_whatever_slot_was_saved(ExtraSlot saved)
    {
        var setup = new TaomSetup
        {
            Name = "Mine",
            Extras =
            [
                new TaomExtraMod { Name = "A", ModuleId = "ExtraA", Slot = saved },
                new TaomExtraMod { Name = "B", ModuleId = "ExtraB", Slot = ExtraSlot.AfterTaom }
            ]
        };

        var client = RotLoadOrder.ClientOrder(setup);
        var server = RotLoadOrder.ServerOrder(setup);
        Assert.Equal(new[] { "ROT-Dragon", "ExtraA", "ExtraB", "RealmOfThrones.CoopCompat" }, client.TakeLast(4));
        Assert.Equal(new[] { "ROT-Dragon", "ExtraA", "ExtraB", "RealmOfThrones.CoopCompat" }, server.TakeLast(4));
        Assert.Equal(RotStack.ClientModules.Length + 2, client.Count);
        Assert.Equal(RotStack.ServerModules.Length + 2, server.Count);
        Assert.Equal(new[] { "ExtraA", "ExtraB", "RealmOfThrones.CoopCompat" }, RotLoadOrder.ServerRows(setup).TakeLast(3).Select(row => row.ModuleId));

        RotLoadOrder.Normalize(setup);
        Assert.All(setup.Extras, mod => Assert.Equal(RotLoadOrder.SlotAfterRot, mod.Slot));
        Assert.Equal(new[] { "ExtraA", "ExtraB" }, setup.Extras.Select(mod => mod.ModuleId));
    }

    [Fact]
    public void An_extra_never_moves_past_rot_dragon_or_our_module_and_turned_off_ones_stay_out()
    {
        var setup = Setup("ExtraA", "ExtraB");
        Assert.False(RotLoadOrder.Move(setup, "ExtraA", -1));
        Assert.False(RotLoadOrder.Move(setup, "ExtraB", +1));
        Assert.True(RotLoadOrder.Move(setup, "ExtraB", -1));
        Assert.Equal(new[] { "ExtraB", "ExtraA" }, setup.Extras.Select(mod => mod.ModuleId));
        RotLoadOrder.MoveTo(setup, "ExtraB", 99);
        Assert.Equal(new[] { "ExtraA", "ExtraB" }, setup.Extras.Select(mod => mod.ModuleId));

        RotLoadOrder.SetEnabled(setup, "ExtraA", false);
        Assert.DoesNotContain("ExtraA", RotLoadOrder.ClientOrder(setup));
        Assert.Contains("ExtraA", RotLoadOrder.ServerRows(setup).Select(row => row.ModuleId));
        Assert.Equal("ExtraB is already on this setup.", RotLoadOrder.Add(setup, "ExtraB", "ExtraB", null));
        Assert.Equal("DedicatedServer.CoodszJoinPassword", RotLoadOrder.LaunchModules(setup, joinPassword: true)[^1]);
    }

    [Fact]
    public void The_core_problem_names_a_broken_core_and_a_compat_module_that_is_not_last()
    {
        var order = RotStack.ClientModules.ToList();
        Assert.Null(RotLoadOrder.CoreProblem(order));
        order.Add("Late");
        Assert.Equal("Realm of Thrones Coop Compat has to load last.", RotLoadOrder.CoreProblem(order));
        var swapped = RotStack.ClientModules.ToList();
        (swapped[11], swapped[12]) = (swapped[12], swapped[11]);
        Assert.Equal("The Realm of Thrones core has to load first, in its order.", RotLoadOrder.CoreProblem(swapped));
        Assert.Equal("ROT-Core is in the module list twice.", RotLoadOrder.CoreProblem(RotStack.ClientModules.Append("ROT-Core").ToList()));
    }

    // ----- Refusals -----

    [Theory]
    [InlineData("ROT-Core", "part of the Realm of Thrones core")]
    [InlineData("rot_map", "part of the Realm of Thrones core")]
    [InlineData("Bannerlord.Harmony", "part of the Realm of Thrones core")]
    [InlineData("NavalDLC", "part of the Realm of Thrones core")]
    [InlineData("RealmOfThrones.CoopCompat", "part of the Realm of Thrones core")]
    [InlineData("ROT_Core", "second copy of ROT-Core")]
    [InlineData("ROT-Map", "second copy of ROT_Map")]
    [InlineData("rot dragon", "second copy of ROT-Dragon")]
    [InlineData("Coop", "old Coop copy")]
    [InlineData("TAOM.Dependencies", "part of TAOM, another overhaul")]
    [InlineData("BurningEmpires", "part of Burning Empires, another overhaul")]
    [InlineData("BurningEmpires.CoopCompat", "part of Burning Empires, another overhaul")]
    [InlineData("ChaosInCalradia", "part of Chaos in Calradia, which has tools of its own")]
    [InlineData("chaosincalradia.coopcompat", "part of Chaos in Calradia, which has tools of its own")]
    [InlineData("DedicatedServer.Windows", "server or launcher module")]
    [InlineData("Multiplayer", "not a campaign module")]
    [InlineData("bad*id", "cannot be loaded")]
    [InlineData("---", "cannot be loaded")]
    [InlineData("rot content", "second copy of ROT-Content")]
    [InlineData("ROT_Dragon", "second copy of ROT-Dragon")]
    [InlineData("BirthAndDeath", "comes with Bannerlord")]
    [InlineData("fastmode", "comes with Bannerlord")]
    [InlineData("DedicatedCustomServer", "not a campaign module")]
    public void Refused_ids_say_why(string id, string reason)
    {
        Assert.Contains(reason, RotLoadOrder.RefusalReason(id));
        Assert.NotNull(RotLoadOrder.Add(new TaomSetup { Name = "Mine" }, id, id, null));
    }

    [Fact]
    public void A_normal_mod_and_a_realm_of_thrones_add_on_with_its_own_id_are_allowed()
    {
        Assert.Null(RotLoadOrder.RefusalReason("RBM"));
        Assert.Null(RotLoadOrder.RefusalReason("ROT-Core-Addon"));
        Assert.Null(RotLoadOrder.SecondCopyOf("ROT-Core-Addon"));
    }

    [Fact]
    public void No_official_module_outside_the_core_can_reach_the_server_list()
    {
        foreach (var id in RotLoadOrder.OfficialModules)
            Assert.NotNull(RotLoadOrder.RefusalReason(id));
        var setup = new TaomSetup
        {
            Name = "Mine",
            Extras = RotLoadOrder.OfficialModules.Select(id => new TaomExtraMod { Name = id, ModuleId = id }).Append(new TaomExtraMod { Name = "RBM", ModuleId = "RBM" }).ToList()
        };
        var server = RotLoadOrder.ServerOrder(setup);
        Assert.Equal(new[] { "ROT-Dragon", "RBM", "RealmOfThrones.CoopCompat" }, server.TakeLast(3));
        foreach (var id in RotLoadOrder.OfficialModules)
            Assert.DoesNotContain(id, server, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_burning_empires_ids_match_the_burning_empires_stack()
    {
        Assert.Equal(new[] { BeStack.BeModuleId, BeStack.ElephantsModuleId, BeStack.CompatModuleId }.OrderBy(id => id),
            RotLoadOrder.BeCoreIds.OrderBy(id => id));
    }

    // ----- Auto sort and checks -----

    [Fact]
    public void Auto_sort_puts_a_mod_after_the_mods_it_needs_and_rts_camera_last_without_leaving_the_one_place()
    {
        var setup = Setup("RTSCamera", "NeedsBase", "Base");
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase)
        {
            ["NeedsBase"] = Manifest("NeedsBase", needs: ["Base"]),
            ["Base"] = Manifest("Base")
        };
        var lines = RotAutoSort.Sort(setup, manifests);
        Assert.Equal(new[] { "Base", "NeedsBase", "RTSCamera" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Equal(new[] { "Sorted the extra mods so each one loads after the mods it needs." }, lines);
        Assert.Equal("RealmOfThrones.CoopCompat", RotLoadOrder.ClientOrder(setup)[^1]);
    }

    [Fact]
    public void Checks_block_a_missing_dependency_and_warn_about_the_game_version_and_bundled_libraries()
    {
        var setup = Setup("Child");
        var facts = new Dictionary<string, TaomModFact>(StringComparer.OrdinalIgnoreCase)
        {
            ["Child"] = new("Child", Manifest("Child", needs: ["Parent", "CoopNightly", "ROT-Core"], native: "v1.2.12"), ["a"], ["Harmony"])
        };
        var issues = RotModChecks.Run(setup, facts);
        Assert.Contains(issues, issue => issue.Blocks && issue.Text == "Child needs Parent, which is not on this setup.");
        Assert.Contains(issues, issue => !issue.Blocks && issue.Text.Contains("Realm of Thrones runs on 1.4.8"));
        Assert.Contains(issues, issue => !issue.Blocks && issue.Text.Contains("The Realm of Thrones core already loads them"));
        Assert.DoesNotContain(issues, issue => issue.Text.Contains("CoopNightly") || issue.Text.Contains("ROT-Core"));
    }

    // ----- Duplicates and odd names -----

    private static TaomSetup Duplicated() => new()
    {
        Name = "Mine",
        Extras =
        [
            new TaomExtraMod { Name = "First", ModuleId = "Child" },
            new TaomExtraMod { Name = "Second", ModuleId = "child" },
            new TaomExtraMod { Name = "Third", ModuleId = " Child " },
            new TaomExtraMod { Name = "Parent", ModuleId = "Parent " }
        ]
    };

    [Fact]
    public void Ensure_trims_extra_ids_and_drops_a_repeated_one_so_every_caller_sees_each_mod_once()
    {
        var book = new RotSetupBook { Setups = [Duplicated()], Active = "Mine" };
        var setup = book.Current();
        Assert.Equal(new[] { "Child", "Parent" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Equal("First", setup.Extras[0].Name);

        Assert.Equal(new[] { "ROT-Dragon", "Child", "Parent", "RealmOfThrones.CoopCompat" }, RotLoadOrder.ClientOrder(setup).TakeLast(4));
        Assert.Equal(new[] { "ROT-Dragon", "Child", "Parent", "RealmOfThrones.CoopCompat" }, RotLoadOrder.ServerOrder(setup).TakeLast(4));
        Assert.Equal(RotLoadOrder.ClientOrder(setup), RotSharedList.Entries(setup, _ => null).Select(entry => entry.Id));
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase) { ["Child"] = Manifest("Child", needs: ["Parent"]) };
        RotAutoSort.Sort(setup, manifests);
        Assert.Equal(new[] { "Parent", "Child" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Empty(RotModChecks.Run(setup, new Dictionary<string, TaomModFact>()));
    }

    [Fact]
    public void Repeated_extras_do_not_crash_the_callers_even_before_ensure()
    {
        var facts = new Dictionary<string, TaomModFact>(StringComparer.OrdinalIgnoreCase)
        {
            ["Child"] = new("Child", Manifest("Child", needs: ["Missing"]), ["a"], [])
        };
        Assert.Equal(new[] { "ROT-Dragon", "Child", "Parent", "RealmOfThrones.CoopCompat" }, RotLoadOrder.ClientOrder(Duplicated()).TakeLast(4));
        Assert.Equal(RotLoadOrder.ClientOrder(Duplicated()), RotSharedList.Entries(Duplicated(), _ => null).Select(entry => entry.Id));
        Assert.Contains(RotModChecks.Run(Duplicated(), facts), issue => issue.Blocks && issue.Text == "First needs Missing, which is not on this setup.");
        var sorted = Duplicated();
        RotAutoSort.Sort(sorted, new Dictionary<string, ModuleManifest>());
        Assert.Equal(new[] { "Child", "Parent" }, sorted.Extras.Select(mod => mod.ModuleId));
    }

    [Fact]
    public void Server_order_and_server_rows_leave_out_refused_and_repeated_extras_and_rows_keep_turned_off_ones()
    {
        var setup = new TaomSetup
        {
            Name = "Mine",
            Extras =
            [
                new TaomExtraMod { Name = "Off", ModuleId = "Off", Enabled = false },
                new TaomExtraMod { Name = "Kept", ModuleId = "Kept" },
                new TaomExtraMod { Name = "Again", ModuleId = "KEPT" },
                new TaomExtraMod { Name = "Core", ModuleId = "ROT-Core" },
                new TaomExtraMod { Name = "Fast", ModuleId = "FastMode" }
            ]
        };
        Assert.Equal(new[] { "ROT-Dragon", "Kept", "RealmOfThrones.CoopCompat" }, RotLoadOrder.ServerOrder(setup).TakeLast(3));
        var rows = RotLoadOrder.ServerRows(setup);
        Assert.Equal(new[] { "ROT-Dragon", "Off", "Kept", "RealmOfThrones.CoopCompat" }, rows.TakeLast(4).Select(row => row.ModuleId));
        Assert.Equal(new[] { 0, 1 }, rows.Where(row => row.ExtraIndex >= 0).Select(row => row.ExtraIndex));
    }

    [Theory]
    [InlineData("plain realm of thrones")]
    [InlineData("  Plain Realm of Thrones  ")]
    [InlineData("PLAIN REALM OF THRONES")]
    public void The_locked_name_cannot_be_taken_by_rename_create_or_copy(string name)
    {
        var book = new RotSetupBook();
        book.Create("Mine");
        Assert.Contains("already exists", Assert.Throws<InvalidOperationException>(() => book.Rename("Mine", name)).Message);
        Assert.Contains("already exists", Assert.Throws<InvalidOperationException>(() => book.Create(name)).Message);
        Assert.Contains("already exists", Assert.Throws<InvalidOperationException>(() => book.CopyOf(RotSetupBook.CoreName, name)).Message);
        Assert.Equal(new[] { RotSetupBook.CoreName, "Mine" }, book.Setups.Select(setup => setup.Name));
        Assert.Same(book.Setups[0], book.Find(name));
    }

    // ----- Settings files -----

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"ModSetups\": 5}")]
    [InlineData("{\"ModSetups\": null, \"LastJoins\": null}")]
    [InlineData("{\"ModSetups\": {\"Active\": \"Gone\", \"Setups\": [null, {\"Name\": \"Plain Realm of Thrones\", \"Locked\": false, \"Extras\": [{\"ModuleId\": \"RBM\"}]}]}}")]
    public void Old_or_damaged_settings_files_fall_back_to_the_locked_setup(string json)
    {
        foreach (var book in new[] { RotClientSettings.Parse(json).ModSetups, RotServerSettings.Parse(json).ModSetups })
        {
            Assert.Equal(RotSetupBook.CoreName, book.Active);
            Assert.Equal(RotSetupBook.CoreName, book.Setups[0].Name);
            Assert.True(book.Setups[0].Locked);
            Assert.Empty(book.Setups[0].Extras);
        }
    }

    [Fact]
    public void A_saved_custom_setup_comes_back_active_with_its_extras_in_the_one_place()
    {
        var json = "{\"Port\": \"4300\", \"ModSetups\": {\"Active\": \"Mine\", \"Setups\": [{\"Name\": \"Mine\", \"Locked\": true, \"Extras\": [{\"Name\": \"A\", \"ModuleId\": \"ExtraA\", \"Slot\": \"BeforeCoop\"}]}]}}";
        var client = RotClientSettings.Parse(json);
        Assert.Equal("4300", client.Port);
        Assert.Equal(new[] { RotSetupBook.CoreName, "Mine" }, client.ModSetups.Setups.Select(setup => setup.Name));
        var mine = client.ModSetups.Current();
        Assert.Equal("Mine", mine.Name);
        Assert.False(mine.Locked);
        Assert.Equal(RotLoadOrder.SlotAfterRot, Assert.Single(mine.Extras).Slot);

        var again = RotClientSettings.Parse(client.Serialize());
        Assert.Equal(new[] { "ROT-Dragon", "ExtraA", "RealmOfThrones.CoopCompat" }, RotLoadOrder.ClientOrder(again.ModSetups.Current()).TakeLast(3));
        Assert.Equal(RotStack.DirectPort, RotClientSettings.Parse("").Port);

        var server = RotServerSettings.Parse(RotServerSettings.Parse(json).Serialize());
        Assert.Equal(new[] { "ROT-Dragon", "ExtraA", "RealmOfThrones.CoopCompat" }, RotLoadOrder.ServerOrder(server.ModSetups.Current()).TakeLast(3));
        Assert.True(server.OpenRouterPort);
    }

    [Fact]
    public void The_settings_files_are_registered_like_the_burning_empires_ones()
    {
        Assert.Equal("rot-launcher.json", RotClientSettings.FileName);
        Assert.Equal("rot-server.json", RotServerSettings.FileName);
        Assert.Contains(RotClientSettings.FileName, Core.IO.ToolData.RotClient.OldFiles);
        Assert.Contains(RotServerSettings.FileName, Core.IO.ToolData.RotServer.OldFiles);
        Assert.Contains(RotClientSettings.FileName, Core.Updates.ToolUpdate.KeptUserFiles);
        Assert.Contains(RotServerSettings.FileName, Core.Updates.ToolUpdate.KeptUserFiles);
        Assert.Equal(RotClientSettings.Parse("").ModSetups.Active, RotClientSettings.Load(Path.Combine(Path.GetTempPath(), "rot-" + Guid.NewGuid().ToString("N") + ".json")).ModSetups.Active);
    }
}
