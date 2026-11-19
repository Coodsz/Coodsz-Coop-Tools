using BannerlordCoopLauncher.Core.Models;

namespace BannerlordCoopLauncher.Tests;

public class OnlineProfileTests
{
    [Fact]
    public void The_online_profile_with_local_mods_and_no_Workshop_id_still_loads()
    {
        // bannerlord.eu/download/modules.json as of 2026-10-15: local mods (DownloadUrl, Sha256) with an
        // empty WorkshopId. The launcher refused the whole profile, so new installs had no mod list.
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "BannerlordCoopLauncher.Tests", "Fixtures", "online-profile-2026-10-15.json"));

        var profile = ProfileJson.Parse(json);

        // More Nations Remastered: a local mod still in the setup (COOP Server Settings Lock left it in v1.3.4).
        var local = profile.Mods.Single(mod => mod.ModuleId == "MoreNationsRemastered");
        Assert.Equal("0", local.WorkshopId);
        Assert.Equal("3770450698", profile.Mods.Single(mod => mod.ModuleId == "Coop").WorkshopId);
    }

    [Fact]
    public void Built_in_profile_is_valid_for_a_new_install_that_cannot_read_the_online_one()
    {
        var profile = ProfileJson.BuiltIn();
        ProfileJson.Validate(profile);
        Assert.Contains("Native", profile.BaseModules);
        Assert.Equal(4200, profile.Port);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
