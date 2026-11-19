using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class BeSharedListTests
{
    private static List<SharedModEntry> HostList(params string[] extrasAfterBe)
    {
        var list = BeStack.ClientModules.Select(id => new SharedModEntry(id, id, "v1.0.0", "0")).ToList();
        list.AddRange(extrasAfterBe.Select(id => new SharedModEntry(id, id, "v1.0.0", "123456")));
        return list;
    }

    [Fact]
    public void A_taom_list_is_refused_before_anything_changes()
    {
        var setup = new TaomSetup { Name = "Mine" };
        Assert.Null(BeLoadOrder.Add(setup, "Kept", "Kept", "111"));
        var taom = TaomStack.ClientModules.Select(id => new SharedModEntry(id, id, "v1", "0")).Append(new SharedModEntry("Other", "Other", "v1", "222")).ToList();
        Assert.Contains("for TAOM", BeSharedList.ForeignGame(taom));
        var error = Assert.Throws<InvalidOperationException>(() => BeSharedList.Import(setup, taom, Array.Empty<FoundMod>()));
        Assert.Contains("TAOM.Dependencies", error.Message);
        Assert.True(Assert.Single(setup.Extras).Enabled);
    }

    [Fact]
    public void Butr_on_the_list_is_core_and_not_refused()
    {
        var setup = new TaomSetup { Name = "Mine" };
        var result = BeSharedList.Import(setup, HostList("Extra"), Array.Empty<FoundMod>());
        Assert.Null(BeSharedList.ForeignGame(HostList()));
        Assert.Empty(result.Refused);
        Assert.Empty(result.MissingCore);
        Assert.Equal(new[] { "Extra" }, result.Added);
        Assert.Equal("Extra", Assert.Single(setup.Extras).ModuleId);
    }

    [Fact]
    public void Import_follows_the_hosts_slots_and_never_adds_elephants_as_an_extra()
    {
        var setup = new TaomSetup { Name = "Mine" };
        Assert.Null(BeLoadOrder.Add(setup, "Old", "Old", "333"));
        var list = HostList();
        list.Insert(list.FindIndex(entry => entry.Id == "Coop"), new SharedModEntry("Early", "Early", "v1", "444"));
        list.Add(new SharedModEntry("Late", "Late", "v1", "555"));
        list.Add(new SharedModEntry("NexusOnly", "Nexus only", "v1", "0"));
        var result = BeSharedList.Import(setup, list, Array.Empty<FoundMod>());

        Assert.Equal(new[] { "Nexus only" }, result.NoLink);
        Assert.Equal(new[] { "Old" }, result.TurnedOff);
        Assert.Equal(new[] { "Early", "Late", "Old" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Equal(BeLoadOrder.SlotBeforeCoop, setup.Extras[0].Slot);
        var order = BeLoadOrder.ClientOrder(setup);
        Assert.Equal(new[] { "BurningEmpires", BeStack.ElephantsModuleId, "Late" }, order.TakeLast(3));
        Assert.True(result.Changed);
    }

    [Fact]
    public void Shared_entries_carry_the_workshop_ids_of_coop_and_butr()
    {
        var book = new BeSetupBook();
        book.Ensure();
        var entries = BeSharedList.Entries(book.Find(BeSetupBook.CoreName)!, _ => null);
        Assert.Equal(BeStack.ClientModules, entries.Select(entry => entry.Id));
        Assert.Equal(BeStack.CoopWorkshopId, entries.Single(entry => entry.Id == "Coop").WorkshopId);
        foreach (var butr in BeStack.Butr)
            Assert.Equal(butr.WorkshopId, entries.Single(entry => entry.Id == butr.ModuleId).WorkshopId);
        Assert.Equal("MCM", entries.Single(entry => entry.Id == "Bannerlord.MBOptionScreen").Name);
    }
}
