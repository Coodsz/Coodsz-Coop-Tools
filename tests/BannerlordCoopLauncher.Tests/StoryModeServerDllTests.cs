using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// Coop crash report #73 (CR-N8TBU): Coop 0.1.7's server has no StoryMode.dll in its engine bin, and Improved Garrisons
// stopped a new campaign with FileNotFoundException. Start copies the game's own v1.4.8 StoryMode.dll there.
public sealed class StoryModeServerDllTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-storymode-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Server(bool withBin = true)
    {
        var server = Path.Combine(_root, "server");
        Directory.CreateDirectory(withBin ? Path.Combine(server, "engine", "bin", "Win64_Shipping_Server") : server);
        return server;
    }

    private string GameModules(byte[] storyMode)
    {
        var modules = Path.Combine(_root, "Modules");
        var bin = Path.Combine(modules, "StoryMode", "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "StoryMode.dll"), storyMode);
        return modules;
    }

    private static string Target(string server) => Path.Combine(server, "engine", "bin", "Win64_Shipping_Server", "StoryMode.dll");

    [Fact]
    public void A_file_that_is_not_the_v148_StoryMode_dll_is_never_copied()
    {
        var server = Server();
        Assert.Null(CoopServerBin.EnsureStoryMode(server, GameModules(new byte[CoopServerBin.StoryModeLength])));
        Assert.False(File.Exists(Target(server)));
    }

    [Fact]
    public void Nothing_happens_without_a_game_an_engine_bin_or_when_the_server_has_one()
    {
        Assert.Null(CoopServerBin.EnsureStoryMode(Server(), null));
        Assert.Null(CoopServerBin.EnsureStoryMode(Server(withBin: false), GameModules([1, 2, 3])));

        var server = Server();
        File.WriteAllText(Target(server), "the server's own");
        Assert.Null(CoopServerBin.EnsureStoryMode(server, GameModules([1, 2, 3])));
        Assert.Equal("the server's own", File.ReadAllText(Target(server)));
    }

    [Fact]
    public void The_game_copy_goes_into_the_engine_bin_when_it_is_the_pinned_file()
    {
        var game = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common",
            "Mount & Blade II Bannerlord", "Modules");
        var real = Path.Combine(game, "StoryMode", "bin", "Win64_Shipping_Client", "StoryMode.dll");
        if (!File.Exists(real) || new FileInfo(real).Length != CoopServerBin.StoryModeLength)
            return; // CI has no game; the pin and the copy are still checked on a PC with Bannerlord v1.4.8.

        var server = Server();
        Assert.Equal(CoopServerBin.StoryModeCopied, CoopServerBin.EnsureStoryMode(server, game));
        Assert.Equal(CoopServerBin.StoryModeSha256, CoopServerBin.Sha256(Target(server)));
        Assert.Null(CoopServerBin.EnsureStoryMode(server, game));
    }

    [Fact]
    public void Start_runs_it_with_the_other_server_file_repairs()
    {
        var bin = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "CoopServerBin.cs"));
        Assert.Contains("var storyMode = EnsureStoryMode(serverRoot, gameModules);", SourceFiles.Method(bin, "public static string? Ensure(string serverRoot, string? gameModules)"));
        Assert.Contains("public static string? Ensure(string serverRoot) => Ensure(serverRoot, GameModulesOrNull());", bin);
    }
}
