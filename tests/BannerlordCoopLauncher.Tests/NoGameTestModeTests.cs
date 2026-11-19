using BannerlordCoopLauncher.Core.Steam;
using Xunit;

namespace BannerlordCoopLauncher.Tests;

// The switch is process wide, so these run on their own after every parallel test.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NoGameTestMode
{
    public const string Name = "No game test mode";
}

[Collection(NoGameTestMode.Name)]
public class NoGameTestModeTests
{
    [Fact]
    public void The_debug_data_root_switch_keeps_live_tests_out_of_the_real_settings()
    {
        var real = BannerlordCoopLauncher.Core.IO.ToolData.DefaultRoot;
        var scratch = Path.Combine(Path.GetTempPath(), "coodsz-data-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("COODSZ_TEST_DATA_ROOT", scratch);
#if DEBUG
            Assert.Equal(scratch, BannerlordCoopLauncher.Core.IO.ToolData.DefaultRoot);
#else
            Assert.Equal(real, BannerlordCoopLauncher.Core.IO.ToolData.DefaultRoot);
#endif
        }
        finally
        {
            Environment.SetEnvironmentVariable("COODSZ_TEST_DATA_ROOT", null);
        }
        Assert.Equal(real, BannerlordCoopLauncher.Core.IO.ToolData.DefaultRoot);
    }

    [Fact]
    public void The_debug_data_root_switch_also_keeps_the_shared_roaming_folder_out_of_live_tests()
    {
        var real = BannerlordCoopLauncher.Core.IO.ToolData.RoamingRoot;
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coodsz"), real);
        var scratch = Path.Combine(Path.GetTempPath(), "coodsz-data-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("COODSZ_TEST_DATA_ROOT", scratch);
#if DEBUG
            Assert.Equal(Path.Combine(scratch, "Roaming"), BannerlordCoopLauncher.Core.IO.ToolData.RoamingRoot);
#else
            Assert.Equal(real, BannerlordCoopLauncher.Core.IO.ToolData.RoamingRoot);
#endif
        }
        finally
        {
            Environment.SetEnvironmentVariable("COODSZ_TEST_DATA_ROOT", null);
        }
        Assert.Equal(real, BannerlordCoopLauncher.Core.IO.ToolData.RoamingRoot);
    }

    // A live test on 2026-10-27 moved Coodsz's real what's new markers: those paths built %APPDATA%\Coodsz
    // themselves. Every one goes through ToolData.RoamingRoot now, so the switch covers them.
    [Fact]
    public void No_tool_builds_the_roaming_Coodsz_folder_itself()
    {
        var src = Path.Combine(SourceFiles.Root(), "src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                && !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !file.EndsWith("ToolData.cs", StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains("SpecialFolder.ApplicationData), \"Coodsz\"", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(src, file))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_debug_switch_hides_steam_and_a_saved_pick_but_not_a_new_pick()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-nogame-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "bin", "Win64_Shipping_Client"));
        Directory.CreateDirectory(Path.Combine(root, "Modules"));
        File.WriteAllText(Path.Combine(root, "bin", "Win64_Shipping_Client", "Bannerlord.exe"), "");
        try
        {
            Assert.False(SteamRegistry.TestNoGame);
            Assert.Equal(GameFolderPickResult.Ok, GameFolderPick.CheckSaved(root).Result);

            Environment.SetEnvironmentVariable("COODSZ_TEST_NO_GAME", "1");
#if DEBUG
            Assert.True(SteamRegistry.TestNoGame);
            Assert.Null(SteamRegistry.TryGetSteamPath());
            Assert.Equal(GameFolderPickResult.NotBannerlord, GameFolderPick.CheckSaved(root).Result);
            Assert.Null(GameFolderPick.ResolveSavedPath(root, () => null));
#else
            Assert.False(SteamRegistry.TestNoGame);
#endif
            // The player's own pick still works, so the button can be tested end to end.
            Assert.Equal(GameFolderPickResult.Ok, GameFolderPick.Check(root).Result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COODSZ_TEST_NO_GAME", null);
            Directory.Delete(root, true);
        }
    }
}
