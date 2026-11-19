using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;

namespace BannerlordCoopLauncher.Tests;

public class BannerlordCoopModulesTests
{
    [Fact]
    public void One_coop_copy_is_shown_and_the_other_is_hidden_when_both_are_installed()
    {
        var both = new[] { "Native", "Coop", "CoopNightly" };
        Assert.Equal("Coop", BannerlordCoopModules.Keep(both));
        Assert.True(BannerlordCoopModules.Hide("CoopNightly", both));
        Assert.False(BannerlordCoopModules.Hide("Coop", both));

        var nightly = new[] { "Native", "CoopNightly" };
        Assert.Equal("CoopNightly", BannerlordCoopModules.Keep(nightly));
        Assert.False(BannerlordCoopModules.Hide("CoopNightly", nightly));

        var release = new[] { "Coop" };
        Assert.Equal("Coop", BannerlordCoopModules.Keep(release));
        Assert.False(BannerlordCoopModules.Hide("Coop", release));
    }

    [Fact]
    public void CoopNightly_is_not_offered_as_a_second_mod()
    {
        var root = Path.Combine(Path.GetTempPath(), "coop-one-" + Guid.NewGuid().ToString("N"));
        var modules = Path.Combine(root, "Modules");
        Directory.CreateDirectory(Path.Combine(modules, "CoopNightly"));
        File.WriteAllText(Path.Combine(modules, "CoopNightly", "SubModule.xml"),
            "<Module><Id value=\"CoopNightly\"/><Name value=\"Bannerlord Coop\"/></Module>");
        try
        {
            var found = CustomModCatalog.Scan(root, Array.Empty<BannerlordCoopLauncher.Core.Steam.WorkshopLibrary>(), modules);
            Assert.DoesNotContain(found, mod => mod.ModuleId.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
