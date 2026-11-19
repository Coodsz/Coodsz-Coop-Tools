using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;

namespace BannerlordCoopLauncher.Tests;

// The official BUTR MCM Workshop item 2859238197 was removed (checked 2026-10-26); the Workshop only has
// re-uploads, some of them old builds the dedicated server refuses. Coodsz: MCM comes from Nexus Mods only.
public sealed class McmNexusOnlyTests
{
    private const string Mcm = "Bannerlord.MBOptionScreen";
    private const string OldItem = "2859238197";

    [Fact]
    public void The_old_mcm_item_is_retired_and_no_setup_downloads_it()
    {
        Assert.Equal(OldItem, RetiredWorkshopItems.For(Mcm));
        Assert.True(RetiredWorkshopItems.IsRetired(OldItem));
        Assert.False(WorkshopTwins.Official.ContainsKey(Mcm));

        var client = new ServerProfile();
        DefaultClientSet.Apply(client);
        Assert.Equal("0", client.Mods.Single(mod => mod.ModuleId == Mcm).WorkshopId);
        var server = new ServerProfile();
        ServerModuleSet.Apply(server);
        Assert.Equal("0", server.Mods.Single(mod => mod.ModuleId == Mcm).WorkshopId);

        var ad1259 = AD1259Stack.ClientMods.Single(mod => mod.Id == Mcm);
        Assert.Null(ad1259.WorkshopId);
        Assert.Equal(612, ad1259.NexusId);
        Assert.Equal("Nexus Mods", ad1259.Source);
    }

    [Fact]
    public void The_lists_link_nexus_not_the_removed_item()
    {
        foreach (var file in new[] { Path.Combine("compat-list", "mods.json"), Path.Combine("ad1259-compat", "compat", "ad1259-mod-compat.json"), Path.Combine("ad1259-compat", "MODS.md") })
            Assert.DoesNotContain(OldItem, SourceFiles.Read(file));
    }

    [Fact]
    public void A_missing_mcm_names_its_nexus_page_and_is_never_subscribed()
    {
        Assert.Equal("https://www.nexusmods.com/mountandblade2bannerlord/mods/612", BannerlordCoopLauncher.Core.Joining.ServerModNeeds.NexusPage(Mcm));
        Assert.Null(BannerlordCoopLauncher.Core.Joining.ServerModNeeds.NexusPage("NoSuchMod"));
        Assert.False(SharedModList.IsWorkshopId(OldItem));
        var support = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "MainWindow.Support.cs"));
        Assert.Contains("id.All(char.IsDigit) && !BannerlordCoopLauncher.Core.Profile.RetiredWorkshopItems.IsRetired(id);", support);
    }

    [Theory]
    [InlineData("v5.12.3", "v5.12.3", true)]
    [InlineData("v5.12.3.0", "v5.12.3", true)]
    [InlineData("5.12.3", "v5.12.3", true)]
    [InlineData("v5.13.1", "v5.12.3", false)]
    public void An_ad1259_pin_reads_versions_the_same_way(string found, string pin, bool same)
    {
        Assert.Equal(same, CoodszHost.Services.ExtraModScan.SameVersion(found, pin));
    }

    [Fact]
    public void A_pinned_workshop_reupload_never_replaces_the_pack_mcm()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcm-pin-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "DedicatedServer");
        var workshop = Path.Combine(root, "workshop");
        static void Module(string folder, string version)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SubModule.xml"),
                "<Module><Name value=\"" + Mcm + "\"/><Id value=\"" + Mcm + "\"/><Version value=\"" + version + "\"/></Module>");
        }

        var modules = CoodszHost.Services.ModuleCatalog.ModulesRoot(server);
        Module(Path.Combine(modules, Mcm), "v5.12.2");
        Module(Path.Combine(workshop, "3747620191"), "v5.13.1");
        try
        {
            var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [Mcm] = "v5.12.3" };
            Assert.Empty(CoodszHost.Services.ExtraModScan.UpdateFromWorkshop(server, new[] { Mcm }, new[] { workshop }, pins));
            Assert.Contains("v5.12.2", File.ReadAllText(Path.Combine(modules, Mcm, "SubModule.xml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_newer_mcm_that_fails_to_copy_leaves_the_old_server_copy_whole()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcm-fail-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "DedicatedServer");
        var game = Path.Combine(root, "Modules");
        static void Module(string folder, string version)
        {
            Directory.CreateDirectory(Path.Combine(folder, "bin", "Win64_Shipping_Server"));
            File.WriteAllText(Path.Combine(folder, "SubModule.xml"),
                "<Module><Name value=\"" + Mcm + "\"/><Id value=\"" + Mcm + "\"/><Version value=\"" + version + "\"/></Module>");
            File.WriteAllText(Path.Combine(folder, "bin", "Win64_Shipping_Server", "MCMv5.dll"), version);
        }

        var serverCopy = Path.Combine(CoodszHost.Services.ModuleCatalog.ModulesRoot(server), Mcm);
        Module(serverCopy, "v5.12.2");
        Module(Path.Combine(game, Mcm), "v5.13.1");
        try
        {
            // A file of the newer copy is locked (the game is open), so the install stops part way.
            using (new FileStream(Path.Combine(game, Mcm, "bin", "Win64_Shipping_Server", "MCMv5.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var lines = CoodszHost.Services.ExtraModScan.UpdateNewerOnlyFromGame(server, new[] { Mcm }, new[] { game });
                Assert.Contains(lines, line => line.Contains("could not be updated", StringComparison.Ordinal));
            }

            Assert.Contains("v5.12.2", File.ReadAllText(Path.Combine(serverCopy, "SubModule.xml")));
            Assert.Equal("v5.12.2", File.ReadAllText(Path.Combine(serverCopy, "bin", "Win64_Shipping_Server", "MCMv5.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
