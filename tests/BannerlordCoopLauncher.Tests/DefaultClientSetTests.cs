using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Profile;

namespace BannerlordCoopLauncher.Tests;

public class DefaultClientSetTests
{
    [Fact]
    public void Default_is_only_the_core_mods_and_the_core_block_cannot_move()
    {
        var profile = new ServerProfile();
        DefaultClientSet.Apply(profile);
        var ids = profile.BaseModules.Concat(profile.Mods.Select(mod => mod.ModuleId)).ToList();
        Assert.Equal(DefaultClientSet.Order, profile.ModuleLoadOrder);
        Assert.Equal(DefaultClientSet.Order.OrderBy(id => id), ids.OrderBy(id => id));
        Assert.DoesNotContain(ids, id => id is "XorberaxLegacy" or "ImprovedGarrisons" or "PlayerSettlement" or "HealOnKillUpdated");

        Assert.True(DefaultClientSet.CanToggle("Coop"));
        Assert.False(DefaultClientSet.CanToggle("StoryMode"));
        Assert.False(DefaultClientSet.CanToggle("CustomBattle"));
        Assert.True(DefaultClientSet.CanToggle("CoopModPatch"));
        Assert.True(DefaultClientSet.CanToggle("PSCoopBridge"));

        var order = DefaultClientSet.Order.ToList();
        Assert.False(WorkingLoadOrder.CanMove(order, 0, 11, DefaultClientSet.Fixed));
        var patch = order.IndexOf("CoopModPatch");
        var bridge = order.IndexOf("PSCoopBridge");
        Assert.True(WorkingLoadOrder.CanMove(order, patch, bridge, DefaultClientSet.Fixed));
    }

    [Fact]
    public void A_new_setup_does_not_include_CoopModPatch_or_PSCoopBridge()
    {
        var profile = new ServerProfile();
        DefaultClientSet.Apply(profile);
        RemovedModLaunch.Drop(profile, DefaultClientSet.NotOnNewSetup);

        Assert.Equal(DefaultClientSet.Fixed.Concat(new[] { "Coop" }), profile.ModuleLoadOrder);
        Assert.DoesNotContain(profile.Mods, mod => DefaultClientSet.NotOnNewSetup.Contains(mod.ModuleId, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoopModPatch_and_PSCoopBridge_can_be_turned_off_without_moving_the_core()
    {
        var profile = new ServerProfile();
        DefaultClientSet.Apply(profile);
        DefaultClientSet.ApplyToggles(profile, ["CoopModPatch", "Coop", "StoryMode"]);
        Assert.False(profile.Mods.Single(mod => mod.ModuleId == "CoopModPatch").Enabled);
        Assert.False(profile.Mods.Single(mod => mod.ModuleId == "Coop").Enabled);
        Assert.Equal(DefaultClientSet.Fixed.Append("PSCoopBridge"), profile.ModuleLoadOrder);
        Assert.DoesNotContain("CoopModPatch", profile.ModuleLoadOrder);
        Assert.DoesNotContain("Coop", profile.ModuleLoadOrder);
    }

    [Fact]
    public void An_added_mod_on_default_can_be_turned_off_without_turning_off_coop()
    {
        var profile = new ServerProfile();
        DefaultClientSet.Apply(profile);
        profile.Mods.Add(new ProfileMod { Name = "Immersive Storms", ModuleId = "ImmersiveStorms", Enabled = true });
        profile.ModuleLoadOrder.Add("ImmersiveStorms");
        DefaultClientSet.ApplyToggles(profile, ["ImmersiveStorms"]);

        Assert.False(profile.Mods.Single(mod => mod.ModuleId == "ImmersiveStorms").Enabled);
        Assert.True(profile.Mods.Single(mod => mod.ModuleId == "Coop").Enabled);
        Assert.DoesNotContain("ImmersiveStorms", profile.ModuleLoadOrder);
        Assert.Contains("Coop", profile.ModuleLoadOrder);
    }
}
