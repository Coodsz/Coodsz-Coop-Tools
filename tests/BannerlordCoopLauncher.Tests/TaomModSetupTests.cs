using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomModSetupTests
{
    // ----- Setup CRUD -----

    [Fact]
    public void A_new_book_has_plain_taom_active_and_the_locked_taom_plus()
    {
        var book = new TaomSetupBook();
        book.Ensure();
        Assert.Equal(new[] { TaomSetupBook.PlainName, TaomSetupBook.PlusName }, book.Setups.Select(setup => setup.Name));
        var plain = book.Setups[0];
        Assert.True(plain.Locked);
        Assert.Empty(plain.Extras);
        Assert.Same(plain, book.Current());
        Assert.True(book.Setups[1].Locked);
        Assert.Equal(TaomPlus.ModuleIds, book.Setups[1].Extras.Select(mod => mod.ModuleId));
    }

    [Fact]
    public void Setups_can_be_created_copied_renamed_switched_and_deleted()
    {
        var book = new TaomSetupBook();
        var first = book.Create();
        Assert.Equal("Setup 2", first.Name);
        Assert.False(first.Locked);
        Assert.Equal("Setup 2", book.Active);
        Assert.Null(TaomLoadOrder.Add(first, "Better Time", "BetterTime", "123456"));

        var copy = book.CopyOf("Setup 2");
        Assert.Equal("Setup 3", copy.Name);
        Assert.Equal("BetterTime", Assert.Single(copy.Extras).ModuleId);
        Assert.NotSame(first.Extras[0], copy.Extras[0]);

        book.Rename("Setup 3", "With friends");
        Assert.Equal("With friends", book.Active);
        Assert.NotNull(book.Find("with FRIENDS"));

        book.Switch("Setup 2");
        Assert.Same(first, book.Current());

        book.Delete("Setup 2");
        Assert.Equal(TaomSetupBook.PlainName, book.Active);
        Assert.Equal(new[] { TaomSetupBook.PlainName, TaomSetupBook.PlusName, "With friends" }, book.Setups.Select(setup => setup.Name));
    }

    [Fact]
    public void Plain_taom_cannot_be_renamed_deleted_or_edited_but_can_be_copied()
    {
        var book = new TaomSetupBook();
        book.Ensure();
        Assert.Throws<InvalidOperationException>(() => book.Rename(TaomSetupBook.PlainName, "Mine"));
        Assert.Throws<InvalidOperationException>(() => book.Delete(TaomSetupBook.PlainName));
        Assert.Throws<InvalidOperationException>(() => TaomLoadOrder.Add(book.Current(), "Better Time", "BetterTime", "1"));

        var copy = book.CopyOf(TaomSetupBook.PlainName);
        Assert.False(copy.Locked);
        Assert.Empty(copy.Extras);
    }

    [Fact]
    public void Setup_names_stay_unique_and_not_empty()
    {
        var book = new TaomSetupBook();
        book.Create("Mine");
        Assert.Throws<InvalidOperationException>(() => book.Create("mine"));
        Assert.Throws<InvalidOperationException>(() => book.Create("Plain taom"));
        var other = book.Create("Other");
        Assert.Throws<InvalidOperationException>(() => book.Rename("Other", "MINE"));
        Assert.Throws<InvalidOperationException>(() => book.Rename("Other", "  "));
        book.Rename("Other", "other");
        Assert.Equal("other", other.Name);
    }

    // ----- Migration from the old taom-launcher.json -----

    private const string OldSettings = """
        {
          "GamePath": "D:\\Steam\\steamapps\\common\\Mount & Blade II Bannerlord",
          "TaomZip": "C:\\Users\\me\\Downloads\\TAOM 2.0.29.5.zip",
          "CompatZip": "C:\\Users\\me\\Downloads\\TAOM-Coop-v0.3.25-CompatOnly-CLIENT.zip",
          "UseSteam": true,
          "SteamName": "Host Name",
          "Host": "192.168.1.10",
          "Port": "4300",
          "Password": "secret",
          "DllsUnblocked": true
        }
        """;

    [Fact]
    public void Old_settings_keep_every_value_and_get_plain_taom()
    {
        var settings = TaomClientSettings.Parse(OldSettings);
        Assert.Equal("D:\\Steam\\steamapps\\common\\Mount & Blade II Bannerlord", settings.GamePath);
        Assert.Equal("C:\\Users\\me\\Downloads\\TAOM 2.0.29.5.zip", settings.TaomZip);
        Assert.Equal("C:\\Users\\me\\Downloads\\TAOM-Coop-v0.3.25-CompatOnly-CLIENT.zip", settings.CompatZip);
        Assert.True(settings.UseSteam);
        Assert.Equal("Host Name", settings.SteamName);
        Assert.Equal("192.168.1.10", settings.Host);
        Assert.Equal("4300", settings.Port);
        Assert.Equal("secret", settings.Password);
        Assert.True(settings.DllsUnblocked);

        var setup = settings.ModSetups.Current();
        Assert.Equal(TaomSetupBook.PlainName, setup.Name);
        Assert.Equal(TaomStack.ClientModules, TaomLoadOrder.ClientOrder(setup));

        // Saving writes the old fields under the same names, so an older launcher still reads them.
        var again = TaomClientSettings.Parse(settings.Serialize());
        Assert.Equal(settings.Password, again.Password);
        Assert.Equal(settings.Port, again.Port);
        Assert.Contains("\"DllsUnblocked\": true", settings.Serialize());
        Assert.Equal(2, again.ModSetups.Setups.Count);
    }

    [Fact]
    public void Missing_or_broken_settings_give_the_defaults_with_plain_taom()
    {
        var missing = TaomClientSettings.Load(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.Equal("4200", missing.Port);
        Assert.Equal(TaomSetupBook.PlainName, missing.ModSetups.Current().Name);

        var broken = TaomClientSettings.Parse("{ not json");
        Assert.Equal("4200", broken.Port);
        Assert.Equal(TaomSetupBook.PlainName, broken.ModSetups.Current().Name);
    }

    [Fact]
    public void Saved_setups_load_back_and_plain_taom_is_locked_again()
    {
        var settings = TaomClientSettings.Parse(OldSettings);
        var mine = settings.ModSetups.Create("Mine");
        TaomLoadOrder.Add(mine, "Better Time", "BetterTime", "123456");
        TaomLoadOrder.Add(mine, "Map Fix", "MapFix", null);
        TaomLoadOrder.Move(mine, "MapFix", -1);
        TaomLoadOrder.Move(mine, "MapFix", -1);
        TaomLoadOrder.SetEnabled(mine, "BetterTime", false);
        var json = settings.Serialize()
            // Someone put a mod on Plain TAOM by hand and unlocked it.
            .Replace("\"Locked\": true", "\"Locked\": false");
        Assert.Contains("\"BeforeTaom\"", json);
        Assert.DoesNotContain("\"Label\"", json);

        var back = TaomClientSettings.Parse(json);
        Assert.Equal("Mine", back.ModSetups.Active);
        var loaded = back.ModSetups.Current();
        Assert.Equal(new[] { "MapFix", "BetterTime" }, loaded.Extras.Select(mod => mod.ModuleId));
        Assert.Equal(ExtraSlot.BeforeTaom, loaded.Extras[0].Slot);
        Assert.False(loaded.Extras[1].Enabled);
        Assert.Equal("0", loaded.Extras[0].WorkshopId);
        Assert.True(back.ModSetups.Setups[0].Locked);
    }

    // ----- Core lock and order rules -----

    [Fact]
    public void The_core_blocks_split_before_taom()
    {
        Assert.Equal(new[] { "TAOM.Dependencies", "Native", "SandBoxCore", "Sandbox", "StoryMode", "CustomBattle" }, TaomLoadOrder.GameBlock);
        Assert.Equal(new[] { "TAOM", "TAOM_Map", "LOTRLOME_Armory", "Coop", "TAOM.CoopCompat" }, TaomLoadOrder.TaomBlock);
    }

    [Fact]
    public void Extras_load_after_taom_by_default_and_before_taom_when_moved_up()
    {
        var setup = new TaomSetup { Name = "Mine" };
        TaomLoadOrder.Add(setup, "A", "ModA", "11");
        TaomLoadOrder.Add(setup, "B", "ModB", "22");
        Assert.Equal(TaomStack.ClientModules.Concat(new[] { "ModA", "ModB" }), TaomLoadOrder.ClientOrder(setup));

        Assert.True(TaomLoadOrder.Move(setup, "ModA", -1));
        Assert.Equal(ExtraSlot.BeforeTaom, setup.Extras[0].Slot);
        var order = TaomLoadOrder.ClientOrder(setup);
        Assert.Equal("ModA", order[TaomLoadOrder.GameBlock.Count]);
        Assert.Equal("TAOM", order[TaomLoadOrder.GameBlock.Count + 1]);
        Assert.Equal("ModB", order[^1]);
        Assert.Null(TaomLoadOrder.CoreProblem(order));

        // The top of the list stays below the game modules.
        Assert.False(TaomLoadOrder.Move(setup, "ModA", -1));
        Assert.True(TaomLoadOrder.Move(setup, "ModA", 1));
        Assert.Equal(new[] { "ModA", "ModB" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.All(setup.Extras, mod => Assert.Equal(ExtraSlot.AfterTaom, mod.Slot));
        Assert.True(TaomLoadOrder.Move(setup, "ModA", 1));
        Assert.Equal(new[] { "ModB", "ModA" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.False(TaomLoadOrder.Move(setup, "ModA", 1));
    }

    [Fact]
    public void Drag_and_drop_places_an_extra_in_a_slot()
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var id in new[] { "A", "B", "C" })
            TaomLoadOrder.Add(setup, id, id, null);
        TaomLoadOrder.MoveTo(setup, "C", ExtraSlot.BeforeTaom, 0);
        TaomLoadOrder.MoveTo(setup, "B", ExtraSlot.BeforeTaom, 0);
        Assert.Equal(new[] { "B", "C", "A" }, setup.Extras.Select(mod => mod.ModuleId));
        TaomLoadOrder.MoveTo(setup, "B", ExtraSlot.AfterTaom, 99);
        Assert.Equal(new[] { "C", "A", "B" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Equal(ExtraSlot.BeforeTaom, setup.Extras[0].Slot);
        Assert.Null(TaomLoadOrder.CoreProblem(TaomLoadOrder.ClientOrder(setup)));
    }

    [Fact]
    public void Turned_off_and_removed_extras_do_not_load()
    {
        var setup = new TaomSetup { Name = "Mine" };
        TaomLoadOrder.Add(setup, "A", "ModA", null);
        TaomLoadOrder.Add(setup, "B", "ModB", null);
        TaomLoadOrder.SetEnabled(setup, "ModA", false);
        Assert.Equal(TaomStack.ClientModules.Append("ModB"), TaomLoadOrder.ClientOrder(setup));
        Assert.True(TaomLoadOrder.Remove(setup, "ModB"));
        Assert.Equal(TaomStack.ClientModules, TaomLoadOrder.ClientOrder(setup));
    }

    [Theory]
    [InlineData("TAOM")]
    [InlineData("taom_map")]
    [InlineData("Coop")]
    [InlineData("TAOM.CoopCompat")]
    [InlineData("Native")]
    [InlineData("CustomBattle")]
    [InlineData("CoopNightly")]
    [InlineData("Bannerlord.Harmony")]
    [InlineData("Bannerlord.ButterLib")]
    [InlineData("Bannerlord.UIExtenderEx")]
    [InlineData("Bannerlord.MBOptionScreen")]
    [InlineData("DedicatedServer.Windows")]
    [InlineData("DedicatedServer.CoodszJoinPassword")]
    [InlineData("TAOM.Dependencies")]
    [InlineData("LOTRLOME_Armory")]
    [InlineData("Multiplayer")]
    [InlineData("Bad*Id")]
    [InlineData("Bad\"Id")]
    [InlineData("..")]
    [InlineData("")]
    public void Core_and_unsafe_modules_cannot_be_extras(string id)
    {
        Assert.NotNull(TaomLoadOrder.RefusalReason(id));
        var setup = new TaomSetup { Name = "Mine" };
        Assert.NotNull(TaomLoadOrder.Add(setup, id, id, null));
        Assert.Empty(setup.Extras);
    }

    [Fact]
    public void A_refused_or_repeated_extra_in_a_saved_file_is_left_out_of_the_launch()
    {
        var setup = new TaomSetup
        {
            Name = "Edited by hand",
            Extras =
            {
                new TaomExtraMod { ModuleId = "Coop", Slot = ExtraSlot.BeforeTaom },
                new TaomExtraMod { ModuleId = "ModA" },
                new TaomExtraMod { ModuleId = "moda" },
                new TaomExtraMod { ModuleId = "Bannerlord.Harmony" }
            }
        };
        Assert.Equal(TaomStack.ClientModules.Append("ModA"), TaomLoadOrder.ClientOrder(setup));
    }

    [Fact]
    public void Core_problem_catches_a_broken_core()
    {
        var core = TaomStack.ClientModules.ToList();
        Assert.Null(TaomLoadOrder.CoreProblem(core));

        var inside = core.ToList();
        inside.Insert(inside.IndexOf("Coop"), "ModA");
        Assert.NotNull(TaomLoadOrder.CoreProblem(inside));

        var first = core.Prepend("ModA").ToList();
        Assert.NotNull(TaomLoadOrder.CoreProblem(first));

        var swapped = core.ToList();
        (swapped[1], swapped[2]) = (swapped[2], swapped[1]);
        Assert.NotNull(TaomLoadOrder.CoreProblem(swapped));

        Assert.NotNull(TaomLoadOrder.CoreProblem(core.Append("Native").ToList()));
        Assert.NotNull(TaomLoadOrder.CoreProblem(core.Where(id => id != "TAOM.CoopCompat").ToList()));
    }

    [Fact]
    public void A_locked_setup_loads_only_the_core_even_with_extras_in_the_file()
    {
        var setup = new TaomSetup { Name = "Plain TAOM", Locked = true, Extras = { new TaomExtraMod { ModuleId = "ModA" } } };
        Assert.Equal(TaomStack.ClientModules, TaomLoadOrder.ClientOrder(setup));
        Assert.Throws<InvalidOperationException>(() => TaomLoadOrder.Move(setup, "ModA", 1));
        Assert.Throws<InvalidOperationException>(() => TaomLoadOrder.Remove(setup, "ModA"));
    }

    [Fact]
    public void The_server_order_keeps_the_sealed_server_list_with_extras_before_dedicated_server()
    {
        Assert.Equal(TaomStack.ServerModules, TaomLoadOrder.ServerOrder(null));
        var setup = new TaomSetup { Name = "Mine" };
        TaomLoadOrder.Add(setup, "A", "ModA", null);
        TaomLoadOrder.Add(setup, "B", "ModB", null);
        TaomLoadOrder.Move(setup, "ModA", -1);
        var order = TaomLoadOrder.ServerOrder(setup);
        Assert.Equal("DedicatedServer.Windows", order[^1]);
        Assert.Equal("ModB", order[^2]);
        Assert.Equal("ModA", order[order.ToList().IndexOf("TAOM") - 1]);
        Assert.Equal(TaomStack.ServerModules, order.Where(TaomLoadOrder.IsCore));
    }

    // ----- Launch command line -----

    [Fact]
    public void Plain_taom_launches_with_exactly_todays_command_line()
    {
        var today = TaomLaunch.Arguments(connect: true, steamLobby: false, password: "pw", directEndpoint: "1.2.3.4:4200",
            modules: TaomStack.ClientModules.Append(ClientAddOns.JoinPasswordId).ToList());
        var book = new TaomSetupBook();
        var plain = TaomLaunch.Arguments(connect: true, steamLobby: false, password: "pw", directEndpoint: "1.2.3.4:4200",
            modules: TaomLoadOrder.LaunchModules(book.Current(), joinPassword: true));
        Assert.Equal(today, plain);
        Assert.Equal(TaomStack.ClientModules, TaomLoadOrder.LaunchModules(book.Current(), joinPassword: false));
    }

    [Fact]
    public void Extras_go_into_the_module_token_in_their_slots_before_the_join_password_module()
    {
        var setup = new TaomSetup { Name = "Mine" };
        TaomLoadOrder.Add(setup, "Swadian Armoury", "swadian armoury", "999");
        TaomLoadOrder.Add(setup, "Map Fix", "MapFix", null);
        TaomLoadOrder.MoveTo(setup, "MapFix", ExtraSlot.BeforeTaom, 0);
        var arguments = TaomLaunch.Arguments(connect: true, steamLobby: true, password: "", lobbyId: 77,
            modules: TaomLoadOrder.LaunchModules(setup, joinPassword: true));
        var expected = "\"_MODULES_*TAOM.Dependencies*Native*SandBoxCore*Sandbox*StoryMode*CustomBattle*MapFix"
            + "*TAOM*TAOM_Map*LOTRLOME_Armory*Coop*TAOM.CoopCompat*swadian armoury*" + ClientAddOns.JoinPasswordId + "*_MODULES_\"";
        Assert.Equal("/singleplayer /client +connect_lobby 77 " + expected, arguments);
        Assert.True(TaomStack.CoopIsAdjacent(TaomLoadOrder.LaunchModules(setup, joinPassword: true)));
    }

    // ----- Share and import -----

    private static (string, string, string?)? Describe(string id) => (id + " name", "v1.0." + id.Length, null);

    [Fact]
    public void A_shared_list_imports_back_into_the_same_setup()
    {
        var mine = new TaomSetup { Name = "Mine" };
        TaomLoadOrder.Add(mine, "Better Time", "BetterTime", "123456");
        TaomLoadOrder.Add(mine, "Map Fix", "MapFix", "654321");
        TaomLoadOrder.Add(mine, "Off one", "OffOne", "777");
        TaomLoadOrder.SetEnabled(mine, "OffOne", false);
        TaomLoadOrder.Move(mine, "MapFix", -1);
        TaomLoadOrder.Move(mine, "MapFix", -1);

        var entries = TaomSharedList.Entries(mine, Describe);
        Assert.Equal(TaomLoadOrder.ClientOrder(mine), entries.Select(entry => entry.Id));
        Assert.Equal(TaomStack.CoopWorkshopId, entries.Single(entry => entry.Id == "Coop").WorkshopId);
        Assert.Equal("123456", entries.Single(entry => entry.Id == "BetterTime").WorkshopId);

        var path = Path.Combine(Path.GetTempPath(), "taom-list-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SharedModList.Write(path, entries);
            var read = SharedModList.Read(File.ReadAllText(path));

            var friend = new TaomSetup { Name = "Friend" };
            var result = TaomSharedList.Import(friend, read, Array.Empty<FoundMod>());
            Assert.True(result.Changed);
            Assert.Equal(new[] { "Map Fix", "Better Time" }, result.Added);
            Assert.Empty(result.MissingCore);
            Assert.Equal(TaomLoadOrder.ClientOrder(mine), TaomLoadOrder.ClientOrder(friend));
            Assert.Equal(new[] { "654321", "123456" }, friend.Extras.Select(mod => mod.WorkshopId));

            var again = TaomSharedList.Import(friend, read, Array.Empty<FoundMod>());
            Assert.False(again.Changed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Import_turns_off_unlisted_extras_and_skips_core_refused_and_unlinked_mods()
    {
        var setup = new TaomSetup { Name = "Mine" };
        TaomLoadOrder.Add(setup, "Old", "OldMod", "5");
        TaomLoadOrder.Add(setup, "Again", "AgainMod", "6");
        TaomLoadOrder.SetEnabled(setup, "AgainMod", false);
        var shared = new List<SharedModEntry>
        {
            new("Native", "Native", "v1.4.8", ""),
            new("Bannerlord.Harmony", "Harmony", "v2.3", "2859188632"),
            new("TAOM", "TAOM", "v2.0.29.5", ""),
            new("Coop", "Coop", "v0.1.6", TaomStack.CoopWorkshopId),
            new("AgainMod", "Again", "", "6"),
            new("LocalOnly", "Local only", "", "0"),
            new("OnThisPc", "On this PC", "", ""),
            new("DedicatedServer.CoodszJoinPassword", "Coodsz Join Password", "", "")
        };
        var found = new[] { new FoundMod("On this PC", "OnThisPc", "0", "C:\\Game\\Modules\\OnThisPc", "Modules") };

        var result = TaomSharedList.Import(setup, shared, found);
        Assert.Equal(new[] { "Old" }, result.TurnedOff);
        Assert.Equal(new[] { "Again" }, result.TurnedOn);
        Assert.Equal(new[] { "On this PC" }, result.Added);
        Assert.Equal(new[] { "Harmony" }, result.Refused);
        Assert.Equal(new[] { "Local only" }, result.NoLink);
        Assert.Contains("TAOM.CoopCompat", result.MissingCore);
        Assert.Equal(new[] { "AgainMod", "OnThisPc", "OldMod" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Equal(TaomStack.ClientModules.Concat(new[] { "AgainMod", "OnThisPc" }), TaomLoadOrder.ClientOrder(setup));
        Assert.Throws<InvalidOperationException>(() => TaomSharedList.Import(new TaomSetup { Name = "Plain TAOM", Locked = true }, shared, found));
    }

    [Fact]
    public void Missing_lists_only_enabled_extras_that_are_not_installed()
    {
        var setup = new TaomSetup { Name = "Mine" };
        TaomLoadOrder.Add(setup, "A", "ModA", "1");
        TaomLoadOrder.Add(setup, "B", "ModB", "2");
        TaomLoadOrder.Add(setup, "C", "ModC", "3");
        TaomLoadOrder.SetEnabled(setup, "ModC", false);
        var missing = TaomSharedList.Missing(setup, new[] { "moda" });
        Assert.Equal("ModB", Assert.Single(missing).ModuleId);
    }
}
