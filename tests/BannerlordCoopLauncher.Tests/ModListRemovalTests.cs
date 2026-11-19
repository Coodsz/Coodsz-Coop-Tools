using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;

namespace BannerlordCoopLauncher.Tests;

public class ModListRemovalTests
{
    [Fact]
    public void Locked_setups_cannot_remove_core_mods_or_their_own_list()
    {
        foreach (var id in ServerModuleSet.WorkingOrder(includeMoreNations: true))
            Assert.False(ModListRemoval.CanRemove(id, locked: true, defaultSetup: false, includeMoreNations: true), id);
        Assert.False(ModListRemoval.CanRemove("MyExtraMod", locked: true, defaultSetup: false, includeMoreNations: true));
        Assert.False(ModListRemoval.CanRemove(BannerlordCoopModules.Coop, locked: true, defaultSetup: false, includeMoreNations: false));
        Assert.False(ModListRemoval.CanRemove(BannerlordCoopModules.CoopNightly, locked: true, defaultSetup: false, includeMoreNations: true));
    }

    [Fact]
    public void Core_engines_and_dependencies_stay_on_an_unlocked_setup()
    {
        var core = new[]
        {
            "Bannerlord.Harmony",
            "Bannerlord.ButterLib",
            "Bannerlord.UIExtenderEx",
            "Bannerlord.MBOptionScreen",
            "Native",
            "SandBoxCore",
            "SandBox",
            "StoryMode",
            "CustomBattle",
            "Coop",
            "CoopNightly"
        };
        foreach (var id in core)
            Assert.False(ModListRemoval.CanRemove(id, locked: false, defaultSetup: false, includeMoreNations: true), id);
        foreach (var id in DefaultClientSet.Fixed)
            Assert.False(ModListRemoval.CanRemove(id, locked: false, defaultSetup: true, includeMoreNations: false), id);
        Assert.True(ModListRemoval.CanRemove("CoopModPatch", locked: false, defaultSetup: true, includeMoreNations: false));
        Assert.True(ModListRemoval.CanRemove("PSCoopBridge", locked: false, defaultSetup: true, includeMoreNations: false));
        Assert.False(ModListRemoval.CanRemove("CoopModPatch", locked: true, defaultSetup: true, includeMoreNations: false));
        Assert.False(ModListRemoval.CanRemove("PSCoopBridge", locked: true, defaultSetup: false, includeMoreNations: true));
    }

    [Fact]
    public void An_added_mod_can_be_removed_from_an_unlocked_setup()
    {
        Assert.True(ModListRemoval.CanRemove("MyExtraMod", locked: false, defaultSetup: false, includeMoreNations: true));
        Assert.True(ModListRemoval.CanRemove("MyExtraMod", locked: false, defaultSetup: true, includeMoreNations: false));
        Assert.False(ModListRemoval.CanRemove("MyExtraMod", locked: true, defaultSetup: false, includeMoreNations: true));
    }
}
