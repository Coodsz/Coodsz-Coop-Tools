using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Tests;

// The check before Play and Connect in every client launcher: a Common.dll or 0Harmony.dll another mod
// left in the game's bin folder gets a warning in the log. A clean install gets none.
public class GameBinLeftoversTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-binleft-" + Guid.NewGuid().ToString("N"));
    private string Bin => Path.Combine(_root, "bin", "Win64_Shipping_Client");

    public GameBinLeftoversTests() => Directory.CreateDirectory(Bin);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_clean_install_gets_no_warning()
    {
        // Files the Steam depot does put there, two of them with Common or Harmony in the name.
        foreach (var name in new[] { "Bannerlord.exe", "TaleWorlds.Library.dll", "PhysXCommon_64.dll", "NoHarmony.txt", "Coop_client.log" })
            File.WriteAllText(Path.Combine(Bin, name), "");
        Assert.Empty(GameBinLeftovers.Find(_root));
        Assert.Empty(GameBinLeftovers.Notes(_root));
        Assert.Empty(GameBinLeftovers.Notes(null));
        Assert.Empty(GameBinLeftovers.Notes(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void A_game_path_Windows_can_not_read_never_throws_before_Play()
    {
        Assert.Empty(GameBinLeftovers.Notes("C:\\Games\\Bad\0Name"));
        Assert.Empty(GameBinLeftovers.Notes("C:\\Games\\" + new string('a', 40_000)));
        Assert.Empty(GameBinLeftovers.Notes("   "));
    }

    [Fact]
    public void Common_dll_and_0Harmony_dll_each_get_a_warning_with_the_path_on_its_own_line()
    {
        File.WriteAllText(Path.Combine(Bin, "Common.dll"), "");
        File.WriteAllText(Path.Combine(Bin, "0harmony.dll"), "");

        Assert.Equal(new[] { "Common.dll", "0Harmony.dll" }, GameBinLeftovers.Find(_root));
        var notes = GameBinLeftovers.Notes(_root);
        Assert.Equal(2, notes.Count);
        Assert.Equal(
            "The game's bin folder has a Common.dll that Bannerlord doesn't come with.\n"
            + "Close the game and move this file out to your Desktop.\n"
            + Path.Combine(Bin, "Common.dll") + "\n"
            + "Another mod left it there, and the game loads it before the copy Coop needs, so Coop can fail to load. Start the game again once it's moved.",
            notes[0]);
        Assert.Contains("\n" + Path.Combine(Bin, "0Harmony.dll") + "\n", notes[1]);
    }

    [Fact]
    public void Every_client_launcher_warns_before_the_game_starts()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo != null && !File.Exists(Path.Combine(repo.FullName, "BannerlordCoopLauncher.sln")))
            repo = repo.Parent;
        foreach (var file in new[] { "BannerlordCoopLauncher/MainWindow.Launch.cs", "AD1259Launcher/MainWindow.xaml.cs", "TaomCoopLauncher/MainWindow.xaml.cs", "BurningEmpiresLauncher/MainWindow.xaml.cs" })
        {
            var text = File.ReadAllText(Path.Combine(repo!.FullName, "src", file));
            var check = text.IndexOf("GameBinLeftovers.Notes(_gameRoot)", StringComparison.Ordinal);
            Assert.True(check > 0, file);
            // Next to the non Steam copy note, before the game process starts.
            Assert.True(text.IndexOf("NonSteamCopy.Note(_gameRoot)", StringComparison.Ordinal) < check, file);
            Assert.True(text.IndexOf("Process.Start", check, StringComparison.Ordinal) > check || text.IndexOf("StartWithPrefabsFix(", check, StringComparison.Ordinal) > check, file);
        }
    }
}
