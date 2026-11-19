using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomStopSaveTests
{
    [Fact]
    public void Only_the_world_saved_line_counts_as_a_finished_save()
    {
        Assert.Equal("taomtest.sav", WorldSavedLine.SavedFile("00:45:36.675  [DedicatedServer] operator save: world saved to 'taomtest.sav' (2058 ms)"));
        Assert.True(WorldSavedLine.Saved("[DedicatedServer] autosave: world saved to 'taomtest.sav' (2735 ms)", "taomtest"));
        Assert.True(WorldSavedLine.Saved("[DedicatedServer] shutdown save: world saved to 'TaomTest.sav' (1879 ms)", "taomtest.sav"));
        Assert.False(WorldSavedLine.Saved("[DedicatedServer] operator save: world saved to 'other.sav' (1 ms)", "taomtest"));
        // The engine lines come before the .sav is written, or belong to the join TransferSave.
        Assert.Null(WorldSavedLine.SavedFile("[DedicatedServer Engine] ------Saving with new context. Save name: taomtest------"));
        Assert.Null(WorldSavedLine.SavedFile("[DedicatedServer Engine] ------Successfully saved------"));
        Assert.Null(WorldSavedLine.SavedFile("[DedicatedServer] > save"));
    }

    [Fact]
    public void Stop_waits_for_the_world_saved_line_and_not_the_json()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "BannerlordCoopLauncher.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = SourceFiles.Method(SourceFiles.Window(root!.FullName, "TaomCoopHost"), "private bool WaitForWorldSave()");
        Assert.Contains("WorldSavedLine.SameSave(_lastWorldSavedFile, name)", source);
        Assert.Contains("WorldSavedLine.TransactionMarker(directory, name)", source);
        Assert.DoesNotContain("GetLastWriteTimeUtc(json)", source);
    }

    [Fact]
    public void A_save_written_on_taom_plus_matches_taom_plus()
    {
        var book = new TaomSetupBook();
        book.Ensure();
        var plus = book.Find(TaomSetupBook.PlusName)!;
        var written = "TAOM.Dependencies;Native;SandBoxCore;Sandbox;StoryMode;CustomBattle;TAOM;TAOM_Map;LOTRLOME_Armory;Coop;TAOM.CoopCompat;BetterExceptionWindow;Bannerlord.CCsBanners;XorberaxLegacy;CoopModPatch;ImprovedGarrisons;DismembermentPlus;BannerFix;RaiseYourBanner;RaiseYourTorch;ImmersiveBattlefields;RTSCamera;RTSCamera.CommandSystem;DedicatedServer.Windows".Split(';');
        Assert.Null(CoopJoinRules.SaveProblem(written, TaomLoadOrder.ServerOrder(plus)));
    }
}

public class SteamRunningTests
{
    [Fact]
    public void Steam_counts_as_running_when_steam_exe_lives_even_if_the_registry_pid_is_0_or_stale()
    {
        Assert.Equal(30152, BannerlordCoopLauncher.Core.Steam.SteamRegistry.LiveSteamProcess(0, [30152]));
        Assert.Equal(30152, BannerlordCoopLauncher.Core.Steam.SteamRegistry.LiveSteamProcess(1234, [30152]));
        Assert.Equal(1234, BannerlordCoopLauncher.Core.Steam.SteamRegistry.LiveSteamProcess(1234, [30152, 1234]));
        // A pid left in the registry after Steam closed is not Steam running.
        Assert.Equal(0, BannerlordCoopLauncher.Core.Steam.SteamRegistry.LiveSteamProcess(1234, []));
    }
}
